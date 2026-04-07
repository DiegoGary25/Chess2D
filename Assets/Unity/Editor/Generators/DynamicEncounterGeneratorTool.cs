#if UNITY_EDITOR
using System.Collections.Generic;
using ChessPrototype.Unity.Data;
using UnityEditor;
using UnityEngine;

namespace ChessPrototype.Unity.EditorTools
{
    public static class DynamicEncounterGeneratorTool
    {
        private const string EncountersDir = "Assets/Unity/Data/Encounters";
        private const string ConfigPath = "Assets/Unity/Data/Config/GameConfig.asset";

        [MenuItem("Tools/Chess Prototype/Generate Dynamic Encounters (First 5)")]
        public static void GenerateFirstFiveDynamicEncounters()
        {
            EnsureFolder("Assets/Unity");
            EnsureFolder("Assets/Unity/Data");
            EnsureFolder(EncountersDir);

            var generated = new List<EncounterTemplateDefinition>();
            var difficulties = new[] { 3f, 4.5f, 6.5f, 8.5f, 11f };
            for (var i = 0; i < 5; i++)
            {
                var idx = i + 1;
                var path = $"{EncountersDir}/E{idx:00}.asset";
                var encounter = GetOrCreate<EncounterTemplateDefinition>(path);
                encounter.encounterId = $"E{idx:00}";
                encounter.boardSize = 4 + Mathf.Min(2, i / 2);
                encounter.useDynamicGeneration = true;
                encounter.chessOnlyEncounter = true;
                encounter.createBeacons = idx >= 3;
                encounter.difficultyBudget = difficulties[i];
                encounter.caveDifficultyThreshold = 7f;
                encounter.requireCaveWhenThresholdReached = true;
                encounter.dynamicSeedOffset = idx * 137;
                encounter.enemyPlacements = new List<Placement>();
                encounter.caves = new List<CaveTemplate>();
                encounter.beacons = encounter.createBeacons ? BuildDefaultBeacons(encounter.boardSize, idx) : new List<BeaconTemplate>();
                EditorUtility.SetDirty(encounter);
                generated.Add(encounter);
            }

            var cfg = AssetDatabase.LoadAssetAtPath<GameConfigDefinition>(ConfigPath);
            if (cfg != null)
            {
                var list = cfg.encounters != null ? new List<EncounterTemplateDefinition>(cfg.encounters) : new List<EncounterTemplateDefinition>();
                while (list.Count < 5) list.Add(null);
                for (var i = 0; i < 5; i++) list[i] = generated[i];
                cfg.encounters = list;
                EditorUtility.SetDirty(cfg);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ChessPrototype] Generated first 5 dynamic encounters.");
        }

        private static List<BeaconTemplate> BuildDefaultBeacons(int boardSize, int idx)
        {
            var row = Mathf.Clamp(boardSize - 2, 0, Mathf.Max(0, boardSize - 1));
            return new List<BeaconTemplate>
            {
                new BeaconTemplate { id = $"B{idx}_L", row = row, col = 1, maxHp = 12 },
                new BeaconTemplate { id = $"B{idx}_R", row = row, col = Mathf.Max(0, boardSize - 2), maxHp = 12 }
            };
        }

        private static T GetOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;
            var parent = System.IO.Path.GetDirectoryName(folderPath)?.Replace("\\", "/");
            var name = System.IO.Path.GetFileName(folderPath);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
#endif
