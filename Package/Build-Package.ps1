<#
.SYNOPSIS
	Packs and signs the sparse MSIX that puts HardSpace in the Windows 11 short context menu.

.DESCRIPTION
	Windows 11's short menu -- the one that appears first, before "Show more options" -- accepts only
	an IExplorerCommand served by a COM server that an MSIX package declares. A classic registry verb
	can never appear there, however it is registered.

	The package is *sparse*: it carries this manifest and its logos, nothing else. The binaries stay
	in an ordinary folder, named when the package is installed, which is Install.ps1's job.

	This script only produces HardSpace.msix. The certificate that signed it travels inside the
	package, so there is nothing to ship alongside; Install.ps1 reads it back out. Nothing is
	installed here and no machine state is touched beyond the certificate store.

.PARAMETER OutputDirectory
	Where HardSpace.msix is written.

.PARAMETER CertificateThumbprint
	An existing code-signing certificate in Cert:\CurrentUser\My. With one the target machines
	already trust -- a real certificate from a CA -- installing needs no certificate step at all.

.PARAMETER CreateSelfSignedCertificate
	Make a development certificate, if none was passed and none from an earlier run is found. Every
	machine installing the package then has to be told to trust it, which needs an administrator.

.EXAMPLE
	.\Build-Package.ps1 -OutputDirectory ..\deploy -CreateSelfSignedCertificate
#>

[CmdletBinding()]
param(
	[Parameter(Mandatory)] [string] $OutputDirectory,
	[string] $CertificateThumbprint,
	[switch] $CreateSelfSignedCertificate
)

$ErrorActionPreference = 'Stop'
$packageRoot = $PSScriptRoot
$stagingDirectory = Join-Path $packageRoot 'obj\staging'
$developmentSubject = 'CN=HardSpace Development'

function Find-SdkTool([string] $name) {
	$candidates = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\$name" -ErrorAction SilentlyContinue |
		Sort-Object { [version]($_.Directory.Parent.Name) }
	if (-not $candidates) { throw "$name was not found. Install the Windows SDK." }
	return $candidates[-1].FullName
}

function Resolve-Certificate {
	if ($CertificateThumbprint) {
		return Get-Item "Cert:\CurrentUser\My\$CertificateThumbprint"
	}

	# Reuse the development certificate from an earlier run. Minting a second one would change the
	# package's publisher, and Windows treats a different publisher as a different application.
	$existing = @(Get-ChildItem Cert:\CurrentUser\My |
		Where-Object { $_.Subject -eq $developmentSubject -and $_.NotAfter -gt (Get-Date) } |
		Sort-Object NotAfter -Descending)
	if ($existing) {
		Write-Host "==> Reusing $developmentSubject ($($existing[0].Thumbprint))" -ForegroundColor Cyan
		return $existing[0]
	}

	if (-not $CreateSelfSignedCertificate) {
		throw 'No signing certificate. Pass -CertificateThumbprint, or -CreateSelfSignedCertificate.'
	}

	Write-Host '==> Creating a development code-signing certificate' -ForegroundColor Cyan
	return New-SelfSignedCertificate `
		-Type Custom -Subject $developmentSubject -FriendlyName 'HardSpace development signing' `
		-KeyUsage DigitalSignature -CertStoreLocation 'Cert:\CurrentUser\My' `
		-TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}Subject Type:End Entity')
}

$certificate = Resolve-Certificate

New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$OutputDirectory = (Resolve-Path $OutputDirectory).Path
$msixPath = Join-Path $OutputDirectory 'HardSpace.msix'

Write-Host '==> Staging the manifest' -ForegroundColor Cyan
if (Test-Path $stagingDirectory) { Remove-Item -Recurse -Force $stagingDirectory }
New-Item -ItemType Directory -Force $stagingDirectory | Out-Null

# Identity/Publisher must match the certificate's subject character for character, or Windows
# rejects the package when it is installed.
(Get-Content (Join-Path $packageRoot 'AppxManifest.xml') -Raw).Replace('CN=HARDSPACE_PUBLISHER', $certificate.Subject) |
	Set-Content (Join-Path $stagingDirectory 'AppxManifest.xml') -Encoding UTF8
Copy-Item (Join-Path $packageRoot 'Images') $stagingDirectory -Recurse

Write-Host '==> Packing' -ForegroundColor Cyan
if (Test-Path $msixPath) { Remove-Item -Force $msixPath }
$packOutput = & (Find-SdkTool 'makeappx.exe') pack /d $stagingDirectory /p $msixPath /nv
if ($LASTEXITCODE -ne 0) {
	$packOutput | Where-Object { $_ -match 'error' } | ForEach-Object { Write-Host $_ -ForegroundColor Red }
	throw 'makeappx failed.'
}

Write-Host '==> Signing' -ForegroundColor Cyan
$signOutput = & (Find-SdkTool 'signtool.exe') sign /fd SHA256 /sha1 $certificate.Thumbprint $msixPath
if ($LASTEXITCODE -ne 0) {
	$signOutput | ForEach-Object { Write-Host $_ -ForegroundColor Red }
	throw 'signtool failed.'
}

Write-Host ''
Write-Host "Package   : $msixPath" -ForegroundColor Green
Write-Host "Publisher : $($certificate.Subject)"
