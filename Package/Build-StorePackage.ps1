<#
.SYNOPSIS
	Builds the MSIX to submit to the Microsoft Store.

.DESCRIPTION
	This is not the package the standalone build embeds. That one is *sparse*: it carries a manifest
	and points at binaries sitting in a folder on the machine. The Store will not accept that, so
	this package carries its own payload -- the executable and the shell extension are inside it --
	and Windows installs them where it keeps packaged apps.

	The identity is not ours to choose. Reserving the app's name in Partner Center assigns a package
	name, a publisher, and a publisher display name, and the package must carry exactly those or the
	submission is rejected. Pass them in; see docs\PUBLISHING-TO-THE-STORE.md for where to find them.

	The result is deliberately unsigned. The Store signs what it accepts, and a package signed by
	anyone else is refused. Use -SignForLocalTesting to sign a copy with the development certificate
	so it can be installed here first, which is worth doing before every submission.

.PARAMETER IdentityName
	Package/Identity/@Name, from Partner Center. Looks like 12345Sonatique.HardSpace.

.PARAMETER Publisher
	Package/Identity/@Publisher, from Partner Center. Looks like CN=A1B2C3D4-....

.PARAMETER PublisherDisplayName
	The name shown to people in the Store listing.

.PARAMETER Version
	Four parts, and the fourth must be 0: the Store reserves the revision for itself. Each submission
	needs a higher version than the last.

.PARAMETER OutputDirectory
	Where the .msix is written. Defaults to store\ beside the repository.

.PARAMETER SignForLocalTesting
	Also produce a signed copy, for installing on this machine before submitting. Never submit that
	one.

.EXAMPLE
	.\Build-StorePackage.ps1 -IdentityName 12345Sonatique.HardSpace -Publisher 'CN=A1B2C3D4-1234-5678-9ABC-DEF012345678' -PublisherDisplayName sonatique

.EXAMPLE
	.\Build-StorePackage.ps1 -SignForLocalTesting
	Builds with placeholder identity and signs it, to try the packaged build before there is a
	Partner Center account at all.
#>

[CmdletBinding()]
param(
	[string] $IdentityName = 'sonatique.HardSpace.Store',
	[string] $Publisher,
	[string] $PublisherDisplayName = 'sonatique',
	[string] $Version = '1.0.0.0',
	[string] $OutputDirectory,
	[switch] $SignForLocalTesting,
	[string] $CertificateThumbprint,
	[string] $Configuration = 'Release',
	[string] $RuntimeIdentifier = 'win-x64'
)

$ErrorActionPreference = 'Stop'

# $PSScriptRoot is empty while parameter defaults are bound under `powershell -File`.
$here = $PSScriptRoot
if (-not $here) { $here = Split-Path -Parent $MyInvocation.MyCommand.Definition }
$root = Split-Path -Parent $here

if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'store' }
$staging = Join-Path $here 'obj\store'
$developmentSubject = 'CN=HardSpace Development'

if ($Version -notmatch '^\d+\.\d+\.\d+\.0$') {
	throw "Version must have four parts ending in 0 -- the Store reserves the last for itself -- and '$Version' does not."
}

function Find-SdkTool([string] $name) {
	$candidates = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\$name" -ErrorAction SilentlyContinue |
		Sort-Object { [version]($_.Directory.Parent.Name) }
	if (-not $candidates) { throw "$name was not found. Install the Windows SDK." }
	return $candidates[-1].FullName
}

# The NativeAOT link step shells out to vswhere, which is not on PATH by default.
$vsInstaller = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
if ((Test-Path (Join-Path $vsInstaller 'vswhere.exe')) -and ($env:PATH -notlike "*$vsInstaller*")) {
	$env:PATH = "$vsInstaller;$env:PATH"
}

if (-not $Publisher) {
	if (-not $SignForLocalTesting) {
		throw 'Pass -Publisher with the value Partner Center assigned, or -SignForLocalTesting to ' +
			'build a package for trying here first.'
	}

	# A locally signed package must name its signer as publisher, exactly.
	$Publisher = $developmentSubject
}

Write-Host '==> Building the payload' -ForegroundColor Cyan
if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
New-Item -ItemType Directory -Force $staging | Out-Null

# Both binaries go inside the package. The executable is built without the embedded installer
# payload: a packaged app is installed by Windows, so carrying a copy of itself to install would be
# two megabytes of nothing, and the Store dislikes an app that offers to install itself.
$embedded = Join-Path $root 'HardSpace\Embedded'
New-Item -ItemType Directory -Force $embedded | Out-Null
Get-ChildItem -LiteralPath $embedded -File -ErrorAction SilentlyContinue | Remove-Item -Force

dotnet publish (Join-Path $root 'HardSpace\HardSpace.csproj') `
	-c $Configuration -r $RuntimeIdentifier -o $staging --nologo
if ($LASTEXITCODE -ne 0) { throw 'publish failed: HardSpace' }

dotnet publish (Join-Path $root 'HardSpace.ShellExtension\HardSpace.ShellExtension.csproj') `
	-c $Configuration -r $RuntimeIdentifier -o $staging --nologo
if ($LASTEXITCODE -ne 0) { throw 'publish failed: HardSpace.ShellExtension' }

Get-ChildItem -LiteralPath $staging -Filter *.pdb | Remove-Item -Force

Write-Host '==> Staging the manifest' -ForegroundColor Cyan
$manifest = Get-Content (Join-Path $here 'Store\AppxManifest.xml') -Raw
$manifest = $manifest.Replace('IDENTITY_NAME', $IdentityName).
	Replace('IDENTITY_PUBLISHER_DISPLAY_NAME', $PublisherDisplayName).
	Replace('IDENTITY_PUBLISHER', $Publisher).
	Replace('IDENTITY_VERSION', $Version)
Set-Content (Join-Path $staging 'AppxManifest.xml') -Value $manifest -Encoding UTF8
Copy-Item (Join-Path $here 'Images') $staging -Recurse

New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$OutputDirectory = (Resolve-Path $OutputDirectory).Path
$msix = Join-Path $OutputDirectory 'HardSpace.msix'

Write-Host '==> Packing' -ForegroundColor Cyan
if (Test-Path $msix) { Remove-Item -Force $msix }
$packOutput = & (Find-SdkTool 'makeappx.exe') pack /d $staging /p $msix /nv
if ($LASTEXITCODE -ne 0) {
	$packOutput | Where-Object { $_ -match 'error' } | ForEach-Object { Write-Host $_ -ForegroundColor Red }
	throw 'makeappx failed.'
}

Write-Host ''
Write-Host "Submit this: $msix" -ForegroundColor Green
Write-Host "  identity  : $IdentityName"
Write-Host "  publisher : $Publisher"
Write-Host "  version   : $Version"
Write-Host '  unsigned, which is what the Store wants: it signs the package itself.'

if ($SignForLocalTesting) {
	$certificate = Get-ChildItem Cert:\CurrentUser\My |
		Where-Object { $_.Subject -eq $developmentSubject -and $_.NotAfter -gt (Get-Date) } |
		Sort-Object NotAfter -Descending | Select-Object -First 1
	if ($CertificateThumbprint) { $certificate = Get-Item "Cert:\CurrentUser\My\$CertificateThumbprint" }
	if (-not $certificate) { throw "No certificate to sign with: expected $developmentSubject in Cert:\CurrentUser\My." }
	if ($certificate.Subject -ne $Publisher) {
		throw "A package can only be signed by the publisher it names. It says '$Publisher'; the " +
			"certificate is '$($certificate.Subject)'. Build without -Publisher to test locally."
	}

	$test = Join-Path $OutputDirectory 'HardSpace-signed-for-testing.msix'
	Copy-Item $msix $test -Force
	$signOutput = & (Find-SdkTool 'signtool.exe') sign /fd SHA256 /sha1 $certificate.Thumbprint $test
	if ($LASTEXITCODE -ne 0) {
		$signOutput | ForEach-Object { Write-Host $_ -ForegroundColor Red }
		throw 'signtool failed.'
	}

	Write-Host ''
	Write-Host "Try it here : $test" -ForegroundColor Green
	Write-Host "  Add-AppxPackage -Path `"$test`""
	Write-Host '  Get-AppxPackage *HardSpace* | Remove-AppxPackage    (to undo)'
	Write-Host '  Never submit this copy: the Store refuses a package signed by anyone else.'
}
