# Publishing HardSpace to the Microsoft Store

Written for a first-time independent publisher. It goes from having no account at all to the app
being installable by anyone, and says which steps are yours, which are Microsoft's, and how long
each takes.

Two warnings before anything else.

**Prices, page names and validation rules change.** Everything here was true when written; treat
figures as "check this", not as fact. Where a number matters — the registration fee especially —
the page itself is the authority.

**The Store build is not the standalone build.** They are different packages with different
limitations, and one does not replace the other:

| | Standalone (`Build.ps1`) | Store (`Build-StorePackage.ps1`) |
| --- | --- | --- |
| Entry in the Windows 11 default menu | yes | yes |
| Entry in the classic menu, and on drives | yes, via registry verbs | **no** |
| Works on a machine forced to the classic context menu | yes | **no — nothing appears** |
| Certificate warnings on the way in | SmartScreen, and a trust step per machine | none |
| Cost to update | 30 seconds | a submission and a review |

That third row is the one to weigh: your own machine forces the classic menu, so a Store install
would show you nothing at all. Publishing is for other people, and you keep the standalone build for
yourself.

---

## Step 1 — Decide the publisher name

The publisher display name is what people see under the app in the Store, and it has to be a name
you can prove is yours. `sonatique` is fine as an individual: identity validation checks *you*, and
the display name is then attached to your account.

Nothing to do here but decide, because the next step asks for it and changing it later is a support
request rather than a form field.

## Step 2 — Register as a developer

Go to **partner.microsoft.com**, sign in with the Microsoft account you want to own this forever —
not a work account, and not one you might lose — and register for the **Windows & Xbox** developer
programme.

- Choose **Individual**, not Company. Company registration requires proof of business existence.
- There is a one-time registration fee. It has historically been about $19 for an individual, and
  Microsoft has changed and occasionally waived it. The page will tell you.
- You give your legal name and address; they are used for validation and for payouts if you ever
  charge for anything, and are not published.

**Then you wait.** Identity validation takes anything from a day to a couple of weeks. Nothing else
can happen until it clears, so do this first even if the app is not finished.

## Step 3 — Reserve the app's name

In Partner Center: **Apps and games → New product → MSIX or PWA app**, and reserve **HardSpace**.

If the name is taken, pick another now: the name is part of the package identity, so changing it
later means a new product.

Reserving assigns three values you cannot choose and must build into the package. Find them under
the product's **Product identity** page:

| Partner Center calls it | Goes into the manifest as | Looks like |
| --- | --- | --- |
| Package/Identity/Name | `Identity/@Name` | `12345Sonatique.HardSpace` |
| Package/Identity/Publisher | `Identity/@Publisher` | `CN=A1B2C3D4-1234-...` |
| Package/Properties/PublisherDisplayName | `PublisherDisplayName` | `sonatique` |

Copy all three. They are the arguments to the build script.

## Step 4 — Try the packaged build before submitting

Do this before you ever submit. It catches everything except Store policy.

```
.\Package\Build-StorePackage.ps1 -SignForLocalTesting
```

That builds `store\HardSpace.msix` (unsigned, for the Store) and
`store\HardSpace-signed-for-testing.msix` signed with the development certificate, which you can
install here:

```
Add-AppxPackage -Path .\store\HardSpace-signed-for-testing.msix
```

Check that it installed and that the app runs as a packaged app:

```
Get-AppxPackage *HardSpace*
HardSpace --console C:\some\folder
```

The second command works only if the package registered its execution alias, so it tests something
real. On a machine with the stock Windows 11 menu, also right-click a folder and confirm the entry
is there. Then remove it:

```
Get-AppxPackage *HardSpace* | Remove-AppxPackage
```

## Step 5 — Build the package you will actually submit

With the three values from step 3:

```
.\Package\Build-StorePackage.ps1 `
    -IdentityName 12345Sonatique.HardSpace `
    -Publisher 'CN=A1B2C3D4-1234-5678-9ABC-DEF012345678' `
    -PublisherDisplayName sonatique `
    -Version 1.0.0.0
```

The result, `store\HardSpace.msix`, is **unsigned on purpose**. The Store signs what it accepts, and
rejects packages signed by anyone else — including the one from step 4, which is why that file has a
different name. Never submit `HardSpace-signed-for-testing.msix`.

Version numbers: four parts, the last always `0`, and every submission must be higher than the last.
`1.0.0.0`, then `1.0.1.0`, and so on.

## Step 6 — Run the certification kit

The **Windows App Certification Kit** ships with the Windows SDK and runs the same automated tests
the Store will. Failing here is quick to fix; failing after submission costs a review cycle.

Launch **Windows App Cert Kit** from the Start menu, choose the MSIX, and let it run. It wants the
package *installed* first, so use the signed test copy from step 4.

## Step 7 — Fill in the listing

Back in Partner Center, on the product's **Submission** page. The parts that matter:

- **Pricing** — free.
- **Properties → Category** — Utilities & tools.
- **Age ratings** — a questionnaire. HardSpace collects nothing and shows nothing; the answers are
  all "no" and it will come out as suitable for everyone.
- **Privacy policy** — required if the app handles personal data. It does not, but the field may
  still be mandatory; a one-page statement saying the app collects, stores and transmits nothing is
  enough, hosted anywhere stable. A page in this repository, served by GitHub Pages, will do.
- **Store listing** — description, and **at least one screenshot** (1366×768 or larger). Screenshots
  of the result window are the obvious choice.
- **Packages** — upload `store\HardSpace.msix`.

### The part that gets apps rejected

HardSpace declares `runFullTrust` and installs a **shell extension**. Both draw human review. The
description and the reviewer notes must explain, in plain words:

- It reads file metadata across the disk, which is why it needs full trust: it opens each file for
  its size and its link count, and reads nothing of the contents.
- It adds one context-menu entry on folders, which is the app's entire purpose.
- It sends nothing anywhere.

Say this in **Notes for certification** as well as in the description. A reviewer who has to guess
why an app wants full trust will reject it.

## Step 8 — Submit, and wait

Certification is usually a few hours to three days for a small app; the first submission from a new
account is often slower. You will get an email either way, and a report naming the policy if it
fails.

When it passes you can publish immediately or hold it. Once published, the listing takes up to
another day to be visible everywhere.

## Step 9 — Afterwards

- **Updating** is steps 5 through 8 again with a higher version. The review is usually faster.
- **The standalone build keeps working** and does not go away. Keep shipping
  `deploy\HardSpace.exe` to colleagues on machines with the classic menu, for whom the Store version
  shows nothing.
- **You keep the development certificate.** It signs the sparse package the standalone build
  embeds. It has nothing to do with the Store.

---

## What the code does differently when packaged

Two things, both already in place:

`Installer.IsPackaged` asks Windows whether this process has package identity. When it does,
`--install` and `--uninstall` do nothing but explain that Windows manages the app — because a
packaged app's registry writes go to a virtualised hive nothing else reads, and its files sit in a
folder it may not write to.

`Package\Store\AppxManifest.xml` is a second manifest, separate from the sparse one. It carries the
payload rather than pointing at it, declares no external content, adds the execution alias so the
app can be run from a terminal, and leaves out the registry verbs a packaged app cannot write.
