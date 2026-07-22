import assert from "node:assert/strict";
import path from "node:path";
import test from "node:test";
import { pathToFileURL } from "node:url";

const clipboardModuleUrl = process.env.LAVANDERIA_PACKAGE_ROOT
  ? pathToFileURL(path.resolve(process.env.LAVANDERIA_PACKAGE_ROOT, "app/wwwroot/clipboard.mjs")).href
  : new URL("../../src/LavanderiaAnjos.Web/wwwroot/clipboard.mjs", import.meta.url).href;
const {
  buildAllData,
  buildFullAddress,
  copyText,
  copyWithFeedback,
  fallbackCopyContentEditable,
  fallbackCopyText,
  hasValue,
  initializeOnceWhenDomReady,
  installCopyDelegation,
  resolveCopyHost
} = await import(clipboardModuleUrl);

function createFallbackEnvironment({ clipboard, execResults = [true], isSecureContext, dialogs = [] } = {}) {
  const children = [];
  const created = [];
  const selection = {
    ranges: [],
    removeAllRanges() { this.ranges = []; },
    addRange(range) { this.ranges.push(range); }
  };
  const previousFocus = { focusCalls: 0, focus() { this.focusCalls += 1; activeElement = this; } };
  let activeElement = previousFocus;
  let execIndex = 0;

  const makeHost = name => ({
    name,
    children: [],
    appendChild(element) { this.children.push(element); children.push(element); element.parentNode = this; return element; },
    removeChild(element) {
      const localIndex = this.children.indexOf(element);
      if (localIndex >= 0) this.children.splice(localIndex, 1);
      const index = children.indexOf(element);
      if (index >= 0) children.splice(index, 1);
    },
    contains(element) { return element === this || this.children.includes(element) || element?.ownerDialog === this; }
  });
  const body = makeHost("body");
  const document = {
    body,
    get activeElement() { return activeElement; },
    querySelectorAll(selector) { assert.equal(selector, "dialog[open]"); return dialogs; },
    createElement(tagName) {
      const element = {
        tagName: tagName.toUpperCase(),
        value: "",
        textContent: "",
        style: {},
        attributes: {},
        selected: false,
        selectionRange: null,
        parentNode: null,
        setAttribute(name, value) { this.attributes[name] = value; },
        focus() { activeElement = this; },
        select() { this.selected = true; },
        setSelectionRange(start, end) { this.selectionRange = [start, end]; },
        remove() { this.parentNode?.removeChild(this); this.parentNode = null; }
      };
      created.push(element);
      return element;
    },
    createRange() { return { selectedNode: null, selectNodeContents(node) { this.selectedNode = node; } }; },
    execCommand(command) {
      assert.equal(command, "copy");
      const result = execResults[Math.min(execIndex, execResults.length - 1)];
      execIndex += 1;
      return result;
    }
  };
  const environment = { navigator: clipboard ? { clipboard } : {}, document, getSelection: () => selection };
  if (isSecureContext !== undefined) environment.isSecureContext = isSecureContext;
  return { environment, body, children, created, dialogs, makeHost, previousFocus, selection, get execCalls() { return execIndex; } };
}

function createDialog(fixture, { modal = true, modalSelectorSupported = true } = {}) {
  const dialog = fixture.makeHost("dialog");
  dialog.open = true;
  dialog.matches = selector => {
    assert.equal(selector, ":modal");
    if (!modalSelectorSupported) throw new Error(":modal nÃ£o suportado");
    return modal;
  };
  fixture.dialogs.push(dialog);
  return dialog;
}

function createSourceButton(dialog = null) {
  return {
    ownerDialog: dialog,
    isConnected: true,
    inert: false,
    focusCalls: 0,
    focus() { this.focusCalls += 1; },
    closest(selector) {
      if (selector === "dialog[open]") return dialog;
      if (selector === "[inert]") return null;
      return null;
    }
  };
}

test("1. usa navigator.clipboard.writeText quando disponível e permitido", async () => {
  let copied;
  const environment = { isSecureContext: true, navigator: { clipboard: { writeText: async value => { copied = value; } } } };
  assert.equal(await copyText("529.982.247-25", null, environment), "clipboard");
  assert.equal(copied, "529.982.247-25");
});

test("2. com API indisponível executa o fallback ainda dentro do gesto", async () => {
  let gestureActive = true;
  const fixture = createFallbackEnvironment();
  fixture.environment.document.execCommand = () => gestureActive;
  const result = copyText("valor individual", null, fixture.environment);
  gestureActive = false;
  assert.equal(await result, "fallback-textarea");
});

test("3. faz fallback quando Clipboard API retorna rejeição", async () => {
  const fixture = createFallbackEnvironment({ clipboard: { writeText: async () => { throw new Error("HTTP inseguro"); } } });
  assert.equal(await copyText("teste", null, fixture.environment), "fallback-textarea");
});

test("faz fallback síncrono quando writeText lança exceção imediatamente", async () => {
  let gestureActive = true;
  const fixture = createFallbackEnvironment({ clipboard: { writeText() { throw new Error("Operação negada"); } } });
  fixture.environment.document.execCommand = () => gestureActive;
  const result = copyText("teste", null, fixture.environment);
  gestureActive = false;
  assert.equal(await result, "fallback-textarea");
});

test("4. aceita sucesso somente quando execCommand copy retorna true", async () => {
  const fixture = createFallbackEnvironment({ execResults: [true] });
  assert.equal(await copyText("teste", null, fixture.environment), "fallback-textarea");
  assert.equal(fixture.execCalls, 1);
});

test("5. execCommand false em todos os fallbacks resulta em falha", async () => {
  const fixture = createFallbackEnvironment({ execResults: [false, false] });
  await assert.rejects(copyText("teste", null, fixture.environment), error => error.code === "unavailable");
  assert.equal(fixture.execCalls, 2);
});

test("6. remove todos os elementos temporários mesmo quando a cópia falha", async () => {
  const fixture = createFallbackEnvironment({ execResults: [false, false] });
  await assert.rejects(copyText("teste", null, fixture.environment));
  assert.equal(fixture.created.length, 2);
  assert.equal(fixture.children.length, 0);
});

test("7. restaura o foco anterior depois do fallback", async () => {
  const fixture = createFallbackEnvironment();
  await copyText("teste", null, fixture.environment);
  assert.equal(fixture.previousFocus.focusCalls, 1);
  assert.equal(fixture.environment.document.activeElement, fixture.previousFocus);
});

test("modal 1 e 13. botão fora de diálogo e ausência de modal usam document.body", () => {
  const fixture = createFallbackEnvironment();
  const button = createSourceButton();
  assert.equal(resolveCopyHost(fixture.environment.document, button), fixture.body);
});

test("modal 2-5. botão dentro de diálogo aberto hospeda textarea no próprio diálogo e o remove", () => {
  const fixture = createFallbackEnvironment();
  const dialog = createDialog(fixture);
  const button = createSourceButton(dialog);
  let parentDuringCopy;
  fixture.environment.document.execCommand = command => {
    assert.equal(command, "copy");
    parentDuringCopy = fixture.created.at(-1).parentNode;
    assert.equal(parentDuringCopy.children.at(-1).tagName, "TEXTAREA");
    return true;
  };

  assert.equal(resolveCopyHost(fixture.environment.document, button), dialog);
  assert.equal(fallbackCopyText("Cliente Modal", fixture.environment, button), true);
  assert.equal(parentDuringCopy, dialog);
  assert.equal(dialog.children.length, 0);
  assert.equal(fixture.children.length, 0);
});

test("modal 6. fallback contenteditable é filho do diálogo durante a cópia", () => {
  const fixture = createFallbackEnvironment();
  const dialog = createDialog(fixture);
  const button = createSourceButton(dialog);
  let parentDuringCopy;
  fixture.environment.document.execCommand = command => {
    assert.equal(command, "copy");
    const editable = fixture.created.at(-1);
    parentDuringCopy = editable.parentNode;
    assert.equal(editable.attributes.contenteditable, "true");
    return true;
  };

  assert.equal(fallbackCopyContentEditable("ConteÃºdo iOS", fixture.environment, button), true);
  assert.equal(parentDuringCopy, dialog);
  assert.equal(dialog.children.length, 0);
});

test("modal 7. restaura foco apenas em elemento conectado e não inerte", () => {
  const fixture = createFallbackEnvironment();
  const dialog = createDialog(fixture);
  const button = createSourceButton(dialog);
  fixture.previousFocus.isConnected = false;

  assert.equal(fallbackCopyText("Foco seguro", fixture.environment, button), true);
  assert.equal(fixture.previousFocus.focusCalls, 0);
  assert.equal(button.focusCalls, 1);
});

test("modal 11. sem botão de origem escolhe o diálogo modal aberto mais recente", () => {
  const fixture = createFallbackEnvironment();
  const first = createDialog(fixture);
  const second = createDialog(fixture);
  assert.equal(resolveCopyHost(fixture.environment.document), second);
  assert.notEqual(resolveCopyHost(fixture.environment.document), first);
});

test("modal 12. sem suporte a :modal usa document.body quando não há diálogo de origem", () => {
  const fixture = createFallbackEnvironment();
  createDialog(fixture, { modalSelectorSupported: false });
  assert.equal(resolveCopyHost(fixture.environment.document), fixture.body);
});

test("modal 12. sem suporte a :modal ainda prioriza o diálogo aberto do botão", () => {
  const fixture = createFallbackEnvironment();
  const dialog = createDialog(fixture, { modalSelectorSupported: false });
  assert.equal(resolveCopyHost(fixture.environment.document, createSourceButton(dialog)), dialog);
});

test("8. delega cliques de botões criados dinamicamente sem listeners duplicados", () => {
  const listeners = new Map();
  let listenerRegistrations = 0;
  const root = {
    addEventListener(name, listener) { listenerRegistrations += 1; listeners.set(name, listener); },
    removeEventListener(name) { listeners.delete(name); },
    contains() { return true; }
  };
  const button = { type: "", dataset: { copyAction: "value" } };
  let calls = 0;
  installCopyDelegation(root, () => { calls += 1; });
  installCopyDelegation(root, () => { calls += 100; });
  listeners.get("click")({ target: { closest: () => button }, preventDefault() {} });
  assert.equal(calls, 100);
  assert.equal(listenerRegistrations, 1);
  assert.equal(button.type, "button");
});

test("inicializador aguarda DOMContentLoaded e executa uma única vez", async () => {
  let listener;
  let calls = 0;
  const document = { readyState: "loading", addEventListener(name, value, options) { assert.equal(name, "DOMContentLoaded"); assert.deepEqual(options, { once: true }); listener = value; } };
  assert.equal(initializeOnceWhenDomReady(document, "clientes", () => { calls += 1; }), true);
  assert.equal(initializeOnceWhenDomReady(document, "clientes", () => { calls += 100; }), false);
  assert.equal(calls, 0);
  listener();
  await Promise.resolve();
  assert.equal(calls, 1);
});

test("9. botão de cópia dentro de formulário não envia o formulário", () => {
  let listener;
  const root = { addEventListener(name, value) { listener = value; }, removeEventListener() {}, contains() { return true; } };
  const button = { type: "submit", dataset: { copyAction: "value" } };
  let defaultPrevented = false;
  installCopyDelegation(root, () => {});
  listener({ target: { closest: () => button }, preventDefault() { defaultPrevented = true; } });
  assert.equal(button.type, "button");
  assert.equal(defaultPrevented, true);
});

test("10. texto vazio não é copiado", async () => {
  assert.equal(hasValue("  "), false);
  await assert.rejects(copyText("", null, {}), error => error.code === "empty");
});

test("11. monta endereço completo somente com partes preenchidas e sem pontuação duplicada", () => {
  const address = buildFullAddress(
    { logradouro: "Rua Exemplo,", numero: "123", complemento: null, bairro: "Centro;", cidade: "Bastos", uf: "SP", cep: "17690000" },
    value => `${value.slice(0, 5)}-${value.slice(5)}`
  );
  assert.equal(address, "Rua Exemplo, 123, Centro, Bastos/SP, CEP 17690-000");
  assert.doesNotMatch(address, /,\s*,|;\s*,/);
});

test("12. copiar todos mantém rótulos e omite campos vazios", () => {
  const text = buildAllData(
    { nomeRazaoSocial: "Empresa Exemplo", documento: "11222333000181", produtorRural: false, inscricaoEstadual: null, emailNotaFiscal: "nf@exemplo.com" },
    { documento: "11.222.333/0001-81", endereco: "" }
  );
  assert.match(text, /Nome ou razão social: Empresa Exemplo/);
  assert.match(text, /CPF ou CNPJ: 11\.222\.333\/0001-81/);
  assert.match(text, /E-mail para nota fiscal: nf@exemplo\.com/);
  assert.doesNotMatch(text, /Inscrição estadual|Endereço:|undefined|null|\n\n/);
});

test("13. preserva valores com acentos", async () => {
  let copied;
  const environment = { navigator: { clipboard: { writeText: value => { copied = value; } } } };
  await copyText("João Açúcar São José", null, environment);
  assert.equal(copied, "João Açúcar São José");
});

test("14. preserva quebras de linha internas", async () => {
  let copied;
  const environment = { navigator: { clipboard: { writeText: value => { copied = value; } } } };
  await copyText("Linha 1\nLinha 2\nLinha 3", null, environment);
  assert.equal(copied, "Linha 1\nLinha 2\nLinha 3");
});

test("15. copia CPF e CNPJ nos formatos exibidos", () => {
  const cpf = buildAllData({ nomeRazaoSocial: "Pessoa", documento: "52998224725" }, { documento: "529.982.247-25" });
  const cnpj = buildAllData({ nomeRazaoSocial: "Empresa", documento: "11222333000181" }, { documento: "11.222.333/0001-81" });
  assert.match(cpf, /CPF ou CNPJ: 529\.982\.247-25/);
  assert.match(cnpj, /CPF ou CNPJ: 11\.222\.333\/0001-81/);
});

test("16. fallback manual é solicitado apenas depois de falha automática total", async () => {
  let manualCalls = 0;
  const success = createFallbackEnvironment({ execResults: [true] });
  await copyWithFeedback("sucesso", null, { onManualFallback() { manualCalls += 1; } }, success.environment);
  assert.equal(manualCalls, 0);

  const failure = createFallbackEnvironment({ execResults: [false, false] });
  await assert.rejects(copyWithFeedback("falha", null, { onManualFallback(text) { manualCalls += 1; assert.equal(text, "falha"); } }, failure.environment));
  assert.equal(manualCalls, 1);
});

test("17. a operação de clipboard não envia dados pessoais pela rede", async () => {
  let networkCalls = 0;
  const environment = {
    navigator: { clipboard: { writeText: async () => {} } },
    fetch() { networkCalls += 1; throw new Error("fetch não deveria ser chamado"); }
  };
  await copyText("CPF 529.982.247-25\nTelefone (14) 99999-9999", null, environment);
  assert.equal(networkCalls, 0);
});

test("fallback secundário contenteditable cobre Safari/iOS quando textarea é recusado", async () => {
  const fixture = createFallbackEnvironment({ execResults: [false, true] });
  assert.equal(await copyText("conteúdo iOS", null, fixture.environment), "fallback-contenteditable");
  assert.equal(fixture.selection.ranges.length, 0);
  assert.equal(fixture.children.length, 0);
});

test("HTTP ignora Clipboard API exposta mas proibida e usa fallback sincronamente", async () => {
  let clipboardCalls = 0;
  let gestureActive = true;
  const fixture = createFallbackEnvironment({
    clipboard: { writeText() { clipboardCalls += 1; return Promise.reject(new Error("inseguro")); } },
    isSecureContext: false
  });
  fixture.environment.document.execCommand = () => gestureActive;
  const result = copyText("rede local", null, fixture.environment);
  gestureActive = false;
  assert.equal(await result, "fallback-textarea");
  assert.equal(clipboardCalls, 0);
});
