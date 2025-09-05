[![Discord](https://img.shields.io/badge/Join-Discord-blue)](https://discord.gg/5gesjDfDBr)

# Echokraut - Echoed with TTS
Bring your dialogue to life in Final Fantasy XIV: Fully voiced NPC storylines and battle calls, auto-advance, and immersive 3D chat. 
* Optional: train your own voice model with Echokraut-Tools.
* That is the goal of this plugin for [Dalamud](https://github.com/goatcorp/Dalamud).

## Dislaimer: 
* Since this plugin is dependent on [AllTalk_TTS](https://github.com/erew123/alltalk_tts) it is important to note that at the moment on Windows only Nvidia GPUs are supported. On Linux AMD should work as well. In the future this will likely expand.
* The plugin is only tested in german, but should work in every client language. (Report an issue if not 😘)
* Self hosted TTS is currently heavily dependent on a strong GPU. It's recommended to have at least a RTX 3060 (or AMD equivalent on Linux) with 6+GB VRAM built into the system hosting [AllTalk_TTS](https://github.com/erew123/alltalk_tts) for inference.
* This plugin is still in it's early stages of development, feel free to report any issues here or [![Discord](https://img.shields.io/badge/Join-Discord-blue)](https://discord.gg/5gesjDfDBr) (preferred)

## Commands
* `/ek` - Opens the configuration window.
* `/ekttalk` - Toggles dialogue voicing
* `/ektbtalk` - Toggles battle dialogue voicing
* `/ektbubble` - Toggles bubble voicing
* `/ektchat` - Toggles chat voicing
* `/ektcutschoice` - Toggles cutscene choice voicing
* `/ektchoice` - Toggles choice voicing
* `/ekdel` - /ekdel n -> Deletes last 'n' local saved files. Default 10
* `/ekdelmin` - /ekdelmin n -> Deletes last 'n' minutes generated local saved files. Default 10

## Features - Each feature is on/off toggleable
* One click install: Upon first starting the plugin you get asked to either install a local Alltalk Instance or connect a remote running one. (The local install is fully automated on Windows, semi automated on Linux. Just follow the steps shown)
* Dialogue TTS: All unvoiced Dialogues get voiced via the TTS Engine.
* Battletalk TTS: All unvoiced Battletalks get voiced via the TTS Engine. (Battletalks are the small popup Texts in Duties or Story contents.)
* Playerchoice TTS: Altough still in it's infant phase, the selections of the player in cutscenes get Voiced. This means you can give your own Character a voice! (Altough small in content)
* Chat TTS: Your Chat get's Voiced in 3D Space. Meaning only people chatting around you are actually audible.
* Bubble TTS: Like the Chat the ingame NPC Bubbles get Voiced in 3D Space. (Bubbles are the small text bubbles above random NPC's you meet on your journey)
* Auto advance: The Text auto advances after the infered text is done speaking -> no need to click while in unvoiced cutscenes/quest dialogues.
* Local saving/loading -> You can set the option so save generated text on your Disk so each time the text then is requested it gets loaded from disk instead of generated.(Only applies as long as the voice of the NPC doesn't change)
* NPC voice selection: You can change the voice of every npc you met.

## Features - Fixed
* Ingame volume: This plugin uses the ingame volume for all generated TTS, meaning the infered(generated) audio should be close or equal to normal voiced cutscenes
* Auto voice matching: The plugin tries to match an NPC via his name to a existing voice in your TTS. If none are found it tries to match to specified 'NPC' voices per race of NPC or lastly the narrator voice. 

## Planned Features
* I'm still trying to figure out a way to identify "???" Dialogues to set the correct voice if possible. At the moment Dialogues with "???" as name get identified as a single NPC called "???" resulting in wrong voices.
  
## Supported TTS providers
* At the moment it only supports [AllTalk_TTS](https://github.com/erew123/alltalk_tts) which uses CoquiTTS for streaming inference.
  The v2_Beta of said TTS System supports XTTS, Piper, VITS and more.

## Setup/Install
* Setup [AllTalk_TTS](https://github.com/erew123/alltalk_tts) -> Refer to this site on how to install (Branch v2_Beta preferred. It's faster, easier to use, and more reliable).
* Add the following path to the experimental paths of [Dalamud](https://github.com/goatcorp/Dalamud): `https://raw.githubusercontent.com/RenNagasaki/MyDalamudPlugins/master/pluginmaster.json`
* Search for 'Echokraut' in Dalamud and install the plugin
* Upon first start the "First time using Echokraut" Window should pop up and lead you through the process of setting up Echokraut.
* (Optional) Finetune the xtts2 model to your own voices. Will sound better than simple voice cloning.
* (Optional) To create your own trained voice model on your own FFXIV GameData, check out: [Echokraut Tools](https://github.com/RenNagasaki/Echokraut-Tools)
* (Optional) The naming scheme of the voices can be like this: `GENDER_RACES_NAME.wav`. That way the plugin can auto interpret how to use the voice.
  For example: `Male_Hyur_Thancred.wav` for a named NPC
  and `Male_Hyur-Elezen-Miqote_NPC1.wav` for a random NPC which is from Hyur, Elezen or Miqote race. If more than one NPC voice exists then the plugin selects one randomly the first time you meet a new NPC.
* There is one exception, the so called narrator voice. It gets used for all dialogues without a speaker and all NPCs where no other voice could be found (last fallback) and should be named `Narrator.wav`.
* For NPCs with multiple names (Nanamo Ul Namo/Lilira) or same voice actor take a look at this file: [VoiceNames](https://github.com/RenNagasaki/Echokraut/blob/master/Echokraut/Resources/VoiceNamesDE.json) or the one matching your language. If there is no entry for one you're expecting feel free to add a pull request. These files are in the works while people use the plugin. (I can't fill this for other languages)
* Small example of how the files could be named:
* ![grafik](https://github.com/user-attachments/assets/7a879f5d-9753-423b-a6cc-850871f6eba9)

## Just starting
I started this whole project as a way for me to enjoy replaying the game without having to read all of 2.0 again. It evolved from there so please bear with many features still missing. You can always request more. 😊

## Thanks
* Everyone contributing on the plugin-dev and dalamud-dev channels on the official [Dalamud](https://github.com/goatcorp/Dalamud) discord!
* Some parts of the code are taken from/inspired by:
    [TextToTalk](https://github.com/karashiiro/TextToTalk).
    [XivVoices](https://github.com/arcsidian/XivVoices).
