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
		  HardSpace --register          Add the Explorer folder context-menu entry (current user).
		  HardSpace --unregister        Remove that context-menu entry.
		  HardSpace --help              Show this text.

		With no folder given, the current directory is scanned.
		""";

	[STAThread]
	private static int Main(string[] args)
	{
		bool console = false;
		string? path = null;

		foreach (string argument in args)
		{
			switch (argument)
			{
				case "-c" or "--console":
					console = true;
					break;

				case "-h" or "--help" or "/?":
					Tell(Usage, console: true);
					return 0;

				// Registration reports to the console it was launched from, and to a message box when
				// there is none -- it is just as likely to be run by double-clicking the executable.
				case "--register":
					Tell(ShellIntegration.Register(), console: true);
					return 0;

				case "--unregister":
					Tell(ShellIntegration.Unregister(), console: true);
					return 0;

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
