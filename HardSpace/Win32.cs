using System;
using System.Runtime.InteropServices;

namespace HardSpace;

/// <summary>
/// The user32/gdi32/kernel32 surface the window needs. Kept deliberately small: this is what
/// replaces WinForms, which cannot be trimmed or AOT-compiled.
/// </summary>
internal static partial class Win32
{
	internal const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
	internal const uint WS_POPUP = 0x80000000;
	internal const uint WS_THICKFRAME = 0x00040000;
	internal const uint WS_BORDER = 0x00800000;
	internal const uint WS_EX_APPWINDOW = 0x00040000;
	internal const uint WS_CHILD = 0x40000000;
	internal const uint WS_VISIBLE = 0x10000000;
	internal const uint WS_VSCROLL = 0x00200000;
	internal const uint WS_HSCROLL = 0x00100000;
	internal const uint WS_TABSTOP = 0x00010000;
	internal const uint WS_EX_CLIENTEDGE = 0x00000200;

	internal const uint ES_MULTILINE = 0x0004;
	internal const uint ES_READONLY = 0x0800;
	internal const uint ES_AUTOVSCROLL = 0x0040;
	internal const uint ES_AUTOHSCROLL = 0x0080;
	internal const uint BS_DEFPUSHBUTTON = 0x0001;
	internal const uint BS_OWNERDRAW = 0x000B;

	internal const uint WM_DESTROY = 0x0002;
	internal const uint WM_SIZE = 0x0005;
	internal const uint WM_CLOSE = 0x0010;
	internal const uint WM_SETFONT = 0x0030;
	internal const uint WM_COMMAND = 0x0111;
	internal const uint WM_KEYDOWN = 0x0100;
	internal const uint WM_DRAWITEM = 0x002B;
	internal const uint WM_ERASEBKGND = 0x0014;
	internal const uint WM_SETTINGCHANGE = 0x001A;
	internal const uint WM_CTLCOLOREDIT = 0x0133;
	internal const uint WM_CTLCOLORSTATIC = 0x0138;
	internal const uint WM_CTLCOLORBTN = 0x0135;
	internal const uint WM_NCHITTEST = 0x0084;
	internal const uint WM_DPICHANGED = 0x02E0;
	internal const uint WM_APP = 0x8000;

	internal const int HTCLIENT = 1;
	internal const int HTCAPTION = 2;

	internal const int VK_ESCAPE = 0x1B;
	internal const int SW_HIDE = 0;
	internal const int SW_SHOW = 5;
	internal const uint SWP_NOMOVE = 0x0002;
	internal const uint SWP_NOSIZE = 0x0001;
	internal const uint SWP_FRAMECHANGED = 0x0020;

	internal const int GWL_STYLE = -16;
	internal static readonly IntPtr HWND_BOTTOM = new(1);

	internal const int SM_CXEDGE = 45;
	internal const int SM_CYEDGE = 46;
	internal const int SM_CXVSCROLL = 2;
	internal const int SM_CYHSCROLL = 3;
	internal const uint SPI_GETWORKAREA = 0x0030;
	internal const uint MONITOR_DEFAULTTONEAREST = 2;
	internal const uint SWP_NOZORDER = 0x0004;

	internal const int COLOR_BTNFACE = 15;

	// Dwm window attributes: 20 asks for the dark non-client area, 33 for Windows 11's rounded
	// corners, 34 sets the border colour.
	internal const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
	internal const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;
	internal const uint DWMWA_BORDER_COLOR = 34;
	internal const int DWMWCP_ROUND = 2;

	internal const int TRANSPARENT = 1;

	// Owner-draw state, and the theme part and states for a push button.
	internal const uint ODS_SELECTED = 0x0001;
	internal const uint ODS_DISABLED = 0x0004;
	internal const uint ODS_FOCUS = 0x0010;
	internal const uint ODS_HOTLIGHT = 0x0040;

	internal const int BP_PUSHBUTTON = 1;
	internal const int PBS_NORMAL = 1;
	internal const int PBS_HOT = 2;
	internal const int PBS_PRESSED = 3;
	internal const int PBS_DISABLED = 4;
	internal const int PBS_DEFAULTED = 5;
	internal const int TMT_TEXTCOLOR = 3803;

	internal const uint DT_CENTER = 0x00000001;
	internal const uint DT_VCENTER = 0x00000004;
	internal const uint DT_SINGLELINE = 0x00000020;

	internal const uint CF_UNICODETEXT = 13;
	internal const uint GMEM_MOVEABLE = 0x0002;

	internal const int IDC_ARROW = 32512;
	internal const int IDI_APPLICATION = 32512;

	[StructLayout(LayoutKind.Sequential)]
	internal struct WNDCLASSEX
	{
		public uint cbSize;
		public uint style;
		public IntPtr lpfnWndProc;
		public int cbClsExtra;
		public int cbWndExtra;
		public IntPtr hInstance;
		public IntPtr hIcon;
		public IntPtr hCursor;
		public IntPtr hbrBackground;
		public IntPtr lpszMenuName;
		public IntPtr lpszClassName;
		public IntPtr hIconSm;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct MSG
	{
		public IntPtr hwnd;
		public uint message;
		public IntPtr wParam;
		public IntPtr lParam;
		public uint time;
		public int ptX;
		public int ptY;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct POINT
	{
		public int X;
		public int Y;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct MONITORINFO
	{
		public uint cbSize;
		public RECT Monitor;
		public RECT Work;
		public uint Flags;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct DRAWITEMSTRUCT
	{
		public uint CtlType;
		public uint CtlID;
		public uint ItemID;
		public uint ItemAction;
		public uint ItemState;
		public IntPtr Item;
		public IntPtr DeviceContext;
		public RECT ItemRect;
		public IntPtr ItemData;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct SIZE
	{
		public int Width;
		public int Height;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct RECT
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;

		public readonly int Width => Right - Left;
		public readonly int Height => Bottom - Top;
	}

	[LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
	internal static partial ushort RegisterClassEx(in WNDCLASSEX wndClass);

	[LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
	internal static partial IntPtr CreateWindowEx(
		uint dwExStyle, string lpClassName, string? lpWindowName, uint dwStyle,
		int x, int y, int width, int height,
		IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

	[LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
	internal static partial IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	[LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
	internal static partial IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	[LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static partial bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	[LibraryImport("user32.dll", EntryPoint = "SetWindowTextW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static partial bool SetWindowText(IntPtr hWnd, string text);

	[LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
	internal static partial int MessageBox(IntPtr hWnd, string text, string caption, uint type);

	[LibraryImport("user32.dll", EntryPoint = "LoadCursorW", SetLastError = true)]
	internal static partial IntPtr LoadCursor(IntPtr hInstance, IntPtr lpCursorName);

	[LibraryImport("user32.dll", EntryPoint = "LoadIconW", SetLastError = true)]
	internal static partial IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static partial bool UpdateWindow(IntPtr hWnd);

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static partial bool DestroyWindow(IntPtr hWnd);

	[LibraryImport("user32.dll")]
	internal static partial void PostQuitMessage(int nExitCode);

	[LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
	internal static partial int GetMessage(out MSG lpMsg, IntPtr hWnd, uint filterMin, uint filterMax);

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static partial bool TranslateMessage(in MSG lpMsg);

	[LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
	internal static partial IntPtr DispatchMessage(in MSG lpMsg);

	[LibraryImport("user32.dll", EntryPoint = "IsDialogMessageW")]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static partial bool IsDialogMessage(IntPtr hDlg, ref MSG lpMsg);

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static partial bool GetClientRect(IntPtr hWnd, out RECT lpRect);

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static partial bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

	[LibraryImport("user32.dll")]
	internal static partial int GetSystemMetrics(int index);

	[LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static partial bool SystemParametersInfo(uint action, uint param, out RECT rect, uint winIni);

	[LibraryImport("user32.dll")]
	internal static partial IntPtr GetDC(IntPtr hWnd);

	[LibraryImport("user32.dll")]
	internal static partial int ReleaseDC(IntPtr hWnd, IntPtr hDC);

	[LibraryImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

	[LibraryImport("user32.dll")]
	internal static partial IntPtr SetFocus(IntPtr hWnd);

	[LibraryImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
	internal static partial int GetWindowLong(IntPtr hWnd, int index);

	[LibraryImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
	internal static partial int SetWindowLong(IntPtr hWnd, int index, int value);

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static partial bool EnableWindow(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool enable);

	[LibraryImport("user32.dll")]
	internal static partial uint GetDpiForWindow(IntPtr hWnd);

	[LibraryImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static partial bool GetCursorPos(out POINT point);

	[LibraryImport("user32.dll")]
	internal static partial IntPtr MonitorFromPoint(POINT point, uint flags);

	[LibraryImport("user32.dll")]
	internal static partial IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

	[LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static partial bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

	[LibraryImport("user32.dll")]
	internal static partial IntPtr GetSysColorBrush(int index);

	[LibraryImport("user32.dll")]
	internal static partial int GetSysColor(int index);

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static partial bool OpenClipboard(IntPtr hWndNewOwner);

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static partial bool CloseClipboard();

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static partial bool EmptyClipboard();

	[LibraryImport("user32.dll", SetLastError = true)]
	internal static partial IntPtr SetClipboardData(uint format, IntPtr hMem);

	[LibraryImport("gdi32.dll", EntryPoint = "CreateFontW", StringMarshalling = StringMarshalling.Utf16)]
	internal static partial IntPtr CreateFont(
		int height, int width, int escapement, int orientation, int weight,
		uint italic, uint underline, uint strikeOut, uint charSet,
		uint outputPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string faceName);

	[LibraryImport("gdi32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static partial bool DeleteObject(IntPtr hObject);

	[LibraryImport("gdi32.dll")]
	internal static partial IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

	[LibraryImport("gdi32.dll")]
	internal static partial IntPtr CreateSolidBrush(uint color);

	[LibraryImport("gdi32.dll")]
	internal static partial IntPtr CreatePen(int style, int width, uint color);

	[LibraryImport("gdi32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static partial bool RoundRect(IntPtr hDC, int left, int top, int right, int bottom, int cornerWidth, int cornerHeight);

	[LibraryImport("gdi32.dll")]
	internal static partial uint SetTextColor(IntPtr hDC, uint color);

	[LibraryImport("gdi32.dll")]
	internal static partial uint SetBkColor(IntPtr hDC, uint color);

	[LibraryImport("gdi32.dll")]
	internal static partial int SetBkMode(IntPtr hDC, int mode);

	[LibraryImport("user32.dll")]
	internal static partial int FillRect(IntPtr hDC, in RECT rect, IntPtr brush);

	[LibraryImport("dwmapi.dll")]
	internal static partial int DwmSetWindowAttribute(IntPtr hWnd, uint attribute, in int value, uint size);

	[LibraryImport("uxtheme.dll", EntryPoint = "SetWindowTheme", StringMarshalling = StringMarshalling.Utf16)]
	internal static partial int SetWindowTheme(IntPtr hWnd, string? subAppName, string? subIdList);

	[LibraryImport("uxtheme.dll", EntryPoint = "OpenThemeData", StringMarshalling = StringMarshalling.Utf16)]
	internal static partial IntPtr OpenThemeData(IntPtr hWnd, string classList);

	[LibraryImport("uxtheme.dll")]
	internal static partial int CloseThemeData(IntPtr theme);

	[LibraryImport("uxtheme.dll")]
	internal static partial int DrawThemeBackground(IntPtr theme, IntPtr hDC, int part, int state, in RECT rect, IntPtr clip);

	[LibraryImport("uxtheme.dll")]
	internal static partial int GetThemeColor(IntPtr theme, int part, int state, int property, out uint color);

	[LibraryImport("user32.dll", EntryPoint = "DrawTextW", StringMarshalling = StringMarshalling.Utf16)]
	internal static partial int DrawText(IntPtr hDC, string text, int length, ref RECT rect, uint format);

	[LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
	internal static partial int GetWindowText(IntPtr hWnd, [Out] char[] text, int maxCount);

	[LibraryImport("gdi32.dll", EntryPoint = "GetTextExtentPoint32W", StringMarshalling = StringMarshalling.Utf16)]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static partial bool GetTextExtentPoint32(IntPtr hDC, string text, int length, out SIZE size);

	[LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
	internal static partial IntPtr GetModuleHandle(string? lpModuleName);

	[LibraryImport("kernel32.dll", EntryPoint = "LoadLibraryW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
	internal static partial IntPtr LoadLibrary(string lpLibFileName);

	// The ordinal overload: the dark-mode helpers in uxtheme have no exported names.
	[LibraryImport("kernel32.dll", EntryPoint = "GetProcAddress", SetLastError = true)]
	internal static partial IntPtr GetProcAddress(IntPtr hModule, IntPtr lpProcName);

	internal static IntPtr GetProcAddress(IntPtr module, int ordinal) => GetProcAddress(module, (IntPtr)ordinal);

	[LibraryImport("kernel32.dll", SetLastError = true)]
	internal static partial IntPtr GlobalAlloc(uint flags, nuint bytes);

	[LibraryImport("kernel32.dll", SetLastError = true)]
	internal static partial IntPtr GlobalLock(IntPtr hMem);

	[LibraryImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static partial bool GlobalUnlock(IntPtr hMem);

	[LibraryImport("kernel32.dll", SetLastError = true)]
	internal static partial IntPtr GlobalFree(IntPtr hMem);
}
