using System;
using System.IO;
using System.Text.RegularExpressions;

class Program
{
    static void Main(string[] args)
    {
        var sourceFilePath = @"Path\To\YourClass.cs"; // Update this path
        var destinationFilePath = @"Path\To\OutputClassProperties.txt"; // Update this path

        try
        {
            using (var reader = new StreamReader(sourceFilePath))
            using (var writer = new StreamWriter(destinationFilePath))
            {
                string line;
                bool isPropertyBlock = false;

                while ((line = reader.ReadLine()) != null)
                {
                    // Check for a public property. This pattern assumes simple property declarations and might need adjustments for your specific cases.
                    if (Regex.IsMatch(line, @"^\s*public\s+(?!class)(?!static)(?!void)[^\s]+\s+[^\s]+\s*\{\s*get;.*set;\s*\}\s*$") && !line.Contains("virtual"))
                    {
                        isPropertyBlock = true;
                    }
                    else
                    {
                        // Not a property or a start of a non-property block
                        isPropertyBlock = false;
                    }

                    // Write the property line if it's not part of a block to be skipped
                    if (isPropertyBlock)
                    {
                        writer.WriteLine(line);
                    }
                }
            }

            Console.WriteLine("Properties have been extracted successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("An error occurred: " + ex.Message);
        }
    }
}
