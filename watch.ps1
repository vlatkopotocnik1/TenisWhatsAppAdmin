param(
    [ValidateSet("Development", "Production")]
    [string]$Environment = "Development"
)

$ErrorActionPreference = "Stop"

$projectPath = "WhatsAppAdmin.csproj"

if (-not (Test-Path $projectPath)) {
    Write-Error "Project file '$projectPath' not found. Make sure you run this script from the repository root."
    exit 1
}

$urlsByEnvironment = @{
    Development = "https://localhost:7232;http://localhost:5008"
    Production  = "https://localhost:7233;http://localhost:5010"
}

$env:ASPNETCORE_ENVIRONMENT = $Environment
$urls = $urlsByEnvironment[$Environment]

Write-Host "Launching 'dotnet watch' for $projectPath with ASPNETCORE_ENVIRONMENT=$Environment"
Write-Host "When the build completes, open $urls in your browser and refresh after making changes."

dotnet watch --project $projectPath run --urls $urls

