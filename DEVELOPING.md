# DEVELOPING

Internal notes for working on this extension. Pairs with `README.md` (which is user-facing). Read this before debugging or tuning performance — most of the non-obvious things we've already learned the hard way are recorded here.

## Architecture at a glance

```
ITextBuffer (VS editor)
    │
    ▼
BufferDocumentAdapter ──► MatchContext { SourceText, SyntaxTree?, SemanticModel? }
                                │
                                ▼
                     MuteSpanProvider.GetSpans(ctx, state, ruleSet)
                                │
        ┌───────────────────────┼───────────────────────┐
        ▼                       ▼                       ▼
   IRuleMatcher[]         ExclusionEvaluator       MuteState filter
   (per-rule.Kind)        (global + rule-local)    (category on/off)
        │                       │
        └───────────────────────┴──► IEnumerable<MuteSpan>
                                            │
                ┌───────────────────────────┴───────────────┐
                ▼                                           ▼
        HushClassifier                          HushOutliningTagger
        (ClassificationSpan)                     (IsDefaultCollapsed)
```

The seam between Core and VS is `IMuteSpanProvider`. Core has zero VS dependencies — that's deliberate so a future VS Code host can reuse it.

## Hot paths (look here first when profiling)

| Location | Frequency | Notes |
|---|---|---|
| `HushClassifier.GetClassificationSpans` | Per visible region, per buffer change, per scroll. Many calls per second. | Currently re-runs the **entire** rule pipeline on every call and filters at the end. See "Performance ideas". |
| `BufferDocumentAdapter.Build` | Once per classifier/tagger call. | Uses `TryGetSyntaxTree` (sync, returns false if not cached). Falls back to standalone `CSharpSyntaxTree.ParseText` — that's an extra parse on every call when not in a workspace. |
| `MuteSpanProvider.GetSpans` | Per `Build` call. | Walks the syntax tree once per rule via `DescendantNodes().OfType<...>()`. With N rules and M nodes that's roughly O(N·M). Materializes all candidates into a `List` before exclusions run. |
| `RegexMatcher.Match` | Per regex rule, per call. | **Compiles a fresh `Regex` every invocation** — `new Regex(pattern, ...)` is not cached. Easy win to cache by pattern string. |
| `ExclusionEvaluator.MaterializeExclusions` | Per `GetSpans` call. | Re-runs every exclusion matcher across the whole tree even if no candidate would intersect. |

## Threading model

- **`HushClassifier`** and **`HushOutliningTagger`** are invoked on the UI thread. Anything blocking inside them stalls the editor.
- **Never** call `.Result` / `.Wait()` on Roslyn `*Async` methods from these — that's a documented deadlock vector (vs-threading VSTHRD002). `BufferDocumentAdapter` uses `Document.TryGetSyntaxTree` / `TryGetSemanticModel` (sync, cache-only) precisely for this reason.
- `MuteState.Changed` fires on whichever thread invoked `Toggle()`. Both consumers (`HushClassifier`, `HushOutliningTagger`) raise their own change event over the **entire buffer** in response — that triggers a full reclassify on every toggle. Cheap correctness, expensive at scale.

## The match heuristic (and why some things don't mute)

`RoslynCallMatcher` works with or without a `SemanticModel`:

- **With semantic model**: resolves `IMethodSymbol.ContainingType.Name` and the declared receiver type — accurate.
- **Without semantic model**: falls back to a name heuristic on the leftmost identifier of the call's member-access chain:
  - exact match (e.g. `Console`)
  - strip leading `_`, PascalCase (e.g. `_logger` → `Logger`)
  - prefix `I` (e.g. `_logger` → `ILogger`, matches `ILogger*` glob)

This means **short variable names won't heuristically match** (`_a` → `A` → `IA`). Real fields like `_logger`, `_telemetryClient`, `activity`, `meter` all work. When debugging a "rule looks right but nothing matches", first check whether you're hitting the heuristic path (`SemanticModel == null`) and whether the receiver name is recognizable.

Null-conditional access (`activity?.SetTag(...)`) lives on `MemberBindingExpressionSyntax`, not `MemberAccessExpressionSyntax`. The matcher walks up to the enclosing `ConditionalAccessExpressionSyntax` to find the receiver. Don't break this path when refactoring.

## VS extension build & deploy gotchas (VS 18 / 2026)

These tripped us up; record any new ones here:

- **`Microsoft.VSSDK.BuildTools` 18.x targets .NET 10** for the build tasks themselves and won't load under VS's .NET Framework MSBuild — `System.Collections.Immutable, Version=10.0.0.3` resolution failure. Stay on the 17.14.x line until VS exposes the .NET 10 task host for VSIX builds.
- The package's auto-imported `build/Microsoft.VSSDK.BuildTools.targets` **only sets env vars**. The real targets (`CreateVsixContainer`, `GeneratePkgDef`, `VSCTCompile`, `DeployVsixExtensionFiles`) live in `tools/VSSDK/Microsoft.VsSDK.targets` and must be **explicitly imported**. `$(VSToolsPath)` is not reliably set under SDK-style projects in VS 18 — point at `$(NuGetPackageRoot)microsoft.vssdk.buildtools\<version>\tools\VSSDK\Microsoft.VsSDK.targets` instead.
- That explicit Import **must run AFTER `Sdk.targets`**, otherwise `Microsoft.NET.Sdk.targets` overwrites `$(PrepareForRunDependsOn)` and `CreateVsixContainer` never gets wired into the build. Hence the explicit-import csproj form (`<Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk"/>` ... `<Import Project="Sdk.targets" Sdk="Microsoft.NET.Sdk"/>` ... explicit VsSDK Import last) instead of `<Project Sdk="...">`.
- VsSDK targets define `$(ResourceManifest)` and friends as `$(IntermediateOutputPath)\resources.json` at import time, but the SDK doesn't populate `$(IntermediateOutputPath)` until later. Result: files land at the **drive root** and the build fails with `VSSDK1207`. We pin all of them explicitly via the `_MutedVsixIntermediate` PropertyGroup. Don't remove it.
- `<UseCodebase>true</UseCodebase>` is required so the generated `.pkgdef` includes a `CodeBase` entry pointing at the deployed extension folder. Without it VS resolves the package assembly against `Common7\IDE\` and fails with `Could not load file or assembly 'Hush.VS, ...'`.
- F5 needs explicit `<StartAction>Program</StartAction>` + `<StartProgram>$(DevEnvDir)devenv.exe</StartProgram>` + `<StartArguments>/rootsuffix Exp</StartArguments>` — SDK-style net472 projects aren't recognized as launchable VSIX projects without these.

## Where things land at runtime

- **VSIX output**: `src/Hush.VS/bin/Debug/net472/Hush.VS.vsix`
- **Experimental-hive deployment** (overwritten by F5): `%LOCALAPPDATA%\Microsoft\VisualStudio\18.0_*Exp\Extensions\Hush\Hush\<version>\` — installer derives this from the vsixmanifest `Publisher` + `DisplayName`, so renaming the product changes the path. Pre-Hush installs live under the old `MutedBoilerplate\Muted Boilerplate\` folder and need to be uninstalled manually if both versions show up in the Extensions Manager.
- **Activity log** (write-only into the Exp instance via `devenv /log /rootsuffix Exp`): `%APPDATA%\Microsoft\VisualStudio\18.0_*Exp\ActivityLog.xml`
- **VsSDK targets** (read-only, useful when tracing target chains): `%USERPROFILE%\.nuget\packages\microsoft.vssdk.buildtools\<version>\tools\VSSDK\Microsoft.VsSDK.targets`

## Debugging entry points

- **A specific call isn't muting**: breakpoint in `RoslynCallMatcher.TryMatch` (Core). Step through `TryGetMethodAndReceiver` and the heuristic — usually `methodName` or `receiverName` is unexpected. Add a unit test in `tests/Hush.Core.Tests/` reproducing it; the Core layer is where to fix matching, not the VS layer.
- **Mute toggles work but the editor doesn't redraw**: check that `MuteStateService.Changed` fires (it does on `Toggle*`/`Set*`/`Set+ExclusionsEnabled`). The classifier and tagger both subscribe and re-raise their own change events over the whole buffer.
- **Hotkey doesn't fire**: VSCT command IDs in `HushCommands.vsct` and `Constants.cs` must match. The chord (`Ctrl+Alt+M`, `<key>`) is in the `<KeyBindings>` section. Editor context is `guidVSStd97` — it only fires when an editor has focus.
- **Package fails to load in the Exp hive**: read `ActivityLog.xml` for the package GUID (`7B4B1D6C-1F0A-4D4F-9E1D-1A6B5B0A2E10`). Most failures are assembly resolution — confirm `Hush.VS.dll` and `Hush.Core.dll` actually landed in the deployment folder above, and that the `.pkgdef` has a `CodeBase` line.
- **Build target tracing**: bump MSBuild verbosity (Tools → Options → Projects and Solutions → Build and Run → Detailed) and grep Output for `CreateVsixContainer`, `SetVsSDKEnvironmentVariables`, `MergeCtoResource`. Their absence usually means the explicit Import or the property-pinning got reverted.

## Performance ideas (not yet done)

In rough order of impact:

1. **Cache the `MatchContext` per buffer snapshot.** Today every `GetClassificationSpans` call rebuilds the context (and may reparse). Key by `ITextSnapshot` and invalidate on `TextBuffer.Changed`.
2. **Cache `MuteSpan` results per snapshot + state hash.** The classifier and the outlining tagger both call `GetSpans` independently for the same buffer; they should share a result.
3. **Filter spans against the requested `SnapshotSpan` *before* materializing.** Currently `GetClassificationSpans` runs the full provider, then filters at the end. For a 5000-line file that's wasteful when the editor only asked about 50 visible lines.
4. **Cache compiled regexes in `RegexMatcher`** (key by pattern string). Same for `IdentifierExclusionMatcher` etc.
5. **Skip exclusion materialization when `RuleSet.Exclusions` is empty and no rule has `Excludes`.** Easy short-circuit.
6. **Switch `ClassificationChanged` to dirty-region mode.** Today every `MuteState` toggle invalidates the whole buffer. We can be more surgical for category-toggles by tracking which spans belong to which category.
7. **Long term**: consider an out-of-process Roslyn host (the same seam the VS Code reuse path needs) so classification doesn't compete with the UI thread at all.

## What lives where

```
src/Hush.Core/         — netstandard2.0, no VS deps
  Model/                            — POCOs (MuteCategory, MuteSpan, MuteState, MuteStyle)
  Rules/                            — RuleSet JSON model + DefaultRules.json (embedded)
  Matching/                         — IRuleMatcher impls + MuteSpanProvider
src/Hush.VS/           — net472 VSIX, depends on Core
  Classification/                   — MEF classifier + format definitions
  Outlining/                        — ITagger<IOutliningRegionTag>
  Options/                          — DialogPage + MuteStateService singleton + UserSlotMap
  Integration/                      — BufferDocumentAdapter (the only ITextBuffer-aware code)
  HushPackage.cs        — AsyncPackage, command/options registration
  HushCommands.vsct     — menus + keybindings
tests/Hush.Core.Tests/ — xUnit, runs on net8.0 against the netstandard2.0 Core
  Fixtures/                         — sample .cs files for manual F5 testing (excluded from compile)
```
