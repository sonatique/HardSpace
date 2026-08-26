using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace HardSpace;

/// <summary>
/// The scan window, written straight against user32: a status line, a read-only text box and two
/// buttons. There is exactly one window per process, so all of its state is static -- which is also
/// what lets the window procedure be an <see cref="UnmanagedCallersOnlyAttribute"/> function pointer
/// rather than a marshalled delegate, the form AOT compilation needs.
/// </summary>
internal static unsafe class ScanWindow
{
	private const string ClassName = "HardSpaceScanWindow";
	private const int IdOk = 1;      // IsDialogMessage turns Enter into this
	private const int IdCopy = 1001;
	private const int IdClose = 1002;
	private const uint WmProgress = Win32.WM_APP + 1;
	private const uint WmFinished = Win32.WM_APP + 2;

	private static readonly CancellationTokenSource Cancellation = new();

	private static IntPtr _window;
	private static IntPtr _status;
	private static IntPtr _output;
	private static IntPtr _copyButton;
	private static IntPtr _closeButton;
	private static IntPtr _uiFont;
	private static IntPtr _monoFont;
	private static int _dpi = 96;

	private static string _statusText = "Scanning...";
	private static string _outputText = string.Empty;
	private static int _progressPending;
	private static bool _finished;

	public static void Run(string root)
	{
		IntPtr instance = Win32.GetModuleHandle(null);
		Create(instance, root);

		_ = Task.Run(() =>
		{
			string status;
			string output;
			try
			{
				ScanResult result = FolderScanner.Scan(root, new WindowProgress(), Cancellation.Token);
				status = result.Cancelled ? "Cancelled." : "Done.";
				output = Report.Build(result);
			}
			catch (Exception exception)
			{
				status = "Failed.";
				output = exception.ToString();
			}

			Volatile.Write(ref _statusText, status);
			Volatile.Write(ref _outputText, output);
			Win32.PostMessage(_window, WmFinished, IntPtr.Zero, IntPtr.Zero);
		});

		Pump();
	}

	private static void Create(IntPtr instance, string root)
	{
		fixed (char* className = ClassName)
		{
			Win32.WNDCLASSEX wndClass = new()
			{
				cbSize = (uint)sizeof(Win32.WNDCLASSEX),
				lpfnWndProc = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, IntPtr>)&WndProc,
				hInstance = instance,
				hIcon = Win32.LoadIcon(IntPtr.Zero, Win32.IDI_APPLICATION),
				hIconSm = Win32.LoadIcon(IntPtr.Zero, Win32.IDI_APPLICATION),
				hCursor = Win32.LoadCursor(IntPtr.Zero, Win32.IDC_ARROW),
				hbrBackground = Win32.GetSysColorBrush(Win32.COLOR_BTNFACE),
				lpszClassName = (IntPtr)className,
			};

			if (Win32.RegisterClassEx(wndClass) == 0)
				throw new InvalidOperationException($"RegisterClassEx failed ({Marshal.GetLastWin32Error()}).");
		}

		_window = Win32.CreateWindowEx(
			0, ClassName, "HardSpace -- " + root, Win32.WS_OVERLAPPEDWINDOW,
			unchecked((int)0x80000000), unchecked((int)0x80000000), 720, 470,
			IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);

		if (_window == IntPtr.Zero)
			throw new InvalidOperationException($"CreateWindowEx failed ({Marshal.GetLastWin32Error()}).");

		_dpi = (int)Win32.GetDpiForWindow(_window);
		if (_dpi <= 0)
			_dpi = 96;

		_uiFont = CreateFont("Segoe UI", 9);
		_monoFont = CreateFont("Consolas", 10);

		_status = CreateChild("STATIC", _statusText, Win32.WS_VISIBLE | Win32.WS_CHILD, 0, IntPtr.Zero, _uiFont);
		_output = CreateChild(
			"EDIT",
			root,
			Win32.WS_VISIBLE | Win32.WS_CHILD | Win32.WS_VSCROLL | Win32.WS_HSCROLL | Win32.WS_TABSTOP
				| Win32.ES_MULTILINE | Win32.ES_READONLY | Win32.ES_AUTOVSCROLL | Win32.ES_AUTOHSCROLL,
			Win32.WS_EX_CLIENTEDGE,
			IntPtr.Zero,
			_monoFont);
		_copyButton = CreateChild("BUTTON", "&Copy", Win32.WS_VISIBLE | Win32.WS_CHILD | Win32.WS_TABSTOP, 0, IdCopy, _uiFont);
		_closeButton = CreateChild("BUTTON", "Cancel", Win32.WS_VISIBLE | Win32.WS_CHILD | Win32.WS_TABSTOP | Win32.BS_DEFPUSHBUTTON, 0, IdClose, _uiFont);

		Win32.EnableWindow(_copyButton, false);
		Layout();
		Win32.ShowWindow(_window, Win32.SW_SHOW);
		Win32.UpdateWindow(_window);
		Win32.SetFocus(_closeButton);
	}

	private static IntPtr CreateChild(string className, string text, uint style, uint exStyle, IntPtr id, IntPtr font)
	{
		IntPtr child = Win32.CreateWindowEx(exStyle, className, text, style, 0, 0, 0, 0, _window, id, IntPtr.Zero, IntPtr.Zero);
		if (child == IntPtr.Zero)
			throw new InvalidOperationException($"CreateWindowEx({className}) failed ({Marshal.GetLastWin32Error()}).");

		Win32.SendMessage(child, Win32.WM_SETFONT, font, 1);
		return child;
	}

	private static IntPtr CreateFont(string face, int points)
	{
		// Negative height asks GDI for a character height rather than a cell height, which is what
		// point sizes mean; scaling by the window DPI keeps it right on high-density displays.
		int height = -(points * _dpi / 72);
		return Win32.CreateFont(height, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, face);
	}

	private static void Layout()
	{
		if (!Win32.GetClientRect(_window, out Win32.RECT client))
			return;

		int margin = Scale(10);
		int statusHeight = Scale(20);
		int buttonWidth = Scale(92);
		int buttonHeight = Scale(28);
		int gap = Scale(8);

		int buttonTop = client.Height - margin - buttonHeight;
		Win32.SetWindowPos(_status, IntPtr.Zero, margin, margin, client.Width - (2 * margin), statusHeight, Win32.SWP_NOZORDER);
		Win32.SetWindowPos(
			_output, IntPtr.Zero,
			margin, margin + statusHeight + gap,
			client.Width - (2 * margin),
			Math.Max(0, buttonTop - gap - (margin + statusHeight + gap)),
			Win32.SWP_NOZORDER);
		Win32.SetWindowPos(_closeButton, IntPtr.Zero, client.Width - margin - buttonWidth, buttonTop, buttonWidth, buttonHeight, Win32.SWP_NOZORDER);
		Win32.SetWindowPos(_copyButton, IntPtr.Zero, client.Width - margin - (2 * buttonWidth) - gap, buttonTop, buttonWidth, buttonHeight, Win32.SWP_NOZORDER);
	}

	private static int Scale(int value) => value * _dpi / 96;

	private static void Pump()
	{
		while (true)
		{
			int result = Win32.GetMessage(out Win32.MSG message, IntPtr.Zero, 0, 0);
			if (result is 0 or -1)
				break;

			if (message.message == Win32.WM_KEYDOWN && (int)message.wParam == Win32.VK_ESCAPE)
			{
				Win32.PostMessage(_window, Win32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
				continue;
			}

			// Gives the plain window dialog-style Tab navigation between the text box and buttons.
			if (Win32.IsDialogMessage(_window, ref message))
				continue;

			Win32.TranslateMessage(message);
			Win32.DispatchMessage(message);
		}
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static IntPtr WndProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam)
	{
		// Nothing may escape into the Win32 dispatcher, so every handler runs inside this guard.
		try
		{
			switch (message)
			{
				case Win32.WM_SIZE:
					Layout();
					return IntPtr.Zero;

				case WmProgress:
					Interlocked.Exchange(ref _progressPending, 0);
					if (!_finished)
					{
						Win32.SetWindowText(_status, Volatile.Read(ref _statusText));
						Win32.SetWindowText(_output, Volatile.Read(ref _outputText));
					}

					return IntPtr.Zero;

				case WmFinished:
					_finished = true;
					Win32.SetWindowText(_status, Volatile.Read(ref _statusText));
					Win32.SetWindowText(_output, Volatile.Read(ref _outputText));
					Win32.EnableWindow(_copyButton, true);
					Win32.SetWindowText(_closeButton, "Close");
					Win32.SetFocus(_closeButton);
					return IntPtr.Zero;

				case Win32.WM_COMMAND:
					switch ((int)wParam & 0xFFFF)
					{
						case IdCopy:
							CopyToClipboard(Volatile.Read(ref _outputText));
							return IntPtr.Zero;

						case IdClose:
						case IdOk:
							Win32.PostMessage(hWnd, Win32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
							return IntPtr.Zero;
					}

					break;

				case Win32.WM_CLOSE:
					Cancellation.Cancel();
					Win32.DestroyWindow(hWnd);
					return IntPtr.Zero;

				case Win32.WM_DESTROY:
					if (_uiFont != IntPtr.Zero)
						Win32.DeleteObject(_uiFont);
					if (_monoFont != IntPtr.Zero)
						Win32.DeleteObject(_monoFont);
					Win32.PostQuitMessage(0);
					return IntPtr.Zero;
			}
		}
		catch (Exception exception)
		{
			Win32.MessageBox(IntPtr.Zero, exception.ToString(), "HardSpace", 0x10 /* MB_ICONERROR */);
		}

		return Win32.DefWindowProc(hWnd, message, wParam, lParam);
	}

	private static void CopyToClipboard(string text)
	{
		if (text.Length == 0 || !Win32.OpenClipboard(_window))
			return;

		try
		{
			Win32.EmptyClipboard();

			nuint bytes = (nuint)((text.Length + 1) * sizeof(char));
			IntPtr memory = Win32.GlobalAlloc(Win32.GMEM_MOVEABLE, bytes);
			if (memory == IntPtr.Zero)
				return;

			IntPtr target = Win32.GlobalLock(memory);
			if (target == IntPtr.Zero)
			{
				Win32.GlobalFree(memory);
				return;
			}

			fixed (char* source = text)
				Buffer.MemoryCopy(source, (void*)target, (long)bytes, (long)text.Length * sizeof(char));

			((char*)target)[text.Length] = '\0';
			Win32.GlobalUnlock(memory);

			// On success the clipboard owns the block; on failure it is still ours to release.
			if (Win32.SetClipboardData(Win32.CF_UNICODETEXT, memory) == IntPtr.Zero)
				Win32.GlobalFree(memory);
		}
		finally
		{
			Win32.CloseClipboard();
		}
	}

	/// <summary>
	/// Hands progress to the window thread: the text is published to a field and a single message is
	/// posted, so a fast scan cannot flood the queue with redundant repaints.
	/// </summary>
	private sealed class WindowProgress : IProgress<ScanProgress>
	{
		public void Report(ScanProgress value)
		{
			Volatile.Write(ref _statusText, string.Create(CultureInfo.CurrentCulture,
				$"{HardSpace.Report.Count(value.Files)} files, {HardSpace.Report.Count(value.Directories)} folders, {HardSpace.Report.Bytes(value.ApparentSize)}..."));
			Volatile.Write(ref _outputText, value.CurrentDirectory);

			if (Interlocked.Exchange(ref _progressPending, 1) == 0)
				Win32.PostMessage(_window, WmProgress, IntPtr.Zero, IntPtr.Zero);
		}
	}
}
