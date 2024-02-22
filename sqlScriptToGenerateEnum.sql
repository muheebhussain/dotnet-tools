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
