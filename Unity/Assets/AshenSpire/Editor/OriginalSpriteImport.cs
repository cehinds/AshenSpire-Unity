// OriginalSpriteImport.cs — stable UI sprite import policy for original styles.
// EDITOR ONLY: BuildTools.ImportContent calls Configure before every export.
// MODIFY: add art under Resources/Art/Styles or Art/Enemies/Painted; update its catalog.
// Keep NPOT pixels and alpha edges intact; these UI figures do not need mipmaps,
// repeat wrapping, CPU-readable textures, or automatically generated physics shapes.
// Existing earlier-campaign art is intentionally outside this import directory.
using UnityEditor;
using UnityEngine;
namespace AshenSpire.Editor
{
    public static class OriginalSpriteImport
    {
        public static void Configure()
        {
            foreach (var path in AssetDatabase.GetAllAssetPaths())
            {
                var enemy = path.StartsWith("Assets/AshenSpire/Resources/Art/Enemies/Painted/", System.StringComparison.Ordinal);
                if ((!enemy && !path.StartsWith("Assets/AshenSpire/Resources/Art/Styles/", System.StringComparison.Ordinal)) || !path.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase)) continue;
                var maximum = enemy ? 512 : 2048;
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                if (importer.npotScale == TextureImporterNPOTScale.None && !importer.mipmapEnabled && importer.wrapMode == TextureWrapMode.Clamp && importer.alphaIsTransparency && !importer.isReadable && importer.maxTextureSize == maximum) continue;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.alphaIsTransparency = true;
                importer.isReadable = false;
                importer.maxTextureSize = maximum;
                importer.SaveAndReimport();
            }
            AssetDatabase.SaveAssets();
        }
    }
}
