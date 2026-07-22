const COPY_ERROR_MESSAGE = "Não foi possível copiar automaticamente.";
const COPY_DELEGATION_KEY = Symbol.for("lavanderiaAnjos.copyDelegation");
const DOM_INITIALIZERS_KEY = Symbol.for("lavanderiaAnjos.domInitializers");

export class ClipboardCopyError extends Error {
  constructor(code, cause) {
    super(code === "empty" ? "Não há valor para copiar." : COPY_ERROR_MESSAGE);
    this.name = "ClipboardCopyError";
    this.code = code;
    if (cause) this.cause = cause;
  }
}

export function hasValue(value) {
  return value !== null && value !== undefined && String(value).trim() !== "";
}

function normalizedText(value) {
  return String(value ?? "").trim();
}

function focusElement(element) {
  if (typeof element?.focus !== "function") return;
  try {
    element.focus({ preventScroll: true });
  } catch {
    element.focus();
  }
}

function isConnectedToDocument(element, documentRef) {
  if (!element) return false;
  if (typeof element.isConnected === "boolean") return element.isConnected;
  return typeof documentRef?.contains !== "function" || documentRef.contains(element);
}

function isExplicitlyInert(element) {
  if (!element) return true;
  if (element.inert === true) return true;
  return Boolean(element.closest?.("[inert]"));
}

function findMostRecentModalDialog(documentRef) {
  const dialogs = Array.from(documentRef?.querySelectorAll?.("dialog[open]") ?? []);
  for (let index = dialogs.length - 1; index >= 0; index -= 1) {
    try {
      if (dialogs[index].matches(":modal")) return dialogs[index];
    } catch {
      return null;
    }
  }
  return null;
}

export function resolveCopyHost(documentRef, sourceButton = null) {
  const sourceDialog = sourceButton?.closest?.("dialog[open]");
  if (sourceDialog) return sourceDialog;

  return findMostRecentModalDialog(documentRef) ?? documentRef?.body ?? null;
}

function canRestoreFocus(element, documentRef) {
  if (!isConnectedToDocument(element, documentRef) || isExplicitlyInert(element)) return false;

  const modalDialog = findMostRecentModalDialog(documentRef);
  if (!modalDialog) return true;
  return element === modalDialog || modalDialog.contains?.(element) === true;
}

function restoreFocus(activeElement, sourceButton, documentRef) {
  const target = canRestoreFocus(activeElement, documentRef)
    ? activeElement
    : canRestoreFocus(sourceButton, documentRef)
      ? sourceButton
      : null;
  focusElement(target);
}

function removeElement(element) {
  if (typeof element?.remove === "function") element.remove();
  else element?.parentNode?.removeChild?.(element);
}

function visuallyHide(element) {
  Object.assign(element.style, {
    position: "fixed",
    top: "0",
    left: "-9999px",
    width: "1px",
    height: "1px",
    padding: "0",
    border: "0",
    opacity: "0"
  });
}

export function fallbackCopyText(text, environment = globalThis, sourceButton = null) {
  const documentRef = environment.document;
  if (!documentRef?.body || typeof documentRef.createElement !== "function" || typeof documentRef.execCommand !== "function") return false;

  const activeElement = documentRef.activeElement;
  const host = resolveCopyHost(documentRef, sourceButton);
  if (!host?.appendChild) return false;
  const textarea = documentRef.createElement("textarea");
  textarea.value = text;
  textarea.setAttribute("readonly", "");
  textarea.setAttribute("aria-hidden", "true");
  visuallyHide(textarea);
  textarea.style.fontSize = "16px";

  host.appendChild(textarea);
  try {
    focusElement(textarea);
    textarea.select();
    textarea.setSelectionRange?.(0, textarea.value.length);
    return documentRef.execCommand("copy") === true;
  } finally {
    removeElement(textarea);
    restoreFocus(activeElement, sourceButton, documentRef);
  }
}

export function fallbackCopyContentEditable(text, environment = globalThis, sourceButton = null) {
  const documentRef = environment.document;
  const getSelection = environment.getSelection?.bind(environment) ?? documentRef?.defaultView?.getSelection?.bind(documentRef.defaultView);
  if (!documentRef?.body || typeof documentRef.createElement !== "function" || typeof documentRef.createRange !== "function" || typeof documentRef.execCommand !== "function" || !getSelection) return false;

  const activeElement = documentRef.activeElement;
  const host = resolveCopyHost(documentRef, sourceButton);
  if (!host?.appendChild) return false;
  const editable = documentRef.createElement("div");
  editable.textContent = text;
  editable.setAttribute("contenteditable", "true");
  editable.setAttribute("aria-hidden", "true");
  visuallyHide(editable);
  editable.style.whiteSpace = "pre-wrap";
  editable.style.userSelect = "text";
  editable.style.webkitUserSelect = "text";

  const selection = getSelection();
  host.appendChild(editable);
  try {
    focusElement(editable);
    const range = documentRef.createRange();
    range.selectNodeContents(editable);
    selection.removeAllRanges();
    selection.addRange(range);
    return documentRef.execCommand("copy") === true;
  } finally {
    selection?.removeAllRanges?.();
    removeElement(editable);
    restoreFocus(activeElement, sourceButton, documentRef);
  }
}

function runFallbacks(text, sourceButton, environment) {
  let lastError;
  try {
    if (fallbackCopyText(text, environment, sourceButton)) return "fallback-textarea";
  } catch (error) {
    lastError = error;
  }

  try {
    if (fallbackCopyContentEditable(text, environment, sourceButton)) return "fallback-contenteditable";
  } catch (error) {
    lastError = error;
  }

  throw new ClipboardCopyError("unavailable", lastError);
}

// Esta função não é async de propósito: em HTTP, o fallback roda antes que
// o listener do clique devolva o controle ao navegador e preserve o gesto do usuário.
export function copyText(value, sourceButton = null, environment = globalThis) {
  const text = normalizedText(value);
  if (!text) return Promise.reject(new ClipboardCopyError("empty"));

  const clipboard = environment.navigator?.clipboard;
  const writeText = clipboard?.writeText;
  const secureContextWasExplicitlyDenied = environment.isSecureContext === false;

  if (typeof writeText !== "function" || secureContextWasExplicitlyDenied) {
    try {
      return Promise.resolve(runFallbacks(text, sourceButton, environment));
    } catch (error) {
      return Promise.reject(error);
    }
  }

  let clipboardResult;
  try {
    clipboardResult = writeText.call(clipboard, text);
  } catch {
    try {
      return Promise.resolve(runFallbacks(text, sourceButton, environment));
    } catch (error) {
      return Promise.reject(error);
    }
  }

  return Promise.resolve(clipboardResult).then(
    () => "clipboard",
    () => runFallbacks(text, sourceButton, environment)
  );
}

export function copyWithFeedback(value, sourceButton, handlers = {}, environment = globalThis) {
  const text = normalizedText(value);
  return copyText(text, sourceButton, environment).then(
    method => {
      handlers.onSuccess?.(method);
      return method;
    },
    error => {
      handlers.onFailure?.(error);
      if (error?.code !== "empty" && text) handlers.onManualFallback?.(text);
      throw error;
    }
  );
}

export function installCopyDelegation(root, handler) {
  if (!root?.addEventListener || typeof handler !== "function") throw new TypeError("Raiz e handler de cópia são obrigatórios.");
  const existing = root[COPY_DELEGATION_KEY];
  if (existing) {
    existing.handler = handler;
    return existing.dispose;
  }

  const registration = { handler, dispose: null };
  const listener = event => {
    const button = event.target?.closest?.("button[data-copy-action]");
    if (!button || (button !== root && typeof root.contains === "function" && !root.contains(button))) return;
    button.type = "button";
    event.preventDefault?.();
    registration.handler(button, event);
  };
  const dispose = () => {
    root.removeEventListener?.("click", listener);
    if (root[COPY_DELEGATION_KEY] === registration) delete root[COPY_DELEGATION_KEY];
  };
  registration.dispose = dispose;
  Object.defineProperty(root, COPY_DELEGATION_KEY, { configurable: true, value: registration });
  root.addEventListener("click", listener);
  return dispose;
}

export function initializeOnceWhenDomReady(documentRef, key, initializer, onError = () => {}) {
  if (!documentRef?.addEventListener || typeof initializer !== "function" || !key) {
    throw new TypeError("Documento, chave e inicializador são obrigatórios.");
  }

  let initializers = documentRef[DOM_INITIALIZERS_KEY];
  if (!initializers) {
    initializers = new Set();
    Object.defineProperty(documentRef, DOM_INITIALIZERS_KEY, { configurable: true, value: initializers });
  }
  if (initializers.has(key)) return false;
  initializers.add(key);

  const run = () => {
    try {
      Promise.resolve(initializer()).catch(onError);
    } catch (error) {
      onError(error);
    }
  };

  if (documentRef.readyState === "loading") documentRef.addEventListener("DOMContentLoaded", run, { once: true });
  else run();
  return true;
}

function cleanAddressPart(value) {
  return hasValue(value) ? String(value).trim().replace(/^[,;\s]+|[,;\s]+$/g, "") : "";
}

export function buildFullAddress(cliente, formatCep = value => value) {
  const city = cleanAddressPart(cliente.cidade);
  const uf = cleanAddressPart(cliente.uf);
  const cityUf = city && uf ? `${city}/${uf}` : city || uf;
  const cep = hasValue(cliente.cep) ? cleanAddressPart(formatCep(cliente.cep)) : "";
  const parts = [
    cliente.logradouro,
    cliente.numero,
    cliente.complemento,
    cliente.bairro,
    cityUf,
    cep ? `CEP ${cep}` : null
  ];
  return parts.map(cleanAddressPart).filter(hasValue).join(", ");
}

export function buildAllData(cliente, formatted = {}) {
  const fields = [
    ["Nome ou razão social", cliente.nomeRazaoSocial],
    ["CPF ou CNPJ", formatted.documento ?? cliente.documento],
    ["Tipo de pessoa", hasValue(cliente.tipoPessoa) ? formatted.tipoPessoa ?? cliente.tipoPessoa : null],
    ["Código legado", cliente.codigoLegado],
    ["Produtor rural", cliente.produtorRural ? "Sim" : null],
    ["Inscrição estadual", cliente.inscricaoEstadual],
    ["Inscrição municipal", cliente.inscricaoMunicipal],
    ["Início do relacionamento", hasValue(cliente.dataInicioRelacionamento) ? formatted.dataInicioRelacionamento ?? cliente.dataInicioRelacionamento : null],
    ["Endereço", formatted.endereco],
    ["Telefone", formatted.telefone ?? cliente.telefone],
    ["E-mail de contato", cliente.emailEnvio],
    ["E-mail para nota fiscal", cliente.emailNotaFiscal],
    ["Observações", cliente.observacoes]
  ];
  return fields
    .filter(([, fieldValue]) => hasValue(fieldValue))
    .map(([label, fieldValue]) => `${label}: ${String(fieldValue).trim()}`)
    .join("\n");
}

export function resolveCopyRequest(button, cliente, formatters = {}) {
  const action = button?.dataset?.copyAction;
  if (action === "value") {
    const text = button.closest?.(".detail-row")?.querySelector?.(".detail-value")?.textContent ?? "";
    return { text: normalizedText(text), label: button.dataset.copyLabel || "Valor", action };
  }

  const formatCep = formatters.formatCep ?? (value => value);
  const address = cliente ? buildFullAddress(cliente, formatCep) : "";
  if (action === "address") return { text: address, label: "Endereço completo", action };

  if (action === "all") {
    const formatted = cliente ? {
      documento: formatters.formatDocument?.(cliente.documento) ?? cliente.documento,
      tipoPessoa: formatters.formatPersonType?.(cliente.tipoPessoa) ?? cliente.tipoPessoa,
      dataInicioRelacionamento: formatters.formatDate?.(cliente.dataInicioRelacionamento) ?? cliente.dataInicioRelacionamento,
      endereco: address,
      telefone: formatters.formatPhone?.(cliente.telefone) ?? cliente.telefone
    } : {};
    return { text: cliente ? buildAllData(cliente, formatted) : "", label: "Dados do cliente", action };
  }

  throw new TypeError("Ação de cópia desconhecida.");
}
