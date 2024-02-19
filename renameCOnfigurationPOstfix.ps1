$folderPath = "C:\Path\To\Your\CSharp\Project\Folder" # Update this with your project's path

# Get all files ending with "Configuration.cs"
$files = Get-ChildItem -Path $folderPath -Filter *Configuration.cs

foreach ($file in $files) {
    # Determine the new file name
    $newFileName = $file.Name -replace "Configuration\.cs$", "EntityConfiguration.cs"
    $newFilePath = Join-Path -Path $file.DirectoryName -ChildPath $newFileName

    # Read the content of the file
    $content = Get-Content -Path $file.FullName -Raw

    # Extract the original class name and determine the new class name
    if ($content -match 'class\s+(\w+Configuration)') {
        $originalClassName = $matches[1]
        $newClassName = $originalClassName -replace "Configuration$", "EntityConfiguration"

        # Replace the class name in the file content
        $newContent = $content -replace "\b$originalClassName\b", $newClassName

        # Write the updated content to the new file path
        $newContent | Set-Content -Path $newFilePath

        # Optionally, remove the original file if the rename was successful
        if (Test-Path -Path $newFilePath) {
            Remove-Item -Path $file.FullName
        }

        Write-Host "Renamed $file to $newFileName and updated class name to $newClassName"
    }
}
