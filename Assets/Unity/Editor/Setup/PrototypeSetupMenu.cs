#if UNITY_EDITOR
using ChessPrototype.Unity.EditorTools;
using UnityEditor;

namespace ChessPrototype.Unity.EditorSetup
{
    public static class PrototypeSetupMenu
    {
        [MenuItem("Tools/ChessPrototype/Generate Data Assets")]
        public static void GenerateDataAssets() => PrototypeDataGenerator.GenerateAll();

        [MenuItem("Tools/ChessPrototype/Generate Prototype Scene")]
        public static void GeneratePrototypeScene() => PrototypeSceneGenerator.GeneratePrototypeScene();

        [MenuItem("Tools/ChessPrototype/Generate Encounter Scene")]
        public static void GenerateEncounterScene() => PrototypeSceneGenerator.GenerateEncounterScene();

        [MenuItem("Tools/ChessPrototype/Generate RunMap Scene")]
        public static void GenerateRunMapScene() => PrototypeSceneGenerator.GenerateRunMapScene();

        [MenuItem("Tools/ChessPrototype/Generate Prefab Staging")]
        public static void GeneratePrefabStaging() => PrototypeSceneGenerator.GeneratePrefabStaging();

        [MenuItem("Tools/ChessPrototype/Auto-Link References")]
        public static void AutoLinkReferences() => PrototypeSceneGenerator.AutoLinkOpenScene();

        [MenuItem("Tools/ChessPrototype/Validate Setup")]
        public static void ValidateSetup() => PrototypeSetupValidator.RunAndLog();

        [MenuItem("Tools/ChessPrototype/Generate All (Prototype)")]
        public static void GenerateAllPrototype()
        {
            PrototypeDataGenerator.GenerateAll();
            PrototypeSceneGenerator.GeneratePrototypeScene();
            PrototypeSetupValidator.RunAndLog();
        }
    }
}
#endif
