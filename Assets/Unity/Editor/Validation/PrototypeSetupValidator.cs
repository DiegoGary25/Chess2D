#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using ChessPrototype.Unity.Core;
using ChessPrototype.Unity.Data;
using ChessPrototype.Unity.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ChessPrototype.Unity.EditorTools
{
    public static class PrototypeSetupValidator
    {
        private const string ConfigPath = "Assets/Unity/Data/Config/GameConfig.asset";
        private const string EncounterScenePath = "Assets/Unity/Scenes/EncounterScene.unity";
        private const string RunMapScenePath = "Assets/Unity/Scenes/RunMapScene.unity";

        public static void RunAndLog()
        {
            var report = Validate();
            if (report.errors == 0) Debug.Log(report.text);
            else Debug.LogWarning(report.text);
        }

        public static (int errors, string text) Validate()
        {
            var errors = 0;
            var warns = 0;
            var created = new List<string>();
            var missing = new List<string>();
            var notes = new List<string>();

            CheckFolder("Assets/Unity", ref errors, created, missing);
            CheckFolder("Assets/Unity/Data", ref errors, created, missing);
            CheckFolder("Assets/Unity/Scenes", ref errors, created, missing);

            var cfg = AssetDatabase.LoadAssetAtPath<GameConfigDefinition>(ConfigPath);
            if (cfg == null)
            {
                errors += 1;
                missing.Add($"Missing config asset: {ConfigPath}");
            }
            else
            {
                created.Add($"Config asset found: {ConfigPath}");
                if (cfg.runMap == null) { errors += 1; missing.Add("GameConfig.runMap is null"); }
                if (cfg.encounters == null || cfg.encounters.Count == 0) { errors += 1; missing.Add("GameConfig.encounters is empty"); }
                if (cfg.starterDeck == null || cfg.starterDeck.Count == 0) { errors += 1; missing.Add("GameConfig.starterDeck is empty"); }
                if (cfg.enemyDefinitions == null || cfg.enemyDefinitions.Count == 0) { warns += 1; notes.Add("No enemy definitions assigned."); }
                if (cfg.pieceDefinitions == null || cfg.pieceDefinitions.Count == 0) { warns += 1; notes.Add("No piece definitions assigned."); }
                ValidateMap(cfg.runMap, ref errors, missing, notes);
            }

            ValidateScene(EncounterScenePath, ref errors, ref warns, created, missing, notes);
            ValidateScene(RunMapScenePath, ref errors, ref warns, created, missing, notes);

            var sb = new StringBuilder();
            sb.AppendLine("[ChessPrototype] Validation Report");
            sb.AppendLine($"Errors: {errors} | Warnings: {warns}");
            if (created.Count > 0)
            {
                sb.AppendLine("Found:");
                for (var i = 0; i < created.Count; i++) sb.AppendLine($"- {created[i]}");
            }
            if (missing.Count > 0)
            {
                sb.AppendLine("Missing/Broken:");
                for (var i = 0; i < missing.Count; i++) sb.AppendLine($"- {missing[i]}");
            }
            if (notes.Count > 0)
            {
                sb.AppendLine("Notes:");
                for (var i = 0; i < notes.Count; i++) sb.AppendLine($"- {notes[i]}");
            }
            return (errors, sb.ToString());
        }

        private static void ValidateScene(string path, ref int errors, ref int warns, List<string> created, List<string> missing, List<string> notes)
        {
            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            if (sceneAsset == null)
            {
                errors += 1;
                missing.Add($"Missing scene: {path}");
                return;
            }

            created.Add($"Scene found: {path}");
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

            if (Object.FindObjectOfType<Camera>(true) == null) { errors += 1; missing.Add($"{path}: missing Camera"); }
            if (Object.FindObjectOfType<Canvas>(true) == null) { errors += 1; missing.Add($"{path}: missing Canvas"); }
            if (Object.FindObjectOfType<EventSystem>(true) == null) { errors += 1; missing.Add($"{path}: missing EventSystem"); }

            var bootstrap = Object.FindObjectOfType<PrototypeBootstrap>(true);
            if (Object.FindObjectOfType<InputModeController>(true) == null)
            {
                warns += 1;
                notes.Add($"{path}: InputModeController missing (optional but recommended).");
            }
            if (bootstrap == null)
            {
                errors += 1;
                missing.Add($"{path}: missing PrototypeBootstrap");
            }
            else
            {
                CheckSerializedRef(bootstrap, "session", path, ref errors, missing);
                CheckSerializedRef(bootstrap, "turn", path, ref errors, missing);
                CheckSerializedRef(bootstrap, "cards", path, ref errors, missing);
                CheckSerializedRef(bootstrap, "encounter", path, ref errors, missing);
                CheckSerializedRef(bootstrap, "map", path, ref errors, missing);
                CheckSerializedRef(bootstrap, "ui", path, ref errors, missing);
            }

            var ui = Object.FindObjectOfType<PrototypeUIController>(true);
            if (ui == null)
            {
                errors += 1;
                missing.Add($"{path}: missing PrototypeUIController");
            }
            else
            {
                CheckSerializedRef(ui, "mapPanel", path, ref errors, missing);
                CheckSerializedRef(ui, "battlePanel", path, ref errors, missing);
                CheckSerializedRef(ui, "cardsPanel", path, ref errors, missing);
                CheckSerializedRef(ui, "piecePanel", path, ref errors, missing);
                CheckSerializedRef(ui, "cardRoot", path, ref errors, missing);
                CheckSerializedRef(ui, "mapNodeRoot", path, ref errors, missing);
            }

            var staging = GameObject.Find("__PrefabStaging");
            if (staging == null)
            {
                warns += 1;
                notes.Add($"{path}: __PrefabStaging missing (optional, can regenerate from menu)");
            }
        }

        private static void ValidateMap(RunMapDefinition map, ref int errors, List<string> missing, List<string> notes)
        {
            if (map == null) return;
            var idSet = new HashSet<string>();
            for (var i = 0; i < map.nodes.Count; i++)
            {
                var n = map.nodes[i];
                if (string.IsNullOrWhiteSpace(n.id))
                {
                    errors += 1;
                    missing.Add("RunMap node has empty id.");
                    continue;
                }
                if (!idSet.Add(n.id))
                {
                    errors += 1;
                    missing.Add($"RunMap duplicate node id: {n.id}");
                }
            }

            for (var i = 0; i < map.edges.Count; i++)
            {
                var e = map.edges[i];
                if (!idSet.Contains(e.from) || !idSet.Contains(e.to))
                {
                    errors += 1;
                    missing.Add($"RunMap edge references missing node: {e.from} -> {e.to}");
                }
            }

            if (map.nodes.Count > 0 && map.edges.Count == 0) notes.Add("RunMap has nodes but no edges.");
        }

        private static void CheckSerializedRef(Object obj, string field, string scenePath, ref int errors, List<string> missing)
        {
            var so = new SerializedObject(obj);
            var p = so.FindProperty(field);
            if (p == null || p.objectReferenceValue == null)
            {
                errors += 1;
                missing.Add($"{scenePath}: {obj.GetType().Name}.{field} is not assigned");
            }
        }

        private static void CheckFolder(string path, ref int errors, List<string> found, List<string> missing)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                errors += 1;
                missing.Add($"Missing folder: {path}");
                return;
            }
            found.Add($"Folder found: {path}");
        }
    }
}
#endif

