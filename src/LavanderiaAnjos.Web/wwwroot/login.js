const form = document.querySelector("#login-form");
const error = document.querySelector("#login-error");
const requestedReturnUrl = new URLSearchParams(window.location.search).get("ReturnUrl");
const destination = requestedReturnUrl?.startsWith("/") && !requestedReturnUrl.startsWith("//")
  ? requestedReturnUrl
  : "/clientes";
const sessionMessage = sessionStorage.getItem("lavanderia-login-message");
if (sessionMessage) {
  error.textContent = sessionMessage;
  sessionStorage.removeItem("lavanderia-login-message");
}

checkSession();
form.addEventListener("submit", login);

async function checkSession() {
  const response = await fetch("/api/auth/me", { headers: { Accept: "application/json" } });
  if (response.ok) window.location.replace(destination);
}

async function login(event) {
  event.preventDefault();
  error.textContent = "";
  if (!form.reportValidity()) return;

  const button = form.querySelector("button[type='submit']");
  const original = button.textContent;
  button.disabled = true;
  button.textContent = "Entrando...";

  try {
    const response = await fetch("/api/auth/login", {
      method: "POST",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify({
        login: form.elements.login.value.trim(),
        password: form.elements.password.value
      })
    });

    if (response.ok) {
      window.location.replace(destination);
      return;
    }

    error.textContent = response.status === 401
      ? "Login ou senha inválidos."
      : "Não foi possível entrar. Tente novamente.";
    form.elements.password.focus();
  } catch {
    error.textContent = "Não foi possível conectar ao sistema.";
  } finally {
    button.disabled = false;
    button.textContent = original;
  }
}
