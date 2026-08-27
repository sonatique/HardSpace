<#
.SYNOPSIS
	Builds HardSpace and gathers what a colleague needs into one folder.

.DESCRIPTION
	Publishes the NativeAOT executable and puts it next to Install.ps1 in deploy\. Those two files
	are the whole deployment: the executable is self-contained, so there is no runtime to install
	and nothing else to copy. Zip the folder, or copy it to a share, and the far end runs
	Install.ps1.

.PARAMETER OutputDirectory
	Where the pair goes. Defaults to deploy\ beside this script.

.PARAMETER Configuration
	Build configuration. Release by default, which is what PublishAot is set up for.

.PARAMETER RuntimeIdentifier
	Target platform. NativeAOT cannot cross-compile, so this has to match the machine building it.

.EXAMPLE
	.\Build.ps1
#>

[CmdletBinding()]
param(
	[string] $OutputDirectory = (Join-Path $PSScriptRoot 'deploy'),
	[string] $Configuration = 'Release',
	[string] $RuntimeIdentifier = 'win-x64'
)

$ErrorActionPreference = 'Stop'

# The NativeAOT link step shells out to vswhere, which is not on PATH by default.
$vsInstaller = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
if ((Test-Path (Join-Path $vsInstaller 'vswhere.exe')) -and ($env:PATH -notlike "*$vsInstaller*")) {
	$env:PATH = "$vsInstaller;$env:PATH"
}

# A stale executable from an earlier build must not survive a failed one and look like the new one.
if (Test-Path $OutputDirectory) {
	Get-ChildItem -LiteralPath $OutputDirectory -File | Remove-Item -Force
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

Write-Host "==> Publishing" -ForegroundColor Cyan
dotnet publish (Join-Path $PSScriptRoot 'HardSpace\HardSpace.csproj') `
	-c $Configuration -r $RuntimeIdentifier -o $OutputDirectory --nologo
if ($LASTEXITCODE -ne 0) { throw 'publish failed.' }

# Symbols are for debugging here, not for shipping.
Get-ChildItem -LiteralPath $OutputDirectory -Filter *.pdb | Remove-Item -Force

Copy-Item (Join-Path $PSScriptRoot 'Install.ps1') -Destination $OutputDirectory -Force

Write-Host ''
Write-Host "Ready to hand over: $OutputDirectory" -ForegroundColor Green
Get-ChildItem -LiteralPath $OutputDirectory | ForEach-Object {
	"  {0,-16} {1,10:N0} bytes" -f $_.Name, $_.Length | Write-Host
}

Write-Host ''
Write-Host 'On the far end:  .\Install.ps1        (current user, no elevation)'
Write-Host '                 .\Install.ps1 -Machine   (every user, elevated prompt)'
