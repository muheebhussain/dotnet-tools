DO $$
DECLARE
    rec RECORD;
BEGIN
    FOR rec IN
        SELECT table_schema,
               table_name,
               column_name
          FROM information_schema.columns
         WHERE table_schema = 'dbo'
           AND data_type = 'timestamp without time zone'
    LOOP
        RAISE NOTICE 'Altering %.% column % to timestamptz (treating as America/New_York local times)', 
                     rec.table_schema, rec.table_name, rec.column_name;

        EXECUTE format(
          'ALTER TABLE %I.%I 
           ALTER COLUMN %I 
           TYPE timestamptz 
           USING %I AT TIME ZONE ''America/New_York'';',
          rec.table_schema,
          rec.table_name,
          rec.column_name,
          rec.column_name
        );
    END LOOP;
END;
$$;
ALTER DATABASE your_database_name SET timezone = 'America/New_York';
