# Define the path to the solution file
$solutionPath = "C:\Path\To\Your\Solution.sln"

# Extract project details from the solution file
$projects = Get-Content $solutionPath | Where-Object { $_ -match "\.csproj" } | ForEach-Object {
    $start = $_.IndexOf('"') + 1
    $end = $_.LastIndexOf('"')
    $relativePath = $_.Substring($start, $end - $start)
    $projectPath = Join-Path (Split-Path $solutionPath) $relativePath
    $projectName = Split-Path $relativePath -Leaf
    @{
        Name = $projectName
        Path = $projectPath
        ProjectType = ''
        References = @()
    }
}

# Load project details
foreach ($project in $projects) {
    [xml]$xmlContent = Get-Content $project.Path
    $project.ProjectType = $xmlContent.Project.PropertyGroup.OutputType
    $project.References = $xmlContent.Project.ItemGroup.Reference | Where-Object { $_.Include } | ForEach-Object { $_.Include }
}

# Display library projects in tabular format
$libraryProjects = $projects | Where-Object { $_.ProjectType -eq 'Library' }
$libraryProjects | Format-Table -Property Name, Path

# Display executable projects and their references in tabular format
$exeProjects = $projects | Where-Object { $_.ProjectType -eq 'Exe' }
foreach ($project in $exeProjects) {
    Write-Output "`nProject: $($project.Name)"
    Write-Output "Location: $($project.Path)"
    $project.References | Format-Table -Property @{Label="Referenced Libraries"; Expression={$_}}
}
