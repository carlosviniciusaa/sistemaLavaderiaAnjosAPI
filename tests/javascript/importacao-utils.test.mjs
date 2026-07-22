import test from "node:test";
import assert from "node:assert/strict";
import { canConfirm, confirmationButtonText, formatApiError, MAX_XLSX_BYTES, validateSpreadsheetSelection } from "../../src/LavanderiaAnjos.Web/wwwroot/importacao-utils.mjs";

test("aceita somente arquivo xlsx não vazio dentro do limite", () => {
  assert.equal(validateSpreadsheetSelection({ name: "Cadastro Clientes.xlsx", size: 149_598 }), null);
  assert.match(validateSpreadsheetSelection({ name: "clientes.xls", size: 10 }), /\.xlsx/);
  assert.match(validateSpreadsheetSelection({ name: "clientes.xlsx", size: MAX_XLSX_BYTES + 1 }), /10 MB/);
  assert.match(validateSpreadsheetSelection({ name: "clientes.xlsx", size: 0 }), /10 MB/);
});

test("confirmação exige seleção e informa a quantidade", () => {
  assert.equal(canConfirm({ quantidadeSelecionada: 2 }), true);
  assert.equal(canConfirm({ quantidadeSelecionada: 0 }), false);
  assert.equal(confirmationButtonText(2), "Importar 2 clientes");
  assert.equal(confirmationButtonText(-1), "Importar 0 clientes");
});

test("erro usa mensagem de domínio e acrescenta o código de correlação", () => {
  assert.equal(formatApiError({ message: "Lote expirado." }), "Lote expirado.");
  assert.equal(
    formatApiError({ message: "Falha interna.", errorCode: "ABC123" }),
    "Falha interna. Código de erro: ABC123.");
  assert.equal(formatApiError(null), "Não foi possível concluir a operação.");
});
