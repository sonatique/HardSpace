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

	internal const uint WM_DESTROY = 0x0002;
	internal const uint WM_SIZE = 0x0005;
	internal const uint WM_CLOSE = 0x0010;
	internal const uint WM_SETFONT = 0x0030;
	internal const uint WM_COMMAND = 0x0111;
	internal const uint WM_KEYDOWN = 0x0100;
	internal const uint WM_APP = 0x8000;

	internal const int VK_ESCAPE = 0x1B;
	internal const int SW_SHOW = 5;
	internal const uint SWP_NOZORDER = 0x0004;

	internal const int COLOR_BTNFACE = 15;

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

	[LibraryImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

	[LibraryImport("user32.dll")]
	internal static partial IntPtr SetFocus(IntPtr hWnd);

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static partial bool EnableWindow(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool enable);

	[LibraryImport("user32.dll")]
	internal static partial uint GetDpiForWindow(IntPtr hWnd);

	[LibraryImport("user32.dll")]
	internal static partial IntPtr GetSysColorBrush(int index);

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

	[LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
	internal static partial IntPtr GetModuleHandle(string? lpModuleName);

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
