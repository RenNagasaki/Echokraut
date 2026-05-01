# Plan: Quest Type Classification for Voice Clips

## Context
Currently voice clips carry no information about which type of quest they came from. All harvested dialog (MSQ, side quests, class quests, beast tribe, DefaultTalk, etc.) is treated identically. The user wants to filter voice clip generation by quest type — e.g., "generate only MSQ clips" or "only unlock quests" — without generating everything or going NPC-by-NPC.

## Approach: New `QuestType` enum + DB column + harvest tagging + UI filter

### 1. New Enum: `QuestType`
**File:** `Echokraut/Enums/QuestType.cs` (new)

```csharp
public enum QuestType
{
    None = 0,        // DefaultTalk, Balloon, other non-quest dialog
    MSQ = 1,         // Main Scenario Quest (meteor icon)
    SideQuest = 2,   // Normal side quest (! icon)
    Unlock = 3,      // Blue unlock quest (+ icon)
    ClassJob = 4,    // Class/Job quest
    BeastTribe = 5,  // Beast tribe daily
    Repeatable = 6,  // Leve, daily, repeatable
    Event = 7,       // Seasonal event quest
}
```

Detection from Quest sheet data (in order of priority):
- `Quest.BeastTribe.RowId != 0` → BeastTribe
- `Quest.IsRepeatable` → Repeatable
- `Quest.EventIconType.RowId` differentiates MSQ vs Unlock vs Side vs ClassJob vs Event
- Known `EventIconType` RowId mappings (need to verify in-game, but standard FFXIV values):
  - RowId 3 = MSQ (meteor)
  - RowId 1 = Normal side quest
  - RowId 2 = Repeatable
  - RowId 8 = Blue unlock
  - RowId 10 = Class/Job
  - RowId 7 = Feature/seasonal
- Fallback: check `Quest.JournalGenre` → `JournalCategory` → `JournalSection` names for classification
- Non-quest dialog (DefaultTalk, Balloon, NpcYell, etc.): `QuestType.None`

### 2. Add `quest_type` Column to `VoiceClipEntity`
**File:** `Echokraut/DataClasses/Database/VoiceClipEntity.cs`

```csharp
[Column("quest_type")]
public int QuestType { get; set; } // QuestType enum
```

### 3. Schema Migration v9
**File:** `Echokraut/Services/DatabaseService.cs`

```sql
ALTER TABLE voice_clips ADD COLUMN quest_type INTEGER NOT NULL DEFAULT 0
```
Default 0 = `QuestType.None`. Existing clips keep None. Re-harvest fills in the correct values.

### 4. Harvest: Classify Quest Dialogs
**File:** `Echokraut/Services/DialogHarvestService.cs`

**In `HarvestQuestDialogs`:** For each quest, determine `QuestType` from the `Quest` row before processing dialog entries. Pass it through `LinkedQuestDialog` → `LinkedDialog` → `VoiceClipEntity`.

Changes:
- Add `QuestType QuestType` field to `LinkedDialog` and `LinkedQuestDialog`
- Add `ClassifyQuest(Quest quest)` helper that reads `EventIconType`, `BeastTribe`, `IsRepeatable`, etc. and returns a `QuestType` enum value
- In `PersistLinkedDialogs`: pass `dialog.QuestType` to the `VoiceClipEntity` being created
- In `PersistLinkedQuestDialogs`: copy `QuestType` from `LinkedQuestDialog` to `LinkedDialog` during conversion
- Non-quest dialogs (`DefaultTalk`, `Balloon`, etc.) remain `QuestType.None`

### 5. DatabaseService: Filter by QuestType
**File:** `Echokraut/Services/DatabaseService.cs` + `IDatabaseService.cs`

Add optional `questType` parameter to existing query methods:
- `GetVoiceClipsForCharacter(charId, limit, offset, questType?)` — filter by quest type
- Or: new method `GetVoiceClipsByQuestType(int characterId, QuestType questType)` for the generation filter

### 6. UI: Quest Type Dropdown for Generation
**File:** `Echokraut/Windows/Native/NativeVoiceClipManagerWindow.cs`

Add a `TextDropDownNode` next to the harvest controls (or above the NPC tree) with options:
- "All" (default — no filter, same as current behavior)
- "MSQ"
- "Unlock"
- "Class/Job"
- "Side Quest"
- "Beast Tribe"
- "Non-Quest" (DefaultTalk/Balloon/NpcYell)

When "Generate All Unsaved" is clicked (per-NPC or globally):
- If filter is "All": pass all encounters (current behavior)
- Otherwise: filter `encounters` by `QuestType` before passing to `GenerateAllUnsaved`

The dropdown value is stored as a field on the window, not persisted to config (session-only filter).

### 7. LogOrUpdateVoiceClip: Preserve QuestType on Re-encounter
**File:** `Echokraut/Services/DatabaseService.cs`

In `LogOrUpdateVoiceClip`, update `QuestType` on existing clips (same pattern as `HasPlayerPlaceholder` and `BodyType`).

## Implementation Order
1. Enum (`QuestType.cs`)
2. VoiceClipEntity column + migration v9
3. `LinkedDialog`/`LinkedQuestDialog` add QuestType field
4. `DialogHarvestService`: `ClassifyQuest` helper + pass through persist
5. `DatabaseService`: update `LogOrUpdateVoiceClip`, add filtered query
6. UI dropdown + generation filter
7. Build + test

## Files to Modify
- `Echokraut/Enums/QuestType.cs` (new)
- `Echokraut/DataClasses/Database/VoiceClipEntity.cs`
- `Echokraut/DataClasses/Database/EchokrautDbContext.cs` (index on quest_type)
- `Echokraut/DataClasses/HarvestedDialog.cs`
- `Echokraut/Services/DatabaseService.cs` (migration v9 + LogOrUpdate + query)
- `Echokraut/Services/IDatabaseService.cs`
- `Echokraut/Services/DialogHarvestService.cs` (ClassifyQuest + pass through)
- `Echokraut/Windows/Native/NativeVoiceClipManagerWindow.cs` (dropdown + filter)
- `Echokraut/Localization/Loc.cs` (dropdown labels)

## Verification
- Build with 0 errors, tests pass
- Harvest fills `quest_type` for quest dialogs (check DB: `SELECT quest_type, COUNT(*) FROM voice_clips GROUP BY quest_type`)
- UI dropdown filters generation correctly
- Non-quest dialog stays QuestType.None
