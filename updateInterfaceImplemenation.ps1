$folderPath = "C:\Path\To\Your\CSharp\Project\Folder" # Update this with your project's path

# Get all C# class files
$files = Get-ChildItem -Path $folderPath -Filter *.cs

foreach ($file in $files) {
    # Read the content of the file
    $content = Get-Content -Path $file.FullName -Raw

    # Check if the file's class already implements IEntity
    if (-not ($content -match "class\s+\w+\s*:\s*IEntity")) {
        # Check for class declaration and add IEntity implementation
        $newContent = $content -replace "(class\s+\w+)(\s*\{)", '$1 : IEntity$2'

        # Write the updated content back to the file
        Set-Content -Path $file.FullName -Value $newContent

        Write-Host "Updated file $file to implement IEntity interface."
    }
    else {
        Write-Host "File $file already implements IEntity interface. No changes made."
    }
}
