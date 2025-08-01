<#
.SYNOPSIS
Builds NuGet client solutions and creates output artifacts.

.PARAMETER Configuration
Build configuration (debug by default)

.PARAMETER ReleaseLabel
Release label to use for package and assemblies versioning (zlocal by default)

.PARAMETER BuildNumber
Build number to use for package and assemblies versioning (auto-generated if not provided)

.PARAMETER Clean
Cleans output artifacts before building

.PARAMETER CI
Indicates the build script is invoked from CI

.PARAMETER PackageEndToEnd
Indicates whether to create the end to end package.

.PARAMETER SkipDelaySigning
Indicates whether to skip strong name signing.  By default assemblies will be delay signed and require strong name validation exclusions.

.PARAMETER RunUnitTests
Indicates whether to run unit tests after building

.PARAMETER Pack
Creates NuGet's packages after building

.EXAMPLE
.\build.ps1
To run full clean build, e.g after switching branches

.EXAMPLE
.\build.ps1 -f
Fast incremental build

.EXAMPLE
.\build.ps1 -v -ea Stop
To troubleshoot build issues
#>
[CmdletBinding()]
param (
    [ValidateSet('debug', 'release')]
    [Alias('c')]
    [string]$Configuration,
    [ValidatePattern('^(beta|final|preview|rc|release|rtm|xprivate|zlocal)([0-9]*)$')]
    [Alias('l')]
    [string]$ReleaseLabel = 'zlocal',
    [Alias('n')]
    [int]$BuildNumber,
    [Alias('su')]
    [switch]$RunUnitTests,
    [Alias('f')]
    [switch]$Clean,
    [switch]$CI,
    [switch]$PackageEndToEnd,
    [switch]$SkipDelaySigning,
    [switch]$Binlog,
    [switch]$IncludeApex,
    [switch]$UpdateXlf,
    [switch]$Pack
)

. "$PSScriptRoot\build\common.ps1"

If (-Not $SkipDelaySigning)
{
    & "$PSScriptRoot\scripts\utils\DisableStrongNameVerification.ps1" -skipNoOpMessage
}

if (-not $Configuration) {
    $Configuration = switch ($CI.IsPresent) {
        $True   { 'Release' } # CI build is Release by default
        $False  { 'Debug' } # Local builds are Debug by default
    }
}

Write-Host ("`r`n" * 3)
Trace-Log ('=' * 60)

$startTime = [DateTime]::UtcNow
Trace-Log "Build started at $startTime"

Test-BuildEnvironment -CI:$CI

$BuildErrors = @()

Invoke-BuildStep 'Cleaning artifacts' {
    Clear-Artifacts
    Clear-Nupkgs
} `
-skip:(-not $Clean) `
-ev +BuildErrors

if ($RunUnitTests) {
    $VSTarget = "RunVS";
    $VSMessage = "Running Build, Pack, Core unit tests, and Unit tests";
}
else {
    if ($Pack) {
        $VSTarget = "BuildVS;Pack";
    } else {
        $VSTarget = "BuildVS";
    }
    $VSMessage = "Running Build"
}

$MSBuildExe = Get-MSBuildExe

Invoke-BuildStep 'Running Restore' {
    # Restore
    $restoreArgs = "build\build.proj", "/t:RestoreVS", "/p:Configuration=$Configuration", "/p:ReleaseLabel=$ReleaseLabel", "/p:IncludeApex=$IncludeApex", "/v:m", "/m"

    if ($BuildNumber)
    {
        $restoreArgs += "/p:BuildNumber=$BuildNumber"
    }

    if ($Binlog)
    {
        $restoreArgs += "-bl:msbuild.restore.binlog"
    }

    Trace-Log ". `"$MSBuildExe`" $restoreArgs"
    & $MSBuildExe @restoreArgs

    if (-not $?)
    {
        Write-Error "Failed - Running Restore"
        exit 1
    }
} `
-ev +BuildErrors

Invoke-BuildStep 'Updating Xlf' {
    $buildArgs = 'build\build.proj', "/t:UpdateXlf", '/v:m', '/m'

    Trace-Log ". `"$MSBuildExe`" $buildArgs"
    & $MSBuildExe @buildArgs

    if (-not $?)
    {
        Write-Error "Failed - Updating Xlf"
        exit 1
    }
} `
-skip:(-not $UpdateXlf)`
-ev +BuildErrors

Invoke-BuildStep $VSMessage {

    $buildArgs = 'build\build.proj', "/t:$VSTarget", "/p:Configuration=$Configuration", "/p:ReleaseLabel=$ReleaseLabel", "/p:IncludeApex=$IncludeApex", '/v:m', '/m'

    if ($BuildNumber)
    {
        $buildArgs += "/p:BuildNumber=$BuildNumber"
    }

    If ($SkipDelaySigning)
    {
        $buildArgs += "/p:SkipSigning=true"
    }

    if ($Binlog)
    {
        $buildArgs += "-bl:msbuild.build.binlog"
    }

    # Build and (If not $SkipUnitTest) Pack, Core unit tests, and Unit tests for VS
    Trace-Log ". `"$MSBuildExe`" $buildArgs"
    & $MSBuildExe @buildArgs

    if (-not $?)
    {
        Write-Error "Failed - $VSMessage"
        exit 1
    }
} `
-ev +BuildErrors

Invoke-BuildStep 'Creating the EndToEnd test package' {
        $msbuildArgs = "test\TestUtilities\CreateEndToEndTestPackage\CreateEndToEndTestPackage.proj", "/p:Configuration=$Configuration", "/restore:false", "/property:BuildProjectReferences=false"

        if ($Binlog)
        {
            $restoreArgs += "-bl:msbuild.createendtoendtestpackage.binlog"
        }

        Trace-Log ". `"$MSBuildExe`" $msbuildArgs"
        & $MSBuildExe @msbuildArgs
    } `
    -args $Configuration `
    -skip:(-not $PackageEndToEnd) `
    -ev +BuildErrors


Invoke-BuildStep 'Running Restore RTM' {

    # Restore for VS
    $restoreArgs = "build\build.proj", "/t:RestoreVS", "/p:Configuration=$Configuration", "/p:BuildRTM=true", "/p:ReleaseLabel=$ReleaseLabel", "/p:ExcludeTestProjects=true", "/v:m", "/m"

    if ($BuildNumber)
    {
        $restoreArgs += "/p:BuildNumber=$BuildNumber"
    }

    if ($Binlog)
    {
        $restoreArgs += "-bl:msbuild.restore.binlog"
    }

    Trace-Log ". `"$MSBuildExe`" $restoreArgs"
    & $MSBuildExe @restoreArgs

    if (-not $?)
    {
        Write-Error "Restore failed!"
        exit 1
    }
} `
-skip:(-not $CI)`
-ev +BuildErrors


Invoke-BuildStep 'Packing RTM' {

    # Build and (If not $SkipUnitTest) Pack, Core unit tests, and Unit tests for VS
    $packArgs = "build\build.proj", "/t:BuildVS`;Pack", "/p:Configuration=$Configuration", "/p:BuildRTM=true", "/p:ReleaseLabel=$ReleaseLabel", "/p:ExcludeTestProjects=true", "/v:m", "/m"

    if ($BuildNumber)
    {
        $packArgs += "/p:BuildNumber=$BuildNumber"
    }

    if ($Binlog)
    {
        $packArgs += "-bl:msbuild.pack.binlog"
    }

    Trace-Log ". `"$MSBuildExe`" $packArgs"
    & $MSBuildExe @packArgs

    if (-not $?)
    {
        Write-Error "Packing RTM build failed!"
        exit 1
    }
} `
-skip:(-not $CI)`
-ev +BuildErrors

## Calculating Build time
$endTime = [DateTime]::UtcNow
Trace-Log "Build #$BuildNumber ended at $endTime"
Trace-Log "Time elapsed $(Format-ElapsedTime ($endTime - $startTime))"

Trace-Log ('=' * 60)

if ($BuildErrors) {
    $ErrorLines = $BuildErrors | %{ ">>> $($_.Exception.Message)" }
    Write-Error "Build's completed with $($BuildErrors.Count) error(s):`r`n$($ErrorLines -join "`r`n")" -ErrorAction Stop
}

Write-Host ("`r`n" * 3)
