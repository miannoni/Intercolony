# Release procedure

How to publish Intercolony without publishing the repository.

Everything in the first section was verified against `reference/decompiled/` —
`Verse.Steam/Workshop.cs`, `Verse/ModMetaData.cs`, `RimWorld/Page_ModsConfig.cs`. None of it is
from recollection. Re-verify it against the decompiled source if RimWorld updates.

---

## Read this before the first upload

**The first upload may become public the instant it is created.** RimWorld never calls
`SteamUGC.SetItemVisibility` — it is not in the submission path at all. The new item therefore
gets whatever Steam defaults to, and that default is not determinable from RimWorld's code.

Plan for the item being visible immediately:

- Have the description text and the gallery screenshots ready **before** clicking Upload, not after.
- The first thing you do once the upload completes is open the item page and set visibility.
  Not the description, not the screenshots — visibility.
- If a public item carrying only `About.xml`'s one-sentence blurb is unacceptable even briefly,
  confirm Steam's creation default before uploading rather than after.

---

## What RimWorld actually does

- **It uploads the mod's root directory wholesale.** `ModMetaData.GetWorkshopUploadDirectory()`
  returns `RootDir`, handed straight to `SteamUGC.SetItemContent(updateHandle, hook.Directory.FullName)`.
  There is no filtering, no exclude list and no junction handling. Whatever sits in that folder is
  what gets published.
- **The mod must be in the RimWorld `Mods` folder.** `CanToUploadToWorkshop()` returns false unless
  `Source == ContentSource.ModsFolder`. A folder in `dist/` cannot be uploaded from where it sits —
  this is why the procedure below moves a clean copy into `Mods`.
- **`About/PublishedFileId.txt` is the binding.** A valid ID sends the upload to
  `SteamUGC.StartItemUpdate` on that ID; only an absent or invalid ID reaches `CreateItem`. It never
  silently creates a second item. The file is written as soon as Steam creates the item, which is
  *before* the content submission succeeds — so a failed first upload can still leave a valid ID
  behind, and the retry is an update rather than a new item.
- **The description is sent only on creation.** `SetItemDescription` sits inside `if (creating)`.
  Anything you write on the Steam website survives every later re-upload. The **title** is re-sent
  every time from `About.xml`'s `<name>`.
- **Tags are automatic:** `Mod`, plus every supported version as `Major.Minor` (`1.6`). Sent on both
  create and update.
- **The preview is `About/Preview.png`,** and RimWorld only checks `File.Exists`. If it is missing
  the game logs `Missing preview file at ...` and uploads anyway, leaving a blank thumbnail.
  RimWorld enforces no size or dimension limit. Steam does — keep the file under **1 MB**, because
  nothing local will catch it if you do not.

---

## Before you start

1. Final `About/Preview.png` in place and **under 1 MB**.
2. `powershell -ExecutionPolicy Bypass -File package.ps1` run clean.
3. Description text and gallery screenshots ready to paste and upload.
4. Steam running and logged in.
5. Development mode on: **Options → General → Development mode**. The upload option is gated on
   `Prefs.DevMode` and does not appear without it.

---

## First upload

### 1. Replace the junction with the clean copy

`Mods\Intercolony` is a junction to `C:\dev\Intercolony`. Uploading through it would publish the
whole repository — including `reference/vanilla-defs`, itself a junction to RimWorld's entire `Data`
directory, and `reference/mods`, which is other authors' work.

```powershell
$mods = "C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods"

# Remove the junction ONLY. rmdir without /s deletes the link and never the target.
# Do NOT use Remove-Item -Recurse -Force on a junction.
cmd /c rmdir "$mods\Intercolony"

# Sanity check: the repository must still be there.
Test-Path C:\dev\Intercolony\Source          # must print True

# Put the clean package in its place as a real directory.
Copy-Item -Recurse "C:\dev\Intercolony\dist\Intercolony-0.9.0" "$mods\Intercolony"

# Confirm it is a real folder, not a reparse point, and holds only release content.
(Get-Item "$mods\Intercolony").Attributes                            # must NOT contain ReparsePoint
Get-ChildItem "$mods\Intercolony" -Recurse -File | Measure-Object    # expect 9
```

**Do not skip the last two checks.** They are the difference between publishing a mod and publishing
Ludeon's game data.

### 2. Upload

Start RimWorld. **Mods** → select **Intercolony** → **More actions** (bottom right of the mod info
panel) → **Upload to Steam Workshop**.

A red confirmation dialog asks whether you are the content author, with a forced ~6 second delay
before **Yes** becomes usable. That delay is normal.

### 3. Set visibility, then fill the page

Open the Workshop item page. **Set visibility first** — see the warning at the top of this document.

Then paste the description and add the gallery screenshots. The description currently holds
`About.xml`'s one-sentence blurb, because that is what creation sends; what you paste replaces it
permanently and will not be overwritten by later uploads.

### 4. Save the PublishedFileId

RimWorld has written `Mods\Intercolony\About\PublishedFileId.txt`. That folder is a copy of
`dist\Intercolony-0.9.0`, and `package.ps1` wipes and rebuilds `dist/` on every run — so the ID dies
with the next package unless it is saved somewhere else now.

```powershell
New-Item -ItemType Directory -Force C:\dev\Intercolony\.workshop | Out-Null
Copy-Item "$mods\Intercolony\About\PublishedFileId.txt" "C:\dev\Intercolony\.workshop\PublishedFileId.txt"
```

`.workshop/` is gitignored: the ID stays with the project, out of the repository and out of every
release zip. If it is ever lost anyway it is the number in the Workshop URL, so this is a nuisance
rather than a disaster.

### 5. Smoke-test what Steam serves

Test the uploaded build, not the one on disk.

```powershell
# The local copy must go, or two mods will share packageId miannoni.intercolony.
Remove-Item -Recurse -Force "$mods\Intercolony"
```

Subscribe on the item page — you can subscribe to your own hidden item — let Steam download it, and
start RimWorld. Check:

- Intercolony appears in the mod list as a **Steam** mod, with the preview image showing.
- Its content is only release files: no `Source`, no `reference`, no `docs`, no `Screenshots`.
- Enable it below Harmony, start a colony, open the **intercolony** tab, no red errors.
- `powershell -ExecutionPolicy Bypass -File dev.ps1 log` for anything unexpected.

### 6. Restore the junction

```powershell
cmd /c rmdir "$mods\Intercolony"          # only if a local folder is still there
cmd /c mklink /J "$mods\Intercolony" "C:\dev\Intercolony"
```

Unsubscribe from the Workshop item afterwards, or two copies will be loaded.

---

## Later updates — keeping the same Workshop item

The only thing that matters is that `About/PublishedFileId.txt` is present in the folder you upload
from. `package.ps1` deliberately does not copy it, so put it back by hand:

```powershell
$mods = "C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods"

powershell -ExecutionPolicy Bypass -File package.ps1 -Version 0.9.1

cmd /c rmdir "$mods\Intercolony"
Copy-Item -Recurse "C:\dev\Intercolony\dist\Intercolony-0.9.1" "$mods\Intercolony"

# This line is what preserves the Workshop item.
Copy-Item "C:\dev\Intercolony\.workshop\PublishedFileId.txt" "$mods\Intercolony\About\PublishedFileId.txt"
```

Then the same **More actions** menu. The option now reads **Update on Steam Workshop** instead of
**Upload to Steam Workshop**.

**That label is the check.** If it still says "Upload", stop — the ID is not being read, and
continuing creates a second Workshop item.

Restore the junction afterwards, as in step 6.

---

## The GitHub half

Independent of Steam and much simpler:

1. `git tag -a v0.9.0 -m "0.9.0 — first public beta"` then `git push origin v0.9.0`.
2. Create the release from that tag, mark it **pre-release**, paste the release notes.
3. Attach `dist/Intercolony-0.9.0.zip` as the release asset.
