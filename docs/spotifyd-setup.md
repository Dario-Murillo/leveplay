# spotifyd setup on Windows

[spotifyd](https://github.com/Spotifyd/spotifyd) is the audio engine: a small daemon that
shows up as a Spotify Connect device. Leveplay does not ship or modify it. Windows support
in spotifyd is experimental, so expect more friction than on Linux.

## 1. Get the binary

Check the spotifyd [Releases](https://github.com/Spotifyd/spotifyd/releases) page for a
Windows build first. If there is none, build it from source:

1. Install Rust with [rustup](https://rustup.rs) (MSVC toolchain).
2. Install the Visual Studio Build Tools with the "Desktop development with C++" workload.
3. From a clone of the spotifyd repository:

   ```bash
   cargo install --path . --locked --no-default-features --features rodio_backend
   ```

   This places `spotifyd.exe` in `%USERPROFILE%\.cargo\bin`.

## 2. Authenticate

```bash
spotifyd auth
```

The browser opens for the OAuth flow and the credentials are cached. Username/password
login is no longer supported by Spotify.

## 3. Run it and name the device

Use the same device name as `DeviceName` in Leveplay's settings:

```bash
spotifyd --no-daemon --device-name Leveplay
```

On Windows, spotifyd keeps its cache and log under `%LOCALAPPDATA%\spotifyd`.

## 4. Verify

With spotifyd running, the device must be listed by `GET /v1/me/player/devices`
(for example from the Spotify Web API console), and it should appear in the device
picker of the Spotify app on your phone.

## Known issue: `error audio key`, tracks skip one after another

**Symptom:** the device appears, playback can be transferred to it, but nothing is heard
and it jumps from track to track.

**What the log shows** (`%LOCALAPPDATA%\spotifyd\.spotifyd.log`), repeated per track:

1. `Loading <track>` and `Downloading file ...` succeed.
2. `error audio key 0 1`: Spotify refuses to hand over the decryption key.
3. `continuing without decryption`, followed by thousands of `skipping junk` /
   `invalid mpeg audio header` lines: the decoder is fed encrypted data.
4. `end of stream`, then `Skipping to next track`.

Authentication, Connect discovery, transfer and download all work; only the key request
fails, and that depends entirely on Spotify's servers. No flag or setting fixes it.

**What to do:**

- Follow the upstream report in librespot (issue #1649 at the time of writing). When a
  fix lands, rebuild spotifyd; nothing in Leveplay needs to change.
- Try another Premium account: reports suggest only some accounts are affected.
- Keep developing against a phone or the official desktop app as the Connect device.
  Leveplay only speaks to the Web API, so it does not care which device plays the audio.

The files under `%LOCALAPPDATA%\spotifyd\cache` downloaded during failed attempts cannot
be decrypted and can be deleted.

## Later: run as a service

Once playback works, spotifyd can be installed as a Windows service with
[NSSM](https://nssm.cc) so it starts with Windows without a console window.
