<#
.SYNOPSIS
	Builds HardSpace into the single file that is its whole deployment.

.DESCRIPTION
	Publishes the NativeAOT executable into deploy\. That one file is everything: it scans folders,
	and it installs itself -- `HardSpace.exe --install` copies it somewhere permanent and registers
	it with Explorer.

	With -ShortMenu the executable also carries the shell-extension DLL and a signed sparse MSIX
	package, embedded as resources, which --install writes out and registers. That is what puts the
	entry in Windows 11's short menu; without them it lives under "Show more options". They are built
	first, because an executable cannot embed what does not exist yet.

.PARAMETER OutputDirectory
	Where the executable is written. Defaults to deploy\ beside this script.

.PARAMETER Configuration
	Build configuration. Release by default, which is what PublishAot is set up for.

.PARAMETER RuntimeIdentifier
	Target platform. NativeAOT cannot cross-compile, so this has to match the machine building it.

.PARAMETER ShortMenu
	Also build and embed the Windows 11 short-menu pieces.

.PARAMETER CertificateThumbprint
	Sign the package with this certificate instead of the development one. A certificate the target
	machines already trust removes the administrator step from their install.

.EXAMPLE
	.\Build.ps1 -ShortMenu
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

# A stale executable from an earlier build must not survive a failed one and pass for the new one.
if (Test-Path $OutputDirectory) {
	Get-ChildItem -LiteralPath $OutputDirectory -File | Remove-Item -Force
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

# What the executable carries is what it can install, so its payload is emptied every time: a build
# without -ShortMenu must not quietly ship the package left behind by the last build with it.
$embedded = Join-Path $PSScriptRoot 'HardSpace\Embedded'
New-Item -ItemType Directory -Force -Path $embedded | Out-Null
Get-ChildItem -LiteralPath $embedded -File | Remove-Item -Force

if ($ShortMenu) {
	Write-Host '==> Building the shell extension' -ForegroundColor Cyan
	dotnet publish (Join-Path $PSScriptRoot 'HardSpace.ShellExtension\HardSpace.ShellExtension.csproj') `
		-c $Configuration -r $RuntimeIdentifier -o $embedded --nologo
	if ($LASTEXITCODE -ne 0) { throw 'publish failed: HardSpace.ShellExtension' }
	Get-ChildItem -LiteralPath $embedded -Filter *.pdb | Remove-Item -Force

	$packageArguments = @{ OutputDirectory = $embedded }
	if ($CertificateThumbprint) { $packageArguments.CertificateThumbprint = $CertificateThumbprint }
	else { $packageArguments.CreateSelfSignedCertificate = $true }
	& (Join-Path $PSScriptRoot 'Package\Build-Package.ps1') @packageArguments
}

Write-Host '==> Publishing' -ForegroundColor Cyan
dotnet publish (Join-Path $PSScriptRoot 'HardSpace\HardSpace.csproj') `
	-c $Configuration -r $RuntimeIdentifier -o $OutputDirectory --nologo
if ($LASTEXITCODE -ne 0) { throw 'publish failed.' }

# Symbols are for debugging here, not for shipping.
Get-ChildItem -LiteralPath $OutputDirectory -Filter *.pdb | Remove-Item -Force

$executable = Join-Path $OutputDirectory 'HardSpace.exe'
$size = '{0:N0}' -f (Get-Item $executable).Length

Write-Host ''
Write-Host "Ready to hand over: $executable  ($size bytes)" -ForegroundColor Green
if ($ShortMenu) {
	Write-Host 'It carries the shell extension and the signed package, so that is the whole deployment.'
}
else {
	Write-Host 'One file, and the whole deployment. Add -ShortMenu for the Windows 11 short menu.'
}

Write-Host ''
Write-Host 'On the far end, one command:  HardSpace.exe --install'
Write-Host 'It installs the most that machine and that prompt allow, and says what it chose.'
$elevatedGives = if ($ShortMenu) { 'every user, and the Windows 11 short menu.' } else { 'every user.' }
Write-Host "From an elevated prompt that means $elevatedGives"
