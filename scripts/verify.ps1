[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$verificationRoot = Join-Path ([IO.Path]::GetTempPath()) (
    "CameraMaintainer-verify-" + [guid]::NewGuid().ToString("N"))
$previousRenderedMarkupRoot = $env:CAMERAMAINTAINER_RENDERED_MARKUP_DIR

function Assert-CommandSucceeded {
    param([Parameter(Mandatory)][string]$Step)

    if ($LASTEXITCODE -ne 0) {
        throw "$Step failed with exit code $LASTEXITCODE."
    }
}

Push-Location $root
try {
    New-Item -ItemType Directory -Path $verificationRoot | Out-Null
    $env:CAMERAMAINTAINER_RENDERED_MARKUP_DIR = Join-Path $verificationRoot "rendered-markup"

    & dotnet restore CameraMaintainer.slnx --locked-mode
    Assert-CommandSucceeded "Package restore"

    $vulnerabilityOutput = & dotnet list CameraMaintainer.slnx package `
        --vulnerable --include-transitive --format json
    Assert-CommandSucceeded "Package vulnerability scan"
    $vulnerabilityReport = $vulnerabilityOutput | Out-String | ConvertFrom-Json
    $vulnerablePackages = @(
        foreach ($project in $vulnerabilityReport.projects) {
            $frameworksProperty = $project.PSObject.Properties["frameworks"]
            if ($null -eq $frameworksProperty) {
                continue
            }

            foreach ($framework in $frameworksProperty.Value) {
                foreach ($packageGroup in "topLevelPackages", "transitivePackages") {
                    $packagesProperty = $framework.PSObject.Properties[$packageGroup]
                    if ($null -eq $packagesProperty) {
                        continue
                    }

                    foreach ($package in $packagesProperty.Value) {
                        $vulnerabilitiesProperty = $package.PSObject.Properties["vulnerabilities"]
                        if ($null -ne $vulnerabilitiesProperty -and
                            @($vulnerabilitiesProperty.Value).Count -gt 0) {
                            $package
                        }
                    }
                }
            }
        }
    )
    if ($vulnerablePackages.Count -gt 0) {
        throw "The dependency scan found $($vulnerablePackages.Count) vulnerable package(s)."
    }

    & dotnet format CameraMaintainer.slnx --verify-no-changes --no-restore --verbosity minimal
    Assert-CommandSucceeded "Code style and naming check"

    & dotnet restore CameraMaintainer.slnx --locked-mode --artifacts-path $verificationRoot
    Assert-CommandSucceeded "Isolated package restore"

    & dotnet build CameraMaintainer.slnx -c Release --no-restore -warnaserror `
        --artifacts-path $verificationRoot
    Assert-CommandSucceeded "Release build"

    & dotnet test --solution CameraMaintainer.slnx -c Release --no-build --no-restore `
        --artifacts-path $verificationRoot
    Assert-CommandSucceeded ".NET tests"

    $javaScriptFiles = @(Get-ChildItem -LiteralPath src/CameraMaintainer.Web/wwwroot/js `
        -Filter "*.js" -File -Recurse | ForEach-Object FullName)
    foreach ($javaScriptFile in $javaScriptFiles) {
        & node --check $javaScriptFile
        Assert-CommandSucceeded "JavaScript syntax check: $javaScriptFile"
    }

    $webTests = @(Get-ChildItem -LiteralPath tests/web -Filter "*.test.cjs" -File |
        Sort-Object Name |
        ForEach-Object FullName)
    if ($webTests.Count -eq 0) {
        throw "No browser tests were found in tests/web."
    }

    & node --test $webTests
    Assert-CommandSucceeded "Browser tests"

    Write-Host "All release checks passed." -ForegroundColor Green
}
finally {
    Pop-Location
    $env:CAMERAMAINTAINER_RENDERED_MARKUP_DIR = $previousRenderedMarkupRoot
    $resolvedVerificationRoot = [IO.Path]::GetFullPath($verificationRoot)
    $resolvedTempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($resolvedVerificationRoot.StartsWith($resolvedTempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedVerificationRoot)) {
        Remove-Item -LiteralPath $resolvedVerificationRoot -Recurse -Force
    }
}
