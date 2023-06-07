using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Please enter the path of the parent directory:");
        string dirPath = Console.ReadLine();

        string[] dirsToSkip = { "bin", "obj", "dist", "node_modules", ".angular" };

        // Create a temp directory for the files that will be included in the zip
        string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);

        try
        {
            // Copy files to the temp directory, skipping the specified subfolders
            CopyDirectory(dirPath, tempDir, dirsToSkip);

            // Create the zip file from the temp directory
            string zipFilePath = $"{dirPath}.zip";
            ZipFile.CreateFromDirectory(tempDir, zipFilePath);

            Console.WriteLine($"Created zip file: {zipFilePath}");
        }
        finally
        {
            // Clean up the temp directory
            Directory.Delete(tempDir, true);
        }
    }

    private static void CopyDirectory(string sourceDir, string targetDir, string[] dirsToSkip)
    {
        // Get all subdirectories
        var dir = new DirectoryInfo(sourceDir);
        var dirs = dir.GetDirectories();

        // If the target directory doesn't exist, create it
        if (!Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        // Get the files in the directory and copy them to the new location, if they aren't in a skipped directory
        FileInfo[] files = dir.GetFiles();
        foreach (FileInfo file in files)
        {
            string temppath = Path.Combine(targetDir, file.Name);
            file.CopyTo(temppath, false);
        }

        // If the directory has subdirectories, copy them
        foreach (DirectoryInfo subdir in dirs)
        {
            if (dirsToSkip.Any(dir => subdir.FullName.Contains(Path.DirectorySeparatorChar + dir + Path.DirectorySeparatorChar)))
            {
                continue;
            }

            string temppath = Path.Combine(targetDir, subdir.Name);
            CopyDirectory(subdir.FullName, temppath, dirsToSkip);
        }
    }
}
