# Define the path to your folder
$folderPath = "C:\Path\To\Your\Folder" # Update this with the actual path to your folder

# Get all the files in the folder
$files = Get-ChildItem -Path $folderPath -File

foreach ($file in $files) {
    # Extract the base name and extension of the file
    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
    $extension = [System.IO.Path]::GetExtension($file.Name)

    # Create the new filename by adding "Entity" postfix before the extension
    $newFileName = "$baseName" + "Entity" + "$extension"

    # Define the full path for the new file
    $newFilePath = Join-Path -Path $folderPath -ChildPath $newFileName

    # Rename the file
    Rename-Item -Path $file.FullName -NewName $newFilePath
}

Write-Host "Files have been renamed successfully."
