# HardSpace

Folder size that does not double-count hard links.

Explorer (and `Get-ChildItem | Measure-Object`) adds up directory entries, so a build tree where
MSBuild hard-linked the same assembly into twenty output folders is reported twenty times over.
HardSpace reports both numbers side by side:

```
C:\li6\Projects\Sniffers\Sniffer#7\Projects

Size (as Explorer counts it) : 21.9 GB (23'513'177'998 bytes)
Actual content size          : 21.63 GB (23'226'728'150 bytes)
Space used on disk           : 21.68 GB (23'280'696'200 bytes)

Hard links                   : 252 names sharing 126 files
Saved by hard links          : 273.18 MB (286'449'848 bytes)

Files                        : 34'196
Folders                      : 3'477
Scan time                    : 0.7 s
```

- **Size (as Explorer counts it)** — every directory entry counted in full, so it matches Explorer.
- **Actual content size** — each distinct file counted once, no matter how many names point at it.
- **Space used on disk** — the clusters those distinct files actually occupy (so NTFS compression
  and sparse files show up here).

## Build

```
dotnet build HardSpace.sln -c Release
```

Requires the .NET 10 SDK, plus the "Desktop development with C++" workload (NativeAOT links with
the MSVC linker).

### Publish (NativeAOT)

The tool is a fresh process on every right-click, so start-up time is paid on every single use.
`PublishAot` is therefore on: a publish with a runtime identifier produces one native executable
with no runtime to find and nothing to JIT.

```
dotnet publish HardSpace\HardSpace.csproj -c Release -r win-x64 -o C:\Tools\HardSpace
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

## Install the right-click entry

There are two menus in Windows 11, and they are registered differently.

### The short menu (Windows 11)

The top-level menu only accepts an `IExplorerCommand` that comes from an MSIX package, so
`Package\` builds a *sparse* package: it carries the manifest and nothing else, while the binaries
stay in a normal folder that is named at install time.

```
Package\Build-Package.ps1 -CreateSelfSignedCertificate
```

That publishes both binaries to `C:\Tools\HardSpace` (override with `-InstallDirectory`), packs
`Package\out\HardSpace.msix`, and signs it. It then prints the two install commands; the first
needs an elevated prompt and is only needed once per machine:

```
Import-Certificate -FilePath "...\out\HardSpace.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople
Add-AppxPackage -Path "...\out\HardSpace.msix" -ExternalLocation "C:\Tools\HardSpace"
```

Windows requires the package to be signed by a certificate the machine trusts -- hence the
certificate step. With a real code-signing certificate, pass `-CertificateThumbprint` instead and
skip it.

The install directory is the package payload, so it must hold this tool and nothing else; the script
refuses to publish into a directory with anything else in it unless `-Force` is passed, which empties
it first. That path is baked into the package: moving the binaries afterwards means installing
again. To remove: `Get-AppxPackage *HardSpace* | Remove-AppxPackage`.

The manifest covers folders and the background of an open folder. Drives are not offered -- the
schema only accepts `*`, `.<extension>`, `Directory` and `Directory\Background` -- so a drive keeps
the legacy entry below.

### The legacy menu ("Show more options")

No package, no certificate, no elevation: the tool writes three keys under
`HKEY_CURRENT_USER\Software\Classes` itself (`Directory\shell`, `Directory\Background\shell`
and `Drive\shell`).

```
HardSpace.exe --register
```

Register from the folder the executable will live in permanently -- the registry stores that exact
path, so moving it afterwards breaks the entry (just re-run `--register`). To remove it:

```
HardSpace.exe --unregister
```

Installing both is fine, and is what gives every case a menu entry; the verb then appears twice for
a folder, once in each menu.

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

The Windows 11 command lives in `HardSpace.ShellExtension`, a NativeAOT *shared library*: Explorer
loads it into its surrogate process on every right-click, so a managed runtime start-up per click is
exactly what it must not cost. It exports `DllGetClassObject`/`DllCanUnloadNow` directly, implements
`IExplorerCommand` through source-generated COM interop, and reads the clicked folder out of the
`IShellItemArray` through its vtable. Clicking the verb starts `HardSpace.exe` from beside the DLL.

Files whose metadata cannot be read (ACL, exclusive lock) are counted as unique, with their
directory-entry size — the same answer Explorer gives — and reported as "unreadable entries".
Folders that cannot be opened at all are skipped, exactly as Explorer skips them.
