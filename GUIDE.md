# Echokraut — User Guide

A step-by-step guide to setting up and using Echokraut, the FFXIV plugin that voices
unvoiced NPC dialogue, battle talk, chat, and speech bubbles with text-to-speech.

If you just want to get talking, jump to **[1. First-time setup](#1-first-time-setup)**.
If you already have it running and want to change how audio is generated, jump to
**[4. Switching TTS engines](#4-switching-tts-engines)**.

---

## Table of contents

1. [First-time setup](#1-first-time-setup)
2. [Understanding engines and modes](#2-understanding-engines-and-modes)
3. [Everyday use](#3-everyday-use)
4. [Switching TTS engines (AllTalk ↔ EchokrauTTS)](#4-switching-tts-engines-alltalk--echokrautts)
5. [Switching sub-engines inside EchokrauTTS (XTTS ↔ F5)](#5-switching-sub-engines-inside-echokrautts-xtts--f5)
6. [Managing voices](#6-managing-voices)
7. [Commands reference](#7-commands-reference)
8. [Troubleshooting](#8-troubleshooting)

---

## 1. First-time setup

### Install the plugin

Add this custom repository to Dalamud's experimental plugin sources
(**Dalamud Settings → Experimental → Custom Plugin Repositories**):

```
https://raw.githubusercontent.com/RenNagasaki/MyDalamudPlugins/master/pluginmaster.json
```

Then search for **Echokraut** in the Dalamud plugin installer and install it.

### The first-time wizard

On first launch a setup wizard opens automatically. You can reopen it anytime with
`/ekfirst`. It has three steps.

**Step 1 — Choose an engine and a mode.**

First pick a **TTS engine**:

- **EchokrauTTS** *(selected by default)* — the purpose-built engine for Echokraut.
- **AllTalk_TTS** — an alternative engine that also bundles XTTS, Piper, VITS and more.

Then pick how that engine **runs**:

| Mode | What it does | Needs a GPU? |
|------|--------------|--------------|
| **Local** | Downloads and installs the engine on your PC. Best quality and privacy. Fully automated install on Windows. | Yes — an Nvidia GPU on Windows (AMD on Linux), 6 GB+ VRAM recommended. |
| **Remote** | Connects to an engine already running on your network or another machine. Enter its URL and you're done. | No — the remote machine does the work. |
| **Audio Files Only** | No generation at all. Plays pre-made audio files from a folder or shared voice packs. | No. |

> **Not sure what to pick?** If you have a decent Nvidia GPU, choose **EchokrauTTS + Local**
> — it's the default and needs no external setup. If your GPU is weak or you're on a
> laptop, run the engine on another PC and use **Remote**, or use **Audio Files Only**.

**Step 2 — Configure the chosen engine.**

- **Local:** confirm the install path (no spaces or dashes), then click **Install**. The
  download and setup run automatically; progress is shown in the wizard. When it finishes,
  click **Start** to launch the local instance.
- **Remote:** enter the engine's base URL and click **Test**. You need the green **Ready**
  result before you can continue.
- **Audio Files Only:** set the folder that holds your audio files (and optionally the
  Google Drive sync options).

**Step 3 — Done.** Close the wizard. You can reopen the full settings anytime with
`/ekconfig`.

---

## 2. Understanding engines and modes

Echokraut separates **which engine** generates speech from **where** it runs. Both
choices are independent and both can be changed later.

### Engines

- **EchokrauTTS** *(default)* — tuned for Echokraut. Its local install bundles **two
  sub-engines**, XTTS and F5, which you can swap between without reinstalling (see
  [section 5](#5-switching-sub-engines-inside-echokrautts-xtts--f5)).
- **AllTalk_TTS** — a general-purpose TTS server (v2 beta recommended) offering XTTS,
  Piper, VITS and more.

### Modes (called *instance type* in settings)

- **Local** — the engine runs on your machine as a background process that Echokraut
  starts and stops.
- **Remote** — Echokraut talks to an engine running elsewhere over HTTP.
- **Audio Files Only / None** — no generation; only pre-recorded audio files play.

Each engine remembers its **own** mode. Switching engines does not reset the other
engine's configuration.

---

## 3. Everyday use

### The voicing toggles

Every voicing type can be turned on or off independently, either in **Settings** or with
a chat command:

| Voicing | What it covers |
|---------|----------------|
| **Dialogue** | Unvoiced NPC dialogue windows. |
| **Battle dialogue** | The small battle-talk pop-ups during duties and story content. |
| **Player choice** | Your character's dialogue choices. |
| **Chat** | Nearby players' chat messages, positioned in 3D. Per-channel toggles (Say, Yell, Shout, FC, Tell, Party, …). |
| **Bubbles** | NPC speech bubbles above characters, in 3D. |
| **Retainer** | Retainer dialogue. |

### Playback and automation

- **Auto-advance** — text advances automatically once the spoken line finishes, so you
  don't have to click through unvoiced cutscenes.
- **Cancel on advance** — optionally stop playback when you close or advance the window
  manually.
- **In-game volume** — generated audio follows the in-game *Voice* volume slider, so it
  blends with normally voiced cutscenes.
- **3D audio** — dialogue, bubbles, and chat can be positioned in 3D relative to the
  speaker or camera.

### In-dialogue controls

When a voiced line plays, Play / Pause / Stop / Mute buttons and a voice selector attach
directly to the dialogue window, so you can adjust things without opening settings.

### Local caching and sync

- **Local caching** — generated audio is saved to disk. The same line replays from disk
  next time instead of regenerating (as long as the NPC's voice hasn't changed).
- **Google Drive sync** — upload and download cached audio to share it across machines or
  with other players.

Configure both in **Settings → Storage**.

---

## 4. Switching TTS engines (AllTalk ↔ EchokrauTTS)

You can change engines at any time — you are not locked into the one you picked in the
wizard.

1. Open settings with `/ekconfig`.
2. Go to **Settings → Backend**.
3. Pick the engine you want at the top: **EchokrauTTS** or **AllTalk_TTS**.
4. Choose that engine's **instance type** (Local / Remote / Audio Files Only).
5. Configure it:
   - **Local** — set the install path and click **Install** (or **Reinstall** if it was
     installed before), then **Start**.
   - **Remote** — enter the base URL and click **Test** until you get **Ready**.
   - **Audio Files Only** — set the audio folder.

Notes:

- Each engine keeps its own settings, so switching back and forth doesn't lose your
  configuration.
- The first time you switch to an engine in **Local** mode you'll need to install it
  once. After that, switching is instant.
- If you switch to **Audio Files Only** mode, tabs that require live generation (such as
  Voice Selection, Phonetics, and Chat) are hidden, because there's nothing to generate.

---

## 5. Switching sub-engines inside EchokrauTTS (XTTS ↔ F5)

This applies only to **EchokrauTTS in Local mode**. Its local install ships **both**
sub-engines and all their models, so switching between them is just a restart of the
local instance — **never a reinstall**.

| Sub-engine | Character | When to use |
|------------|-----------|-------------|
| **XTTS** *(default)* | Coqui XTTS-v2. Clones a voice from audio alone (no reference text needed), multilingual. | The best all-round default — start here. |
| **F5** | F5-TTS. Per-language fine-tunes; each voice sample needs an accompanying reference text. | When you have F5-style samples with reference text and want its output for your language. |

### How to switch

1. Open `/ekconfig` → **Settings → Backend**.
2. Make sure **EchokrauTTS** is the selected engine and its mode is **Local**.
3. Find the **TTS engine** dropdown (labelled *"TTS engine (restarts local instance on
   change)"*).
4. Pick **XTTS** or **F5**.

Echokraut saves the choice and, if the local instance is running, restarts it
automatically with the new sub-engine. There's no download and no reinstall.

### FP16 (XTTS only)

If a supported GPU is detected, an extra checkbox appears:

> **Faster XTTS generation with FP16 (needs NVIDIA GPU, ~1.3–1.8×)**

Enabling it makes XTTS generation faster at half precision. It only affects XTTS, and
toggling it also restarts the local instance (precision is fixed when the model loads).
It has no effect on F5 and won't appear if no compatible GPU is present.

### A note on F5 reference text

F5 needs a reference text for each voice sample. XTTS does not. If you switch to F5 and a
voice has no reference text, its cloning quality will suffer — so XTTS is the safer
default unless you specifically prepared F5 samples.

---

## 6. Managing voices

### Automatic voice matching

Echokraut matches NPCs to voices by name. If there's no direct match it falls back to a
race/gender-appropriate NPC voice, and finally to a narrator voice for unnamed speakers.

### Reassigning a voice

Open `/ekconfig` → **Voice Selection**. Search across gender, race, name, and voice, then
reassign the voice of any NPC you've encountered. You can also do this on the fly from the
voice selector attached to the dialogue window.

### Phonetic corrections

Open `/ekconfig` → **Phonetic Corrections** to add pronunciation rules for names and terms
the engine mispronounces.

### Custom voice files (optional)

If you supply your own voice files, name them so the plugin can auto-assign them:

```
GENDER_RACES_NAME.wav
```

- `Male_Hyur_Thancred.wav` — a specific named NPC.
- `Male_Hyur-Elezen-Miqote_NPC1.wav` — random NPCs of those races (one is chosen at first
  encounter if several match).
- `Narrator.wav` — the fallback for speaker-less dialogue and unmatched NPCs.

### Voice Clip Manager

Open the Voice Clip Manager with `/ek` to browse, play, (re)generate, and delete saved
audio clips per NPC.

---

## 7. Commands reference

| Command | What it does |
|---------|--------------|
| `/ek` | Opens the **Voice Clip Manager** |
| `/ekconfig` | Opens the **configuration window** |
| `/ekfirst` | Opens the **first-time setup wizard** |
| `/ekdata` | Opens the **Game Data Tools** window |
| `/ekt` | Toggles the whole plugin on/off |
| `/ekttalk` | Toggles dialogue voicing |
| `/ektbtalk` | Toggles battle dialogue voicing |
| `/ektbubble` | Toggles bubble voicing |
| `/ektchat` | Toggles chat voicing |
| `/ektchoice` | Toggles player choice voicing |
| `/ektcutschoice` | Toggles cutscene choice voicing |

---

## 8. Troubleshooting

**Local install won't start.** Make sure the install path has no spaces or dashes, and
that you have enough free disk space. On Windows the install is automated; wait for it to
finish before clicking **Start**.

**Remote mode won't connect.** Re-check the base URL and click **Test** — you need the
green **Ready** result. If the URL changes, the previous successful test is invalidated
and you must test again.

**No sound at all.** Confirm the plugin is enabled (`/ekt`), the specific voicing type is
toggled on, and the in-game **Voice** volume slider isn't at zero.

**Crackling or stuttering with local EchokrauTTS + XTTS on a weak GPU.** This is a known
symptom of the GPU generating slower than real time. Recent versions prebuffer audio to
smooth it out; if it persists on long lines, disabling streaming (full buffering) avoids
it at the cost of a short delay before playback.

**Voice sounds wrong for an NPC.** Reassign it in **Voice Selection**, or add a
[phonetic correction](#phonetic-corrections) if it's a pronunciation issue.

---

Still stuck? Report issues on
[GitHub](https://github.com/RenNagasaki/Echokraut/issues) or ask on the
[Discord](https://discord.gg/5gesjDfDBr) (preferred).
