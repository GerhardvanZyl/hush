# Hush

A Visual Studio + VS Code extension that visually mutes boilerplate code (telemetry calls, logging, parameter guards, etc.) so it fades into the background while the meaningful logic stays prominent.

## Goals

- Mute boilerplate via styling (color, font size, font type, opacity, italic) and optional auto-collapsed regions.
- Per-category configuration with sensible defaults shipped in `DefaultRules.json`.
- Hotkeys (`Ctrl+Alt+M, <key>`) to toggle each category — including 8 user-defined categories.
- Architecture that lets the rule + matcher core be reused by a future VS Code extension.

## Solution layout

- `src/Hush.Core/` — `netstandard2.0` rule model, matchers (Roslyn call, signature, regex, identifier), `IMuteSpanProvider`, exclusion evaluator. No VS dependencies.
- `src/Hush.VS/` — `net472` VSIX. MEF-exports the classifier + outlining tagger, hosts the options page and command set, bridges `ITextBuffer` to a Roslyn `Document` via the live workspace.
- `tests/Hush.Core.Tests/` — xUnit tests for matchers, exclusion evaluator, rule-set round-trip, end-to-end span provider.

## Default categories

| Category  | Default style                                    | Auto-collapse |
|-----------|--------------------------------------------------|---------------|
| Telemetry | `#7A7A7A`, opacity 0.55, italic, 90% font size   | off           |
| Logging   | `#888888`, opacity 0.60, 90% font size           | off           |
| User 1–8  | `#8A8A8A`, opacity 0.60, 100% font size          | off           |

Override foreground/typeface in **Tools → Options → Fonts and Colors** under the `Muted *` entries. Toggle auto-collapse and pick a custom rules file in **Tools → Options → Hush**.

## Hotkeys

Chord `Ctrl+Alt+M` followed by:

| Chord       | Action                                  |
|-------------|-----------------------------------------|
| `T`         | toggle Telemetry                        |
| `L`         | toggle Logging                          |
| `S`         | toggle Signature                        |
| `A`         | toggle All                              |
| `X`         | toggle Exclusions                       |
| `1` … `8`   | toggle the user-defined slot 1–8        |

## User-defined categories

Add a category to your rules JSON and reference it from a rule:

```json
{
  "categories": [
    { "key": "validation", "displayName": "Validation",
      "style": { "foreground": "#445566", "italic": true, "opacity": 0.55 } }
  ],
  "rules": [
    { "name": "guards", "category": "validation", "kind": "roslynCall",
      "pattern": { "receiverTypeGlob": "Guard", "methodNameGlob": "Against*" },
      "scope": "wholeStatement" }
  ],
  "exclusions": [
    { "name": "keep-audit", "kind": "identifier",
      "pattern": { "identifier": "_auditLogger" }, "appliesTo": "*" }
  ]
}
```

The first user category encountered binds to user slot 1 (and `Ctrl+Alt+M, 1`); the binding is persisted so the same category keeps the same slot across sessions.

## Build

Core + tests build with the `dotnet` CLI:

```sh
dotnet test tests/Hush.Core.Tests
```

The VSIX project (`src/Hush.VS`) builds the assembly under either CLI MSBuild or VS, but **producing the `.vsix` container reliably requires opening `vs-ext-hush.sln` in Visual Studio 2022/2026 with the *Visual Studio extension development* workload installed and building from there**. Pressing F5 launches the experimental hive with the extension deployed.

## Reuse seam for a future VS Code extension

`Hush.Core` deliberately depends only on `Microsoft.CodeAnalysis.CSharp` and `System.Text.Json`. The same `IMuteSpanProvider`, `RuleSet`, and `MuteState` types can be hosted in a `dotnet`-based JSON-RPC sidecar process that a VS Code extension speaks to over stdio, exposing matches as `setDecorations` calls and folding ranges via a `FoldingRangeProvider`.
