DECLARE @TableName NVARCHAR(256), @ColumnName NVARCHAR(128), @SQL NVARCHAR(MAX)
DECLARE @Results TABLE (TableName NVARCHAR(256), ColumnName NVARCHAR(128))

DECLARE TableCursor CURSOR FOR
SELECT t.name, c.name
FROM sys.tables t
INNER JOIN sys.columns c ON t.object_id = c.object_id
INNER JOIN sys.types ty ON c.user_type_id = ty.user_type_id
WHERE ty.name IN ('int', 'tinyint', 'bit') -- Add other numeric types if needed

OPEN TableCursor
FETCH NEXT FROM TableCursor INTO @TableName, @ColumnName

WHILE @@FETCH_STATUS = 0
BEGIN
    SET @SQL = N'SELECT @Result = CASE WHEN EXISTS (
                    SELECT 1 FROM ' + QUOTENAME(@TableName) + ' 
                    WHERE ' + QUOTENAME(@ColumnName) + ' NOT IN (0, 1) AND ' + QUOTENAME(@ColumnName) + ' IS NOT NULL
                ) THEN 0 ELSE 1 END'

    DECLARE @Result BIT
    EXEC sp_executesql @SQL, N'@Result BIT OUTPUT', @Result OUTPUT

    IF @Result = 1
    BEGIN
        INSERT INTO @Results (TableName, ColumnName) VALUES (@TableName, @ColumnName)
    END

    FETCH NEXT FROM TableCursor INTO @TableName, @ColumnName
END

CLOSE TableCursor
DEALLOCATE TableCursor

SELECT * FROM @Results
