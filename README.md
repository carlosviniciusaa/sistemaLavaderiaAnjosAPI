# Lavanderia Anjos

Sistema web interno para cadastrar, consultar, pesquisar, editar, visualizar, inativar, reativar e fazer a importação inicial controlada de clientes. O frontend HTML/CSS/JavaScript é servido pela mesma aplicação ASP.NET Core e foi pensado para uso rápido na rede local, inclusive para copiar dados ao emissor de notas fiscais.

## Arquitetura e requisitos

- .NET 10, ASP.NET Core Minimal APIs e Entity Framework Core;
- Microsoft SQL Server 2022 Express;
- projetos `Domain`, `Application`, `Infrastructure`, `Web` e `Tests`;
- conexão configurada externamente em `ConnectionStrings:LavanderiaAnjos`;
- autenticação por cookie e senha armazenada somente como hash oficial do ASP.NET Core;
- nenhum `EnsureCreated`, credencial, cliente fictício ou planilha real versionado.

## Restaurar, compilar e testar

```powershell
dotnet restore .\LavanderiaAnjos.sln
dotnet build .\LavanderiaAnjos.sln --no-restore
dotnet test .\LavanderiaAnjos.sln --no-build
node --test .\tests\javascript\*.test.mjs
```

Os testes automáticos usam provedores isolados EF Core em memória e SQLite; os testes transacionais instrumentam uma estratégia de repetição sem acessar o banco real. A verificação `PlanilhaRealPreviewIntegrationTests` retorna sem acessar dados por padrão e só consulta o banco real quando executada isoladamente com `RUN_REAL_PREVIEW=1`; ela gera apenas a pré-visualização e comprova que nenhum cliente foi gravado.

## Configuração externa

A connection string de desenvolvimento permanece em .NET User Secrets, na chave `ConnectionStrings:LavanderiaAnjos`. Nunca use `dotnet user-secrets list` em terminal compartilhado nem copie a conexão para arquivos do repositório.

O primeiro administrador só é criado quando ainda não existe usuário e as três chaves abaixo estão preenchidas externamente:

```powershell
dotnet user-secrets set "BootstrapAdmin:Nome" "<nome escolhido>" --project .\src\LavanderiaAnjos.Web\LavanderiaAnjos.Web.csproj
dotnet user-secrets set "BootstrapAdmin:Login" "<login escolhido>" --project .\src\LavanderiaAnjos.Web\LavanderiaAnjos.Web.csproj
dotnet user-secrets set "BootstrapAdmin:Password" "<senha forte escolhida>" --project .\src\LavanderiaAnjos.Web\LavanderiaAnjos.Web.csproj
```

Não há usuário ou senha padrão. Em produção, o bootstrap deve permanecer ausente depois que o administrador existir. A implantação utiliza apenas a connection string externa do serviço.

Administradores autenticados podem gerenciar outros administradores em
`/administradores`. Não há exclusão física nem perfis distintos; login é
normalizado, senhas usam `PasswordHasher<Usuario>` e a inativação nunca pode deixar
o sistema sem administrador ativo.

## Banco e migrations

As migrations criam `Clientes`, `Usuarios`, índices, o histórico padrão do EF Core e o resumo de lotes em `ImportacoesClientes`. Os scripts idempotentes revisáveis estão em `scripts/sql/`; nenhum deles importa clientes.

```powershell
dotnet ef database update `
  --project .\src\LavanderiaAnjos.Infrastructure\LavanderiaAnjos.Infrastructure.csproj `
  --startup-project .\src\LavanderiaAnjos.Web\LavanderiaAnjos.Web.csproj
```

## Executar e verificar

```powershell
dotnet run --project .\src\LavanderiaAnjos.Web\LavanderiaAnjos.Web.csproj
Invoke-RestMethod http://localhost:5080/health
```

Rotas públicas: `/login`, arquivos estáticos necessários e `/health`. As telas `/clientes` e `/importacao`, a API de clientes e toda a API de importação exigem autenticação.

## API

- `POST /api/auth/login`
- `POST /api/auth/logout`
- `GET /api/auth/me`
- `GET /api/clientes`
- `GET /api/clientes/{id}`
- `POST /api/clientes`
- `PUT /api/clientes/{id}`
- `PATCH /api/clientes/{id}/inativar`
- `PATCH /api/clientes/{id}/reativar`
- `GET /api/importacoes/clientes/antiforgery`
- `POST /api/importacoes/clientes/previsualizar`
- `GET /api/importacoes/clientes/{loteId}`
- `PUT /api/importacoes/clientes/{loteId}/itens/{itemId}`
- `PATCH /api/importacoes/clientes/{loteId}/itens/{itemId}/selecao`
- `POST /api/importacoes/clientes/{loteId}/duplicidades/resolver`
- `POST /api/importacoes/clientes/{loteId}/confirmar`
- `POST /api/importacoes/clientes/{loteId}/cancelar`
- `GET /api/importacoes/clientes/{loteId}/relatorio`

A listagem aceita `busca`, `tipoPessoa`, `cidade`, `ativo`, `pagina`, `tamanhoPagina`, `ordenarPor` e `direcao`.

## Importação inicial

A pré-visualização usa ClosedXML, valida estritamente as abas e os cabeçalhos esperados, limita cada aba a 10.000 linhas e reutiliza `DocumentoFiscal` e `ClienteInputValidator`, as mesmas regras do CRUD. O arquivo Excel nunca é gravado no servidor.

O banco preserva apenas o resumo do lote. Os itens completos ficam, por no máximo 24 horas, em um arquivo JSON protegido no diretório temporário do servidor, fora do `wwwroot`, associado ao usuário autenticado. A confirmação relê e revalida esse estágio, consulta novamente os documentos existentes e grava todos os selecionados em uma única transação. Após confirmação, cancelamento ou expiração, o estágio completo é eliminado. O relatório CSV é mascarado e mantido por prazo limitado.

Todas as mutações da importação exigem cookie autenticado e token antifalsificação. A tabela nunca exibe CPF/CNPJ completo; o documento integral só aparece no formulário explícito de correção. O índice filtrado por hash bloqueia uma segunda conclusão do mesmo arquivo.

## Planilha e publicação

A planilha original permanece fora do Git. A importação real só ocorre depois de pré-visualização, correção dos inválidos, resolução manual das duplicidades, seleção e confirmação explícita em `/importacao`. Nunca use a planilha real como fixture versionada.

A publicação reproduzível está em `scripts/production/Publish-Production.ps1`. Consulte [implantação](docs/implantacao-producao.md), [operação do servidor](docs/operacao-servidor.md) e [atualização e rollback](docs/atualizacao-e-rollback.md). Nenhum script contém ou imprime a connection string.
