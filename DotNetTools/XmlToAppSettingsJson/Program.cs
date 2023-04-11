using System;
using System.IO;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

//dotnet run "config.xml" "appsettings.json"
namespace XmlToAppSettingsJson
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.WriteLine("Usage: XmlToAppSettingsJson <input_xml_file> <output_json_file>");
                return;
            }

            string inputXmlFile = args[0];
            string outputJsonFile = args[1];

            try
            {
                // Load XML file
                XDocument xmlDocument = XDocument.Load(inputXmlFile);

                // Convert XML to JSON
                string jsonString = JsonConvert.SerializeXNode(xmlDocument, Formatting.Indented);

                // Load JSON string as JObject
                JObject jsonSettings = JObject.Parse(jsonString);

                // Remove XML declaration if exists
                if (jsonSettings.ContainsKey("?xml"))
                {
                    jsonSettings.Remove("?xml");
                }

                // Save JSON object to appsettings.json file
                File.WriteAllText(outputJsonFile, jsonSettings.ToString(Formatting.Indented));

                Console.WriteLine($"Successfully converted '{inputXmlFile}' to '{outputJsonFile}'.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }
}