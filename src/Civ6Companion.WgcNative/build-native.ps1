param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration,

    [ValidateScript({
        if ([string]::IsNullOrWhiteSpace($_))
        {
            throw 'MSBuildPath cannot be empty.'
        }

        if (-not (Test-Path -LiteralPath $_ -PathType Leaf))
        {
            throw "MSBuildPath does not exist: $_"
        }

        if ([System.IO.Path]::GetFileName($_) -ine 'MSBuild.exe')
        {
            throw "MSBuildPath must point to MSBuild.exe: $_"
        }

        $true
    })]
    [string]$MSBuildPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Preserve the caller's PATH value while normalizing its key casing. MSBuild's C++
# targets add a `Path` environment entry and cannot launch CL.exe when an inherited
# `PATH` entry differs only by casing.
$callerPath = [Environment]::GetEnvironmentVariable('Path', 'Process')
[Environment]::SetEnvironmentVariable('PATH', $null, 'Process')
[Environment]::SetEnvironmentVariable('Path', $callerPath, 'Process')

function Resolve-MSBuildPath
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Candidate,
        [Parameter(Mandatory = $true)]
        [string]$Source
    )

    if ([string]::IsNullOrWhiteSpace($Candidate) -or -not (Test-Path -LiteralPath $Candidate -PathType Leaf))
    {
        throw "$Source did not provide an existing MSBuild.exe path."
    }

    $resolvedPath = (Resolve-Path -LiteralPath $Candidate).ProviderPath
    if ([System.IO.Path]::GetFileName($resolvedPath) -ine 'MSBuild.exe')
    {
        throw "$Source returned a path that is not MSBuild.exe: $resolvedPath"
    }

    return $resolvedPath
}

if (-not $MSBuildPath)
{
    $vswhereCandidates = @()
    if (${env:ProgramFiles(x86)})
    {
        $vswhereCandidates += Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    }

    if ($env:ProgramFiles)
    {
        $vswhereCandidates += Join-Path $env:ProgramFiles 'Microsoft Visual Studio\Installer\vswhere.exe'
    }

    $vswherePath = $vswhereCandidates |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Select-Object -First 1
    if (-not $vswherePath)
    {
        throw 'Could not find the Visual Studio Installer vswhere.exe. Install Visual Studio Build Tools with the MSBuild component, or pass -MSBuildPath to an existing MSBuild.exe.'
    }

    $discoveredPaths = @(& $vswherePath -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe')
    if ($LASTEXITCODE -ne 0 -or $discoveredPaths.Count -eq 0)
    {
        throw 'vswhere.exe could not find MSBuild. Install Visual Studio Build Tools with the MSBuild component, or pass -MSBuildPath to an existing MSBuild.exe.'
    }

    $MSBuildPath = $discoveredPaths[0].Trim()
    $MSBuildPath = Resolve-MSBuildPath -Candidate $MSBuildPath -Source 'vswhere.exe'
}
else
{
    $MSBuildPath = Resolve-MSBuildPath -Candidate $MSBuildPath -Source '-MSBuildPath'
}

$project = Join-Path $PSScriptRoot 'Civ6Companion.WgcNative.vcxproj'
& $MSBuildPath $project "/p:Configuration=$Configuration" '/p:Platform=x64' '/warnaserror'
exit $LASTEXITCODE
