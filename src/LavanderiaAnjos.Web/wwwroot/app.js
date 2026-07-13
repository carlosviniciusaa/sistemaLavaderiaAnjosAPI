const card = document.querySelector(".health-card");
const title = document.querySelector("#health-title");
const detail = document.querySelector("#health-detail");
const refreshButton = document.querySelector("#health-refresh");

async function refreshHealth() {
  card.classList.remove("healthy", "unhealthy");
  title.textContent = "Verificando serviços…";
  detail.textContent = "Aplicação e banco de dados";
  refreshButton.disabled = true;

  try {
    const response = await fetch("/health", { headers: { Accept: "application/json" } });
    const health = await response.json();
    const healthy = response.ok && health.status === "Healthy" && health.database === "Healthy";

    card.classList.add(healthy ? "healthy" : "unhealthy");
    title.textContent = healthy ? "Serviços disponíveis" : "Banco indisponível";
    detail.textContent = healthy
      ? "Aplicação e banco responderam normalmente"
      : "A aplicação está ativa, mas o banco requer verificação";
  } catch {
    card.classList.add("unhealthy");
    title.textContent = "Aplicação indisponível";
    detail.textContent = "Não foi possível concluir a verificação";
  } finally {
    refreshButton.disabled = false;
  }
}

refreshButton.addEventListener("click", refreshHealth);
refreshHealth();
