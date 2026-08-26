using System;
using System.Diagnostics;
using System.Text;
using Microsoft.Win32;

namespace HardSpace;

/// <summary>
/// Adds and removes the Explorer context-menu entry, either for the current user (HKCU, no
/// elevation) or for the machine (HKLM, elevation required).
/// </summary>
/// <remarks>
/// Per-user verbs are the polite default, but they are not universally honoured: on a machine that
/// forces the Windows 11 classic context menu, Explorer was observed drawing an HKCU verb and then
/// dropping it again, while HKLM verbs on the same machine rendered normally. Hence --machine.
/// </remarks>
internal static class ShellIntegration
{
	private const string KeyName = "HardSpace";
	private const string MenuText = "Folder size (hard-link aware)";

	// Right-click on a folder, on the background of an open folder, and on a drive. The verb key
	// holds the label; the "command" subkey holds the command line.
	private static readonly (string Path, string Argument)[] Targets =
	[
		(@"Software\Classes\Directory\shell\" + KeyName, "\"%1\""),
		(@"Software\Classes\Directory\Background\shell\" + KeyName, "\"%V\""),
		(@"Software\Classes\Drive\shell\" + KeyName, "\"%1\""),
	];

	private static RegistryKey Root(bool machineWide) => machineWide ? Registry.LocalMachine : Registry.CurrentUser;

	private static string Scope(bool machineWide) => machineWide ? "all users of this machine" : "the current user";

	public static string ExecutablePath => Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;

	public static string Register(bool machineWide)
	{
		string executable = ExecutablePath;
		try
		{
			foreach ((string path, string argument) in Targets)
			{
				using RegistryKey key = Root(machineWide).CreateSubKey(path, writable: true);
				key.SetValue(null, MenuText);
				using RegistryKey command = key.CreateSubKey("command", writable: true);
				command.SetValue(null, $"\"{executable}\" {argument}");
			}
		}
		catch (UnauthorizedAccessException) when (machineWide)
		{
			return "Registering for all users writes to HKEY_LOCAL_MACHINE, which needs administrator "
				+ "rights. Re-run this from an elevated prompt, or drop --machine to register for "
				+ "the current user only.";
		}

		return $"Context-menu entry \"{MenuText}\" installed for {Scope(machineWide)}.\r\n\r\n"
			+ $"Command: {executable}\r\n\r\n"
			+ "On Windows 11 it appears under \"Show more options\" (Shift+F10) in the folder context "
			+ "menu. Restart Explorer if it does not show up straight away.";
	}

	/// <summary>
	/// Removes the entry from both hives: whichever one it went into, this takes it out, and a
	/// machine-wide entry left behind by an earlier install would otherwise be invisible to a
	/// per-user uninstall.
	/// </summary>
	public static string Unregister(bool machineWide)
	{
		StringBuilder report = new();
		foreach (bool scope in machineWide ? new[] { true, false } : [false])
		{
			foreach ((string path, _) in Targets)
			{
				try
				{
					Root(scope).DeleteSubKeyTree(path, throwOnMissingSubKey: false);
				}
				catch (Exception exception)
				{
					string hive = scope ? "HKLM" : "HKCU";
					report.AppendLine($@"{hive}\{path}: {exception.Message}");
				}
			}
		}

		return report.Length == 0
			? $"Context-menu entry removed for {Scope(machineWide)}."
			: "Context-menu entry partially removed:\r\n" + report;
	}
}
