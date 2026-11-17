// Isolated browser regressions: all API calls terminate in this process, never in a database.
const assert = require('node:assert/strict');
const http = require('node:http');
const fs = require('node:fs/promises');
const path = require('node:path');
const repository = path.resolve(__dirname, '../..');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE_PATH || path.join(repository, '.codex-tmp/node_modules/playwright'));
const root = process.env.LAVANDERIA_PACKAGE_ROOT
  ? path.resolve(process.env.LAVANDERIA_PACKAGE_ROOT, 'app/wwwroot')
  : path.join(repository, 'src/LavanderiaAnjos.Web/wwwroot');
const output = path.resolve(process.env.LAVANDERIA_BROWSER_OUTPUT || path.join(repository, '.codex-tmp/visual-polish'));
const types = { '.html': 'text/html; charset=utf-8', '.css': 'text/css', '.js': 'text/javascript', '.mjs': 'text/javascript', '.svg': 'image/svg+xml', '.png': 'image/png', '.ico': 'image/x-icon', '.webmanifest': 'application/manifest+json' };
const client = { id: 1, nomeRazaoSocial: 'Cliente de demonstração', documento: '52998224725', tipoPessoa: 1, cidade: 'Cidade de teste', telefone: '11999990000', ativo: true, logradouro: 'Rua de teste', numero: '10', uf: 'SP', criadoEm: '2026-11-17T12:00:00Z' };
const admins = [{ id: 1, nome: 'Administrador de teste', login: 'admin.teste', ativo: true, versao: 1, criadoEm: '2026-11-17T12:00:00Z', atualizadoEm: null }];
const preview = {
  resumo: { id: 'lote-teste', hashArquivoAbreviado: '1234567890abcdef', quantidadeTotal: 1, quantidadeValida: 1, quantidadeComAlerta: 0, quantidadeInvalida: 0, quantidadeDuplicada: 0, quantidadeExistente: 0, quantidadeSelecionada: 1, quantidadeIgnorada: 0 },
  metricas: { abas: [{ aba: 'Clientes', ultimaLinhaFisica: 2, linhasCabecalho: 1, linhasTotalmenteVazias: 0, linhasParciaisDescartadas: 0, linhasOcultas: 0, linhasFiltradas: 0, registrosConsiderados: 1 }] },
  itens: { pagina: 1, totalPaginas: 1, totalItens: 1, itens: [{ ...client, aba: 'Clientes', linha: 2, selecionado: true, podeSelecionar: true, documentoEncontradoMascarado: '***.982.***-25', documentoNormalizadoMascarado: '***.982.***-25', status: 'Válido', problemas: [], alertas: [] }] }
};
const requests = [];
let empty = false, fail = false, browser;
const server = http.createServer(async (req, res) => {
  const url = new URL(req.url, 'http://localhost');
  const route = url.pathname;
  if (route.startsWith('/api/')) {
    requests.push({ route, search: url.search, method: req.method });
    let status = 200, data;
    if (route === '/api/auth/me') { status = req.headers.referer?.includes('/login') ? 401 : 200; data = admins[0]; }
    else if (route === '/api/auth/login') { status = 401; data = {}; }
    else if (route.endsWith('/antiforgery')) data = { token: 'isolated-browser-fixture' };
    else if (route === '/api/clientes' && req.method === 'GET') { status = fail ? 503 : 200; data = { itens: empty ? [] : [client], pagina: 1, totalPaginas: empty ? 0 : 1, totalItens: empty ? 0 : 1 }; }
    else if (route === '/api/clientes/1') data = client;
    else if (route === '/api/clientes' && req.method === 'POST') { status = 201; data = client; }
    else if (route === '/api/administradores') data = admins;
    else if (route === '/api/importacoes/clientes/historico') data = [];
    else if (route === '/api/importacoes/clientes/lote-teste') data = preview;
    else { status = 501; data = { message: 'No fixture for this API' }; }
    res.writeHead(status, { 'Content-Type': 'application/json' });
    return res.end(JSON.stringify(data));
  }
  const routes = { '/': '/index.html', '/clientes': '/index.html', '/login': '/login.html', '/importacao': '/importacao.html', '/administradores': '/administradores.html' };
  const file = path.resolve(root, '.' + (routes[route] || route));
  if (!file.startsWith(root + path.sep)) return res.writeHead(403).end();
  try { res.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream' }); res.end(await fs.readFile(file)); }
  catch { res.writeHead(404).end(); }
});
async function settled(page) { await page.evaluate(() => document.getAnimations().forEach(animation => { if (animation.effect.getComputedTiming().iterations !== Infinity) animation.finish(); })); }
async function noOverflow(page) {
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `Overflow: ${page.url()}`);
  assert.ok(await page.locator('img').evaluateAll(imgs => imgs.every(img => img.complete && img.naturalWidth > 0)), 'Brand images render');
}
async function closed(page, selector) { assert.equal(await page.locator(selector).evaluate(dialog => dialog.open), false); await settled(page); }
(async () => {
  await fs.mkdir(output, { recursive: true });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const base = `http://127.0.0.1:${server.address().port}`;
  const errors = [];
  try {
    browser = await chromium.launch({ executablePath: process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
    for (const width of [1440, 1024, 768, 390, 320]) {
      const context = await browser.newContext({ viewport: { width, height: 900 }, reducedMotion: 'no-preference' });
      const page = await context.newPage();
      page.on('pageerror', error => errors.push(error.message));
      for (const route of ['/login', '/clientes', '/administradores', '/importacao']) {
        await page.goto(base + route);
        await page.locator('.split-text-unit').first().waitFor();
        const title = await page.locator('h1 .split-text-label').innerText();
        await page.getByRole('heading', { name: title, exact: true }).waitFor();
        if (route === '/clientes') await page.locator('#clients-body tr').waitFor();
        if (route === '/administradores') await page.locator('#admin-body tr').waitFor();
        await settled(page);
        await noOverflow(page);
        const motion = await page.locator('.split-text-unit').evaluateAll(units => units.map(unit => {
          const style = getComputedStyle(unit); return parseFloat(style.animationDuration) + parseFloat(style.animationDelay);
        }));
        assert.ok(Math.max(...motion) <= .409, 'Title settles in at most 408ms');
        if (route !== '/login') {
          for (const link of await page.locator('nav a').all()) assert.ok(await link.isVisible(), 'All mobile areas stay reachable');
          assert.equal(await page.locator('nav [aria-current="page"]').count(), 1);
          await page.locator('.skip-link').focus();
          await page.keyboard.press('Enter');
          assert.equal(await page.evaluate(() => location.hash), '#main-content');
          assert.equal(await page.evaluate(() => document.activeElement.id), 'main-content');
          assert.equal(await page.locator('.skip-link').evaluate(el => el.matches(':focus')), false);
          await page.locator('#main-content').evaluate(el => el.blur());
          await page.mouse.click(2, 180);
          await page.evaluate(() => scrollTo(0, 0));
        }
        await page.screenshot({ path: path.join(output, `${route.slice(1)}-${width}.png`), fullPage: true });
        if (route === '/clientes') {
          await page.locator('#new-client-button').click();
          assert.equal(await page.locator('#client-dialog').evaluate(d => d.open), true);
          assert.equal(await page.locator('[name="nomeRazaoSocial"]').evaluate(el => el === document.activeElement), true);
          await noOverflow(page);
          await settled(page);
          const dialogBounds = await page.locator('#client-dialog').boundingBox();
          assert.ok(dialogBounds.x >= 0 && dialogBounds.x + dialogBounds.width <= width + 1, 'Dialog fits viewport');
          await page.screenshot({ path: path.join(output, `form-${width}.png`), fullPage: true });
          await page.keyboard.press('Escape');
          await closed(page, '#client-dialog');
          assert.equal(await page.locator('#new-client-button').evaluate(el => el === document.activeElement), true);
          // Reopening during/after the native exit must not leave an invisible or unclickable modal.
          await page.locator('#new-client-button').click();
          await page.locator('#close-form').click();
          await closed(page, '#client-dialog');
        }
        if (route === '/administradores') {
          await page.locator('#new-admin-button').click();
          await page.locator('#admin-form [name="nome"]').fill('Teste visual');
          await page.locator('#admin-form [name="senha"]').fill('Senha1234');
          await page.locator('#admin-form [data-toggle-password="senha"]').click();
          assert.equal(await page.locator('#admin-form [name="senha"]').getAttribute('type'), 'text');
          await page.locator('#cancel-admin-form').click();
          await closed(page, '#admin-dialog');
        }
        if (route === '/importacao') {
          await page.goto(base + '/importacao?lote=lote-teste');
          await page.locator('#preview-body tr').waitFor();
          await settled(page);
          await noOverflow(page);
          assert.equal(await page.locator('.summary-card').count(), 8);
          await page.screenshot({ path: path.join(output, `preview-${width}.png`), fullPage: true });
          await page.locator('#open-confirm').click();
          assert.equal(await page.locator('#confirm-dialog').evaluate(d => d.open), true);
          assert.equal(await page.locator('#confirm-import').isEnabled(), true);
          await page.locator('#cancel-confirm').click();
          await closed(page, '#confirm-dialog');
          assert.equal(await page.locator('#preview-panel').isVisible(), true);
        }
      }
      await context.close();
    }
    const context = await browser.newContext({ viewport: { width: 390, height: 844 } });
    const page = await context.newPage();
    page.on('pageerror', error => errors.push(error.message));
    await page.goto(base + '/clientes');
    await page.locator('#clients-body tr').waitFor();
    await page.locator('#search-input').fill('Busca de teste');
    await page.waitForResponse(response => response.url().includes('busca=Busca'));
    assert.ok(requests.some(request => request.search.includes('busca=Busca')));
    await page.locator('[data-action="view"]').click();
    await page.locator('#details-dialog[open]').waitFor();
    await settled(page);
    await page.locator('#copy-all').click();
    await page.locator('.toast').first().waitFor();
    await settled(page);
    assert.equal(await page.locator('.toast').first().evaluate(el => getComputedStyle(el).opacity), '1');
    await page.locator('#close-details').click();
    await closed(page, '#details-dialog');
    await page.locator('#new-client-button').click();
    const postsBeforeValidation = requests.filter(request => request.route === '/api/clientes' && request.method === 'POST').length;
    await page.locator('#save-client').click();
    assert.equal(requests.filter(request => request.route === '/api/clientes' && request.method === 'POST').length, postsBeforeValidation);
    await page.locator('#client-form [name="nomeRazaoSocial"]').fill('Novo cliente de teste');
    page.once('dialog', dialog => dialog.dismiss());
    await page.keyboard.press('Escape');
    assert.equal(await page.locator('#client-dialog').evaluate(d => d.open), true, 'Dirty form still asks before discarding');
    await page.locator('#client-form [name="documento"]').fill('52998224725');
    await page.locator('#save-client').click();
    await page.locator('#client-dialog').waitFor({ state: 'hidden' });
    assert.ok(requests.some(request => request.route === '/api/clientes' && request.method === 'POST'));
    empty = true;
    await page.goto(base + '/clientes'); await page.locator('#empty-state').waitFor();
    await page.screenshot({ path: path.join(output, 'empty-390.png'), fullPage: true });
    empty = false; fail = true;
    await page.reload(); await page.locator('#error-state').waitFor();
    fail = false;
    await page.locator('#retry-button').click(); await page.locator('#clients-body tr').waitFor();
    await page.goto(base + '/importacao');
    await page.locator('#file-input').setInputFiles({ name: 'arquivo.txt', mimeType: 'text/plain', buffer: Buffer.from('teste') });
    await page.locator('#preview-button').click();
    assert.ok((await page.locator('#upload-message').innerText()).length > 0);
    assert.equal(requests.filter(request => request.route.includes('/previsualizar')).length, 0);
    await page.goto(base + '/login');
    await page.locator('#login').fill('login.teste'); await page.locator('#password').fill('invalida');
    await page.locator('button[type="submit"]').click();
    await page.getByText('Login ou senha inválidos.').waitFor();
    assert.equal(await page.locator('#password').evaluate(el => el === document.activeElement), true);
    await context.close();
    // Real copy events and paste in a native top-layer dialog, with the production module.
    for (const mode of ['api', 'textarea', 'contenteditable', 'manual']) {
      const clipboardContext = await browser.newContext({ viewport: { width: 390, height: 844 }, permissions: ['clipboard-read', 'clipboard-write'] });
      const clipboardPage = await clipboardContext.newPage();
      clipboardPage.on('pageerror', error => errors.push(error.message));
      await clipboardPage.addInitScript(({ mode }) => {
        window.copyAudit = [];
        if (mode !== 'api') Object.defineProperty(navigator, 'clipboard', { value: undefined, configurable: true });
        const nativeCopy = document.execCommand.bind(document);
        document.execCommand = command => {
          if (command !== 'copy') return nativeCopy(command);
          const active = document.activeElement;
          const selected = typeof active.selectionStart === 'number'
            ? active.value.slice(active.selectionStart, active.selectionEnd)
            : document.getSelection().toString();
          window.copyAudit.push({ tag: active.tagName, host: active.closest('dialog')?.id, modal: active.closest('dialog')?.matches(':modal'), selected });
          if (mode === 'manual' || (mode === 'contenteditable' && active.tagName === 'TEXTAREA')) return false;
          return nativeCopy(command);
        };
        window.nativeCopyEvents = 0;
        document.addEventListener('copy', () => window.nativeCopyEvents++);
      }, { mode });
      await clipboardPage.goto(base + '/clientes');
      await clipboardPage.locator('[data-action="view"]').click();
      await clipboardPage.locator('#details-dialog[open]').waitFor();
      await settled(clipboardPage);
      const allText = 'Nome ou razão social: Cliente de demonstração\nCPF ou CNPJ: 529.982.247-25\nTipo de pessoa: Pessoa física\nEndereço: Rua de teste, 10, Cidade de teste/SP\nTelefone: (11) 99999-0000';
      await clipboardPage.locator('#copy-all').click();
      if (mode === 'manual') {
        await clipboardPage.locator('#manual-copy-dialog[open]').waitFor();
        assert.equal(await clipboardPage.locator('#manual-copy-text').inputValue(), allText);
        await clipboardPage.locator('#select-manual-copy').click();
        assert.equal(await clipboardPage.locator('#manual-copy-text').evaluate(el => el.value.slice(el.selectionStart, el.selectionEnd)), allText);
        await clipboardPage.locator('#close-manual-copy').click();
        await closed(clipboardPage, '#manual-copy-dialog');
        assert.equal(await clipboardPage.locator('#details-dialog').evaluate(el => el.matches(':modal')), true);
      } else {
        await clipboardPage.locator('#copy-all.copied').waitFor();
        if (mode !== 'api') {
          const audit = await clipboardPage.evaluate(() => window.copyAudit);
          assert.ok(audit.length > 0);
          assert.ok(audit.every(item => item.host === 'details-dialog' && item.modal === true));
          assert.equal(audit.at(-1).tag, mode === 'textarea' ? 'TEXTAREA' : 'DIV');
          assert.equal(audit.at(-1).selected, allText);
          assert.ok(await clipboardPage.evaluate(() => window.nativeCopyEvents > 0));
          assert.equal(await clipboardPage.locator('#copy-all').evaluate(el => el === document.activeElement), true);
        }
        assert.equal(await clipboardPage.locator('[aria-hidden="true"][readonly], [aria-hidden="true"][contenteditable]').count(), 0);
        // Paste through the browser instead of inferring success from execCommand's return value.
        await clipboardPage.evaluate(() => { const input = document.createElement('textarea'); input.id = 'clipboard-paste-proof'; document.querySelector('#details-dialog').append(input); input.focus(); });
        await clipboardPage.keyboard.press('Control+V');
        assert.equal(await clipboardPage.locator('#clipboard-paste-proof').inputValue(), allText);
      }
      await clipboardContext.close();
    }
    // Changing the OS preference after the title was split also stops all moving letters.
    const dynamicContext = await browser.newContext({ reducedMotion: 'no-preference' });
    const dynamicPage = await dynamicContext.newPage();
    await dynamicPage.goto(base + '/login');
    await dynamicPage.locator('.split-text-unit').first().waitFor();
    await dynamicPage.emulateMedia({ reducedMotion: 'reduce' });
    assert.ok(await dynamicPage.locator('.split-text-unit').evaluateAll(units => units.every(unit => {
      const style = getComputedStyle(unit); return style.animationName === 'none' && style.opacity === '1' && style.transform === 'none';
    })));
    await dynamicContext.close();
    for (const options of [{ reducedMotion: 'reduce' }, { javaScriptEnabled: false }]) {
      const c = await browser.newContext({ ...options, viewport: { width: 320, height: 844 } });
      const p = await c.newPage();
      for (const route of ['/login', '/clientes', '/administradores', '/importacao']) {
        await p.goto(base + route);
        assert.equal(await p.locator('.split-text-unit').count(), 0);
        assert.ok(await p.locator('h1').isVisible());
        await noOverflow(p);
        if (options.reducedMotion && route === '/clientes') {
          await p.locator('#new-client-button').click();
          assert.ok(await p.locator('#client-dialog').isVisible());
          assert.equal(await p.locator('#client-dialog').evaluate(d => getComputedStyle(d).transitionDuration), '0s');
          await p.keyboard.press('Escape'); await closed(p, '#client-dialog');
        }
      }
      await c.close();
    }
    assert.deepEqual(errors, []);
    console.log('PASS: 4 pages at 1440/1024/768/390/320; dialogs, Escape, focus, reopen, dirty form, validation/save, filters, import preview/confirmation, password toggle, empty/error/retry, upload validation, login error; accessible title names, reduced motion initial/dynamic and no-JS headings; real modal clipboard API/textarea/contenteditable copy and paste, manual fallback, cleanup and focus. APIs isolated.');
  } finally {
    if (browser) await browser.close();
    await new Promise(resolve => server.close(resolve));
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
