// CombatSession.Equipment.cs — finish a validated, restored equipment transaction.
// GameSession owns rollback around this call, including event hooks and end-turn.
// Costs and pile changes were calculated by OriginalCombatEquipment on snapshots.
using Newtonsoft.Json.Linq;
namespace AshenSpire.Domain.Original
{
    public sealed partial class CombatSession
    {
        internal JArray FinishEquipmentSwap(JArray events, bool endsTurn)
        {
            RequirePlayerTurn(); var start = _events.Count;
            foreach (JObject row in events) { var payload = (JObject)row.DeepClone(); var type = (string)payload["type"]; payload.Remove("type"); Emit(type, payload); }
            Drain(); if (endsTurn && _result == null) EndTurn();
            return Since(start);
        }
    }
}
