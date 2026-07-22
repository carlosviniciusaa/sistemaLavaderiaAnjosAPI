SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @MigrationId nvarchar(150) = N'20260721194510_GerenciamentoAdministradores';
    DECLARE @LockResult int;

    EXEC @LockResult = sys.sp_getapplock
        @Resource = N'LavanderiaAnjos:Migration:20260721194510_GerenciamentoAdministradores',
        @LockMode = N'Exclusive',
        @LockOwner = N'Transaction',
        @LockTimeout = 60000;

    IF @LockResult < 0
        THROW 51001, 'Não foi possível obter o bloqueio exclusivo para aplicar a migration de administradores.', 1;

    IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
        THROW 51002, 'A tabela dbo.__EFMigrationsHistory não existe. Aplique primeiro as migrations anteriores.', 1;

    IF NOT EXISTS (
        SELECT 1
        FROM dbo.__EFMigrationsHistory WITH (UPDLOCK, HOLDLOCK)
        WHERE MigrationId = @MigrationId
    )
    BEGIN
        IF OBJECT_ID(N'dbo.Usuarios', N'U') IS NULL
            THROW 51003, 'A tabela dbo.Usuarios não existe. Aplique primeiro as migrations anteriores.', 1;

        -- A colisão é verificada antes de qualquer alteração de schema. A expressão usa
        -- somente a coluna antiga Login e equivale à normalização definida pela migration.
        IF EXISTS (
            SELECT LOWER(LTRIM(RTRIM(Login)))
            FROM dbo.Usuarios
            GROUP BY LOWER(LTRIM(RTRIM(Login)))
            HAVING COUNT_BIG(*) > 1
        )
            THROW 51000, 'Existem logins duplicados após a normalização. A migration foi interrompida sem alterar os dados.', 1;

        -- Cada DDL é compilado apenas quando executado. Assim, nenhuma instrução do lote
        -- externo referencia estaticamente uma coluna recém-criada.
        IF COL_LENGTH(N'dbo.Usuarios', N'LoginNormalizado') IS NULL
            EXEC sys.sp_executesql N'ALTER TABLE dbo.Usuarios ADD LoginNormalizado nvarchar(100) NOT NULL CONSTRAINT DF_Usuarios_LoginNormalizado DEFAULT N'''';';

        IF COL_LENGTH(N'dbo.Usuarios', N'Versao') IS NULL
            EXEC sys.sp_executesql N'ALTER TABLE dbo.Usuarios ADD Versao bigint NOT NULL CONSTRAINT DF_Usuarios_Versao DEFAULT CAST(1 AS bigint);';

        IF COL_LENGTH(N'dbo.Usuarios', N'VersaoSessao') IS NULL
            EXEC sys.sp_executesql N'ALTER TABLE dbo.Usuarios ADD VersaoSessao int NOT NULL CONSTRAINT DF_Usuarios_VersaoSessao DEFAULT 0;';

        IF NOT EXISTS (
            SELECT 1
            FROM sys.columns AS c
            INNER JOIN sys.types AS t ON t.user_type_id = c.user_type_id
            WHERE c.object_id = OBJECT_ID(N'dbo.Usuarios')
              AND c.name = N'LoginNormalizado'
              AND t.name = N'nvarchar'
              AND c.max_length = 200
              AND c.is_nullable = 0
        )
            THROW 51004, 'A coluna dbo.Usuarios.LoginNormalizado existe, mas não corresponde a nvarchar(100) NOT NULL.', 1;

        IF NOT EXISTS (
            SELECT 1
            FROM sys.columns AS c
            INNER JOIN sys.types AS t ON t.user_type_id = c.user_type_id
            WHERE c.object_id = OBJECT_ID(N'dbo.Usuarios')
              AND c.name = N'Versao'
              AND t.name = N'bigint'
              AND c.is_nullable = 0
        )
            THROW 51005, 'A coluna dbo.Usuarios.Versao existe, mas não corresponde a bigint NOT NULL.', 1;

        IF NOT EXISTS (
            SELECT 1
            FROM sys.columns AS c
            INNER JOIN sys.types AS t ON t.user_type_id = c.user_type_id
            WHERE c.object_id = OBJECT_ID(N'dbo.Usuarios')
              AND c.name = N'VersaoSessao'
              AND t.name = N'int'
              AND c.is_nullable = 0
        )
            THROW 51006, 'A coluna dbo.Usuarios.VersaoSessao existe, mas não corresponde a int NOT NULL.', 1;

        IF NOT EXISTS (
            SELECT 1
            FROM sys.default_constraints AS dc
            INNER JOIN sys.columns AS c
                ON c.object_id = dc.parent_object_id
               AND c.column_id = dc.parent_column_id
            WHERE dc.parent_object_id = OBJECT_ID(N'dbo.Usuarios')
              AND c.name = N'LoginNormalizado'
        )
            EXEC sys.sp_executesql N'ALTER TABLE dbo.Usuarios ADD CONSTRAINT DF_Usuarios_LoginNormalizado DEFAULT N'''' FOR LoginNormalizado;';

        IF NOT EXISTS (
            SELECT 1
            FROM sys.default_constraints AS dc
            INNER JOIN sys.columns AS c
                ON c.object_id = dc.parent_object_id
               AND c.column_id = dc.parent_column_id
            WHERE dc.parent_object_id = OBJECT_ID(N'dbo.Usuarios')
              AND c.name = N'Versao'
        )
            EXEC sys.sp_executesql N'ALTER TABLE dbo.Usuarios ADD CONSTRAINT DF_Usuarios_Versao DEFAULT CAST(1 AS bigint) FOR Versao;';

        IF NOT EXISTS (
            SELECT 1
            FROM sys.default_constraints AS dc
            INNER JOIN sys.columns AS c
                ON c.object_id = dc.parent_object_id
               AND c.column_id = dc.parent_column_id
            WHERE dc.parent_object_id = OBJECT_ID(N'dbo.Usuarios')
              AND c.name = N'VersaoSessao'
        )
            EXEC sys.sp_executesql N'ALTER TABLE dbo.Usuarios ADD CONSTRAINT DF_Usuarios_VersaoSessao DEFAULT 0 FOR VersaoSessao;';

        EXEC sys.sp_executesql N'
            UPDATE dbo.Usuarios
            SET LoginNormalizado = LOWER(LTRIM(RTRIM(Login)));';

        IF EXISTS (
            SELECT 1
            FROM sys.indexes AS i
            WHERE i.object_id = OBJECT_ID(N'dbo.Usuarios')
              AND i.name = N'UX_Usuarios_LoginNormalizado'
              AND (
                  i.is_unique = 0
                  OR i.is_disabled = 1
                  OR i.has_filter = 1
                  OR 1 <> (
                      SELECT COUNT(*)
                      FROM sys.index_columns AS ic
                      WHERE ic.object_id = i.object_id
                        AND ic.index_id = i.index_id
                        AND ic.key_ordinal > 0
                  )
                  OR NOT EXISTS (
                      SELECT 1
                      FROM sys.index_columns AS ic
                      INNER JOIN sys.columns AS c
                          ON c.object_id = ic.object_id
                         AND c.column_id = ic.column_id
                      WHERE ic.object_id = i.object_id
                        AND ic.index_id = i.index_id
                        AND ic.key_ordinal = 1
                        AND c.name = N'LoginNormalizado'
                  )
              )
        )
            THROW 51007, 'O índice UX_Usuarios_LoginNormalizado existe, mas não corresponde ao índice único esperado.', 1;

        IF NOT EXISTS (
            SELECT 1
            FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'dbo.Usuarios')
              AND name = N'UX_Usuarios_LoginNormalizado'
        )
            EXEC sys.sp_executesql N'CREATE UNIQUE INDEX UX_Usuarios_LoginNormalizado ON dbo.Usuarios (LoginNormalizado);';

        -- TEST-HOOK: before migration history registration
        INSERT INTO dbo.__EFMigrationsHistory (MigrationId, ProductVersion)
        VALUES (@MigrationId, N'10.0.9');
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
