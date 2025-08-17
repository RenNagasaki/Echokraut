using Echokraut.DataClasses;
using Echokraut.Helper.Data;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Reflection;

namespace Echokraut.Helper.Functional
{
    public unsafe static class ClickHelper
    {
        public static void ClickDialogue(nint addon, EKEventId eventId)
        {
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Auto advancing...", eventId);
            var unitBase = (AtkUnitBase*)addon;

            if (unitBase != null && AtkStage.Instance() != null)
            {
                var evt = stackalloc AtkEvent[1]
                {
                    new()
                    {
                        Listener = (AtkEventListener*)unitBase,
                        State = new AtkEventState() {
                            StateFlags = AtkEventStateFlags.Pooled | AtkEventStateFlags.Unk3,
                        },
                        Target = &AtkStage.Instance()->AtkEventTarget
                    }
                };
                var data = stackalloc AtkEventData[1];

                unitBase->ReceiveEvent(AtkEventType.MouseClick, 0, evt, data);
            }
        }
    }
}
