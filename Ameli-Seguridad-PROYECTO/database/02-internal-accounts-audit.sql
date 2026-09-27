BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927035616_InternalAccountsAudit'
)
BEGIN
    ALTER TABLE [users] ADD [phone] nvarchar(8) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927035616_InternalAccountsAudit'
)
BEGIN
    ALTER TABLE [users] ADD [revision] uniqueidentifier NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927035616_InternalAccountsAudit'
)
BEGIN
    ALTER TABLE [users] ADD [updated_at_utc] datetimeoffset NOT NULL DEFAULT '0001-01-01T00:00:00.0000000+00:00';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927035616_InternalAccountsAudit'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[security_events]') AND [c].[name] = N'detail');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [security_events] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [security_events] ALTER COLUMN [detail] nvarchar(max) NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927035616_InternalAccountsAudit'
)
BEGIN
    ALTER TABLE [security_events] ADD [actor_email] nvarchar(254) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927035616_InternalAccountsAudit'
)
BEGIN
    ALTER TABLE [security_events] ADD [actor_name] nvarchar(160) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927035616_InternalAccountsAudit'
)
BEGIN
    ALTER TABLE [security_events] ADD [after_json] nvarchar(4000) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927035616_InternalAccountsAudit'
)
BEGIN
    ALTER TABLE [security_events] ADD [before_json] nvarchar(4000) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927035616_InternalAccountsAudit'
)
BEGIN
    ALTER TABLE [security_events] ADD [entity] nvarchar(80) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927035616_InternalAccountsAudit'
)
BEGIN
    ALTER TABLE [security_events] ADD [entity_id] nvarchar(100) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927035616_InternalAccountsAudit'
)
BEGIN
    ALTER TABLE [security_events] ADD [failed_attempts] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927035616_InternalAccountsAudit'
)
BEGIN
    ALTER TABLE [security_events] ADD [integrity_hash] varchar(64) NOT NULL DEFAULT '';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927035616_InternalAccountsAudit'
)
BEGIN
    ALTER TABLE [security_events] ADD [integrity_version] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927035616_InternalAccountsAudit'
)
BEGIN
    ALTER TABLE [security_events] ADD [is_legacy] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927035616_InternalAccountsAudit'
)
BEGIN
    ALTER TABLE [security_events] ADD [locked_until_utc] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927035616_InternalAccountsAudit'
)
BEGIN
    ALTER TABLE [security_events] ADD [lockout_started_at_utc] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927035616_InternalAccountsAudit'
)
BEGIN
    ALTER TABLE [security_events] ADD [module] nvarchar(80) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927035616_InternalAccountsAudit'
)
BEGIN
    ALTER TABLE [security_events] ADD [previous_hash] varchar(64) NOT NULL DEFAULT '';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927035616_InternalAccountsAudit'
)
BEGIN
    ALTER TABLE [security_events] ADD [session_id] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927035616_InternalAccountsAudit'
)
BEGIN
    UPDATE users SET revision=NEWID(), updated_at_utc=created_at_utc;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927035616_InternalAccountsAudit'
)
BEGIN
    UPDATE security_events SET module=N'Seguridad', entity=N'users', entity_id=COALESCE(CONVERT(nvarchar(36),subject_user_id),N'');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927035616_InternalAccountsAudit'
)
BEGIN
    CREATE TABLE [audit_chain_head] (
        [id] int NOT NULL,
        [last_event_id] bigint NOT NULL,
        [record_count] bigint NOT NULL,
        [last_hash] varchar(64) NOT NULL,
        [signature] varchar(64) NOT NULL,
        [key_id] varchar(16) NOT NULL,
        [baseline_at_utc] datetimeoffset NOT NULL,
        CONSTRAINT [PK_audit_chain_head] PRIMARY KEY ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927035616_InternalAccountsAudit'
)
BEGIN
    CREATE INDEX [IX_users_is_internal_is_active_name] ON [users] ([is_internal], [is_active], [name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927035616_InternalAccountsAudit'
)
BEGIN
    CREATE INDEX [IX_security_events_actor_user_id_occurred_at_utc] ON [security_events] ([actor_user_id], [occurred_at_utc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927035616_InternalAccountsAudit'
)
BEGIN
    CREATE INDEX [IX_security_events_module_occurred_at_utc] ON [security_events] ([module], [occurred_at_utc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927035616_InternalAccountsAudit'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260927035616_InternalAccountsAudit', N'10.0.9');
END;

COMMIT;
GO

