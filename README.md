# bp_only_tv

Keep Steam Big Picture on the TV only.

When Big Picture opens, every display except the TV is switched off and the TV
becomes the primary display, so Big Picture, the games and emulators you launch
from it all land on the TV instead of the monitor or spanned across both. When
Big Picture closes, your normal desktop layout comes back.

Windows 10 and 11, Windows PowerShell 5.1 (built in). No admin rights needed.

## Install

1. Download or clone this repo.
2. Turn the TV on so Windows can see it.
3. Open PowerShell in the repo folder and run:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\Install.ps1
   ```

4. Pick your TV from the list (type part of its name, e.g. `LG TV`).

That's it. A hidden background task starts now and at every sign-in.

To also keep the TV-only layout while an emulator runs outside Big Picture:

```powershell
powershell -ExecutionPolicy Bypass -File .\Install.ps1 -TvName "LG TV" -ExtraProcesses retroarch,dolphin
```

## Uninstall

```powershell
powershell -ExecutionPolicy Bypass -File .\Install.ps1 -Uninstall
```

## Running it by hand

```powershell
.\BigPictureTV.ps1 -ListDisplays        # show display names
.\BigPictureTV.ps1 -TvName "LG TV"       # watch for Big Picture (Ctrl+C to stop)
.\BigPictureTV.ps1 -TvName "LG TV" -TvOnly   # switch to the TV right now
.\BigPictureTV.ps1 -Restore              # put the saved layout back
```

## If you get stuck on the TV

Press `Win+P` and choose **Extend**, or run `.\BigPictureTV.ps1 -Restore`.
The watcher also restores a leftover layout the next time it starts.

## BigPictureTV.exe (preview tray app)

`BigPictureTV.exe` does the same as the watcher, from an icon next to the clock.
It finds the TV on its own. No install and no admin rights needed: download it
and double-click it.

- The icon is grey on the desktop and blue while only the TV is on.
- Double-click the icon to switch to the TV, or back to the desktop.
- The menu also has: pause automatic switching, choose the TV (or leave it on
  automatic detection), start with Windows, and open the log folder.
- Exiting always puts the desktop back.

If you installed the PowerShell watcher with `Install.ps1`, uninstall it first
(`Install.ps1 -Uninstall`). Only one of them can run at a time.

The menu is in Spanish or English, following the Windows display language.

## bptv.exe (preview)

A C# version of the same tool lives in `src/`. It is the core of an upcoming
tray app. It finds the TV on its own, so you don't have to type its name.

```powershell
bptv list              # displays, and which one it thinks is the TV
bptv select 2          # optional: pick the TV yourself (number from the list)
bptv tv-only           # switch to the TV now
bptv restore           # put the desktop back
bptv watch             # switch automatically while Big Picture is open (Ctrl+C to stop)
```

`watch` also accepts `--extra retroarch,dolphin`, `--grace 5` and `--poll 2`. It
shares its files with the PowerShell script, so either one can restore a layout
the other saved. Don't run both at once; the second one exits.

Build it with the .NET 8 SDK: `dotnet publish src/BigPictureTV.Cli -c Release -r win-x64 -o publish`.
Every push also builds both exes in GitHub Actions (artifacts `BigPictureTV-win-x64` and `bptv-win-x64`).

## How it works

- Every 2 seconds it looks for a visible window titled **Steam Big Picture Mode**.
- On open it saves the current layout to
  `%LOCALAPPDATA%\BigPictureTV\saved-layout.json`, then uses the Windows
  display configuration API (`SetDisplayConfig`) to keep only the TV active at
  position (0,0), which makes it primary.
- After Big Picture has been closed for 5 seconds it reapplies the saved
  layout. If that fails (for example after a reboot or driver update changed
  the adapter IDs), it falls back to Windows' last remembered **Extend** layout.
- Log: `%LOCALAPPDATA%\BigPictureTV\BigPictureTV.log`.

Options: `-PollSeconds`, `-GraceSeconds`, `-ExtraProcesses`. See
`Get-Help .\BigPictureTV.ps1 -Full`.
