using UnityEngine;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation
{
    // UI bridge keeps native card hit testing while artwork is an editable 2D prefab.
    // Each portrait has a finite lifetime; no Update loop or global actor registry.
    public sealed class CombatActorImage : Image
    {
        private GameObject prefab;
        private CombatActor2D actor;
        private RenderTexture portrait;
        private static int nextStudio;
        private Color actorTint = Color.white;
        public CombatActorImage()
        {
            pickingMode = PickingMode.Ignore;
            RegisterCallback<AttachToPanelEvent>(_ => Mount());
            RegisterCallback<DetachFromPanelEvent>(_ => Release());
        }
        public void Configure(string key)
        {
            prefab = Resources.Load<GameObject>("Combatants2D/" + key);
            if (prefab != null && panel != null) Mount();
        }
        public void SetTint(Color color)
        {
            actorTint = color;
            if (actor != null) actor.Render(portrait, color);
            else tintColor = color;
        }
        private void Mount()
        {
            if (prefab == null || actor != null) return;
            var instance = Object.Instantiate(prefab);
            instance.name = "Combat portrait - " + prefab.name;
            instance.transform.position = new Vector3(10000 + (++nextStudio % 1000) * 40, 10000, 0);
            actor = instance.GetComponent<CombatActor2D>();
            var sprite = actor.Artwork.sprite;
            var aspect = sprite.bounds.size.x / sprite.bounds.size.y;
            portrait = new RenderTexture(Mathf.Max(1, Mathf.RoundToInt(768 * aspect)), 768, 16, RenderTextureFormat.ARGB32);
            portrait.name = "Combat sprite " + prefab.name;
            portrait.Create();
            actor.Render(portrait, actorTint);
            image = portrait;
            tintColor = Color.white;
        }
        private void Release()
        {
            if (actor != null) Object.Destroy(actor.gameObject);
            actor = null;
            if (portrait != null) { image = null; portrait.Release(); Object.Destroy(portrait); }
            portrait = null;
        }
    }
}
