using System.IO;
using AshenSpire.Presentation;
using UnityEditor;
using UnityEngine;

namespace AshenSpire.Editor
{
    public static class CombatSpriteAuthoring
    {
        [MenuItem("AshenSpire/Art/Rebuild painted 2D combat prefabs")]
        public static void Build()
        {
            const string resources = "Assets/AshenSpire/Resources/";
            var cardBack = (TextureImporter)AssetImporter.GetAtPath(resources + "Art/CombatRefresh/ember-card-back.png");
            cardBack.npotScale = TextureImporterNPOTScale.None;
            cardBack.maxTextureSize = 2048;
            cardBack.mipmapEnabled = false;
            cardBack.alphaIsTransparency = true;
            cardBack.textureCompression = TextureImporterCompression.Uncompressed;
            cardBack.SaveAndReimport();
            Directory.CreateDirectory(resources + "Combatants2D");
            foreach (var key in new[] { "reaver", "herald", "rogue", "starseer", "wanderingSoldier", "blightHound", "charredColossus" })
            {
                var path = resources + (key == "wanderingSoldier" ? "Art/CombatRefresh/wanderingSoldier-hilt-v2.png" : "Art/OwnerAppearance/" + key + ".png");
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = 8192;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                settings.spriteAlignment = (int)SpriteAlignment.Center;
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null) throw new System.InvalidOperationException("Missing combat sprite " + key);
                var root = new GameObject(key);
                var renderer = root.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                root.AddComponent<CombatActor2D>().Configure(renderer);
                PrefabUtility.SaveAsPrefabAsset(root, resources + "Combatants2D/" + key + ".prefab");
                Object.DestroyImmediate(root);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Combat 2D: seven custom painted SpriteRenderer prefabs authored and validated.");
        }
    }
}
