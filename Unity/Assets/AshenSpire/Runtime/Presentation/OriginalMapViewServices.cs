// OriginalMapViewServices.cs — explicit display-only bridge for solo/co-op maps.
// CampaignView supplies screen sizing and shell layout; RunController owns local
// profile preferences. These callbacks never enter gameplay or network commands.
using System;
using Newtonsoft.Json.Linq;
namespace AshenSpire.Presentation
{
    public sealed class OriginalMapViewServices
    {
        public Func<string, JObject> Read { get; set; }
        public Action<string, JObject> Write { get; set; }
        public Func<double> DisplayScale { get; set; }
        public Action<bool> SetMapSurface { get; set; }
        public Action Report { get; set; }
    }
}
