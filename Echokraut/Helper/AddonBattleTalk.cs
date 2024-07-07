using System.Runtime.InteropServices;
using FFXIVClientStructs.Attributes;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Echokraut.Helper;

[StructLayout(LayoutKind.Explicit, Size = 0x290)]
//[Addon(new string[] { "_BattleTalk" })]
//[GenerateInterop(false)]
//[Inherits<AtkUnitBase>(0)]
public unsafe struct AddonBattleTalk
{
    [FieldOffset(0x0)] public AtkUnitBase AtkUnitBase;
    [FieldOffset(0x230)] public AtkTextNode* AtkTextNode230;
    [FieldOffset(0x238)] public AtkTextNode* AtkTextNode238;
    [FieldOffset(0x240)] public AtkResNode* AtkResNode240;
    [FieldOffset(0x248)] public AtkNineGridNode* AtkNineGridNode248;
    [FieldOffset(0x250)] public AtkNineGridNode* AtkNineGridNode250;
    [FieldOffset(0x258)] public AtkResNode* AtkResNode258;
    [FieldOffset(0x260)] public AtkImageNode* AtkImageNode260;
    // 0x258:0x260 - Possibly two small i32 values
    // 0x260:0x268 - Pointer to a pointer to a static function - it was just "xor al,al; ret;"
    //               when I looked at it, but it probably gets replaced with something
    //               interesting sometimes
    // 0x268:0x270 - Looks like an enum value of some kind
    [FieldOffset(0x280)] public AtkResNode* AtkResNode280;
    // 0x270:0x278 - Pointer to some sparse-looking object
}
