# HardSpace

Folder size that does not double-count hard links.

Explorer (and `Get-ChildItem | Measure-Object`) adds up directory entries, so a build tree where
MSBuild hard-linked the same assembly into twenty output folders is reported twenty times over.
HardSpace reports both numbers side by side:

```
C:\src\SomeSolution

Explorer size        : 21.9 GB (23'513'177'998 bytes)
Actual content size  : 21.63 GB (23'226'728'150 bytes)
Space used on disk   : 21.68 GB (23'280'696'200 bytes)

Hard links           : 252 names sharing 126 files
Saved by hard links  : 273.18 MB (286'449'848 bytes)

Files                : 34'196
Folders              : 3'477
Scan time            : 0.7 s
```

- **Explorer size** — every directory entry counted in full, so it matches Explorer.
- **Actual content size** — each distinct file counted once, no matter how many names point at it.
- **Space used on disk** — the clusters those distinct files actually occupy (so NTFS compression
  and sparse files show up here).

## Build

```
dotnet build HardSpace.sln -c Release
```

Requires the .NET 10 SDK, plus the "Desktop development with C++" workload (NativeAOT links with
the MSVC linker).

### Deploy

```
.\Build.ps1
```

Publishes and leaves two equivalent things: `deploy\`, to install straight from a build, and
`HardSpace.zip`, which is the same folder as one file to send someone.

Add `-ShortMenu` to also build the pieces for Windows 11's short menu -- the shell-extension DLL and
a signed sparse package. See below for what that buys and what it costs.

### Publish (NativeAOT)

The tool is a fresh process on every right-click, so start-up time is paid on every single use.
`PublishAot` is therefore on: a publish with a runtime identifier produces one native executable
with no runtime to find and nothing to JIT.

```
dotnet publish HardSpace\HardSpace.csproj -c Release -r win-x64 -o publish
```

Measured on this machine, launch to visible window (8 runs, average / best):

| Publish | Size | Startup |
| --- | --- | --- |
| **NativeAOT** | **1.9 MB (one file)** | **50 / 36 ms** |
| self-contained, composite ReadyToRun | 130 MB | 136 / 126 ms |
| self-contained, no R2R | 118 MB | 295 / 172 ms |
| framework-dependent, R2R | 268 KB | 154 / 143 ms |
| framework-dependent, no R2R | 220 KB | 155 / 142 ms |

Passing `-p:PublishAot=false` falls back to a ReadyToRun publish, which is worth it self-contained
(composite R2R folds the framework into one image) and measures as nothing framework-dependent: the
app assembly is a few tens of kilobytes and everything start-up touches -- host, CLR, window
toolkit -- already lives precompiled in the shared runtime.

A plain `dotnet build` ignores all of this: it needs an explicit RID and a publish.

The window is written directly against user32 rather than WinForms. That is what makes AOT possible
at all: `PublishTrimmed` on a WinForms project fails with NETSDK1175, and NativeAOT implies
trimming.

## Install

```
.\Install.ps1
```

One command, whatever the machine. It works out the best install available and says what it chose:

| Run as | What you get |
| --- | --- |
| yourself | `%LOCALAPPDATA%\Programs\HardSpace`, entry for you only |
| administrator | `%ProgramFiles%\HardSpace`, entry for every user -- and the Windows 11 short-menu package too, if the folder was built with `-ShortMenu` |

Elevated is the one to prefer: some machines ignore per-user verbs entirely, and only an elevated
install can reach Windows 11's short menu. Neither needs a runtime installed -- the executable is
self-contained.

`-Machine:$false` and `-ShortMenu:$false` override the decision; `-Machine` and `-ShortMenu` demand
it and fail loudly if the prompt is not elevated.

Explorer reads context-menu entries when it starts, so the new one may not appear until it
restarts. The script explains that and asks; answering no prints the ways to do it later. `-RestartExplorer`
answers yes up front, `-KeepExplorer` neither asks nor restarts, which is also what happens when
there is no console to ask on.

If the entry does not appear -- or appears for a split second and then vanishes -- the machine
ignores per-user shell verbs. A machine forcing the Windows 11 classic context menu was observed
doing exactly that, and dropping a plain `notepad.exe` verb the same way, so it is worth testing
with one before blaming this tool. The cure is to register for the machine instead, from an
**elevated** prompt:

```
.\Install.ps1 -Machine
```

which installs to `%ProgramFiles%\HardSpace` and writes the same three keys under `HKLM`.

To remove, with `-Machine` if that is how it went in:

```
.\Install.ps1 -Uninstall
```

### Installing on someone else's machine

Run `.\Build.ps1` and send them `HardSpace.zip`. They unzip it anywhere and run `Install.ps1`. If the executable arrives
by mail or chat it carries a mark of the web and SmartScreen stops it on first run; the script
clears that with `Unblock-File`, but they will still see the prompt if they run the executable
before the script. Copying through a network share or a USB stick avoids the mark entirely.
Signing would not help: the certificate would have to be one their machine already trusts.

### What it registers

Three keys under `Software\Classes`, in `HKEY_CURRENT_USER` or `HKEY_LOCAL_MACHINE` depending on the
scope: `Directory\shell` for a folder, `Directory\Background\shell` for the empty space inside an
open folder, and `Drive\shell` for a drive. On Windows 11 the entry appears under **Show more
options** (Shift+F10) unless the machine has been set to use the classic menu, where it is in the
menu proper.

### The short menu (stock Windows 11)

On a machine with Windows 11's default context menu, a registry verb -- which is all `--register`
can write, in either hive -- always lands under **Show more options**. The short menu that opens
first accepts only an `IExplorerCommand` served by a COM server that an MSIX package declares. There
is no third way.

```
.\Build.ps1 -ShortMenu
```

adds `HardSpace.ShellExtension.dll` and `HardSpace.msix` to the handover, and on the target
machine, from an elevated prompt:

```
.\Install.ps1 -ShortMenu
```

installs the payload machine-wide, tells the machine to trust the package's certificate, and
registers the package against that folder. It registers the classic verb as well, which is what
covers drives -- the package manifest cannot, its schema accepting only `*`, `.<extension>`,
`Directory` and `Directory\Background`.

There is no certificate file to ship: a signature carries its own signer, so the installer reads the
certificate out of the package. The trust step itself is the price of a self-signed package -- it is
trusted nowhere until a machine is told to, and telling it needs an administrator, once per
machine. Signing with a
certificate the machines already trust (`Build.ps1 -ShortMenu -CertificateThumbprint ...`) removes
that step entirely.

None of this shows on a machine set to the classic context menu, where the short menu never renders.

## Command line

```
HardSpace <folder>            Scan a folder and show the result in a window.
HardSpace -c|--console <dir>  Scan and print the result to the console.
HardSpace --register          Add the Explorer context-menu entry (current user).
HardSpace --unregister        Remove that context-menu entry.
HardSpace --help              Show this text.
```

## How the de-duplication works

Every hard link to a file reports the same `(VolumeSerialNumber, FileId)` pair, read here with
`GetFileInformationByHandleEx(FileIdInfo)`. The same handle also yields `FILE_STANDARD_INFO`, whose
`NumberOfLinks` says whether the file has more than one name at all — only those files are put in
the de-duplication set, so a tree without hard links costs nothing extra.

Junctions and directory symlinks are not followed, and symlinks to files are not counted: their
content is counted where it really lives. They are reported as a separate count.

Files whose metadata cannot be read (ACL, exclusive lock) are counted as unique, with their
directory-entry size — the same answer Explorer gives — and reported as "unreadable entries".
Folders that cannot be opened at all are skipped, exactly as Explorer skips them.
