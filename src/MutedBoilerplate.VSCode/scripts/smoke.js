#!/usr/bin/env node
/**
 * Headless smoke test: spawn the sidecar and exercise the JSON-RPC contract end-to-end
 * against a fixture C# file. No VS Code involvement. Use with:
 *
 *   node scripts/smoke.js <path-to-sidecar.exe> <path-to-fixture.cs>
 *
 * Exit code 0 = at least one span returned for telemetry/logging.
 */
const { spawn } = require('child_process');
const fs = require('fs');
const path = require('path');
const { createMessageConnection, ParameterStructures, StreamMessageReader, StreamMessageWriter } = require('vscode-jsonrpc/node');

const [sidecarPath, fixturePath] = process.argv.slice(2);
if (!sidecarPath || !fixturePath) {
  console.error('usage: node scripts/smoke.js <sidecar-exe> <fixture-cs>');
  process.exit(2);
}
if (!fs.existsSync(sidecarPath)) {
  console.error(`sidecar not found: ${sidecarPath}`);
  process.exit(2);
}
if (!fs.existsSync(fixturePath)) {
  console.error(`fixture not found: ${fixturePath}`);
  process.exit(2);
}

const content = fs.readFileSync(fixturePath, 'utf8');
const child = spawn(sidecarPath, [], { stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
child.stderr.on('data', (d) => process.stderr.write(`[sidecar-stderr] ${d}`));
child.on('exit', (code) => console.log(`[smoke] sidecar exit code=${code}`));

const reader = new StreamMessageReader(child.stdout);
const writer = new StreamMessageWriter(child.stdin);
const conn = createMessageConnection(reader, writer);
conn.listen();

(async () => {
  try {
    const init = await conn.sendRequest('initialize', ParameterStructures.byPosition, {
      rulesPath: null,
      workspaceFolders: [],
      initialState: null,
      exclusionsEnabled: true,
    });
    console.log(`[smoke] initialize: ${init.categories.length} categories, stateVer=${init.stateVersion} ruleVer=${init.ruleSetVersion}`);

    const uri = 'file://' + path.resolve(fixturePath).replace(/\\/g, '/');
    await conn.sendRequest('didOpen', ParameterStructures.byPosition, {
      uri,
      languageId: 'csharp',
      version: 1,
      content,
    });

    const resp = await conn.sendRequest('getSpans', ParameterStructures.byPosition, { uri, version: 1 });
    console.log(`[smoke] getSpans: ${resp.spans.length} spans for ${path.basename(fixturePath)}`);
    const byCategory = new Map();
    for (const s of resp.spans) {
      byCategory.set(s.categoryKey, (byCategory.get(s.categoryKey) ?? 0) + 1);
    }
    for (const [k, v] of byCategory) console.log(`  ${k}: ${v}`);

    const off = await conn.sendRequest('setMuteState', ParameterStructures.byPosition, { categoryKey: 'telemetry', enabled: false });
    const resp2 = await conn.sendRequest('getSpans', ParameterStructures.byPosition, { uri, version: 1 });
    console.log(`[smoke] after disable telemetry: stateVer=${off.stateVersion} spans=${resp2.spans.length}`);

    if (resp.spans.length === 0) {
      console.error('[smoke] FAIL: no spans returned for the fixture');
      process.exit(1);
    }
    if (resp2.spans.length >= resp.spans.length) {
      console.error('[smoke] FAIL: disabling telemetry did not reduce span count');
      process.exit(1);
    }

    console.log('[smoke] OK');
  } catch (e) {
    console.error('[smoke] error:', e);
    process.exit(1);
  } finally {
    conn.dispose();
    try { child.kill(); } catch { /* ignore */ }
  }
})();
