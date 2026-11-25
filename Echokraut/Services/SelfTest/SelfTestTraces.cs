namespace Echokraut.Services.SelfTest;

/// <summary>Removes what a self-test line left in the database (the throwaway character and its clip rows).</summary>
internal static class SelfTestTraces
{
    /// <summary>
    /// Deletes <paramref name="speaker"/>'s voice clip rows and the character itself — only when its name is
    /// <paramref name="expectedName"/>, so a real character is never touched.
    /// </summary>
    public static void RemoveCharacter(IDatabaseService db, INpcDataService npcData, DataClasses.NpcMapData? speaker,
        string expectedName)
    {
        if (speaker == null || speaker.Name != expectedName) return;
        var character = db.FindCharacter(speaker.Name, speaker.Gender, speaker.Race, (int)speaker.Language);
        if (character != null)
            foreach (var clip in db.GetVoiceClipsForCharacter(character.Id))
                db.DeleteVoiceClip(clip.Id);
        npcData.RemoveCharacter(speaker);
    }
}
