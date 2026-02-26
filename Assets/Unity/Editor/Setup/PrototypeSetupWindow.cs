#if UNITY_EDITOR
using ChessPrototype.Unity.EditorTools;
using UnityEditor;
using UnityEngine;

namespace ChessPrototype.Unity.EditorSetup
{
    public sealed class PrototypeSetupWindow : EditorWindow
    {
        [MenuItem("Tools/ChessPrototype/Setup Window")]
        public static void Open()
        {
            var w = GetWindow<PrototypeSetupWindow>("Chess Prototype Setup");
            w.minSize = new Vector2(380, 300);
        }

        private void OnGUI()
        {
            GUILayout.Label("Chess Prototype Generator", EditorStyles.boldLabel);
            GUILayout.Space(6);

            if (GUILayout.Button("Generate Data Assets")) PrototypeDataGenerator.GenerateAll();
            if (GUILayout.Button("Generate Prototype Scene (Merged)")) PrototypeSceneGenerator.GeneratePrototypeScene();
            if (GUILayout.Button("Generate Encounter Scene")) PrototypeSceneGenerator.GenerateEncounterScene();
            if (GUILayout.Button("Generate RunMap Scene")) PrototypeSceneGenerator.GenerateRunMapScene();
            if (GUILayout.Button("Generate Prefab Staging (Active Scene)")) PrototypeSceneGenerator.GeneratePrefabStaging();
            if (GUILayout.Button("Auto-Link References (Active Scene)")) PrototypeSceneGenerator.AutoLinkOpenScene();
            if (GUILayout.Button("Validate Setup")) PrototypeSetupValidator.RunAndLog();

            GUILayout.Space(10);
            if (GUILayout.Button("Generate All (Prototype)", GUILayout.Height(32)))
            {
                PrototypeDataGenerator.GenerateAll();
                PrototypeSceneGenerator.GeneratePrototypeScene();
                PrototypeSetupValidator.RunAndLog();
            }

            GUILayout.Space(8);
            EditorGUILayout.HelpBox(
                "Use Generate All first. Open Assets/Unity/Scenes/PrototypeScene.unity, then Auto-Link if needed.",
                MessageType.Info);
        }
    }
}
#endif
