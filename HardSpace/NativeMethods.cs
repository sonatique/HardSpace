using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HardSpace;

/// <summary>
/// The Win32 surface needed to tell hard links apart: two names pointing at the same file share the
/// same (volume serial, file id) pair, and the file's link count says whether it is worth checking.
/// </summary>
internal static partial class NativeMethods
{
	private const uint FILE_READ_ATTRIBUTES = 0x0080;
	private const uint FILE_SHARE_READ = 0x0001;
	private const uint FILE_SHARE_WRITE = 0x0002;
	private const uint FILE_SHARE_DELETE = 0x0004;
	private const uint OPEN_EXISTING = 3;
	private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
	private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;

	private const int FileStandardInfo = 1;
	private const int FileIdInfo = 18;

	[StructLayout(LayoutKind.Sequential)]
	private struct FILE_STANDARD_INFO
	{
		public long AllocationSize;
		public long EndOfFile;
		public uint NumberOfLinks;
		public byte DeletePending;
		public byte Directory;
	}

	// FILE_ID_INFO: ULONGLONG VolumeSerialNumber + FILE_ID_128 (16 raw bytes, split here into two
	// ulongs purely as a comparable key -- the bytes are opaque, only equality matters).
	[StructLayout(LayoutKind.Sequential)]
	private struct FILE_ID_INFO
	{
		public ulong VolumeSerialNumber;
		public ulong FileIdLow;
		public ulong FileIdHigh;
	}

	[LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
	private static partial SafeFileHandle CreateFile(
		string lpFileName,
		uint dwDesiredAccess,
		uint dwShareMode,
		IntPtr lpSecurityAttributes,
		uint dwCreationDisposition,
		uint dwFlagsAndAttributes,
		IntPtr hTemplateFile);

	[LibraryImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool GetFileInformationByHandleEx(
		SafeFileHandle hFile,
		int fileInformationClass,
		IntPtr lpFileInformation,
		uint dwBufferSize);

	/// <summary>
	/// Opens <paramref name="path"/> for metadata only and reads its identity, allocated size and
	/// link count. Returns false (with <paramref name="error"/> set) when the file cannot be opened,
	/// which is common for files locked with no sharing at all or denied by ACL.
	/// </summary>
	internal static bool TryGetFileFacts(string path, out FileFacts facts, out int error)
	{
		facts = default;
		error = 0;

		// FILE_READ_ATTRIBUTES with full sharing is the least intrusive open there is; the reparse
		// flag keeps a symlink from silently resolving to its target on another volume.
		using SafeFileHandle handle = CreateFile(
			path,
			FILE_READ_ATTRIBUTES,
			FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
			IntPtr.Zero,
			OPEN_EXISTING,
			FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
			IntPtr.Zero);

		if (handle.IsInvalid)
		{
			error = Marshal.GetLastWin32Error();
			return false;
		}

		FILE_STANDARD_INFO standardInfo = default;
		FILE_ID_INFO idInfo = default;

		unsafe
		{
			if (!GetFileInformationByHandleEx(handle, FileStandardInfo, (IntPtr)(&standardInfo), (uint)sizeof(FILE_STANDARD_INFO)))
			{
				error = Marshal.GetLastWin32Error();
				return false;
			}

			// FileIdInfo is unsupported on some remote/legacy file systems; the caller then treats
			// the file as unique, which is the same answer NTFS gives for a link count of 1.
			if (!GetFileInformationByHandleEx(handle, FileIdInfo, (IntPtr)(&idInfo), (uint)sizeof(FILE_ID_INFO)))
			{
				error = Marshal.GetLastWin32Error();
				facts = new FileFacts(standardInfo.EndOfFile, standardInfo.AllocationSize, standardInfo.NumberOfLinks, default, HasId: false);
				return true;
			}
		}

		FileKey key = new(idInfo.VolumeSerialNumber, idInfo.FileIdLow, idInfo.FileIdHigh);
		facts = new FileFacts(standardInfo.EndOfFile, standardInfo.AllocationSize, standardInfo.NumberOfLinks, key, HasId: true);
		return true;
	}
}

/// <summary>Identity of a file's content: every hard link to it reports this same value.</summary>
internal readonly record struct FileKey(ulong VolumeSerialNumber, ulong FileIdLow, ulong FileIdHigh);

/// <summary>What one directory entry contributes, before de-duplication.</summary>
internal readonly record struct FileFacts(long Length, long AllocationSize, uint LinkCount, FileKey Key, bool HasId);
