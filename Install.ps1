<#
.SYNOPSIS
	Installs HardSpace and its Explorer context-menu entry.

.DESCRIPTION
	Copies HardSpace.exe into a permanent folder and registers the "Folder size (hard-link aware)"
	verb for folders, folder backgrounds and drives. The executable is self-contained: there is no
	runtime to install and nothing else to copy.

	It works out what to do from what it is given. Run from an elevated prompt it installs for every
	user, which is the reliable choice: some machines ignore per-user verbs entirely, drawing the
	entry and dropping it a frame later so that it visibly flashes. Run without elevation it installs
	for the current user, which needs no rights and is right on most machines.

	If the short-menu files are present beside it -- and they only are if the deploy folder was built
	with Build.ps1 -ShortMenu -- an elevated run also installs the package that puts the entry in
	Windows 11's short menu, the one that opens before "Show more options". Nothing else can put it
	there. On a machine set to the classic context menu the package is simply never shown, and the
	classic verb, which this always registers, is what serves.

	So: run it elevated if you can, and it covers every case. Run it as yourself and it covers most.

.PARAMETER InstallDirectory
	Where the executable goes. Defaults to a folder under the user's profile, or to Program Files
	with -Machine. The path is written into the registry, so moving the executable afterwards breaks
	the entry; re-run this script instead.

.PARAMETER Machine
	Register for every user of the machine (HKLM) rather than the current one. Needs elevation. On by
	default when the prompt is already elevated; -Machine:$false forces per-user.

.PARAMETER ShortMenu
	Also install the MSIX package that puts the entry in Windows 11's *short* context menu -- the one
	that appears before "Show more options". Only an IExplorerCommand from a package can go there, so
	this needs HardSpace.ShellExtension.dll, HardSpace.msix and HardSpace.cer beside this script;
	build them with Build.ps1 -ShortMenu. Implies -Machine, and needs an elevated prompt to tell the
	machine to trust the package's certificate. On by default when those files are there and the
	prompt is elevated; -ShortMenu:$false leaves the package out. Pointless on a machine set to the
	classic context menu, where the short menu never renders at all.

.PARAMETER Quiet
	Skip the summary of what was decided.

.PARAMETER Source
	The HardSpace.exe to install. Defaults to the one next to this script, then to the repository's
	build output.

.PARAMETER RestartExplorer
	Restart Explorer without asking. Explorer reads the list of context-menu entries when it starts,
	so a new entry usually does not appear until it does. Without this the script asks; with
	-KeepExplorer it neither asks nor restarts.

.PARAMETER KeepExplorer
	Never restart Explorer and do not ask. For unattended installs.

.PARAMETER Uninstall
	Remove the entry and delete the installed executable.

.EXAMPLE
	.\Install.ps1
	Installs for the current user.

.EXAMPLE
	.\Install.ps1 -Machine
	Installs for every user, from an elevated prompt.

.EXAMPLE
	.\Install.ps1 -Uninstall -Machine
	Removes a machine-wide installation.
#>

[CmdletBinding()]
param(
	[string] $InstallDirectory,
	[switch] $Machine,
	[switch] $ShortMenu,
	[switch] $Quiet,
	[string] $Source,
	[switch] $RestartExplorer,
	[switch] $KeepExplorer,
	[switch] $Uninstall
)

$ErrorActionPreference = 'Stop'

# Work out the best install this machine and this prompt allow, unless told otherwise. The point is
# that whoever runs this should not have to know which of three answers applies to their Explorer.
$elevated = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent()).
	IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$packageFiles = @('HardSpace.ShellExtension.dll', 'HardSpace.msix', 'HardSpace.cer') |
	ForEach-Object { Join-Path $PSScriptRoot $_ }
$packageAvailable = -not ($packageFiles | Where-Object { -not (Test-Path $_) })

if (-not $PSBoundParameters.ContainsKey('ShortMenu')) { $ShortMenu = $elevated -and $packageAvailable -and -not $Uninstall }
if (-not $PSBoundParameters.ContainsKey('Machine')) { $Machine = $elevated }

# The package's payload is loaded into every user's Explorer, so it belongs in a machine-wide folder.
if ($ShortMenu) { $Machine = $true }

if (-not $Quiet -and -not $Uninstall) {
	Write-Host ''
	Write-Host ('Elevated prompt      : {0}' -f $(if ($elevated) { 'yes' } else { 'no -- installing for you only' }))
	Write-Host ('Scope                : {0}' -f $(if ($Machine) { 'every user (HKLM)' } else { 'current user (HKCU)' }))
	Write-Host ('Windows 11 short menu: {0}' -f $(
		if ($ShortMenu) { 'yes, installing the package' }
		elseif (-not $packageAvailable) { 'no -- built without -ShortMenu, entry goes under "Show more options"' }
		else { 'no -- needs an elevated prompt' }))
}

function Test-Elevated {
	$identity = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
	return $identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Resolve-Source {
	foreach ($candidate in @(
		$Source,
		(Join-Path $PSScriptRoot 'HardSpace.exe'),
		(Join-Path $PSScriptRoot 'HardSpace\bin\Release\net10.0-windows\HardSpace.exe'))) {
		if ($candidate -and (Test-Path $candidate)) { return (Resolve-Path $candidate).Path }
	}

	throw 'HardSpace.exe not found. Put it next to this script, or pass -Source.'
}

<#
.SYNOPSIS
	Installs the sparse MSIX package that carries the Windows 11 short-menu command.
.DESCRIPTION
	The package declares a COM server and a context-menu extension; its payload -- the executable and
	the shell-extension DLL -- stays in the install folder, which is what -ExternalLocation names.
	Windows will not install a package it does not trust, and a development certificate is trusted
	nowhere until a machine is told to, which is the one part of this needing an administrator.
#>
function Install-ShortMenu([string] $directory) {
	$dll = Join-Path $PSScriptRoot 'HardSpace.ShellExtension.dll'
	$msix = Join-Path $PSScriptRoot 'HardSpace.msix'
	$certificate = Join-Path $PSScriptRoot 'HardSpace.cer'
	foreach ($required in $dll, $msix, $certificate) {
		if (-not (Test-Path $required)) {
			throw "$(Split-Path -Leaf $required) is missing. Build the short-menu pieces with " +
				'Build.ps1 -ShortMenu, and copy the whole deploy folder over.'
		}
	}

	Copy-Item -LiteralPath $dll -Destination $directory -Force

	$thumbprint = (New-Object Security.Cryptography.X509Certificates.X509Certificate2 $certificate).Thumbprint
	if (-not (Test-Path "Cert:\LocalMachine\TrustedPeople\$thumbprint")) {
		Write-Host '==> Trusting the package certificate (machine-wide, once)' -ForegroundColor Cyan
		Import-Certificate -FilePath $certificate -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
	}

	Write-Host '==> Registering the package' -ForegroundColor Cyan
	Get-AppxPackage *HardSpace* | Remove-AppxPackage -ErrorAction SilentlyContinue
	Add-AppxPackage -Path $msix -ExternalLocation $directory

	Write-Host 'The entry is in the short menu too, on a machine that shows one.'
}

if (-not $InstallDirectory) {
	$InstallDirectory = if ($Machine) { Join-Path $env:ProgramFiles 'HardSpace' }
	                    else { Join-Path $env:LOCALAPPDATA 'Programs\HardSpace' }
}

$installedExe = Join-Path $InstallDirectory 'HardSpace.exe'
$scope = if ($Machine) { 'every user of this machine' } else { 'the current user' }

# Writing outside the user's own profile, and writing to HKLM, both need an administrator.
$perUserPath = @($env:LOCALAPPDATA, $env:USERPROFILE) |
	Where-Object { $_ -and $InstallDirectory.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) }
if (($Machine -or -not $perUserPath) -and -not (Test-Elevated)) {
	throw "Installing to $InstallDirectory for $scope needs an elevated prompt. Re-run this from one, " +
		'or drop -Machine to install under your own profile.'
}

if ($Uninstall) {
	if (Get-AppxPackage *HardSpace*) {
		Write-Host '==> Removing the package' -ForegroundColor Cyan
		Get-AppxPackage *HardSpace* | Remove-AppxPackage
	}

	Remove-Item -LiteralPath (Join-Path $InstallDirectory 'HardSpace.ShellExtension.dll') -Force -ErrorAction SilentlyContinue

	if (Test-Path $installedExe) {
		Write-Host "==> Removing the context-menu entry" -ForegroundColor Cyan
		$arguments = @('--unregister'); if ($Machine) { $arguments += '--machine' }
		& $installedExe @arguments | Write-Host
		Remove-Item -LiteralPath $installedExe -Force
	}
	else {
		Write-Warning "$installedExe not found; removing the registry entries only."
		$key = if ($Machine) { 'HKLM:' } else { 'HKCU:' }
		foreach ($path in 'Directory\shell\HardSpace', 'Directory\Background\shell\HardSpace', 'Drive\shell\HardSpace') {
			Remove-Item -LiteralPath "$key\Software\Classes\$path" -Recurse -Force -ErrorAction SilentlyContinue
		}
	}

	if ((Test-Path $InstallDirectory) -and -not (Get-ChildItem $InstallDirectory -Force)) {
		Remove-Item -LiteralPath $InstallDirectory -Force
	}

	Write-Host "Removed for $scope." -ForegroundColor Green
}
else {
	$source = Resolve-Source
	Write-Host "==> Installing $source" -ForegroundColor Cyan
	Write-Host "    to $InstallDirectory, for $scope"

	New-Item -ItemType Directory -Force -Path $InstallDirectory | Out-Null
	Copy-Item -LiteralPath $source -Destination $installedExe -Force

	# A copy that arrived by mail or chat carries a mark of the web, and SmartScreen would stop it on
	# first run. The file is ours; clear it.
	Unblock-File -LiteralPath $installedExe -ErrorAction SilentlyContinue

	Write-Host '==> Registering the context-menu entry' -ForegroundColor Cyan
	$arguments = @('--register'); if ($Machine) { $arguments += '--machine' }
	& $installedExe @arguments | Write-Host

	$hive = if ($Machine) { 'HKLM:' } else { 'HKCU:' }
	$registered = Test-Path "$hive\Software\Classes\Directory\shell\HardSpace\command"
	if (-not $registered) { throw 'Registration did not take; nothing was written to the registry.' }

	if ($ShortMenu) { Install-ShortMenu $InstallDirectory }

	Write-Host ''
	Write-Host "Installed: $installedExe" -ForegroundColor Green
	Write-Host "Right-click any folder and choose `"Folder size (hard-link aware)`"."
	if (-not $Machine) {
		Write-Host ''
		Write-Host 'If the entry does not appear, or appears and vanishes again, this machine ignores'
		Write-Host 'per-user verbs: re-run this from an elevated prompt and it will install for every'
		Write-Host 'user instead, which such machines do honour.'
	}
	elseif (-not $ShortMenu -and $packageAvailable) {
		Write-Host 'It is under "Show more options" on a stock Windows 11 menu; -ShortMenu lifts it out.'
	}
}

<#
.SYNOPSIS
	Restarts Explorer, asking first, because it closes the user's open windows.
.DESCRIPTION
	Explorer reads context-menu entries when it starts, so a newly registered one usually does not
	appear until it restarts. That is not a reason to close somebody's File Explorer windows out
	from under them without asking, so this explains the trade and takes an answer -- and when there
	is nobody to ask, it says what to do rather than acting.
#>
function Complete-Installation {
	if ($KeepExplorer) { return }

	$question = 'Explorer only reads context-menu entries when it starts, so the new entry may not' +
		[Environment]::NewLine + 'appear until it is restarted. Restarting closes your open File Explorer windows.'

	if (-not $RestartExplorer) {
		Write-Host ''
		Write-Host $question
		Write-Host ''

		$answer = $null
		try {
			if ([Environment]::UserInteractive) {
				$answer = Read-Host 'Restart Explorer now? [y/N]'
			}
		}
		catch {
			# No console to ask on: an unattended run, so leave Explorer alone.
			$answer = $null
		}

		if ($answer -notmatch '^(y|yes|o|oui)$') {
			Write-Host ''
			Write-Host 'Left running. The entry will appear after any of these:' -ForegroundColor Yellow
			Write-Host '  - Task Manager (Ctrl+Shift+Esc), right-click "Windows Explorer", Restart'
			Write-Host '  - Stop-Process -Name explorer -Force        (Explorer restarts itself)'
			Write-Host '  - signing out and back in, or a reboot'
			return
		}
	}

	Write-Host ''
	Write-Host '==> Restarting Explorer' -ForegroundColor Cyan
	Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
}

Complete-Installation
