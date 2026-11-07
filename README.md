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

A tap on the controller's **Share/View** button (or the keyboard shortcut) opens
the **TV menu** on the TV from anywhere: the desktop, Big Picture or a game. It
has big tiles for YouTube, Crunchyroll, Big Picture and the desktop. With the
menu open, the same button goes back to the app you were using; if there is
none, the menu stays on screen (the Desktop tile or B goes back to the
desktop). The tray icon opens it too. The buttons (one or several, tapped or
held up to 5 s) can be changed or turned off in Settings → Advanced options →
TV menu.

- **In the menu:** the cross or left stick moves, A opens, B goes back.
- **Like a console:** going back to the menu doesn't close the app. It stays
  open behind it, its tile says "Open", A goes back to it and X closes it.
  Several can be open at once (music while you play, for example). Going back
  to the desktop (from the menu, the shortcut or the tray) closes them all,
  music too. Opening the menu over a game (or any other window) adds a green
  tile with its name first, which shows the open windows to pick the one to go
  back to (A twice goes back to the first); B and the menu button go back to
  the game too when it was the one in front. When TV mode starts, the menu
  opens or closes, or an app opens from it, the mouse pointer moves to the
  bottom right corner, out of the way.
- **On a web page:** it opens full screen in Microsoft Edge, with TV menu
  profiles of its own (sign-ins to the sites are kept; they never sign in to
  your Microsoft account or sync, so your everyday extensions and settings
  stay out). Plain pages like Crunchyroll share one, so the navigation
  extension is set up once for all of them; YouTube and the music tiles keep
  their own. The app drives the page directly: cross = arrows, A = Enter,
  B = back, Y = search, Start = pause.
  - **YouTube** uses its TV interface (youtube.com/tv), made for a controller,
    with its own keyboard. The buttons do what they do in the PS5 app: LB/RB =
    previous/next video, LT/RT = rewind/fast forward, Y/Triangle opens search
    (a space inside it), X/Square deletes, and on the keyboard LT/RT jump 4
    keys.
  - **Other pages**, like Crunchyroll, use a small extension: the cross moves
    an orange focus box between links and buttons, A opens the one chosen,
    Y goes to the page's search, LB/RB move between items and LT/RT scroll.
    Inside an embedded video player the cross goes to the player. With a video
    on the page, LT/RT go back and forward 10 s and X/Square makes the player
    fill the window, where the cross seeks and A pauses. Edge installs the
    extension from its store by itself, turned off until you turn it on: the
    first time a page needs it, the app opens a normal window of that profile
    where one click turns it on (or adds it from the store). It only acts in
    TV menu windows.
- **On-screen keyboard:** on those other pages, Y also brings up a keyboard at
  the bottom of the TV, and R3 (right stick click) opens or closes it over any
  text box, for example to sign in. It works like a console's: the cross picks
  a key, A types it, X deletes, Y types a space, LT capitals (one letter), RT
  symbols and accents, LB/RB move the cursor, Start sends Enter and closes it,
  B closes it. If R3 is the TV menu button, only Y opens it.
- **Music and sound output:** the Spotify (its desktop app if installed, else
  the web player) and YouTube Music tiles keep playing when you go back to the
  menu, Big Picture or a game. Under the tiles, the menu shows what the PC is
  playing (from any player, with its cover) and the sound output in use. Press
  down to reach them: A on the music plays or pauses, A on the output moves
  the sound to the next one (TV, headphones, speakers). LB/RB skip and Start
  pauses from anywhere in the menu.
- **Volume:** the right stick turns only the volume of the music opened from
  the TV menu (its own slider in Windows' volume mixer, so games, videos and
  other apps keep theirs; closing it gives back the volume it had, so Edge or
  Spotify aren't left low), in the menu or over the menu's apps (in a game the
  stick is the game's), and shows the percentage at the top of the screen. On
  the sound output it turns the whole PC's volume.
- **Controllers:** Xbox, PlayStation (DualShock 4, DualSense), Switch and
  others.

### From Big Picture

Add `BigPictureTV.exe` as a non-Steam game, with `--menu` as its launch option.
Opening it from Big Picture shows the TV menu, and Steam counts it as a game
running until the menu closes. In that shortcut's controller settings, choose
the **Gamepad** template (not the desktop one), so Steam doesn't send keys on
top of the app.

### Editing the tiles

They live in `%LOCALAPPDATA%\BigPictureTV\settings.json`, under `TvMenuApps`.
Each has a `Name`, a `Kind` (`Web`, `Program`, `BigPicture` or `Desktop`), a
`Target` (web address or program path), and optionally `Arguments`,
`UserAgent`, `BackKey` (the key for B, like `Esc` or `Alt+Left`), `SearchKey`
(for Y), `Color` (`#RRGGBB`) and `Icon` (an image, a program or a web address
for the logo). Changes show the next time the menu opens.

Each tile shows its logo on its own: the icon the page declares (kept in
`%LOCALAPPDATA%\BigPictureTV\Icons`; if the page doesn't answer, say behind a
bot check, Google's icon service is asked), or the program's icon (Spotify,
Steam for Big Picture, the game in front). `Icon` picks another.

## Stuck on the TV?

Press `Ctrl+Alt+Shift+F12`. If the app isn't running, press `Win+P` and choose
**Extend**. The app also restores a leftover layout the next time it starts, if the displays are still on the TV (if the desktop is already back, it is left as it is).

## FAQ

**Does it send anything over the internet?** No telemetry. It connects to:

- GitHub, once a day, to see if there is a new version (can be turned off).
  It never downloads or installs a new version by itself.
- The sites of the TV menu's web tiles, when you open them, and their icons:
  from the site, or from Google's icon service (`www.google.com/s2/favicons`)
  if the site doesn't answer.
- Microsoft's Edge Add-ons store, where Edge gets the navigation extension
  for the TV menu's web pages.

Without the TV menu, only the new-version check.

**Where does it keep its files?** `%LOCALAPPDATA%\BigPictureTV`: the settings
(`settings.json`, and `settings.json.bak` from before the last change), the
saved desktop layout while on the TV, the TV menu's browser profiles and
icons, and a log (`BigPictureTV.log`, about 1 MB at most, plus the previous
one as `BigPictureTV.old.log`).

**How do I uninstall it?** Untick **Start with Windows**, exit from the menu,
and delete the exe and the `%LOCALAPPDATA%\BigPictureTV` folder.

**Big Picture isn't detected.** It looks for a Steam window with *Big
Picture* in its title, which Steam keeps in most languages, and for the titles
in `BigPictureTitles` in `settings.json` (*Steam Big Picture Mode* and *Steam
Big Picture*). If your Steam language calls it something else, add that title
there and open an issue so it can be built in.

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
