#!/usr/bin/env node
/**
 * copy-assets.mjs — copy USWDS static assets into an app's public/ dir.
 *
 * The VA theme (_va-theme.scss) sets $theme-font-path / $theme-image-path to
 * the root-relative URLs /uswds/fonts and /uswds/img, so each front end must
 * serve @uswds/uswds/dist/{fonts,img} from public/uswds/. This runs from the
 * app's `predev` / `prebuild` npm hooks; the output directory is gitignored.
 *
 * Usage (from src/admin or src/public):  node ../theme/uswds/copy-assets.mjs
 */
import { cpSync, existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';

const appRoot = process.cwd();
const require = createRequire(join(appRoot, 'package.json'));
// package.json isn't in the package's exports map; resolve the main entry
// (dist/js/uswds.min.js) and walk up to the package root instead.
const uswdsRoot = dirname(dirname(dirname(require.resolve('@uswds/uswds'))));
const { version } = JSON.parse(readFileSync(join(uswdsRoot, 'package.json'), 'utf8'));

const dest = join(appRoot, 'public', 'uswds');
const stamp = join(dest, '.version');

if (existsSync(stamp) && readFileSync(stamp, 'utf8').trim() === version) {
  console.log(`uswds assets already at ${version} in public/uswds`);
  process.exit(0);
}

rmSync(dest, { recursive: true, force: true });
mkdirSync(dest, { recursive: true });
for (const dir of ['fonts', 'img']) {
  cpSync(join(uswdsRoot, 'dist', dir), join(dest, dir), { recursive: true });
}
writeFileSync(stamp, `${version}\n`);
console.log(`copied uswds ${version} fonts + img to public/uswds`);
