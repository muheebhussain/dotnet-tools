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

using System;
using Microsoft.Build.Locator;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Construction;
using System.Linq;
using System.Collections.Generic;

class Program
{
    static void Main(string[] args)
    {
        // Register an instance of MSBuild
        MSBuildLocator.RegisterDefaults();

        // Specify the path to your solution file
        string solutionPath = @"C:\Path\To\Your\Solution.sln";

        // Load the solution file
        var solutionFile = SolutionFile.Parse(solutionPath);
        var projects = solutionFile.ProjectsInOrder;

        // Use MSBuild to load each project
        var pc = new ProjectCollection();
        List<Project> exeProjects = new List<Project>();

        foreach (var project in projects.Where(p => p.ProjectType == SolutionProjectType.KnownToBeMSBuildFormat))
        {
            var loadedProject = pc.LoadProject(project.AbsolutePath);
            var outputType = loadedProject.GetProperty("OutputType")?.EvaluatedValue;
            if (outputType == "Exe")
            {
                exeProjects.Add(loadedProject);
            }
        }

        // Display executable projects and their references
        foreach (var project in exeProjects)
        {
            Console.WriteLine($"Project: {project.GetPropertyValue("ProjectName")}");
            Console.WriteLine($"Location: {project.FullPath}");
            Console.WriteLine("References:");

            var references = project.Items.Where(item => item.ItemType == "ProjectReference");
            foreach (var reference in references)
            {
                Console.WriteLine($"  {reference.EvaluatedInclude}");
            }

            Console.WriteLine();
        }

        // Ensure to unload all projects
        pc.UnloadAllProjects();
    }
}
using System;
using Microsoft.Build.Locator;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Construction;
using System.Linq;
using System.Collections.Generic;

class Program
{
    static void Main(string[] args)
    {
        // Register an instance of MSBuild
        MSBuildLocator.RegisterDefaults();

        // Specify the path to your solution file
        string solutionPath = @"C:\Path\To\Your\Solution.sln";

        // Load the solution file
        var solutionFile = SolutionFile.Parse(solutionPath);
        var projects = solutionFile.ProjectsInOrder;

        // Use MSBuild to load each project
        var pc = new ProjectCollection();
        List<Project> exeProjects = new List<Project>();

        foreach (var project in projects.Where(p => p.ProjectType == SolutionProjectType.KnownToBeMSBuildFormat))
        {
            var loadedProject = pc.LoadProject(project.AbsolutePath);
            var outputType = loadedProject.GetProperty("OutputType")?.EvaluatedValue;
            if (outputType == "Exe")
            {
                exeProjects.Add(loadedProject);
            }
        }

        // Display executable projects and their references
        foreach (var project in exeProjects)
        {
            Console.WriteLine($"Project: {project.GetPropertyValue("ProjectName")}");
            Console.WriteLine($"Location: {project.FullPath}");
            Console.WriteLine("References:");

            var references = project.Items.Where(item => item.ItemType == "ProjectReference");
            foreach (var reference in references)
            {
                Console.WriteLine($"  {reference.EvaluatedInclude}");
            }

            Console.WriteLine();
        }

        // Ensure to unload all projects
        pc.UnloadAllProjects();
    }
}
using System;
using Microsoft.Build.Locator;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Construction;
using System.Linq;
using System.Collections.Generic;

class Program
{
    static void Main(string[] args)
    {
        // Register an instance of MSBuild
        MSBuildLocator.RegisterDefaults();

        // Specify the path to your solution file
        string solutionPath = @"C:\Path\To\Your\Solution.sln";

        // Load the solution file
        var solutionFile = SolutionFile.Parse(solutionPath);
        var projects = solutionFile.ProjectsInOrder;

        // Use MSBuild to load each project
        var pc = new ProjectCollection();
        List<Project> exeProjects = new List<Project>();

        foreach (var project in projects.Where(p => p.ProjectType == SolutionProjectType.KnownToBeMSBuildFormat))
        {
            var loadedProject = pc.LoadProject(project.AbsolutePath);
            var outputType = loadedProject.GetProperty("OutputType")?.EvaluatedValue;
            if (outputType == "Exe")
            {
                exeProjects.Add(loadedProject);
            }
        }

        // Display executable projects and their references
        foreach (var project in exeProjects)
        {
            Console.WriteLine($"Project: {project.GetPropertyValue("ProjectName")}");
            Console.WriteLine($"Location: {project.FullPath}");
            Console.WriteLine("References:");

            var references = project.Items.Where(item => item.ItemType == "ProjectReference");
            foreach (var reference in references)
            {
                Console.WriteLine($"  {reference.EvaluatedInclude}");
            }

            Console.WriteLine();
        }

        // Ensure to unload all projects
        pc.UnloadAllProjects();
    }
}
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net6.0</TargetFramework> <!-- You can use net5.0, netcoreapp3.1, etc., depending on your environment -->
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Build.Locator" Version="1.4.1" />
    <PackageReference Include="Microsoft.Build" Version="17.0.0" />
    <PackageReference Include="Microsoft.Build.Framework" Version="17.0.0" />
    <PackageReference Include="Microsoft.Build.Utilities.Core" Version="17.0.0" />
  </ItemGroup>

</Project>
