namespace Echokraut.Enums;

public enum QuestType
{
    None = 0,        // DefaultTalk, Balloon, NpcYell, other non-quest dialog
    MSQ = 1,         // Main Scenario Quest (meteor icon, EventIconType 3)
    SideQuest = 2,   // Normal side quest (! icon, EventIconType 1)
    Unlock = 3,      // Blue unlock / class-job quest (+ icon, EventIconType 8)
    BeastTribe = 4,  // Beast tribe quest (BeastTribe field != 0)
    Repeatable = 5,  // Repeatable / daily / leve
    Event = 6,       // Seasonal event quest (EventIconType 2)
}
