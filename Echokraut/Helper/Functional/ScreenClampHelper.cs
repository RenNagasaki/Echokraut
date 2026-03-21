using System;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Echokraut.Helper.Functional;

/// <summary>
/// Clamps native addon windows so at least 50% of width and height stays on screen.
/// </summary>
internal static unsafe class ScreenClampHelper
{
    internal static void ClampToScreen(AtkUnitBase* addon, Vector2 size)
    {
        if (addon == null) return;

        var screenSize = (Vector2)AtkStage.Instance()->ScreenSize;
        var halfW = size.X * 0.5f;
        var halfH = size.Y * 0.5f;
        var x = (float)addon->X;
        var y = (float)addon->Y;
        var clampedX = Math.Clamp(x, -halfW, screenSize.X - halfW);
        var clampedY = Math.Clamp(y, -halfH, screenSize.Y - halfH);

        if (Math.Abs(x - clampedX) > 0.5f || Math.Abs(y - clampedY) > 0.5f)
            addon->SetPosition((short)clampedX, (short)clampedY);
    }
}
