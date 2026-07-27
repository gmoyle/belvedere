# Changelog

All notable changes to Belvedere are documented here. Versions follow
[Semantic Versioning](https://semver.org/); format loosely follows
[Keep a Changelog](https://keepachangelog.com/).

## [2.1.0] — 2026-07-27

### Added
- **Folder-targeting rules.** A new Files/Folders selector lets a rule match
  and act on subfolders themselves (move, copy, rename, recycle, or delete a
  whole directory) rather than only the files inside them. Extension and
  Size stay files-only (a folder's size would need an expensive recursive
  sum); Print is unsupported for folders.
- **"Test rule" dry-run preview.** A button in the rule editor shows every
  file or folder the current (even unsaved) rule would act on right now, and
  what would happen, without touching the filesystem.
- **About tab** in the Manage window, alongside Rules/Preferences/Log —
  version, description, credits, and a link to the GitHub repo. Previously
  "About" was only a tray popup.
- **rules.ini import warnings.** If an imported rule's destination folder
  can't be created (e.g. it references a drive that doesn't exist on this
  machine), the rule is imported disabled with a clear explanation, instead
  of silently importing broken and enabled.
- Missing destination folders are now created automatically for Move/Copy
  actions (and during rules.ini import), instead of failing with "destination
  folder missing."
- Every validation message in the rule editor now focuses (and scrolls to)
  the actual field that needs fixing, instead of just showing an OK dialog.
- `FEATURE_IDEAS.md`: a backlog of feature requests sourced from the original
  Lifehacker comment thread, checked against the current feature set.

### Fixed
- **"Move & leave shortcut" left a dangling shortcut.** The `.lnk` created
  when moving a file or folder pointed at the item's *old* location — the one
  about to be vacated — instead of its new destination, so the shortcut
  stopped working the instant the move completed. It now points at the new
  location, matching the original AutoHotkey version's intended behavior.
- **Silent config corruption.** If `config.json` couldn't be read, Belvedere
  used to quietly reset to blank settings with no explanation. It now shows
  a dialog naming the corrupt file's backup path.
- **Silent save failures.** If saving settings failed (disk, permissions, or
  a corporate registry policy blocking the "start at sign-in" key), the
  Manage window used to appear to do nothing. It now shows the actual error,
  and "Save & Close" only closes on a confirmed successful save.
- **Rules that could never match, silently.** A non-numeric Size/Date value
  or an invalid regex pattern used to save without complaint and then never
  match anything, forever, with no indication why. The rule editor now
  validates every condition before it can be saved.
- Folder-watcher and sweep failures now also show a tray notification, not
  just a log entry.
- `MainForm.SaveLog()` (Save the log to a file) now reports a real error
  instead of silently failing.

### Changed
- Removed the legacy AutoHotkey source (`Belvedere.ahk`, `includes/`,
  `help/`, `installer/`) from the working tree now that the .NET rewrite is
  the supported version. Still available in git history and the upstream
  repos.

## [2.0.0] — Initial .NET revival

The original AutoHotkey v1 build stopped working on modern Windows: AHK v1
is legacy, the unsigned compiled exe was blocked by SmartScreen, and it
depended on dead components (Growl, iTunes COM automation, bundled 7-Zip).
This release replaces it with a native rewrite.

### Added
- Full C#/.NET 8 WinForms rewrite (`src/Belvedere`): the same subject/verb/
  object rule engine, actions (Move/Copy/Rename/Recycle/Delete/Open/Print/
  Custom), system tray app, and a Manage window (Rules/Preferences/Log tabs).
- Real-time folder watching (`FileSystemWatcher`) with a periodic sweep as a
  safety net, replacing fixed-interval polling.
- Native Windows toast notifications, replacing Growl.
- Recycle Bin via the proper Windows shell API.
- JSON configuration (`%AppData%\Belvedere\config.json`) with a one-click
  importer for an existing `rules.ini`.
- Runs as the current user (no forced Administrator elevation); optional
  launch at Windows sign-in.
- Self-contained, single-file, code-signable executable; a WiX MSI installer
  with a Start Menu shortcut and clean Add/Remove Programs entry.
- Dropped: iTunes integration, Growl, built-in 7-Zip compression (all
  unmaintained/dead dependencies in the original).
