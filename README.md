# HardSpace

Folder size that does not double-count hard links.

Explorer (and `Get-ChildItem | Measure-Object`) adds up directory entries, so a build tree where
MSBuild hard-linked the same assembly into twenty output folders is reported twenty times over.
HardSpace reports both numbers side by side:

```
C:\src\SomeSolution

Space used on disk   : 21.04 GB (22'593'299'392 bytes)  100%
Explorer size        : 21.88 GB (23'493'212'847 bytes)  104%
Actual content size  : 20.99 GB (22'539'986'803 bytes)  99.8%

Hard links           : 650 names sharing 151 files
Saved by hard links  : 909.07 MB (953'226'044 bytes)  4.2%

Files                : 34'196
Folders              : 3'477
Scan time            : 0.7 s
```

- **Space used on disk** — the clusters those distinct files actually occupy, and so what the volume
  really gives up (NTFS compression and sparse files show up here).
- **Explorer size** — every directory entry counted in full, so it matches Explorer.
- **Actual content size** — each distinct file counted once, no matter how many names point at it.

The percentages are shares of the space actually used, which is why that line is always 100%: it is
the one figure here that is ground truth, so everything else is an error measured against it. The
Explorer line above 100% is exactly the amount Explorer overstates by -- 104% means it is claiming
4% more than the volume gives up.

A tree with no hard links still does not come out at exactly 100%, because a file occupies whole
clusters: the sizes add up to slightly less than the room they take.

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

Publishes `deploy\HardSpace.exe`. That one file is the whole deployment: it scans folders, and it
installs itself.

It carries everything any machine might need, because the machine it is built on is not the machine
it will run on. That means the Windows 11 short-menu pieces too -- the shell-extension DLL and a
signed sparse MSIX package, which `--install` writes out and registers -- taking the executable from
3.1 MB to 4.8 MB. Whether they are *used* is decided on the target machine, not here.

Packing and signing them needs the Windows SDK, and a certificate; a development one is created if
there is none. Without the SDK the build stops rather than quietly producing a lesser executable
that looks identical:

```
The Windows SDK is needed to pack and sign the short-menu package, and makeappx.exe and signtool.exe
could not be found. Install the Windows SDK, or pass -NoShortMenu for a build without it.
```

`-NoShortMenu` is for a quick local build with neither. `-CertificateThumbprint` signs with a
certificate of your own instead of the development one.

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

Being raw user32 means none of the Windows 11 dressing comes for free, so it is done by hand: the
system's light or dark colours (followed live, through `WM_SETTINGCHANGE`), rounded corners and a
matching border via DWM attributes, and the text box coloured through `WM_CTLCOLOR*`. The buttons
are owner-drawn -- a push button paints itself and ignores both its parent's colours and
`SetWindowTheme`, so in light mode it is drawn through the theme renderer, and in dark mode by hand,
the dark button class the shell uses for its own not being one `OpenThemeData` will hand out.

## Install

```
HardSpace.exe --install
```

One command, whatever the machine. It works out the best install available and says what it chose:

| Run as | What you get |
| --- | --- |
| yourself | `%LOCALAPPDATA%\Programs\HardSpace`, entry for you only |
| administrator | `%ProgramFiles%\HardSpace`, entry for every user, and the Windows 11 short menu |

Elevated is the one to prefer: some machines ignore per-user verbs entirely, and only an elevated
install can reach Windows 11's short menu. So an ordinary prompt does not just quietly settle for
less -- it says what the difference is and offers to restart itself elevated:

```
Putting HardSpace in Windows 11's default right-click menu -- the one that opens first -- needs an
elevated prompt, and installs it for every user of this machine.

Without that it installs for you alone, and its entry lives in the "Show more options" menu.

Yes     restart elevated, and install into the default menu
No      carry on without it
Cancel  install nothing
```

Answering yes raises the UAC prompt and runs the same command again with the same arguments; if
elevation is refused, it carries on with the lesser install and says so. `--user`, `--machine`,
`--no-short-menu` and `--quiet` each answer the question in advance, so a scripted install is never
stopped by it. Neither install needs a runtime.

`--user` and `--machine` force the scope; `--no-short-menu` leaves the package out; `--quiet` and
`--restart-explorer` decide the Explorer question up front; `--uninstall` undoes all of it. An install folder can be
given as the argument: `--install "C:\Tools\HardSpace"`.

Explorer reads context-menu entries when it starts, so the new one may not appear until it restarts.
The installer explains that and asks, always in a message box: a Windows-subsystem program is not
waited for by the shell, so the console it prints to belongs to the shell, which is reading the
keyboard itself. Anything typed there is answered by the shell, not by this. `--restart-explorer`
answers yes up front, `--quiet` neither asks nor restarts. Either way it says how to restart later.

If the entry does not appear, or appears for a split second and then vanishes, the machine ignores
per-user shell verbs. A machine forcing the Windows 11 classic context menu was observed doing
exactly that, and dropping a plain `notepad.exe` verb the same way, so it is worth testing with one
before blaming this tool. The cure is an elevated `--install`, which registers for the machine.

Because it is a Windows-subsystem program, a shell does not wait for it. From a script, use
`Start-Process -Wait HardSpace.exe -ArgumentList '--install','--quiet'` if the exit code matters.

### Installing on someone else's machine

Run `.\Build.ps1` and send them `deploy\HardSpace.exe`. One file, and it installs itself.

If it arrives by mail or chat it carries a mark of the web, and SmartScreen stops it on first run --
"Windows protected your PC", *More info*, *Run anyway*. Copying through a network share or a USB
stick avoids that. Signing would not help unless the certificate is one their machine already
trusts.

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
.\Build.ps1
```

embeds the shell extension and the package in the executable -- which the default build does -- and
on the target machine, from an
elevated prompt, `--install` writes them out beside the installed executable, tells the machine to
trust the package's certificate, and registers the package against that folder. It registers the classic verb as well, which is what
covers drives -- the package manifest cannot, its schema accepting only `*`, `.<extension>`,
`Directory` and `Directory\Background`.

There is no certificate file anywhere: a signature carries its own signer, so the installer reads it
out of `AppxSignature.p7x` inside the package it has just written. The trust step itself is the price of a self-signed package -- it is
trusted nowhere until a machine is told to, and telling it needs an administrator, once per
machine. Signing with a
certificate the machines already trust (`Build.ps1 -CertificateThumbprint ...`) removes
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
