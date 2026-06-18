param(
    [string]$Solution = "WinPet.sln"
)

$ErrorActionPreference = "Stop"
$json = & dotnet list $Solution package `
    --vulnerable `
    --include-transitive `
    --format json
if ($LASTEXITCODE -ne 0) {
    throw "NuGet vulnerability scan failed."
}

$report = $json | ConvertFrom-Json
$vulnerabilities = @(
    foreach ($project in $report.projects) {
        foreach ($framework in @($project.frameworks)) {
            foreach ($packageGroup in @(
                $framework.topLevelPackages,
                $framework.transitivePackages
            ) | Where-Object { $null -ne $_ }) {
                foreach ($package in @($packageGroup) |
                    Where-Object { $null -ne $_ }) {
                    foreach ($vulnerability in @($package.vulnerabilities) |
                        Where-Object { $null -ne $_ }) {
                        [pscustomobject]@{
                            Project = $project.path
                            Framework = $framework.framework
                            Package = $package.id
                            Version = $package.resolvedVersion
                            Severity = $vulnerability.severity
                            Advisory = $vulnerability.advisoryurl
                        }
                    }
                }
            }
        }
    }
)

if ($vulnerabilities.Count -gt 0) {
    $vulnerabilities | Format-Table | Out-String | Write-Error
    throw "Known NuGet vulnerabilities were found."
}

Write-Output "No known NuGet vulnerabilities found."
