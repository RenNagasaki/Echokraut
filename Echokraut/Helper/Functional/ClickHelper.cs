using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Echokraut.Helper.Functional
{
    public unsafe static class ClickHelper
    {
        public static void ClickDialogue(nint addon)
        {
            var unitBase = (AtkUnitBase*)addon;

            if (unitBase != null && AtkStage.Instance() != null)
            {
                var evt = stackalloc AtkEvent[1]
                {
                    new()
                    {
                        Listener = (AtkEventListener*)unitBase,
                        Flags = 132,
                        Target = &AtkStage.Instance()->AtkEventTarget
                    }
                };
                var data = stackalloc AtkEventData[1];

                unitBase->ReceiveEvent(AtkEventType.MouseClick, 0, evt, data);
            }
        }
    }
}
