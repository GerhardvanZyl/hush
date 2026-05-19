# Hush — VS Code

Visually quiet the boilerplate (telemetry, logging, signatures, parameter guards) so the meaningful code stays loud. Companion to the Visual Studio extension; both share the same rules engine (`Hush.Core`, kept under its original name).

## How it works

The extension splits work by language:

- **C# (`.cs`)** → bundled .NET sidecar (`Hush.VSCode.Sidecar`) hosts the Roslyn-based matching engine. Extension talks to it via JSON-RPC over stdio.
- **TS/TSX/JS/JSX** → in-process `TsCallMatcher` using the TypeScript Compiler API (the `typescript` npm package). No sidecar round-trip. Rules of kind `tsCall` are shipped from the sidecar's loaded `RuleSet` at initialize time so the sidecar stays the single source of truth.

```
VS Code editor ─► Extension host (TS) ─┬─ csharp ─JSON-RPC─► .NET sidecar ─► Hush.Core
                                       └─ ts/tsx/js/jsx ─► TsCallMatcher (in-process)
```

Decorations + folding + auto-collapse work uniformly across both paths.

## Build (development)

From this directory:

```sh
npm install
npm run build
```

Build the sidecar (from the repo root):

```sh
dotnet build src/Hush.VSCode.Sidecar -c Debug -r win-x64 --self-contained
```

Then press F5 to launch a development host with the extension loaded.

## Package per-platform `.vsix`

From the repo root:

```powershell
./build/build-vscode.ps1 -Rid win-x64
./build/build-vscode.ps1 -Rid linux-x64
./build/build-vscode.ps1 -Rid osx-arm64
# etc.
```

Each invocation publishes the sidecar for that RID, then packs a platform-specific `.vsix`. Install with:

```sh
code --install-extension hush-vscode-<version>-<rid>.vsix
```

## Keybindings

`Ctrl+Alt+M` chord (on macOS: `Cmd+Alt+M`):

| Chord | Action |
|---|---|
| `T` | Toggle Telemetry |
| `L` | Toggle Logging |
| `S` | Toggle Signature |
| `G` | Toggle Guards |
| `A` | Toggle All |
| `X` | Toggle Exclusions |
| `1`…`8` | Toggle User Slot 1…8 |

## Settings

See `hush.*` in the Settings UI:

- `rulesPath` — path to a custom rules JSON file
- `categories.<key>.enabled` — per-category initial state
- `exclusionsEnabled` — toggle exclusion rules globally
- `autoCollapse` — fold AutoCollapse regions on document open
- `debounceMs` — debounce window for decoration refresh
- `sidecarPath` — override the bundled sidecar binary

## Theming

Each built-in category registers a `hush.*.foreground` color ID with sensible defaults for `dark`, `light`, and high-contrast themes. Override in your theme's `colors` block or in your user settings under `workbench.colorCustomizations`.
