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
        private const string outputDirectory = @"Path\To\Test.Model\Project";

        public static void GenerateModel(string entityName)
        {
            Type entityType = Assembly.GetExecutingAssembly().GetTypes()
                .FirstOrDefault(t => t.Name.Equals(entityName + "Entity", StringComparison.Ordinal));

            if (entityType == null)
            {
                Console.WriteLine($"Entity type {entityName}Entity not found.");
                return;
            }

            var properties = entityType.GetProperties()
                .Where(p => p.PropertyType.IsPrimitive || p.PropertyType == typeof(string) || p.PropertyType == typeof(DateTime)); // Add more simple types as needed

            string modelName = entityName + "Model";
            StringBuilder classBuilder = new StringBuilder();
            classBuilder.AppendLine("using System;");
            classBuilder.AppendLine("namespace Test.Model");
            classBuilder.AppendLine("{");
            classBuilder.AppendLine($"    public class {modelName} : IModel");
            classBuilder.AppendLine("    {");

           foreach (var prop in properties)
            {
                Type propType = prop.PropertyType;
                string typeName = propType.IsGenericType && propType.GetGenericTypeDefinition() == typeof(Nullable<>)
                                  ? Nullable.GetUnderlyingType(propType).Name + "?"
                                  : propType.Name;

                if (propType.IsGenericType && typeof(IEnumerable<>).IsAssignableFrom(propType.GetGenericTypeDefinition()))
                {
                    Type genericTypeArgument = propType.GetGenericArguments()[0];
                    typeName = $"IEnumerable<{genericTypeArgument.Name}>";
                }

                string propName = prop.Name;
                string defaultValue = propType == typeof(string) ? " = null!;" : "";

                classBuilder.AppendLine($"        public {typeName} {propName} {{ get; set; }}{defaultValue}");
            }

            classBuilder.AppendLine("    }");
            classBuilder.AppendLine("}");

            string outputPath = Path.Combine(outputDirectory, $"{modelName}.cs");
            File.WriteAllText(outputPath, classBuilder.ToString());
        }
    }
}
