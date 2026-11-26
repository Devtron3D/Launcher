# Launcher (version 0.3)

A portable Windows 10 / 11 launcher for a USB stick. A **profile** is a list of programs that start in order, for
example a set of test tools. Everything the launcher saves stays on the stick.

## Getting started

1. Copy **Launcher.exe** to the stick, for example next to a `Tools` folder.
2. Double-click it. On first start it creates `profiles.json` (three example profiles), `launcher-settings.json`
   and a `PortableData` folder next to itself.
3. To move your setup to another stick, copy `Launcher.exe`, `profiles.json` and `launcher-settings.json` together.

No installer is needed: .NET Framework 4.8 is part of Windows 10 and 11.

## Profiles

- **Launch** starts a profile's programs in order. Each program (step) starts:
  - **Immediately**,
  - **After a delay** of N seconds,
  - **When the previous one runs** (its window is open), or
  - **When the previous one has closed** (handy for setup scripts).

  Waiting for the previous program stops after 60 seconds by default; you can change that per step.
- Steps can **run as administrator** or **start minimized**.
- While a profile runs, its button shows **Running · 01:12**. Point at it to get **Stop**: during the launch it
  stops the launch, afterwards it closes that profile's programs (it asks first).
- **Several profiles can run at once.** A program that is already open from another profile is shared instead of
  started twice.
- The **⋯** button on a profile (or right-click) has: Edit, Duplicate, Move up/down, Export, Close its programs,
  Delete.
- **Export** saves a profile to `Exported profiles\<name>.json`; **Import** adds profiles from such a file.
- **Edit** opens the timeline editor. Drag a program's bar to set when it starts. **Enter** saves, **Esc** cancels.
- Pick an icon for each profile in the editor; without one it's guessed from the name.

## Finding programs

Paths are saved as `{tools}\...` (the tools folder) or `{app}\...` (the launcher's folder), so a new drive letter
doesn't matter. Set the tools folder in **Settings**.

The launcher never searches your drives by itself. When a program isn't where the profile says:

- **Before a launch** you're asked where it is now. You can use a suggested match, **Find it** yourself, **Look in a
  folder**, skip it for this launch, or not launch at all.
- **CHECK** in the title bar checks every profile at once, without launching anything.
- **Look in a folder** finds all missing programs in one folder (2 levels of subfolders deep) and lists them for you
  to confirm.

The program list is made for the keyboard: arrow keys move, **Enter** opens or picks, **Backspace** goes up,
**just type** to jump to a name, **Ctrl+F** lists every program in this folder and 2 levels below.

## The title bar

From left to right:

- **LAUNCHER**
- **The stick:** a ring showing how full it is, its name and the free space. Click it for details and actions:
  open it in Explorer, change the tools folder, empty the temp folder, and close everything and eject the stick.
  When the stick is almost full, the ring turns red and sends out red ripples.
- **▶ running / ▭ open:** how many profiles are running and how many programs are open.
- **CHECK:** check programs (in the compact layout this is in the ⋯ menu).
- **C / F:** switch between the compact and the full layout.
- **LIGHT / DARK:** switch the look.
- **⋯:** Settings, Import profiles, Show the log, Open launcher folder, Restart as admin.

Point at the stick to see warnings, such as when the launcher isn't running from a USB stick. A missing tools folder
gets an orange block; click it to fix it.

## Layouts and looks

- **Full:** a wide window with a card per profile and a status bar at the bottom.
- **Compact:** a narrow window with a list, a colour key and a 3-line log.
- **Looks:** Dark, Light, Green phosphor, or Match Windows (in **Settings**).

Running profiles get a green glow and a moving icon. This turns off in Settings, or when Windows' animations are off.

The small bar on each profile has one segment per program: green = found or running, red = missing,
amber = starting, outline = queued, striped = shared.

## Closing programs

- **Close all** (or **Close launched programs**) asks every program the launcher started to close. Programs that
  don't close within 5 seconds are listed, and you choose whether to force them (unsaved work is lost).
- Only programs the launcher started are closed, never your own windows of the same program.
- **Stop all** only stops launches in progress. Programs that already started stay open.

## Keeping the PC clean

- Launched programs use `PortableData\Temp` on the stick as their temp folder. It's emptied when the launcher
  starts (files in use are skipped), or from the stick panel.
- Optional (**Settings**): programs' AppData can go to the stick too.
- The log is kept in memory only.

Limits: programs that ignore these settings, or write to the registry, still write to the PC. Portable versions of
tools are the reliable choice. Windows itself always records that programs ran and that a stick was plugged in.

## Administrator rights

The launcher runs with normal rights. To give programs admin rights, either tick **Run as administrator** on a step
(one Windows prompt each), or use **Restart as admin** in the ⋯ menu (one prompt, then every program runs as admin).
A launcher without admin rights can't close programs that run as admin.

## Start from a shortcut

`Launcher.exe --run "Bench setup"` opens the launcher and starts that profile. Repeat `--run` for more.

## Building

1. Install the .NET SDK from https://dotnet.microsoft.com/download (or use Visual Studio 2022).
2. In this folder, run `dotnet build -c Release`. The first build needs internet once.
3. The program is `bin\Release\net48\Launcher.exe`. Only that file is needed. `Launcher.pdb` holds crash details.

The version number is set in `LauncherApp.csproj` (`<Version>`). To always start as admin, change `asInvoker`
to `requireAdministrator` in `app.manifest`.

For testing, a Debug build started with the environment variable `LAUNCHER_FAKE_FULL=1` shows the "almost full"
warning on any drive.

| File | What's in it |
|---|---|
| `MainForm.cs` | the main window, both layouts, all actions |
| `TitleBar.cs` | the title bar and the stick block |
| `ProfileViews.cs` | a profile as a card or a row |
| `MainLayout.cs` | the full layout's status bar |
| `EditorForm.cs` | the timeline editor |
| `PortableForms.cs` | the program list, Settings, and the missing-program windows |
| `LaunchEngine.cs` | starting, waiting for, tracking and closing programs |
| `Portable.cs` | settings, folders, `{tools}` / `{app}` paths, finding moved programs, the temp folder |
| `Models.cs` | profiles and steps, `profiles.json` |
| `Theme.cs` | colours, fonts, sizes for high-DPI screens |
| `Icons.cs`, `ProfileIconArt.cs` | the icons, drawn in code |
