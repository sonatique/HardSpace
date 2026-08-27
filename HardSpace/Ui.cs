using System;
using System.IO;
using System.Runtime.InteropServices;

namespace HardSpace;

/// <summary>
/// Talking to whoever ran the program, which is not always someone at a console: this is a WinExe,
/// so an instance started from Explorer or by double-clicking has no console at all. Every message
/// goes to the console when there is one and to a message box when there is not.
/// </summary>
internal static partial class Ui
{
	private const uint MB_OK = 0x00000000;
	private const uint MB_YESNO = 0x00000004;
	private const uint MB_ICONINFORMATION = 0x00000040;
	private const uint MB_ICONQUESTION = 0x00000020;
	private const int IDYES = 6;

	private const uint AttachParentProcess = 0xFFFFFFFF;
	private const int StdOutputHandle = -11;

	public static void Tell(string message)
	{
		if (HasConsole())
		{
			Console.Out.WriteLine(message);
			Console.Out.Flush();
			return;
		}

		Win32.MessageBox(IntPtr.Zero, message, "HardSpace", MB_OK | MB_ICONINFORMATION);
	}

	/// <summary>
	/// Puts a yes/no question to whoever is there to answer it. With no console and no desktop to
	/// show a box on -- an unattended run -- the answer is no: this is only ever used for things
	/// that are the caller's to consent to.
	/// </summary>
	public static bool Ask(string question)
	{
		if (HasConsole())
		{
			Console.Out.WriteLine(question);
			Console.Out.Write("[y/N] ");
			Console.Out.Flush();
			string? answer = Console.In.ReadLine();
			return answer is not null && answer.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase);
		}

		return Win32.MessageBox(IntPtr.Zero, question, "HardSpace", MB_YESNO | MB_ICONQUESTION) == IDYES;
	}

	private static bool HasConsole()
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

	[LibraryImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool AttachConsole(uint dwProcessId);

	[LibraryImport("kernel32.dll", SetLastError = true)]
	private static partial IntPtr GetStdHandle(int nStdHandle);
}
