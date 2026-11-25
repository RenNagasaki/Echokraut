using System.Collections.Generic;
using Dalamud.Game;
using Echokraut.Helper.Functional;

namespace Echokraut.Services;

/// <summary>The game's dialogue sheets as plain rows (quest, cut_scene, DefaultTalk), for <see cref="IDialogContinuationService"/>.</summary>
public interface IDialogSheetSource
{
    /// <summary>Each sheet's text rows in sheet order. Lazily enumerated; unreadable sheets are skipped.</summary>
    IEnumerable<IReadOnlyList<SheetLine>> ReadDialogueSheets(ClientLanguage language);
}
