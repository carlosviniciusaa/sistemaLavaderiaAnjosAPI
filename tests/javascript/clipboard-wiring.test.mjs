import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import path from "node:path";
import test from "node:test";
import { pathToFileURL } from "node:url";

const packageRoot = process.env.LAVANDERIA_PACKAGE_ROOT;
const artifactUrl = (repositoryRelative, packageRelative) => packageRoot
  ? pathToFileURL(path.resolve(packageRoot, packageRelative))
  : new URL(repositoryRelative, import.meta.url);
const clipboardModuleUrl = artifactUrl("../../src/LavanderiaAnjos.Web/wwwroot/clipboard.mjs", "app/wwwroot/clipboard.mjs").href;
const { initializeOnceWhenDomReady, installCopyDelegation, resolveCopyRequest } = await import(clipboardModuleUrl);
const appSource = await readFile(artifactUrl("../../src/LavanderiaAnjos.Web/wwwroot/app.js", "app/wwwroot/app.js"), "utf8");
const indexSource = await readFile(artifactUrl("../../src/LavanderiaAnjos.Web/wwwroot/index.html", "app/wwwroot/index.html"), "utf8");
const programSource = packageRoot ? null : await readFile(new URL("../../src/LavanderiaAnjos.Web/Program.cs", import.meta.url), "utf8");

function valueButton(value, label = "Nome ou razão social") {
  const valueElement = { textContent: value };
  const row = { querySelector(selector) { assert.equal(selector, ".detail-value"); return valueElement; } };
  return { dataset: { copyAction: "value", copyLabel: label }, closest(selector) { assert.equal(selector, ".detail-row"); return row; } };
}

function actionButton(action) {
  return { dataset: { copyAction: action } };
}

const clienteA = {
  nomeRazaoSocial: "Cliente A",
  documento: "52998224725",
  tipoPessoa: 1,
  cep: "17690000",
  logradouro: "Rua A",
  numero: "10",
  bairro: "Centro",
  cidade: "Bastos",
  uf: "SP",
  telefone: "14999998888"
};
const formatters = {
  formatCep: () => "17690-000",
  formatDocument: () => "529.982.247-25",
  formatPersonType: () => "Pessoa física",
  formatDate: value => value,
  formatPhone: () => "(14) 99999-8888"
};

test("1. /clientes serve index.html e o entrypoint module importa clipboard.mjs versionado", () => {
  if (programSource) assert.match(programSource, /app\.MapGet\("\/clientes"[\s\S]*?"index\.html"/);
  assert.match(indexSource, /<script type="module" src="\/app\.js\?v=20261117-ui-polish"><\/script>/);
  assert.match(appSource, /from "\/clipboard\.mjs\?v=20260722-copy-modal-fix"/);
});

test("2-4. entrypoint instala installCopyDelegation com document e handleCopyAction", () => {
  assert.match(appSource, /installCopyDelegation\(document, handleCopyAction\)/);
  assert.match(appSource, /function handleCopyAction\(button\)/);
  assert.match(appSource, /resolveCopyRequest\(button, state\.currentClient/);
});

test("5-6. inicializa depois do DOM pronto e apenas uma vez", async () => {
  let domReady;
  let calls = 0;
  const document = { readyState: "loading", addEventListener(name, handler) { assert.equal(name, "DOMContentLoaded"); domReady = handler; } };
  assert.equal(initializeOnceWhenDomReady(document, "wiring", () => { calls += 1; }), true);
  assert.equal(initializeOnceWhenDomReady(document, "wiring", () => { calls += 10; }), false);
  assert.equal(calls, 0);
  domReady();
  await Promise.resolve();
  assert.equal(calls, 1);
});

test("7. value resolve somente o texto associado ao campo", () => {
  assert.deepEqual(resolveCopyRequest(valueButton("  João Açúcar  "), clienteA, formatters), {
    text: "João Açúcar", label: "Nome ou razão social", action: "value"
  });
});

test("8. address usa buildFullAddress com o cliente atual", () => {
  assert.deepEqual(resolveCopyRequest(actionButton("address"), clienteA, formatters), {
    text: "Rua A, 10, Centro, Bastos/SP, CEP 17690-000", label: "Endereço completo", action: "address"
  });
});

test("9. all usa buildAllData com valores formatados", () => {
  const request = resolveCopyRequest(actionButton("all"), clienteA, formatters);
  assert.equal(request.label, "Dados do cliente");
  assert.match(request.text, /Nome ou razão social: Cliente A/);
  assert.match(request.text, /CPF ou CNPJ: 529\.982\.247-25/);
  assert.match(request.text, /Endereço: Rua A, 10, Centro, Bastos\/SP, CEP 17690-000/);
  assert.match(request.text, /Telefone: \(14\) 99999-8888/);
});

test("10-11. delega para botões dinâmicos após fechar e reabrir detalhes", () => {
  let listener;
  let registrations = 0;
  const root = { addEventListener(name, value) { assert.equal(name, "click"); registrations += 1; listener = value; }, removeEventListener() {}, contains() { return true; } };
  const actions = [];
  installCopyDelegation(root, button => actions.push(button.dataset.copyAction));

  for (const button of [valueButton("Cliente A"), actionButton("address"), valueButton("Cliente B")]) {
    button.type = "button";
    listener({ target: { closest: () => button }, preventDefault() {} });
  }
  assert.deepEqual(actions, ["value", "address", "value"]);
  assert.equal(registrations, 1);
});

test("12. troca de cliente resolve address e all com o novo estado", () => {
  const clienteB = { ...clienteA, nomeRazaoSocial: "Cliente B", logradouro: "Avenida B", numero: "20" };
  assert.match(resolveCopyRequest(actionButton("address"), clienteB, formatters).text, /^Avenida B, 20/);
  assert.match(resolveCopyRequest(actionButton("all"), clienteB, formatters).text, /Nome ou razão social: Cliente B/);
});

test("13. login, logout ou reavaliação do entrypoint não duplicam a inicialização", async () => {
  let calls = 0;
  const document = { readyState: "complete", addEventListener() {} };
  initializeOnceWhenDomReady(document, "clientes-app", () => { calls += 1; });
  initializeOnceWhenDomReady(document, "clientes-app", () => { calls += 1; });
  await Promise.resolve();
  assert.equal(calls, 1);
  assert.match(appSource, /logout_button\.addEventListener\("click", logout\)/);
});

test("17-19. entrypoint liga sucesso, toast e fallback manual sem expor dados", () => {
  assert.match(appSource, /onSuccess: \(\) => showCopySuccess\(button, label\)/);
  assert.match(appSource, /showToast\(`\$\{label\} copiado\.`\)/);
  assert.match(appSource, /onManualFallback: showManualCopy/);
  assert.doesNotMatch(appSource, /console\.(?:log|info)\([^)]*(?:value|text|cliente)/i);
});
