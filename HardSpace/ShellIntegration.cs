using System;
using System.Diagnostics;
using System.Text;
using Microsoft.Win32;

namespace HardSpace;

/// <summary>
/// Adds and removes the Explorer context-menu entry. Everything is written under HKCU, so no
/// elevation is needed and the entry follows the user rather than the machine.
/// </summary>
internal static class ShellIntegration
{
	private const string KeyName = "HardSpace";
	private const string MenuText = "Folder size (hard-link aware)";

	// Right-click on a folder, on the background of an open folder, and on a drive. The verb key
	// holds the label and icon; the "command" subkey holds the command line.
	private static readonly (string Path, string Argument)[] Targets =
	[
		(@"Software\Classes\Directory\shell\" + KeyName, "\"%1\""),
		(@"Software\Classes\Directory\Background\shell\" + KeyName, "\"%V\""),
		(@"Software\Classes\Drive\shell\" + KeyName, "\"%1\""),
	];

	public static string ExecutablePath => Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;

	public static string Register()
	{
		string executable = ExecutablePath;
		foreach ((string path, string argument) in Targets)
		{
			using RegistryKey key = Registry.CurrentUser.CreateSubKey(path, writable: true);
			key.SetValue(null, MenuText);
			key.SetValue("Icon", executable);
			using RegistryKey command = key.CreateSubKey("command", writable: true);
			command.SetValue(null, $"\"{executable}\" {argument}");
		}

		return $"Context-menu entry \"{MenuText}\" installed for the current user.\r\n\r\n"
			+ $"Command: {executable}\r\n\r\n"
			+ "On Windows 11 it appears under \"Show more options\" (Shift+F10) in the folder context menu.";
	}

	public static string Unregister()
	{
		StringBuilder report = new();
		foreach ((string path, _) in Targets)
		{
			try
			{
				Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
			}
			catch (Exception exception)
			{
				report.AppendLine($"{path}: {exception.Message}");
			}
		}

		return report.Length == 0
			? "Context-menu entry removed for the current user."
			: "Context-menu entry partially removed:\r\n" + report;
	}
}
