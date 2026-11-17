# Release 20261117-visual-polish

Pacote Windows win-x64, Release, autocontido, sem single-file e sem trimming.
Refinamento do login, clientes, administradores e importação: paleta, formulários,
tabelas, navegação móvel e transições curtas. Os títulos levam no máximo 408 ms;
diálogos abrem em 250 ms. Movimento reduzido desativa a entrada dos títulos e as
transições dos diálogos e notificações. O texto acessível original é preservado.

Autenticação, autorização, regras de clientes, administradores, importação e
clipboard permanecem preservados. O pacote mantém o ajuste de segurança já existente
de System.Security.Cryptography.Pkcs 9.0.21. A configuração continua externa: nenhum
login padrão ou senha de bootstrap é distribuído. Arquivos appsettings por ambiente
não são publicados; appsettings.json contém apenas a chave de conexão vazia.

## Limites desta entrega

- Nenhum acesso ao servidor de produção, SQL Server ou backups reais.
- Nenhuma migration, script SQL ou alteração de schema executada.
- Testes .NET usam EF em memória/SQLite isolados; RUN_REAL_PREVIEW foi desabilitado.
- O teste Test-PackagedApplication.ps1 antigo cria banco e aplica SQL: não foi usado
  e não faz parte do procedimento desta release.
- operations/sql permanece vazio. O utilitário operacional de migration é preservado
  para compatibilidade, mas não deve ser executado para esta atualização.
- Os testes do navegador usam respostas locais simuladas, sem banco ou credenciais reais.
- A release foi preparada e testada localmente; não foi instalada em produção.

## Conteúdo e conferência local

O ZIP contém a pasta 20261117-visual-polish com app, operations,
release-manifest.json, release-files.sha256, source-files.sha256,
security-scan.json e validation-report.json. As provas brutas de testes ficam no
workspace de desenvolvimento, fora do ZIP. O manifesto informa o commit de base,
o estado pendente do Git e o hash do inventário de origem; o pacote inclui as
alterações visuais pendentes, sem afirmar que elas já foram commitadas.

O arquivo .zip.sha256 acompanha o ZIP. Antes de extrair:

```powershell
$archive = 'C:\Temp\20261117-visual-polish.zip'
$expected = ((Get-Content -LiteralPath "$archive.sha256" -Raw).Trim() -split '\s+')[0]
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expected) {
    throw 'SHA-256 do ZIP divergente.'
}
Expand-Archive -LiteralPath $archive -DestinationPath 'C:\Temp\LavanderiaAnjos-visual-polish'
$package = 'C:\Temp\LavanderiaAnjos-visual-polish\20261117-visual-polish'
& "$package\operations\tests\Test-PackagedSafety.ps1" -PackagePath $package
```

Conserve o SHA-256 recebido pelo canal de entrega para compará-lo com o arquivo
acompanhante. Os hashes detectam corrupção/alterações; não substituem assinatura digital.

Opcionalmente, em uma estação de homologação com Node e Chrome:

```powershell
& "$package\operations\tests\Test-PackagedBranding.ps1" -PackagePath $package
& "$package\operations\tests\Test-PackagedCopyWiring.ps1" -PackagePath $package
& "$package\operations\tests\Test-PackagedVisual.ps1" `
    -PackagePath $package `
    -PlaywrightModulePath 'C:\Ferramentas\node_modules\playwright' `
    -OutputPath 'C:\Temp\LavanderiaAnjos-visual-check'
```

Não instale dependências de desenvolvimento no servidor de produção para rodar
testes de navegador. O pacote não inclui Node, Playwright ou Chrome.

## Atualização futura pelo operador

Os comandos abaixo são somente o procedimento de operação; não foram executados
nesta preparação. No servidor, em PowerShell como administrador, depois de conferir
o ZIP e o inventário do pacote:

```powershell
$package = 'C:\Temp\LavanderiaAnjos-visual-polish\20261117-visual-polish'
$deploymentRoot = 'D:\LavanderiaAnjos\Sistema'
$previousTarget = [string](Get-Item -LiteralPath "$deploymentRoot\current" -Force).Target
$previousReleaseId = Split-Path -Leaf $previousTarget.TrimEnd('\')
if ([string]::IsNullOrWhiteSpace($previousReleaseId)) { throw 'Release anterior não identificada.' }
Write-Output "Registrar para rollback: $previousReleaseId"

& "$package\operations\tests\Test-PackagedSafety.ps1" -PackagePath $package
& "$package\operations\scripts\Update-Service.ps1" `
    -PackagePath $package `
    -ReleaseId '20261117-visual-polish' `
    -Root $deploymentRoot `
    -ServiceName 'LavanderiaAnjos'
```

Registre o ID anterior fora da sessão antes da troca. Não presuma que a versão
anterior em produção é igual ao último pacote local. Preserve as configurações
externas, a identidade do serviço, as permissões e as chaves de autenticação já
utilizadas; não reexecute o bootstrap de administrador.

Update-Service.ps1 copia a nova release sem sobrescrever versões anteriores, para
o serviço, troca a junção current, reinicia e aguarda /health. Se a nova versão não
ficar saudável, retorna automaticamente ao alvo anterior e verifica sua saúde.
O script não executa migrations nem toca em dados e backups.

Depois da troca, valide /health, login existente, consulta de clientes,
administradores, abertura da importação e cópia/colagem nos diálogos. Use consultas
e o cliente sintético já aprovado para homologação; não edite clientes reais nem
confirme importações como parte desta atualização. Recarregue a página para obter
os assets versionados. Compare a rotina de backup existente com o seu registro
operacional, sem reconfigurá-la e sem excluir backups.

## Rollback futuro pelo operador

Use o ID anterior registrado, mesmo se houver outras pastas de release:

```powershell
& 'D:\LavanderiaAnjos\Sistema\scripts\Rollback-Service.ps1' `
    -TargetReleaseId $previousReleaseId `
    -Root 'D:\LavanderiaAnjos\Sistema' `
    -ServiceName 'LavanderiaAnjos'
```

Se abrir uma nova sessão, atribua a previousReleaseId o ID registrado antes de
executar o comando. O rollback troca somente os arquivos da aplicação e verifica
/health; esta release não requer reversão de schema ou restauração de banco.
Não remova a release anterior nem as outras duas versões de contingência.

## Evidências

validation-report.json registra as execuções desta entrega e a validação dos
arquivos publicados. O navegador verifica cinco larguras (320 a 1440 px), títulos
com nomes acessíveis, teclado, Escape, restauração do foco, mudança da preferência
de movimento, formulários e pré-visualização. No modal real, testa Clipboard API,
fallback textarea, fallback contenteditable, remoção dos temporários e seleção
manual; os três caminhos automáticos incluem cópia e colagem real no navegador.
Chrome Android e Safari iOS físicos continuam seguindo o roteiro de homologação
em testes-copia-mobile.md; não foram simulados como dispositivos reais nesta entrega.

security-scan.json registra a inspeção de configurações, nomes proibidos, strings
de conexão literais e assinaturas comuns de tokens/chaves. O teste de segurança
também confere cada hash do inventário e recusa arquivos extras. Não são incluídos
User Secrets, configurações por ambiente, dados de clientes, planilhas, relatórios
reais, chaves privadas, backups ou arquivos SQL.
