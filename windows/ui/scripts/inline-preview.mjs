// Turns the `vite build --mode design` output into one self-contained file: design/preview.html.
import { readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const src = join(root, 'node_modules', '.design');
const out = resolve(root, '..', '..', 'design', 'preview.html');

let html = readFileSync(join(src, 'index.html'), 'utf8');
const assets = readdirSync(join(src, 'assets'));
const read = (f) => readFileSync(join(src, 'assets', f), 'utf8');

for (const f of assets.filter((a) => a.endsWith('.css'))) {
  html = html.replace(new RegExp(`<link rel="stylesheet"[^>]*href="\\./assets/${f.replace('.', '\\.')}"[^>]*>`), () => `<style>\n${read(f)}\n</style>`);
}
for (const f of assets.filter((a) => a.endsWith('.js'))) {
  const js = read(f).replaceAll('</script', '<\\/script');
  // Module scripts run after parsing: move it to the end of <body> so #app exists either way.
  html = html.replace(new RegExp(`<script type="module"[^>]*src="\\./assets/${f.replace('.', '\\.')}"[^>]*></script>`), '');
  html = html.replace('</body>', () => `<script type="module">\n${js}\n</script>\n</body>`);
}
if (/\.\/assets\//.test(html)) throw new Error('preview.html still references ./assets/ (an asset was not inlined)');
html = html.replace('<title>PhoneBridge</title>', '<title>PhoneBridge — maquette (preview)</title>');
writeFileSync(out, html);
console.log(`wrote ${out} (${Math.round(html.length / 1024)} KB)`);
