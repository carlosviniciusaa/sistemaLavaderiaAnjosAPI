import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const packageRoot = process.env.LAVANDERIA_PACKAGE_ROOT;
const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const webRoot = packageRoot
  ? path.resolve(packageRoot, "app/wwwroot")
  : path.resolve(repositoryRoot, "src/LavanderiaAnjos.Web/wwwroot");
const projectPath = path.resolve(repositoryRoot, "src/LavanderiaAnjos.Web/LavanderiaAnjos.Web.csproj");
const brandRoot = path.join(webRoot, "assets/brand");
const pageNames = ["login.html", "index.html", "importacao.html", "administradores.html"];

async function source(relativePath, encoding = "utf8") {
  return readFile(path.join(webRoot, relativePath), encoding);
}

function pngMetadata(buffer) {
  assert.equal(buffer.subarray(1, 4).toString("ascii"), "PNG");
  return {
    width: buffer.readUInt32BE(16),
    height: buffer.readUInt32BE(20),
    colorType: buffer[25]
  };
}

function icoSizes(buffer) {
  assert.equal(buffer.readUInt16LE(0), 0);
  assert.equal(buffer.readUInt16LE(2), 1);
  const count = buffer.readUInt16LE(4);
  const sizes = [];
  for (let index = 0; index < count; index += 1) {
    const offset = 6 + index * 16;
    sizes.push([buffer[offset] || 256, buffer[offset + 1] || 256]);
  }
  return sizes;
}

test("SVGs oficiais preservam viewBox, transparência e não contêm conteúdo ativo ou remoto", async () => {
  const color = await readFile(path.join(brandRoot, "lavanderia-anjos-color.svg"), "utf8");
  const monochrome = await readFile(path.join(brandRoot, "lavanderia-anjos-monochrome.svg"), "utf8");
  for (const svg of [color, monochrome]) {
    assert.match(svg, /viewBox="0 0 160 160"/);
    assert.doesNotMatch(svg, /<script\b|javascript:|<image\b|<a\b|data:image|(?:href|src)\s*=\s*["']https?:\/\/|url\(\s*["']?https?:\/\//i);
    assert.doesNotMatch(svg, /<rect[^>]+(?:fill="(?:#fff(?:fff)?|white)"|class="background")/i);
  }
  assert.match(color, /linearGradient/);
  assert.match(color, /#f3e600/i);
  assert.doesNotMatch(monochrome, /linearGradient|#[0-9a-f]{3,8}/i);
});

test("todas as páginas reais usam logo, favicons, touch icon, mask icon e manifest versionados", async () => {
  for (const pageName of pageNames) {
    const html = await source(pageName);
    assert.match(html, /lavanderia-anjos-color\.svg\?v=20260824-brand-icons/);
    assert.match(html, /rel="icon" type="image\/png" sizes="16x16"/);
    assert.match(html, /rel="icon" type="image\/png" sizes="32x32"/);
    assert.match(html, /rel="alternate icon"[^>]+favicon\.ico\?v=20260824-brand-icons/);
    assert.match(html, /rel="apple-touch-icon" sizes="180x180"/);
    assert.match(html, /rel="mask-icon"[^>]+lavanderia-anjos-monochrome\.svg\?v=20260824-brand-icons/);
    assert.match(html, /rel="manifest" href="\/site\.webmanifest\?v=20260824-brand-icons"/);
    assert.match(html, /styles\.css\?v=20260824-brand-icons/);
    assert.doesNotMatch(html, /brand-mark|>LA<|src="[A-Z]:\\/i);
  }
});

test("logo do cabeçalho é decorativo e o login usa versão maior sem leitura duplicada", async () => {
  for (const pageName of ["index.html", "importacao.html", "administradores.html"]) {
    const html = await source(pageName);
    assert.match(html, /class="brand-logo"[^>]+width="44" height="44" alt="" aria-hidden="true"/);
  }
  const login = await source("login.html");
  assert.match(login, /class="login-logo"[^>]+width="104" height="104" alt="" aria-hidden="true"/);
  assert.match(login, /<h1 id="login-title">Lavanderia Anjos<\/h1>/);
});

test("CSS preserva proporção e adapta logos ao viewport móvel", async () => {
  const css = await source("styles.css");
  assert.match(css, /\.brand-logo\s*\{[^}]*object-fit:\s*contain/s);
  assert.match(css, /\.login-logo\s*\{[^}]*object-fit:\s*contain/s);
  assert.match(css, /@media \(max-width: 680px\)[\s\S]*\.brand-logo\s*\{\s*width:\s*40px;\s*height:\s*40px;/);
});

test("manifest é simples, não registra service worker e referencia ícones oficiais", async () => {
  const manifest = JSON.parse(await source("site.webmanifest"));
  assert.equal(manifest.name, "Lavanderia Anjos");
  assert.equal(manifest.short_name, "Lavanderia Anjos");
  assert.equal(manifest.start_url, "/clientes");
  assert.deepEqual(manifest.icons.map(icon => icon.sizes), ["192x192", "512x512"]);
  assert.ok(manifest.icons.every(icon => icon.purpose === "any"));
  const files = await Promise.all(["login.html", "index.html", "importacao.html", "administradores.html"].map(name => source(name)));
  assert.ok(files.every(html => !/serviceWorker\.register/i.test(html)));
});

test("PNGs têm dimensões exatas e canal alfa", async () => {
  const expected = new Map([
    ["lavanderia-anjos-16.png", 16],
    ["lavanderia-anjos-32.png", 32],
    ["lavanderia-anjos-48.png", 48],
    ["apple-touch-icon.png", 180],
    ["lavanderia-anjos-192.png", 192],
    ["lavanderia-anjos-512.png", 512]
  ]);
  for (const [name, size] of expected) {
    const metadata = pngMetadata(await readFile(path.join(brandRoot, name)));
    assert.deepEqual(metadata, { width: size, height: size, colorType: 6 });
  }
});

test("favicon ICO contém resoluções adequadas ao Windows e navegadores", async () => {
  const sizes = icoSizes(await readFile(path.join(brandRoot, "favicon.ico")));
  for (const size of [16, 32, 48, 256]) assert.ok(sizes.some(([width, height]) => width === size && height === size));
});

test("ícone do executável está configurado e o copy-modal-fix permanece intacto", async () => {
  if (!packageRoot) {
    const project = await readFile(projectPath, "utf8");
    assert.match(project, /<ApplicationIcon>wwwroot\\assets\\brand\\favicon\.ico<\/ApplicationIcon>/);
  }
  const app = await source("app.js");
  const clipboard = await source("clipboard.mjs");
  assert.match(app, /installCopyDelegation\(document, handleCopyAction\)/);
  assert.match(app, /clipboard\.mjs\?v=20260722-copy-modal-fix/);
  assert.match(clipboard, /resolveCopyHost\(documentRef, sourceButton\)/);
  assert.match(clipboard, /host\.appendChild\(textarea\)/);
  assert.match(clipboard, /host\.appendChild\(editable\)/);
  assert.doesNotMatch(clipboard, /documentRef\.body\.appendChild/);
});
