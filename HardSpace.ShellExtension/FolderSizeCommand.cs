using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace HardSpace.ShellExtension;

/// <summary>
/// <c>IExplorerCommand</c>, in vtable order. Every method is <see cref="PreserveSigAttribute"/> and
/// takes raw pointers: the shell types on the other side are consumed through their vtables in
/// <see cref="Shell"/>, which keeps this DLL free of any marshalling that AOT would have to reason
/// about.
/// </summary>
[GeneratedComInterface]
[Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9")]
internal partial interface IExplorerCommand
{
	[PreserveSig] int GetTitle(nint items, out nint title);

	[PreserveSig] int GetIcon(nint items, out nint icon);

	[PreserveSig] int GetToolTip(nint items, out nint toolTip);

	[PreserveSig] int GetCanonicalName(out Guid name);

	[PreserveSig] int GetState(nint items, [MarshalAs(UnmanagedType.Bool)] bool okToBeSlow, out uint state);

	[PreserveSig] int Invoke(nint items, nint bindContext);

	[PreserveSig] int GetFlags(out uint flags);

	[PreserveSig] int EnumSubCommands(out nint commands);
}

[GeneratedComClass]
internal sealed partial class FolderSizeCommand : IExplorerCommand
{
	private const string Title = "Folder size (hard-link aware)";

	private const int S_OK = 0;
	private const int E_FAIL = unchecked((int)0x80004005);
	private const int E_NOTIMPL = unchecked((int)0x80004001);

	private const uint ECS_ENABLED = 0;
	private const uint ECS_HIDDEN = 8;
	private const uint ECF_DEFAULT = 0;

	public int GetTitle(nint items, out nint title)
	{
		title = 0;
		try
		{
			// The shell frees this with CoTaskMemFree, which is exactly what this allocator uses.
			title = Marshal.StringToCoTaskMemUni(Title);
			return S_OK;
		}
		catch
		{
			return E_FAIL;
		}
	}

	public int GetIcon(nint items, out nint icon)
	{
		icon = 0;
		try
		{
			string? executable = Shell.FindExecutable();
			if (executable is null)
				return E_NOTIMPL;

			// "<path>,0": the first icon of the tool itself.
			icon = Marshal.StringToCoTaskMemUni(executable + ",0");
			return S_OK;
		}
		catch
		{
			return E_NOTIMPL;
		}
	}

	public int GetToolTip(nint items, out nint toolTip)
	{
		toolTip = 0;
		return E_NOTIMPL;
	}

	public int GetCanonicalName(out Guid name)
	{
		name = Guid.Empty;
		return S_OK;
	}

	public int GetState(nint items, bool okToBeSlow, out uint state)
	{
		// The package manifest already restricts the verb to folders and drives, so anything that
		// reaches here is eligible -- unless the tool is missing next to this DLL.
		state = Shell.FindExecutable() is null ? ECS_HIDDEN : ECS_ENABLED;
		return S_OK;
	}

	public int Invoke(nint items, nint bindContext)
	{
		try
		{
			string? executable = Shell.FindExecutable();
			if (executable is null)
				return E_FAIL;

			string? folder = Shell.GetFirstFileSystemPath(items);
			if (folder is null)
				return E_FAIL;

			return Shell.Launch(executable, folder) ? S_OK : E_FAIL;
		}
		catch
		{
			return E_FAIL;
		}
	}

	public int GetFlags(out uint flags)
	{
		flags = ECF_DEFAULT;
		return S_OK;
	}

	public int EnumSubCommands(out nint commands)
	{
		commands = 0;
		return E_NOTIMPL;
	}
}
