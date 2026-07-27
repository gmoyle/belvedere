Belvedere
=========

An automated file manager for Windows — **revived**.
-------------------------------------

Belvedere watches folders you choose and automatically moves, copies, renames,
recycles, or otherwise acts on files based on rules you define (by name,
extension, size, or date). It was originally written by **Adam Pash** and
distributed by **Lifehacker** as an AutoHotkey app, and later maintained by
**Matthew Shorts** ([@mshorts](https://github.com/mshorts/belvedere)).

This repository is a **fork of [mshorts/belvedere](https://github.com/mshorts/belvedere)**
(itself derived from [adampash/belvedere](https://github.com/adampash/belvedere))
and now contains a **native .NET rewrite** (`src/Belvedere`) that replaces the
unmaintained AutoHotkey version, which stopped working on modern Windows. This
project remains under the same GPL v3 license as the original. The original
AutoHotkey source is preserved in the project's git history (see the commits
before the .NET rewrite, or the upstream repos linked above) rather than kept
in the working tree.

* Platform: Windows 10 / 11 (x64)
* Language: C# / .NET 8 (WinForms)
* License: GPL v3 — see [LICENSE.txt](LICENSE.txt)

Why the rewrite
---------------

The AutoHotkey v1 build broke on current Windows for several reasons: AHK v1 is
legacy (the ecosystem moved to v2), the unsigned compiled `.exe` was blocked by
SmartScreen/Defender, and it depended on dead components (Growl, iTunes COM
automation, a bundled 7-Zip). The rewrite is a modern, signable, self-contained
tray application with no external runtime dependencies.

What's new / different
----------------------

* **Real-time folder watching** (`FileSystemWatcher`) with a periodic sweep as a
  safety net — no more waiting for a fixed poll interval.
* **Rules can target whole folders, not just files** — move, copy, rename,
  recycle, or delete a subfolder as a unit (e.g. "delete album folders
  untouched for 30 days"), in addition to the original file-based rules.
* **"Test rule" dry-run preview** — see exactly which files or folders a rule
  would act on right now, and what would happen to them, before you ever
  enable it or let it touch anything.
* **Native Windows notifications** instead of Growl.
* **Recycle Bin** via the proper Windows shell API.
* **JSON configuration** under `%AppData%\Belvedere\config.json`, plus a
  **one-click importer for your existing `rules.ini`** so upgraders keep their
  rules.
* **Runs as the current user** (no forced Administrator elevation) and can start
  automatically at sign-in.
* An **About tab** in the Manage window (not just a tray popup) with version,
  credits, and a link back to this repo.
* Dropped: iTunes integration, Growl, and built-in 7-Zip compression.

Rule model (unchanged concept)
------------------------------

A rule watches a source folder and, for every file *or folder* matching
**all** or **any** of its conditions, performs an action:

* **Target:** Files or Folders — most subjects/actions work for either, except
  Extension and Size (files only) and Print (files only).
* **Subjects:** Name, Extension, Size, Date last modified / opened / created
* **Verbs:** is / is not, matches/contains one of, contains, RegEx, greater/less
  than (size), is (not) in the last N seconds…weeks (dates)
* **Actions:** Move (optionally leaving a shortcut), Copy, Rename (with
  `[filename]`, `[ext]`, `[YYYY]`, … tokens), Send to Recycle Bin, Delete, Open,
  Print (files only), or run a Custom program.

See [docs/USER_GUIDE.md](docs/USER_GUIDE.md) for a full walkthrough of rules,
the Test-rule preview, and preferences.

Install
-------

Download and run **`Belvedere-<version>.msi`**. It installs a single
self-contained executable to `C:\Program Files\Belvedere`, adds a Start Menu
shortcut, and registers a normal Add/Remove Programs entry for clean uninstall.
No .NET runtime or AutoHotkey is required. Enable "Start Belvedere when I sign
in" from the app's Preferences if you want it to launch automatically.

Build & run (from source)
-------------------------

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```bash
# Run from source
dotnet run --project src/Belvedere/Belvedere.csproj

# Produce a self-contained, single-file exe (no runtime install needed)
dotnet publish src/Belvedere/Belvedere.csproj -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/win-x64
```

The published `dist/win-x64/Belvedere.exe` is fully self-contained — the icons
are embedded, so that single file is all you need to distribute.

Build the installer
-------------------

Requires the [WiX v5 toolset](https://wixtoolset.org/) (`dotnet tool install --global wix`).
The script publishes the exe and packages the MSI in one step:

```powershell
pwsh installer-dotnet/build.ps1                # builds dist/Belvedere-2.1.0.msi
pwsh installer-dotnet/build.ps1 -Version 2.2.0 # override the version
```

For production distribution, code-sign both the `.exe` and the `.msi` to avoid
SmartScreen warnings.

Documentation
-------------

* [docs/USER_GUIDE.md](docs/USER_GUIDE.md) — how rules, conditions, actions,
  the Test-rule preview, and preferences work.
* [CHANGELOG.md](CHANGELOG.md) — what changed in each version.
* [FEATURE_IDEAS.md](FEATURE_IDEAS.md) — backlog of feature requests pulled
  from the original Lifehacker comment thread, checked against the current
  feature set.

Legacy AutoHotkey version
-------------------------

The original AutoHotkey source has been removed from the working tree now that
the .NET rewrite is the supported version. It's still available in this
repository's git history, and in the upstream repos linked above.

Credits & lineage
------------------

Belvedere is the work of many hands, and this revival stands entirely on that
foundation:

* **Adam Pash** — original author, published via **Lifehacker**.
* **Matthew Shorts** ([@mshorts](https://github.com/mshorts/belvedere)) —
  maintainer of the fork this repository is based on (build automation, bug
  fixes, license cleanup).
* Icon design by **What Cheer**.
* This **.NET revival** is a fork that ports the app to native C#/.NET 8 while
  preserving the original design, behavior, and GPL v3 license.

Enormous thanks to Adam and Matthew — this project exists only because of their
work. Contributions and merges upstream are welcome.
