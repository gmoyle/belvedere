# Belvedere user guide

Belvedere watches folders you choose and automatically acts on the files (or
folders) inside them, based on rules you define. This guide covers the app
itself; see the [README](../README.md) for installation and building from
source.

## Getting started

1. Install `Belvedere-<version>.msi`, or run the published `.exe` directly.
2. On first launch, Belvedere offers to import a legacy `rules.ini` if it
   finds one nearby. Say no if this is a fresh setup.
3. Belvedere lives in the **system tray** — look for its icon near the clock
   (it may be under the `^` overflow arrow). Right-click it for the menu, or
   double-click to open **Manage**.

Nothing runs until you add and enable at least one rule.

## The Manage window

Right-click the tray icon → **Manage…** opens four tabs:

- **Rules** — add, edit, duplicate, delete, enable/disable rules, and import
  a legacy `rules.ini`.
- **Preferences** — sweep interval, real-time watching, logging,
  notifications, exit confirmation, and launch-at-sign-in.
- **Log** — a running history of what Belvedere has done (and any errors),
  with buttons to refresh, clear, or save it to a file.
- **About** — version, credits, and a link back to the project's repo.

Changes made in Manage only take effect when you click **Apply** or
**Save & Close**. **Save & Close** only closes the window if the save actually
succeeded — if something goes wrong (disk, permissions), you'll see the real
error and the window stays open so nothing is silently lost.

## Creating a rule

Click **Add…** on the Rules tab. A rule has:

**Rule name** — anything descriptive; it's shown in the Rules list and in
confirmation prompts.

**Watch folder** — the folder Belvedere scans. **Include subfolders**
extends the scan recursively.

**Match: Files or Folders** — whether this rule evaluates the *files* inside
the watch folder, or the *subfolders* themselves. Switching this filters
which subjects and actions are available (see below).

**Match: ALL conditions / ANY condition** — whether every condition must be
true, or just one of them, for the rule to act.

**Conditions** — one or more subject/verb/value tests:

| Subject | Applies to | Notes |
|---|---|---|
| Name | Files & Folders | The name without extension |
| Extension | Files only | Without the leading dot (e.g. `jpg`, not `.jpg`) |
| Size | Files only | Folder size isn't computed (would require a slow recursive sum) |
| Date last modified / opened / created | Files & Folders | |

Verbs depend on the subject:
- **Text** (Name, Extension): is, is not, contains, does not contain,
  matches one of, does not match one of, contains one of (comma-separated
  lists), RegEx.
- **Size**: is, is not, is greater/less than (or equal), with a KB/MB/GB unit.
- **Dates**: is (not) in the last *N* seconds/minutes/hours/days/weeks.

Text matching is always case-insensitive.

**Action** — what happens to each match:

| Action | Files | Folders | Needs a destination? |
|---|---|---|---|
| Move | ✅ | ✅ | Yes — a folder |
| Move & leave shortcut | ✅ | ✅ | Yes — a folder |
| Copy | ✅ | ✅ | Yes — a folder |
| Rename | ✅ | ✅ | Yes — a name template (see below) |
| Send to Recycle Bin | ✅ | ✅ | No |
| Delete (permanent) | ✅ | ✅ | No |
| Open | ✅ | ✅ | No |
| Print | ✅ | ❌ | No |
| Custom command | ✅ | ✅ | Yes — a program to run |

Move/Copy destinations that don't exist yet are created automatically.
**Overwrite** controls what happens if something with the same name is
already there.

**Rename templates** use tokens that get replaced with real values:

```
[filename]  name without extension       [ext]        .ext (with the dot)
[fullname]  full name with extension     [drive]      e.g. D:
[YYYY][MM][DD]         year/month/day    [hh][mm][ss] hour/minute/second
[MMMM][MMM]            full/short month  [DDDD][DDD]  full/short weekday
[WDay][YDay]           day-of-week/year  [ms]         milliseconds
[DT][DT-UTC]           full timestamp (local / UTC), yyyyMMddHHmmss
```

Example: `[filename]_[YYYY]-[MM]-[DD][ext]` renames `report.csv` to
`report_2026-07-27.csv`.

**Ignore files/folders that are** read-only, hidden, or system — checkboxes
to exclude items carrying those attributes.

**Ask before acting on each file** — shows a confirmation prompt for every
match, instead of acting silently.

## Test rule — preview before you trust it

Click **Test rule…** at the bottom of the rule editor. Belvedere scans the
watch folder right now with the rule's current settings and shows every file
or folder that matches, and exactly what would happen to it — without
touching anything. Use this to check a new or edited rule before enabling it.

A scan is capped at 50,000 entries and shows up to 500 matches, so pointing a
recursive rule at something huge (e.g. a whole drive) can't hang the app.

## Preferences

- **Sweep folders every** — how often the periodic safety-net scan runs (in
  addition to real-time watching).
- **Watch folders in real time** — react to new/changed files immediately via
  `FileSystemWatcher`, rather than waiting for the next sweep.
- **Enable logging** — write actions and errors to the log file.
- **Show a notification when an action is taken** — tray toast per action.
- **Confirm before exiting** — ask before the tray "Exit" closes the app.
- **Start Belvedere when I sign in** — registers a per-user startup entry
  (no admin rights needed). If this fails (e.g. a corporate policy blocks
  it), your other settings still save; you'll get a separate warning just
  for this one.

## Importing an existing rules.ini

**Rules tab → Import rules.ini…** (or the first-run prompt) reads a legacy
AutoHotkey-era `rules.ini` and adds its rules to your current configuration.
If a rule's destination folder can't be created (e.g. it references a drive
letter that doesn't exist on this machine), that rule is imported **disabled**
with a clear explanation — fix the destination and re-enable it once it's
correct.

## Troubleshooting

- **A rule doesn't seem to do anything** — open it and click **Test rule…**
  first. If it finds no matches, the conditions aren't matching what you
  expect; if it finds matches but the rule is still not running, check that
  it's enabled (the checkbox in the Rules list) and that real-time watching
  or the sweep interval is configured the way you expect.
- **Check the Log tab** for a full history of actions and errors.
- **Belvedere is already running** — it's single-instance; look in the
  system tray rather than launching it again.
