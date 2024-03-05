using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace YourNamespace
{
    public interface IModel
    {
    }

    public class ModelGenerator
    {
        public static void GenerateModel(Type entityType)
        {
            var properties = entityType.GetProperties()
                .Where(p => p.PropertyType.IsPrimitive || p.PropertyType == typeof(string) || p.PropertyType == typeof(DateTime)); // Add more simple types as needed

            StringBuilder classBuilder = new StringBuilder();
            classBuilder.AppendLine("using System;");
            classBuilder.AppendLine("namespace YourNamespace.Models");
            classBuilder.AppendLine("{");
            classBuilder.AppendLine($"    public class {entityType.Name}Model : IModel");
            classBuilder.AppendLine("    {");

            foreach (var prop in properties)
            {
                string typeName = prop.PropertyType.Name;
                string propName = prop.Name;
                string defaultValue = "";

                // Adjust non-nullable string properties to have a default value of null!
                if (prop.PropertyType == typeof(string))
                {
                    defaultValue = " = null!";
                }

                classBuilder.AppendLine($"        public {typeName} {propName} {{ get; set; }}{defaultValue}");
            }

            classBuilder.AppendLine("    }");
            classBuilder.AppendLine("}");

            File.WriteAllText($"{entityType.Name}Model.cs", classBuilder.ToString());
        }
    }
}
