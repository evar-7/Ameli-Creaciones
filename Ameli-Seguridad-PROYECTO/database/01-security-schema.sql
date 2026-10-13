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
    WHERE [MigrationId] = N'20260922172942_InitialSecurity'
)
BEGIN
    CREATE TABLE [email_outbox] (
        [id] uniqueidentifier NOT NULL,
        [recipient] nvarchar(254) NOT NULL,
        [subject] nvarchar(200) NOT NULL,
        [protected_body] nvarchar(max) NOT NULL,
        [created_at_utc] datetimeoffset NOT NULL,
        [next_attempt_at_utc] datetimeoffset NOT NULL,
        [sent_at_utc] datetimeoffset NULL,
        [attempts] int NOT NULL,
        CONSTRAINT [PK_email_outbox] PRIMARY KEY ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922172942_InitialSecurity'
)
BEGIN
    CREATE TABLE [recovery_attempts] (
        [id] uniqueidentifier NOT NULL,
        [account_key] varchar(64) NOT NULL,
        [origin] nvarchar(80) NOT NULL,
        [created_at_utc] datetimeoffset NOT NULL,
        CONSTRAINT [PK_recovery_attempts] PRIMARY KEY ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922172942_InitialSecurity'
)
BEGIN
    CREATE TABLE [roles] (
        [name] nvarchar(30) NOT NULL,
        [is_active] bit NOT NULL,
        CONSTRAINT [PK_roles] PRIMARY KEY ([name])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922172942_InitialSecurity'
)
BEGIN
    CREATE TABLE [security_events] (
        [id] bigint NOT NULL IDENTITY,
        [occurred_at_utc] datetimeoffset NOT NULL,
        [actor_user_id] uniqueidentifier NULL,
        [subject_user_id] uniqueidentifier NULL,
        [actor_role] nvarchar(30) NOT NULL,
        [action] nvarchar(80) NOT NULL,
        [outcome] nvarchar(30) NOT NULL,
        [origin] nvarchar(80) NOT NULL,
        [correlation_id] nvarchar(100) NOT NULL,
        [detail] nvarchar(1500) NOT NULL,
        CONSTRAINT [PK_security_events] PRIMARY KEY ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922172942_InitialSecurity'
)
BEGIN
    CREATE TABLE [users] (
        [id] uniqueidentifier NOT NULL,
        [name] nvarchar(160) NOT NULL,
        [email] nvarchar(254) NOT NULL,
        [normalized_email] nvarchar(254) NOT NULL,
        [password_hash] nvarchar(512) NOT NULL,
        [role_name] nvarchar(30) NOT NULL,
        [is_internal] bit NOT NULL,
        [is_active] bit NOT NULL,
        [failed_access_count] int NOT NULL,
        [locked_until_utc] datetimeoffset NULL,
        [last_login_utc] datetimeoffset NULL,
        [security_version] int NOT NULL,
        [created_at_utc] datetimeoffset NOT NULL,
        CONSTRAINT [PK_users] PRIMARY KEY ([id]),
        CONSTRAINT [FK_users_roles_role_name] FOREIGN KEY ([role_name]) REFERENCES [roles] ([name]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922172942_InitialSecurity'
)
BEGIN
    CREATE TABLE [auth_sessions] (
        [id] uniqueidentifier NOT NULL,
        [user_id] uniqueidentifier NOT NULL,
        [security_version] int NOT NULL,
        [refresh_token_hash] varchar(64) NOT NULL,
        [created_at_utc] datetimeoffset NOT NULL,
        [last_activity_at_utc] datetimeoffset NOT NULL,
        [absolute_expires_at_utc] datetimeoffset NOT NULL,
        [revoked_at_utc] datetimeoffset NULL,
        [device] nvarchar(240) NOT NULL,
        CONSTRAINT [PK_auth_sessions] PRIMARY KEY ([id]),
        CONSTRAINT [FK_auth_sessions_users_user_id] FOREIGN KEY ([user_id]) REFERENCES [users] ([id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922172942_InitialSecurity'
)
BEGIN
    CREATE TABLE [password_resets] (
        [id] uniqueidentifier NOT NULL,
        [user_id] uniqueidentifier NOT NULL,
        [token_hash] varchar(64) NOT NULL,
        [created_at_utc] datetimeoffset NOT NULL,
        [expires_at_utc] datetimeoffset NOT NULL,
        [used_at_utc] datetimeoffset NULL,
        CONSTRAINT [PK_password_resets] PRIMARY KEY ([id]),
        CONSTRAINT [FK_password_resets_users_user_id] FOREIGN KEY ([user_id]) REFERENCES [users] ([id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922172942_InitialSecurity'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'name', N'is_active') AND [object_id] = OBJECT_ID(N'[roles]'))
        SET IDENTITY_INSERT [roles] ON;
    EXEC(N'INSERT INTO [roles] ([name], [is_active])
    VALUES (N''Administrador'', CAST(1 AS bit)),
    (N''Cliente'', CAST(1 AS bit)),
    (N''Logística'', CAST(1 AS bit))');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'name', N'is_active') AND [object_id] = OBJECT_ID(N'[roles]'))
        SET IDENTITY_INSERT [roles] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922172942_InitialSecurity'
)
BEGIN
    CREATE UNIQUE INDEX [IX_auth_sessions_refresh_token_hash] ON [auth_sessions] ([refresh_token_hash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922172942_InitialSecurity'
)
BEGIN
    CREATE INDEX [IX_auth_sessions_user_id_revoked_at_utc] ON [auth_sessions] ([user_id], [revoked_at_utc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922172942_InitialSecurity'
)
BEGIN
    CREATE INDEX [IX_email_outbox_sent_at_utc_next_attempt_at_utc] ON [email_outbox] ([sent_at_utc], [next_attempt_at_utc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922172942_InitialSecurity'
)
BEGIN
    CREATE UNIQUE INDEX [IX_password_resets_token_hash] ON [password_resets] ([token_hash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922172942_InitialSecurity'
)
BEGIN
    CREATE INDEX [IX_password_resets_user_id_created_at_utc] ON [password_resets] ([user_id], [created_at_utc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922172942_InitialSecurity'
)
BEGIN
    CREATE INDEX [IX_recovery_attempts_account_key_created_at_utc] ON [recovery_attempts] ([account_key], [created_at_utc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922172942_InitialSecurity'
)
BEGIN
    CREATE INDEX [IX_recovery_attempts_origin_created_at_utc] ON [recovery_attempts] ([origin], [created_at_utc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922172942_InitialSecurity'
)
BEGIN
    CREATE INDEX [IX_security_events_occurred_at_utc] ON [security_events] ([occurred_at_utc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922172942_InitialSecurity'
)
BEGIN
    CREATE UNIQUE INDEX [IX_users_normalized_email] ON [users] ([normalized_email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922172942_InitialSecurity'
)
BEGIN
    CREATE INDEX [IX_users_role_name] ON [users] ([role_name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922172942_InitialSecurity'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260922172942_InitialSecurity', N'10.0.9');
END;

COMMIT;
GO

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

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260928002343_CatalogBase'
)
BEGIN
    CREATE TABLE [product_categories] (
        [id] uniqueidentifier NOT NULL,
        [name] nvarchar(120) NOT NULL,
        [normalized_name] varchar(120) NOT NULL,
        [description] nvarchar(500) NOT NULL,
        [is_active] bit NOT NULL,
        [created_at_utc] datetimeoffset NOT NULL,
        [updated_at_utc] datetimeoffset NOT NULL,
        CONSTRAINT [PK_product_categories] PRIMARY KEY ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260928002343_CatalogBase'
)
BEGIN
    CREATE TABLE [suppliers] (
        [id] uniqueidentifier NOT NULL,
        [name] nvarchar(160) NOT NULL,
        [normalized_name] varchar(160) NOT NULL,
        [contact_name] nvarchar(160) NOT NULL,
        [email] nvarchar(254) NOT NULL,
        [normalized_email] varchar(254) NOT NULL,
        [phone] nvarchar(8) NOT NULL,
        [notes] nvarchar(500) NOT NULL,
        [is_active] bit NOT NULL,
        [created_at_utc] datetimeoffset NOT NULL,
        [updated_at_utc] datetimeoffset NOT NULL,
        CONSTRAINT [PK_suppliers] PRIMARY KEY ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260928002343_CatalogBase'
)
BEGIN
    CREATE TABLE [base_products] (
        [id] uniqueidentifier NOT NULL,
        [name] nvarchar(160) NOT NULL,
        [normalized_name] varchar(160) NOT NULL,
        [description] nvarchar(1000) NOT NULL,
        [category_id] uniqueidentifier NOT NULL,
        [supplier_id] uniqueidentifier NOT NULL,
        [species] nvarchar(10) NOT NULL,
        [material] nvarchar(80) NOT NULL,
        [icon] nvarchar(8) NOT NULL,
        [unit_price] decimal(18,2) NOT NULL,
        [stock_quantity] int NOT NULL,
        [is_active] bit NOT NULL,
        [created_at_utc] datetimeoffset NOT NULL,
        [updated_at_utc] datetimeoffset NOT NULL,
        CONSTRAINT [PK_base_products] PRIMARY KEY ([id]),
        CONSTRAINT [FK_base_products_product_categories_category_id] FOREIGN KEY ([category_id]) REFERENCES [product_categories] ([id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_base_products_suppliers_supplier_id] FOREIGN KEY ([supplier_id]) REFERENCES [suppliers] ([id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260928002343_CatalogBase'
)
BEGIN
    CREATE INDEX [IX_base_products_category_id_supplier_id_is_active] ON [base_products] ([category_id], [supplier_id], [is_active]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260928002343_CatalogBase'
)
BEGIN
    CREATE UNIQUE INDEX [IX_base_products_normalized_name] ON [base_products] ([normalized_name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260928002343_CatalogBase'
)
BEGIN
    CREATE INDEX [IX_base_products_species_is_active_name] ON [base_products] ([species], [is_active], [name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260928002343_CatalogBase'
)
BEGIN
    CREATE INDEX [IX_base_products_supplier_id] ON [base_products] ([supplier_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260928002343_CatalogBase'
)
BEGIN
    CREATE INDEX [IX_product_categories_is_active_name] ON [product_categories] ([is_active], [name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260928002343_CatalogBase'
)
BEGIN
    CREATE UNIQUE INDEX [IX_product_categories_normalized_name] ON [product_categories] ([normalized_name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260928002343_CatalogBase'
)
BEGIN
    CREATE INDEX [IX_suppliers_is_active_name] ON [suppliers] ([is_active], [name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260928002343_CatalogBase'
)
BEGIN
    CREATE UNIQUE INDEX [IX_suppliers_normalized_email] ON [suppliers] ([normalized_email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260928002343_CatalogBase'
)
BEGIN
    CREATE UNIQUE INDEX [IX_suppliers_normalized_name] ON [suppliers] ([normalized_name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260928002343_CatalogBase'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260928002343_CatalogBase', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013010000_CustomerIdentity'
)
BEGIN
    ALTER TABLE [users] ADD [first_name] nvarchar(80) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013010000_CustomerIdentity'
)
BEGIN
    ALTER TABLE [users] ADD [last_names] nvarchar(160) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013010000_CustomerIdentity'
)
BEGIN
    CREATE TABLE [pet_profiles] (
        [id] uniqueidentifier NOT NULL,
        [owner_user_id] uniqueidentifier NOT NULL,
        [name] nvarchar(100) NOT NULL,
        [species] nvarchar(40) NOT NULL,
        [breed] nvarchar(100) NOT NULL,
        [age_years] int NOT NULL,
        [weight_kg] decimal(7,2) NOT NULL,
        [height_cm] decimal(6,2) NOT NULL,
        [allergies] nvarchar(500) NOT NULL,
        [is_active] bit NOT NULL,
        [photo] varbinary(max) NULL,
        [photo_content_type] nvarchar(20) NULL,
        [created_at_utc] datetimeoffset NOT NULL,
        [updated_at_utc] datetimeoffset NOT NULL,
        [revision] uniqueidentifier NOT NULL,
        [colors_json] nvarchar(2000) NOT NULL,
        [materials_json] nvarchar(2000) NOT NULL,
        [garment_types_json] nvarchar(2000) NOT NULL,
        [styles_json] nvarchar(2000) NOT NULL,
        [preferences_updated_at_utc] datetimeoffset NULL,
        CONSTRAINT [PK_pet_profiles] PRIMARY KEY ([id]),
        CONSTRAINT [FK_pet_profiles_users_owner_user_id] FOREIGN KEY ([owner_user_id]) REFERENCES [users] ([id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013010000_CustomerIdentity'
)
BEGIN
    CREATE INDEX [IX_pet_profiles_owner_user_id_is_active_name] ON [pet_profiles] ([owner_user_id], [is_active], [name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013010000_CustomerIdentity'
)
BEGIN
    CREATE TABLE [delivery_addresses] (
        [id] uniqueidentifier NOT NULL,
        [owner_user_id] uniqueidentifier NOT NULL,
        [province] nvarchar(100) NOT NULL,
        [canton] nvarchar(100) NOT NULL,
        [district] nvarchar(100) NOT NULL,
        [exact_address] nvarchar(300) NOT NULL,
        [reference] nvarchar(300) NOT NULL,
        [is_default] bit NOT NULL,
        [created_at_utc] datetimeoffset NOT NULL,
        CONSTRAINT [PK_delivery_addresses] PRIMARY KEY ([id]),
        CONSTRAINT [FK_delivery_addresses_users_owner_user_id] FOREIGN KEY ([owner_user_id]) REFERENCES [users] ([id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013010000_CustomerIdentity'
)
BEGIN
    CREATE INDEX [IX_delivery_addresses_owner_user_id_is_default] ON [delivery_addresses] ([owner_user_id], [is_default]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013010000_CustomerIdentity'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261013010000_CustomerIdentity', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013021234_AssistantCommerce'
)
BEGIN
    ALTER TABLE [pet_profiles] ADD [allergies_declared] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013021234_AssistantCommerce'
)
BEGIN
    ALTER TABLE [base_products] ADD [care_instructions] nvarchar(1000) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013021234_AssistantCommerce'
)
BEGIN
    ALTER TABLE [base_products] ADD [colors_json] nvarchar(2000) NOT NULL DEFAULT N'[]';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013021234_AssistantCommerce'
)
BEGIN
    ALTER TABLE [base_products] ADD [fit_notes] nvarchar(500) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013021234_AssistantCommerce'
)
BEGIN
    ALTER TABLE [base_products] ADD [garment_type] nvarchar(40) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013021234_AssistantCommerce'
)
BEGIN
    ALTER TABLE [base_products] ADD [sku] varchar(40) NOT NULL DEFAULT '';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013021234_AssistantCommerce'
)
BEGIN
    ALTER TABLE [base_products] ADD [style] nvarchar(40) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013021234_AssistantCommerce'
)
BEGIN
    UPDATE [base_products] SET [sku] = 'AML-' + UPPER(REPLACE(CONVERT(varchar(36), [id]), '-', '')) WHERE [sku] = '';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013021234_AssistantCommerce'
)
BEGIN
    UPDATE [pet_profiles] SET [allergies_declared] = 1 WHERE LEN(LTRIM(RTRIM([allergies]))) > 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013021234_AssistantCommerce'
)
BEGIN
    CREATE TABLE [cart_lines] (
        [id] uniqueidentifier NOT NULL,
        [owner_user_id] uniqueidentifier NOT NULL,
        [product_id] uniqueidentifier NOT NULL,
        [pet_id] uniqueidentifier NULL,
        [size] nvarchar(20) NOT NULL,
        [quantity] int NOT NULL,
        CONSTRAINT [PK_cart_lines] PRIMARY KEY ([id]),
        CONSTRAINT [FK_cart_lines_base_products_product_id] FOREIGN KEY ([product_id]) REFERENCES [base_products] ([id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_cart_lines_pet_profiles_pet_id] FOREIGN KEY ([pet_id]) REFERENCES [pet_profiles] ([id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_cart_lines_users_owner_user_id] FOREIGN KEY ([owner_user_id]) REFERENCES [users] ([id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013021234_AssistantCommerce'
)
BEGIN
    CREATE TABLE [cart_operations] (
        [id] uniqueidentifier NOT NULL,
        [owner_user_id] uniqueidentifier NOT NULL,
        [request_id] uniqueidentifier NOT NULL,
        [fingerprint] varchar(64) NOT NULL,
        [result_quantity] int NOT NULL,
        [created_at_utc] datetimeoffset NOT NULL,
        CONSTRAINT [PK_cart_operations] PRIMARY KEY ([id]),
        CONSTRAINT [FK_cart_operations_users_owner_user_id] FOREIGN KEY ([owner_user_id]) REFERENCES [users] ([id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013021234_AssistantCommerce'
)
BEGIN
    CREATE TABLE [customer_orders] (
        [id] uniqueidentifier NOT NULL,
        [owner_user_id] uniqueidentifier NOT NULL,
        [checkout_request_id] uniqueidentifier NOT NULL,
        [number] varchar(40) NOT NULL,
        [status] nvarchar(30) NOT NULL,
        [carrier] nvarchar(100) NOT NULL,
        [tracking_number] nvarchar(100) NOT NULL,
        [created_at_utc] datetimeoffset NOT NULL,
        [updated_at_utc] datetimeoffset NOT NULL,
        [total] decimal(18,2) NOT NULL,
        [delivery_snapshot] nvarchar(1200) NOT NULL,
        CONSTRAINT [PK_customer_orders] PRIMARY KEY ([id]),
        CONSTRAINT [FK_customer_orders_users_owner_user_id] FOREIGN KEY ([owner_user_id]) REFERENCES [users] ([id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013021234_AssistantCommerce'
)
BEGIN
    CREATE TABLE [product_size_rules] (
        [id] uniqueidentifier NOT NULL,
        [product_id] uniqueidentifier NOT NULL,
        [name] nvarchar(20) NOT NULL,
        [min_height_cm] decimal(6,2) NOT NULL,
        [max_height_cm] decimal(6,2) NOT NULL,
        [min_weight_kg] decimal(7,2) NOT NULL,
        [max_weight_kg] decimal(7,2) NOT NULL,
        [equivalent] nvarchar(80) NOT NULL,
        CONSTRAINT [PK_product_size_rules] PRIMARY KEY ([id]),
        CONSTRAINT [FK_product_size_rules_base_products_product_id] FOREIGN KEY ([product_id]) REFERENCES [base_products] ([id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013021234_AssistantCommerce'
)
BEGIN
    CREATE TABLE [customer_order_items] (
        [id] uniqueidentifier NOT NULL,
        [order_id] uniqueidentifier NOT NULL,
        [product_id] uniqueidentifier NOT NULL,
        [pet_id] uniqueidentifier NULL,
        [product_name] nvarchar(160) NOT NULL,
        [sku] varchar(40) NOT NULL,
        [size] nvarchar(20) NOT NULL,
        [quantity] int NOT NULL,
        [unit_price] decimal(18,2) NOT NULL,
        [pet_snapshot] nvarchar(1200) NOT NULL,
        CONSTRAINT [PK_customer_order_items] PRIMARY KEY ([id]),
        CONSTRAINT [FK_customer_order_items_base_products_product_id] FOREIGN KEY ([product_id]) REFERENCES [base_products] ([id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_customer_order_items_customer_orders_order_id] FOREIGN KEY ([order_id]) REFERENCES [customer_orders] ([id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_customer_order_items_pet_profiles_pet_id] FOREIGN KEY ([pet_id]) REFERENCES [pet_profiles] ([id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013021234_AssistantCommerce'
)
BEGIN
    CREATE UNIQUE INDEX [IX_base_products_sku] ON [base_products] ([sku]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013021234_AssistantCommerce'
)
BEGIN
    CREATE INDEX [IX_cart_lines_owner_user_id] ON [cart_lines] ([owner_user_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013021234_AssistantCommerce'
)
BEGIN
    CREATE INDEX [IX_cart_lines_pet_id] ON [cart_lines] ([pet_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013021234_AssistantCommerce'
)
BEGIN
    CREATE INDEX [IX_cart_lines_product_id] ON [cart_lines] ([product_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013021234_AssistantCommerce'
)
BEGIN
    CREATE UNIQUE INDEX [IX_cart_operations_owner_user_id_request_id] ON [cart_operations] ([owner_user_id], [request_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013021234_AssistantCommerce'
)
BEGIN
    CREATE INDEX [IX_customer_order_items_order_id] ON [customer_order_items] ([order_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013021234_AssistantCommerce'
)
BEGIN
    CREATE INDEX [IX_customer_order_items_pet_id] ON [customer_order_items] ([pet_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013021234_AssistantCommerce'
)
BEGIN
    CREATE INDEX [IX_customer_order_items_product_id] ON [customer_order_items] ([product_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013021234_AssistantCommerce'
)
BEGIN
    CREATE UNIQUE INDEX [IX_customer_orders_number] ON [customer_orders] ([number]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013021234_AssistantCommerce'
)
BEGIN
    CREATE UNIQUE INDEX [IX_customer_orders_owner_user_id_checkout_request_id] ON [customer_orders] ([owner_user_id], [checkout_request_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013021234_AssistantCommerce'
)
BEGIN
    CREATE UNIQUE INDEX [IX_product_size_rules_product_id_name] ON [product_size_rules] ([product_id], [name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013021234_AssistantCommerce'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261013021234_AssistantCommerce', N'10.0.9');
END;

COMMIT;
GO

