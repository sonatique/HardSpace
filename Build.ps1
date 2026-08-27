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

.PARAMETER ShortMenu
	Also build the pieces for Windows 11's short context menu: the shell-extension DLL and a signed
	sparse MSIX package. Without this the entry lands under "Show
	more options" instead, which needs no package, no certificate and no administrator.

.PARAMETER CertificateThumbprint
	Sign the package with this certificate instead of the development one. A certificate the target
	machines already trust removes the administrator step from their install.

.EXAMPLE
	.\Build.ps1
#>

[CmdletBinding()]
param(
	[string] $OutputDirectory = (Join-Path $PSScriptRoot 'deploy'),
	[string] $Configuration = 'Release',
	[string] $RuntimeIdentifier = 'win-x64',
	[switch] $ShortMenu,
	[string] $CertificateThumbprint
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

if ($ShortMenu) {
	Write-Host '==> Publishing the shell extension' -ForegroundColor Cyan
	dotnet publish (Join-Path $PSScriptRoot 'HardSpace.ShellExtension\HardSpace.ShellExtension.csproj') `
		-c $Configuration -r $RuntimeIdentifier -o $OutputDirectory --nologo
	if ($LASTEXITCODE -ne 0) { throw 'publish failed: HardSpace.ShellExtension' }

	$packageArguments = @{ OutputDirectory = $OutputDirectory }
	if ($CertificateThumbprint) { $packageArguments.CertificateThumbprint = $CertificateThumbprint }
	else { $packageArguments.CreateSelfSignedCertificate = $true }
	& (Join-Path $PSScriptRoot 'Package\Build-Package.ps1') @packageArguments
}

# Symbols are for debugging here, not for shipping.
Get-ChildItem -LiteralPath $OutputDirectory -Filter *.pdb | Remove-Item -Force

Copy-Item (Join-Path $PSScriptRoot 'Install.ps1') -Destination $OutputDirectory -Force

# One file to hand over. The folder stays as well, for installing straight from a build.
$archive = Join-Path (Split-Path -Parent $OutputDirectory) 'HardSpace.zip'
if (Test-Path $archive) { Remove-Item -LiteralPath $archive -Force }
Compress-Archive -Path (Join-Path $OutputDirectory '*') -DestinationPath $archive

Write-Host ''
$archiveSize = '{0:N0}' -f (Get-Item $archive).Length
Write-Host "Ready to hand over: $archive  ($archiveSize bytes)" -ForegroundColor Green
Write-Host "                    unzipped alongside it in $OutputDirectory, holding"
Get-ChildItem -LiteralPath $OutputDirectory | ForEach-Object {
	"  {0,-16} {1,10:N0} bytes" -f $_.Name, $_.Length | Write-Host
}

Write-Host ''
Write-Host 'On the far end, one command either way:  .\Install.ps1'
Write-Host 'It installs the most that machine and that prompt allow, and says what it chose.'
$elevatedGives = if ($ShortMenu) { 'every user, and the Windows 11 short menu.' } else { 'every user.' }
Write-Host "From an elevated prompt that means $elevatedGives"
