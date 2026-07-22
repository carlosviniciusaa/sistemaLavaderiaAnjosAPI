export function hasValue(value) {
  return value !== null && value !== undefined && String(value).trim() !== "";
}

export async function copyText(value, environment = globalThis) {
  const text = String(value ?? "").trim();
  if (!text) throw new Error("Não há valor para copiar.");

  const clipboard = environment.navigator?.clipboard;
  if (clipboard?.writeText) {
    try {
      await clipboard.writeText(text);
      return "clipboard";
    } catch {
      // Em HTTP na rede local, o navegador pode expor a API e ainda negar a operação.
    }
  }

  const documentRef = environment.document;
  if (!documentRef?.body || typeof documentRef.execCommand !== "function") {
    throw new Error("A cópia não está disponível neste navegador.");
  }

  const textarea = documentRef.createElement("textarea");
  textarea.value = text;
  textarea.setAttribute("readonly", "");
  Object.assign(textarea.style, {
    position: "fixed",
    left: "-9999px",
    top: "0",
    opacity: "0"
  });

  documentRef.body.appendChild(textarea);
  try {
    textarea.focus();
    textarea.select();
    textarea.setSelectionRange?.(0, textarea.value.length);
    if (!documentRef.execCommand("copy")) throw new Error("O navegador recusou a cópia.");
    return "fallback";
  } finally {
    textarea.remove();
  }
}

export async function copyWithFeedback(value, setFeedback, environment = globalThis) {
  const method = await copyText(value, environment);
  setFeedback("Copiado!");
  return method;
}

export function buildFullAddress(cliente, formatCep = value => value) {
  const cityUf = [cliente.cidade, cliente.uf].filter(hasValue).join("/");
  const parts = [
    cliente.logradouro,
    cliente.numero,
    cliente.complemento,
    cliente.bairro,
    cityUf,
    hasValue(cliente.cep) ? `CEP ${formatCep(cliente.cep)}` : null
  ];
  return parts.filter(hasValue).map(value => String(value).trim()).join(", ");
}

export function buildAllData(cliente, formatted = {}) {
  const fields = [
    ["Nome ou razão social", cliente.nomeRazaoSocial],
    ["CPF ou CNPJ", formatted.documento ?? cliente.documento],
    ["Tipo de pessoa", formatted.tipoPessoa],
    ["Código legado", cliente.codigoLegado],
    ["Produtor rural", cliente.produtorRural ? "Sim" : null],
    ["Inscrição estadual", cliente.inscricaoEstadual],
    ["Inscrição municipal", cliente.inscricaoMunicipal],
    ["Início do relacionamento", formatted.dataInicioRelacionamento],
    ["Endereço", formatted.endereco],
    ["Telefone", formatted.telefone ?? cliente.telefone],
    ["E-mail de contato", cliente.emailEnvio],
    ["E-mail para nota fiscal", cliente.emailNotaFiscal],
    ["Observações", cliente.observacoes]
  ];
  return fields.filter(([, value]) => hasValue(value)).map(([label, value]) => `${label}: ${String(value).trim()}`).join("\n");
}
