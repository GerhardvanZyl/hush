const esbuild = require('esbuild');

const production = process.argv.includes('--production');
const watch = process.argv.includes('--watch');

async function main() {
  const ctx = await esbuild.context({
    entryPoints: ['src/extension.ts'],
    bundle: true,
    format: 'cjs',
    minify: production,
    sourcemap: !production,
    sourcesContent: false,
    platform: 'node',
    outfile: 'out/extension.js',
    external: ['vscode'],
    logLevel: 'silent',
    plugins: [
      {
        name: 'log-errors',
        setup(build) {
          build.onEnd((result) => {
            result.errors.forEach(({ text, location }) => {
              console.error(`✘ ${text}`);
              if (location) console.error(`    ${location.file}:${location.line}:${location.column}`);
            });
            console.log(result.errors.length === 0 ? '✓ build ok' : '✘ build failed');
          });
        },
      },
    ],
  });

  if (watch) {
    await ctx.watch();
  } else {
    await ctx.rebuild();
    await ctx.dispose();
  }
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
