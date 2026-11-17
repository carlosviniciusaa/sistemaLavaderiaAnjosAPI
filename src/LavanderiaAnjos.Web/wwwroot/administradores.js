import { formatDateTime, validateIdentity, validatePassword } from "./administradores-utils.mjs";

const state = { administradores: [], usuarioAtualId: null, csrf: "", pendingStatus: null };
const elementIds = [
  "session-name", "logout-button", "new-admin-button", "admin-count", "admin-loading",
  "admin-error", "admin-retry", "admin-empty", "admin-table-wrap", "admin-body",
  "admin-dialog", "admin-form", "admin-form-title", "close-admin-form", "cancel-admin-form",
  "create-password-fields", "admin-form-error", "save-admin", "password-dialog", "password-form",
  "password-admin-name", "close-password-form", "cancel-password-form", "password-form-error",
  "save-password", "status-dialog", "close-status-dialog", "status-message", "cancel-status",
  "confirm-status", "toast-stack"
];
const elements = Object.fromEntries(elementIds.map(id => [id.replaceAll("-", "_"), document.getElementById(id)]));

initialize();

async function initialize() {
  bindEvents();
  try {
    const [meResponse, tokenResponse] = await Promise.all([
      fetch("/api/auth/me", { headers: { Accept: "application/json" } }),
      fetch("/api/administradores/antiforgery", { headers: { Accept: "application/json" } })
    ]);
    if (meResponse.status === 401 || tokenResponse.status === 401) return redirectToLogin();
    if (!meResponse.ok || !tokenResponse.ok) throw new Error("Falha ao iniciar a tela.");
    const me = await meResponse.json();
    state.usuarioAtualId = Number(me.id);
    elements.session_name.textContent = me.nome;
    state.csrf = (await tokenResponse.json()).token;
    await loadAdministradores();
  } catch {
    setListState("error");
  }
}

function bindEvents() {
  elements.new_admin_button.addEventListener("click", openCreate);
  elements.admin_retry.addEventListener("click", loadAdministradores);
  elements.admin_form.addEventListener("submit", saveAdministrador);
  elements.password_form.addEventListener("submit", savePassword);
  elements.close_admin_form.addEventListener("click", () => elements.admin_dialog.close());
  elements.cancel_admin_form.addEventListener("click", () => elements.admin_dialog.close());
  elements.close_password_form.addEventListener("click", () => elements.password_dialog.close());
  elements.cancel_password_form.addEventListener("click", () => elements.password_dialog.close());
  elements.logout_button.addEventListener("click", logout);
  elements.close_status_dialog.addEventListener("click", closeStatusDialog);
  elements.cancel_status.addEventListener("click", closeStatusDialog);
  elements.confirm_status.addEventListener("click", confirmStatusChange);
  document.addEventListener("click", event => {
    const button = event.target.closest("[data-toggle-password]");
    if (button) togglePassword(button);
  });
}

async function loadAdministradores() {
  setListState("loading");
  try {
    const response = await fetch("/api/administradores", { headers: { Accept: "application/json" } });
    if (response.status === 401) return redirectToLogin();
    if (!response.ok) throw new Error();
    state.administradores = await response.json();
    renderAdministradores();
  } catch {
    setListState("error");
  }
}

function renderAdministradores() {
  elements.admin_body.replaceChildren();
  elements.admin_count.textContent = `${state.administradores.length} ${state.administradores.length === 1 ? "administrador" : "administradores"}`;
  for (const administrador of state.administradores) {
    const row = document.createElement("tr");
    if (!administrador.ativo) row.className = "inactive";
    row.append(
      cell(administrador.nome),
      cell(administrador.login),
      statusCell(administrador.ativo),
      cell(formatDateTime(administrador.criadoEm)),
      cell(formatDateTime(administrador.atualizadoEm)),
      actionCell(administrador)
    );
    elements.admin_body.append(row);
  }
  setListState(state.administradores.length ? "table" : "empty");
}

function cell(value) {
  const td = document.createElement("td");
  td.textContent = value;
  return td;
}

function statusCell(active) {
  const td = document.createElement("td");
  const badge = document.createElement("span");
  badge.className = `badge${active ? "" : " inactive"}`;
  badge.textContent = active ? "Ativo" : "Inativo";
  td.append(badge);
  return td;
}

function actionCell(administrador) {
  const td = document.createElement("td");
  td.className = "actions-column";
  const actions = document.createElement("div");
  actions.className = "row-actions";
  actions.append(
    actionButton("Editar", () => openEdit(administrador)),
    actionButton("Redefinir senha", () => openPassword(administrador)),
    administrador.ativo
      ? actionButton("Inativar", () => requestStatusChange(administrador, false), "danger-action", administrador.id === state.usuarioAtualId)
      : actionButton("Reativar", () => changeStatus(administrador, true))
  );
  td.append(actions);
  return td;
}

function actionButton(label, callback, extraClass = "", disabled = false) {
  const button = document.createElement("button");
  button.type = "button";
  button.className = `button secondary ${extraClass}`.trim();
  button.textContent = label;
  button.disabled = disabled;
  if (disabled) button.title = "Você não pode inativar a própria conta.";
  button.addEventListener("click", callback);
  return button;
}

function openCreate() {
  elements.admin_form.reset();
  clearErrors(elements.admin_form, elements.admin_form_error);
  elements.admin_form.elements.id.value = "";
  elements.admin_form.elements.versao.value = "";
  elements.admin_form_title.textContent = "Novo administrador";
  elements.create_password_fields.hidden = false;
  elements.admin_form.elements.senha.required = true;
  elements.admin_form.elements.confirmacaoSenha.required = true;
  elements.save_admin.textContent = "Criar administrador";
  elements.admin_dialog.showModal();
  elements.admin_form.elements.nome.focus();
}

function openEdit(administrador) {
  elements.admin_form.reset();
  clearErrors(elements.admin_form, elements.admin_form_error);
  elements.admin_form.elements.id.value = administrador.id;
  elements.admin_form.elements.versao.value = administrador.versao;
  elements.admin_form.elements.nome.value = administrador.nome;
  elements.admin_form.elements.login.value = administrador.login;
  elements.admin_form_title.textContent = "Editar administrador";
  elements.create_password_fields.hidden = true;
  elements.admin_form.elements.senha.required = false;
  elements.admin_form.elements.confirmacaoSenha.required = false;
  elements.save_admin.textContent = "Salvar alterações";
  elements.admin_dialog.showModal();
  elements.admin_form.elements.nome.focus();
}

function openPassword(administrador) {
  elements.password_form.reset();
  clearErrors(elements.password_form, elements.password_form_error);
  elements.password_form.elements.id.value = administrador.id;
  elements.password_form.elements.versao.value = administrador.versao;
  elements.password_admin_name.textContent = `${administrador.nome} (${administrador.login})`;
  elements.password_dialog.showModal();
  elements.password_form.elements.novaSenha.focus();
}

async function saveAdministrador(event) {
  event.preventDefault();
  const form = elements.admin_form;
  clearErrors(form, elements.admin_form_error);
  const identity = validateIdentity(form.elements.nome.value, form.elements.login.value);
  const isCreate = !form.elements.id.value;
  const errors = { ...identity.errors };
  if (isCreate) Object.assign(errors, validatePassword(form.elements.senha.value, form.elements.confirmacaoSenha.value));
  if (showErrors(form, errors)) return;

  const payload = isCreate
    ? { nome: identity.nome, login: identity.login, senha: form.elements.senha.value, confirmacaoSenha: form.elements.confirmacaoSenha.value }
    : { nome: identity.nome, login: identity.login, versao: Number(form.elements.versao.value) };
  const url = isCreate ? "/api/administradores" : `/api/administradores/${form.elements.id.value}`;
  const method = isCreate ? "POST" : "PUT";
  await submit(elements.save_admin, async () => {
    const response = await api(url, method, payload);
    if (!response.ok) return handleFormError(response, form, elements.admin_form_error);
    const reauthentication = response.headers.get("X-Reauthentication-Required") === "true";
    if (!isCreate && Number(form.elements.id.value) === state.usuarioAtualId) {
      elements.session_name.textContent = identity.nome;
    }
    elements.admin_dialog.close();
    showToast(isCreate ? "Administrador criado." : "Administrador atualizado.");
    if (reauthentication) return relogin("Seu login foi alterado. Entre novamente com o novo login.");
    await loadAdministradores();
  });
}

async function savePassword(event) {
  event.preventDefault();
  const form = elements.password_form;
  clearErrors(form, elements.password_form_error);
  const errors = validatePassword(form.elements.novaSenha.value, form.elements.confirmacaoSenha.value);
  if (showErrors(form, errors)) return;
  await submit(elements.save_password, async () => {
    const response = await api(`/api/administradores/${form.elements.id.value}/redefinir-senha`, "POST", {
      novaSenha: form.elements.novaSenha.value,
      confirmacaoSenha: form.elements.confirmacaoSenha.value,
      versao: Number(form.elements.versao.value)
    });
    if (!response.ok) return handleFormError(response, form, elements.password_form_error);
    const reauthentication = response.headers.get("X-Reauthentication-Required") === "true";
    form.reset();
    elements.password_dialog.close();
    showToast("Senha redefinida e sessões anteriores encerradas.");
    if (reauthentication) return relogin("Sua senha foi alterada. Entre novamente com a nova senha.");
    await loadAdministradores();
  });
}

async function changeStatus(administrador, active) {
  const action = active ? "reativar" : "inativar";
  const response = await api(`/api/administradores/${administrador.id}/${action}`, "PATCH", { versao: administrador.versao });
  if (!response.ok) {
    const error = await readError(response);
    showToast(error.message, true);
    if (response.status === 409) await loadAdministradores();
    return;
  }
  showToast(active ? "Administrador reativado." : "Administrador inativado.");
  await loadAdministradores();
}

function requestStatusChange(administrador, active) {
  state.pendingStatus = { administrador, active };
  elements.status_message.textContent = `Inativar ${administrador.nome}? Esse administrador não poderá entrar no sistema.`;
  elements.status_dialog.showModal();
  elements.confirm_status.focus();
}

async function confirmStatusChange() {
  const pending = state.pendingStatus;
  closeStatusDialog();
  if (pending) await changeStatus(pending.administrador, pending.active);
}

function closeStatusDialog() {
  state.pendingStatus = null;
  if (elements.status_dialog.open) elements.status_dialog.close();
}

async function api(url, method, body) {
  const response = await fetch(url, {
    method,
    headers: { "Content-Type": "application/json", Accept: "application/json", "X-CSRF-TOKEN": state.csrf },
    body: JSON.stringify(body)
  });
  if (response.status === 401) redirectToLogin();
  return response;
}

async function handleFormError(response, form, messageElement) {
  const error = await readError(response);
  showErrors(form, Object.fromEntries(Object.entries(error.errors ?? {}).map(([key, value]) => [key, Array.isArray(value) ? value[0] : value])));
  messageElement.textContent = error.message;
  if (response.status === 409) await loadAdministradores();
}

async function readError(response) {
  const payload = await response.json().catch(() => null);
  return { message: payload?.message || payload?.title || "Não foi possível concluir a operação.", errors: payload?.errors };
}

function showErrors(form, errors) {
  for (const [field, message] of Object.entries(errors)) {
    const target = form.querySelector(`[data-error-for="${field}"]`);
    if (target) target.textContent = message;
    const inputName = field === "novaSenha" && form === elements.admin_form ? "senha" : field;
    const input = form.elements[inputName];
    if (input) input.setAttribute("aria-invalid", "true");
  }
  const first = form.querySelector('[aria-invalid="true"]');
  first?.focus();
  return Boolean(first);
}

function clearErrors(form, messageElement) {
  messageElement.textContent = "";
  form.querySelectorAll("[data-error-for]").forEach(element => { element.textContent = ""; });
  form.querySelectorAll('[aria-invalid="true"]').forEach(element => element.removeAttribute("aria-invalid"));
}

async function submit(button, operation) {
  const original = button.textContent;
  button.disabled = true;
  button.textContent = "Salvando...";
  try { await operation(); }
  catch { showToast("Não foi possível conectar ao sistema.", true); }
  finally { button.disabled = false; button.textContent = original; }
}

function togglePassword(button) {
  const input = button.closest("form").elements[button.dataset.togglePassword];
  const showing = input.type === "text";
  input.type = showing ? "password" : "text";
  button.textContent = showing ? "Mostrar" : "Ocultar";
  button.setAttribute("aria-label", showing ? "Mostrar senha" : "Ocultar senha");
}

function setListState(stateName) {
  elements.admin_loading.hidden = stateName !== "loading";
  elements.admin_error.hidden = stateName !== "error";
  elements.admin_empty.hidden = stateName !== "empty";
  elements.admin_table_wrap.hidden = stateName !== "table";
}

function showToast(message, isError = false) {
  const toast = document.createElement("div");
  toast.className = `toast t-toast is-open${isError ? " error" : ""}`;
  toast.textContent = message;
  elements.toast_stack.append(toast);
  setTimeout(() => toast.remove(), 3500);
}

async function logout() {
  await fetch("/api/auth/logout", { method: "POST" });
  redirectToLogin();
}

function relogin(message) {
  sessionStorage.setItem("lavanderia-login-message", message);
  setTimeout(redirectToLogin, 700);
}

function redirectToLogin() {
  window.location.replace("/login?ReturnUrl=/administradores");
}
