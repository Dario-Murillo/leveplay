# Audio engine setup: Spotify Soloist

[Spotify Soloist](https://developer.spotify.com/documentation/soloist) is Spotify's
official headless Connect client: a command-line program with no interface that shows up
as a Spotify Connect device and plays audio on the machine it runs on. Leveplay uses it
as its audio engine and manages its lifetime, so after this one-time setup you never
start it by hand.

Requirements: Windows on x86_64 and a Spotify Premium account.

## 1. Download Soloist

Soloist is not shipped with Leveplay, because Spotify does not allow its binaries to be
redistributed.

1. In Leveplay, right click the tray icon and choose **Audio engine → 1. Get Soloist…**.
   This opens the Soloist documentation and the folder Leveplay looks in:
   `%LOCALAPPDATA%\Leveplay\engine`.
2. Download the Windows build (`soloist_release_windows_x86_64.zip`) from Spotify.
3. Extract `soloist.exe` into that folder.

To keep it somewhere else, set `EnginePath` in `appsettings.local.json` to the full path
of `soloist.exe`.

## 2. Create an API key

1. Open the [Soloist dashboard](https://developer.spotify.com/dashboard/soloist) and
   create an API key. The account that creates it must have Premium.
2. In Leveplay, choose **Audio engine → 2. Set API key…** and paste it. On a first run
   with Soloist installed this dialog opens by itself.

The key is stored encrypted with DPAPI in `%APPDATA%\Leveplay\engine-key.bin`. It is not
written to any settings file or to the log. Treat it like a password: do not commit it,
share it or paste it into scripts. If it leaks, revoke it in the dashboard and set a new
one.

As an alternative, Leveplay reads the key from the `SOLOIST_API_KEY` environment
variable when none has been saved in the app.

## 3. Pair it with your account (first time only)

Soloist does not open a browser or ask for a password. The first time it runs it waits
to be selected from a Spotify app:

1. With Leveplay running, open Spotify on a phone or computer on the same network.
2. Open the device picker and choose the device named `Leveplay`.

Soloist stores the session in `%LOCALAPPDATA%\soloist\data` and restores it on every
later start, so this is not needed again.

## 4. Verify

- The **Audio engine** submenu shows `Audio engine (running)` with all three steps ticked.
- The tooltip reads `connected to Leveplay`.
- Play starts music on this computer.

The device name comes from `DeviceName` in the settings. Leveplay passes it to Soloist
and then looks for a Connect device with that same name, so the two always match.

## Updating: builds expire after 90 days

Each Soloist build stops working 90 days after it was built. When that happens Soloist
exits with code 10 and Leveplay reports that the build has expired.

1. Choose **Audio engine → 1. Soloist installed (update…)**.
2. Download the current build and replace `soloist.exe` in the engine folder.
3. Choose **Audio engine → 3. Start**.

The log shows how long the current build has left, for example
`engine: client expires in 89 days`.

## Troubleshooting

| Symptom | What to check |
| --- | --- |
| `Audio engine (not installed)` | `soloist.exe` is not in the engine folder and `EnginePath` is not set |
| `Audio engine (API key needed)` | No key saved yet: use step 2 of the submenu |
| `Device not found` after the engine starts | The engine has not been paired yet (section 3), or the key was rejected: see the log |
| `Audio engine stopped` | The message includes the exit code; code 10 means the build expired |
| The engine keeps running after Leveplay closes | Leveplay was killed instead of exited. The next start reuses that engine but will not stop it on exit; end `soloist.exe` once from Task Manager |

Everything Soloist prints, except its periodic progress lines, goes to
`%APPDATA%\Leveplay\leveplay.log` with the prefix `engine:`.

## Running Soloist yourself

Leveplay only starts the engine when none is running. If you prefer to run it by hand,
use the same device name:

```bash
.\soloist.exe --device-name "Leveplay" --api-key "$env:SOLOIST_API_KEY"
```

Leveplay will detect it, use it and leave it running on exit.

## Why not spotifyd

Leveplay was first designed around [spotifyd](https://github.com/Spotifyd/spotifyd),
which is built on librespot, an unofficial implementation of the Connect protocol. For
some accounts librespot cannot obtain the key that decrypts the audio
(`error audio key`, librespot issue #1649): tracks download but are skipped without
sound. That was still the case with spotifyd 0.4.2 and librespot 0.8.0. Soloist does not
have this problem and is within Spotify's terms, so it replaced spotifyd as the engine.

Since the controller only talks to the Web API, spotifyd or any other Connect device
still works as a target if it is running under the configured `DeviceName`.
