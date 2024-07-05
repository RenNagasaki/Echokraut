using Echokraut.DataClasses;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using static Echokraut.Helper.AddonTalkHelper;
using System.Runtime.InteropServices;
using System;
using static FFXIVClientStructs.FFXIV.Client.Game.UI.PublicInstance;
using Echokraut.Enums;
using Echokraut.Utils;

namespace Echokraut.Helper
{
    public unsafe static class ClickHelper
    {
        internal unsafe delegate IntPtr ReceiveEventDelegate(AtkEventListener* eventListener, EventType evt, uint which, void* eventData, void* inputData);
        private static nint Address = nint.Zero;

        public static void Click(nint talkAddress)
        {
            Address = talkAddress;

            if (Address != nint.Zero)
            {
                ClickAddonStage();

                LogHelper.Debug("Clickhelper.Click", "Clicking dialog.");
            }
        }
        private static void ClickAddonStage(EventType type = EventType.MOUSE_CLICK)
        {
            var unitBase = (AtkUnitBase*)Address;
            var target = AtkStage.Instance();

            var eventData = EventData.ForNormalTarget(target, unitBase);
            var inputData = InputData.Empty();

            InvokeReceiveEvent(&unitBase->AtkEventListener, type, 0, eventData, inputData);
        }

        /// <summary>
        /// Invoke the receive event delegate.
        /// </summary>
        /// <param name="eventListener">Type receiving the event.</param>
        /// <param name="type">Event type.</param>
        /// <param name="which">Internal routing number.</param>
        /// <param name="eventData">Event data.</param>
        /// <param name="inputData">Keyboard and mouse data.</param>
        private static unsafe void InvokeReceiveEvent(AtkEventListener* eventListener, EventType type, uint which, EventData eventData, InputData inputData)
        {
            var receiveEvent = GetReceiveEvent(eventListener);
            receiveEvent(eventListener, type, which, eventData.Data, inputData.Data);
        }

        private static unsafe ReceiveEventDelegate GetReceiveEvent(AtkEventListener* listener)
        {
            var receiveEventAddress = new IntPtr(listener->VirtualTable->ReceiveGlobalEvent);
            return Marshal.GetDelegateForFunctionPointer<ReceiveEventDelegate>(receiveEventAddress)!;
        }
    }
}
