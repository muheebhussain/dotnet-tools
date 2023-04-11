using Newtonsoft.Json.Linq;

namespace AppSettingsJsonToPoco
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.WriteLine("Usage: AppSettingsJsonToPoco <input_json_file> <output_directory>");
                return;
            }

            string inputJsonFile = args[0];
            string outputDirectory = args[1];

            try
            {
                // Load appsettings.json file
                string jsonString = File.ReadAllText(inputJsonFile);
                JObject jsonSettings = JObject.Parse(jsonString);

                // Create output directory if it doesn't exist
                Directory.CreateDirectory(outputDirectory);

                // Iterate through each section
                foreach (var section in jsonSettings)
                {
                    // Generate POCO class
                    string className = $"{section.Key}Config";
                    string classContent = GeneratePocoClass(className, section.Value as JObject);

                    // Save POCO class to file
                    string outputClassFile = Path.Combine(outputDirectory, $"{className}.cs");
                    File.WriteAllText(outputClassFile, classContent);

                    Console.WriteLine($"Generated POCO class: {outputClassFile}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        private static string GeneratePocoClass(string className, JObject section)
        {
            string indent = "    ";
            string classTemplate = "public class {0}\n{{\n{1}}}\n";
            string propertyTemplate = "{0}public {1} {2} {{ get; set; }}\n";

            string properties = string.Empty;

            foreach (var property in section)
            {
                string typeName = GetTypeName(property.Value.Type, property.Value as JObject);
                string propertyName = property.Key;
                string propertyDefinition = string.Format(propertyTemplate, indent, typeName, propertyName);
                properties += propertyDefinition;
            }

            return string.Format(classTemplate, className, properties);
        }

        private static string GetTypeName(JTokenType type, JObject obj = null)
        {
            switch (type)
            {
                case JTokenType.String:
                    return "string";
                case JTokenType.Integer:
                    return "int";
                case JTokenType.Float:
                    return "double";
                case JTokenType.Boolean:
                    return "bool";
                case JTokenType.Object:
                    if (obj != null)
                    {
                        string className = $"{obj.Path.Replace(".", "_")}_Config";
                        string classContent = GeneratePocoClass(className, obj);
                        File.WriteAllText($"{className}.cs", classContent);
                        return className;
                    }
                    else
                    {
                        return "object";
                    }
                default:
                    return "object";
            }
        }
    }
}
