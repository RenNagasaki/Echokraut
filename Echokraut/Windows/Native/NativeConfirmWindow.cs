using System;
using System.Collections.Generic;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.UI.Nodes.Layout;
using Echotools.UI.Nodes;

using static Echokraut.Windows.Native.NativeNodeFactory;

namespace Echokraut.Windows.Native;

/// <summary>
/// Small modal-style question popup: word-wrapped paragraphs + a confirm and a cancel button.
/// Used for the game-voice opt-in (plan game-voice-reference §5.2) and the "delete game-voice
/// clips?" question when the feature is switched off (§6).
/// <para>Exactly one of the two callbacks runs, once: a button click, or <c>onCancel</c> when the
/// window is closed any other way (X, Escape, close-all). Callers pass already-localised text.</para>
/// </summary>
public sealed unsafe class NativeConfirmWindow : NativeAddon
{
    private readonly IReadOnlyList<string> _paragraphs;
    private readonly string _confirmLabel;
    private readonly string _cancelLabel;
    private readonly Action _onConfirm;
    private readonly Action _onCancel;
    private bool _resolved;
    private Action? _pending;

    public NativeConfirmWindow(IReadOnlyList<string> paragraphs, string confirmLabel, string cancelLabel,
        Action onConfirm, Action onCancel)
    {
        _paragraphs = paragraphs;
        _confirmLabel = confirmLabel;
        _cancelLabel = cancelLabel;
        _onConfirm = onConfirm;
        _onCancel = onCancel;
    }

    protected override void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
    {
        var pos = ContentStartPosition;
        var size = ContentSize;
        var w = size.X;
        const float buttonRowH = 32f;

        var list = new ScrollingListNode
        {
            Position = pos,
            Size = new Vector2(w, size.Y - buttonRowH - 6),
            FitWidth = true,
            ItemSpacing = 8,
        };
        foreach (var paragraph in _paragraphs)
        {
            var text = Label(paragraph, w - 16);
            text.AddTextFlags(TextFlags.WordWrap | TextFlags.MultiLine);
            text.Size = new Vector2(w - 16, text.GetTextDrawSize(false).Y + 4);
            list.AddNode(text);
        }
        list.RecalculateLayout();
        AddNode(list);

        // Clicks are deferred to OnUpdate: the callbacks may close this addon, and closing it
        // from inside its own event handler is the dispose-during-event pattern ATK dislikes.
        var row = new HorizontalListNode
        {
            Position = new Vector2(pos.X, pos.Y + size.Y - buttonRowH),
            Size = new Vector2(w, buttonRowH),
            ItemSpacing = 8,
        };
        row.AddNode(Button(_confirmLabel, 160, () => _pending ??= Confirm));
        row.AddNode(Button(_cancelLabel, 120, () => _pending ??= Cancel));
        AddNode(row);
    }

    protected override void OnUpdate(AtkUnitBase* addon)
    {
        var action = _pending;
        if (action == null) return;
        _pending = null;
        action();
        Close();
    }

    protected override void OnHide(AtkUnitBase* addon) => Cancel();

    private void Confirm()
    {
        if (_resolved) return;
        _resolved = true;
        _onConfirm();
    }

    private void Cancel()
    {
        if (_resolved) return;
        _resolved = true;
        _onCancel();
    }
}
