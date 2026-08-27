using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace HardSpace;

internal static partial class Program
{
	private const string Usage = """
		HardSpace -- folder size that does not double-count hard links.

		  HardSpace <folder>            Scan a folder and show the result in a window.
		  HardSpace -c|--console <dir>  Scan and print the result to the console.
		  HardSpace --install [folder]  Install: copy this executable somewhere permanent and add it
		                                to Explorer's context menu. Elevated it installs for every
		                                user, and adds the Windows 11 short-menu package this build
		                                carries; otherwise for the current user only.
		    --user, --machine           Force the scope instead of taking the best available.
		    --no-short-menu             Leave the Windows 11 short-menu package out.
		    --quiet                     Do not ask about Explorer, and do not restart it.
		    --restart-explorer          Restart Explorer without asking.
		  HardSpace --uninstall         Undo all of that.

		  HardSpace --register          Add the context-menu entry for this executable, where it is.
		  HardSpace --unregister        Remove that context-menu entry.
		  --machine                     With either of those: all users instead of the current one.
		                                Writes to HKLM, so it needs an elevated prompt. Required on
		                                machines where Explorer ignores per-user verbs.
		  HardSpace --help              Show this text.

		With no folder given, the current directory is scanned.
		""";

	[STAThread]
	private static int Main(string[] args)
	{
		// Flags are collected first so that they hold whatever order they were given in: "--register
		// --machine" and "--machine --register" must mean the same thing.
		bool console = false;
		bool machineWide = false;
		bool currentUserOnly = false;
		bool? shortMenu = null;
		bool quiet = false;
		bool restartExplorer = false;
		string? path = null;
		string? action = null;

		foreach (string argument in args)
		{
			switch (argument)
			{
				case "-c" or "--console":
					console = true;
					break;

				case "--machine":
					machineWide = true;
					break;

				case "--user":
					currentUserOnly = true;
					break;

				case "--short-menu":
					shortMenu = true;
					break;

				case "--no-short-menu":
					shortMenu = false;
					break;

				case "--quiet":
					quiet = true;
					break;

				case "--restart-explorer":
					restartExplorer = true;
					break;

				case "-h" or "--help" or "/?" or "--register" or "--unregister" or "--install" or "--uninstall":
					action = argument;
					break;

				default:
					if (argument.StartsWith('-'))
					{
						Tell($"Unknown option: {argument}\r\n\r\n{Usage}", console: true);
						return 2;
					}

					path ??= argument;
					break;
			}
		}

		switch (action)
		{
			case "-h" or "--help" or "/?":
				Tell(Usage, console: true);
				return 0;

			// Registration reports to the console it was launched from, and to a message box when
			// there is none -- it is just as likely to be run by double-clicking the executable.
			case "--register":
				Tell(ShellIntegration.Register(machineWide), console: true);
				return 0;

			case "--unregister":
				Tell(ShellIntegration.Unregister(machineWide), console: true);
				return 0;

			// The whole install, from one file: copy this executable somewhere permanent, register it,
			// and put in the short-menu package if this build carries one and the prompt allows it.
			case "--install":
				return Installer.Install(new InstallOptions
				{
					Scope = machineWide ? Scope.Machine : currentUserOnly ? Scope.CurrentUser : Scope.Best,
					ShortMenu = shortMenu,
					Directory = path,
					Quiet = quiet,
					RestartExplorer = restartExplorer,
				});

			case "--uninstall":
				return Installer.Uninstall(new InstallOptions
				{
					Scope = currentUserOnly ? Scope.CurrentUser : machineWide ? Scope.Machine : Scope.Best,
					Directory = path,
					Quiet = quiet,
					RestartExplorer = restartExplorer,
				});
		}

		// Explorer passes the folder with a trailing backslash for a drive root ("D:\") and without
		// one otherwise; GetFullPath normalises both, and quotes are already stripped by the shell.
		string root;
		try
		{
			root = Path.GetFullPath(path ?? Directory.GetCurrentDirectory());
		}
		catch (Exception exception)
		{
			Tell($"Invalid path: {exception.Message}", console);
			return 2;
		}

		if (!Directory.Exists(root))
		{
			Tell($"Not a folder: {root}", console);
			return 2;
		}

		if (console)
		{
			ScanResult result = FolderScanner.Scan(root, progress: null, CancellationToken.None);
			Tell(Report.Build(result), console: true);
			return result.Cancelled ? 1 : 0;
		}

		ScanWindow.Run(root);
		return 0;
	}

	/// <summary>
	/// Writes to the console when the process has one, and falls back to a message box: this is a
	/// WinExe, so an Explorer-launched or double-clicked instance has no console at all. When it was
	/// started from a shell that gave it no standard handles, it borrows the parent's console.
	/// </summary>
	private static void Tell(string message, bool console)
	{
		if (console && HasStandardOutput())
		{
			Console.Out.WriteLine(message);
			Console.Out.Flush();
			return;
		}

		Win32.MessageBox(IntPtr.Zero, message, "HardSpace", MB_OK | MB_ICONINFORMATION);
	}

	private static bool HasStandardOutput()
	{
		if (IsValid(GetStdHandle(StdOutputHandle)))
			return true;

		if (!AttachConsole(AttachParentProcess) || !IsValid(GetStdHandle(StdOutputHandle)))
			return false;

		// Console.Out was resolved to a null writer before the console existed; rebind it.
		Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
		return true;

		static bool IsValid(IntPtr handle) => handle != IntPtr.Zero && handle != new IntPtr(-1);
	}

	private const uint MB_OK = 0x00000000;
	private const uint MB_ICONINFORMATION = 0x00000040;
	private const uint AttachParentProcess = 0xFFFFFFFF;
	private const int StdOutputHandle = -11;

	[LibraryImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool AttachConsole(uint dwProcessId);

	[LibraryImport("kernel32.dll", SetLastError = true)]
	private static partial IntPtr GetStdHandle(int nStdHandle);
}
