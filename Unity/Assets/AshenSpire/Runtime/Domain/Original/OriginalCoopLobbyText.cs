// Explain the companion's public readiness gate without changing its authority.
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public static class OriginalCoopLobbyText
    {
        private static bool Flag(JToken seat, string key) => seat is JObject row && row[key]?.Type == JTokenType.Boolean && (bool)row[key];

        public static string StartBlocker(JToken lobby)
        {
            if (!(lobby is JObject party) || !(party["seats"] is JArray seats)) return "Waiting for the companion's party list.";
            if (Flag(party, "started")) return "The shared climb has already begun.";
            if (seats.Count < 2) return "Invite at least one other wanderer before beginning the shared climb.";
            if (seats.Any(seat => !Flag(seat, "connected"))) return "Waiting for everyone to reconnect. The host can remove a disconnected guest before the climb.";
            if (seats.Any(seat => !Flag(seat, "ready"))) return "Every wanderer needs to choose I'm ready before the climb can begin.";
            return null;
        }

        public static string Notice(string message, JToken lobby) => message == "all_players_must_be_ready"
            ? StartBlocker(lobby) ?? "The party changed. Wait for every wanderer to reconnect and choose I'm ready, then try again."
            : message;
    }
}
