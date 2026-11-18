BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118191252_GOLLogisticsAndCatalogLifecycle'
)
BEGIN
    ALTER TABLE [product_categories] ADD [revision] uniqueidentifier NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118191252_GOLLogisticsAndCatalogLifecycle'
)
BEGIN
    ALTER TABLE [order_status_history] ADD [actor_name] nvarchar(160) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118191252_GOLLogisticsAndCatalogLifecycle'
)
BEGIN
    ALTER TABLE [order_status_history] ADD [actor_user_id] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118191252_GOLLogisticsAndCatalogLifecycle'
)
BEGIN
    ALTER TABLE [customer_orders] ADD [carrier_confirmation] nvarchar(200) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118191252_GOLLogisticsAndCatalogLifecycle'
)
BEGIN
    ALTER TABLE [customer_orders] ADD [delivered_at_utc] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118191252_GOLLogisticsAndCatalogLifecycle'
)
BEGIN
    ALTER TABLE [customer_orders] ADD [delivery_recorded_at_utc] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118191252_GOLLogisticsAndCatalogLifecycle'
)
BEGIN
    ALTER TABLE [customer_orders] ADD [delivery_user_id] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118191252_GOLLogisticsAndCatalogLifecycle'
)
BEGIN
    ALTER TABLE [customer_orders] ADD [delivery_user_name] nvarchar(160) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118191252_GOLLogisticsAndCatalogLifecycle'
)
BEGIN
    ALTER TABLE [customer_orders] ADD [dispatched_at_utc] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118191252_GOLLogisticsAndCatalogLifecycle'
)
BEGIN
    ALTER TABLE [customer_orders] ADD [normalized_tracking_number] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118191252_GOLLogisticsAndCatalogLifecycle'
)
BEGIN
    ALTER TABLE [customer_orders] ADD [payment_status] nvarchar(40) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118191252_GOLLogisticsAndCatalogLifecycle'
)
BEGIN
    ALTER TABLE [customer_orders] ADD [points_enabled] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118191252_GOLLogisticsAndCatalogLifecycle'
)
BEGIN
    ALTER TABLE [customer_orders] ADD [preparation_started_at_utc] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118191252_GOLLogisticsAndCatalogLifecycle'
)
BEGIN
    ALTER TABLE [customer_orders] ADD [preparation_user_id] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118191252_GOLLogisticsAndCatalogLifecycle'
)
BEGIN
    ALTER TABLE [customer_orders] ADD [preparation_user_name] nvarchar(160) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118191252_GOLLogisticsAndCatalogLifecycle'
)
BEGIN
    ALTER TABLE [customer_orders] ADD [rating_enabled] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118191252_GOLLogisticsAndCatalogLifecycle'
)
BEGIN
    ALTER TABLE [customer_orders] ADD [receipt_reference] nvarchar(200) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118191252_GOLLogisticsAndCatalogLifecycle'
)
BEGIN
    ALTER TABLE [customer_order_items] ADD [packed_quantity] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118191252_GOLLogisticsAndCatalogLifecycle'
)
BEGIN
    ALTER TABLE [customer_order_items] ADD [reserved_quantity] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118191252_GOLLogisticsAndCatalogLifecycle'
)
BEGIN
    CREATE TABLE [order_incidents] (
        [id] uniqueidentifier NOT NULL,
        [order_id] uniqueidentifier NOT NULL,
        [request_id] uniqueidentifier NOT NULL,
        [reported_by_user_id] uniqueidentifier NOT NULL,
        [reported_by_name] nvarchar(160) NOT NULL,
        [occurred_at_utc] datetimeoffset NOT NULL,
        [detail] nvarchar(1000) NOT NULL,
        [missing_items_json] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_order_incidents] PRIMARY KEY ([id]),
        CONSTRAINT [FK_order_incidents_customer_orders_order_id] FOREIGN KEY ([order_id]) REFERENCES [customer_orders] ([id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118191252_GOLLogisticsAndCatalogLifecycle'
)
BEGIN
    CREATE TABLE [order_inventory_alerts] (
        [order_id] uniqueidentifier NOT NULL,
        [detail] nvarchar(4000) NOT NULL,
        [raised_at_utc] datetimeoffset NOT NULL,
        [resolved_at_utc] datetimeoffset NULL,
        CONSTRAINT [PK_order_inventory_alerts] PRIMARY KEY ([order_id]),
        CONSTRAINT [FK_order_inventory_alerts_customer_orders_order_id] FOREIGN KEY ([order_id]) REFERENCES [customer_orders] ([id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118191252_GOLLogisticsAndCatalogLifecycle'
)
BEGIN
    CREATE TABLE [purchase_ratings] (
        [order_id] uniqueidentifier NOT NULL,
        [stars] int NOT NULL,
        [comment] nvarchar(1000) NOT NULL,
        [created_at_utc] datetimeoffset NOT NULL,
        CONSTRAINT [PK_purchase_ratings] PRIMARY KEY ([order_id]),
        CONSTRAINT [FK_purchase_ratings_customer_orders_order_id] FOREIGN KEY ([order_id]) REFERENCES [customer_orders] ([id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118191252_GOLLogisticsAndCatalogLifecycle'
)
BEGIN
    CREATE INDEX [IX_customer_orders_delivery_user_id] ON [customer_orders] ([delivery_user_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118191252_GOLLogisticsAndCatalogLifecycle'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_customer_orders_normalized_tracking_number] ON [customer_orders] ([normalized_tracking_number]) WHERE [normalized_tracking_number] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118191252_GOLLogisticsAndCatalogLifecycle'
)
BEGIN
    CREATE INDEX [IX_customer_orders_preparation_user_id] ON [customer_orders] ([preparation_user_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118191252_GOLLogisticsAndCatalogLifecycle'
)
BEGIN
    CREATE UNIQUE INDEX [IX_order_incidents_order_id_request_id] ON [order_incidents] ([order_id], [request_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118191252_GOLLogisticsAndCatalogLifecycle'
)
BEGIN
    ALTER TABLE [customer_orders] ADD CONSTRAINT [FK_customer_orders_users_delivery_user_id] FOREIGN KEY ([delivery_user_id]) REFERENCES [users] ([id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118191252_GOLLogisticsAndCatalogLifecycle'
)
BEGIN
    ALTER TABLE [customer_orders] ADD CONSTRAINT [FK_customer_orders_users_preparation_user_id] FOREIGN KEY ([preparation_user_id]) REFERENCES [users] ([id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118191252_GOLLogisticsAndCatalogLifecycle'
)
BEGIN
    UPDATE product_categories SET revision = NEWID();
    UPDATE customer_orders SET payment_status = N'Pendiente de verificación';
    UPDATE o SET payment_status = N'Pagado'
    FROM customer_orders o WHERE EXISTS (
        SELECT 1 FROM payment_attempts p WHERE p.owner_user_id = o.owner_user_id
        AND p.request_id = o.checkout_request_id AND p.status = N'Aprobado'
        AND p.amount = o.total AND p.reference = o.payment_reference);
    UPDATE i SET reserved_quantity = i.quantity,
        packed_quantity = CASE WHEN o.status IN (N'En camino', N'Entregado') THEN i.quantity ELSE 0 END
    FROM customer_order_items i JOIN customer_orders o ON o.id = i.order_id
    WHERE o.status IN (N'Confirmado', N'En preparación', N'En camino', N'Entregado');
    UPDATE o SET dispatched_at_utc = COALESCE(
        (SELECT MIN(h.occurred_at_utc) FROM order_status_history h WHERE h.order_id = o.id AND h.status = N'En camino'),
        o.updated_at_utc)
    FROM customer_orders o WHERE o.status IN (N'En camino', N'Entregado');
    UPDATE customer_orders SET delivery_recorded_at_utc = updated_at_utc WHERE status = N'Entregado';
    ;WITH guides AS (
        SELECT id, UPPER(LTRIM(RTRIM(tracking_number))) AS normalized,
        ROW_NUMBER() OVER (PARTITION BY UPPER(LTRIM(RTRIM(tracking_number))) ORDER BY created_at_utc, id) AS rn
        FROM customer_orders WHERE LEN(LTRIM(RTRIM(tracking_number))) > 0)
    UPDATE o SET normalized_tracking_number = g.normalized
    FROM customer_orders o JOIN guides g ON o.id = g.id WHERE g.rn = 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118191252_GOLLogisticsAndCatalogLifecycle'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261118191252_GOLLogisticsAndCatalogLifecycle', N'10.0.9');
END;

COMMIT;
GO

