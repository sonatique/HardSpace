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
	Force the Windows 11 short-menu pieces in or out. Left alone, they go in when this machine can
	build them: the Windows SDK for packing and signing, and a certificate to sign with. Passing
	-ShortMenu also creates a development certificate if there is none, which is a change to the
	certificate store and so is never done implicitly.

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

<#
.SYNOPSIS
	Whether the short-menu package can be built here, and if not, why not.
.DESCRIPTION
	Packing and signing an MSIX needs the Windows SDK and a certificate. Neither is needed to build
	the tool itself, so a clone with neither still builds -- it just produces an executable that
	cannot offer the short menu, and says so rather than leaving it to be discovered later.
#>
function Get-ShortMenuReadiness {
	$sdk = @('makeappx.exe', 'signtool.exe') | Where-Object {
		-not (Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\$_" -ErrorAction SilentlyContinue)
	}
	if ($sdk) {
		return [pscustomobject]@{ Ready = $false; Reason = "the Windows SDK is not installed ($($sdk -join ', ') not found)" }
	}

	if ($CertificateThumbprint) {
		if (-not (Test-Path "Cert:\CurrentUser\My\$CertificateThumbprint")) {
			return [pscustomobject]@{ Ready = $false; Reason = "certificate $CertificateThumbprint is not in Cert:\CurrentUser\My" }
		}

		return [pscustomobject]@{ Ready = $true; Reason = 'signing with the certificate given' }
	}

	$development = Get-ChildItem Cert:\CurrentUser\My |
		Where-Object { $_.Subject -eq 'CN=HardSpace Development' -and $_.NotAfter -gt (Get-Date) }
	if (-not $development) {
		return [pscustomobject]@{ Ready = $false; Reason = 'there is no signing certificate; -ShortMenu creates a development one' }
	}

	return [pscustomobject]@{ Ready = $true; Reason = 'signing with the development certificate' }
}

# Include the short menu when this machine can, unless told either way. What goes into the
# executable decides what it can install, so it is said out loud rather than inferred from its size.
$readiness = Get-ShortMenuReadiness
$forced = $PSBoundParameters.ContainsKey('ShortMenu')
if (-not $forced) { $ShortMenu = $readiness.Ready }

Write-Host ''
if ($ShortMenu) {
	Write-Host "Windows 11 short menu: embedding it -- $($readiness.Reason)." -ForegroundColor Cyan
}
elseif ($forced) {
	Write-Host 'Windows 11 short menu: left out, as asked.' -ForegroundColor Yellow
}
else {
	Write-Host "Windows 11 short menu: left out -- $($readiness.Reason)." -ForegroundColor Yellow
	Write-Host 'The entry will live under "Show more options" on a stock Windows 11.'
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
	elseif ($forced) { $packageArguments.CreateSelfSignedCertificate = $true }
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
	Write-Host 'One file, and the whole deployment.'
}

Write-Host ''
Write-Host 'On the far end, one command:  HardSpace.exe --install'
Write-Host 'It installs the most that machine and that prompt allow, and says what it chose.'
$elevatedGives = if ($ShortMenu) { 'every user, and the Windows 11 short menu.' } else { 'every user.' }
Write-Host "From an elevated prompt that means $elevatedGives"
