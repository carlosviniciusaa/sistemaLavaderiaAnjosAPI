# Lavanderia Anjos

Sistema web interno da Lavanderia Anjos. A aplicação será desenvolvida neste computador e publicada futuramente no servidor Windows `SISTEMALAV`, onde também está o SQL Server Express oficial.

Esta primeira etapa entrega a infraestrutura, o diagnóstico do banco, o health check, a análise segura da planilha e a preparação do módulo de clientes. O CRUD, a entidade definitiva, migrations e a importação ainda não foram criados.

## Arquitetura

A solução usa .NET 10 LTS, ASP.NET Core, Entity Framework Core e o provedor oficial do SQL Server. O frontend estático fica na mesma aplicação web, mantendo uma única unidade de publicação.

```text
LavanderiaAnjos/
├── src/
│   ├── LavanderiaAnjos.Web/            # HTTP, health check e frontend
│   ├── LavanderiaAnjos.Application/    # contratos e regras de aplicação
│   ├── LavanderiaAnjos.Domain/         # futuro modelo de domínio
│   └── LavanderiaAnjos.Infrastructure/ # EF Core e SQL Server
├── tests/LavanderiaAnjos.Tests/
├── docs/
├── scripts/
├── dados-reais/
└── LavanderiaAnjos.sln
```

O `LavanderiaAnjosDbContext` começa sem entidades. Não existe `EnsureCreated`, migration inicial ou código que crie/ajuste tabelas ao iniciar.

## Requisitos

- Windows com .NET SDK 10.0.301 ou compatível pelo `global.json`;
- acesso de rede ao servidor `SISTEMALAV` durante o desenvolvimento;
- credencial SQL autorizada para o banco `LavanderiaAnjos`;
- a planilha real, quando necessária, mantida somente fora do Git.

## Restaurar e compilar

```powershell
dotnet restore .\LavanderiaAnjos.sln
dotnet build .\LavanderiaAnjos.sln --no-restore
```

## Configurar a conexão em Development

O projeto usa a chave `ConnectionStrings:LavanderiaAnjos`. Cadastre o valor nos segredos do projeto web, substituindo todos os placeholders:

```powershell
dotnet user-secrets set `
  "ConnectionStrings:LavanderiaAnjos" `
  "Server=<servidor>\<instancia>;Database=<banco>;User Id=<usuario>;Password=<senha>;Encrypt=True;TrustServerCertificate=True;" `
  --project .\src\LavanderiaAnjos.Web\LavanderiaAnjos.Web.csproj
```

Não use `dotnet user-secrets list` em terminais compartilhados e nunca copie o valor para `appsettings.json`, documentação, código ou Git.

## Executar em Development

```powershell
dotnet run --project .\src\LavanderiaAnjos.Web\LavanderiaAnjos.Web.csproj
```

Acesse [http://localhost:5080](http://localhost:5080). O perfil de desenvolvimento habilita mensagens detalhadas para diagnóstico local; elas não são usadas em produção.

## Testar

```powershell
dotnet test .\LavanderiaAnjos.sln --no-build
```

Os testes cobrem inicialização, ausência de configuração, health check, sucesso/falha do diagnóstico do banco, ausência de segredos nas respostas, regras das abas/colunas e leitura somente da planilha real quando ela está presente localmente.

## Health check

Com a aplicação em execução:

```powershell
Invoke-RestMethod http://localhost:5080/health
```

Com aplicação e banco disponíveis, a resposta é:

```json
{
  "status": "Healthy",
  "database": "Healthy"
}
```

Falhas do banco retornam HTTP 503 e conteúdo genérico, sem credencial, connection string ou stack trace.

## Diagnóstico somente leitura do SQL Server

O script executa pela aplicação uma consulta de identificação sem alterar o banco:

```powershell
.\scripts\Test-DatabaseConnection.ps1
```

O resultado mostra somente servidor, banco e versão, ou uma categoria técnica segura de falha. A aplicação não substitui automaticamente o banco remoto por LocalDB, SQLite ou outro banco.

## Ambientes

### Development

- aplicação neste computador;
- SQL Server no host de rede `SISTEMALAV`;
- conexão em .NET User Secrets;
- console e página de erro de desenvolvimento;
- URL local padrão `http://localhost:5080`.

### Production

- aplicação e SQL Server no `SISTEMALAV`;
- instância SQL local indicada externamente, por exemplo `Server=.\<instancia>`;
- connection string fornecida por configuração protegida do serviço ou variável de ambiente;
- erros genéricos para o navegador;
- logs técnicos diários na pasta `logs`, além do console/host do serviço;
- nenhuma dependência de internet em tempo de execução;
- nenhuma ferramenta OpenAPI/Swagger exposta.

Exemplo temporário com variável de ambiente e placeholders:

```powershell
$env:ConnectionStrings__LavanderiaAnjos = "Server=.\<instancia>;Database=<banco>;User Id=<usuario>;Password=<senha>;Encrypt=True;TrustServerCertificate=True;"
$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:ASPNETCORE_URLS = "http://0.0.0.0:5080"
```

Para instalação definitiva, configure essas variáveis na conta/serviço do Windows com ACL adequada; não as coloque em script versionado. A exposição pela rede local e qualquer regra de firewall deverão ser aprovadas e configuradas manualmente no `SISTEMALAV`.

## Publicação planejada no Windows

O projeto já aceita hosting como Windows Service e publicação `win-x64`:

```powershell
.\scripts\Publish-Windows.ps1
```

O script gera uma publicação self-contained em `publish/win-x64`, pasta ignorada pelo Git. Esta etapa não instala nem inicia o serviço; a conta de serviço, a pasta de logs, a URL, a conexão protegida e a regra de firewall deverão ser definidas durante a implantação autorizada.

## Planilha real

`Cadastro Clientes.xlsx` contém dados empresariais reais e é ignorada pela regra `*.xlsx`. Cópias locais também podem ficar em `dados-reais/` ou `importacao/originais/`, igualmente ignoradas.

Nunca versione a planilha ou exportações derivadas. O relatório agregado está em [docs/analise-cadastro-clientes.md](docs/analise-cadastro-clientes.md).

## Segurança e logs

- nunca versionar senhas, secrets, arquivos `.env` ou `appsettings.Production.json`;
- nunca registrar connection strings, credenciais ou dados pessoais;
- o logger de arquivo remove padrões de credencial, e-mail e documento como defesa adicional;
- falhas do banco são registradas apenas por categoria e código técnico;
- logs de auditoria de negócio serão definidos junto com autenticação e o módulo de clientes.

## Próxima etapa

Validar o modelo preliminar documentado e então implementar a entidade `Cliente`, configurações EF Core, migration revisável, validação de CPF/CNPJ, serviço de pré-visualização da importação e CRUD protegido por autenticação. A importação deverá permanecer separada da confirmação de gravação.
