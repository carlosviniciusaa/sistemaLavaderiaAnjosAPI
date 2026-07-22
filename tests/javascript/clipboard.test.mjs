import assert from "node:assert/strict";
import test from "node:test";
import { buildAllData, buildFullAddress, copyText, copyWithFeedback, hasValue } from "../../src/LavanderiaAnjos.Web/wwwroot/clipboard.mjs";

test("copia somente o valor com Clipboard API e informa Copiado", async () => {
  let copied;
  let feedback;
  const environment = { navigator: { clipboard: { writeText: async value => { copied = value; } } } };
  const method = await copyWithFeedback("529.982.247-25", value => { feedback = value; }, environment);
  assert.equal(method, "clipboard");
  assert.equal(copied, "529.982.247-25");
  assert.equal(feedback, "Copiado!");
  assert.equal(copied.includes("CPF ou CNPJ"), false);
});

test("usa fallback, seleciona o textarea e remove o elemento temporário", async () => {
  let selected = false;
  let removed = false;
  let copiedValue;
  const textarea = {
    value: "", style: {}, setAttribute() {}, focus() {},
    select() { selected = true; copiedValue = this.value; }, setSelectionRange() {}, remove() { removed = true; }
  };
  const environment = {
    navigator: {},
    document: { body: { appendChild() {} }, createElement: () => textarea, execCommand: command => command === "copy" }
  };
  const method = await copyText("valor individual", environment);
  assert.equal(method, "fallback");
  assert.equal(copiedValue, "valor individual");
  assert.equal(selected, true);
  assert.equal(removed, true);
});

test("faz fallback quando Clipboard API rejeita", async () => {
  const textarea = { value: "", style: {}, setAttribute() {}, focus() {}, select() {}, setSelectionRange() {}, remove() {} };
  const environment = {
    navigator: { clipboard: { writeText: async () => { throw new Error("HTTP inseguro"); } } },
    document: { body: { appendChild() {} }, createElement: () => textarea, execCommand: () => true }
  };
  assert.equal(await copyText("teste", environment), "fallback");
});

test("campo vazio não é copiável", async () => {
  assert.equal(hasValue("  "), false);
  await assert.rejects(copyText("", {}), /Não há valor/);
});

test("monta endereço sem partes vazias nem vírgulas duplicadas", () => {
  const address = buildFullAddress({ logradouro: "Rua Exemplo", numero: "123", complemento: null, bairro: "Centro", cidade: "Bastos", uf: "SP", cep: "17690000" }, value => `${value.slice(0, 5)}-${value.slice(5)}`);
  assert.equal(address, "Rua Exemplo, 123, Centro, Bastos/SP, CEP 17690-000");
  assert.equal(address.includes(", ,"), false);
});

test("copiar todos inclui somente campos preenchidos", () => {
  const text = buildAllData({ nomeRazaoSocial: "Empresa Exemplo", documento: "11222333000181", produtorRural: false, inscricaoEstadual: null, emailNotaFiscal: "nf@exemplo.com" }, { documento: "11.222.333/0001-81", endereco: "" });
  assert.match(text, /Nome ou razão social: Empresa Exemplo/);
  assert.match(text, /CPF ou CNPJ: 11\.222\.333\/0001-81/);
  assert.match(text, /E-mail para nota fiscal: nf@exemplo\.com/);
  assert.doesNotMatch(text, /Inscrição estadual/);
  assert.doesNotMatch(text, /Endereço:/);
});
