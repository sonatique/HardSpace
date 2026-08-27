<#
.SYNOPSIS
	Regenerates HardSpace\Properties\App.ico from MakeIcon.cs.

.DESCRIPTION
	The icon is drawn rather than authored, so changing it means changing a few numbers in
	MakeIcon.cs and running this. The .ico is committed as well: nothing in the build depends on
	this script, and a normal build must not need System.Drawing or PowerShell.

	MakeIcon.cs lives outside both project directories on purpose -- a .cs file under HardSpace\
	would be swept into the compilation by the SDK's source glob.

.EXAMPLE
	.\Build-Icon.ps1
#>

[CmdletBinding()]
param(
	[string] $OutputPath,
	[int[]] $Sizes = @(16, 20, 24, 32, 48, 64, 128, 256)
)

$ErrorActionPreference = 'Stop'

# $PSScriptRoot is not populated while parameter defaults are being bound under `powershell -File`,
# so the script's own folder is worked out here, in the body, and the defaults are filled in after.
$root = $PSScriptRoot
if (-not $root) { $root = Split-Path -Parent $MyInvocation.MyCommand.Definition }

if (-not $OutputPath) { $OutputPath = Join-Path (Split-Path -Parent $root) 'HardSpace\Properties\App.ico' }

Add-Type -Path (Join-Path $root 'MakeIcon.cs') -ReferencedAssemblies System.Drawing
Write-Host ([MakeIcon]::Build($OutputPath, $Sizes))
