using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

class Program
{
    static string ExtractPublicProperties(string sourceFilePath)
    {
        StringBuilder extractedProperties = new StringBuilder();

        try
        {
            using (var reader = new StreamReader(sourceFilePath))
            {
                string line;
                bool isPropertyLine = false;

               while ((line = reader.ReadLine()) != null)
                {
                    // Updated check for a public property, excluding virtual properties
                    if (Regex.IsMatch(line, @"^\s*public\s+((?!virtual).)*\s+[^\s]+\s*\{\s*get;.*set;\s*\}\s*$"))
                    {
                        extractedProperties.AppendLine(line);
                    }
                }
            }

            return extractedProperties.ToString();
        }
        catch (Exception ex)
        {
            Console.WriteLine("An error occurred: " + ex.Message);
            return null;
        }
    }

    static void Main(string[] args)
    {
        var sourceFilePath = @"Path\To\YourClass.cs"; // Update this path
        var propertiesString = ExtractPublicProperties(sourceFilePath);

        if (propertiesString != null)
        {
            Console.WriteLine("Extracted Properties:");
            Console.WriteLine(propertiesString);
        }
    }
}
