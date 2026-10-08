# BigPictureTV

Keep Steam Big Picture on the TV, and only on the TV.

*[Leer en español](README.es.md)*

[![Buy Me a Coffee](https://img.shields.io/badge/Buy%20Me%20a%20Coffee-support-FFDD00?logo=buymeacoffee&logoColor=black)](https://buymeacoffee.com/emarinelli)

When Big Picture opens, BigPictureTV switches your displays so Big Picture, and
the games and emulators you launch from it, land on the TV instead of the
monitor or spanned across both. When Big Picture closes, your normal desktop
comes back.

It runs as a small icon next to the clock. Windows 10 and 11, any graphics card
(NVIDIA, AMD, Intel). No install and no admin rights needed.

## Download

1. Get **BigPictureTV.exe** from the [latest release](https://github.com/emarinelli26/bp_only_tv/releases/latest).
2. Turn the TV on so Windows can see it, and double-click the exe.
3. The settings window opens with the display it thinks is the TV. Check it
   and press **Test**.

Windows may warn that the app is from an unknown publisher, because it isn't
code-signed. Click **More info → Run anyway**. Every release is built by
GitHub Actions from the code in this repo, with its SHA-256 listed next to it.

## What it does

- Switches to the TV when Big Picture opens, and back when it closes (after a
  few seconds, with a notice, in case you reopen it).
- Stepping away from Big Picture (Windows key, Alt+Tab) keeps the TV. Only
  closing it brings the desktop back, unless you change that in the settings.
- `Ctrl+Alt+F12` (or double-clicking the icon) switches between the TV and the
  desktop by hand, from anywhere, Big Picture included.
- `Ctrl+Alt+Shift+F12` always brings the desktop back.
- **Open Big Picture on the TV** in the menu switches first, so Steam starts on
  the TV.
- If Windows turns the other displays back on by itself (TV standby, Win+P),
  the app notices and follows along.
- Exiting the app always puts the desktop back.

The icon is grey on the desktop and blue while on the TV. The app follows the
Windows display language (English or Spanish).

## Settings

Right-click the icon → **Settings…**

- **Which display is the TV.** Automatic by default; **Identify displays**
  shows a big number on each screen.
- **What switching does:** only the TV (default), the TV as the main display
  with the others still on, or the same picture on every display.
- **Keyboard shortcut:** click the box and press the combination.
- **Start with Windows.**

**Advanced options:**

- **Sound:** move the sound to the TV too, and back afterwards.
- **Controller combo** (off by default): one button or several pressed together
  to switch, held from a quick tap up to 5 s (1.5 s by default). Press
  **Record**, press them and let go, for example both sticks (`LS+RS`). Avoid Back, Start and Home: controllers use those for
  their own shortcuts (a GameSir Nova Lite changes mode with Back+Start+LB).
- Make the shortcut and the controller combo open Big Picture as well.
- Whether stepping away from Big Picture goes back to the desktop.
- How long to wait after Big Picture closes, and programs (emulators) that
  should also use the TV.
- A notice when a new version is out.

## TV menu

A tap on the controller's **Share/View** button (or the keyboard shortcut) opens the **TV menu** on the TV from anywhere: big tiles for YouTube, Crunchyroll, Big Picture and the desktop. The tray icon opens it too, and the buttons (one or several, tapped or held up to 5 s) can be changed or turned off in Settings. Like on a console, going back to the menu leaves the app running: its tile says "Open", A goes back to it and X closes it. Web tiles open full screen in Microsoft Edge with a profile of their own, and the app drives the page directly like a console app: cross = arrows, A = Enter, B = back, Y = search, Start = pause, and on YouTube the buttons do what they do in the PS5 app: LB/RB = previous/next video, LT/RT = rewind/fast forward, Y/Triangle opens search (a space inside it), X/Square deletes, and on the keyboard LT/RT jump 4 keys (on other pages, like Crunchyroll, a small extension loaded only into the TV menu's browser profiles moves an orange focus box between links and buttons with the cross, A opens the one chosen, Y goes to the page's search, LB/RB move between items and LT/RT scroll; inside an embedded video player the cross goes to the player; with a video on the page, LT/RT go back and forward 10 s and X/Square makes the player fill the window, where the cross seeks and A pauses). On those pages Y also brings up an on-screen keyboard at the bottom of the TV, and R3 (right stick click) opens or closes it over any text box, for example to sign in: the cross chooses a key, A types it, X deletes, Y types a space, LT capitals (one letter), RT symbols and accents, LB/RB move the cursor, Start sends Enter and closes it, B closes it. Spotify (its desktop app if installed, else the web player) and YouTube Music tiles keep playing behind the menu, Big Picture and games, and under the tiles the menu shows what the PC is playing (from any player, with its cover) and the sound output in use: press down to reach them, A on the music plays or pauses, A on the output moves the sound to the next one (TV, headphones, speakers), and LB/RB skip and Start pauses from anywhere in the menu. The right stick turns only the music's volume (its own slider in Windows' volume mixer, so games and videos keep theirs), in the menu or over any app, showing the percentage at the top of the screen; on the sound output it turns the whole PC's volume. Xbox, PlayStation and Switch controllers work. To open it from Big Picture, add `BigPictureTV.exe` as a non-Steam game with `--menu` as its launch option, and give it the Gamepad controller template. Tiles live in `TvMenuApps` in `settings.json`; [README.es.md](README.es.md#menú-tv) describes the fields.

## Stuck on the TV?

Press `Ctrl+Alt+Shift+F12`. If the app isn't running, press `Win+P` and choose
**Extend**. The app also restores a leftover layout the next time it starts.

## FAQ

**Does it send anything over the internet?** Only the optional new-version
check, which asks GitHub for the latest release once a day. No telemetry. It
never downloads or installs anything by itself.

**Where does it keep its files?** `%LOCALAPPDATA%\BigPictureTV`: settings, the
saved desktop layout while on the TV, and a log.

**How do I uninstall it?** Untick **Start with Windows**, exit from the menu,
and delete the exe and the `%LOCALAPPDATA%\BigPictureTV` folder.

**Big Picture isn't detected.** It looks for windows titled *Steam Big Picture
Mode* or *Steam Big Picture*. If your Steam language uses another title, add it
to `BigPictureTitles` in `settings.json` and open an issue so it can be built in.

**Something went wrong.** Right-click the icon → **Copy diagnostic info**, and
paste it in a [new issue](https://github.com/emarinelli26/bp_only_tv/issues/new/choose).

## Other ways to run it

**PowerShell script** (the original version, no exe):

```powershell
powershell -ExecutionPolicy Bypass -File .\Install.ps1          # pick the TV, runs at every sign-in
powershell -ExecutionPolicy Bypass -File .\Install.ps1 -Uninstall
.\BigPictureTV.ps1 -Restore                                     # put the saved layout back
```

Run only one of the script or the app at a time.

**Command line** (`bptv.exe`, in each release):

```powershell
bptv list        # displays, and which one it thinks is the TV
bptv select 2    # pick the TV yourself
bptv tv-only     # switch to the TV now
bptv restore     # put the desktop back
bptv watch       # switch automatically while Big Picture is open
```

## Building

With the .NET 8 SDK:

```powershell
dotnet test
dotnet publish src/BigPictureTV.App -c Release -r win-x64 -o publish
```

Every push builds both exes in GitHub Actions. Pushing a tag such as `v1.0.0`
creates a draft release with them.

## How it works

On open it saves the current display layout and uses the Windows display
configuration API (`SetDisplayConfig`) to apply the TV layout. On close it puts
the saved layout back, falling back to Windows' last **Extend** layout if the
saved one no longer applies (for example after a driver update).

## License

[MIT](LICENSE)
