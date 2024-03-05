using System;
using System.Collections.Generic;
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
                var typeName = prop.PropertyType.Name;
                var propName = prop.Name;
                
                // Handling non-nullable strings to be nullable in the model
                if (prop.PropertyType == typeof(string) && !prop.PropertyType.IsGenericType)
                {
                    typeName = typeName + "?";
                }

                classBuilder.AppendLine($"        public {typeName} {propName} {{ get; set; }}");
            }

            classBuilder.AppendLine("    }");
            classBuilder.AppendLine("}");

            File.WriteAllText($"{entityType.Name}Model.cs", classBuilder.ToString());
        }
    }
}
