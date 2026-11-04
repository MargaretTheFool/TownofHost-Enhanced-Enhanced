# Custom sound settings

The custom sound GUI controls playback on the current client. A sound sent to another player is still subject to that recipient's local custom-sound switch and volume settings; the sender cannot override them.

Open the GUI from the main menu sound icon or from Settings → TOHE Options → Custom Sound Settings, including in the lobby and during a game. It opens above TOHE Options as a modal panel, leaving the settings underneath. The TOHE Options entry remains available when custom sound effects are disabled so the settings can still be edited; playback and previews remain disabled until the switch is turned back on.

## Sound IDs and resources

Use the resource file's basename, without its extension, as the sound ID. IDs are case-sensitive and are part of the reliable RPC payload, so keep an ID stable when updating the sound. The current resource IDs are `AWP`, `Bet`, `Bite`, `Boom`, `Clothe`, `Congrats`, `Curse`, `Dove`, `Eat`, `FlashBang`, `Gunfire`, `Gunload`, `Line`, `MarioCoin`, `MarioJump`, `Oiiai`, `Onichian`, `Shield`, and `Teleport`. To add an ID, place `<id>.raw` under `Resources/Sounds` and add the ID to `CustomSoundsManager.EffectIds` in `Modules/CustomSounds.cs`. The embedded resource name is `TOHE.Resources.Sounds.<id>.raw`. Use the same ID for GUI rows, previews, local playback, and RPC calls. Do not derive the ID from a translated display name.

The bundled samples use 48 kHz, two-channel, signed 32-bit little-endian PCM with no WAV/container header. Convert an input file to headerless PCM with FFmpeg:

```sh
ffmpeg -i input.wav -ar 48000 -ac 2 -c:a pcm_s32le -f s32le Resources/Sounds/<id>.raw
```

The loader expects this exact format (48,000 samples/second, 2 channels, 32-bit signed little-endian) because the file has no header to inspect.

## Loading and audio lifetime

Playback calls must run on Unity's main thread. The manager decodes each embedded resource into interleaved float samples and creates its `AudioClip` on first use; it caches clips for the lifetime of the plugin. The clip length is measured in frames per channel, so the interleaved sample count is divided by two. GUI previews and game RPCs share the cached clip. The sound sources belong to `SoundManager` but play directly through the audio listener instead of the game's SFX mixer channel; closing the panel stops its preview without destroying the shared clips.

## Playback and authority

The existing playback API names and responsibilities are:

- `CustomSoundsManager.Play(string sound)` plays locally on the calling client and respects the individual sound's enabled flag. RPC handlers use it after receiving a sound ID.
- `PlayerControl.RPCPlayCustomSound(string sound, bool force = false)` requests playback for that player's client. By default, the caller must be the host and the target player must be modded. `force: true` bypasses those host/modded-target checks; reserve it for trusted internal callers. Passing the local player plays locally without sending a targeted RPC.
- `CustomSoundsManager.RPCPlayCustomSoundAll(string sound)` is host-only. It sends the sound ID to all recipients and also plays it on the host.
- `CustomSoundsManager.ReceiveRPC(MessageReader reader)` reads the received ID and invokes local playback. The RPC dispatcher invokes this only on the local recipient.

These methods send a sound ID, not audio data or volume values. Each receiving client resolves the ID from its local registered resources and applies its own settings. A recipient needs the sound resource/registration locally to hear it.

```csharp
CustomSoundsManager.Play("Teleport");                  // This client only
player.RPCPlayCustomSound("Teleport");                // Host to one modded player
CustomSoundsManager.RPCPlayCustomSoundAll("Boom");    // Host broadcasts and plays locally
```

## Local volume settings

`TOHEE-DATA/CustomSoundsVolumeConfig.json` stores per-client settings:

```json
{
  "version": 1,
  "masterVolume": 100,
  "effects": {
    "Boom": { "enabled": true, "volume": 100 },
    "Teleport": { "enabled": false, "volume": 100 }
  }
}
```

`version` identifies the config schema. `masterVolume` is the client-wide percentage. Each `effects` entry is keyed by stable sound ID and contains an `enabled` flag and a `volume` percentage. Percentage values are non-negative 32-bit integers; values above 100 are valid. Unlisted effects use their default enabled state and volume.

Effective gain is the product of the master and per-effect percentages:

```text
effective gain = masterVolume / 100 × effect.volume / 100
```

For example, master 80 and effect 50 produce gain 0.4, while master 200 and effect 200 produce gain 4. The game's SFX volume does not control custom sounds. `PlayOneShot` applies the custom gain, so values over 100 amplify the signal; boosting a loud source can cause clipping or audible distortion.

The GUI slider covers 0–100%. Clicking a numeric field focuses it and clears the displayed value so typing replaces it. The field accepts an integer from 0 to 500; Enter or leaving the field commits it. Leaving it empty or entering invalid input restores the last valid value. For a typed value above 100, the slider remains at 100 while the field shows the actual value. Dragging the slider sets a value in its 0–100 range. Changing a sound's volume previews it; changing the master previews the most recently selected sound, initially Teleport. Each preview stops the previous preview. A per-effect disabled state prevents ordinary playback but does not block an explicit preview. The original TOHE Options custom-sound switch takes precedence and stops or suppresses all custom sounds, including previews.
