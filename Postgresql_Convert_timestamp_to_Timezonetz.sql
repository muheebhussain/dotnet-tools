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

-- Function to perfrom the above to given tables
CREATE OR REPLACE FUNCTION update_timestamp_columns_in_tables(
    table_list text, 
    timezone_name text DEFAULT 'America/New_York'
)
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE
    tlist text[];
    rec record;
BEGIN
    -- Convert the comma-separated table_list into an array
    tlist := string_to_array(table_list, ',');
    
    -- Optional: Trim whitespace or remove empty elements if the user had trailing commas
    -- Example: remove empty if user typed something like 'table1,,table2'
    -- (Uncomment if needed)
    -- tlist := array_remove(tlist, '');

    -- Loop through all columns in the dbo schema that have type 'timestamp without time zone'
    -- AND whose table_name is in the supplied list.
    FOR rec IN
        SELECT table_schema,
               table_name,
               column_name
          FROM information_schema.columns
         WHERE table_schema = 'dbo'
           AND data_type = 'timestamp without time zone'
           AND table_name = ANY(tlist)
    LOOP
        RAISE NOTICE 'Altering %.% column % to timestamptz (interpreting existing data as % local time)',
                     rec.table_schema, rec.table_name, rec.column_name, timezone_name;

        EXECUTE format(
          'ALTER TABLE %I.%I
           ALTER COLUMN %I
           TYPE timestamptz
           USING %I AT TIME ZONE %L',
          rec.table_schema,
          rec.table_name,
          rec.column_name,
          rec.column_name,
          timezone_name
        );
    END LOOP;
END;
$$;

-- Update columns in "orders" and "customers" tables,
-- treating existing timestamp values as America/New_York local time
SELECT update_timestamp_columns_in_tables('orders,customers', 'America/New_York');
