// OriginalSpriteImport.cs — stable UI sprite import policy for original styles.
// EDITOR ONLY: BuildTools.ImportContent calls Configure before every export.
// MODIFY: add PNG artwork under Resources/Art/Styles and update sprite-styles.json.
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
                if (!path.StartsWith("Assets/AshenSpire/Resources/Art/Styles/", System.StringComparison.Ordinal) || !path.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase)) continue;
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                if (importer.npotScale == TextureImporterNPOTScale.None && !importer.mipmapEnabled && importer.wrapMode == TextureWrapMode.Clamp && importer.alphaIsTransparency && !importer.isReadable && importer.maxTextureSize == 2048) continue;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.alphaIsTransparency = true;
                importer.isReadable = false;
                importer.maxTextureSize = 2048;
                importer.SaveAndReimport();
            }
            AssetDatabase.SaveAssets();
        }
    }
}
