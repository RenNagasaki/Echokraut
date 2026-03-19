[![Discord](https://img.shields.io/badge/Join-Discord-blue)](https://discord.gg/5gesjDfDBr)

# Echokraut - Echoed with TTS

Bring your dialogue to life in Final Fantasy XIV: Fully voiced NPC storylines and battle calls, auto-advance, and immersive 3D chat.

## Disclaimer

- The plugin supports three modes: **Local TTS** (GPU-based), **Remote Server**, and **Audio Files Only**. Local TTS currently requires an Nvidia GPU on Windows; AMD GPUs are supported on Linux. A dedicated GPU with at least 6 GB VRAM (e.g. RTX 3060 or AMD equivalent on Linux) is recommended for local inference.
- The plugin supports English, German, French, and Japanese clients.
- This plugin is still in active development. Please report issues on [GitHub](https://github.com/RenNagasaki/Echokraut/issues) or on [![Discord](https://img.shields.io/badge/Discord-blue)](https://discord.gg/5gesjDfDBr) (preferred).

## Features

### Voicing (each individually toggleable)

- **Dialogue TTS** — All unvoiced NPC dialogues get voiced via the TTS engine.
- **Battle Dialogue TTS** — Unvoiced battle talk popups (the small text boxes during duties and story content) get voiced.
- **Player Choice TTS** — Your character's dialogue choices in cutscenes and regular interactions get voiced.
- **Chat TTS** — Chat messages are voiced in 3D space — only players chatting near you are audible. Supports per-channel toggles (Say, Yell, Shout, FC, Tell, Party, Alliance, Novice, Linkshell, Cross-Linkshell).
- **Bubble TTS** — NPC speech bubbles are voiced in 3D space (the small text bubbles above random NPCs you encounter).
- **Retainer TTS** — Retainer dialogue gets voiced.

### Playback & Automation

- **Auto-advance** — Text auto-advances after the voiced line finishes, so you don't need to click during unvoiced cutscenes and quest dialogues.
- **Cancel on advance** — Optionally stop audio playback when the text window is closed or advanced manually.
- **In-game volume** — Uses the in-game voice volume slider, so generated audio matches normal voiced cutscenes.
- **3D audio** — Dialogue and chat audio can be positioned in 3D space relative to the speaking character or camera.

### Voice Management

- **Auto voice matching** — The plugin matches NPCs by name to existing voices in your TTS engine. If no match is found, it falls back to race/gender-specific NPC voices, and finally to a narrator voice.
- **NPC voice selection** — Change the voice of any NPC you've met via the Voice Selection tab.
- **Phonetic corrections** — Add custom pronunciation rules for names and terms the TTS engine mispronounces.

### Storage

- **Local caching** — Save generated audio to disk. Subsequent requests for the same line load from disk instead of re-generating (as long as the NPC's voice assignment hasn't changed).
- **Google Drive sync** — Upload and download cached audio via Google Drive to share across machines or with other players.

### UI

- **Native UI mode** — Switch between traditional ImGui windows and native FFXIV-styled addon windows (powered by KamiToolKit).
- **In-game dialog controls** — Play/Pause/Stop/Mute and voice selection controls attached directly to the dialogue window.
- **Localization** — Full UI localization for English, German, French, and Japanese.

## Commands

| Command | Description |
|---------|-------------|
| `/ek` | Opens the configuration window |
| `/ekt` | Toggles the plugin on/off |
| `/ekttalk` | Toggles dialogue voicing |
| `/ektbtalk` | Toggles battle dialogue voicing |
| `/ektbubble` | Toggles bubble voicing |
| `/ektchat` | Toggles chat voicing |
| `/ektchoice` | Toggles player choice voicing |
| `/ektcutschoice` | Toggles cutscene choice voicing |
| `/ekdel [n]` | Deletes last *n* local saved audio files (default 10) |
| `/ekdelmin [n]` | Deletes audio files generated in the last *n* minutes (default 10) |
| `/ekfirst` | Opens the first-time setup wizard |

## Setup

### 1. Install the plugin

Add the following custom repository to Dalamud's experimental plugin sources:

```
https://raw.githubusercontent.com/RenNagasaki/MyDalamudPlugins/master/pluginmaster.json
```

Search for **Echokraut** in the Dalamud plugin installer and install it.

### 2. First-time wizard

On first launch, a setup wizard guides you through three steps:

**Step 1 — Choose your mode:**

| Mode | Description |
|------|-------------|
| **Local TTS** | Installs a local [AllTalk_TTS](https://github.com/erew123/alltalk_tts) instance on your GPU. Best quality, requires ~20 GB disk space and a supported GPU. The installation is fully automated on Windows. |
| **Remote Server** | Connect to an existing AllTalk instance running on your network or another machine. Enter the server URL and you're ready to go. |
| **Audio Files Only** | No TTS generation. Play pre-made audio files from a local folder or download shared voice packs from Google Drive. |

**Step 2 — Configure your chosen mode** (install progress, server URL, or file paths).

**Step 3 — Done!** Open settings anytime with `/ek`.

### 3. Voice file naming (optional)

If you use custom voice files, name them with this pattern so the plugin can auto-assign voices:

```
GENDER_RACES_NAME.wav
```

Examples:
- `Male_Hyur_Thancred.wav` — for a named NPC
- `Male_Hyur-Elezen-Miqote_NPC1.wav` — for random NPCs of those races (if multiple NPC voices exist, one is picked randomly on first encounter)
- `Narrator.wav` — fallback voice for dialogues without a speaker and NPCs with no other match

### 4. Voice training (optional)

For higher quality, you can fine-tune the XTTS model with your own FFXIV voice data using [Echokraut Tools](https://github.com/RenNagasaki/Echokraut-Tools).

### 5. Multi-name NPCs

For NPCs with multiple names (e.g. Nanamo Ul Namo / Lilira) or shared voice actors, see the voice name mapping files: [VoiceNamesDE.json](https://github.com/RenNagasaki/Echokraut/blob/master/Echokraut/Resources/VoiceNamesDE.json) (and equivalents for other languages). Pull requests to expand these mappings are welcome.

## Settings Overview

The configuration window (`/ek`) has four main tabs:

- **Settings** — Five sub-tabs:
  - *General* — Master toggle, in-game controls, reset data
  - *Dialogue* — Dialogue/battle dialogue/bubble voicing, 3D audio options
  - *Chat* — Per-channel chat voicing toggles, 3D audio
  - *Storage* — Local save paths, Google Drive sync
  - *Backend* — AllTalk instance type selection, install/connection, advanced options
- **Voice Selection** — Search and reassign NPC voices (unified search across gender, race, name, and voice)
- **Phonetic Corrections** — Manage pronunciation rules
- **Logs** — View logs per category (Talk, Battle Talk, Bubbles, Chat, Choices, Backend)

## Supported TTS Backend

Currently supports [AllTalk_TTS](https://github.com/erew123/alltalk_tts) (v2 beta recommended), which provides XTTS, Piper, VITS and more engines for streaming inference.

## Thanks

- [MidoriKami](https://github.com/MidoriKami) for [KamiToolKit](https://github.com/MidoriKami/KamiToolKit) — the native UI framework that makes Echokraut's in-game interface possible. An awesome library for building native FFXIV addon UIs.
- Everyone contributing on the plugin-dev and dalamud-dev channels on the official [Dalamud](https://github.com/goatcorp/Dalamud) discord!
- Some parts of the code are taken from/inspired by:
  [TextToTalk](https://github.com/karashiiro/TextToTalk),
  [XivVoices](https://github.com/arcsidian/XivVoices).
