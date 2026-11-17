import { buildFullAddress, copyWithFeedback, hasValue, initializeOnceWhenDomReady, installCopyDelegation, resolveCopyRequest } from "/clipboard.mjs?v=20260722-copy-modal-fix";

const state = { pagina: 1, tamanhoPagina: 20, totalPaginas: 0, currentClient: null, formSnapshot: "", loading: false };
const elementIds = [
  "session-name", "logout-button", "new-client-button", "search-input", "type-filter", "city-filter", "active-filter", "sort-filter",
  "loading-state", "error-state", "empty-state", "table-wrap", "clients-body", "result-count", "pagination", "previous-page",
  "next-page", "page-info", "retry-button", "client-dialog", "client-form", "form-title", "form-error", "close-form", "cancel-form",
  "save-client", "details-dialog", "details-title", "details-sections", "close-details", "close-details-footer", "edit-from-details",
  "copy-address", "copy-all", "manual-copy-dialog", "manual-copy-text", "select-manual-copy", "close-manual-copy", "toast-stack"
];
let elements;

initializeOnceWhenDomReady(document, "clientes-app", initialize, handleApplicationInitializationError);

async function initialize() {
  elements = collectElements();
  bindEvents();
  const response = await apiFetch("/api/auth/me");
  if (!response?.ok) return;
  const session = await response.json();
  elements.session_name.textContent = session.nome;
  await loadClients();
}

function collectElements() {
  const entries = elementIds.map(id => [id.replace(/-/g, "_"), document.getElementById(id)]);
  const missing = entries.filter(([, element]) => !element).map(([key]) => key);
  if (missing.length) throw new Error(`Elementos obrigatórios ausentes: ${missing.join(", ")}`);
  return Object.fromEntries(entries);
}

function bindEvents() {
  elements.logout_button.addEventListener("click", logout);
  elements.new_client_button.addEventListener("click", () => openForm());
  elements.retry_button.addEventListener("click", loadClients);
  elements.previous_page.addEventListener("click", () => changePage(-1));
  elements.next_page.addEventListener("click", () => changePage(1));
  elements.clients_body.addEventListener("click", handleRowAction);
  elements.client_form.addEventListener("submit", saveClient);
  elements.client_form.addEventListener("input", handleFormInput);
  elements.close_form.addEventListener("click", closeForm);
  elements.cancel_form.addEventListener("click", closeForm);
  elements.client_dialog.addEventListener("cancel", event => { event.preventDefault(); closeForm(); });
  elements.close_details.addEventListener("click", () => elements.details_dialog.close());
  elements.close_details_footer.addEventListener("click", () => elements.details_dialog.close());
  elements.edit_from_details.addEventListener("click", () => { const client = state.currentClient; elements.details_dialog.close(); openForm(client); });
  try {
    installCopyDelegation(document, handleCopyAction);
    document.documentElement.dataset.copyWiring = "ready";
  } catch (error) {
    handleCopyInitializationError(error);
  }
  elements.select_manual_copy.addEventListener("click", selectManualCopyText);
  elements.close_manual_copy.addEventListener("click", () => elements.manual_copy_dialog.close());
  elements.manual_copy_dialog.addEventListener("cancel", event => { event.preventDefault(); elements.manual_copy_dialog.close(); });
  window.addEventListener("beforeunload", event => { if (isFormDirty()) { event.preventDefault(); event.returnValue = ""; } });

  let timer;
  [elements.search_input, elements.city_filter].forEach(input => input.addEventListener("input", () => {
    clearTimeout(timer); timer = setTimeout(resetAndLoad, 300);
  }));
  [elements.type_filter, elements.active_filter, elements.sort_filter].forEach(input => input.addEventListener("change", resetAndLoad));
}

async function apiFetch(url, options) {
  try {
    const response = await fetch(url, options);
    if (response.status === 401) { window.location.replace("/login"); return null; }
    return response;
  } catch {
    return null;
  }
}

async function logout() {
  elements.logout_button.disabled = true;
  await apiFetch("/api/auth/logout", { method: "POST" });
  window.location.replace("/login");
}

function resetAndLoad() { state.pagina = 1; loadClients(); }
function changePage(delta) { const next = state.pagina + delta; if (next >= 1 && next <= state.totalPaginas) { state.pagina = next; loadClients(); } }

async function loadClients() {
  if (state.loading) return;
  state.loading = true;
  showListState("loading");
  const [ordenarPor, direcao] = elements.sort_filter.value.split(":");
  const params = new URLSearchParams({
    pagina: state.pagina, tamanhoPagina: state.tamanhoPagina, ativo: elements.active_filter.value, ordenarPor, direcao
  });
  if (elements.search_input.value.trim()) params.set("busca", elements.search_input.value.trim());
  if (elements.type_filter.value) params.set("tipoPessoa", elements.type_filter.value);
  if (elements.city_filter.value.trim()) params.set("cidade", elements.city_filter.value.trim());

  const response = await apiFetch(`/api/clientes?${params}`);
  state.loading = false;
  if (!response?.ok) { showListState("error"); return; }
  const result = await response.json();
  state.totalPaginas = result.totalPaginas;
  renderClients(result);
}

function showListState(kind) {
  elements.loading_state.hidden = kind !== "loading";
  elements.error_state.hidden = kind !== "error";
  elements.empty_state.hidden = kind !== "empty";
  elements.table_wrap.hidden = kind !== "table";
  elements.pagination.hidden = kind !== "table";
}

function renderClients(result) {
  elements.clients_body.replaceChildren();
  elements.result_count.textContent = `${result.totalItens} ${result.totalItens === 1 ? "cliente" : "clientes"}`;
  if (!result.itens.length) { showListState("empty"); return; }

  for (const client of result.itens) {
    const row = document.createElement("tr");
    if (!client.ativo) row.classList.add("inactive");
    [client.nomeRazaoSocial, formatDocument(client.documento), formatPersonType(client.tipoPessoa), client.cidade || "—", formatPhone(client.telefone) || "—"].forEach(value => {
      const cell = document.createElement("td"); cell.textContent = value; row.appendChild(cell);
    });
    const status = document.createElement("td");
    const badge = document.createElement("span"); badge.className = `badge${client.ativo ? "" : " inactive"}`; badge.textContent = client.ativo ? "Ativo" : "Inativo"; status.appendChild(badge); row.appendChild(status);
    const actionsCell = document.createElement("td"); actionsCell.className = "actions-column";
    const actions = document.createElement("div"); actions.className = "row-actions";
    actions.append(actionButton("Visualizar", "view", client.id), actionButton("Editar", "edit", client.id), actionButton(client.ativo ? "Inativar" : "Reativar", client.ativo ? "deactivate" : "reactivate", client.id, client.ativo));
    actionsCell.appendChild(actions); row.appendChild(actionsCell); elements.clients_body.appendChild(row);
  }
  elements.page_info.textContent = `Página ${result.pagina} de ${Math.max(1, result.totalPaginas)}`;
  elements.previous_page.disabled = result.pagina <= 1;
  elements.next_page.disabled = result.pagina >= result.totalPaginas;
  showListState("table");
}

function actionButton(text, action, id, danger = false) {
  const button = document.createElement("button");
  button.type = "button"; button.className = `button secondary${danger ? " danger-action" : ""}`; button.textContent = text;
  button.dataset.action = action; button.dataset.id = id; button.setAttribute("aria-label", `${text} cliente`); return button;
}

async function handleRowAction(event) {
  const button = event.target.closest("button[data-action]"); if (!button) return;
  const id = Number(button.dataset.id);
  if (button.dataset.action === "view") return showDetails(id);
  if (button.dataset.action === "edit") { const client = await getClient(id); if (client) openForm(client); return; }
  const activate = button.dataset.action === "reactivate";
  if (!window.confirm(`${activate ? "Reativar" : "Inativar"} este cliente?`)) return;
  button.disabled = true;
  const response = await apiFetch(`/api/clientes/${id}/${activate ? "reativar" : "inativar"}`, { method: "PATCH" });
  if (response?.ok) { showToast(`Cliente ${activate ? "reativado" : "inativado"}.`); await loadClients(); }
  else { const error = await readError(response); showToast(error, true); button.disabled = false; }
}

async function getClient(id) {
  const response = await apiFetch(`/api/clientes/${id}`);
  if (!response?.ok) { showToast("Não foi possível carregar o cliente.", true); return null; }
  return response.json();
}

async function openForm(client = null) {
  clearFormErrors(); elements.client_form.reset();
  elements.form_title.textContent = client ? "Editar cliente" : "Novo cliente";
  if (client) fillForm(client);
  updatePersonType(); state.formSnapshot = formPayloadJson();
  elements.client_dialog.showModal(); elements.client_form.elements.nomeRazaoSocial.focus();
}

function fillForm(client) {
  for (const [key, value] of Object.entries(client)) {
    const input = elements.client_form.elements[key]; if (!input || key === "tipoPessoa") continue;
    if (input.type === "checkbox") input.checked = Boolean(value); else input.value = value ?? "";
  }
  elements.client_form.elements.documento.value = formatDocument(client.documento);
  elements.client_form.elements.cep.value = formatCep(client.cep);
  elements.client_form.elements.telefone.value = formatPhone(client.telefone);
}

function closeForm() {
  if (isFormDirty() && !window.confirm("Há alterações não salvas. Deseja sair mesmo assim?")) return;
  state.formSnapshot = ""; elements.client_dialog.close();
}

function isFormDirty() { return elements.client_dialog.open && state.formSnapshot && formPayloadJson() !== state.formSnapshot; }

function handleFormInput(event) {
  const input = event.target;
  if (input.name === "documento") { input.value = formatDocument(input.value); updatePersonType(); }
  if (input.name === "cep") input.value = formatCep(input.value);
  if (input.name === "telefone") input.value = formatPhone(input.value);
  if (input.name === "uf") input.value = input.value.toUpperCase();
  input.removeAttribute("aria-invalid");
  const error = elements.client_form.querySelector(`[data-error-for="${input.name}"]`); if (error) error.textContent = "";
}

function updatePersonType() {
  const length = digits(elements.client_form.elements.documento.value).length;
  elements.client_form.elements.tipoPessoaDisplay.value = length === 11 ? "Pessoa física" : length === 14 ? "Pessoa jurídica" : "";
}

async function saveClient(event) {
  event.preventDefault(); clearFormErrors();
  if (!elements.client_form.reportValidity()) { focusFirstInvalid(); return; }
  const id = Number(elements.client_form.elements.id.value || 0);
  const button = elements.save_client; button.disabled = true; button.textContent = "Salvando...";
  const response = await apiFetch(id ? `/api/clientes/${id}` : "/api/clientes", {
    method: id ? "PUT" : "POST", headers: { "Content-Type": "application/json", Accept: "application/json" }, body: formPayloadJson()
  });
  button.disabled = false; button.textContent = "Salvar cliente";
  if (response?.ok) { state.formSnapshot = ""; elements.client_dialog.close(); showToast(`Cliente ${id ? "atualizado" : "cadastrado"}.`); await loadClients(); return; }
  const payload = await response?.json().catch(() => null);
  showValidationErrors(payload?.errors);
  elements.form_error.textContent = payload?.message || (response ? "Revise os campos destacados." : "Não foi possível conectar ao sistema.");
  focusFirstInvalid();
}

function formPayloadJson() {
  const form = elements.client_form.elements;
  const nullable = name => form[name].value.trim() || null;
  return JSON.stringify({
    codigoLegado: form.codigoLegado.value ? Number(form.codigoLegado.value) : null,
    nomeRazaoSocial: nullable("nomeRazaoSocial"), documento: nullable("documento"), produtorRural: form.produtorRural.checked,
    inscricaoEstadual: nullable("inscricaoEstadual"), inscricaoMunicipal: nullable("inscricaoMunicipal"), dataInicioRelacionamento: nullable("dataInicioRelacionamento"),
    cep: nullable("cep"), logradouro: nullable("logradouro"), numero: nullable("numero"), complemento: nullable("complemento"), bairro: nullable("bairro"), cidade: nullable("cidade"), uf: nullable("uf"),
    telefone: nullable("telefone"), emailEnvio: nullable("emailEnvio"), emailNotaFiscal: nullable("emailNotaFiscal"), observacoes: nullable("observacoes")
  });
}

function clearFormErrors() {
  elements.form_error.textContent = "";
  elements.client_form.querySelectorAll("[data-error-for]").forEach(item => item.textContent = "");
  elements.client_form.querySelectorAll("[aria-invalid]").forEach(item => item.removeAttribute("aria-invalid"));
}

function showValidationErrors(errors) {
  if (!errors) return;
  for (const [field, messages] of Object.entries(errors)) {
    const input = elements.client_form.elements[field]; const target = elements.client_form.querySelector(`[data-error-for="${field}"]`);
    if (input) input.setAttribute("aria-invalid", "true"); if (target) target.textContent = Array.isArray(messages) ? messages.join(" ") : messages;
  }
}

function focusFirstInvalid() { elements.client_form.querySelector("[aria-invalid='true'], :invalid")?.focus(); }

async function showDetails(id) {
  const client = await getClient(id); if (!client) return;
  state.currentClient = client; elements.details_title.textContent = client.nomeRazaoSocial; renderDetails(client); elements.details_dialog.showModal();
}

function renderDetails(client) {
  elements.details_sections.replaceChildren();
  const address = buildFullAddress(client, formatCep);
  const sections = [
    ["Identificação", [["Nome ou razão social", client.nomeRazaoSocial], ["CPF ou CNPJ", formatDocument(client.documento)], ["Tipo de pessoa", formatPersonType(client.tipoPessoa)], ["Código legado", client.codigoLegado], ["Produtor rural", client.produtorRural ? "Sim" : "Não"], ["Início do relacionamento", formatDate(client.dataInicioRelacionamento)], ["Situação", client.ativo ? "Ativo" : "Inativo"]]],
    ["Inscrições", [["Inscrição estadual", client.inscricaoEstadual], ["Inscrição municipal", client.inscricaoMunicipal]]],
    ["Endereço", [["CEP", formatCep(client.cep)], ["Endereço", client.logradouro], ["Número", client.numero], ["Complemento", client.complemento], ["Bairro", client.bairro], ["Cidade", client.cidade], ["UF", client.uf]]],
    ["Contato", [["Telefone", formatPhone(client.telefone)], ["E-mail de contato", client.emailEnvio], ["E-mail de nota fiscal", client.emailNotaFiscal]]],
    ["Informações adicionais", [["Observações", client.observacoes]]]
  ];
  for (const [title, fields] of sections) {
    const filled = fields.filter(([, value]) => hasValue(value)); if (!filled.length) continue;
    const section = document.createElement("section"); section.className = "detail-section";
    const heading = document.createElement("h3"); heading.textContent = title; section.appendChild(heading);
    const rows = document.createElement("div"); rows.className = "detail-rows";
    for (const [label, value] of filled) rows.appendChild(detailRow(label, value));
    section.appendChild(rows); elements.details_sections.appendChild(section);
  }
  elements.copy_address.disabled = !address;
}

function detailRow(label, value) {
  const row = document.createElement("div"); row.className = "detail-row";
  const labelElement = document.createElement("span"); labelElement.className = "detail-label"; labelElement.textContent = label;
  const valueElement = document.createElement("span"); valueElement.className = "detail-value"; valueElement.textContent = String(value).trim();
  const button = document.createElement("button"); button.type = "button"; button.className = "button secondary copy-button"; button.textContent = "Copiar";
  button.dataset.copyAction = "value"; button.dataset.copyLabel = label;
  button.title = `Copiar ${label}`; button.setAttribute("aria-label", `Copiar ${label}`);
  row.append(labelElement, valueElement, button); return row;
}

function handleCopyAction(button) {
  try {
    const request = resolveCopyRequest(button, state.currentClient, { formatCep, formatDocument, formatPersonType, formatDate, formatPhone });
    return copyValue(request.text, button, request.label);
  } catch (error) {
    showToast("Não foi possível preparar o texto para cópia.", true);
    logDevelopmentError("Falha ao resolver o texto do botão de copiar.", error);
    return null;
  }
}

function copyValue(value, button, label) {
  return copyWithFeedback(value, button, {
    onSuccess: () => showCopySuccess(button, label),
    onFailure: () => showToast("Não foi possível copiar automaticamente. Toque e segure o texto para copiar manualmente.", true),
    onManualFallback: showManualCopy
  }).catch(() => null);
}

function showCopySuccess(button, label) {
  const original = button.dataset.copyOriginalText || button.textContent;
  button.dataset.copyOriginalText = original;
  const width = button.getBoundingClientRect?.().width;
  if (width) button.style.minWidth = `${Math.ceil(width)}px`;
  clearTimeout(button.copyFeedbackTimer);
  button.textContent = "Copiado!";
  button.classList.add("copied");
  showToast(`${label} copiado.`);
  button.copyFeedbackTimer = setTimeout(() => {
    button.textContent = original;
    button.classList.remove("copied");
    button.style.minWidth = "";
    delete button.dataset.copyOriginalText;
  }, 1400);
}

function showManualCopy(text) {
  elements.manual_copy_text.value = text;
  if (!elements.manual_copy_dialog.open) elements.manual_copy_dialog.showModal();
  selectManualCopyText();
}

function selectManualCopyText() {
  const textarea = elements.manual_copy_text;
  try { textarea.focus({ preventScroll: true }); } catch { textarea.focus(); }
  textarea.select();
  textarea.setSelectionRange?.(0, textarea.value.length);
}

function showToast(message, isError = false) {
  const toast = document.createElement("div"); toast.className = `toast t-toast is-open${isError ? " error" : ""}`; toast.textContent = message; elements.toast_stack.appendChild(toast); setTimeout(() => toast.remove(), 3200);
}

function handleCopyInitializationError(error) {
  showToast("Não foi possível ativar os botões de copiar. Atualize a página.", true);
  logDevelopmentError("Falha ao inicializar os botões de copiar.", error);
}

function handleApplicationInitializationError(error) {
  const stack = document.getElementById("toast-stack");
  if (stack) {
    const toast = document.createElement("div"); toast.className = "toast t-toast is-open error"; toast.textContent = "Não foi possível iniciar a tela de clientes. Atualize a página."; stack.appendChild(toast);
  }
  logDevelopmentError("Falha ao inicializar a tela de clientes.", error);
}

function logDevelopmentError(message, error) {
  if (["localhost", "127.0.0.1", "::1"].includes(window.location.hostname)) console.error(message, error);
}

async function readError(response) {
  if (!response) return "Não foi possível conectar ao sistema.";
  const payload = await response.json().catch(() => null); return payload?.message || "Não foi possível concluir a operação.";
}

function digits(value) { return String(value || "").replace(/\D/g, ""); }
function formatDocument(value) { const d = digits(value).slice(0, 14); if (d.length <= 11) return d.replace(/^(\d{3})(\d)/, "$1.$2").replace(/^(\d{3})\.(\d{3})(\d)/, "$1.$2.$3").replace(/(\d{3})(\d{1,2})$/, "$1-$2"); return d.replace(/^(\d{2})(\d)/, "$1.$2").replace(/^(\d{2})\.(\d{3})(\d)/, "$1.$2.$3").replace(/\.(\d{3})(\d)/, ".$1/$2").replace(/(\d{4})(\d{1,2})$/, "$1-$2"); }
function formatCep(value) { const d = digits(value).slice(0, 8); return d.replace(/(\d{5})(\d)/, "$1-$2"); }
function formatPhone(value) { const d = digits(value).slice(0, 13); if (d.length === 10) return d.replace(/(\d{2})(\d{4})(\d{4})/, "($1) $2-$3"); if (d.length === 11) return d.replace(/(\d{2})(\d{5})(\d{4})/, "($1) $2-$3"); return value ? String(value).trim() : ""; }
function formatPersonType(value) { return value === 1 || value === "PessoaFisica" ? "Pessoa física" : value === 2 || value === "PessoaJuridica" ? "Pessoa jurídica" : "—"; }
function formatDate(value) { if (!value) return ""; const [year, month, day] = value.split("-"); return `${day}/${month}/${year}`; }
