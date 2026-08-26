<#
.SYNOPSIS
	Publishes HardSpace and builds the sparse MSIX package that puts its verb in the Windows 11
	short context menu.

.DESCRIPTION
	Windows 11 only lifts a context-menu verb out of "Show more options" when it comes from an
	IExplorerCommand declared by an MSIX package. The package here is *sparse*: it carries the
	manifest only, while HardSpace.exe and HardSpace.ShellExtension.dll live in a normal folder
	that is named at install time (-ExternalLocation).

	The package must be signed, and the signing certificate must be trusted by the machine, before
	Windows will install it. Steps that need administrator rights are printed rather than run.

.PARAMETER InstallDirectory
	Where the published binaries go, and what the package points at. This path is baked into the
	installed package, so moving it afterwards means re-installing.

	It defaults to a machine-wide location, which requires an elevated prompt: Explorer loads the
	extension DLL from here into every user's session, so the folder must not be one a standard user
	can write to. The script creates it with an ACL that says so. A path inside the user profile is
	accepted without elevation, for development.

.PARAMETER CertificateThumbprint
	Thumbprint of an existing code-signing certificate in Cert:\CurrentUser\My.

.PARAMETER CreateSelfSignedCertificate
	Creates a development certificate in Cert:\CurrentUser\My and uses it. The machine will not
	trust it until it is imported into LocalMachine\TrustedPeople; the script prints how.

.PARAMETER SkipSigning
	Builds an unsigned .msix. Useful to validate the manifest; such a package cannot be installed.

.EXAMPLE
	.\Build-Package.ps1 -CreateSelfSignedCertificate
#>

[CmdletBinding()]
param(
	[string] $InstallDirectory = 'C:\ProgramData\HardSpace',
	[string] $CertificateThumbprint,
	[switch] $CreateSelfSignedCertificate,
	[switch] $SkipSigning,
	[switch] $Force,
	[string] $Configuration = 'Release',
	[string] $RuntimeIdentifier = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$packageRoot = $PSScriptRoot
$toolRoot = Split-Path -Parent $packageRoot
$outputDirectory = Join-Path $packageRoot 'out'
$stagingDirectory = Join-Path $packageRoot 'obj\staging'
$msixPath = Join-Path $outputDirectory 'HardSpace.msix'

function Test-Elevated {
	$identity = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
	return $identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

<#
.SYNOPSIS
	Creates the install directory with an ACL fit for something Explorer loads.
.DESCRIPTION
	C:\ProgramData grants BUILTIN\Users a Write ACE that its subfolders inherit, so a folder created
	there is writable by any standard user by default. The DLL in this folder is loaded into every
	user's Explorer, which would make that a straightforward way for one user to run code in
	another's session. Inheritance is therefore broken and the rights restated: full control for
	SYSTEM and administrators, read and execute for everyone else.
#>
function Initialize-InstallDirectory([string] $path, [bool] $harden) {
	New-Item -ItemType Directory -Force $path | Out-Null
	if (-not $harden) {
		# A folder inside the user's own profile is already closed to other standard users, and
		# hardening it here would take write access away from the very account doing the publish.
		return
	}

	# Well-known SIDs rather than names, which are localised.
	$system = [Security.Principal.SecurityIdentifier]'S-1-5-18'
	$administrators = [Security.Principal.SecurityIdentifier]'S-1-5-32-544'
	$users = [Security.Principal.SecurityIdentifier]'S-1-5-32-545'
	$inherit = [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
	$none = [Security.AccessControl.PropagationFlags]::None
	$allow = [Security.AccessControl.AccessControlType]::Allow

	# A fresh descriptor rather than one read back with Get-Acl, applied through the directory itself
	# rather than Set-Acl: both of those try to write every section of the descriptor, including the
	# audit section, and fail with "does not possess the 'SeSecurityPrivilege' privilege". This writes
	# only the sections actually modified here, which is the DACL.
	$security = New-Object Security.AccessControl.DirectorySecurity
	$security.SetAccessRuleProtection($true, $false)   # break inheritance, keep nothing inherited
	foreach ($grant in @(, @($system, 'FullControl')) + @(, @($administrators, 'FullControl')) + @(, @($users, 'ReadAndExecute'))) {
		$security.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule(
			$grant[0], $grant[1], $inherit, $none, $allow)))
	}

	try {
		(Get-Item $path).SetAccessControl($security)
	}
	catch {
		Write-Warning "Could not set the ACL on $path ($($_.Exception.Message)). Check that a standard user cannot write there."
	}
}

function Find-SdkTool([string] $name) {
	$candidates = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\$name" -ErrorAction SilentlyContinue |
		Sort-Object { [version]($_.Directory.Parent.Name) }
	if (-not $candidates) { throw "$name was not found. Install the Windows SDK." }
	return $candidates[-1].FullName
}

# The NativeAOT link step shells out to vswhere; it is not on PATH by default.
$vsInstaller = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
if ((Test-Path (Join-Path $vsInstaller 'vswhere.exe')) -and ($env:PATH -notlike "*$vsInstaller*")) {
	$env:PATH = "$vsInstaller;$env:PATH"
}

# Publishing into a machine-wide folder is an administrator's job, exactly as it is for any other
# installer. A path under the user's own profile is let through for development.
$perUser = [bool](@($env:LOCALAPPDATA, $env:USERPROFILE) |
	Where-Object { $_ -and $InstallDirectory.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) })
if (-not $perUser -and -not (Test-Elevated)) {
	throw "$InstallDirectory is a machine-wide location; re-run this from an elevated prompt, or " +
		'pass -InstallDirectory pointing somewhere under your user profile.'
}

# The install directory is the package payload, so it must contain this tool and nothing else: an
# earlier framework-dependent or self-contained publish left there would sit next to the native
# binaries as several hundred stale files.
$ourFiles = @('HardSpace.exe', 'HardSpace.ShellExtension.dll', 'HardSpace.pdb', 'HardSpace.ShellExtension.pdb')
$strays = @(Get-ChildItem $InstallDirectory -Force -ErrorAction SilentlyContinue | Where-Object { $ourFiles -notcontains $_.Name })
if ($strays) {
	if (-not $Force) {
		throw "$InstallDirectory already holds $($strays.Count) other item(s) (e.g. $($strays[0].Name)). " +
			'Re-run with -Force to empty it first, or pass a different -InstallDirectory.'
	}

	Write-Host "==> Emptying $InstallDirectory ($($strays.Count) stale item(s))" -ForegroundColor Yellow
	$strays | Remove-Item -Recurse -Force
}

Write-Host "==> Publishing to $InstallDirectory" -ForegroundColor Cyan
Initialize-InstallDirectory $InstallDirectory (-not $perUser)
foreach ($project in @('HardSpace\HardSpace.csproj', 'HardSpace.ShellExtension\HardSpace.ShellExtension.csproj')) {
	dotnet publish (Join-Path $toolRoot $project) -c $Configuration -r $RuntimeIdentifier -o $InstallDirectory --nologo
	if ($LASTEXITCODE -ne 0) { throw "publish failed: $project" }
}

# Symbols are not part of an install, and shipping them here only confuses the package payload.
Get-ChildItem $InstallDirectory -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force

$publisher = 'CN=HardSpace Development'
$certificate = $null

if (-not $SkipSigning) {
	if ($CreateSelfSignedCertificate) {
		Write-Host '==> Creating a development code-signing certificate' -ForegroundColor Cyan
		$certificate = New-SelfSignedCertificate `
			-Type Custom -Subject $publisher -FriendlyName 'HardSpace development signing' `
			-KeyUsage DigitalSignature -CertStoreLocation 'Cert:\CurrentUser\My' `
			-TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}Subject Type:End Entity')
	}
	elseif ($CertificateThumbprint) {
		$certificate = Get-Item "Cert:\CurrentUser\My\$CertificateThumbprint"
	}
	else {
		throw 'Pass -CertificateThumbprint, -CreateSelfSignedCertificate, or -SkipSigning.'
	}

	# Identity/Publisher and the certificate subject must match character for character, or Windows
	# rejects the package at install time.
	$publisher = $certificate.Subject
}

Write-Host '==> Staging the package' -ForegroundColor Cyan
if (Test-Path $stagingDirectory) { Remove-Item -Recurse -Force $stagingDirectory }
New-Item -ItemType Directory -Force $stagingDirectory | Out-Null
New-Item -ItemType Directory -Force $outputDirectory | Out-Null

(Get-Content (Join-Path $packageRoot 'AppxManifest.xml') -Raw).Replace('CN=HARDSPACE_PUBLISHER', $publisher) |
	Set-Content (Join-Path $stagingDirectory 'AppxManifest.xml') -Encoding UTF8
Copy-Item (Join-Path $packageRoot 'Images') $stagingDirectory -Recurse

Write-Host '==> Packing' -ForegroundColor Cyan
if (Test-Path $msixPath) { Remove-Item -Force $msixPath }
$packOutput = & (Find-SdkTool 'makeappx.exe') pack /d $stagingDirectory /p $msixPath /nv
if ($LASTEXITCODE -ne 0) {
	$packOutput | Where-Object { $_ -match 'error' } | ForEach-Object { Write-Host $_ -ForegroundColor Red }
	throw 'makeappx failed.'
}

if ($certificate) {
	Write-Host '==> Signing' -ForegroundColor Cyan
	$signOutput = & (Find-SdkTool 'signtool.exe') sign /fd SHA256 /sha1 $certificate.Thumbprint $msixPath
	if ($LASTEXITCODE -ne 0) {
		$signOutput | ForEach-Object { Write-Host $_ -ForegroundColor Red }
		throw 'signtool failed.'
	}
}

Write-Host ''
Write-Host "Package : $msixPath" -ForegroundColor Green
Write-Host "Payload : $InstallDirectory" -ForegroundColor Green
Write-Host ''

if ($certificate) {
	$certificatePath = Join-Path $outputDirectory 'HardSpace.cer'
	Export-Certificate -Cert $certificate -FilePath $certificatePath | Out-Null
	Write-Host 'To install (the first step needs an elevated prompt, and is only needed once):'
	Write-Host "  Import-Certificate -FilePath `"$certificatePath`" -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
	Write-Host "  Add-AppxPackage -Path `"$msixPath`" -ExternalLocation `"$InstallDirectory`""
}
else {
	Write-Host 'Unsigned package: Windows will refuse to install it. Re-run with a signing option.'
}

Write-Host ''
Write-Host 'To remove:'
Write-Host '  Get-AppxPackage *HardSpace* | Remove-AppxPackage'
