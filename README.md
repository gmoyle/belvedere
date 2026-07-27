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
unmaintained AutoHotkey version, which stopped working on modern Windows. All of
the original AutoHotkey source is preserved unchanged in the repository root and
`includes/` for reference and to honor its history. This project remains under
the same GPL v3 license as the original.

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
* **Native Windows notifications** instead of Growl.
* **Recycle Bin** via the proper Windows shell API.
* **JSON configuration** under `%AppData%\Belvedere\config.json`, plus a
  **one-click importer for your existing `rules.ini`** so upgraders keep their
  rules.
* **Runs as the current user** (no forced Administrator elevation) and can start
  automatically at sign-in.
* Dropped: iTunes integration, Growl, and built-in 7-Zip compression.

Rule model (unchanged concept)
------------------------------

A rule watches a source folder and, for every file matching **all** or **any**
of its conditions, performs an action:

* **Subjects:** Name, Extension, Size, Date last modified / opened / created
* **Verbs:** is / is not, matches/contains one of, contains, RegEx, greater/less
  than (size), is (not) in the last N seconds…weeks (dates)
* **Actions:** Move (optionally leaving a shortcut), Copy, Rename (with
  `[filename]`, `[ext]`, `[YYYY]`, … tokens), Send to Recycle Bin, Delete, Open,
  Print, or run a Custom program.

Build & run
-----------

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```bash
# Run from source
dotnet run --project src/Belvedere/Belvedere.csproj

# Produce a self-contained single-file exe (no runtime install needed)
dotnet publish src/Belvedere/Belvedere.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -o dist/win-x64
```

The published `dist/win-x64/Belvedere.exe` (plus its `resources/` folder) is all
you need to distribute. For production, code-sign the `.exe` to avoid SmartScreen
warnings.

Legacy AutoHotkey version
-------------------------

The original AutoHotkey source (`Belvedere.ahk`, `includes/`, `installer/`) is
kept for historical reference and requires AutoHotkey v1.x to build. It is no
longer the supported build path.

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
