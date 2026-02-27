#if UNITY_EDITOR
using System.Collections.Generic;
using ChessPrototype.Unity.Data;
using UnityEditor;
using UnityEngine;

namespace ChessPrototype.Unity.EditorSetup
{
    public sealed class AddLastChangesTool : EditorWindow
    {
        private string _bundleName = "EnemyRework";
        private string _rootFolder = "Assets/Unity/Data/Rework";

        [MenuItem("Tools/Add Last Changes/Add Last Changes...")]
        public static void Open()
        {
            var w = GetWindow<AddLastChangesTool>("Add Last Changes");
            w.minSize = new Vector2(420f, 180f);
        }

        private void OnGUI()
        {
            GUILayout.Label("Create Enemy Rework SO Bundle", EditorStyles.boldLabel);
            _bundleName = EditorGUILayout.TextField("Name", _bundleName);
            _rootFolder = EditorGUILayout.TextField("Root Folder", _rootFolder);

            GUILayout.Space(8f);
            if (GUILayout.Button("Create / Update Assets"))
            {
                CreateBundle(_bundleName, _rootFolder);
            }
        }

        private static void CreateBundle(string name, string rootFolder)
        {
            if (string.IsNullOrWhiteSpace(name)) name = "EnemyRework";
            if (string.IsNullOrWhiteSpace(rootFolder)) rootFolder = "Assets/Unity/Data/Rework";

            EnsureFolder(rootFolder);
            var bundleFolder = $"{rootFolder}/{name}";
            EnsureFolder(bundleFolder);

            var behaviorFolder = $"{bundleFolder}/Behaviors";
            var specialFolder = $"{bundleFolder}/Specials";
            var visualFolder = $"{bundleFolder}/IntentVisuals";
            var effectFolder = $"{bundleFolder}/GridEffects";
            EnsureFolder(behaviorFolder);
            EnsureFolder(specialFolder);
            EnsureFolder(visualFolder);
            EnsureFolder(effectFolder);

            var enemyKinds = new List<UnitKind>
            {
                UnitKind.Bat, UnitKind.Coyote, UnitKind.Spider, UnitKind.Toad, UnitKind.Skunk,
                UnitKind.Owl, UnitKind.Boar, UnitKind.Bear, UnitKind.WolfAlpha
            };

            foreach (var kind in enemyKinds)
            {
                var behavior = CreateOrLoad<EnemyBehaviorDefinition>($"{behaviorFolder}/{kind}_Behavior.asset");
                behavior.kind = kind;
                ApplyBehaviorDefaults(behavior);
                EditorUtility.SetDirty(behavior);

                var special = CreateOrLoad<EnemySpecialDefinition>($"{specialFolder}/{kind}_Special.asset");
                special.kind = kind;
                ApplySpecialDefaults(special);
                EditorUtility.SetDirty(special);

                var visual = CreateOrLoad<EnemyIntentVisualDefinition>($"{visualFolder}/{kind}_IntentVisual.asset");
                visual.kind = kind;
                EditorUtility.SetDirty(visual);

                var enemyDefPath = $"Assets/Unity/Data/Enemies/Enemy_{kind}.asset";
                var enemyDef = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(enemyDefPath);
                if (enemyDef != null)
                {
                    enemyDef.behavior = behavior;
                    enemyDef.special = special;
                    enemyDef.intentVisuals = visual;
                    EditorUtility.SetDirty(enemyDef);
                }
            }

            var web = CreateOrLoad<GridEffectDefinition>($"{effectFolder}/GE_Web_1x1.asset");
            web.type = GridEffectType.Web;
            web.displayName = "Web";
            web.turnsRemaining = 99;
            web.damageOnEnter = 1;
            web.rootedTurns = 1;
            web.destroyOnFirstTrigger = true;
            web.footprint = Vector2Int.one;
            EditorUtility.SetDirty(web);

            var spray = CreateOrLoad<GridEffectDefinition>($"{effectFolder}/GE_Spray_Line3_1Turn.asset");
            spray.type = GridEffectType.Spray;
            spray.displayName = "Spray";
            spray.turnsRemaining = 1;
            spray.damageOnEnter = 1;
            spray.damageOnStartTurn = 0;
            spray.destroyOnFirstTrigger = false;
            spray.footprint = new Vector2Int(3, 1);
            EditorUtility.SetDirty(spray);

            var cloud = CreateOrLoad<GridEffectDefinition>($"{effectFolder}/GE_Cloud_2x2_2Turns.asset");
            cloud.type = GridEffectType.Cloud;
            cloud.displayName = "Cloud";
            cloud.turnsRemaining = 2;
            cloud.damageOnEnter = 1;
            cloud.damageOnStartTurn = 1;
            cloud.destroyOnFirstTrigger = false;
            cloud.footprint = new Vector2Int(2, 2);
            EditorUtility.SetDirty(cloud);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Add Last Changes] Assets created/updated at {bundleFolder}");
        }

        private static void ApplyBehaviorDefaults(EnemyBehaviorDefinition behavior)
        {
            behavior.baseDamage = 1;
            behavior.attackTiles = 1;
            behavior.attackRange = 1;
            behavior.attackMode = EnemyAttackMode.MeleeSingle;
            behavior.moveMode = EnemyMoveMode.Step;
            behavior.moveRange = 1;
            behavior.ignoresOccupiedTiles = false;
            behavior.attackStopsAtFirstObject = true;
            behavior.attackRetargetsOnResolve = true;

            switch (behavior.kind)
            {
                case UnitKind.Bat:
                    behavior.attackMode = EnemyAttackMode.MeleeSingle;
                    behavior.moveMode = EnemyMoveMode.Fly;
                    behavior.moveRange = 2;
                    behavior.ignoresOccupiedTiles = true;
                    break;
                case UnitKind.Coyote:
                    behavior.attackMode = EnemyAttackMode.MeleeCluster;
                    behavior.attackTiles = 3;
                    behavior.moveRange = 2;
                    break;
                case UnitKind.Spider:
                    behavior.attackMode = EnemyAttackMode.MeleeSingle;
                    behavior.moveRange = 1;
                    break;
                case UnitKind.Toad:
                    behavior.attackMode = EnemyAttackMode.LinearProjectile;
                    behavior.attackRange = 2;
                    behavior.moveMode = EnemyMoveMode.Leap;
                    behavior.moveRange = 3;
                    behavior.ignoresOccupiedTiles = true;
                    break;
                case UnitKind.Skunk:
                    behavior.attackMode = EnemyAttackMode.LineHazard;
                    behavior.attackRange = 3;
                    behavior.moveRange = 1;
                    break;
                case UnitKind.Owl:
                    behavior.attackMode = EnemyAttackMode.LinearProjectile;
                    behavior.attackRange = 99;
                    behavior.moveMode = EnemyMoveMode.Fly;
                    behavior.moveRange = 2;
                    behavior.ignoresOccupiedTiles = true;
                    break;
                case UnitKind.Boar:
                    behavior.attackMode = EnemyAttackMode.Ram;
                    behavior.attackRange = 3;
                    behavior.moveMode = EnemyMoveMode.RamOnly;
                    behavior.moveRange = 3;
                    break;
                case UnitKind.Bear:
                    behavior.attackMode = EnemyAttackMode.Area2x2Front;
                    behavior.baseDamage = 2;
                    behavior.moveRange = 1;
                    break;
                case UnitKind.WolfAlpha:
                    behavior.attackMode = EnemyAttackMode.MeleeSingle;
                    behavior.baseDamage = 2;
                    behavior.moveRange = 2;
                    break;
            }
        }

        private static void ApplySpecialDefaults(EnemySpecialDefinition special)
        {
            special.type = EnemySpecialType.None;
            special.triggerChance = 0f;
            special.range = 1;
            special.radius = 1;
            special.turns = 1;
            special.amount = 1;
            special.footprint = Vector2Int.one;

            switch (special.kind)
            {
                case UnitKind.Bat:
                    special.type = EnemySpecialType.Shriek;
                    special.triggerChance = 0.15f;
                    break;
                case UnitKind.Coyote:
                    special.type = EnemySpecialType.PackHowl;
                    special.triggerChance = 0.20f;
                    special.amount = 1;
                    break;
                case UnitKind.Spider:
                    special.type = EnemySpecialType.WebTrap;
                    special.triggerChance = 0.25f;
                    special.range = 2;
                    special.turns = 99;
                    break;
                case UnitKind.Toad:
                    special.type = EnemySpecialType.SuperLeap;
                    special.triggerChance = 0.20f;
                    special.radius = 1;
                    break;
                case UnitKind.Skunk:
                    special.type = EnemySpecialType.StenchMissile;
                    special.triggerChance = 0.15f;
                    special.range = 3;
                    special.turns = 2;
                    special.footprint = new Vector2Int(2, 2);
                    break;
                case UnitKind.Owl:
                    special.type = EnemySpecialType.Sleep;
                    special.triggerChance = 0.20f;
                    special.turns = 1;
                    break;
                case UnitKind.Boar:
                    special.type = EnemySpecialType.Enrage;
                    special.triggerChance = 0.15f;
                    special.amount = 1;
                    break;
                case UnitKind.Bear:
                    special.type = EnemySpecialType.Rend;
                    special.triggerChance = 0.20f;
                    break;
                case UnitKind.WolfAlpha:
                    special.type = EnemySpecialType.AlphaCall;
                    special.triggerChance = 0.25f;
                    special.amount = 1;
                    break;
            }
        }

        private static T CreateOrLoad<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            var created = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(created, path);
            return created;
        }

        private static void EnsureFolder(string folderPath)
        {
            var parts = folderPath.Split('/');
            if (parts.Length < 2) return;
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }
    }
}
#endif
