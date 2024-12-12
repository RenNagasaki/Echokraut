using System.Runtime.InteropServices;
using FFXIVClientStructs.Attributes;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Echokraut.Helper.Addons;

[StructLayout(LayoutKind.Explicit, Size = 0x290)]
public unsafe struct AddonBattleTalk
{
    [FieldOffset(0x0)] public AtkUnitBase Base;
    [FieldOffset(0x238)] public AtkTextNode* Speaker;
    [FieldOffset(0x240)] public AtkTextNode* Text;
}
