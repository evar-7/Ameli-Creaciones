BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117225723_GOLCommerce'
)
BEGIN
    ALTER TABLE [customer_orders] ADD [discount] decimal(18,2) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117225723_GOLCommerce'
)
BEGIN
    ALTER TABLE [customer_orders] ADD [payment_mode] nvarchar(30) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117225723_GOLCommerce'
)
BEGIN
    ALTER TABLE [customer_orders] ADD [payment_reference] nvarchar(100) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117225723_GOLCommerce'
)
BEGIN
    ALTER TABLE [customer_orders] ADD [subtotal] decimal(18,2) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117225723_GOLCommerce'
)
BEGIN
    ALTER TABLE [customer_orders] ADD [tax] decimal(18,2) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117225723_GOLCommerce'
)
BEGIN
    ALTER TABLE [cart_lines] ADD [revision] uniqueidentifier NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117225723_GOLCommerce'
)
BEGIN
    ALTER TABLE [base_products] ADD [image] varbinary(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117225723_GOLCommerce'
)
BEGIN
    ALTER TABLE [base_products] ADD [image_content_type] nvarchar(30) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117225723_GOLCommerce'
)
BEGIN
    ALTER TABLE [base_products] ADD [minimum_stock] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117225723_GOLCommerce'
)
BEGIN
    ALTER TABLE [base_products] ADD [revision] uniqueidentifier NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117225723_GOLCommerce'
)
BEGIN
    ALTER TABLE [base_products] ADD [unit_cost] decimal(18,2) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117225723_GOLCommerce'
)
BEGIN
    CREATE TABLE [order_status_history] (
        [id] uniqueidentifier NOT NULL,
        [sequence] bigint NOT NULL IDENTITY,
        [order_id] uniqueidentifier NOT NULL,
        [status] nvarchar(30) NOT NULL,
        [occurred_at_utc] datetimeoffset NOT NULL,
        [carrier] nvarchar(100) NOT NULL,
        [tracking_number] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_order_status_history] PRIMARY KEY ([id]),
        CONSTRAINT [FK_order_status_history_customer_orders_order_id] FOREIGN KEY ([order_id]) REFERENCES [customer_orders] ([id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117225723_GOLCommerce'
)
BEGIN
    CREATE TABLE [payment_attempts] (
        [id] uniqueidentifier NOT NULL,
        [owner_user_id] uniqueidentifier NOT NULL,
        [request_id] uniqueidentifier NOT NULL,
        [amount] decimal(18,2) NOT NULL,
        [status] nvarchar(30) NOT NULL,
        [provider] nvarchar(30) NOT NULL,
        [reference] nvarchar(100) NOT NULL,
        [occurred_at_utc] datetimeoffset NOT NULL,
        CONSTRAINT [PK_payment_attempts] PRIMARY KEY ([id]),
        CONSTRAINT [FK_payment_attempts_users_owner_user_id] FOREIGN KEY ([owner_user_id]) REFERENCES [users] ([id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117225723_GOLCommerce'
)
BEGIN
    CREATE INDEX [IX_order_status_history_order_id_occurred_at_utc] ON [order_status_history] ([order_id], [occurred_at_utc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117225723_GOLCommerce'
)
BEGIN
    CREATE UNIQUE INDEX [IX_payment_attempts_owner_user_id_request_id] ON [payment_attempts] ([owner_user_id], [request_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117225723_GOLCommerce'
)
BEGIN
    UPDATE base_products SET revision = NEWID();
    UPDATE cart_lines SET revision = NEWID();
    UPDATE customer_orders SET subtotal = total WHERE subtotal = 0;
    UPDATE customer_orders SET status = N'En camino' WHERE status = N'Enviado';
    INSERT INTO order_status_history (id, order_id, status, occurred_at_utc, carrier, tracking_number)
    SELECT NEWID(), id, N'Confirmado', created_at_utc, N'', N'' FROM customer_orders;
    INSERT INTO order_status_history (id, order_id, status, occurred_at_utc, carrier, tracking_number)
    SELECT NEWID(), id, status, updated_at_utc, carrier, tracking_number
    FROM customer_orders WHERE status <> N'Confirmado';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117225723_GOLCommerce'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261117225723_GOLCommerce', N'10.0.9');
END;

COMMIT;
GO

