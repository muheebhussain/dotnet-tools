using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

public class SchemaTrigger
{
    public string TableName { get; set; }
    public string TriggerName { get; set; }
}

class Program
{
    static void Main(string[] args)
    {
        // Read JSON file content
        string json = File.ReadAllText("schemaTriggers.json");

        // Deserialize JSON content to list of SchemaTrigger objects
        List<SchemaTrigger> schemaTriggers = JsonConvert.DeserializeObject<List<SchemaTrigger>>(json);

        // Group schema triggers by table name
        var groupedSchemaTriggers = schemaTriggers.GroupBy(st => st.TableName);

        // Generate code for each table with associated triggers
        using (StreamWriter outputFile = new StreamWriter("output.txt"))
        {
            foreach (var group in groupedSchemaTriggers)
            {
                // Write comment with table name
                outputFile.WriteLine("//TableName: {0}", group.Key);

                outputFile.WriteLine("builder.ToTable(tb =>");
                outputFile.WriteLine("{");

                // Write code for each trigger associated with the current table
                foreach (var schemaTrigger in group)
                {
                    outputFile.WriteLine("\ttb.HasTrigger(\"{0}\");", schemaTrigger.TriggerName);
                }

                outputFile.WriteLine("});");
                outputFile.WriteLine();
            }
        }

        Console.WriteLine("Code generated successfully in output.txt");
    }
}
