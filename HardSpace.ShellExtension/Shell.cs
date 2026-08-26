using System;
using System.IO;
using System.Runtime.InteropServices;

namespace HardSpace.ShellExtension;

/// <summary>
/// The shell side of the extension: reading the clicked folder out of an <c>IShellItemArray</c>,
/// finding the tool next to this DLL, and starting it.
/// </summary>
internal static unsafe partial class Shell
{
	private const int S_OK = 0;
	private const uint SIGDN_FILESYSPATH = 0x80058000;
	private const int SW_SHOWNORMAL = 1;

	// GetModuleHandleEx flags: resolve the module from an address inside it, without taking a
	// reference (this DLL is never unloaded, see DllCanUnloadNow).
	private const uint GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS = 0x00000004;
	private const uint GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT = 0x00000002;

	private static string? _executable;
	private static bool _executableResolved;

	/// <summary>
	/// The path of HardSpace.exe, which the package installs next to this DLL. Resolved once: the
	/// answer cannot change while Explorer holds the DLL loaded.
	/// </summary>
	internal static string? FindExecutable()
	{
		if (_executableResolved)
			return _executable;

		_executableResolved = true;
		try
		{
			string? directory = Path.GetDirectoryName(GetOwnModulePath());
			if (directory is null)
				return _executable = null;

			string candidate = Path.Combine(directory, "HardSpace.exe");
			return _executable = File.Exists(candidate) ? candidate : null;
		}
		catch
		{
			return _executable = null;
		}
	}

	/// <summary>The file-system path of the first item in the array, or null if there is none.</summary>
	internal static string? GetFirstFileSystemPath(nint itemArray)
	{
		if (itemArray == 0)
			return null;

		// IShellItemArray vtable: 0-2 IUnknown, 3 BindToHandler, 4 GetPropertyStore,
		// 5 GetPropertyDescriptionList, 6 GetAttributes, 7 GetCount, 8 GetItemAt, 9 EnumItems.
		void** array = *(void***)itemArray;

		uint count;
		if (((delegate* unmanaged[Stdcall]<nint, uint*, int>)array[7])(itemArray, &count) is not S_OK || count == 0)
			return null;

		nint item;
		if (((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)array[8])(itemArray, 0, &item) is not S_OK || item == 0)
			return null;

		try
		{
			// IShellItem vtable: 0-2 IUnknown, 3 BindToHandler, 4 GetParent, 5 GetDisplayName,
			// 6 GetAttributes, 7 Compare.
			void** shellItem = *(void***)item;

			nint name;
			if (((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)shellItem[5])(item, SIGDN_FILESYSPATH, &name) is not S_OK || name == 0)
				return null;

			try
			{
				return Marshal.PtrToStringUni(name);
			}
			finally
			{
				Marshal.FreeCoTaskMem(name);
			}
		}
		finally
		{
			// IUnknown::Release
			((delegate* unmanaged[Stdcall]<nint, uint>)(*(void***)item)[2])(item);
		}
	}

	/// <summary>
	/// Starts the tool on <paramref name="folder"/>. ShellExecute rather than CreateProcess so the
	/// new process does not inherit Explorer's handles or its working directory.
	/// </summary>
	internal static bool Launch(string executable, string folder)
		=> ShellExecute(IntPtr.Zero, "open", executable, "\"" + folder + "\"", null, SW_SHOWNORMAL) > 32;

	private static string GetOwnModulePath()
	{
		// The anchor must be an address the loader can attribute to this image, so it is the code of
		// one of our own exports -- a string literal would not do, as AOT keeps those on the heap.
		IntPtr anchor = (IntPtr)(delegate* unmanaged<int>)&Exports.DllCanUnloadNow;
		if (!GetModuleHandleEx(
				GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
				anchor,
				out IntPtr module))
		{
			return string.Empty;
		}

		char* buffer = stackalloc char[1024];
		uint length = GetModuleFileName(module, buffer, 1024);
		return length == 0 ? string.Empty : new string(buffer, 0, (int)length);
	}

	[LibraryImport("shell32.dll", EntryPoint = "ShellExecuteW", StringMarshalling = StringMarshalling.Utf16)]
	private static partial nint ShellExecute(IntPtr hwnd, string? verb, string file, string? parameters, string? directory, int showCommand);

	[LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleExW", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool GetModuleHandleEx(uint flags, IntPtr address, out IntPtr module);

	[LibraryImport("kernel32.dll", EntryPoint = "GetModuleFileNameW", SetLastError = true)]
	private static partial uint GetModuleFileName(IntPtr module, char* fileName, uint size);
}
