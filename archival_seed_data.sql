-- Seed data for configuration tables used by ArchivalSystem
-- Run in the ArchivalDb database.
-- Provides retention policies, lifecycle policies and table configurations for testing.

SET IDENTITY_INSERT dbo.archival_table_retention_policy ON;

INSERT INTO dbo.archival_table_retention_policy
    (id, name, is_active, keep_last_eod, keep_last_eom, keep_last_eoq, keep_last_eoy, created_at_et, created_by)
VALUES
    (1, N'Default Retention', 1, 30, 12, 8, 3, SYSUTCDATETIME(), N'seed'),
    (2, N'Long Term Retention', 1, 365, 60, 24, 10, SYSUTCDATETIME(), N'seed');

SET IDENTITY_INSERT dbo.archival_table_retention_policy OFF;


SET IDENTITY_INSERT dbo.archival_file_lifecycle_policy ON;

INSERT INTO dbo.archival_file_lifecycle_policy
    (id, name, is_active, azure_policy_tag,
     eod_cool_days, eod_archive_days, eod_delete_days,
     eom_cool_days, eom_archive_days, eom_delete_days,
     eoq_cool_days, eoq_archive_days, eoq_delete_days,
     eoy_cool_days, eoy_archive_days, eoy_delete_days,
     external_cool_days, external_archive_days, external_delete_days,
     created_at_et, created_by)
VALUES
    (1, N'Default File Policy', 1, N'default-policy',
     7, 30, 365,
     7, 60, 3650,
     14, 90, 3650,
     30, 180, 3650,
     3, 30, 365,
     SYSUTCDATETIME(), N'seed'),

    (2, N'Cold Archive Policy', 1, N'cold-policy',
     0, 7, 365,
     0, 30, 3650,
     0, 60, 3650,
     0, 180, 3650,
     0, 30, 365,
     SYSUTCDATETIME(), N'seed'),

    (3, N'External Files Policy', 1, N'external-policy',
     0, 0, 0,
     0, 0, 0,
     0, 0, 0,
     0, 0, 0,
     1, 30, 365,
     SYSUTCDATETIME(), N'seed');

SET IDENTITY_INSERT dbo.archival_file_lifecycle_policy OFF;


SET IDENTITY_INSERT dbo.archival_table_configuration ON;

INSERT INTO dbo.archival_table_configuration
    (id, database_name, schema_name, table_name, as_of_date_column, export_mode,
     storage_account_name, container_name, archive_path_template, discovery_path_prefix,
     table_retention_policy_id, file_lifecycle_policy_id, is_active, delete_from_source,
     created_at_et, created_by)
VALUES
    -- Self-managed example table (exports Parquet from DB)
    (10, N'TestDb', N'dbo', N'transactions', N'as_of_date', N'SelfManaged',
     N'devstore', N'archival', N'/{db}/{schema}/{table}/{yyyy}/{MM}/{dd}', NULL,
     1, 1, 1, 1,
     SYSUTCDATETIME(), N'seed'),

    -- External example table (discover blobs in storage)
    (20, N'TestDb', N'dbo', N'external_reports', NULL, N'External',
     N'devstore', N'external', N'external/{db}/{schema}/{table}/{yyyy}/{MM}/{dd}', N'external/',
     2, 3, 1, 0,
     SYSUTCDATETIME(), N'seed');

SET IDENTITY_INSERT dbo.archival_table_configuration OFF;


-- Optional: verify inserted rows
SELECT id, name, is_active, keep_last_eod, keep_last_eom, keep_last_eoq, keep_last_eoy
FROM dbo.archival_table_retention_policy
ORDER BY id;

SELECT id, name, azure_policy_tag, eod_archive_days, external_archive_days
FROM dbo.archival_file_lifecycle_policy
ORDER BY id;

SELECT id, database_name, schema_name, table_name, as_of_date_column, export_mode,
       storage_account_name, container_name, archive_path_template, discovery_path_prefix,
       table_retention_policy_id, file_lifecycle_policy_id, is_active, delete_from_source
FROM dbo.archival_table_configuration
ORDER BY id;

-- Seed a regulatory/audit retention policy and file lifecycle policy,
-- plus an example table configuration that uses them.
-- Run in ArchivalDb.

SET IDENTITY_INSERT dbo.archival_table_retention_policy ON;

INSERT INTO dbo.archival_table_retention_policy
    (id, name, is_active, keep_last_eod, keep_last_eom, keep_last_eoq, keep_last_eoy, created_at_et, created_by)
VALUES
    (3, N'Regulatory Audit Retention (10y)', 1, 3650, 3650, 3650, 3650, SYSUTCDATETIME(), N'seed');

SET IDENTITY_INSERT dbo.archival_table_retention_policy OFF;


SET IDENTITY_INSERT dbo.archival_file_lifecycle_policy ON;

INSERT INTO dbo.archival_file_lifecycle_policy
    (id, name, is_active, azure_policy_tag,
     eod_cool_days, eod_archive_days, eod_delete_days,
     eom_cool_days, eom_archive_days, eom_delete_days,
     eoq_cool_days, eoq_archive_days, eoq_delete_days,
     eoy_cool_days, eoy_archive_days, eoy_delete_days,
     external_cool_days, external_archive_days, external_delete_days,
     created_at_et, created_by)
VALUES
    (4, N'Regulatory File Policy', 1, N'regulatory-policy',
     30, 365, 3650,      -- EOD: short cool, archive within a year, keep 10y
     30, 365, 3650,      -- EOM
     30, 365, 3650,      -- EOQ
     30, 365, 3650,      -- EOY
     7,  90,  3650,      -- External: slightly shorter cool, archived but kept 10y
     SYSUTCDATETIME(), N'seed');

SET IDENTITY_INSERT dbo.archival_file_lifecycle_policy OFF;


SET IDENTITY_INSERT dbo.archival_table_configuration ON;

-- Example table configuration that represents an audited dataset.
INSERT INTO dbo.archival_table_configuration
    (id, database_name, schema_name, table_name, as_of_date_column, export_mode,
     storage_account_name, container_name, archive_path_template, discovery_path_prefix,
     table_retention_policy_id, file_lifecycle_policy_id, is_active, delete_from_source,
     created_at_et, created_by)
VALUES
    (30, N'TestDb', N'dbo', N'financial_audit', N'as_of_date', N'SelfManaged',
     N'devstore', N'audit-archive', N'/{db}/{schema}/{table}/{yyyy}/{MM}/{dd}', NULL,
     3, 4, 1, 0,
     SYSUTCDATETIME(), N'seed');

SET IDENTITY_INSERT dbo.archival_table_configuration OFF;


-- Quick verification queries (optional)
SELECT id, name, is_active, keep_last_eod FROM dbo.archival_table_retention_policy WHERE id = 3;
SELECT id, name, azure_policy_tag, external_archive_days FROM dbo.archival_file_lifecycle_policy WHERE id = 4;
SELECT id, database_name, table_name, table_retention_policy_id, file_lifecycle_policy_id FROM dbo.archival_table_configuration WHERE id = 30;
