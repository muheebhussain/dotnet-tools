-- SQL Server schema for ArchivalSystem (dbo schema)
-- Run in the ArchivalDb database.

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- 1) archival_table_retention_policy
CREATE TABLE dbo.archival_table_retention_policy
(
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    name NVARCHAR(200) NOT NULL,
    is_active BIT NOT NULL,
    keep_last_eod INT NULL,
    keep_last_eom INT NULL,
    keep_last_eoq INT NULL,
    keep_last_eoy INT NULL,
    created_at_et DATETIME2 NOT NULL,
    created_by NVARCHAR(100) NULL,
    updated_at_et DATETIME2 NULL,
    updated_by NVARCHAR(100) NULL
);
GO

-- 2) archival_file_lifecycle_policy
CREATE TABLE dbo.archival_file_lifecycle_policy
(
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    name NVARCHAR(200) NOT NULL,
    is_active BIT NOT NULL,
    azure_policy_tag NVARCHAR(100) NULL,

    -- EOD
    eod_cool_days INT NULL,
    eod_archive_days INT NULL,
    eod_delete_days INT NULL,

    -- EOM
    eom_cool_days INT NULL,
    eom_archive_days INT NULL,
    eom_delete_days INT NULL,

    -- EOQ
    eoq_cool_days INT NULL,
    eoq_archive_days INT NULL,
    eoq_delete_days INT NULL,

    -- EOY
    eoy_cool_days INT NULL,
    eoy_archive_days INT NULL,
    eoy_delete_days INT NULL,

    -- External
    external_cool_days INT NULL,
    external_archive_days INT NULL,
    external_delete_days INT NULL,

    created_at_et DATETIME2 NOT NULL,
    created_by NVARCHAR(100) NULL,
    updated_at_et DATETIME2 NULL,
    updated_by NVARCHAR(100) NULL
);

-- Unique index on azure_policy_tag when not null
CREATE UNIQUE INDEX ux_archival_file_lifecycle_policy_azure_policy_tag
ON dbo.archival_file_lifecycle_policy(azure_policy_tag)
WHERE azure_policy_tag IS NOT NULL;
GO

-- 3) archival_table_configuration
CREATE TABLE dbo.archival_table_configuration
(
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    database_name NVARCHAR(128) NOT NULL,
    schema_name NVARCHAR(128) NOT NULL,
    table_name NVARCHAR(128) NOT NULL,
    as_of_date_column NVARCHAR(128) NULL,
    export_mode NVARCHAR(50) NOT NULL,
    storage_account_name NVARCHAR(200) NOT NULL,
    container_name NVARCHAR(200) NOT NULL,
    archive_path_template NVARCHAR(400) NOT NULL,
    discovery_path_prefix NVARCHAR(400) NULL,
    table_retention_policy_id INT NULL,
    file_lifecycle_policy_id INT NULL,
    is_active BIT NOT NULL,
    delete_from_source BIT NOT NULL CONSTRAINT df_archival_table_configuration_delete_from_source DEFAULT (1),
    created_at_et DATETIME2 NOT NULL,
    created_by NVARCHAR(100) NULL,
    updated_at_et DATETIME2 NULL,
    updated_by NVARCHAR(100) NULL
);

ALTER TABLE dbo.archival_table_configuration
ADD CONSTRAINT fk_tableconfig_retention FOREIGN KEY (table_retention_policy_id)
    REFERENCES dbo.archival_table_retention_policy(id)
    ON DELETE NO ACTION;

ALTER TABLE dbo.archival_table_configuration
ADD CONSTRAINT fk_tableconfig_filepolicy FOREIGN KEY (file_lifecycle_policy_id)
    REFERENCES dbo.archival_file_lifecycle_policy(id)
    ON DELETE NO ACTION;

-- unique index on (database_name, schema_name, table_name)
CREATE UNIQUE INDEX uq_archival_table_configuration_table
ON dbo.archival_table_configuration(database_name, schema_name, table_name);
GO

-- 4) archival_file
CREATE TABLE dbo.archival_file
(
    id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    table_configuration_id INT NOT NULL,
    as_of_date DATE NULL,
    date_type NVARCHAR(4) NULL,
    storage_account_name NVARCHAR(200) NOT NULL,
    container_name NVARCHAR(200) NOT NULL,
    blob_path NVARCHAR(1024) NOT NULL,
    etag NVARCHAR(200) NULL,
    content_type NVARCHAR(200) NULL,
    file_size_bytes BIGINT NULL,
    row_count BIGINT NULL,
    status NVARCHAR(20) NOT NULL,
    created_at_et DATETIME2 NOT NULL,
    archival_policy_tag NVARCHAR(100) NULL,
    current_access_tier NVARCHAR(50) NULL,
    last_tier_check_at_et DATETIME2 NULL,
    override_file_lifecycle_policy_id INT NULL,
    last_tags_sync_at_et DATETIME2 NULL
);

ALTER TABLE dbo.archival_file
ADD CONSTRAINT fk_archival_file_tableconfig FOREIGN KEY (table_configuration_id)
    REFERENCES dbo.archival_table_configuration(id)
    ON DELETE CASCADE;

ALTER TABLE dbo.archival_file
ADD CONSTRAINT fk_archival_file_override_policy FOREIGN KEY (override_file_lifecycle_policy_id)
    REFERENCES dbo.archival_file_lifecycle_policy(id)
    ON DELETE NO ACTION;

-- Unique constraint for table/date/path as in EF mapping
CREATE UNIQUE INDEX ux_archival_file_unique
ON dbo.archival_file(table_configuration_id, as_of_date, blob_path);
GO

-- 5) archival_exemption
CREATE TABLE dbo.archival_exemption
(
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    table_configuration_id INT NOT NULL,
    as_of_date DATE NULL,
    scope NVARCHAR(10) NOT NULL,
    reason NVARCHAR(MAX) NULL,
    created_at_et DATETIME2 NOT NULL,
    created_by NVARCHAR(100) NULL
);

ALTER TABLE dbo.archival_exemption
ADD CONSTRAINT fk_archival_exemption_tableconfig FOREIGN KEY (table_configuration_id)
    REFERENCES dbo.archival_table_configuration(id)
    ON DELETE CASCADE;

CREATE INDEX ix_archival_exemption_table_date
ON dbo.archival_exemption(table_configuration_id, as_of_date);
GO

-- 6) archival_run
CREATE TABLE dbo.archival_run
(
    id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    started_at_et DATETIME2 NOT NULL,
    ended_at_et DATETIME2 NULL,
    status NVARCHAR(20) NOT NULL,
    note NVARCHAR(MAX) NULL
);
GO

-- 7) archival_run_detail
CREATE TABLE dbo.archival_run_detail
(
    id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    run_id BIGINT NOT NULL,
    table_configuration_id INT NULL,
    as_of_date DATE NULL,
    date_type NVARCHAR(4) NULL,
    archival_file_id BIGINT NULL,
    phase NVARCHAR(20) NOT NULL,
    status NVARCHAR(20) NOT NULL,
    rows_affected BIGINT NULL,
    file_path NVARCHAR(1024) NULL,
    error_message NVARCHAR(MAX) NULL,
    created_at_et DATETIME2 NOT NULL
);

ALTER TABLE dbo.archival_run_detail
ADD CONSTRAINT fk_archival_run_detail_run FOREIGN KEY (run_id)
    REFERENCES dbo.archival_run(id)
    ON DELETE CASCADE;

ALTER TABLE dbo.archival_run_detail
ADD CONSTRAINT fk_archival_run_detail_tableconfig FOREIGN KEY (table_configuration_id)
    REFERENCES dbo.archival_table_configuration(id)
    ON DELETE NO ACTION;

ALTER TABLE dbo.archival_run_detail
ADD CONSTRAINT fk_archival_run_detail_file FOREIGN KEY (archival_file_id)
    REFERENCES dbo.archival_file(id)
    ON DELETE SET NULL;

CREATE INDEX ix_archival_run_detail_run ON dbo.archival_run_detail(run_id);
CREATE INDEX ix_archival_run_detail_table_date ON dbo.archival_run_detail(table_configuration_id, as_of_date);
GO

-- 8) Optional: v_archival_file_full view (example projection)
IF OBJECT_ID('dbo.v_archival_file_full', 'V') IS NOT NULL
    DROP VIEW dbo.v_archival_file_full;
GO

CREATE VIEW dbo.v_archival_file_full
AS
SELECT
    f.id AS archival_file_id,
    f.table_configuration_id,
    f.as_of_date,
    f.date_type,
    f.storage_account_name AS file_storage_account_name,
    f.container_name AS file_container_name,
    f.blob_path,
    f.etag,
    f.content_type,
    f.file_size_bytes,
    f.row_count,
    f.status AS file_status,
    f.created_at_et AS file_created_at_et,
    f.archival_policy_tag,
    f.current_access_tier,
    f.last_tier_check_at_et,
    f.override_file_lifecycle_policy_id,
    f.last_tags_sync_at_et,

    tc.database_name,
    tc.schema_name,
    tc.table_name,
    tc.as_of_date_column,
    tc.export_mode,
    tc.storage_account_name AS config_storage_account_name,
    tc.container_name AS config_container_name,
    tc.archive_path_template,
    tc.discovery_path_prefix,
    tc.table_retention_policy_id,
    tc.file_lifecycle_policy_id,
    tc.is_active AS table_configuration_is_active,

    p.name AS file_lifecycle_policy_name,
    p.azure_policy_tag AS file_lifecycle_azure_policy_tag,

    rp.name AS table_retention_policy_name,
    rp.is_active AS table_retention_policy_is_active,
    rp.keep_last_eod,
    rp.keep_last_eom,
    rp.keep_last_eoq,
    rp.keep_last_eoy

FROM dbo.archival_file f
LEFT JOIN dbo.archival_table_configuration tc ON tc.id = f.table_configuration_id
LEFT JOIN dbo.archival_file_lifecycle_policy p ON p.id = tc.file_lifecycle_policy_id
LEFT JOIN dbo.archival_table_retention_policy rp ON rp.id = tc.table_retention_policy_id;
GO

-- End of schema
