# Muted Boilerplate — VS Code

Visually mute boilerplate (telemetry, logging, signatures, parameter guards) so the interesting code stays prominent. Companion to the Visual Studio extension; both share the same rules engine (`MutedBoilerplate.Core`).

## How it works

The extension runs a bundled .NET sidecar (`MutedBoilerplate.VSCode.Sidecar`) that hosts the C# matching engine. The extension talks to it via JSON-RPC over stdio, applies results as editor decorations, and surfaces auto-collapse regions through `FoldingRangeProvider` + the `editor.fold` command.

```
VS Code editor ─► Extension host (TS) ─JSON-RPC─► .NET sidecar ─► MutedBoilerplate.Core
```

## Build (development)

From this directory:

```sh
npm install
npm run build
```

Build the sidecar (from the repo root):

```sh
dotnet build src/MutedBoilerplate.VSCode.Sidecar -c Debug -r win-x64 --self-contained
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
code --install-extension muted-boilerplate-vscode-<version>-<rid>.vsix
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

See `mutedBoilerplate.*` in the Settings UI:

- `rulesPath` — path to a custom rules JSON file
- `categories.<key>.enabled` — per-category initial state
- `exclusionsEnabled` — toggle exclusion rules globally
- `autoCollapse` — fold AutoCollapse regions on document open
- `debounceMs` — debounce window for decoration refresh
- `sidecarPath` — override the bundled sidecar binary

## Theming

Each built-in category registers a `mutedBoilerplate.*.foreground` color ID with sensible defaults for `dark`, `light`, and high-contrast themes. Override in your theme's `colors` block or in your user settings under `workbench.colorCustomizations`.
