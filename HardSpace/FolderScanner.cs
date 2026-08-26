using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Threading;
using System.Threading.Tasks;

namespace HardSpace;

/// <summary>Totals for one scanned folder. All sizes are in bytes.</summary>
internal sealed class ScanResult
{
	public string Root = string.Empty;
	public long ApparentSize;          // what Explorer shows: every link counted in full
	public long UniqueSize;            // each distinct file content counted once
	public long AllocatedSize;         // clusters actually consumed by those distinct contents
	public long FileCount;
	public long DirectoryCount;
	public long HardLinkedFileCount;   // entries whose content has more than one name
	public long HardLinkedUniqueCount; // distinct contents behind those entries
	public long ReparsePointCount;     // symlinks/junctions, not followed and not counted
	public long UnreadableCount;       // entries we could not open for metadata
	public TimeSpan Elapsed;
	public bool Cancelled;
	public List<string> Errors = [];

	/// <summary>Space that would be needed if every hard link were a real copy.</summary>
	public long HardLinkSavings => ApparentSize - UniqueSize;
}

/// <summary>Progress ticks for the UI; cheap enough to raise per file.</summary>
internal readonly record struct ScanProgress(long Files, long Directories, long ApparentSize, string CurrentDirectory);

internal static class FolderScanner
{
	private const int MaxReportedErrors = 20;

	public static ScanResult Scan(string root, IProgress<ScanProgress>? progress, CancellationToken cancellation)
	{
		ScanResult result = new() { Root = root };
		long startTicks = Environment.TickCount64;

		// Contents already counted. Only files with a link count above one can collide, so nothing
		// else is stored here -- on a tree with few hard links this dictionary stays near empty.
		ConcurrentDictionary<FileKey, byte> seenContents = new();
		object errorLock = new();

		long apparent = 0, unique = 0, allocated = 0;
		long files = 0, directories = 0, linkedFiles = 0, linkedUnique = 0, reparsePoints = 0, unreadable = 0;
		long lastReport = 0;
		string currentDirectory = root;

		EnumerationOptions options = new()
		{
			RecurseSubdirectories = true,
			IgnoreInaccessible = true,
			AttributesToSkip = 0,
			ReturnSpecialDirectories = false,
		};

		FileSystemEnumerable<Entry> enumerable = new(
			root,
			(ref FileSystemEntry entry) => new Entry(entry.ToFullPath(), entry.Length, entry.Attributes, entry.IsDirectory),
			options)
		{
			// Never walk into a junction or directory symlink: its content lives elsewhere and would
			// be counted twice (or send us round a loop).
			ShouldRecursePredicate = (ref FileSystemEntry entry) => (entry.Attributes & FileAttributes.ReparsePoint) == 0,
		};

		ParallelOptions parallelOptions = new()
		{
			CancellationToken = cancellation,
			MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount * 2, 32),
		};

		try
		{
			Parallel.ForEach(enumerable, parallelOptions, entry =>
			{
				if (entry.IsDirectory)
				{
					Interlocked.Increment(ref directories);
					if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
						Interlocked.Increment(ref reparsePoints);

					Volatile.Write(ref currentDirectory, entry.FullPath);
					return;
				}

				if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
				{
					// A file symlink is a pointer, not content: its target is counted where it lives.
					Interlocked.Increment(ref reparsePoints);
					return;
				}

				Interlocked.Increment(ref files);
				Interlocked.Add(ref apparent, entry.Length);

				if (!NativeMethods.TryGetFileFacts(entry.FullPath, out FileFacts facts, out int error))
				{
					// Unreadable metadata: fall back to the directory entry's size and assume the file
					// is unique, which matches what Explorer would have reported for it.
					Interlocked.Increment(ref unreadable);
					Interlocked.Add(ref unique, entry.Length);
					Interlocked.Add(ref allocated, entry.Length);
					RecordError(entry.FullPath, error);
				}
				else if (facts.LinkCount <= 1 || !facts.HasId)
				{
					Interlocked.Add(ref unique, facts.Length);
					Interlocked.Add(ref allocated, facts.AllocationSize);
				}
				else
				{
					Interlocked.Increment(ref linkedFiles);
					if (seenContents.TryAdd(facts.Key, 0))
					{
						Interlocked.Increment(ref linkedUnique);
						Interlocked.Add(ref unique, facts.Length);
						Interlocked.Add(ref allocated, facts.AllocationSize);
					}
				}

				if (progress is not null)
				{
					long seen = Interlocked.Read(ref files);
					long previous = Interlocked.Read(ref lastReport);
					if (seen - previous >= 500 && Interlocked.CompareExchange(ref lastReport, seen, previous) == previous)
					{
						progress.Report(new ScanProgress(
							seen,
							Interlocked.Read(ref directories),
							Interlocked.Read(ref apparent),
							Volatile.Read(ref currentDirectory)));
					}
				}
			});
		}
		catch (OperationCanceledException)
		{
			result.Cancelled = true;
		}

		result.ApparentSize = apparent;
		result.UniqueSize = unique;
		result.AllocatedSize = allocated;
		result.FileCount = files;
		result.DirectoryCount = directories;
		result.HardLinkedFileCount = linkedFiles;
		result.HardLinkedUniqueCount = linkedUnique;
		result.ReparsePointCount = reparsePoints;
		result.UnreadableCount = unreadable;
		result.Elapsed = TimeSpan.FromMilliseconds(Environment.TickCount64 - startTicks);
		return result;

		void RecordError(string path, int error)
		{
			lock (errorLock)
			{
				if (result.Errors.Count < MaxReportedErrors)
					result.Errors.Add($"[{error}] {path}");
			}
		}
	}

	private readonly record struct Entry(string FullPath, long Length, FileAttributes Attributes, bool IsDirectory);
}
