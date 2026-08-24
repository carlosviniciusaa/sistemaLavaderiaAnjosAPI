# Identidade visual oficial

## Arquivos-fonte

Os desenhos originais permanecem em `icons`:

- `LOGO_SISTEMA_SisLavColorido.svg`: gradiente azul com símbolo amarelo;
- `LOGO_SISTEMA_SisLavMonocromático.svg`: desenho de uma única cor, controlada pelo contexto.

Ambos usam `viewBox="0 0 160 160"`, fundo transparente e não definem dimensões fixas.
Não contêm JavaScript, imagens embutidas, links ou referências remotas. As cópias para
publicação estão em `wwwroot/assets/brand` e não alteram o desenho.

## Uso

- colorido: cabeçalhos, login, favicon, Apple Touch Icon, manifesto e executável Windows;
- monocromático: `mask-icon`, no qual o navegador aplica a cor declarada pela página.

Os PNGs transparentes de 16, 32, 48, 180, 192 e 512 pixels são derivados diretamente
do SVG colorido. O `favicon.ico` contém 16, 32, 48, 64, 128 e 256 pixels. O manifesto é
somente de identidade e instalação na tela inicial; nenhum service worker foi criado.

## Cache e acessibilidade

Os novos assets e `styles.css` usam a versão `20260824-brand-icons`. As versões existentes
de `app.js` e `clipboard.mjs` permanecem inalteradas. Nos cabeçalhos e no login, o nome
Lavanderia Anjos já está visível, portanto as imagens são decorativas (`alt=""` e
`aria-hidden="true"`) para não duplicar a leitura por tecnologia assistiva.

## Validação

Execute, antes da atualização:

```powershell
cd <pacote>\operations\tests
.\Test-PackagedBranding.ps1 -PackagePath <pacote>
.\Test-PackagedCopyWiring.ps1 -PackagePath <pacote>
```
