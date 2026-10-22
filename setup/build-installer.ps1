# Builds the ptc-xae-mcp installer and its .sha256 in one go.
#
#   powershell -ExecutionPolicy Bypass -File setup\build-installer.ps1
#
# Steps: read <Version> from Directory.Build.props -> dotnet publish (self-contained win-x64)
# -> iscc setup.iss /DAppVersion=<Version> -> write setup\dist\ptc-xae-mcp-setup-<Version>.exe.sha256.
#
# Publish the .sha256 next to the installer (the installer runs elevated, so a deployment
# should verify it first). Format: "<lowercase sha256>  <exe name>", ASCII.
#
# Optional: -ShareDir <folder> names a folder that holds earlier ptc-xae-mcp-setup-*.exe builds
# (e.g. a team share). The script then refuses a version that is not HIGHER than the newest one
# there, because setup.ps1 -Update does not see an equal version as an update. -AllowSameVersion
# skips that check for local test builds. Without -ShareDir nothing is compared.
[CmdletBinding()]
param(
    [string]$ShareDir = '',
    [switch]$AllowSameVersion,
    [string]$Iscc = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

[xml]$props = Get-Content (Join-Path $repo 'Directory.Build.props')
$version = ($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { throw 'No <Version> in Directory.Build.props.' }
Write-Host "Version: $version"

if (-not $AllowSameVersion -and $ShareDir -and (Test-Path $ShareDir)) {
    $onShare = Get-ChildItem $ShareDir -Filter 'ptc-xae-mcp-setup-*.exe' |
        ForEach-Object { if ($_.BaseName -match '^ptc-xae-mcp-setup-(.+)$') { try { [version]$Matches[1] } catch { $null } } } |
        Where-Object { $_ } | Sort-Object -Descending | Select-Object -First 1
    if ($onShare -and [version]$version -le $onShare) {
        throw "Version $version is not higher than $onShare on the share ($ShareDir). Raise <Version> in Directory.Build.props, or pass -AllowSameVersion for a local build."
    }
}

$publish = Join-Path $PSScriptRoot 'publish'
$dist = Join-Path $PSScriptRoot 'dist'
Remove-Item -Recurse -Force $publish -ErrorAction SilentlyContinue

dotnet publish (Join-Path $repo 'src\PtcXaeMcp.WinForms\PtcXaeMcp.WinForms.csproj') `
    -c Release -r win-x64 --self-contained true -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }

# Issue #3: the Microsoft EnvDTE interop assemblies are licensed for use with Visual Studio only
# and are loaded from the local TcXaeShell at runtime. A publish without TcXaeShell on the build
# machine falls back to the envdte NuGet packages and would bundle them - refuse that.
$msInterop = Get-ChildItem $publish -Recurse -File |
    Where-Object { $_.Name -like 'envdte*' -or $_.Name -like 'stdole*' -or $_.Name -like 'Microsoft.VisualStudio.*' }
if ($msInterop) {
    throw "The publish folder contains Microsoft interop assemblies that must not be redistributed: $(($msInterop.Name | Sort-Object) -join ', '). Build on a machine with TcXaeShell installed."
}

& $Iscc (Join-Path $PSScriptRoot 'setup.iss') "/DAppVersion=$version"
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)." }

$exeName = "ptc-xae-mcp-setup-$version.exe"
$exe = Join-Path $dist $exeName
if (-not (Test-Path $exe)) { throw "Installer not found: $exe" }
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()
"$hash  $exeName" | Set-Content "$exe.sha256" -Encoding Ascii

Write-Host "Installer: $exe"
Write-Host "SHA256:    $exe.sha256 ($hash)"
if ($ShareDir) { Write-Host "For the share: copy BOTH files to $ShareDir" }
