using Dalamud.Game.ClientState.Objects.Enums;
using Echokraut.DataClasses;
using System.Collections.Generic;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// Documents the <see cref="NpcMapData.Voice"/> getter semantics that BackendService.GenerateVoice
/// relies on: the resolved <c>Voice</c> object is derived from the in-memory <c>Voices</c> selectable
/// list, which can be stale/empty when the backend connected only after startup (no MapVoices yet).
/// In that state the getter returns null even though a voice key IS assigned — so generation must fall
/// back to the persisted <c>voice</c> key rather than warning "No voice assigned".
/// </summary>
public class NpcMapDataVoiceTests
{
    private static NpcMapData Npc(string voiceKey, List<EchokrautVoice> selectables)
    {
        var npc = new NpcMapData(ObjectKind.EventNpc) { Name = "Glazrael", voice = voiceKey };
        npc.Voices = selectables;
        return npc;
    }

    [Fact]
    public void Voice_ResolvesFromSelectableList_WhenListContainsKey()
    {
        var voice = new EchokrautVoice { BackendVoice = "Female_All_NPC101.wav", VoiceName = "NPC101" };
        var npc = Npc("Female_All_NPC101.wav", [voice]);

        Assert.Same(voice, npc.Voice);
        Assert.Equal("Female_All_NPC101.wav", npc.Voice?.BackendVoice);
    }

    [Fact]
    public void Voice_ReturnsNull_WhenSelectableListIsEmpty_ButKeyIsPreserved()
    {
        // Reproduces the late-connect state: key assigned, but the selectable snapshot is empty.
        var npc = Npc("Female_All_NPC101.wav", []);

        Assert.Null(npc.Voice);                              // getter can't resolve → old bug surfaced here
        Assert.Equal("Female_All_NPC101.wav", npc.voice);    // persisted key is the reliable fallback
    }

    [Fact]
    public void Voice_ReturnsNull_WhenSelectableListMissesKey_ButKeyIsPreserved()
    {
        var other = new EchokrautVoice { BackendVoice = "Male_All_NPC001.wav", VoiceName = "NPC001" };
        var npc = Npc("Female_All_NPC101.wav", [other]);

        Assert.Null(npc.Voice);
        Assert.Equal("Female_All_NPC101.wav", npc.voice);
    }

    [Fact]
    public void Voice_ReturnsNull_WhenSelectableListIsNull_ButKeyIsPreserved()
    {
        var npc = new NpcMapData(ObjectKind.EventNpc) { Name = "Glazrael", voice = "Female_All_NPC101.wav" };
        // npc.Voices left null (never refreshed)

        Assert.Null(npc.Voice);
        Assert.Equal("Female_All_NPC101.wav", npc.voice);
    }

    [Fact]
    public void VoiceSetter_WritesBackendVoiceIntoKeyField()
    {
        var voice = new EchokrautVoice { BackendVoice = "Female_All_NPC101.wav", VoiceName = "NPC101" };
        var npc = Npc("", [voice]);

        npc.Voice = voice;

        Assert.Equal("Female_All_NPC101.wav", npc.voice);
    }
}
