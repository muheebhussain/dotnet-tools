param(
    [Parameter(Mandatory=$true)]
    [string]$parentPath,

    [string[]]$skipFolders = @('bin', 'obj', 'dist', 'node_modules', '.angular')
)

# Check if the parent path exists
if (!(Test-Path $parentPath)) {
    Write-Output "The parent path provided does not exist."
    return
}

$destinationPath = Join-Path $parentPath "ParentFolder.zip"

# Get all sub-directories except the ones in skip list
$subDirectories = Get-ChildItem -Path $parentPath -Directory | Where-Object { $skipFolders -notcontains $_.Name }

# Delete zip if it exists already
if (Test-Path $destinationPath) {
    Remove-Item $destinationPath
}

# Create a temporary folder
$tempFolder = New-Item -ItemType Directory -Path (Join-Path $parentPath "TempFolder")

# Copy necessary folders and files to temporary folder
foreach($dir in $subDirectories) {
    Copy-Item -Path $dir.FullName -Destination $tempFolder.FullName -Recurse
}
Copy-Item -Path (Join-Path $parentPath "*.*") -Destination $tempFolder.FullName

# Create zip
Compress-Archive -Path $tempFolder.FullName -DestinationPath $destinationPath

# Remove temporary folder
Remove-Item -Path $tempFolder.FullName -Recurse

Write-Output "Compression is complete. You can find the zip file at $destinationPath."
