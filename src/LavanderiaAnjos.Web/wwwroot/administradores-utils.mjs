export function normalizeLogin(value) {
  return String(value ?? "").trim().toLowerCase();
}

export function validateIdentity(nome, login) {
  const errors = {};
  const normalizedName = String(nome ?? "").trim();
  const normalizedLogin = normalizeLogin(login);
  if (!normalizedName) errors.nome = "Informe o nome do administrador.";
  else if (normalizedName.length > 150) errors.nome = "O nome deve ter no máximo 150 caracteres.";
  if (!normalizedLogin) errors.login = "Informe o login.";
  else if (normalizedLogin.length < 3) errors.login = "O login deve ter pelo menos 3 caracteres.";
  else if (normalizedLogin.length > 100) errors.login = "O login deve ter no máximo 100 caracteres.";
  else if (!/^[a-z0-9._-]+$/.test(normalizedLogin)) errors.login = "Use apenas letras, números, ponto, hífen e sublinhado, sem espaços.";
  return { nome: normalizedName, login: normalizedLogin, errors };
}

export function validatePassword(password, confirmation) {
  const errors = {};
  const value = String(password ?? "");
  if (!value) errors.novaSenha = "Informe a senha.";
  else if (value.length < 8) errors.novaSenha = "A senha deve ter pelo menos 8 caracteres.";
  else if (value.length > 256) errors.novaSenha = "A senha deve ter no máximo 256 caracteres.";
  else if (!/\p{L}/u.test(value)) errors.novaSenha = "A senha deve conter pelo menos uma letra.";
  else if (!/\d/.test(value)) errors.novaSenha = "A senha deve conter pelo menos um número.";
  if (value !== String(confirmation ?? "")) errors.confirmacaoSenha = "A confirmação da senha não confere.";
  return errors;
}

export function formatDateTime(value) {
  if (!value) return "—";
  return new Intl.DateTimeFormat("pt-BR", { dateStyle: "short", timeStyle: "short" }).format(new Date(value));
}
