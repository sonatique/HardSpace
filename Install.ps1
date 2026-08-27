<#
.SYNOPSIS
	Installs HardSpace and its Explorer context-menu entry.

.DESCRIPTION
	Copies HardSpace.exe into a permanent folder and registers the "Folder size (hard-link aware)"
	verb for folders, folder backgrounds and drives. The executable is self-contained: there is no
	runtime to install and nothing else to copy.

	Per-user by default, which needs no administrator rights. Some machines ignore per-user shell
	verbs entirely -- Explorer draws the entry and then drops it a frame later, so it visibly
	flashes -- and there -Machine is required, which writes to HKLM and needs an elevated prompt.

.PARAMETER InstallDirectory
	Where the executable goes. Defaults to a folder under the user's profile, or to Program Files
	with -Machine. The path is written into the registry, so moving the executable afterwards breaks
	the entry; re-run this script instead.

.PARAMETER Machine
	Register for every user of the machine (HKLM) rather than the current one. Needs elevation.

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
	[string] $Source,
	[switch] $RestartExplorer,
	[switch] $KeepExplorer,
	[switch] $Uninstall
)

$ErrorActionPreference = 'Stop'

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

	Write-Host ''
	Write-Host "Installed: $installedExe" -ForegroundColor Green
	Write-Host "Right-click any folder and choose `"Folder size (hard-link aware)`"."
	if (-not $Machine) {
		Write-Host ''
		Write-Host 'If the entry does not appear, or appears and vanishes again, this machine ignores'
		Write-Host 'per-user verbs. Re-run from an elevated prompt with -Machine.'
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
