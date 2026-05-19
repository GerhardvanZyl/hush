import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';

/**
 * Locates the bundled .NET sidecar binary for the current platform.
 *
 * Layout shipped in the .vsix:
 *   out/sidecar/<rid>/Hush.VSCode.Sidecar[.exe]
 *
 * Where <rid> is one of: win-x64, win-arm64, linux-x64, linux-arm64, osx-x64, osx-arm64.
 * In dev mode (F5) we also probe the sidecar project's bin/Debug/net8.0/<rid>/ directory
 * so we don't have to copy the binary into the extension out dir every build.
 */
export function resolveSidecarPath(extensionRoot: string, overridePath?: string): string {
  if (overridePath && overridePath.trim().length > 0) {
    if (!fs.existsSync(overridePath)) {
      throw new Error(`Configured hush.sidecarPath does not exist: ${overridePath}`);
    }
    return overridePath;
  }

  const rid = currentRid();
  const exeName = process.platform === 'win32'
    ? 'Hush.VSCode.Sidecar.exe'
    : 'Hush.VSCode.Sidecar';

  const sidecarBin = path.join(extensionRoot, '..', 'Hush.VSCode.Sidecar', 'bin');
  const candidates = [
    path.join(extensionRoot, 'out', 'sidecar', rid, exeName),
    // Dev fallback: `dotnet publish` output (has the self-contained exe).
    path.join(sidecarBin, 'Debug', 'net8.0', rid, 'publish', exeName),
    path.join(sidecarBin, 'Release', 'net8.0', rid, 'publish', exeName),
    // `dotnet build` only emits the framework-dependent dll, but try the exe anyway.
    path.join(sidecarBin, 'Debug', 'net8.0', rid, exeName),
    path.join(sidecarBin, 'Release', 'net8.0', rid, exeName),
  ];

  for (const candidate of candidates) {
    if (fs.existsSync(candidate)) return candidate;
  }

  throw new Error(
    `Sidecar binary not found for ${rid}. Looked in:\n  ` + candidates.join('\n  '),
  );
}

function currentRid(): string {
  const arch = process.arch === 'arm64' ? 'arm64' : process.arch === 'x64' ? 'x64' : process.arch;
  switch (process.platform) {
    case 'win32': return `win-${arch}`;
    case 'darwin': return `osx-${arch}`;
    case 'linux': return `linux-${arch}`;
    default: throw new Error(`Unsupported platform: ${process.platform} (${os.platform()})`);
  }
}
