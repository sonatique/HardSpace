using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace HardSpace;

/// <summary>
/// Making a raw user32 window look like it belongs on Windows 11: the system's light or dark
/// colours, rounded corners, and controls drawn by the dark common-control themes rather than left
/// white in a dark window.
/// </summary>
/// <remarks>
/// None of this is automatic for a window built from CreateWindowEx. The colours a control paints
/// with are the ones its parent hands back from WM_CTLCOLOR*, the corners come from a DWM attribute,
/// and the dark variants of the control themes have to be asked for by name.
/// </remarks>
internal static unsafe class Theme
{
	// COLORREF is 0x00BBGGRR, which reads backwards from the hex everyone quotes for these colours.
	private const uint DarkBackground = 0x00202020;
	private const uint DarkControl = 0x002B2B2B;
	private const uint DarkText = 0x00F0F0F0;
	private const uint DarkBorder = 0x00404040;

	// Explorer's light surface is white, not the old dialog grey that COLOR_BTNFACE still returns.
	private const uint LightBackground = 0x00FFFFFF;
	private const uint LightText = 0x00000000;

	private static IntPtr _backgroundBrush;
	private static IntPtr _controlBrush;

	public static bool IsDark { get; private set; }

	/// <summary>The background the window paints, and the controls sit on.</summary>
	public static IntPtr BackgroundBrush => _backgroundBrush;

	/// <summary>
	/// Reads the system's app theme. Windows exposes it as a per-user registry value and nothing
	/// friendlier; absent or unreadable means light, which is Windows' own default.
	/// </summary>
	public static bool SystemPrefersDark()
	{
		try
		{
			using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
				@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
			return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
		}
		catch (Exception)
		{
			return false;
		}
	}

	/// <summary>Applies the current system theme to a window and its controls.</summary>
	public static void Apply(IntPtr window, params IntPtr[] children)
	{
		IsDark = SystemPrefersDark();

		IntPtr oldBackground = _backgroundBrush;
		IntPtr oldControl = _controlBrush;
		_backgroundBrush = Win32.CreateSolidBrush(IsDark ? DarkBackground : LightBackground);
		_controlBrush = Win32.CreateSolidBrush(IsDark ? DarkControl : LightBackground);

		int dark = IsDark ? 1 : 0;
		Win32.DwmSetWindowAttribute(window, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, in dark, sizeof(int));

		int corner = Win32.DWMWCP_ROUND;
		Win32.DwmSetWindowAttribute(window, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, in corner, sizeof(int));

		// A dark window with the default border reads as a white outline around black.
		int border = unchecked((int)(IsDark ? DarkBorder : 0xFFFFFFFF));   // 0xFFFFFFFF: leave it to DWM
		Win32.DwmSetWindowAttribute(window, Win32.DWMWA_BORDER_COLOR, in border, sizeof(int));

		// Push buttons draw themselves, and ignore both the parent's colours and the theme name,
		// until the process opts in through the entry points Explorer uses for its own dark mode.
		// They are exported by ordinal only and documented nowhere, so a miss is simply accepted:
		// the window is still dark, with light buttons, which is what it looked like before.
		PreferDarkMode(IsDark);
		AllowDarkMode(window, IsDark);

		foreach (IntPtr child in children)
		{
			AllowDarkMode(child, IsDark);
			Win32.SetWindowTheme(child, IsDark ? "DarkMode_Explorer" : null, null);
		}

		if (oldBackground != IntPtr.Zero)
			Win32.DeleteObject(oldBackground);
		if (oldControl != IntPtr.Zero)
			Win32.DeleteObject(oldControl);
	}

	/// <summary>
	/// Answers a WM_CTLCOLOR* message: sets the colours the control is about to paint with, and
	/// returns the brush for its background. A read-only edit asks with WM_CTLCOLORSTATIC rather
	/// than WM_CTLCOLOREDIT, so both arrive here.
	/// </summary>
	/// <remarks>
	/// Answered in both themes, not just the dark one. Left to itself a read-only edit paints its
	/// background with the button face -- the old dialog grey -- however white the window around it
	/// is, and the only way to say otherwise is to hand back a brush from here.
	/// </remarks>
	public static IntPtr ControlColor(IntPtr deviceContext, bool isTextBox)
	{
		Win32.SetTextColor(deviceContext, IsDark ? DarkText : LightText);
		Win32.SetBkColor(deviceContext, IsDark ? (isTextBox ? DarkControl : DarkBackground) : LightBackground);
		return isTextBox ? _controlBrush : _backgroundBrush;
	}

	/// <summary>
	/// Draws a push button the way the shell draws its own. Push buttons paint themselves and ignore
	/// both the parent's colours and SetWindowTheme, so in a dark window they stay stubbornly light;
	/// owner-drawing them through the theme renderer is what Explorer does, and it keeps the light
	/// theme pixel-for-pixel what it always was rather than an approximation of it.
	/// </summary>
	public static void DrawButton(in Win32.DRAWITEMSTRUCT item)
	{
		int state = (item.ItemState & Win32.ODS_DISABLED) != 0 ? Win32.PBS_DISABLED
			: (item.ItemState & Win32.ODS_SELECTED) != 0 ? Win32.PBS_PRESSED
			: (item.ItemState & Win32.ODS_HOTLIGHT) != 0 ? Win32.PBS_HOT
			: (item.ItemState & Win32.ODS_FOCUS) != 0 ? Win32.PBS_DEFAULTED
			: Win32.PBS_NORMAL;

		// In the dark theme the buttons are painted here rather than by the renderer: the dark button
		// class the shell uses for its own is not one OpenThemeData will hand out, so asking for it
		// silently returns the light one -- which is the very thing being avoided.
		if (IsDark)
		{
			DrawDarkButton(in item, state);
			return;
		}

		IntPtr theme = Win32.OpenThemeData(item.Item, "Button");

		try
		{
			if (theme != IntPtr.Zero)
				Win32.DrawThemeBackground(theme, item.DeviceContext, Win32.BP_PUSHBUTTON, state, in item.ItemRect, IntPtr.Zero);

			uint text;
			if (theme == IntPtr.Zero || Win32.GetThemeColor(theme, Win32.BP_PUSHBUTTON, state, Win32.TMT_TEXTCOLOR, out text) != 0)
				text = 0x00000000;

			char[] caption = new char[128];
			int length = Win32.GetWindowText(item.Item, caption, caption.Length);

			Win32.SetBkMode(item.DeviceContext, Win32.TRANSPARENT);
			Win32.SetTextColor(item.DeviceContext, text);
			Win32.RECT bounds = item.ItemRect;
			Win32.DrawText(item.DeviceContext, new string(caption, 0, length), length, ref bounds,
				Win32.DT_CENTER | Win32.DT_VCENTER | Win32.DT_SINGLELINE);
		}
		finally
		{
			if (theme != IntPtr.Zero)
				Win32.CloseThemeData(theme);
		}
	}

	/// <summary>
	/// The dark push button, drawn to match Windows 11's own: a rounded rectangle a shade lighter
	/// than the window, a slightly lighter border, and the states the shell shows.
	/// </summary>
	private static void DrawDarkButton(in Win32.DRAWITEMSTRUCT item, int state)
	{
		(uint fill, uint border, uint text) = state switch
		{
			Win32.PBS_PRESSED => (0x00272727u, 0x00373737u, 0x00C8C8C8u),
			Win32.PBS_HOT => (0x00383838u, 0x00474747u, DarkText),
			Win32.PBS_DISABLED => (0x00262626u, 0x00303030u, 0x006A6A6Au),
			Win32.PBS_DEFAULTED => (0x002D2D2Du, 0x00686868u, DarkText),   // the focused one, outlined
			_ => (0x002D2D2Du, 0x003D3D3Du, DarkText),
		};

		IntPtr brush = Win32.CreateSolidBrush(fill);
		IntPtr pen = Win32.CreatePen(0, 1, border);
		IntPtr oldBrush = Win32.SelectObject(item.DeviceContext, brush);
		IntPtr oldPen = Win32.SelectObject(item.DeviceContext, pen);

		Win32.RECT bounds = item.ItemRect;
		int radius = Math.Max(4, (bounds.Bottom - bounds.Top) / 5);
		Win32.RoundRect(item.DeviceContext, bounds.Left, bounds.Top, bounds.Right, bounds.Bottom, radius, radius);

		Win32.SelectObject(item.DeviceContext, oldBrush);
		Win32.SelectObject(item.DeviceContext, oldPen);
		Win32.DeleteObject(brush);
		Win32.DeleteObject(pen);

		char[] caption = new char[128];
		int length = Win32.GetWindowText(item.Item, caption, caption.Length);
		Win32.SetBkMode(item.DeviceContext, Win32.TRANSPARENT);
		Win32.SetTextColor(item.DeviceContext, text);
		Win32.DrawText(item.DeviceContext, new string(caption, 0, length), length, ref bounds,
			Win32.DT_CENTER | Win32.DT_VCENTER | Win32.DT_SINGLELINE);
	}

	private enum PreferredAppMode
	{
		Default = 0,
		AllowDark = 1,
	}

	/// <summary>uxtheme!SetPreferredAppMode, ordinal 135: lets this process use the dark controls.</summary>
	private static void PreferDarkMode(bool dark)
	{
		if (TryGetUxThemeExport(135) is not { } export)
			return;

		((delegate* unmanaged[Stdcall]<int, int>)export)((int)(dark ? PreferredAppMode.AllowDark : PreferredAppMode.Default));
	}

	/// <summary>uxtheme!AllowDarkModeForWindow, ordinal 133: opts one window into them.</summary>
	private static void AllowDarkMode(IntPtr window, bool dark)
	{
		if (TryGetUxThemeExport(133) is not { } export)
			return;

		((delegate* unmanaged[Stdcall]<IntPtr, int, int>)export)(window, dark ? 1 : 0);
	}

	private static IntPtr? TryGetUxThemeExport(int ordinal)
	{
		try
		{
			IntPtr module = Win32.GetModuleHandle("uxtheme.dll");
			if (module == IntPtr.Zero)
				module = Win32.LoadLibrary("uxtheme.dll");

			if (module == IntPtr.Zero)
				return null;

			IntPtr export = Win32.GetProcAddress(module, ordinal);
			return export == IntPtr.Zero ? null : export;
		}
		catch (Exception)
		{
			return null;
		}
	}
}
