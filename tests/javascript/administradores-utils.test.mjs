import test from "node:test";
import assert from "node:assert/strict";
import { normalizeLogin, validateIdentity, validatePassword } from "../../src/LavanderiaAnjos.Web/wwwroot/administradores-utils.mjs";

test("normaliza login removendo espaços externos e caixa", () => {
  assert.equal(normalizeLogin("  Admin.Teste  "), "admin.teste");
});

test("valida nome e formato de login", () => {
  assert.equal(validateIdentity("  Maria Silva ", " Maria_01 ").nome, "Maria Silva");
  assert.ok(validateIdentity("Maria", "ab").errors.login);
  assert.ok(validateIdentity("Maria", "maria silva").errors.login);
  assert.equal(Object.keys(validateIdentity("Maria", "maria.silva-01").errors).length, 0);
});

test("valida requisitos e confirmação da senha", () => {
  assert.ok(validatePassword("curta1", "curta1").novaSenha);
  assert.ok(validatePassword("12345678", "12345678").novaSenha);
  assert.ok(validatePassword("abcdefgh", "abcdefgh").novaSenha);
  assert.ok(validatePassword("Senha123", "outra").confirmacaoSenha);
  assert.deepEqual(validatePassword("Senha123!", "Senha123!"), {});
});
