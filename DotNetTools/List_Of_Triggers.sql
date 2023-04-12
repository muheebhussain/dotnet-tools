SELECT 
    s.name AS SchemaName,
    t.name AS TableName,
    tr.name AS TriggerName,
    tr.type_desc AS TriggerType,
    tr.is_disabled AS IsDisabled
FROM 
    sys.triggers AS tr
INNER JOIN 
    sys.tables AS t ON tr.parent_id = t.object_id
INNER JOIN
    sys.schemas AS s ON t.schema_id = s.schema_id
ORDER BY
    SchemaName,
    TableName,
    TriggerName;
