# Leveplay

A lightweight Spotify controller for Windows that lives in the system tray. It is meant
for low-end PCs or for use while gaming, where the official Electron-based client is too
heavy. The target footprint is tens of MB, not hundreds.

## How it works

Leveplay never touches audio. It has two separate pieces:

| Piece        | Role                                                              | Technology                         |
| ------------ | ----------------------------------------------------------------- | ---------------------------------- |
| Audio engine | The Spotify Connect device: authenticates, downloads, plays audio | [spotifyd] (external binary)       |
| Controller   | Sends playback commands and shows state                           | C# / .NET, WinForms tray icon      |

The controller talks to the Spotify Web API over HTTPS, and Spotify relays the commands
to whichever Connect device is active. The two local processes never talk to each other.
Because of that, the controller works with **any** Connect device (a phone, the official
desktop app), not only spotifyd.

A Spotify Premium account is required: the Web API player endpoints and spotifyd both
depend on it.

## Status

| Milestone                                                      | State       |
| -------------------------------------------------------------- | ----------- |
| 1. Sign-in (Authorization Code + PKCE, silent on later starts) | Implemented |
| 2. Find the configured device and transfer playback to it      | Implemented |
| 3. Tray menu (Play/Pause, Next, Previous) and now-playing tooltip | Implemented |
| 4. Global hotkeys, auto-start, Native AOT packaging            | Planned     |

> **Known issue with spotifyd:** playback through spotifyd/librespot currently fails for
> some accounts with `error audio key` (tracks are skipped one after another). This is an
> upstream problem, not a Leveplay one. Until it is fixed, develop and test against a
> phone or the official app. See [docs/spotifyd-setup.md](docs/spotifyd-setup.md).

## Setup

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download).
2. Register an app in the [Spotify Developer Dashboard](https://developer.spotify.com/dashboard)
   and add `http://127.0.0.1:5543/callback` as a Redirect URI.
3. Copy `src/Leveplay.Tray/appsettings.local.example.json` to
   `src/Leveplay.Tray/appsettings.local.json` and set your `ClientId`.
   This file is git-ignored; no client secret is needed (PKCE).

## Build, test, run

```bash
dotnet build
dotnet test
dotnet run --project src/Leveplay.Tray
```

On the first run the browser opens for Spotify sign-in. The refresh token is then stored
encrypted (DPAPI, current Windows user) in `%APPDATA%\Leveplay\token.bin`, and later
starts sign in without a browser. Hover the tray icon to see the current status.

## Using it

- **Left click** the tray icon to play or pause.
- **Right click** for the menu: Play/Pause, Next, Previous, and a `Devices` submenu
  that lists every online Spotify Connect device and switches playback on click.
- **Hover** the icon to see what is playing.

If nothing has been played yet, Play starts `DefaultContextUri` on the configured device.
A diagnostic log is kept at `%APPDATA%\Leveplay\leveplay.log` (menu: "Open log").

## Configuration

Defaults are in `appsettings.json`; override them in `appsettings.local.json`.

| Key                   | Default    | Meaning                                                      |
| --------------------- | ---------- | ------------------------------------------------------------ |
| `ClientId`            | (required) | Client ID of your Spotify app                                |
| `RedirectPort`        | `5543`     | Loopback port for the OAuth callback; must match the Dashboard |
| `DeviceName`          | `Leveplay` | Name of the Connect device to control (spotifyd `device_name`) |
| `DefaultContextUri`   | `null`     | Playlist/album URI to start when nothing is playing          |
| `PollIntervalSeconds` | `5`        | How often player state is polled                             |
| `EnginePath`          | `null`     | Path to `soloist.exe`. When set, Leveplay starts and stops the audio engine itself |

## Repository layout

```
src/Leveplay.Core/         Auth, config and (later) device/playback logic. No UI.
src/Leveplay.Tray/         WinForms tray application (NotifyIcon, no main window).
tests/Leveplay.Core.Tests/ xUnit tests for Leveplay.Core.
docs/spotifyd-setup.md     How to build, authenticate and run spotifyd on Windows.
```

## Disclaimer

Leveplay itself only uses the official Spotify Web API. spotifyd is built on librespot,
an unofficial open-source implementation of the Spotify Connect protocol; using it is
outside Spotify's Terms of Service. This project is not affiliated with Spotify.

[spotifyd]: https://github.com/Spotifyd/spotifyd
