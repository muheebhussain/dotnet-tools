param(
    [Parameter(Mandatory=$true)]
    [string]$startDirectory,

    [Parameter(Mandatory=$true)]
    [string]$outputFile
)

# This will hold the results
$results = @()

# Counter variables for the summary
$folderCount = 0
$csprojCount = 0
$matchingCsprojCount = 0

# Get all the directories within the start directory and sub-directories
$directories = Get-ChildItem -Path $startDirectory -Recurse -Directory

# For each directory, scan the .csproj files
foreach ($dir in $directories) {
    Write-Host "Scanning directory: $($dir.FullName)"
    $folderCount++

    # Get all the .csproj files in the current directory
    $csprojFiles = Get-ChildItem -Path $dir.FullName -Filter *.csproj
    $csprojCount += $csprojFiles.Count

    # Iterate through each .csproj file and check if it contains the term "Moq"
    foreach ($file in $csprojFiles) {
        if (Select-String -Path $file.FullName -Pattern 'Moq' -Quiet) {
            $matchingCsprojCount++
            $results += [PSCustomObject]@{
                "Folder"       = $file.DirectoryName
                "Project Name" = $file.BaseName
            }
        }
    }
}

# If there are results, write them to the specified output file
if ($results.Count -gt 0) {
    $results | Export-Csv -Path $outputFile -Delimiter "`t" -NoTypeInformation
}

# Print summary
Write-Host "Summary:"
Write-Host "Number of folders scanned: $folderCount"
Write-Host "Number of .csproj files scanned: $csprojCount"
Write-Host "Number of .csproj files containing 'Moq': $matchingCsprojCount"
