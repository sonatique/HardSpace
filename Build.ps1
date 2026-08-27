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

.PARAMETER NoShortMenu
	Leave the Windows 11 short-menu pieces out. They go in by default: what the executable carries is
	what it can install, and a build meant for someone else's machine should carry everything any
	machine might want. Packing and signing them needs the Windows SDK, and a certificate -- a
	development one is created if there is none. Use this for a quick local build without either.

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
	[switch] $NoShortMenu,
	[string] $CertificateThumbprint
)

$ErrorActionPreference = 'Stop'

# Everything that any machine might need goes in, because the machine this is built for is not the
# machine it will run on: whoever gets this executable should not be short of a piece of it.
$ShortMenu = -not $NoShortMenu

if ($ShortMenu) {
	# The one thing that cannot be arranged from here. Saying so beats shipping a lesser executable
	# that looks identical and fails only when someone right-clicks a folder on Windows 11.
	$missing = @('makeappx.exe', 'signtool.exe') | Where-Object {
		-not (Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\$_" -ErrorAction SilentlyContinue)
	}
	if ($missing) {
		throw "The Windows SDK is needed to pack and sign the short-menu package, and $($missing -join ' and ') " +
			'could not be found. Install the Windows SDK, or pass -NoShortMenu for a build without it -- ' +
			'whose entry will sit under "Show more options" on a stock Windows 11.'
	}
}

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

# What the executable carries is what it can install, so its payload is emptied every time: a
# -NoShortMenu build must not quietly ship the package left behind by an earlier one.
$embedded = Join-Path $PSScriptRoot 'HardSpace\Embedded'
New-Item -ItemType Directory -Force -Path $embedded | Out-Null
Get-ChildItem -LiteralPath $embedded -File | Remove-Item -Force

if ($ShortMenu) {
	Write-Host '==> Building the shell extension' -ForegroundColor Cyan
	dotnet publish (Join-Path $PSScriptRoot 'HardSpace.ShellExtension\HardSpace.ShellExtension.csproj') `
		-c $Configuration -r $RuntimeIdentifier -o $embedded --nologo
	if ($LASTEXITCODE -ne 0) { throw 'publish failed: HardSpace.ShellExtension' }
	Get-ChildItem -LiteralPath $embedded -Filter *.pdb | Remove-Item -Force

	# Reuses whatever development certificate is already there; only makes one when there is none.
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
	Write-Host 'One file -- but built with -NoShortMenu, so it cannot offer the Windows 11 short menu.'
}

Write-Host ''
Write-Host 'On the far end, one command:  HardSpace.exe --install'
Write-Host 'It installs the most that machine and that prompt allow, and says what it chose.'
$elevatedGives = if ($ShortMenu) { 'every user, and the Windows 11 short menu.' } else { 'every user.' }
Write-Host "From an elevated prompt that means $elevatedGives"
