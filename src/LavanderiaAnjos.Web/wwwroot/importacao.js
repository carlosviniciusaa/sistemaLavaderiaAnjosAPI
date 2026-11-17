import { canConfirm, confirmationButtonText, formatApiError, validateSpreadsheetSelection } from "/importacao-utils.mjs";

const state = { loteId: null, csrf: null, pagina: 1, totalPaginas: 0, resumo: null, editItemId: null, duplicateGroup: null };
const byId = id => document.getElementById(id);
const elements = Object.fromEntries([
  "session-name", "logout-button", "upload-panel", "upload-form", "file-input", "file-name", "preview-button", "upload-message",
  "preview-panel", "summary-grid", "metrics-table", "hash-label", "preview-count", "cancel-lot", "open-confirm", "import-filter",
  "import-search", "preview-loading", "preview-empty", "preview-table-wrap", "preview-body", "import-pagination", "import-previous",
  "import-next", "import-page-info", "result-panel", "result-content", "report-link", "history-content", "edit-dialog", "edit-form",
  "edit-title", "close-edit", "cancel-edit", "save-edit", "edit-errors", "duplicate-dialog", "duplicate-options", "close-duplicate",
  "cancel-duplicate", "resolve-duplicate", "confirm-dialog", "confirm-summary", "close-confirm", "cancel-confirm", "confirm-import", "toast-stack"
].map(id => [id.replaceAll("-", "_"), byId(id)]));

initialize();

async function initialize() {
  bindEvents();
  const sessionResponse = await apiFetch("/api/auth/me");
  if (!sessionResponse?.ok) return;
  const session = await sessionResponse.json();
  elements.session_name.textContent = session.nome;
  const tokenResponse = await apiFetch("/api/importacoes/clientes/antiforgery");
  if (tokenResponse?.ok) state.csrf = (await tokenResponse.json()).token;
  const requestedLot = new URLSearchParams(window.location.search).get("lote");
  if (requestedLot) {
    state.loteId = requestedLot; elements.upload_panel.hidden = true; elements.preview_panel.hidden = false; setStep(2); await loadPreview();
  }
  await loadHistory();
}

function bindEvents() {
  elements.logout_button.addEventListener("click", logout);
  elements.file_input.addEventListener("change", () => elements.file_name.textContent = elements.file_input.files[0]?.name || "Nenhum arquivo selecionado");
  elements.upload_form.addEventListener("submit", upload);
  elements.import_filter.addEventListener("change", resetAndLoad);
  let searchTimer;
  elements.import_search.addEventListener("input", () => { clearTimeout(searchTimer); searchTimer = setTimeout(resetAndLoad, 300); });
  elements.import_previous.addEventListener("click", () => changePage(-1));
  elements.import_next.addEventListener("click", () => changePage(1));
  elements.preview_body.addEventListener("click", handleRowAction);
  elements.preview_body.addEventListener("change", handleSelection);
  elements.close_edit.addEventListener("click", closeEdit);
  elements.cancel_edit.addEventListener("click", closeEdit);
  elements.edit_form.addEventListener("submit", saveCorrection);
  elements.edit_form.elements.documento.addEventListener("input", updatePersonType);
  elements.close_duplicate.addEventListener("click", closeDuplicate);
  elements.cancel_duplicate.addEventListener("click", closeDuplicate);
  elements.duplicate_options.addEventListener("change", () => { elements.resolve_duplicate.disabled = !elements.duplicate_options.querySelector("input:checked"); });
  elements.resolve_duplicate.addEventListener("click", resolveDuplicate);
  elements.open_confirm.addEventListener("click", openConfirmation);
  elements.close_confirm.addEventListener("click", closeConfirmation);
  elements.cancel_confirm.addEventListener("click", closeConfirmation);
  elements.confirm_import.addEventListener("click", confirmImport);
  elements.cancel_lot.addEventListener("click", cancelLot);
}

async function apiFetch(url, options = {}) {
  try {
    const response = await fetch(url, options);
    if (response.status === 401) { window.location.replace("/login"); return null; }
    return response;
  } catch { return null; }
}

async function mutate(url, method, body, form = false) {
  const headers = { "X-CSRF-TOKEN": state.csrf, Accept: "application/json" };
  if (!form) headers["Content-Type"] = "application/json";
  return apiFetch(url, { method, headers, body: form ? body : body === undefined ? undefined : JSON.stringify(body) });
}

async function logout() {
  elements.logout_button.disabled = true;
  await apiFetch("/api/auth/logout", { method: "POST" });
  window.location.replace("/login");
}

async function upload(event) {
  event.preventDefault();
  const file = elements.file_input.files[0];
  elements.upload_message.textContent = "";
  const selectionError = validateSpreadsheetSelection(file);
  if (selectionError) { elements.upload_message.textContent = selectionError; return; }
  const data = new FormData(); data.append("arquivo", file);
  setBusy(elements.preview_button, true, "Analisando…");
  const response = await mutate("/api/importacoes/clientes/previsualizar", "POST", data, true);
  setBusy(elements.preview_button, false, "Analisar planilha");
  if (!response?.ok) { elements.upload_message.textContent = await errorMessage(response); return; }
  const result = await response.json();
  state.loteId = result.resumo.id; state.pagina = 1;
  elements.upload_panel.hidden = true; elements.preview_panel.hidden = false;
  setStep(2); renderPreview(result); await loadHistory();
}

async function loadPreview() {
  if (!state.loteId) return;
  elements.preview_loading.hidden = false;
  const params = new URLSearchParams({ pagina: state.pagina, tamanhoPagina: 50 });
  if (elements.import_filter.value) params.set("filtro", elements.import_filter.value);
  if (elements.import_search.value.trim()) params.set("busca", elements.import_search.value.trim());
  const response = await apiFetch(`/api/importacoes/clientes/${state.loteId}?${params}`);
  elements.preview_loading.hidden = true;
  if (!response?.ok) { showToast(await errorMessage(response), true); return; }
  renderPreview(await response.json());
}

function renderPreview(result) {
  state.resumo = result.resumo; state.totalPaginas = result.itens.totalPaginas;
  renderSummary(result.resumo); renderMetrics(result.metricas); renderItems(result.itens);
  elements.hash_label.textContent = `SHA-256: ${result.resumo.hashArquivoAbreviado}…`;
  elements.open_confirm.textContent = `Revisar confirmação (${result.resumo.quantidadeSelecionada})`;
  elements.open_confirm.disabled = !canConfirm(result.resumo);
}

function renderSummary(summary) {
  const values = [
    ["Total", summary.quantidadeTotal], ["Válidos", summary.quantidadeValida], ["Com alertas", summary.quantidadeComAlerta],
    ["Inválidos", summary.quantidadeInvalida], ["Duplicados", summary.quantidadeDuplicada], ["Já existentes", summary.quantidadeExistente],
    ["Selecionados", summary.quantidadeSelecionada], ["Ignorados", summary.quantidadeIgnorada]
  ];
  elements.summary_grid.replaceChildren(...values.map(([label, value]) => {
    const card = document.createElement("article"); card.className = "summary-card";
    const strong = document.createElement("strong"); strong.textContent = value;
    const span = document.createElement("span"); span.textContent = label; card.append(strong, span); return card;
  }));
}

function renderMetrics(metrics) {
  const table = document.createElement("table");
  table.innerHTML = "<thead><tr><th>Aba</th><th>Linhas físicas</th><th>Cabeçalho</th><th>Vazias</th><th>Parciais descartadas</th><th>Ocultas</th><th>Filtradas</th><th>Registros</th></tr></thead>";
  const body = document.createElement("tbody");
  for (const item of metrics.abas) {
    const row = document.createElement("tr");
    [item.aba, item.ultimaLinhaFisica, item.linhasCabecalho, item.linhasTotalmenteVazias, item.linhasParciaisDescartadas, item.linhasOcultas, item.linhasFiltradas, item.registrosConsiderados].forEach(value => {
      const cell = document.createElement("td"); cell.textContent = value; row.appendChild(cell);
    }); body.appendChild(row);
  }
  table.appendChild(body); elements.metrics_table.replaceChildren(table);
}

function renderItems(page) {
  elements.preview_body.replaceChildren();
  elements.preview_count.textContent = `${page.totalItens} registro(s) no filtro`;
  elements.preview_empty.hidden = page.itens.length > 0;
  elements.preview_table_wrap.hidden = page.itens.length === 0;
  elements.import_pagination.hidden = page.itens.length === 0;
  for (const item of page.itens) elements.preview_body.appendChild(itemRow(item));
  elements.import_page_info.textContent = `Página ${page.pagina} de ${Math.max(1, page.totalPaginas)}`;
  elements.import_previous.disabled = page.pagina <= 1; elements.import_next.disabled = page.pagina >= page.totalPaginas;
}

function itemRow(item) {
  const row = document.createElement("tr");
  const selectCell = document.createElement("td"); const checkbox = document.createElement("input");
  checkbox.type = "checkbox"; checkbox.checked = item.selecionado; checkbox.disabled = !item.podeSelecionar && !item.selecionado;
  checkbox.dataset.itemId = item.id; checkbox.setAttribute("aria-label", `Selecionar linha ${item.linha}`); selectCell.appendChild(checkbox); row.appendChild(selectCell);
  appendText(row, `${item.aba}\nLinha ${item.linha}`, "pre-line"); appendText(row, item.nomeRazaoSocial || "—");
  appendText(row, item.documentoEncontradoMascarado || "—"); appendText(row, item.documentoNormalizadoMascarado || "—");
  appendText(row, personType(item.tipoPessoa)); appendText(row, item.cidade || "—");
  const problemCell = document.createElement("td"); const badge = document.createElement("span"); badge.className = `badge status-${statusClass(item.status)}`; badge.textContent = item.status; problemCell.appendChild(badge);
  const messages = [...item.problemas, ...item.alertas]; if (messages.length) { const list = document.createElement("ul"); list.className = "issue-list"; messages.forEach(message => { const entry = document.createElement("li"); entry.textContent = message; list.appendChild(entry); }); problemCell.appendChild(list); } row.appendChild(problemCell);
  const actionCell = document.createElement("td"); const actions = document.createElement("div"); actions.className = "row-actions";
  actions.appendChild(actionButton("Corrigir", "edit", item.id));
  if (item.grupoDuplicidade) { const button = actionButton("Resolver duplicidade", "duplicate", item.id); button.dataset.group = item.grupoDuplicidade; actions.appendChild(button); }
  if (item.clienteExistenteId) { const link = document.createElement("a"); link.className = "button secondary button-link"; link.href = `/clientes?cliente=${item.clienteExistenteId}`; link.textContent = item.clienteExistenteAtivo ? "Abrir existente" : "Revisar inativo"; actions.appendChild(link); }
  actionCell.appendChild(actions); row.appendChild(actionCell); return row;
}

function appendText(row, value, className) { const cell = document.createElement("td"); cell.textContent = value; if (className) cell.className = className; row.appendChild(cell); }
function actionButton(label, action, id) { const button = document.createElement("button"); button.type = "button"; button.className = "button secondary"; button.textContent = label; button.dataset.action = action; button.dataset.itemId = id; return button; }

async function handleSelection(event) {
  const checkbox = event.target.closest("input[data-item-id]"); if (!checkbox) return;
  checkbox.disabled = true;
  const response = await mutate(`/api/importacoes/clientes/${state.loteId}/itens/${checkbox.dataset.itemId}/selecao`, "PATCH", { selecionado: checkbox.checked });
  if (!response?.ok) showToast(await errorMessage(response), true);
  else renderPreview(await response.json());
}

async function handleRowAction(event) {
  const button = event.target.closest("button[data-action]"); if (!button) return;
  if (button.dataset.action === "edit") await openEdit(button.dataset.itemId);
  if (button.dataset.action === "duplicate") await openDuplicate(button.dataset.group);
}

async function openEdit(itemId) {
  const response = await apiFetch(`/api/importacoes/clientes/${state.loteId}/itens/${itemId}`);
  if (!response?.ok) { showToast(await errorMessage(response), true); return; }
  const item = await response.json(); state.editItemId = itemId; elements.edit_form.reset();
  for (const [key, value] of Object.entries(item.dados)) { const input = elements.edit_form.elements[key]; if (!input) continue; if (input.type === "checkbox") input.checked = Boolean(value); else input.value = value ?? ""; }
  elements.edit_title.textContent = `${item.aba} — linha ${item.linha}`; elements.edit_errors.textContent = item.erros.join(" "); updatePersonType(); elements.edit_dialog.showModal();
}

function closeEdit() { elements.edit_dialog.close(); state.editItemId = null; }
function updatePersonType() { const length = digits(elements.edit_form.elements.documento.value).length; elements.edit_form.elements.tipoPessoa.value = length === 11 ? "Pessoa física" : length === 14 ? "Pessoa jurídica" : ""; }

async function saveCorrection(event) {
  event.preventDefault(); if (!elements.edit_form.reportValidity()) return;
  setBusy(elements.save_edit, true, "Revalidando…");
  const response = await mutate(`/api/importacoes/clientes/${state.loteId}/itens/${state.editItemId}`, "PUT", correctionPayload());
  setBusy(elements.save_edit, false, "Salvar e revalidar");
  if (!response?.ok) { elements.edit_errors.textContent = await errorMessage(response); return; }
  closeEdit(); renderPreview(await response.json()); showToast("Registro corrigido e lote revalidado.");
}

function correctionPayload() {
  const form = elements.edit_form.elements; const nullable = name => form[name].value.trim() || null;
  return { codigoLegado: form.codigoLegado.value ? Number(form.codigoLegado.value) : null, nomeRazaoSocial: nullable("nomeRazaoSocial"), documento: nullable("documento"), produtorRural: form.produtorRural.checked, inscricaoEstadual: nullable("inscricaoEstadual"), inscricaoMunicipal: nullable("inscricaoMunicipal"), dataInicioRelacionamento: nullable("dataInicioRelacionamento"), cep: nullable("cep"), logradouro: nullable("logradouro"), numero: nullable("numero"), complemento: nullable("complemento"), bairro: nullable("bairro"), cidade: nullable("cidade"), uf: nullable("uf"), telefone: nullable("telefone"), emailEnvio: nullable("emailEnvio"), emailNotaFiscal: nullable("emailNotaFiscal"), observacoes: null };
}

async function openDuplicate(group) {
  const response = await apiFetch(`/api/importacoes/clientes/${state.loteId}?filtro=duplicados&tamanhoPagina=100`);
  if (!response?.ok) { showToast(await errorMessage(response), true); return; }
  const options = (await response.json()).itens.itens.filter(item => item.grupoDuplicidade === group);
  state.duplicateGroup = group; elements.duplicate_options.replaceChildren();
  for (const item of options) {
    const label = document.createElement("label"); label.className = "duplicate-card";
    const radio = document.createElement("input"); radio.type = "radio"; radio.name = "principal"; radio.value = item.id;
    const title = document.createElement("strong"); title.textContent = `${item.aba} — linha ${item.linha}`;
    const details = document.createElement("span"); details.textContent = `${item.nomeRazaoSocial || "Sem nome"}\n${item.documentoNormalizadoMascarado || "Sem documento"}\n${item.cidade || "Sem cidade"}`;
    label.append(radio, title, details); elements.duplicate_options.appendChild(label);
  }
  elements.resolve_duplicate.disabled = true; elements.duplicate_dialog.showModal();
}

function closeDuplicate() { elements.duplicate_dialog.close(); state.duplicateGroup = null; }
async function resolveDuplicate() {
  const selected = elements.duplicate_options.querySelector("input:checked"); if (!selected) return;
  setBusy(elements.resolve_duplicate, true, "Resolvendo…");
  const response = await mutate(`/api/importacoes/clientes/${state.loteId}/duplicidades/resolver`, "POST", { grupoDuplicidade: state.duplicateGroup, itemPrincipalId: selected.value });
  setBusy(elements.resolve_duplicate, false, "Usar linha selecionada");
  if (!response?.ok) { showToast(await errorMessage(response), true); return; }
  closeDuplicate(); renderPreview(await response.json()); showToast("Linha principal definida; as demais foram ignoradas.");
}

function openConfirmation() {
  const value = state.resumo;
  elements.confirm_summary.replaceChildren(summaryList([
    ["Selecionados", value.quantidadeSelecionada], ["Inválidos", value.quantidadeInvalida], ["Ignorados", value.quantidadeIgnorada],
    ["Duplicados não resolvidos", value.quantidadeDuplicada], ["Já existentes", value.quantidadeExistente]
  ]));
  elements.confirm_import.textContent = confirmationButtonText(value.quantidadeSelecionada);
  elements.confirm_import.disabled = !canConfirm(value); setStep(3); elements.confirm_dialog.showModal();
}

function closeConfirmation() { elements.confirm_dialog.close(); setStep(2); }
async function confirmImport() {
  setBusy(elements.confirm_import, true, "Importando…");
  const response = await mutate(`/api/importacoes/clientes/${state.loteId}/confirmar`, "POST");
  setBusy(elements.confirm_import, false, confirmationButtonText(state.resumo.quantidadeSelecionada));
  if (!response?.ok) { showToast(await errorMessage(response), true); closeConfirmation(); await loadPreview(); return; }
  const result = await response.json(); elements.confirm_dialog.close(); elements.preview_panel.hidden = true; elements.result_panel.hidden = false; setStep(4);
  elements.result_content.replaceChildren(summaryList([["Clientes importados", result.quantidadeImportada], ["Ignorados", result.quantidadeIgnorada], ["Inválidos", result.quantidadeInvalida], ["Responsável", result.usuarioResponsavel]]));
  elements.report_link.href = `/api/importacoes/clientes/${state.loteId}/relatorio`; await loadHistory();
}

async function cancelLot() {
  if (!window.confirm("Cancelar esta pré-visualização? Nenhum cliente será cadastrado.")) return;
  const response = await mutate(`/api/importacoes/clientes/${state.loteId}/cancelar`, "POST");
  if (!response?.ok) { showToast(await errorMessage(response), true); return; }
  state.loteId = null; elements.preview_panel.hidden = true; elements.upload_panel.hidden = false; elements.upload_form.reset(); elements.file_name.textContent = "Nenhum arquivo selecionado"; setStep(1); await loadHistory();
}

async function loadHistory() {
  const response = await apiFetch("/api/importacoes/clientes/historico"); if (!response?.ok) { elements.history_content.textContent = "Não foi possível carregar o histórico."; return; }
  const lots = await response.json(); if (!lots.length) { elements.history_content.textContent = "Nenhum lote registrado."; return; }
  const table = document.createElement("table"); table.innerHTML = "<thead><tr><th>Data</th><th>Arquivo</th><th>Status</th><th>Total</th><th>Importados</th><th>Ignorados</th><th>Relatório</th></tr></thead>";
  const body = document.createElement("tbody");
  for (const lot of lots) { const row = document.createElement("tr"); [new Date(lot.criadoEm).toLocaleString("pt-BR"), lot.nomeArquivo, lotStatus(lot.status), lot.quantidadeTotal, lot.quantidadeImportada, lot.quantidadeIgnorada].forEach(value => appendText(row, value)); const action = document.createElement("td"); if (lot.status === 1) { const link = document.createElement("a"); link.href = `/importacao?lote=${encodeURIComponent(lot.id)}`; link.textContent = "Abrir prévia"; action.appendChild(link); } else if ([2, 3].includes(lot.status)) { const link = document.createElement("a"); link.href = `/api/importacoes/clientes/${lot.id}/relatorio`; link.textContent = "Baixar CSV"; action.appendChild(link); } else action.textContent = "—"; row.appendChild(action); body.appendChild(row); }
  table.appendChild(body); const wrap = document.createElement("div"); wrap.className = "table-wrap"; wrap.appendChild(table); elements.history_content.className = ""; elements.history_content.replaceChildren(wrap);
}

function summaryList(items) { const list = document.createElement("dl"); list.className = "confirmation-list"; items.forEach(([label, value]) => { const term = document.createElement("dt"); term.textContent = label; const detail = document.createElement("dd"); detail.textContent = value; list.append(term, detail); }); return list; }
function resetAndLoad() { state.pagina = 1; loadPreview(); }
function changePage(delta) { const page = state.pagina + delta; if (page >= 1 && page <= state.totalPaginas) { state.pagina = page; loadPreview(); } }
function setStep(step) { document.querySelectorAll(".stepper li").forEach(item => item.classList.toggle("active", Number(item.dataset.step) <= step)); }
function setBusy(button, busy, text) { button.disabled = busy; button.textContent = text; }
function digits(value) { return String(value || "").replace(/\D/g, ""); }
function personType(value) { return value === 1 || value === "PessoaFisica" ? "Pessoa física" : value === 2 || value === "PessoaJuridica" ? "Pessoa jurídica" : "—"; }
function statusClass(status) { if (status.includes("Inválido") || status.includes("Duplicado")) return "danger"; if (status.includes("alerta") || status.includes("inativo")) return "warning"; return "success"; }
function lotStatus(value) { return ({ 1: "Pré-visualização", 2: "Concluída", 3: "Cancelada", 4: "Expirada", 5: "Falhou" })[value] || value; }
async function errorMessage(response) { if (!response) return "Não foi possível conectar ao sistema."; return formatApiError(await response.json().catch(() => null)); }
function showToast(message, error = false) { const toast = document.createElement("div"); toast.className = `toast t-toast is-open${error ? " error" : ""}`; toast.textContent = message; elements.toast_stack.appendChild(toast); setTimeout(() => toast.remove(), 4200); }
