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

	// At 96 DPI; scaled to the window's actual DPI as soon as there is a window to ask. Deliberately
	// modest: it only has to hold the progress line, because the finished report resizes the window
	// to fit itself and never shrinks it below this.
	private const int DefaultWidth = 560;
	private const int DefaultHeight = 380;

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
	private static bool _succeeded;
	private static bool _statusVisible = true;

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
				_succeeded = !result.Cancelled;
				status = result.Cancelled ? "Cancelled." : string.Empty;
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

		// No caption and no sizing border: WS_POPUP drops the title bar, and the window is sized to
		// its report, so dragging an edge could only reveal blank space. WS_EX_APPWINDOW keeps the
		// taskbar button a popup would otherwise lose -- without it there would be no way back to
		// the window once something covered it.
		// Where the click was: Explorer has just closed a context menu under the cursor, so that is
		// where the folder is on screen. Creating the window there rather than moving it afterwards
		// also means it is born on the right monitor, and reports that monitor's DPI.
		Win32.POINT cursor = default;
		bool haveCursor = Win32.GetCursorPos(out cursor);

		_window = Win32.CreateWindowEx(
			Win32.WS_EX_APPWINDOW, ClassName, "HardSpace -- " + root, Win32.WS_POPUP | Win32.WS_BORDER,
			haveCursor ? cursor.X : unchecked((int)0x80000000),
			haveCursor ? cursor.Y : unchecked((int)0x80000000),
			DefaultWidth, DefaultHeight,
			IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);

		if (_window == IntPtr.Zero)
			throw new InvalidOperationException($"CreateWindowEx failed ({Marshal.GetLastWin32Error()}).");

		_dpi = (int)Win32.GetDpiForWindow(_window);
		if (_dpi <= 0)
			_dpi = 96;

		// CreateWindowEx takes raw pixels, so the size above is only right at 96 DPI: on a 175%
		// display it produced a window barely half the size of its own text. Now that the window
		// exists and can be asked what DPI it is on, restate the size in those terms.
		Win32.SetWindowPos(_window, IntPtr.Zero, 0, 0, Scale(DefaultWidth), Scale(DefaultHeight),
			Win32.SWP_NOZORDER | Win32.SWP_NOMOVE);

		_uiFont = CreateFont("Segoe UI", 9);
		_monoFont = CreateFont("Consolas", 10);

		_status = CreateChild("STATIC", _statusText, Win32.WS_VISIBLE | Win32.WS_CHILD, 0, IntPtr.Zero, _uiFont);
		_output = CreateOutput(root, Win32.WS_VISIBLE | Win32.WS_CHILD | Win32.WS_TABSTOP
			| Win32.ES_MULTILINE | Win32.ES_READONLY | Win32.ES_AUTOVSCROLL | Win32.ES_AUTOHSCROLL);
		_copyButton = CreateChild("BUTTON", "&Copy", Win32.WS_VISIBLE | Win32.WS_CHILD | Win32.WS_TABSTOP, 0, IdCopy, _uiFont);
		_closeButton = CreateChild("BUTTON", "Cancel", Win32.WS_VISIBLE | Win32.WS_CHILD | Win32.WS_TABSTOP | Win32.BS_DEFPUSHBUTTON, 0, IdClose, _uiFont);

		Win32.EnableWindow(_copyButton, false);
		if (haveCursor)
			PlaceNear(cursor, Scale(DefaultWidth), Scale(DefaultHeight));

		Layout();
		Win32.ShowWindow(_window, Win32.SW_SHOW);
		Win32.UpdateWindow(_window);
		Win32.SetFocus(_closeButton);
	}

	/// <summary>
	/// The report's text box. Its scroll bars are deliberately not part of the base style: they are
	/// drawn permanently once present, needed or not, and the window is sized to its content so that
	/// they are not needed. The one case that can still overflow -- a report wider or taller than the
	/// screen allows -- gets them by rebuilding this control, because an edit control fixes its
	/// scroll bars at creation and will not take them from SetWindowLong afterwards.
	/// </summary>
	private static IntPtr CreateOutput(string text, uint style)
		=> CreateChild("EDIT", text, style, Win32.WS_EX_CLIENTEDGE, IntPtr.Zero, _monoFont);

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

		// A finished scan has nothing to say in the status line, so it is taken away entirely and the
		// report gets the space rather than sitting under a blank strip.
		int statusBand = _statusVisible ? statusHeight + gap : 0;

		int buttonTop = client.Height - margin - buttonHeight;
		if (_statusVisible)
			Win32.SetWindowPos(_status, IntPtr.Zero, margin, margin, client.Width - (2 * margin), statusHeight, Win32.SWP_NOZORDER);

		Win32.SetWindowPos(
			_output, IntPtr.Zero,
			margin, margin + statusBand,
			client.Width - (2 * margin),
			Math.Max(0, buttonTop - gap - (margin + statusBand)),
			Win32.SWP_NOZORDER);
		Win32.SetWindowPos(_closeButton, IntPtr.Zero, client.Width - margin - buttonWidth, buttonTop, buttonWidth, buttonHeight, Win32.SWP_NOZORDER);
		Win32.SetWindowPos(_copyButton, IntPtr.Zero, client.Width - margin - (2 * buttonWidth) - gap, buttonTop, buttonWidth, buttonHeight, Win32.SWP_NOZORDER);
	}

	private static int Scale(int value) => value * _dpi / 96;

	/// <summary>
	/// Puts the window just below and right of a point, pulled back inside the monitor's work area
	/// when it would otherwise hang off an edge -- which is what happens for a folder near the
	/// bottom or right of the screen, the common case for a long file list.
	/// </summary>
	private static void PlaceNear(Win32.POINT point, int width, int height)
	{
		int offset = Scale(12);
		int x = point.X + offset;
		int y = point.Y + offset;

		if (TryGetWorkArea(Win32.MonitorFromPoint(point, Win32.MONITOR_DEFAULTTONEAREST), out Win32.RECT work))
		{
			x = Math.Min(x, work.Right - width);
			y = Math.Min(y, work.Bottom - height);
			x = Math.Max(x, work.Left);
			y = Math.Max(y, work.Top);
		}

		Win32.SetWindowPos(_window, IntPtr.Zero, x, y, width, height, Win32.SWP_NOZORDER);
	}

	/// <summary>
	/// The work area of one monitor. SystemParametersInfo(SPI_GETWORKAREA) answers for the primary
	/// monitor only, which is the wrong answer on every other one.
	/// </summary>
	private static bool TryGetWorkArea(IntPtr monitor, out Win32.RECT work)
	{
		Win32.MONITORINFO info = new() { cbSize = (uint)Marshal.SizeOf<Win32.MONITORINFO>() };
		if (monitor != IntPtr.Zero && Win32.GetMonitorInfo(monitor, ref info))
		{
			work = info.Work;
			return true;
		}

		return Win32.SystemParametersInfo(Win32.SPI_GETWORKAREA, 0, out work, 0);
	}

	/// <summary>Recreates the fonts at the current DPI and hands them to the controls.</summary>
	private static void RebuildFonts()
	{
		IntPtr oldUi = _uiFont;
		IntPtr oldMono = _monoFont;

		_uiFont = CreateFont("Segoe UI", 9);
		_monoFont = CreateFont("Consolas", 10);

		Win32.SendMessage(_status, Win32.WM_SETFONT, _uiFont, 1);
		Win32.SendMessage(_output, Win32.WM_SETFONT, _monoFont, 1);
		Win32.SendMessage(_copyButton, Win32.WM_SETFONT, _uiFont, 1);
		Win32.SendMessage(_closeButton, Win32.WM_SETFONT, _uiFont, 1);

		// Only once nothing is drawing with them any more.
		if (oldUi != IntPtr.Zero)
			Win32.DeleteObject(oldUi);
		if (oldMono != IntPtr.Zero)
			Win32.DeleteObject(oldMono);
	}

	/// <summary>
	/// Grows the window so the whole report is visible without scrolling. The report is a handful of
	/// short lines whose length depends on the folder's name and figures, so measuring beats guessing
	/// a size that is either too small for a long path or wastefully large for a short one.
	/// </summary>
	private static void FitToContent(string text)
	{
		if (text.Length == 0)
			return;

		string[] lines = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
		string longest = string.Empty;
		foreach (string line in lines)
		{
			if (line.Length > longest.Length)
				longest = line;
		}

		IntPtr dc = Win32.GetDC(_output);
		if (dc == IntPtr.Zero)
			return;

		// Measured with two trailing spaces: the edit control keeps a small inset of its own on each
		// side, and a line ending flush against the border reads as clipped even when it is not.
		string measuring = longest + "  ";
		Win32.SIZE extent;
		IntPtr previousFont = Win32.SelectObject(dc, _monoFont);
		bool measured = Win32.GetTextExtentPoint32(dc, measuring, measuring.Length, out extent);
		Win32.SelectObject(dc, previousFont);
		Win32.ReleaseDC(_output, dc);

		if (!measured || extent.Height <= 0)
			return;

		int margin = Scale(10);
		int gap = Scale(8);
		int buttonWidth = Scale(92);
		int buttonHeight = Scale(28);
		int edgeWidth = 2 * Win32.GetSystemMetrics(Win32.SM_CXEDGE);    // the WS_EX_CLIENTEDGE border
		int edgeHeight = 2 * Win32.GetSystemMetrics(Win32.SM_CYEDGE);

		// The text box gets the same breathing room under its last line as there is between the box
		// and the buttons, so the report does not end flush against the border.
		int bottomPadding = gap;

		// What the report needs, with no allowance for scroll bars: the point is not to have any.
		int clientWidth = (2 * margin) + extent.Width + edgeWidth;
		int clientHeight = margin + (lines.Length * extent.Height) + bottomPadding + edgeHeight
			+ gap + buttonHeight + margin;

		// A very short report must still leave room for the two buttons.
		clientWidth = Math.Max(clientWidth, (2 * margin) + (2 * buttonWidth) + gap);

		// The client rectangle has to grow by the frame to become a window size.
		if (!Win32.GetWindowRect(_window, out Win32.RECT window) || !Win32.GetClientRect(_window, out Win32.RECT client))
			return;

		int frameWidth = window.Width - client.Width;
		int frameHeight = window.Height - client.Height;
		int width = clientWidth + frameWidth;
		int height = clientHeight + frameHeight;

		// The window is sized to the report in both directions: the size it was created at only ever
		// had to hold the progress line, and leaving it at that would pad the report with dead space.
		bool haveWork = TryGetWorkArea(Win32.MonitorFromWindow(_window, Win32.MONITOR_DEFAULTTONEAREST), out Win32.RECT workArea);
		if (haveWork)
		{
			width = Math.Min(width, workArea.Width);
			height = Math.Min(height, workArea.Height);
		}

		// Only a window clamped by the screen can still be too small for its report; give that one
		// the scroll bars it needs, and nothing else any.
		int textWidth = width - frameWidth - (2 * margin) - edgeWidth;
		int textHeight = height - frameHeight - margin - edgeHeight - bottomPadding - gap - buttonHeight - margin;
		EnsureScrollBars(
			vertical: lines.Length * extent.Height > textHeight,
			horizontal: extent.Width > textWidth,
			text);

		// Keep the top-left where the click put it, but not at the cost of hanging off the screen.
		int left = window.Left;
		int top = window.Top;
		if (haveWork)
		{
			left = Math.Max(workArea.Left, Math.Min(left, workArea.Right - width));
			top = Math.Max(workArea.Top, Math.Min(top, workArea.Bottom - height));
		}

		Win32.SetWindowPos(_window, IntPtr.Zero, left, top, width, height, Win32.SWP_NOZORDER);
	}

	/// <summary>
	/// Gives the text box the scroll bars it needs, rebuilding it when that changes: an edit control
	/// decides on its scroll bars when it is created and ignores a later style change.
	/// </summary>
	private static void EnsureScrollBars(bool vertical, bool horizontal, string text)
	{
		uint current = (uint)Win32.GetWindowLong(_output, Win32.GWL_STYLE);
		uint wanted = current & ~(Win32.WS_VSCROLL | Win32.WS_HSCROLL);
		if (vertical)
			wanted |= Win32.WS_VSCROLL;
		if (horizontal)
			wanted |= Win32.WS_HSCROLL;

		if (wanted == current)
			return;

		Win32.DestroyWindow(_output);
		_output = CreateOutput(text, wanted);

		// A freshly created child sits on top of the z-order, which is also the tab order; put it
		// back behind the buttons so Tab still reaches Copy and Close in the order they are read.
		Win32.SetWindowPos(_output, Win32.HWND_BOTTOM, 0, 0, 0, 0,
			Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_FRAMECHANGED);
	}

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

				// A window moved to a display at a different scale keeps its old fonts and its old
				// idea of a pixel, which is how a report ends up adrift in a window sized for the
				// other monitor. Rebuild both, then re-fit.
				case Win32.WM_DPICHANGED:
					_dpi = (int)wParam & 0xFFFF;
					RebuildFonts();
					if (_finished)
						FitToContent(Volatile.Read(ref _outputText));
					Layout();
					return IntPtr.Zero;

				// With no title bar to grab, the window's own background becomes the drag handle.
				// Only the margins around the controls report HTCLIENT -- the text box and buttons
				// are asked about their own hits -- so this costs nothing that was usable before,
				// and the resize borders keep whatever DefWindowProc says they are.
				case Win32.WM_NCHITTEST:
				{
					IntPtr hit = Win32.DefWindowProc(hWnd, message, wParam, lParam);
					return (int)hit == Win32.HTCLIENT ? Win32.HTCAPTION : hit;
				}

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
					if (_succeeded)
					{
						Win32.ShowWindow(_status, Win32.SW_HIDE);
						_statusVisible = false;
					}
					else
					{
						Win32.SetWindowText(_status, Volatile.Read(ref _statusText));
					}

					Win32.SetWindowText(_output, Volatile.Read(ref _outputText).TrimEnd('\r', '\n'));
					Win32.EnableWindow(_copyButton, true);
					Win32.SetWindowText(_closeButton, "Close");
					Win32.SetFocus(_closeButton);

					// Hiding the status line moves everything up, and FitToContent may find the
					// window is already big enough and leave it alone -- in which case no WM_SIZE
					// arrives and nothing else would ever re-lay out the controls.
					FitToContent(Volatile.Read(ref _outputText));
					Layout();
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
