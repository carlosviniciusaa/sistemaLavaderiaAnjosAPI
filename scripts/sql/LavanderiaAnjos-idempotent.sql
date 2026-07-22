IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721151248_CadastroClientesEAutenticacao'
)
BEGIN
    CREATE TABLE [Clientes] (
        [Id] int NOT NULL IDENTITY,
        [CodigoLegado] int NULL,
        [NomeRazaoSocial] nvarchar(200) NOT NULL,
        [Documento] varchar(14) NOT NULL,
        [TipoPessoa] varchar(20) NOT NULL,
        [ProdutorRural] bit NOT NULL,
        [InscricaoEstadual] nvarchar(30) NULL,
        [InscricaoMunicipal] nvarchar(30) NULL,
        [DataInicioRelacionamento] date NULL,
        [Cep] varchar(8) NULL,
        [Logradouro] nvarchar(200) NULL,
        [Numero] nvarchar(30) NULL,
        [Complemento] nvarchar(150) NULL,
        [Bairro] nvarchar(100) NULL,
        [Cidade] nvarchar(100) NULL,
        [Uf] varchar(2) NULL,
        [Telefone] varchar(20) NULL,
        [EmailEnvio] nvarchar(500) NULL,
        [EmailNotaFiscal] nvarchar(500) NULL,
        [Observacoes] nvarchar(2000) NULL,
        [Ativo] bit NOT NULL DEFAULT CAST(1 AS bit),
        [CriadoEm] datetime2 NOT NULL,
        [AtualizadoEm] datetime2 NULL,
        CONSTRAINT [PK_Clientes] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721151248_CadastroClientesEAutenticacao'
)
BEGIN
    CREATE TABLE [Usuarios] (
        [Id] int NOT NULL IDENTITY,
        [Nome] nvarchar(150) NOT NULL,
        [Login] nvarchar(100) NOT NULL,
        [SenhaHash] nvarchar(500) NOT NULL,
        [Ativo] bit NOT NULL DEFAULT CAST(1 AS bit),
        [CriadoEm] datetime2 NOT NULL,
        [AtualizadoEm] datetime2 NULL,
        CONSTRAINT [PK_Usuarios] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721151248_CadastroClientesEAutenticacao'
)
BEGIN
    CREATE INDEX [IX_Clientes_Ativo_NomeRazaoSocial] ON [Clientes] ([Ativo], [NomeRazaoSocial]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721151248_CadastroClientesEAutenticacao'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Clientes_CodigoLegado] ON [Clientes] ([CodigoLegado]) WHERE [CodigoLegado] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721151248_CadastroClientesEAutenticacao'
)
BEGIN
    CREATE INDEX [IX_Clientes_NomeRazaoSocial] ON [Clientes] ([NomeRazaoSocial]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721151248_CadastroClientesEAutenticacao'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Clientes_Documento_Ativo] ON [Clientes] ([Documento]) WHERE [Ativo] = 1');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721151248_CadastroClientesEAutenticacao'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Usuarios_Login] ON [Usuarios] ([Login]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721151248_CadastroClientesEAutenticacao'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260721151248_CadastroClientesEAutenticacao', N'10.0.9');
END;

COMMIT;
GO

