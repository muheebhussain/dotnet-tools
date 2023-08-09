# Define the directory to start the search in
$startDirectory = "C:\path\to\your\directory"

# This will hold the results
$results = @()

# Get all the .csproj files from the directory and sub directories
$csprojFiles = Get-ChildItem -Path $startDirectory -Recurse -Filter *.csproj

# Iterate through each .csproj file and check if it contains the term "Moq"
foreach ($file in $csprojFiles) {
    if (Select-String -Path $file.FullName -Pattern 'Moq' -Quiet) {
        $results += [PSCustomObject]@{
            "Folder"       = $file.DirectoryName
            "Project Name" = $file.BaseName
        }
    }
}

# If there are results, write them to a text file
if ($results.Count -gt 0) {
    $results | Export-Csv -Path "C:\path\to\output.txt" -Delimiter "`t" -NoTypeInformation
} else {
    Write-Host "No matching .csproj files found."
}
