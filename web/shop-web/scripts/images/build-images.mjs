// Converts already-downloaded Unsplash originals (<sourceDir>/<unsplashId>.jpg) into WebP files.
// Usage: node scripts/images/build-images.mjs <sourceDir>
// Products   -> public/images/products/<key>.webp (long edge 1200) and <key>-400.webp
// Categories -> public/images/categories/<slug>.webp and <slug>-400.webp
// Re-runnable: outputs are overwritten. Uses npx sharp-cli (no API key needed).
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, statSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, '..', '..');
const sourceDir = process.argv[2];
if (!sourceDir) {
  console.error('Usage: node scripts/images/build-images.mjs <sourceDir>');
  process.exit(1);
}

const sources = JSON.parse(readFileSync(join(here, 'sources.json'), 'utf8'));
const outDirs = { product: join(root, 'public/images/products'), category: join(root, 'public/images/categories') };
Object.values(outDirs).forEach((d) => mkdirSync(d, { recursive: true }));

const sharp = (args) =>
  execFileSync('npx', ['--yes', 'sharp-cli', ...args], { stdio: ['ignore', 'ignore', 'inherit'], shell: process.platform === 'win32' });

let total = 0;
for (const s of sources) {
  const input = join(sourceDir, `${s.unsplashId}.jpg`);
  if (!existsSync(input)) throw new Error(`Missing source ${input} for ${s.key}`);
  const dir = outDirs[s.kind];
  for (const [suffix, size] of [['', 1200], ['-400', 400]]) {
    const output = join(dir, `${s.key}${suffix}.webp`);
    sharp(['-i', input, '-o', output, '--format', 'webp', '--quality', '78', 'resize', String(size), String(size), '--fit', 'inside', '--withoutEnlargement']);
    total += statSync(output).size;
  }
}
console.log(`${sources.length} sources -> ${sources.length * 2} files, ${(total / 1024 / 1024).toFixed(2)} MB`);
