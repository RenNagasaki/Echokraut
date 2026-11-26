using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Echokraut.Services;

namespace Echokraut.Tests.SelfTest.E2e;

/// <summary>The window manager's side: windows that open, close, move, show texts and run steps.</summary>
internal sealed class FakeWindows
{
    public sealed class Window
    {
        public bool Open;
        public Vector2 Position = new(100, 100);
        public Vector2 Size = new(400, 300);
        public bool Clamps = true;
        public List<string> Texts = [];
        public Dictionary<string, Action> Steps = new();
    }

    public readonly Dictionary<string, Window> Windows = new();
    public readonly OwnedWindowsService Service = new();
    public readonly Vector2 Screen = new(1920, 1080);

    public FakeWindows()
    {
        foreach (var id in new[] { OwnedWindowIds.Config, OwnedWindowIds.FirstTime, OwnedWindowIds.VoiceClipManager,
                     OwnedWindowIds.VoiceClipDetail, OwnedWindowIds.GameDataTools, OwnedWindowIds.Changelog })
            Windows[id] = new Window();
        Service.ProbeRequested += Handle;
    }

    public Window this[string id] => Windows[id];

    private void Handle(OwnedWindowsProbe p)
    {
        p.Handled = true;
        if (p.Kind == OwnedWindowProbeKind.Snapshot)
        {
            p.Windows = Windows.Select(kv => new OwnedWindowState(kv.Key, kv.Value.Open, kv.Value.Open, kv.Value.Position,
                kv.Value.Size, Screen, kv.Value.Open ? kv.Value.Texts.Select((t, i) => new OwnedWindowText(t, 300, 50, false, 0, i)).ToList() : [])).ToList();
            return;
        }
        var w = Windows[p.WindowId!];
        switch (p.Kind)
        {
            case OwnedWindowProbeKind.Open: w.Open = true; break;
            case OwnedWindowProbeKind.Close: w.Open = false; break;
            case OwnedWindowProbeKind.Move:
                w.Position = w.Clamps
                    ? Vector2.Clamp(p.Position, -w.Size / 2, Screen - w.Size / 2)
                    : p.Position;
                break;
            case OwnedWindowProbeKind.SweepSteps: p.Steps = w.Steps.Keys.ToList(); break;
            case OwnedWindowProbeKind.RunStep:
                if (w.Steps.TryGetValue(p.Step!, out var run)) run();
                else p.Error = $"no step {p.Step}";
                break;
        }
    }
}
