# Feature backlog — from the old Lifehacker comments

Sourced from a full read-through of every comment on the original Belvedere
Lifehacker post, newest to oldest, filtered to genuine feature requests only
(bug reports, download complaints, praise, and already-answered questions
excluded). Cross-checked against the current .NET rewrite's feature set on
2026-07-27.

Review these one at a time whenever you're ready — ask to implement a
specific numbered item, or ask for a fresh comparison if the feature set has
moved on since this was written.

## Already had (no action taken)

- Recycle Bin *and* permanent Delete as separate actions
- Overwrite checkbox for Move/Copy
- Recursive subfolder scanning
- Configurable scan interval
- Enable/disable individual rules + manual "Run now"
- Rename to change just the extension while keeping the name (via the
  Rename template)
- "Send file to another program" (the Custom action)
- Suppressing "no match" notification spam (we don't log non-matches at all)
- Typing a folder path directly instead of only browsing

## Backlog

| # | Status | Request | Who / when |
|---|---|---|---|
| 1 | **Skipped** (2026-07-27) | Progress/activity indicator while a rule runs or "Test rule" scans | Darren Penner, 2013 |
| 2 | **Done** (commit `6add1b7`) | Act on whole folders, not just files inside them | snze, wannabeharleyguy, Robert Yorks, 2013 |
| 3 | Deferred | Email notifications when a file is created/modified/deleted. Needs a design discussion first: sending unattended mail means storing SMTP credentials somewhere, which needs a safe approach (e.g. an app password + OS credential store, not a plaintext config value) | wannabeharleyguy, 2013 |
| 4 | Deferred | Case-sensitive matching option (currently hardcoded case-insensitive) | ZahirHoddie, 2013 |
| 5 | Deferred | Environment-variable expansion in folder paths (e.g. `%TEMP%`) | mig000, 2013 |
| 6 | Deferred | Run as a Windows Service / scheduled task instead of a persistent tray app | johnnyrevenge + Ryan Fisher, 2013 |
| 7 | Deferred | Templated/dated destination folders for Move & Copy (e.g. auto-sort into a folder named by year) — tokens currently only work in Rename | greeze, 2013 |
| 8 | Deferred | Apply one rule to multiple source folders at once, more easily than manual duplication | OptoGeek, 2013 |
| 9 | Deferred | Compound AND/OR condition logic beyond simple "match ALL" or "match ANY" | OptoGeek (per Adam's reply), 2013 |
| 10 | Deferred | Photo metadata conditions (EXIF/XMP tags) | InsaneNinja, 2013 |
| 11 | Deferred | Dedicated "starts with" / "ends with" verbs (Regex already covers this, just less friendly) | zapper, 2013 |
| 12 | Deferred | Archive-aware rules — act based on a zip's contents | Lunisneko, 2013 |
