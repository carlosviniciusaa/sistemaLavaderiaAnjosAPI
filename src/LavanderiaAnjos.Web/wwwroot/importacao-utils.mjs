export const MAX_XLSX_BYTES = 10 * 1024 * 1024;

export function validateSpreadsheetSelection(file) {
  if (!file || !file.name?.toLowerCase().endsWith(".xlsx")) return "Selecione um arquivo .xlsx.";
  if (file.size <= 0 || file.size > MAX_XLSX_BYTES) return "O arquivo deve ter até 10 MB.";
  return null;
}

export function confirmationButtonText(selectedCount) {
  return `Importar ${Math.max(0, Number(selectedCount) || 0)} clientes`;
}

export function canConfirm(summary) {
  return Boolean(summary && summary.quantidadeSelecionada > 0);
}

export function formatApiError(payload, fallback = "Não foi possível concluir a operação.") {
  const message = payload?.message || fallback;
  return payload?.errorCode ? `${message} Código de erro: ${payload.errorCode}.` : message;
}
