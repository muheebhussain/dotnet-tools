# Define the path to the solution file
$solutionPath = "C:\Path\To\Your\Solution.sln"

# Extract project paths from the solution file
$projectPaths = (Get-Content $solutionPath | Where-Object { $_ -match "\.csproj" }) -replace '^.+=", "' -replace '".*$'

# Define a list to hold project information
$exeProjects = @()

# Iterate over each project path
foreach ($relativePath in $projectPaths) {
    $fullPath = Join-Path (Split-Path $solutionPath) $relativePath
    $projectName = Split-Path $relativePath -Leaf

    # Load the project file as XML
    [xml]$projectXml = Get-Content $fullPath

    # Check if the project's OutputType is 'Exe'
    if ($projectXml.Project.PropertyGroup.OutputType -eq 'Exe') {
        $references = @($projectXml.Project.ItemGroup.ProjectReference | ForEach-Object {
            $refPath = $_.Include
            $refName = [System.IO.Path]::GetFileNameWithoutExtension($refPath)
            @{ Name = $refName; Path = $refPath }
        })

        # Add project details to the list
        $exeProjects += @{
            Name = $projectName
            Path = $fullPath
            References = $references
        }
    }
}

# Display the exe projects and their references
foreach ($project in $exeProjects) {
    Write-Output "`nProject: $($project.Name)"
    Write-Output "Location: $($project.Path)"
    if ($project.References.Count -gt 0) {
        $project.References | Format-Table -Property Name, Path
    } else {
        Write-Output "No references found."
    }
}
