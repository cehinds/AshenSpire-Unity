// CampaignView.Slots.cs — the save-slot screen in the shared UI shell.
// The title's "Load" entry raises SlotsRequested; RunController answers with Slots(...).
// Layout and confirmation live in OriginalSlotPanel; storage stays in RunController.Slots.
using System;
using System.Collections.Generic;
using AshenSpire.Domain.Original;
namespace AshenSpire.Presentation
{
    public sealed partial class CampaignView
    {
        public event Action SlotsRequested;
        public void Slots(IReadOnlyList<OriginalSaveSlotInfo> slots, Func<string, string> className, string notice,
            Action<int> load, Action<int> start, Action<int> delete, Action<int, int> copy)
        {
            Shell("ASHEN SPIRE", "SAVED CLIMBS · THREE SLOTS");
            _ = new OriginalSlotPanel(_body, slots, className, notice, load, start, delete, copy,
                () => MenuRequested?.Invoke(), (id, label, clicked, style) => Control(id, label, clicked, style), () => Report());
        }
    }
}
