#!/usr/bin/env tsx
/**
 * Smoke test for the TS Compiler API matcher. Spawns the sidecar, pulls the
 * actual `tsCallRules` from initialize (so we test the live shipping path),
 * then runs the production TsCallMatcher against ts/tsx/js/jsx fixtures and
 * asserts each rule lands where expected.
 *
 *   npx tsx scripts/smoke-ts.ts <path-to-sidecar.exe>
 */
import { spawn } from 'child_process';
import * as fs from 'fs';
import * as path from 'path';
import {
  createMessageConnection,
  ParameterStructures,
  StreamMessageReader,
  StreamMessageWriter,
} from 'vscode-jsonrpc/node';
import { TsCallMatcher } from '../src/matching/TsCallMatcher';
import { InitializeResponse, MuteSpanDto, TsCallRuleDto } from '../src/sidecar/protocol';

const sidecarPath = process.argv[2];
if (!sidecarPath || !fs.existsSync(sidecarPath)) {
  console.error('usage: tsx scripts/smoke-ts.ts <sidecar-exe>');
  process.exit(2);
}

interface Case {
  languageId: 'typescript' | 'typescriptreact' | 'javascript' | 'javascriptreact';
  content: string;
  // Substrings expected to appear inside at least one returned span.
  expect: string[];
  // Optional: substrings that should NOT be in any span.
  reject?: string[];
}

const cases: Case[] = [
  {
    languageId: 'typescript',
    content: `function compute(x: number, y: number) {
  console.log('starting compute', x, y);
  const r = x + y;
  console.warn(\`magnitude: \${Math.abs(r)}\`);
  if (r < 0) console.error('negative!', { x, y });
  console.info('done');
  console.debug({ extra: 1 });
  console.trace();
  return r;
}
function notLogged() {
  return 42;
}`,
    expect: ["console.log('starting compute'", 'console.warn(', 'console.error(', 'console.info(', 'console.debug(', 'console.trace('],
    reject: ['return 42'],
  },
  {
    languageId: 'typescriptreact',
    content: `import { useEffect } from 'react';
export function Greeting({ name }: { name: string }) {
  useEffect(() => {
    console.log('mounted', name);
  }, [name]);
  return <div onClick={() => console.error('clicked', name)}>Hello {name}</div>;
}`,
    expect: ["console.log('mounted', name);", "console.error('clicked', name)"],
    // Scope must NOT extend to the whole return statement just because the
    // call is nested inside JSX.
    reject: ['return <div', 'Hello {name}', '</div>'],
  },
  {
    languageId: 'javascript',
    content: `const things = [1, 2, 3];
things.forEach((t) => console.log('item', t));
function pure(n) { return n * 2; }
pure(5);
console.log('top-level');`,
    expect: ["console.log('item', t)", "console.log('top-level');"],
    reject: ['pure(5);', 'pure(n)', 'forEach', 'things'],
  },
  {
    languageId: 'javascriptreact',
    content: `function App() {
  return <button onClick={() => { console.warn('click'); window.localStorage.setItem('x', '1'); }}>tap</button>;
}`,
    expect: ["console.warn('click');"],
    reject: ['localStorage.setItem'],
  },
];

const child = spawn(sidecarPath, [], { stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
child.stderr.on('data', (d) => process.stderr.write(`[sidecar-stderr] ${d}`));

const reader = new StreamMessageReader(child.stdout!);
const writer = new StreamMessageWriter(child.stdin!);
const conn = createMessageConnection(reader, writer);
conn.listen();

(async () => {
  let failures = 0;
  try {
    const init = await conn.sendRequest<InitializeResponse>('initialize', ParameterStructures.byPosition, {
      rulesPath: null,
      workspaceFolders: [],
      initialState: null,
      exclusionsEnabled: true,
    });
    const tsRules: TsCallRuleDto[] = init.tsCallRules ?? [];
    console.log(`[smoke-ts] sidecar shipped ${tsRules.length} tsCall rules: ${tsRules.map((r) => r.name).join(', ')}`);
    if (tsRules.length === 0) {
      console.error('[smoke-ts] FAIL: no tsCall rules — DefaultRules.json missing ts-console?');
      process.exit(1);
    }

    const matcher = new TsCallMatcher();
    for (const c of cases) {
      const spans = matcher.match(c.content, c.languageId, tsRules);
      console.log(`\n[${c.languageId}] ${spans.length} spans`);
      for (const s of spans) {
        const text = c.content.slice(s.start, s.end).replace(/\s+/g, ' ').slice(0, 80);
        console.log(`  [${s.categoryKey}/${s.ruleName}] ${text}`);
      }
      for (const want of c.expect) {
        const ok = spans.some((s) => c.content.slice(s.start, s.end).includes(want));
        if (!ok) {
          failures++;
          console.error(`  FAIL: expected a span covering "${want}"`);
        }
      }
      for (const bad of c.reject ?? []) {
        const hit = spans.some((s) => c.content.slice(s.start, s.end).includes(bad));
        if (hit) {
          failures++;
          console.error(`  FAIL: did not expect a span over "${bad}"`);
        }
      }
    }

    if (failures > 0) {
      console.error(`\n[smoke-ts] FAIL: ${failures} assertion(s) failed`);
      process.exit(1);
    }
    console.log('\n[smoke-ts] OK');
  } catch (e) {
    console.error('[smoke-ts] error:', e);
    process.exit(1);
  } finally {
    conn.dispose();
    try { child.kill(); } catch { /* ignore */ }
  }
})();

// Discard the unused import warning — `path` is here for cross-platform path mangling if cases grow.
void path;
