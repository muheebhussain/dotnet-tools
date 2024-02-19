$folderPath = "C:\Path\To\Your\Folder" # Update this path to the folder you want to scan

# Get all files in the specified folder without their extensions
$files = Get-ChildItem -Path $folderPath -File | ForEach-Object { $_.BaseName }

foreach ($file in $files) {
    # Define variables for filenames
    $FileNameWithoutPostfix = $file
    $FileNameWithPostfix = $file + "Configuration"

    # Define the content of the new file, replacing placeholders with actual variable values
    $fileContent = @"
using UnusedEntities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataAccess.EntityConfigurations;

public class $FileNameWithPostfix : IEntityTypeConfiguration<$FileNameWithoutPostfix>
{
	public void Configure(EntityTypeBuilder<$FileNameWithoutPostfix> entity)
	{

	}
}
"@

    # Specify the path and filename for the new file
    $newFilePath = Join-Path -Path $folderPath -ChildPath ("$FileNameWithPostfix.cs")

    # Create the new file with the defined content
    $fileContent | Out-File -FilePath $newFilePath -Encoding UTF8
}
