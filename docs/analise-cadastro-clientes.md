# Análise do Cadastro de Clientes

Data da análise: 13/07/2026.

## Escopo e proteção dos dados

A análise foi executada em modo somente leitura sobre `Cadastro Clientes.xlsx`. O hash SHA-256 foi calculado antes e depois da leitura e permaneceu idêntico. Este relatório contém somente métricas agregadas; não reproduz documentos, telefones, e-mails ou endereços reais.

As duas abas representam a mesma entidade de negócio, `Cliente`. Os nomes das abas não devem originar categorias, tabelas, telas ou regras distintas.

## Abas, linhas e cabeçalhos

| Aba | Linhas da planilha | Linhas de dados | Colunas |
|---|---:|---:|---:|
| Uniformes e hoteis | 24 | 23 | 21 |
| Consumidor final | 333 | 332 | 17 |
| **Total** | **357** | **355** | — |

### Uniformes e hoteis

Cabeçalhos: `Código`, `Boleto`, `Vencimento`, `Envio`, `Nome`, `CNPJ ou CPF`, `Produtor Rural`, `I.E.`, `I.M.`, `Início`, `Logradouro`, `Nº`, `Bairro`, `Cidade`, `UF`, `CEP`, `Telefone`, `E-mail Envio`, `E-mail NF`, `Última`, `Próximo`.

Tipos aparentes:

- `Código` é numérico.
- `Boleto`, `Envio`, `Nome`, `CNPJ ou CPF`, `Produtor Rural`, endereço, UF, CEP, telefone e e-mails aparecem principalmente como texto.
- `Vencimento` mistura texto e número.
- `I.E.` é predominantemente numérica; `I.M.` mistura números, texto e vazios. Ambas devem ser tratadas como identificadores textuais.
- `Início`, `Última` e `Próximo` aparecem como números internos do Excel nas células preenchidas.
- `Nº` mistura texto e número.

### Consumidor final

Cabeçalhos: `Boleto`, `Vencimento`, `Envio`, `Nome`, `CNPJ ou CPF`, `I.E.`, `I.M.`, `CEP`, `Logradouro`, `Nº`, `Complemento`, `Bairro`, `Cidade`, `UF`, `Telefone`, `E-mail Envio`, `E-mail NF`.

Tipos aparentes:

- `Nome`, documento e campos de endereço são majoritariamente texto.
- Um documento e a maior parte dos telefones estão armazenados como números, o que exige conversão cuidadosa para texto.
- `I.E.` e `I.M.` misturam números, texto e vazios.
- `Nº` mistura número, texto e vazio.
- `Boleto`, `Vencimento` e `Envio` estão vazios nas 332 linhas desta aba.

## Campos comuns e exclusivos

Campos comuns: `Boleto`, `Vencimento`, `Envio`, `Nome`, `CNPJ ou CPF`, `I.E.`, `I.M.`, `Logradouro`, `Nº`, `Bairro`, `Cidade`, `UF`, `CEP`, `Telefone`, `E-mail Envio` e `E-mail NF`.

Campos exclusivos:

- `Uniformes e hoteis`: `Código`, `Produtor Rural`, `Início`, `Última` e `Próximo`.
- `Consumidor final`: `Complemento`.

Exclusividade de coluna não implica diferença de entidade. Quando uma coluna não existir na aba de origem, o campo correspondente será considerado ausente.

## Qualidade dos dados

### Documentos

Após remover pontuação apenas para a análise:

| Verificação | Quantidade |
|---|---:|
| Aparência de CPF (11 dígitos) | 314 |
| Aparência de CNPJ (14 dígitos) | 40 |
| Documentos inválidos | 2 |
| Documento com tamanho inesperado | 1 |
| Documento com 11/14 dígitos e dígito verificador inválido | 1 |
| Grupos de documentos duplicados | 6 |
| Ocorrências nos grupos duplicados | 12 |
| Linhas excedentes causadas pelas duplicidades | 6 |
| Linhas sem documento | 0 |

Os exemplos devem permanecer mascarados na futura pré-visualização, como `***.***.***-**` e `**.***.***/****-**`. A regra de unicidade deve considerar o valor normalizado e somente clientes ativos.

### Nome

- Linhas sem nome: **0**.
- Foram detectadas células de nome com espaços externos ou tabulações; o valor deverá passar por `Trim` e normalização de espaços antes da validação.

### E-mails

- Foram encontradas **2 células** com múltiplos e-mails separados por ponto e vírgula.
- `E-mail Envio` é um campo válido e não deve ser confundido com a coluna ignorada `Envio`.
- Cada endereço deverá ser separado, aparado e validado individualmente na pré-visualização. O armazenamento em um único campo deverá usar `;` como separador canônico enquanto não houver uma tabela própria de contatos.

### Telefones

| Aba | Situação | Quantidade |
|---|---|---:|
| Uniformes e hoteis | Com máscara de parênteses e hífen | 22 |
| Uniformes e hoteis | Vazio | 1 |
| Consumidor final | Número do Excel | 312 |
| Consumidor final | Texto somente com dígitos | 1 |
| Consumidor final | Vazio | 19 |

Nos valores preenchidos, há comprimentos de 10 e 11 dígitos. Números armazenados numericamente podem perder zeros à esquerda; a futura importação deverá convertê-los para texto, preservar apenas dígitos e validar DDD/comprimento sem inventar dados ausentes.

### CEPs

| Aba | Situação | Quantidade |
|---|---|---:|
| Uniformes e hoteis | Oito dígitos, com formatos variados | 23 |
| Consumidor final | Formato `00000-000` | 318 |
| Consumidor final | Outro formato, ainda com oito dígitos | 3 |
| Consumidor final | Vazio | 11 |

Todos os CEPs preenchidos possuem oito dígitos após a retirada da pontuação. O armazenamento proposto é somente com dígitos.

### Datas e números internos do Excel

- `Início`: 21 números internos do Excel e 2 vazios.
- `Última`: 16 números internos do Excel e 7 vazios.
- `Próximo`: 17 números internos do Excel e 6 vazios.
- `Vencimento` na primeira aba: 8 números e 15 textos/marcadores; na segunda aba está vazio.

Somente `Início` pertence ao modelo preliminar de cliente. `Última` e `Próximo` pertencem ao futuro contexto de atendimentos/serviços. `Vencimento` será ignorado. A futura leitura deve usar o estilo/formato da célula e o calendário do arquivo para converter seriais Excel, apresentando alerta quando o valor não for uma data válida.

### Espaços e tabulações

Foram identificadas **52 células** com espaços externos e/ou tabulações, incluindo **12 células com tabulação**. As ocorrências atingem, entre outros, nome, documento, CEP, logradouro, complemento, bairro, cidade, UF, telefone, boleto e e-mail de envio.

A normalização futura deve:

1. remover espaços externos;
2. substituir tabulações e quebras internas indevidas por espaço;
3. reduzir espaços consecutivos sem alterar conteúdo significativo;
4. aplicar maiúsculas apenas a campos canônicos, como UF;
5. nunca registrar o valor pessoal original em logs.

## Colunas excluídas do cadastro/importação

Ignorar completamente:

- `Boleto`;
- `Vencimento`;
- `Envio`.

Não associar diretamente a `Cliente`:

- `Última`;
- `Próximo`.

Manter como campos válidos:

- `E-mail Envio`;
- `E-mail NF`.

## Mapeamento proposto

| Origem | Campo futuro | Transformação |
|---|---|---|
| Código | CodigoLegado | Converter para texto sem formatação numérica |
| Nome | NomeRazaoSocial | Aparar e normalizar espaços |
| CNPJ ou CPF | Documento | Manter somente dígitos; validar CPF/CNPJ |
| Derivado do Documento | TipoPessoa | 11 dígitos = PF; 14 dígitos = PJ |
| Produtor Rural | ProdutorRural | Normalizar Sim/Não para booleano |
| I.E. | InscricaoEstadual | Converter para texto, sem validação estadual nesta etapa |
| I.M. | InscricaoMunicipal | Converter para texto |
| Início | DataInicioRelacionamento | Converter serial/data Excel para data |
| CEP | Cep | Manter somente oito dígitos |
| Logradouro | Logradouro | Aparar e normalizar espaços |
| Nº | Numero | Converter para texto |
| Complemento | Complemento | Aparar e normalizar espaços |
| Bairro | Bairro | Aparar e normalizar espaços |
| Cidade | Cidade | Aparar e normalizar espaços |
| UF | Uf | Aparar e converter para duas letras maiúsculas |
| Telefone | Telefone | Converter para texto e manter somente dígitos |
| E-mail Envio | EmailEnvio | Separar por `;`, aparar e validar cada item |
| E-mail NF | EmailNotaFiscal | Separar por `;`, aparar e validar cada item |
| Sem origem direta | Observacoes | Opcional; não concatenar colunas ignoradas automaticamente |
| Sem origem direta | Ativo | Valor inicial `true` após confirmação da importação |
| Gerado pelo sistema | Id, CriadoEm, AtualizadoEm | Não importar da planilha |

## Modelo preliminar da entidade Cliente

Este é um modelo para validação; nenhuma entidade definitiva ou migration foi criada nesta etapa.

| Campo | Tipo .NET / SQL sugerido | Tamanho | Obrigatório | Normalização / padrão |
|---|---|---:|:---:|---|
| Id | `int` / `int` identity | — | Sim | Gerado pelo banco |
| CodigoLegado | `string` / `nvarchar` | 30 | Não | Texto aparado; não assumir sequência global |
| NomeRazaoSocial | `string` / `nvarchar` | 200 | Sim | Trim e espaços internos normalizados |
| Documento | `string` / `varchar` | 14 | Sim | Somente dígitos; CPF/CNPJ e dígitos verificadores válidos |
| TipoPessoa | `enum` / `char` | 2 | Sim | Derivado automaticamente: `PF` ou `PJ` |
| ProdutorRural | `bool` / `bit` | — | Sim | Padrão `false`; converter Sim/Não |
| InscricaoEstadual | `string` / `nvarchar` | 30 | Não | Texto aparado; preservar zeros |
| InscricaoMunicipal | `string` / `nvarchar` | 30 | Não | Texto aparado; preservar zeros |
| DataInicioRelacionamento | `DateOnly?` / `date` | — | Não | Converter data/serial Excel |
| Cep | `string` / `char` | 8 | Não | Somente dígitos |
| Logradouro | `string` / `nvarchar` | 200 | Não | Trim e espaços normalizados |
| Numero | `string` / `nvarchar` | 20 | Não | Texto; preservar `S/N` e complementos legítimos do número |
| Complemento | `string` / `nvarchar` | 100 | Não | Trim e espaços normalizados |
| Bairro | `string` / `nvarchar` | 100 | Não | Trim e espaços normalizados |
| Cidade | `string` / `nvarchar` | 100 | Não | Trim e espaços normalizados |
| Uf | `string` / `char` | 2 | Não | Duas letras maiúsculas |
| Telefone | `string` / `varchar` | 20 | Não | Somente dígitos; incluir código do país apenas quando informado |
| EmailEnvio | `string` / `nvarchar` | 500 | Não | Lista validada com separador `;` |
| EmailNotaFiscal | `string` / `nvarchar` | 500 | Não | Lista validada com separador `;` |
| Observacoes | `string` / `nvarchar` | 1000 | Não | Trim; sem dados derivados de colunas ignoradas |
| Ativo | `bool` / `bit` | — | Sim | Padrão `true` |
| CriadoEm | `DateTimeOffset` / `datetimeoffset(0)` | — | Sim | UTC, gerado pelo sistema |
| AtualizadoEm | `DateTimeOffset?` / `datetimeoffset(0)` | — | Não | UTC, alterado pelo sistema |

Índices recomendados:

- índice único filtrado em `Documento` para registros ativos: unicidade entre clientes ativos;
- índice em `NomeRazaoSocial` para busca e ordenação;
- índice não único e filtrado em `CodigoLegado` quando não nulo;
- índice composto em `Ativo, NomeRazaoSocial` se a listagem padrão filtrar ativos.

Não criar campo de categoria de cliente.

## Alertas da futura pré-visualização

A pré-visualização deverá bloquear ou destacar, por linha:

- documento vazio, com tamanho inesperado ou dígitos verificadores inválidos;
- documento duplicado dentro do arquivo ou já existente entre clientes ativos;
- conflito entre tamanho do documento e `TipoPessoa` derivado;
- nome vazio após normalização;
- telefone numérico, com possível perda de zero, ou com tamanho inesperado;
- CEP vazio ou fora de oito dígitos;
- UF fora do padrão de duas letras;
- data Excel inválida, ambígua ou texto não conversível;
- inscrição recebida como número e sujeita à perda de zeros;
- múltiplos e-mails e endereço individual inválido;
- conteúdo com tabulação, espaços externos ou comprimento maior que o modelo;
- campos de uma aba ausentes na outra, sem tratá-los como erro quando opcionais;
- colunas desconhecidas ou mudança nos cabeçalhos esperados;
- confirmação explícita de que `Boleto`, `Vencimento`, `Envio`, `Última` e `Próximo` não serão gravados em `Clientes`.

Nenhuma importação foi executada nesta etapa.
