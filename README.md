[![Discord](https://img.shields.io/badge/Join-Discord-blue)](https://discord.gg/5gesjDfDBr)

# Echokraut
Breaking the silence! That is the goal of this plugin for [Dalamud](https://github.com/goatcorp/Dalamud). Unlike the official Dalamud Plugin [TextToTalk](https://github.com/karashiiro/TextToTalk), this plugin is meant for local/self hosted high quality TTS for those wanting a rich experience without paying an arm and a leg for it.

## Dislaimer: 
* Since this plugin is dependent on [AllTalk_TTS](https://github.com/erew123/alltalk_tts) it is important to note that at the moment on Windows only Nvidia GPUs are supported. On Linux AMD should work as well. In the future this will expand.
* The plugin is only tested in german, but should work in every client language. (Report an issue if not 😘)
* Self hosted TTS is currently heavily dependent on a strong GPU. It's recommended to have at least a RTX 3060 (or AMD equivalent on Linux) with 6+GB VRAM built into the system hosting [AllTalk_TTS](https://github.com/erew123/alltalk_tts) for inference. (Keep in mind thats just for training. If you want to play FFXIV on the same machine I guess 3080 is minimum) For training I'd recommend at least 12 GB of VRAM but the more the better.

## Commands
* `/eksettings`: Opens the configuration window.

## Features
* Ingame volume: This plugin uses the ingame volume for all generated TTS, meaning the infered(generated) audio should be close or equal to normal voiced cutscenes
* Auto advance: You have the option to have text auto advance after the infered text is done speaking.
* Auto voice matching: The plugin tries to match an NPC via his name to a existing voice in your TTS of none are found it tries to match to specified 'NPC' voices per race of NPC or lastly the narrator voice. [AllTalk_TTS](https://github.com/erew123/alltalk_tts) has the option to inform you of all available voices.
* NPC voice selection: You can change the voice of every npc you met.

## Planned Features
* Chat TTS: At the moment only dialogues are getting voiced, I'm planning to expand into chat as well.
* I'm currently looking in getting emotions to work for TTS meaning that people could use [angry] in their chats and the voice would sound angry. The raw dialogue text for story or quests sometimes contains stuff like <pant> for when a npc is exhausted and more. I aim to use that for more detailed voicing.
  
## Supported TTS providers
* At the moment it only supports [AllTalk_TTS](https://github.com/erew123/alltalk_tts) which uses CoquiTTS for streaming inference. The developer of said TTS is working hard to make it an 'one service many TTS engines' project. Please refer to his GitHub regarding setting it up. (NVIDIA GPU Only at the moment)

## Setup/Install
* Setup [AllTalk_TTS](https://github.com/erew123/alltalk_tts) -> Refer to this site on how to install.
* (Optional) Finetune the xtts2 model to your own voices. Will sound better than simple voice cloning.
* Add the following path to the experimental paths of [Dalamud](https://github.com/goatcorp/Dalamud): `https://raw.githubusercontent.com/RenNagasaki/MyDalamudPlugins/master/pluginmaster.json`
* Search for 'Echokraut' in Dalamud and install the plugin
* Open the settings window either via the button or by typing `/eksettings`
* In the 'Backend' Tab enter the url of your [AllTalk_TTS](https://github.com/erew123/alltalk_tts) instance. (127.0.0.1:7851 should be default)
* If clicking 'Test Connection' results in `ready`, click on Load Voices and you're set.
* (IMPORTANT) The naming scheme of the voices should be like this: `GENDER_RACE_NAME.wav`.
    For example: `Male_Hyur_Thancred.wav` for a named NPC
    and `Male_Hyur_NPC1.wav` for a random Hyur NPC. If more than one NPC voice exists then the plugin selects one randomly the first time you meet a new NPC.
    There is one exception, the so called narrator voice. It gets used for all dialogues without a speaker and all NPCs where no other voice could be found (last fallback) and should be named `Narrator.wav`. 

## Just starting
I started this whole project as a way for me to enjoy replaying the game without having to read all of 2.0 again. It evolved from there so please bear with many features still missing. You can always request more. 😊

## Thanks
* Some parts of the code are taken from/inspired by:
    [TextToTalk](https://github.com/karashiiro/TextToTalk).
    [XivVoices](https://github.com/arcsidian/XivVoices).
