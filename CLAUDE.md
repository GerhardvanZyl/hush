# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A Visual Studio extension (VSIX) that visually mutes boilerplate (telemetry, logging, signatures, guards…) via classification styling and optional auto-collapsed outlining regions. Rules live in JSON and are matched by a Core library that has **no VS dependencies** so the matching engine can later be reused from a VS Code host.

Read `DEVELOPING.md` before debugging or tuning performance — it records the non-obvious things we've already learned the hard way (threading constraints, VSSDK build quirks, match heuristics, deployment layout).

## Build / test / run

Core + tests build with the `dotnet` CLI:

```sh
dotnet test tests/Hush.Core.Tests
# single test
dotnet test tests/Hush.Core.Tests --filter FullyQualifiedName~RoslynCallMatcherTests
```

The VSIX project (`src/Hush.VS`) builds the assembly under CLI MSBuild, but **producing a reliable `.vsix` container requires Visual Studio 2022/2026 with the *Visual Studio extension development* workload** — open `vs-ext-hush.sln` and build, or press F5 to launch the experimental hive (`/rootsuffix Exp`) with the extension deployed.

Version-stamped release packaging: `build/pack.ps1` (derives version from git commit count, preserves VSIX Identity so VSIXInstaller treats it as an upgrade).

## Architecture

Three assemblies, strict layering:

- `src/Hush.Core/` — `netstandard2.0`, depends only on `Microsoft.CodeAnalysis.CSharp` and `System.Text.Json`. Contains rule models (`RuleSet`, `MuteRule`, `ExclusionRule`), matchers (`RoslynCallMatcher`, `SignatureMatcher`, `RegexMatcher`, `IdentifierMatcher`, `GuardMatcher`), the `ExclusionEvaluator`, and the `IMuteSpanProvider` seam. Default rules are embedded as `Rules/DefaultRules.json`.
- `src/Hush.VS/` — `net472` VSIX. MEF-exports the classifier and outlining tagger, registers the `AsyncPackage`, options page, and VSCT command set. `Integration/BufferDocumentAdapter` is the only code that translates `ITextBuffer` → Roslyn `Document`/`SyntaxTree` via the live workspace.
- `tests/Hush.Core.Tests/` — xUnit on `net8.0` against the `netstandard2.0` Core. `Fixtures/**/*.cs` are deliberately excluded from compile; they're sample files copied next to the test assembly for manual F5 testing.

Data flow:

```
ITextBuffer → BufferDocumentAdapter → MatchContext
           → MuteSpanProvider.GetSpans(ctx, state, ruleSet)
             (IRuleMatcher[] + ExclusionEvaluator + MuteState filter)
           → MuteSpan[]
             → HushClassifier (ClassificationSpans)
             → HushOutliningTagger (IsDefaultCollapsed)
```

**The Core/VS seam is `IMuteSpanProvider`.** Don't leak VS types into Core — that boundary is what lets a future VS Code host reuse the matching engine.

## Performance architecture (important)

The classifier and outlining tagger are called from the UI thread many times per second. The current design keeps them cheap:

- **`Integration/SnapshotMuteCache`** memoizes `(MatchContext, MuteSpan[])` per `(ITextSnapshot, stateVersion, ruleSetVersion)`. `Get()` never blocks the UI: fresh → return; stale → return stale *and* schedule a ThreadPool recompute; first call → return empty placeholder and schedule immediate compute. Recomputes are debounced 1s (see `DebounceMilliseconds`) so steady typing coalesces into one compute when the user pauses.
- **Incremental reuse** (`SnapshotMuteCache.TryIncremental` + `MuteSpanProvider.GetSpansInRange`): when text changes, spans outside the dirty range are shifted from the previous result and only the dirty range is re-walked. Preconditions: matching state/ruleset versions, strictly newer snapshot, **exclusions disabled** (an exclusion outside the dirty range could veto a candidate inside), and dirty range < ~30% of file. Don't weaken these without thinking through the exclusion-vetoes-from-afar case.
- **Fused syntax walk** (`Matching/FusedSyntaxWalker`): one descent of the tree handles all Roslyn-based rules and Roslyn-kind exclusions, partitioned by `RuleKind`. Avoid adding per-rule `DescendantNodes().OfType<>()` calls back into `MuteSpanProvider.GetSpansCore`.
- **Thread rules**: never `.Result` / `.Wait()` on Roslyn `*Async` from classifier/tagger (VSTHRD002 deadlock). `BufferDocumentAdapter` uses `TryGetSyntaxTree` / `TryGetSemanticModel` (sync, cache-only) for this reason; it falls back to a standalone `CSharpSyntaxTree.ParseText` when not in a workspace.

## Match semantics worth remembering

`RoslynCallMatcher` has two paths:

- **With `SemanticModel`** — resolves `IMethodSymbol.ContainingType.Name` etc. Accurate.
- **Without `SemanticModel`** — heuristic on the leftmost identifier of a call's member-access chain: exact, strip leading `_` + PascalCase, and `I`-prefix (so `_logger` → `Logger` and `ILogger`). Short names like `_a` won't heuristically match. When "the rule looks right but nothing mutes", check whether you're on the heuristic path and whether the receiver name is recognizable.

Null-conditional access (`activity?.SetTag(...)`) lives on `MemberBindingExpressionSyntax`, not `MemberAccessExpressionSyntax` — the matcher walks up to `ConditionalAccessExpressionSyntax` to find the receiver. Don't break that path when refactoring matchers.

## State, toggles, versioning

`Options/MuteStateService` is the MEF-exported singleton holding the active `MuteState` + loaded `RuleSet`. It exposes `StateVersion` and `RuleSetVersion` monotonic counters; `SnapshotMuteCache` keys on these. Every `Toggle*` / `Set*` increments a version and fires `Changed`. Both the classifier and outlining tagger subscribe and re-raise a change event over the **whole buffer** (cheap correctness, expensive at scale — noted in `DEVELOPING.md` as a future surgical-invalidation opportunity).

User-slot hotkeys (`Ctrl+Alt+M, 1..8`) bind to user-defined categories via `Options/UserSlotMap`, which persists the first-seen-category → slot binding so the same category keeps its slot across sessions.

## VSIX build pitfalls (don't "fix" these without reading DEVELOPING.md)

These are already done correctly in `src/Hush.VS/Hush.VS.csproj` and removing them will silently break the build:

- The csproj uses **explicit `<Import Sdk.props/>` … `<Import Sdk.targets/>` form** (not `<Project Sdk="...">`) so the VsSDK targets Import runs *after* `Sdk.targets`. Otherwise `Microsoft.NET.Sdk.targets` overwrites `$(PrepareForRunDependsOn)` and `CreateVsixContainer` never hooks in — you get a DLL but no `.vsix`.
- `Microsoft.VSSDK.BuildTools` is **pinned to 17.14.x**; 18.x targets .NET 10 and won't load under VS's .NET Framework MSBuild.
- The VsSDK real targets are imported explicitly from `$(NuGetPackageRoot)microsoft.vssdk.buildtools\<version>\tools\VSSDK\Microsoft.VsSDK.targets` — the auto-imported `build/...targets` only sets env vars.
- The `_MutedVsixIntermediate` PropertyGroup pins `FileManifest`, `ResourceManifest`, `CtoFileManifest` etc. because VsSDK evaluates them against `$(IntermediateOutputPath)` before the SDK populates it — without this they land at the drive root and the build fails with VSSDK1207.
- `<UseCodebase>true</UseCodebase>` is required so the generated `.pkgdef` includes a `CodeBase` entry; without it VS loads the package from `Common7\IDE\` and fails with "Could not load file or assembly 'Hush.VS'".

## Where things land at runtime

- VSIX output: `src/Hush.VS/bin/Debug/net472/Hush.VS.vsix`
- Experimental hive install (F5 overwrites): `%LOCALAPPDATA%\Microsoft\VisualStudio\18.0_*Exp\Extensions\Hush\Hush\<version>\`
- Activity log (diagnose package load failures): `%APPDATA%\Microsoft\VisualStudio\18.0_*Exp\ActivityLog.xml` — package GUID is `7B4B1D6C-1F0A-4D4F-9E1D-1A6B5B0A2E10`.

## Where to fix bugs

- Matching / classification correctness → fix in `Hush.Core` and add an xUnit test under `tests/Hush.Core.Tests`. Don't push matcher logic into the VS layer.
- Editor behavior / redraw / tagger issues → `Hush.VS/Classification` or `Outlining`. Check `MuteStateService.Changed` is firing before blaming the matcher.
- Hotkey not firing → VSCT command IDs in `HushCommands.vsct` must match `Constants.cs`; editor-context-only firing is by design.
