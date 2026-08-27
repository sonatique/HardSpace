using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;

namespace HardSpace;

/// <summary>How much of the machine an install is allowed to touch.</summary>
internal enum Scope
{
	/// <summary>Whatever this prompt can manage: machine-wide when elevated, the user otherwise.</summary>
	Best,
	CurrentUser,
	Machine,
}

internal sealed class InstallOptions
{
	public Scope Scope = Scope.Best;
	public bool? ShortMenu;          // null: yes if this executable carries it and the prompt is elevated
	public string? Directory;
	public bool Quiet;               // do not ask about Explorer, and do not restart it
	public bool RestartExplorer;     // restart it without asking
	public string[] Arguments = [];  // what this process was given, to hand to an elevated retry
}

/// <summary>
/// Installing HardSpace: copying the executable somewhere permanent and registering it with Explorer.
/// </summary>
/// <remarks>
/// Whoever runs this cannot be expected to know which of three registrations their Explorer honours,
/// so nothing here asks them. It works out the best install this machine and this prompt allow, does
/// that, and says what it decided. The pieces for the Windows 11 short menu -- the shell extension
/// and the package that declares it -- travel inside this executable, so all of it is one file.
/// </remarks>
internal static class Installer
{
	private const string ExecutableName = "HardSpace.exe";
	private const string ExtensionName = "HardSpace.ShellExtension.dll";
	private const string PackageName = "HardSpace.msix";

	private const string ExtensionResource = "HardSpace.Embedded." + ExtensionName;
	private const string PackageResource = "HardSpace.Embedded." + PackageName;

	public static int Install(InstallOptions options)
	{
		bool elevated = IsElevated();
		bool carriesPackage = HasResource(ExtensionResource) && HasResource(PackageResource);
		bool shortMenu = options.ShortMenu ?? (elevated && carriesPackage);
		bool machineWide = shortMenu || options.Scope switch
		{
			Scope.Machine => true,
			Scope.CurrentUser => false,
			_ => elevated,
		};

		// A plain --install from an ordinary prompt can do better than it is about to, and only the
		// reader can decide whether that is worth a UAC prompt. Ask rather than quietly settling.
		if (!elevated && carriesPackage && options.ShortMenu is null && options.Scope == Scope.Best && !options.Quiet)
		{
			switch (Ui.AskWithCancel(
				"Putting HardSpace in Windows 11\u0027s default right-click menu -- the one that opens "
				+ "first -- needs an elevated prompt, and installs it for every user of this machine."
				+ "\r\n\r\nWithout that it installs for you alone, and its entry lives in the "
				+ "\"Show more options\" menu (Shift+F10, or Shift+right-click)."
				+ "\r\n\r\nYes\trestart elevated, and install into the default menu"
				+ "\r\nNo\tcarry on without it"
				+ "\r\nCancel\tinstall nothing"))
			{
				case Answer.Yes:
					int code = RelaunchElevated(options);
					if (code >= 0)
						return code;

					Ui.Tell("Elevation was declined, so HardSpace is installing for you alone; its entry "
						+ "will be under \"Show more options\".");
					break;

				case Answer.Cancel:
					Ui.Tell("Nothing was installed.");
					return 1;
			}
		}

		if (machineWide && !elevated)
		{
			Ui.Tell("Installing for every user writes to HKEY_LOCAL_MACHINE and to Program Files, which "
				+ "needs administrator rights. Re-run this from an elevated prompt, or pass --user to "
				+ "install for yourself only.");
			return 2;
		}

		if (shortMenu && !carriesPackage)
		{
			Ui.Tell("This build carries no short-menu package. Rebuild with Build.ps1 -ShortMenu, or drop "
				+ "--short-menu to register the classic entry only.");
			return 2;
		}

		string directory = options.Directory ?? DefaultDirectory(machineWide);
		StringBuilder report = new();
		report.AppendLine(machineWide ? "Installing for every user of this machine." : "Installing for the current user.");
		report.AppendLine($"Folder: {directory}");

		Directory.CreateDirectory(directory);
		string executable = Path.Combine(directory, ExecutableName);
		string running = Environment.ProcessPath ?? executable;
		if (!string.Equals(running, executable, StringComparison.OrdinalIgnoreCase))
			File.Copy(running, executable, overwrite: true);

		if (shortMenu)
			report.AppendLine(InstallShortMenu(directory, executable));
		else if (carriesPackage)
			report.AppendLine("Short menu: skipped. It needs an elevated prompt, and Explorer's own menu is unaffected.");
		else
			report.AppendLine("Short menu: not in this build; the entry lives under \"Show more options\".");

		report.AppendLine(ShellIntegration.Register(machineWide, executable));
		Ui.Tell(report.ToString().TrimEnd());

		OfferExplorerRestart(options);
		return 0;
	}

	public static int Uninstall(InstallOptions options)
	{
		bool elevated = IsElevated();
		bool machineWide = options.Scope != Scope.CurrentUser && elevated;

		StringBuilder report = new();
		if (elevated)
			report.AppendLine(RemovePackage());

		report.AppendLine(ShellIntegration.Unregister(machineWide));

		// Whatever was installed, leave nothing of it behind but the executable that is running now,
		// which Windows will not let us delete from under ourselves.
		string directory = options.Directory ?? DefaultDirectory(machineWide);
		Delete(Path.Combine(directory, ExtensionName));

		string executable = Path.Combine(directory, ExecutableName);
		string running = Environment.ProcessPath ?? string.Empty;
		if (File.Exists(executable) && !string.Equals(running, executable, StringComparison.OrdinalIgnoreCase))
		{
			Delete(executable);
			TryRemoveEmptyDirectory(directory);
		}
		else if (File.Exists(executable))
		{
			report.AppendLine($"Delete {executable} once this has exited; it is the file running now.");
		}

		Ui.Tell(report.ToString().TrimEnd());
		OfferExplorerRestart(options);
		return 0;
	}

	/// <summary>
	/// Runs this same command again, elevated, and waits for it. Returns its exit code, or -1 when
	/// the elevation prompt was refused -- which is an answer, not a failure, and leaves the caller
	/// free to carry on with the lesser install.
	/// </summary>
	private static int RelaunchElevated(InstallOptions options)
	{
		ProcessStartInfo start = new(Environment.ProcessPath ?? ExecutableName)
		{
			UseShellExecute = true,   // required for the runas verb, which is what raises the UAC prompt
			Verb = "runas",
		};
		foreach (string argument in options.Arguments)
			start.ArgumentList.Add(argument);

		try
		{
			using Process? elevated = Process.Start(start);
			if (elevated is null)
				return -1;

			elevated.WaitForExit();
			return elevated.ExitCode;
		}
		catch (Win32Exception)
		{
			return -1;
		}
	}

	/// <summary>
	/// Writes out the shell extension and the package, tells the machine to trust whoever signed the
	/// package, and registers it against the folder the binaries are in.
	/// </summary>
	private static string InstallShortMenu(string directory, string executable)
	{
		Write(ExtensionResource, Path.Combine(directory, ExtensionName));

		string package = Path.Combine(Path.GetTempPath(), PackageName);
		Write(PackageResource, package);
		try
		{
			string trusted = TrustPackageSigner(package);

			// Any registration already there has to go first. A package remembers the folder it was
			// installed against, and Windows will not move it to another one: installing over it
			// fails with 0x80073D0B, "already installed with a different external location", which
			// is exactly what a second install into a different folder is.
			RunPowerShell("Get-AppxPackage *HardSpace* | Remove-AppxPackage");

			string added = RunPowerShell($"Add-AppxPackage -Path '{package}' -ExternalLocation '{directory}'");
			return added.Length > 0
				? $"Short menu: FAILED. {added}"
				: $"Short menu: installed. {trusted}";
		}
		finally
		{
			Delete(package);
		}
	}

	/// <summary>
	/// An MSIX signature carries its own signer, in AppxSignature.p7x -- four magic bytes, then PKCS#7 --
	/// so there is no certificate file to ship beside the package, or to pair with the wrong one.
	/// </summary>
	private static string TrustPackageSigner(string package)
	{
		using ZipArchive archive = ZipFile.OpenRead(package);
		ZipArchiveEntry entry = archive.GetEntry("AppxSignature.p7x")
			?? throw new InvalidOperationException("The package is not signed.");

		byte[] blob;
		using (Stream stream = entry.Open())
		using (MemoryStream buffer = new())
		{
			stream.CopyTo(buffer);
			blob = buffer.ToArray();
		}

		SignedCms signature = new();
		signature.Decode(blob.AsSpan(4).ToArray());   // past the 'PKCX' magic
		X509Certificate2 signer = signature.SignerInfos[0].Certificate
			?? throw new InvalidOperationException("The package signature carries no certificate.");

		using X509Store store = new(StoreName.TrustedPeople, StoreLocation.LocalMachine);
		store.Open(OpenFlags.ReadWrite);
		if (store.Certificates.Find(X509FindType.FindByThumbprint, signer.Thumbprint, validOnly: false).Count > 0)
			return $"Signer {signer.Subject} was already trusted.";

		store.Add(signer);
		return $"Signer {signer.Subject} is now trusted by this machine.";
	}

	private static string RemovePackage()
	{
		string error = RunPowerShell("Get-AppxPackage *HardSpace* | Remove-AppxPackage");
		return error.Length > 0 ? $"Package: not removed. {error}" : "Package: removed.";
	}

	/// <summary>
	/// Package registration is a WinRT API with no plain Win32 face, so it goes through PowerShell.
	/// Returns the error output, or an empty string when it worked.
	/// </summary>
	private static string RunPowerShell(string command)
	{
		ProcessStartInfo start = new("powershell.exe")
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardError = true,
			RedirectStandardOutput = true,
		};
		start.ArgumentList.Add("-NoProfile");
		start.ArgumentList.Add("-NonInteractive");
		start.ArgumentList.Add("-Command");
		start.ArgumentList.Add(command);

		using Process? process = Process.Start(start);
		if (process is null)
			return "powershell.exe could not be started.";

		string error = process.StandardError.ReadToEnd();
		process.StandardOutput.ReadToEnd();
		process.WaitForExit();
		return process.ExitCode == 0 ? string.Empty : error.Trim();
	}

	private const string HowToRestart =
		"Explorer has not been restarted, and reads context-menu entries only when it starts, so "
		+ "the entry may not appear yet. Any of these does it:\r\n"
		+ "  - Task Manager (Ctrl+Shift+Esc), right-click \"Windows Explorer\", Restart\r\n"
		+ "  - HardSpace.exe --install --restart-explorer\r\n"
		+ "  - signing out and back in, or a reboot";

	private static void OfferExplorerRestart(InstallOptions options)
	{
		if (options.Quiet)
		{
			Ui.Tell(HowToRestart);
			return;
		}

		if (!options.RestartExplorer && !Ui.Ask("Explorer only reads context-menu entries when it starts, so the entry may not appear "
			+ "until it restarts. Restarting closes your open File Explorer windows.\r\n\r\nRestart Explorer now?"))
		{
			Ui.Tell(HowToRestart);
			return;
		}

		foreach (Process explorer in Process.GetProcessesByName("explorer"))
		{
			try
			{
				explorer.Kill();
			}
			catch (Exception)
			{
				// Another session's Explorer, or one already gone: neither is ours to worry about.
			}
			finally
			{
				explorer.Dispose();
			}
		}
	}

	private static string DefaultDirectory(bool machineWide) => Path.Combine(
		Environment.GetFolderPath(machineWide ? Environment.SpecialFolder.ProgramFiles : Environment.SpecialFolder.LocalApplicationData),
		machineWide ? "HardSpace" : @"Programs\HardSpace");

	private static bool IsElevated()
	{
		using WindowsIdentity identity = WindowsIdentity.GetCurrent();
		return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
	}

	private static bool HasResource(string name)
	{
		using Stream? stream = typeof(Installer).Assembly.GetManifestResourceStream(name);
		return stream is not null;
	}

	private static void Write(string resource, string path)
	{
		using Stream source = typeof(Installer).Assembly.GetManifestResourceStream(resource)
			?? throw new InvalidOperationException($"{resource} is not in this build.");
		using FileStream target = File.Create(path);
		source.CopyTo(target);
	}

	private static void Delete(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch (Exception)
		{
			// Locked or already gone; the caller reports what it managed, not what it wished for.
		}
	}

	private static void TryRemoveEmptyDirectory(string path)
	{
		try
		{
			if (Directory.Exists(path) && Directory.GetFileSystemEntries(path).Length == 0)
				Directory.Delete(path);
		}
		catch (Exception)
		{
		}
	}
}
