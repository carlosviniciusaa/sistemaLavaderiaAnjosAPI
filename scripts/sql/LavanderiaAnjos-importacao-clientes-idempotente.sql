BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721164735_ImportacaoInicialClientes'
)
BEGIN
    CREATE TABLE [ImportacoesClientes] (
        [Id] uniqueidentifier NOT NULL,
        [HashArquivo] char(64) NOT NULL,
        [NomeArquivo] nvarchar(260) NOT NULL,
        [Status] int NOT NULL,
        [CriadoEm] datetime2 NOT NULL,
        [ExpiraEm] datetime2 NOT NULL,
        [ConfirmadoEm] datetime2 NULL,
        [UsuarioId] int NOT NULL,
        [QuantidadeTotal] int NOT NULL,
        [QuantidadeValida] int NOT NULL,
        [QuantidadeComAlerta] int NOT NULL,
        [QuantidadeImportada] int NOT NULL,
        [QuantidadeIgnorada] int NOT NULL,
        [QuantidadeInvalida] int NOT NULL,
        [QuantidadeDuplicada] int NOT NULL,
        [QuantidadeExistente] int NOT NULL,
        [QuantidadeSelecionada] int NOT NULL,
        CONSTRAINT [PK_ImportacoesClientes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ImportacoesClientes_Usuarios_UsuarioId] FOREIGN KEY ([UsuarioId]) REFERENCES [Usuarios] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721164735_ImportacaoInicialClientes'
)
BEGIN
    CREATE INDEX [IX_ImportacoesClientes_UsuarioId_CriadoEm] ON [ImportacoesClientes] ([UsuarioId], [CriadoEm]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721164735_ImportacaoInicialClientes'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_ImportacoesClientes_Hash_Concluida] ON [ImportacoesClientes] ([HashArquivo]) WHERE [Status] = 2');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721164735_ImportacaoInicialClientes'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260721164735_ImportacaoInicialClientes', N'10.0.9');
END;

COMMIT;
GO
