using System;
using System.Collections.Generic;
using System.IO;

namespace RecordTypeCounter
{
    class Program
    {
        static void Main(string[] args)
        {
            // If a file path is provided as a command-line argument, use it.
            // Otherwise, specify a default path here or prompt the user for input.
            string filePath = args.Length > 0 
                ? args[0] 
                : @"C:\path\to\your\huge_file.txt";

            if (!File.Exists(filePath))
            {
                Console.WriteLine($"File not found: {filePath}");
                return;
            }

            // Dictionary to track counts by record type
            var recordTypeCounts = new Dictionary<string, long>();

            // Read the file line by line
            using (var reader = new StreamReader(filePath))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    // Skip empty lines if present
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    // Split by pipe delimiter
                    var columns = line.Split('|');

                    // The record type is assumed to be the first column
                    if (columns.Length > 0)
                    {
                        string recordType = columns[0];

                        // Increment count for this record type
                        if (!recordTypeCounts.ContainsKey(recordType))
                        {
                            recordTypeCounts[recordType] = 0;
                        }
                        recordTypeCounts[recordType]++;
                    }
                }
            }

            // Output results
            Console.WriteLine("Record Type Counts:");
            foreach (var kvp in recordTypeCounts)
            {
                Console.WriteLine($"Record Type: {kvp.Key}, Count: {kvp.Value}");
            }

            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}
