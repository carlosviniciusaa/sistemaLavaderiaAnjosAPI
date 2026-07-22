[CmdletBinding()]
param(
    [string] $ServerInstance = '(localdb)\MSSQLLocalDB'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$migrationPath = Join-Path $repositoryRoot 'scripts\sql\LavanderiaAnjos-admin-management-idempotent.sql'
$executorPath = Join-Path $repositoryRoot 'scripts\production\Invoke-DatabaseMigration.ps1'
$databasePrefix = 'LavanderiaAnjos_AdminSqlTest_'
$createdDatabases = [Collections.Generic.List[string]]::new()
$results = [Collections.Generic.List[object]]::new()

function New-ConnectionString([string] $Database) {
    $builder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new()
    $builder['Data Source'] = $ServerInstance
    $builder['Initial Catalog'] = $Database
    $builder['Integrated Security'] = $true
    $builder['Encrypt'] = $false
    $builder['Application Name'] = 'LavanderiaAnjos SQL migration tests'
    return $builder.ConnectionString
}

function Invoke-NonQuery([string] $Database, [string] $Sql) {
    $connection = [System.Data.SqlClient.SqlConnection]::new((New-ConnectionString $Database))
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        try {
            $command.CommandText = $Sql
            $command.CommandTimeout = 120
            [void]$command.ExecuteNonQuery()
        } finally {
            $command.Dispose()
        }
    } finally {
        $connection.Dispose()
    }
}

function Invoke-Scalar([string] $Database, [string] $Sql) {
    $connection = [System.Data.SqlClient.SqlConnection]::new((New-ConnectionString $Database))
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        try {
            $command.CommandText = $Sql
            $command.CommandTimeout = 120
            return $command.ExecuteScalar()
        } finally {
            $command.Dispose()
        }
    } finally {
        $connection.Dispose()
    }
}

function Assert-Equal($Expected, $Actual, [string] $Message) {
    if ($Expected -ne $Actual) {
        throw "$Message Esperado: '$Expected'; obtido: '$Actual'."
    }
}

function New-SyntheticDatabase([string] $State, [switch] $Collision) {
    $database = $databasePrefix + [Guid]::NewGuid().ToString('N')
    if ($database -notmatch '^LavanderiaAnjos_AdminSqlTest_[0-9a-f]{32}$') {
        throw "Nome de banco sintético inesperado: $database"
    }

    Invoke-NonQuery -Database 'master' -Sql "CREATE DATABASE [$database];"
    $createdDatabases.Add($database)

    $schema = @'
CREATE TABLE dbo.__EFMigrationsHistory (
    MigrationId nvarchar(150) NOT NULL CONSTRAINT PK___EFMigrationsHistory PRIMARY KEY,
    ProductVersion nvarchar(32) NOT NULL
);

CREATE TABLE dbo.Usuarios (
    Id int IDENTITY NOT NULL CONSTRAINT PK_Usuarios PRIMARY KEY,
    Nome nvarchar(150) NOT NULL,
    Login nvarchar(100) NOT NULL,
    SenhaHash nvarchar(500) NOT NULL,
    Ativo bit NOT NULL CONSTRAINT DF_Usuarios_Ativo DEFAULT CAST(1 AS bit),
    CriadoEm datetime2 NOT NULL,
    AtualizadoEm datetime2 NULL
);
CREATE UNIQUE INDEX UX_Usuarios_Login ON dbo.Usuarios (Login);

CREATE TABLE dbo.Clientes (
    Id int IDENTITY NOT NULL CONSTRAINT PK_Clientes PRIMARY KEY,
    NomeRazaoSocial nvarchar(200) NOT NULL
);

CREATE TABLE dbo.ImportacoesClientes (
    Id uniqueidentifier NOT NULL CONSTRAINT PK_ImportacoesClientes PRIMARY KEY,
    UsuarioId int NOT NULL,
    NomeArquivo nvarchar(260) NOT NULL,
    CONSTRAINT FK_ImportacoesClientes_Usuarios FOREIGN KEY (UsuarioId) REFERENCES dbo.Usuarios (Id)
);

INSERT INTO dbo.Usuarios (Nome, Login, SenhaHash, Ativo, CriadoEm)
VALUES (N'Carlos', N'carlos', N'HASH-PRESERVAR-123', 1, SYSUTCDATETIME());

INSERT INTO dbo.Clientes (NomeRazaoSocial) VALUES (N'Cliente preservado');
INSERT INTO dbo.ImportacoesClientes (Id, UsuarioId, NomeArquivo)
VALUES ('11111111-1111-1111-1111-111111111111', 1, N'historico-preservado.xlsx');
'@
    Invoke-NonQuery -Database $database -Sql $schema

    switch ($State) {
        'OldSchema' { }
        'LoginOnly' {
            Invoke-NonQuery $database "ALTER TABLE dbo.Usuarios ADD LoginNormalizado nvarchar(100) NOT NULL CONSTRAINT DF_Usuarios_LoginNormalizado DEFAULT N'';"
            Invoke-NonQuery $database "UPDATE dbo.Usuarios SET LoginNormalizado = N'valor-anterior';"
        }
        'SessionOnly' {
            Invoke-NonQuery $database 'ALTER TABLE dbo.Usuarios ADD VersaoSessao int NOT NULL CONSTRAINT DF_Usuarios_VersaoSessao DEFAULT 0;'
        }
        'SomeColumns' {
            Invoke-NonQuery $database "ALTER TABLE dbo.Usuarios ADD LoginNormalizado nvarchar(100) NOT NULL CONSTRAINT DF_Usuarios_LoginNormalizado DEFAULT N''; ALTER TABLE dbo.Usuarios ADD Versao bigint NOT NULL CONSTRAINT DF_Usuarios_Versao DEFAULT CAST(1 AS bigint);"
        }
        'AllColumns' {
            Invoke-NonQuery $database "ALTER TABLE dbo.Usuarios ADD LoginNormalizado nvarchar(100) NOT NULL CONSTRAINT DF_Usuarios_LoginNormalizado DEFAULT N''; ALTER TABLE dbo.Usuarios ADD Versao bigint NOT NULL CONSTRAINT DF_Usuarios_Versao DEFAULT CAST(1 AS bigint); ALTER TABLE dbo.Usuarios ADD VersaoSessao int NOT NULL CONSTRAINT DF_Usuarios_VersaoSessao DEFAULT 0;"
            Invoke-NonQuery $database "UPDATE dbo.Usuarios SET LoginNormalizado = N'valor-anterior';"
        }
        'Applied' {
            Invoke-NonQuery $database "ALTER TABLE dbo.Usuarios ADD LoginNormalizado nvarchar(100) NOT NULL CONSTRAINT DF_Usuarios_LoginNormalizado DEFAULT N''; ALTER TABLE dbo.Usuarios ADD Versao bigint NOT NULL CONSTRAINT DF_Usuarios_Versao DEFAULT CAST(1 AS bigint); ALTER TABLE dbo.Usuarios ADD VersaoSessao int NOT NULL CONSTRAINT DF_Usuarios_VersaoSessao DEFAULT 0;"
            Invoke-NonQuery $database "UPDATE dbo.Usuarios SET LoginNormalizado = N'carlos'; CREATE UNIQUE INDEX UX_Usuarios_LoginNormalizado ON dbo.Usuarios (LoginNormalizado); INSERT INTO dbo.__EFMigrationsHistory (MigrationId, ProductVersion) VALUES (N'20260721194510_GerenciamentoAdministradores', N'10.0.9');"
        }
        default { throw "Estado sintético desconhecido: $State" }
    }

    if ($Collision) {
        Invoke-NonQuery $database "INSERT INTO dbo.Usuarios (Nome, Login, SenhaHash, Ativo, CriadoEm) VALUES (N'Carlos duplicado', N' CARLOS ', N'OUTRO-HASH', 1, SYSUTCDATETIME());"
    }

    return $database
}

function Invoke-Migration([string] $Database, [string] $SqlFile = $migrationPath) {
    & $executorPath -SqlFile $SqlFile -ServerInstance $ServerInstance -Database $Database | Out-Null
}

function Assert-PreservedData([string] $Database) {
    Assert-Equal 1 ([int](Invoke-Scalar $Database "SELECT COUNT(*) FROM dbo.Usuarios WHERE Login = N'carlos' AND SenhaHash = N'HASH-PRESERVAR-123';")) 'Usuário carlos ou hash foi alterado.'
    Assert-Equal 1 ([int](Invoke-Scalar $Database 'SELECT COUNT(*) FROM dbo.Clientes;')) 'Clientes não foram preservados.'
    Assert-Equal 1 ([int](Invoke-Scalar $Database 'SELECT COUNT(*) FROM dbo.ImportacoesClientes;')) 'Histórico de importação não foi preservado.'
}

function Assert-MigrationApplied([string] $Database) {
    Assert-Equal 1 ([int](Invoke-Scalar $Database "SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'20260721194510_GerenciamentoAdministradores';")) 'Histórico da migration incorreto.'
    Assert-Equal 3 ([int](Invoke-Scalar $Database "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Usuarios') AND name IN (N'LoginNormalizado', N'Versao', N'VersaoSessao');")) 'Colunas da migration ausentes.'
    Assert-Equal 'nvarchar:200:0|bigint:8:0|int:4:0' ([string](Invoke-Scalar $Database @"
SELECT STRING_AGG(CONCAT(t.name, N':', c.max_length, N':', CONVERT(int, c.is_nullable)), N'|') WITHIN GROUP (
    ORDER BY CASE c.name WHEN N'LoginNormalizado' THEN 1 WHEN N'Versao' THEN 2 ELSE 3 END)
FROM sys.columns AS c
INNER JOIN sys.types AS t ON t.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID(N'dbo.Usuarios')
  AND c.name IN (N'LoginNormalizado', N'Versao', N'VersaoSessao');
"@)) 'Tipo ou nulabilidade das colunas diverge da migration EF.'
    Assert-Equal 1 ([int](Invoke-Scalar $Database "SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Usuarios') AND name = N'UX_Usuarios_LoginNormalizado' AND is_unique = 1 AND is_disabled = 0 AND has_filter = 0;")) 'Índice único normalizado ausente ou inválido.'
    Assert-Equal 'carlos' ([string](Invoke-Scalar $Database "SELECT LoginNormalizado FROM dbo.Usuarios WHERE Login = N'carlos';")) 'Login existente não foi normalizado.'

    Invoke-NonQuery $Database "INSERT INTO dbo.Usuarios (Nome, Login, SenhaHash, Ativo, CriadoEm) VALUES (N'Teste defaults', N'teste-defaults', N'HASH-TESTE', 1, SYSUTCDATETIME());"
    Assert-Equal ':1:0' ([string](Invoke-Scalar $Database "SELECT CONCAT(LoginNormalizado, N':', Versao, N':', VersaoSessao) FROM dbo.Usuarios WHERE Login = N'teste-defaults';")) 'Valores padrão divergem da migration EF.'
    Invoke-NonQuery $Database "DELETE FROM dbo.Usuarios WHERE Login = N'teste-defaults';"
    Assert-PreservedData $Database
}

function Add-Result([string] $Scenario, [string] $Status, [string] $Details) {
    $results.Add([pscustomobject]@{ Scenario = $Scenario; Status = $Status; Details = $Details })
}

try {
    $scenarios = @(
        @{ Name = '1. Esquema anterior completo'; State = 'OldSchema' },
        @{ Name = '2. Somente LoginNormalizado'; State = 'LoginOnly' },
        @{ Name = '3. Somente VersaoSessao'; State = 'SessionOnly' },
        @{ Name = '4. Algumas colunas'; State = 'SomeColumns' },
        @{ Name = '5. Todas as colunas, sem histórico'; State = 'AllColumns' },
        @{ Name = '6. Todas as colunas e histórico'; State = 'Applied' }
    )

    foreach ($scenario in $scenarios) {
        $database = New-SyntheticDatabase -State $scenario.State
        Invoke-Migration $database
        Assert-MigrationApplied $database
        Add-Result $scenario.Name 'PASS' 'Schema, defaults, índice, histórico e dados preservados.'
    }

    $collisionDatabase = New-SyntheticDatabase -State 'OldSchema' -Collision
    $collisionMessage = $null
    try {
        Invoke-Migration $collisionDatabase
        throw 'O cenário de colisão deveria ter falhado.'
    } catch {
        $collisionMessage = $_.Exception.GetBaseException().Message
    }
    if ($collisionMessage -notmatch 'logins duplicados') {
        throw "Mensagem de colisão inesperada: $collisionMessage"
    }
    Assert-Equal 0 ([int](Invoke-Scalar $collisionDatabase "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Usuarios') AND name IN (N'LoginNormalizado', N'Versao', N'VersaoSessao');")) 'Colisão deixou alteração parcial de schema.'
    Assert-Equal 0 ([int](Invoke-Scalar $collisionDatabase "SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'20260721194510_GerenciamentoAdministradores';")) 'Colisão registrou a migration indevidamente.'
    Assert-PreservedData $collisionDatabase
    Add-Result '7. Colisão após normalização' 'PASS' 'Erro claro e nenhuma alteração parcial.'

    $twiceDatabase = New-SyntheticDatabase -State 'OldSchema'
    Invoke-Migration $twiceDatabase
    Invoke-Migration $twiceDatabase
    Assert-MigrationApplied $twiceDatabase
    Add-Result '8. Execução duas vezes' 'PASS' 'Segunda execução concluída sem duplicar schema ou histórico.'

    $rollbackDatabase = New-SyntheticDatabase -State 'OldSchema'
    $temporarySql = Join-Path ([IO.Path]::GetTempPath()) ("LavanderiaAnjos-admin-rollback-{0}.sql" -f [Guid]::NewGuid().ToString('N'))
    try {
        $failingSql = (Get-Content -LiteralPath $migrationPath -Raw -Encoding UTF8).Replace(
            '-- TEST-HOOK: before migration history registration',
            "-- TEST-HOOK: before migration history registration`r`n        THROW 51999, 'Falha simulada para validar rollback.', 1;")
        [IO.File]::WriteAllText($temporarySql, $failingSql, [Text.UTF8Encoding]::new($false))
        try {
            Invoke-Migration $rollbackDatabase $temporarySql
            throw 'O cenário de rollback deveria ter falhado.'
        } catch {
            if ($_.Exception.GetBaseException().Message -notmatch 'Falha simulada') { throw }
        }
    } finally {
        if (Test-Path -LiteralPath $temporarySql) { Remove-Item -LiteralPath $temporarySql -Force }
    }
    Assert-Equal 0 ([int](Invoke-Scalar $rollbackDatabase "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Usuarios') AND name IN (N'LoginNormalizado', N'Versao', N'VersaoSessao');")) 'Falha simulada deixou alteração parcial de schema.'
    Assert-Equal 0 ([int](Invoke-Scalar $rollbackDatabase "SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'20260721194510_GerenciamentoAdministradores';")) 'Falha simulada registrou a migration.'
    Assert-PreservedData $rollbackDatabase
    Add-Result '9. Falha simulada e rollback' 'PASS' 'DDL, dados e histórico foram revertidos.'

    $preservationDatabase = New-SyntheticDatabase -State 'LoginOnly'
    Invoke-Migration $preservationDatabase
    Assert-PreservedData $preservationDatabase
    Add-Result '10. Usuário e hash existentes' 'PASS' 'carlos, hash, clientes e importações preservados.'

    $results | Format-Table -AutoSize
    Write-Output "SQL_TEST_SUMMARY: $($results.Count) PASS; 0 FAIL"
} finally {
    foreach ($database in $createdDatabases) {
        if ($database -notmatch '^LavanderiaAnjos_AdminSqlTest_[0-9a-f]{32}$') {
            throw "Recusa em remover banco fora do prefixo de teste: $database"
        }
        try {
            Invoke-NonQuery -Database 'master' -Sql "ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$database];"
        } catch {
            Write-Warning "Não foi possível remover o banco sintético ${database}: $($_.Exception.Message)"
        }
    }
}
