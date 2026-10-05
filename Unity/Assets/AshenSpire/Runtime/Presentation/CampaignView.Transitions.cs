using System.Collections.Generic;
using AshenSpire.Domain;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation
{
    public sealed partial class CampaignView
    {
        private readonly Dictionary<string, Rect> _enemyPositions = new Dictionary<string, Rect>();
        private readonly List<FeelTween> _enemyDeaths = new List<FeelTween>();
        private void PresentationTrace(string action, string status)
        {
            if (_diagnostics) Debug.Log("ASHENSPIRE_TRANSITION " + new JObject { ["action"] = action, ["status"] = status }.ToString(Newtonsoft.Json.Formatting.None));
        }
        private void CaptureEnemyPositions()
        {
            foreach (var tween in _enemyDeaths) tween.Stop();
            _enemyDeaths.Clear(); _enemyPositions.Clear();
            foreach (var image in _root.Query<OriginalEnemyFigure>().ToList())
            {
                var prefix = image.name?.StartsWith("native-enemy-art-") == true ? "native-enemy-art-" : "coop-enemy-art-";
                if (image.name?.StartsWith(prefix) != true) continue;
                var bounds = image.worldBound;
                _enemyPositions[image.name.Substring(prefix.Length)] = new Rect(_root.WorldToLocal(bounds.position), bounds.size);
            }
        }
        private void EnemyDeaths(IEnumerable<JToken> events)
        {
            if (events == null || _disposed) return;
            var motion = FeelDriver.Profile.Resolve("enemy.death", FeelDriver.Settings);
            if (!motion.Play) return;
            foreach (var row in events)
            {
                if ((string)row["type"] != "enemyDied" || !_enemyPositions.TryGetValue((string)row["targetId"] ?? "", out var bounds)) continue;
                var image = OriginalEnemyFigure.Create((string)row["enemyId"]);
                image.name = "defeated-enemy"; image.pickingMode = PickingMode.Ignore;
                image.style.position = Position.Absolute;
                image.style.left = bounds.x; image.style.top = bounds.y;
                image.style.width = bounds.width; image.style.height = bounds.height;
                _root.Add(image);
                PresentationTrace("enemy.death", "started");
                _enemyDeaths.Add(FeelTween.Run(image, motion.DurationMs,
                    ms => { image.style.opacity = (float)motion.SampleAt(FeelProperty.Opacity, ms); FeelDriver.Place(image, 0, (float)motion.SampleAt(FeelProperty.Y, ms), (float)motion.SampleAt(FeelProperty.Scale, ms)); },
                    status => { FeelDriver.Rest(image); image.RemoveFromHierarchy(); PresentationTrace("enemy.death", status); }));
            }
        }
    }
}
