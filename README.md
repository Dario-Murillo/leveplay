# Leveplay

A lightweight Spotify player for Windows that lives in the system tray. It is meant for
low-end PCs or for use while gaming, where the official desktop client is too heavy.

There is no main window: a tray icon shows what is playing, a click plays or pauses, and
the music comes out of the computer Leveplay runs on.

## How it works

Leveplay has two separate pieces:

| Piece        | Role                                                               | Technology                          |
| ------------ | ------------------------------------------------------------------ | ----------------------------------- |
| Audio engine | The Spotify Connect device: authenticates, streams and plays audio | [Spotify Soloist] (external binary) |
| Controller   | Starts the engine, sends playback commands and shows state         | C# / .NET, WinForms tray icon       |

The controller never touches audio. It talks to the Spotify Web API over HTTPS, and
Spotify relays the commands to whichever Connect device is active. Because of that it
can control **any** Connect device (a phone, another computer), not only the local
engine, and it keeps working as a remote control when the engine is not installed.

Soloist is Spotify's official headless Connect client. Leveplay starts it hidden when it
launches and stops it on exit, so the two behave as one application.

A Spotify Premium account is required.

## Status

| Milestone                                                         | State       |
| ----------------------------------------------------------------- | ----------- |
| 1. Sign-in (Authorization Code + PKCE, silent on later starts)    | Implemented |
| 2. Find the configured device and transfer playback to it         | Implemented |
| 3. Tray menu (Play/Pause, Next, Previous) and now-playing tooltip | Implemented |
| 4. Local playback through a managed audio engine                  | Implemented |
| 5. Global hotkeys, auto-start, Native AOT packaging               | Planned     |

## Setup

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download).
2. Register an app in the [Spotify Developer Dashboard](https://developer.spotify.com/dashboard)
   and add `http://127.0.0.1:5543/callback` as a Redirect URI.
3. Copy `src/Leveplay.Tray/appsettings.local.example.json` to
   `src/Leveplay.Tray/appsettings.local.json` and set your `ClientId`.
   This file is git-ignored; no client secret is needed (PKCE).
4. Install the audio engine: see [docs/soloist-setup.md](docs/soloist-setup.md).
   Leveplay runs without it, but then it can only control other devices.

## Build, test, run

```bash
dotnet build
dotnet test
dotnet run --project src/Leveplay.Tray
```

On the first run the browser opens for Spotify sign-in, and Leveplay asks for the Soloist
API key if the engine is installed. Both secrets are stored encrypted (DPAPI, current
Windows user) under `%APPDATA%\Leveplay`; later starts need no interaction.

## Using it

- **Left click** the tray icon to play or pause.
- **Right click** for the menu: Play/Pause, Next, Previous, and a `Devices` submenu
  that lists every online Spotify Connect device and switches playback on click.
- **Hover** the icon to see what is playing.
- **Audio engine** submenu: a three-step checklist (get Soloist, set the API key, start)
  that shows what is left to do and lets you restart the engine.

If nothing has been played yet, Play starts `DefaultContextUri` on the configured device.
A diagnostic log is kept at `%APPDATA%\Leveplay\leveplay.log` (menu: "Open log").

## Configuration

Defaults are in `appsettings.json`; override them in `appsettings.local.json`.

| Key                   | Default    | Meaning                                                        |
| --------------------- | ---------- | -------------------------------------------------------------- |
| `ClientId`            | (required) | Client ID of your Spotify app                                  |
| `RedirectPort`        | `5543`     | Loopback port for the OAuth callback; must match the Dashboard |
| `DeviceName`          | `Leveplay` | Connect device to control; also the name the engine announces  |
| `DefaultContextUri`   | `null`     | Playlist/album URI to start when nothing is playing            |
| `PollIntervalSeconds` | `5`        | How often player state is polled                               |
| `EnginePath`          | `null`     | Path to `soloist.exe`, only needed when it is not in `%LOCALAPPDATA%\Leveplay\engine` |

To control a different device by default (for example a phone), set `DeviceName` to the
name shown in the `Devices` submenu.

## Footprint

Measured on Windows 11 while playing, with a Debug build started through `dotnet run`:

| Process  | Working set | Private memory |
| -------- | ----------- | -------------- |
| Leveplay | 116 MB      | 28 MB          |
| Soloist  | 56 MB       | 34 MB          |

The controller is currently the heavier of the two because a Debug build loads the full
.NET runtime and JIT. Reducing that is the goal of the Native AOT packaging milestone.

## Repository layout

```
src/Leveplay.Core/         Auth, config, devices, playback and engine management. No UI.
src/Leveplay.Tray/         WinForms tray application (NotifyIcon, no main window).
tests/Leveplay.Core.Tests/ xUnit tests for Leveplay.Core.
docs/soloist-setup.md      How to install and pair the audio engine.
```

## Disclaimer

Leveplay is not affiliated with Spotify. It uses the official Spotify Web API and runs
Spotify Soloist unmodified. Soloist is not included in this repository or in any Leveplay
package: Spotify does not allow its binaries to be redistributed, so each user downloads
it from Spotify and creates their own API key.

[Spotify Soloist]: https://developer.spotify.com/documentation/soloist
