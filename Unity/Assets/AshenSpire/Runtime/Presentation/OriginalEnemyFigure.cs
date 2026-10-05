// OriginalEnemyFigure.cs — one painted enemy-art binding shared by solo and co-op.
// WIRING: OriginalRunPanel and OriginalCoopPanel create this image for the target.
// MODIFY: enemy IDs and Resources paths in GameContent/Unity/Original/enemy-art.json;
// place replacement PNGs in Resources/Art/Enemies/Painted and run content import.
// The build validates every enemy mapping. No combat rules or saved state live here.
using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
namespace AshenSpire.Presentation
{
    public sealed class OriginalEnemyFigure : Image
    {
        private static JObject _paths;
        private readonly CombatActorImage _art = new CombatActorImage { scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
        private readonly Texture2D _sourceTexture;
        private readonly JArray _bounds;
        public static OriginalEnemyFigure Create(string enemyId) => new OriginalEnemyFigure(enemyId);
        private OriginalEnemyFigure(string enemyId)
        {
            if (_paths == null) _paths = (JObject)JObject.Parse(Resources.Load<TextAsset>("Original/enemy-art").text)["enemies"];
            var row = _paths[enemyId] as JObject;
            var resource = (string)row?["resource"];
            if (string.IsNullOrEmpty(resource)) throw new InvalidOperationException("Missing painted enemy mapping: " + enemyId);
            var texture = Resources.Load<Texture2D>(resource);
            if (texture == null) throw new InvalidOperationException("Missing painted enemy texture: " + resource);
            var ownerArt = Resources.Load<Texture2D>("Art/OwnerAppearance/" + enemyId);
            if (enemyId == "wanderingSoldier") ownerArt = Resources.Load<Texture2D>("Art/CombatRefresh/wanderingSoldier-hilt-v2") ?? ownerArt;
            if (ownerArt != null) { texture = ownerArt; _bounds = new JArray(0,0,1,1); }
            else _bounds = (JArray)row["bounds"];
            name = "enemy-art-" + enemyId; pickingMode = PickingMode.Ignore;
            AddToClassList("fighter"); style.overflow = Overflow.Hidden;
            _art.image = texture; _art.style.position = Position.Absolute; Add(_art);
            _sourceTexture = texture;
            if (ownerArt != null) _art.Configure(enemyId);
            RegisterCallback<GeometryChangedEvent>(_ => Place());
            var diagnostics = Debug.isDebugBuild || (Uri.TryCreate(UnityEngine.Application.absoluteURL, UriKind.Absolute, out var uri) && (uri.IsLoopback || uri.AbsolutePath.Contains("/dev/")));
            if (diagnostics) Debug.Log("ASHENSPIRE_ENEMY_ART " + new JObject { ["enemyId"] = enemyId, ["resource"] = resource, ["width"] = texture.width, ["height"] = texture.height }.ToString(Newtonsoft.Json.Formatting.None));
        }
        public void FeedbackTint(Color color) { _art.SetTint(color); }
        internal float ArtAspect => (float)_bounds[2] * _sourceTexture.width / ((float)_bounds[3] * _sourceTexture.height);
        private void Place()
        {
            if (contentRect.width <= 0 || contentRect.height <= 0) return;
            var texture = _sourceTexture;
            var left = (float)_bounds[0] * texture.width; var top = (float)_bounds[1] * texture.height;
            var width = (float)_bounds[2] * texture.width; var height = (float)_bounds[3] * texture.height;
            var scale = Math.Min(contentRect.width / width, contentRect.height / height);
            _art.style.width = texture.width * scale; _art.style.height = texture.height * scale;
            _art.style.left = contentRect.x + contentRect.width / 2 - (left + width / 2) * scale;
            _art.style.top = contentRect.y + contentRect.height - (top + height) * scale;
        }
    }
}
