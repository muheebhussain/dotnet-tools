using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;

public class ExcelReader
{
    public DataTable ReadExcelTemplate(Stream excelStream, string templateName)
    {
        if (!ExcelTemplateConfigurations.Templates.TryGetValue(templateName, out var columns))
        {
            throw new ArgumentException($"Template '{templateName}' is not defined.");
        }

        using (var workbook = new XLWorkbook(excelStream))
        {
            var worksheet = workbook.Worksheet(1); // Assuming data is in the first worksheet
            var headerRow = worksheet.Row(1).Cells().Select(c => c.Value.ToString()).ToList();
            ValidateColumns(headerRow, columns.Select(c => c.Name).ToList());

            DataTable dataTable = CreateDataTable(columns);
            var rows = worksheet.RangeUsed().RowsUsed();

            foreach (var row in rows.Skip(1)) // Skip header row
            {
                DataRow dataRow = dataTable.NewRow();
                for (int col = 1; col <= columns.Count; col++)
                {
                    var column = columns[col - 1];
                    dataRow[column.Name] = ConvertCellValue(row.Cell(col).Value, column.Type);
                }
                dataTable.Rows.Add(dataRow);
            }

            return dataTable;
        }
    }

    private void ValidateColumns(List<string> excelColumns, List<string> templateColumns)
    {
        var missingColumns = templateColumns.Except(excelColumns).ToList();
        if (missingColumns.Any())
        {
            throw new ArgumentException($"The following columns are missing in the Excel file: {string.Join(", ", missingColumns)}");
        }

        var extraColumns = excelColumns.Except(templateColumns).ToList();
        if (extraColumns.Any())
        {
            throw new ArgumentException($"The following columns in the Excel file are not defined in the template: {string.Join(", ", extraColumns)}");
        }
    }

    private DataTable CreateDataTable(List<ExcelColumn> columns)
    {
        DataTable dataTable = new DataTable();
        foreach (var column in columns)
        {
            dataTable.Columns.Add(new DataColumn(column.Name, column.Type));
        }
        return dataTable;
    }

    private object ConvertCellValue(object value, Type targetType)
    {
        if (value == null)
        {
            return DBNull.Value;
        }
        if (targetType == typeof(int))
        {
            return Convert.ToInt32(value);
        }
        if (targetType == typeof(decimal))
        {
            return Convert.ToDecimal(value);
        }
        if (targetType == typeof(double))
        {
            return Convert.ToDouble(value);
        }
        if (targetType == typeof(DateTime))
        {
            return Convert.ToDateTime(value);
        }
        if (targetType == typeof(bool))
        {
            return Convert.ToBoolean(value);
        }
        return value.ToString();
    }
}

public class ExcelColumn
{
    public string Name { get; set; }
    public Type Type { get; set; }
}
using System;
using System.Collections.Generic;

public static class ExcelTemplateConfigurations
{
    public static readonly Dictionary<string, List<ExcelColumn>> Templates = new Dictionary<string, List<ExcelColumn>>
    {
        { "Template1", new List<ExcelColumn>
            {
                new ExcelColumn { Name = "FundId", Type = typeof(int) },
                new ExcelColumn { Name = "Month", Type = typeof(int) },
                new ExcelColumn { Name = "Year", Type = typeof(int) },
                new ExcelColumn { Name = "Return", Type = typeof(double) }
            }
        },
        { "Template2", new List<ExcelColumn>
            {
                new ExcelColumn { Name = "ColumnA", Type = typeof(string) },
                new ExcelColumn { Name = "ColumnB", Type = typeof(int) },
                // Add more columns for Template2
            }
        },
        // Add more templates as needed
    };
}
using System;
using System.Data;
using System.IO;

class Program
{
    static void Main(string[] args)
    {
        var excelReader = new ExcelReader();
        string templateName = "Template1"; // Specify the template name here

        try
        {
            using (FileStream stream = new FileStream("path_to_your_excel_file.xlsx", FileMode.Open))
            {
                DataTable dataTable = excelReader.ReadExcelTemplate(stream, templateName);
                // Save dataTable to your database
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}
public class ExcelReader
{
    public List<T> ReadExcelTemplate<T>(Stream excelStream, string templateName) where T : new()
    {
        if (!ExcelTemplateConfigurations.Templates.TryGetValue(templateName, out var columns))
        {
            throw new ArgumentException($"Template '{templateName}' is not defined.");
        }

        using var workbook = new XLWorkbook(excelStream);
        var worksheet = workbook.Worksheet(1); // Assuming data is in the first worksheet
        var headerRow = worksheet.Row(1).Cells().Select(c => c.Value.ToString()).ToList();
        ValidateColumns(headerRow, columns.Select(c => c.Name).ToList());

        var result = new List<T>();
        var rows = worksheet.RangeUsed().RowsUsed().Skip(1); // Skip header row

        foreach (var row in rows)
        {
            var obj = new T();
            for (int col = 1; col <= columns.Count; col++)
            {
                var column = columns[col - 1];
                var property = typeof(T).GetProperty(column.Name.Replace(" ", ""), BindingFlags.Public | BindingFlags.Instance);
                if (property != null && property.CanWrite)
                {
                    property.SetValue(obj, ConvertCellValue(row.Cell(col).Value, column.Type));
                }
            }
            result.Add(obj);
        }

        return result;
    }

    private void ValidateColumns(List<string> excelColumns, List<string> templateColumns)
    {
        var missingColumns = templateColumns.Except(excelColumns).ToList();
        if (missingColumns.Any())
        {
            throw new ArgumentException($"The following columns are missing in the Excel file: {string.Join(", ", missingColumns)}");
        }

        var extraColumns = excelColumns.Except(templateColumns).ToList();
        if (extraColumns.Any())
        {
            throw new ArgumentException($"The following columns in the Excel file are not defined in the template: {string.Join(", ", extraColumns)}");
        }
    }

    private object ConvertCellValue(object value, Type targetType) => targetType switch
    {
        { } t when t == typeof(int) || t == typeof(int?) => value == null ? (object)null : Convert.ToInt32(value),
        { } t when t == typeof(decimal) || t == typeof(decimal?) => value == null ? (object)null : Convert.ToDecimal(value),
        { } t when t == typeof(double) || t == typeof(double?) => value == null ? (object)null : Convert.ToDouble(value),
        { } t when t == typeof(DateTime) || t == typeof(DateTime?) => value == null ? (object)null : Convert.ToDateTime(value),
        { } t when t == typeof(bool) || t == typeof(bool?) => value == null ? (object)null : Convert.ToBoolean(value),
        _ => value?.ToString()
    };
}
