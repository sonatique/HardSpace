using System;
using System.Globalization;
using System.Text;

namespace HardSpace;

internal static class Report
{
	private static readonly string[] Units = ["bytes", "KB", "MB", "GB", "TB", "PB"];

	/// <summary>Formats a byte count the way Explorer does (binary units), plus the exact value.</summary>
	public static string Bytes(long value)
	{
		string exact = value.ToString("N0", CultureInfo.CurrentCulture) + " bytes";
		if (value < 1024)
			return exact;

		double scaled = value;
		int unit = 0;
		while (scaled >= 1024 && unit < Units.Length - 1)
		{
			scaled /= 1024;
			unit++;
		}

		return string.Create(CultureInfo.CurrentCulture, $"{scaled:0.##} {Units[unit]} ({exact})");
	}

	public static string Count(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

	public static string Build(ScanResult result)
	{
		StringBuilder text = new();
		text.AppendLine(result.Root);
		if (result.Cancelled)
			text.AppendLine("*** Cancelled -- the figures below cover only what was scanned. ***");
		text.AppendLine();

		text.AppendLine($"Size (as Explorer counts it) : {Bytes(result.ApparentSize)}");
		text.AppendLine($"Actual content size          : {Bytes(result.UniqueSize)}");
		text.AppendLine($"Space used on disk           : {Bytes(result.AllocatedSize)}");
		text.AppendLine();

		if (result.HardLinkedFileCount > 0)
		{
			text.AppendLine($"Hard links                   : {Count(result.HardLinkedFileCount)} names sharing {Count(result.HardLinkedUniqueCount)} files");
			text.AppendLine($"Saved by hard links          : {Bytes(result.HardLinkSavings)}");
		}
		else
		{
			text.AppendLine("Hard links                   : none found");
		}

		text.AppendLine();
		text.AppendLine($"Files                        : {Count(result.FileCount)}");
		text.AppendLine($"Folders                      : {Count(result.DirectoryCount)}");
		if (result.ReparsePointCount > 0)
			text.AppendLine($"Symlinks / junctions         : {Count(result.ReparsePointCount)} (not followed, not counted)");
		if (result.UnreadableCount > 0)
			text.AppendLine($"Unreadable entries           : {Count(result.UnreadableCount)} (counted as if unique)");
		text.AppendLine($"Scan time                    : {result.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture)} s");

		if (result.Errors.Count > 0)
		{
			text.AppendLine();
			text.AppendLine("First errors:");
			foreach (string error in result.Errors)
				text.AppendLine("  " + error);
		}

		return text.ToString();
	}
}
