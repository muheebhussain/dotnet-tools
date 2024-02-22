DECLARE @EnumTemplate NVARCHAR(MAX)

-- Initialize the enum template with the enum declaration
SET @EnumTemplate = 'public enum MyEnum {0}'

-- Query to generate the enum body from the table
DECLARE @EnumBody NVARCHAR(MAX)
SET @EnumBody = ''

SELECT @EnumBody = @EnumBody + 
    '[' + CAST(Id AS NVARCHAR) + '] ' + 
    Description + ' = ' + CAST(Id AS NVARCHAR) + ',' + CHAR(13) 
FROM
    YourTableName
ORDER BY
    Id

-- Remove the last comma
SET @EnumBody = LEFT(@EnumBody, LEN(@EnumBody) - 2)

-- Combine the template and the body
SET @EnumTemplate = REPLACE(@EnumTemplate, '{0}', @EnumBody)

-- Output the result
PRINT @EnumTemplate


-- Updated
DECLARE @TableName NVARCHAR(128) = N'YourTableName'; -- Set your table name here
DECLARE @EnumName NVARCHAR(128) = N'MyEnum'; -- Set your enum name here
DECLARE @SQL NVARCHAR(MAX);
DECLARE @EnumBody NVARCHAR(MAX) = '';
DECLARE @EnumTemplate NVARCHAR(MAX) = 'public enum ' + @EnumName + ' {0}';

-- Dynamic SQL to generate the enum body
SET @SQL = N'SELECT @EnumBodyOUT = (SELECT ''['' + CAST(Id AS NVARCHAR) + ''] '' + 
            Description + '' = '' + CAST(Id AS NVARCHAR) + '','' + CHAR(13)
            FROM ' + QUOTENAME(@TableName) + 
            ' ORDER BY Id FOR XML PATH(''''), TYPE).value(''.'', ''NVARCHAR(MAX)'')';

-- Execute the dynamic SQL
EXEC sp_executesql @SQL, N'@EnumBodyOUT NVARCHAR(MAX) OUTPUT', @EnumBody OUTPUT;

-- Remove the last comma
IF LEN(@EnumBody) > 0
    SET @EnumBody = LEFT(@EnumBody, LEN(@EnumBody) - 2);

-- Combine the template and the body
SET @EnumTemplate = REPLACE(@EnumTemplate, '{0}', @EnumBody);

-- Output the result
PRINT @EnumTemplate;
