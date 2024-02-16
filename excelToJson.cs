using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ExcelToJSON
{
    public class Program
    {
        public static void Main(string[] args)
        {
            string excelFilePath = @"path\to\your\excel\file.xlsx"; // Change this to your Excel file path
            var migrateTables = new MigrateTables
            {
                Tables = ReadExcelFile(excelFilePath, "Transformation").ToArray()
            };

            string json = JsonConvert.SerializeObject(migrateTables, Formatting.Indented);
            File.WriteAllText(@"path\to\save\migratetable.json", json); // Specify your desired path here
            Console.WriteLine("Migration table JSON has been saved successfully.");
        }

        private static List<Tables> ReadExcelFile(string filePath, string sheetName)
        {
            var tablesList = new List<Tables>();

            using (var document = SpreadsheetDocument.Open(filePath, false))
            {
                var workbookPart = document.WorkbookPart;
                var sheet = workbookPart.Workbook.Descendants<Sheet>().FirstOrDefault(s => s.Name == sheetName);
                if (sheet == null)
                {
                    throw new ArgumentException($"Sheet {sheetName} not found", nameof(sheetName));
                }

                WorksheetPart worksheetPart = (WorksheetPart)(workbookPart.GetPartById(sheet.Id));
                SheetData sheetData = worksheetPart.Worksheet.Elements<SheetData>().First();
                foreach (Row row in sheetData.Elements<Row>().Skip(1)) // Skipping header row
                {
                    var cellValues = row.Elements<Cell>().Select(cell => GetCellValue(document, cell)).ToArray();

                    var tableName = cellValues[0];
                    var newClassName = cellValues[1];
                    var columnName = cellValues[2];
                    var propertyName = cellValues[3];

                    var table = tablesList.FirstOrDefault(t => t.Name == tableName && t.NewName == newClassName);
                    if (table == null)
                    {
                        table = new Tables
                        {
                            Name = tableName,
                            NewName = newClassName,
                            Columns = new List<Column>()
                        };
                        tablesList.Add(table);
                    }

                    table.Columns.Add(new Column
                    {
                        Name = columnName,
                        NewName = propertyName
                    });
                }
            }

            return tablesList;
        }

        private static string GetCellValue(SpreadsheetDocument document, Cell cell)
        {
            string value = cell.InnerText;
            if (cell.DataType != null && cell.DataType.Value == CellValues.SharedString)
            {
                return document.WorkbookPart.SharedStringTablePart.SharedStringTable.Elements<SharedStringItem>().ElementAt(int.Parse(value)).InnerText;
            }
            return value;
        }
    }

    public class MigrateTables
    {
        public List<Tables> Tables { get; set; }
    }

    public class Tables
    {
        public string Name { get; set; }
        public string NewName { get; set; }
        public List<Column> Columns { get; set; }
    }

    public class Column
    {
        public string Name { get; set; }
        public string NewName { get; set; }
    }
}
