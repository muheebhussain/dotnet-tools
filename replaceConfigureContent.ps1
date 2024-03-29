$folderPath = "C:\Path\To\Your\CSharp\Project\Folder" # Update with your actual folder path

# Get all C# files in the folder
$files = Get-ChildItem -Path $folderPath -Filter *.cs

foreach ($file in $files) {
    # Read the file content
    $content = Get-Content -Path $file.FullName -Raw

    # Pattern to find the Configure method and its content
    $pattern = '(public\s+void\s+Configure\s*\(\s*EntityTypeBuilder<\w+>\s*\w+\s*\)\s*\{)(.*?)(^\s*\})' -replace '\s', '\s*'

    # Check if the current file contains the Configure method
    if ($content -match $pattern) {
        # Extract the Configure method's content
        $methodContent = $matches[2]

        # Keep only lines starting with "entity.Property"
        $filteredContent = ($methodContent -split "\r?\n" | Where-Object { $_.Trim().StartsWith("entity.Property") }) -join "`n"

        # Reconstruct the method with only the filtered lines
        $newMethod = $matches[1] + "`n" + $filteredContent + "`n" + $matches[3]

        # Replace the old method content with the new filtered content in the file's content
        $newContent = $content -replace [regex]::Escape($matches[0]), $newMethod

        # Write the updated content back to the file
        Set-Content -Path $file.FullName -Value $newContent

        Write-Host "Updated file: $file"
    }
    else {
        Write-Host "No Configure method found in file: $file or it does not match the expected pattern."
    }
}



$folderPath = "C:\Path\To\Your\CSharp\Project\Folder" # Update with the path to your project

# Define the regex pattern to match [Table attributes followed by a blank line
$pattern = '(\[Table[^\]]*\]\r?\n)\r?\n'

# Get all C# files in the specified folder
$files = Get-ChildItem -Path $folderPath -Filter *.cs -Recurse

foreach ($file in $files) {
    # Read the content of the file
    $content = Get-Content -Path $file.FullName -Raw

    # Replace the matched pattern ([Table attribute followed by blank line) with just the attribute
    $updatedContent = $content -replace $pattern, '$1'

    # Write the updated content back to the file
    Set-Content -Path $file.FullName -Value $updatedContent

    Write-Host "Processed file: $($file.Name)"
}

Write-Host "Completed processing files."
