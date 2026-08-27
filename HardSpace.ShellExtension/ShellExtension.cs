using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace HardSpace.ShellExtension;

/// <summary>
/// The in-process COM server behind the Windows 11 short context menu. Windows 11 only shows a verb
/// in the top-level menu when it comes from an <c>IExplorerCommand</c> declared by an MSIX package,
/// so this DLL is loaded into Explorer's surrogate and asked for the item's title, state and click.
/// </summary>
/// <remarks>
/// It is compiled with NativeAOT: Explorer loads this into its own process, so a managed runtime
/// start-up per right-click is exactly what must be avoided. Every entry point is therefore either
/// an unmanaged export or a source-generated COM vtable, and nothing may throw across the boundary.
/// </remarks>
internal static unsafe class Exports
{
	// Stable identity of the verb. It appears in the package manifest and in nothing else, so it may
	// never change once a package has shipped.
	internal static readonly Guid CommandClsid = new("6D8C3B1A-9E2F-4B7C-8A15-3F0D5C7E9A42");

	// One set of wrappers for the whole DLL: object identity across calls is what lets Explorer's
	// reference counting see a single COM object per managed instance.
	internal static readonly StrategyBasedComWrappers Wrappers = new();

	private const int S_OK = 0;
	private const int S_FALSE = 1;
	private const int E_NOINTERFACE = unchecked((int)0x80004002);
	private const int CLASS_E_CLASSNOTAVAILABLE = unchecked((int)0x80040111);

	[UnmanagedCallersOnly(EntryPoint = "DllGetClassObject")]
	public static int DllGetClassObject(Guid* rclsid, Guid* riid, void** ppv)
	{
		if (ppv is null || riid is null)
			return unchecked((int)0x80004003); // E_POINTER

		*ppv = null;

		try
		{
			if (rclsid is null || *rclsid != CommandClsid)
				return CLASS_E_CLASSNOTAVAILABLE;

			nint unknown = Wrappers.GetOrCreateComInterfaceForObject(new ClassFactory(), CreateComInterfaceFlags.None);
			try
			{
				return Marshal.QueryInterface(unknown, *riid, out nint requested) is not S_OK
					? E_NOINTERFACE
					: Assign(ppv, requested);
			}
			finally
			{
				Marshal.Release(unknown);
			}
		}
		catch
		{
			return unchecked((int)0x80004005); // E_FAIL
		}

		static int Assign(void** target, nint value)
		{
			*target = (void*)value;
			return S_OK;
		}
	}

	/// <summary>
	/// Explorer's surrogate keeps the DLL loaded for as long as it likes; refusing to unload keeps
	/// the AOT runtime from ever being torn down and re-initialised underneath a live command.
	/// </summary>
	[UnmanagedCallersOnly(EntryPoint = "DllCanUnloadNow")]
	public static int DllCanUnloadNow() => S_FALSE;
}

[GeneratedComInterface]
[Guid("00000001-0000-0000-C000-000000000046")]
internal partial interface IClassFactory
{
	[PreserveSig]
	int CreateInstance(nint outer, in Guid riid, out nint instance);

	[PreserveSig]
	int LockServer([MarshalAs(UnmanagedType.Bool)] bool @lock);
}

[GeneratedComClass]
internal sealed partial class ClassFactory : IClassFactory
{
	private const int S_OK = 0;
	private const int E_NOINTERFACE = unchecked((int)0x80004002);
	private const int CLASS_E_NOAGGREGATION = unchecked((int)0x80040110);

	public int CreateInstance(nint outer, in Guid riid, out nint instance)
	{
		instance = 0;
		if (outer != 0)
			return CLASS_E_NOAGGREGATION;

		try
		{
			nint unknown = Exports.Wrappers.GetOrCreateComInterfaceForObject(new FolderSizeCommand(), CreateComInterfaceFlags.None);
			try
			{
				if (Marshal.QueryInterface(unknown, riid, out nint requested) is not S_OK)
					return E_NOINTERFACE;

				instance = requested;
				return S_OK;
			}
			finally
			{
				Marshal.Release(unknown);
			}
		}
		catch
		{
			return unchecked((int)0x80004005); // E_FAIL
		}
	}

	public int LockServer(bool @lock) => S_OK;
}
