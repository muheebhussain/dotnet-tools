DO $$ 
DECLARE 
    table_name text := 'role';  -- Replace with your table name
    result text := 'public class ' || initcap(table_name) || ' {' || chr(10);
    column_name text;
    ordinal_position int;
    column_type text;
    nullable_sign text;
    attribute text;
    max_length_attribute text;
BEGIN
    FOR column_name, ordinal_position, column_type, nullable_sign, attribute, max_length_attribute IN 
        SELECT 
            column_name,
            ordinal_position,
            CASE data_type 
                WHEN 'bigint' THEN 'long'
                WHEN 'boolean' THEN 'bool'
                WHEN 'character' THEN 'string'
                WHEN 'character varying' THEN 'string'
                WHEN 'date' THEN 'DateTime'
                WHEN 'double precision' THEN 'double'
                WHEN 'integer' THEN 'int'
                WHEN 'numeric' THEN 'decimal'
                WHEN 'real' THEN 'float'
                WHEN 'smallint' THEN 'short'
                WHEN 'text' THEN 'string'
                WHEN 'time without time zone' THEN 'TimeSpan'
                WHEN 'timestamp without time zone' THEN 'DateTime'
                WHEN 'uuid' THEN 'Guid'
                WHEN 'bytea' THEN 'byte[]'
                ELSE 'UNKNOWN_' || data_type 
            END AS column_type,
            CASE 
                WHEN is_nullable = 'YES' AND data_type IN ('bigint', 'boolean', 'date', 'double precision', 'integer', 'numeric', 'real', 'smallint', 'timestamp without time zone', 'uuid') THEN '?' 
                ELSE '' 
            END AS nullable_sign,
            CASE 
                WHEN is_nullable = 'NO' AND column_name <> 'id' THEN '[Required]' 
                ELSE '' 
            END AS attribute,
            CASE 
                WHEN data_type IN ('character varying') AND character_maximum_length < 1000 THEN '[MaxLength(' || character_maximum_length || ')]' 
                ELSE '' 
            END AS max_length_attribute
        FROM information_schema.columns 
        WHERE table_name = table_name 
        ORDER BY ordinal_position
    LOOP
        result := result || chr(10) || 
                  attribute || ' ' || max_length_attribute || chr(10) || 
                  '    public ' || column_type || nullable_sign || ' ' || initcap(column_name) || ' { get; set; }' || chr(10);
    END LOOP;

    result := result || chr(10) || '}';

    RAISE NOTICE '%', result;
END $$;
