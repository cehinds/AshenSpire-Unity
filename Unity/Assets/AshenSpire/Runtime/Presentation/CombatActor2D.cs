using UnityEngine;

namespace AshenSpire.Presentation
{
    // Authored SpriteRenderer prefab, rendered only when its portrait or tint changes.
    // The existing combat presenter remains the sole owner of rules and targeting.
    public sealed class CombatActor2D : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer artwork;
        private Camera portraitCamera;
        public SpriteRenderer Artwork => artwork;
        public void Configure(SpriteRenderer renderer) => artwork = renderer;
        public void Render(RenderTexture target, Color tint)
        {
            if (portraitCamera == null)
            {
                var cameraObject = new GameObject("Portrait camera");
                cameraObject.transform.SetParent(transform, false);
                cameraObject.transform.localPosition = new Vector3(0, 0, -10);
                portraitCamera = cameraObject.AddComponent<Camera>();
                portraitCamera.enabled = false;
                portraitCamera.orthographic = true;
                portraitCamera.clearFlags = CameraClearFlags.SolidColor;
                portraitCamera.backgroundColor = Color.clear;
                portraitCamera.cullingMask = 1 << 31;
                portraitCamera.nearClipPlane = .1f;
                portraitCamera.farClipPlane = 20;
                portraitCamera.allowHDR = false;
                portraitCamera.allowMSAA = false;
            }
            artwork.gameObject.layer = 31;
            artwork.color = tint;
            portraitCamera.orthographicSize = artwork.sprite.bounds.size.y / 2;
            portraitCamera.aspect = (float)target.width / target.height;
            portraitCamera.targetTexture = target;
            portraitCamera.Render();
        }
    }
}
