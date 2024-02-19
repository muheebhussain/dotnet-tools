$folderPath = "C:\Path\To\Your\CSharp\Project" # Update this with your project's path

# Get all C# files in the specified folder
$files = Get-ChildItem -Path $folderPath -Filter *.cs -Recurse

foreach ($file in $files) {
    # Read the file content
    $content = Get-Content -Path $file.FullName -Raw

    # Attempt to extract the class name using a simple regex pattern
    if ($content -match 'class\s+(\w+)') {
        $currentClassName = $matches[1]
        $expectedClassName = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)

        # Check if the class name matches the file name (without extension)
        if ($currentClassName -ne $expectedClassName) {
            # Replace the class name in the file content
            $newContent = $content -replace "\b$currentClassName\b", $expectedClassName

            # Write the updated content back to the file
            $newContent | Set-Content -Path $file.FullName

            Write-Host "Renamed class $currentClassName to $expectedClassName in file $file"
        }
    }
}
