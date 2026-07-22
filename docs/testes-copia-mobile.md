# Testes da cópia em celulares

## Pré-condições

- publique a release em uma máquina de homologação na mesma rede local;
- use um endereço HTTP por IP, por exemplo `http://192.168.x.x:5080`;
- use somente um cliente sintético aprovado para o teste;
- não altere o `SISTEMALAV` para executar este roteiro.

## Chrome Android

1. Abra a aplicação pelo IP HTTP, autentique-se e visualize o cliente sintético.
2. Confirme no console remoto que `window.isSecureContext` é `false` e que a tela continua funcional.
3. Teste os botões de nome, CPF/CNPJ, inscrições, CEP, endereço, telefone e e-mails.
4. Teste **Copiar endereço completo** e **Copiar todos os dados**.
5. Cole cada resultado em um campo local e confira formatação, acentos e quebras de linha.
6. Confirme `Copiado!`, o toast e a restauração do texto do botão.
7. Force `document.execCommand` a retornar `false` em homologação e confirme que a caixa manual abre com o conteúdo selecionado.

## Safari iOS

Repita o roteiro acima no Safari. Verifique especialmente a seleção por `textarea`, o
segundo fallback `contenteditable` e, se ambos forem recusados, o botão **Selecionar texto**.
Toque e segure o texto e escolha **Copiar**; confirme que fechar a caixa devolve o uso ao
modal de detalhes.

## Resultado esperado

A cópia ocorre inteiramente no navegador. Nenhuma requisição de rede é criada pelo clique,
nenhum formulário é enviado e a caixa manual não aparece quando um método automático funciona.

## Validação automatizada desta release

Em 22/07/2026, o defeito da release anterior foi reproduzido em Chromium real por HTTP
em `http://192.168.68.120:5100`. O teste abriu um `<dialog>` por `showModal()` e confirmou
`open === true` e `matches(':modal') === true`. Ao clicar em **Copiar nome** usando o módulo
anterior, o `textarea` temporário foi observado como filho de `body`, fora do diálogo.
`execCommand('copy')` retornou sucesso, mas a tentativa de colagem da automação encontrou
o clipboard virtual vazio.

Com a release `20260722-copy-modal-fix`, o mesmo clique manteve o `textarea` como filho de
`details-dialog`. O evento real `copy` foi recebido enquanto o elemento ativo também era
filho do diálogo e o texto selecionado observado foi exatamente `Cliente Modal Árvore`.
Nome, CPF/CNPJ, telefone, endereço completo e todos os dados foram exercitados por cliques
reais; todos usaram `fallback-textarea`, o host `details-dialog` e removeram o temporário.
O segundo fallback também foi exercitado em Chromium real e usou um `DIV` contenteditable
dentro do mesmo diálogo.

O controle de navegador usa um clipboard virtual próprio e não o sincroniza com gravações
feitas por `document.execCommand`; por isso ele bloqueou `Ctrl+V` mesmo depois de observar o
evento `copy` e a seleção correta. A colagem no clipboard do sistema continua sendo uma
etapa manual obrigatória em Chrome, Edge, Chrome Android e Safari iOS conforme o roteiro
acima.

### Por que os testes anteriores não detectaram o defeito

- `document.execCommand` estava mockado para devolver apenas `true` ou `false`;
- o documento e os elementos eram objetos falsos, sem top layer e sem `showModal()`;
- nenhum teste verificava o pai do elemento durante a cópia;
- sucesso era inferido exclusivamente do retorno booleano;
- não havia evento real de cópia nem tentativa de colagem.

Os testes atuais verificam o host durante `execCommand`, remoção, foco, múltiplos diálogos,
ausência de `:modal`, fallback sem modal e os dois tipos de elemento temporário. O harness
de navegador real está em `operations/tests/browser` no pacote.

Para servir o harness do pacote por HTTP na rede de homologação:

```powershell
cd <pacote>\operations\tests\browser
node .\serve-harness.mjs 5100 ..\..\..
```

Abra no Chromium:

```text
http://<ip-local>:5100/operations/tests/browser/clipboard-dialog-harness.html?module=/app/wwwroot/clipboard.mjs
```

Os testes incluídos no pacote podem ser repetidos antes da instalação:

```powershell
cd <pacote>\operations\tests
.\Test-PackagedCopyWiring.ps1 -PackagePath <pacote>
```
