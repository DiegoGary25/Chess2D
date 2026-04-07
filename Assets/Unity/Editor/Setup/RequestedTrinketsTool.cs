#if UNITY_EDITOR
using System.Collections.Generic;
using ChessPrototype.Unity.Data;
using UnityEditor;
using UnityEngine;

namespace ChessPrototype.Unity.EditorSetup
{
    public static class RequestedTrinketsTool
    {
        private const string TrinketsDir = "Assets/Unity/Data/Trinkets/New";
        private const string GameConfigAssetPath = "Assets/Unity/Data/Enemies/Config/GameConfig.asset";

        [MenuItem("Tools/Add Last Changes/Create Requested Trinkets (Run)")]
        public static void CreateRequestedTrinkets()
        {
            EnsureFolder(TrinketsDir);

            var cfg = AssetDatabase.LoadAssetAtPath<GameConfigDefinition>(GameConfigAssetPath);
            if (cfg == null)
            {
                Debug.LogError($"[Requested Trinkets] Missing config asset at {GameConfigAssetPath}");
                return;
            }

            var created = new List<TrinketDefinition>();
            var specs = BuildSpecs();
            for (var i = 0; i < specs.Count; i++)
            {
                var spec = specs[i];
                var path = $"{TrinketsDir}/Trinket_{spec.id}.asset";
                var trinket = AssetDatabase.LoadAssetAtPath<TrinketDefinition>(path);
                if (trinket == null)
                {
                    trinket = ScriptableObject.CreateInstance<TrinketDefinition>();
                    AssetDatabase.CreateAsset(trinket, path);
                }

                trinket.name = $"Trinket_{spec.id}";
                trinket.trinketId = spec.id;
                trinket.displayName = spec.displayName;
                trinket.description = spec.description;
                trinket.shopCost = spec.shopCost;
                trinket.allowDuplicates = false;
                trinket.effectType = TrinketEffectType.None;
                trinket.amount = 1;
                if (trinket.tags == null) trinket.tags = new List<SynergyTag>();
                trinket.tags.Clear();
                trinket.tags.AddRange(spec.tags);
                EditorUtility.SetDirty(trinket);
                created.Add(trinket);
            }

            cfg.trinketDefinitions = MergeUnique(cfg.trinketDefinitions, created);
            cfg.shopTrinketPool = MergeUnique(cfg.shopTrinketPool, created);
            EditorUtility.SetDirty(cfg);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Requested Trinkets] Created/updated {created.Count} trinkets and auto-assigned to GameConfig.");
        }

        private static List<TrinketSpec> BuildSpecs()
        {
            return new List<TrinketSpec>
            {
                new TrinketSpec(
                    "pawn_brokers_seal",
                    "Pawn Broker's Seal",
                    "The first Pawn you summon each combat costs 1 less energy.",
                    70,
                    SynergyTag.Pawn,
                    SynergyTag.Economy),
                new TrinketSpec(
                    "last_rank_medal",
                    "Last Rank Medal",
                    "When a Pawn promotes into a Queen, it gains Shield 4 and Battle Focus.",
                    110,
                    SynergyTag.Pawn,
                    SynergyTag.Queen),
                new TrinketSpec(
                    "marching_banner",
                    "Marching Banner",
                    "The first move action each turn gains +1 tile movement.",
                    95,
                    SynergyTag.Tempo),
                new TrinketSpec(
                    "royal_plate",
                    "Royal Plate",
                    "The King takes 1 less damage from first hit.",
                    60,
                    SynergyTag.Fortify,
                    SynergyTag.Objective),
                new TrinketSpec(
                    "crown_lantern",
                    "Crown Lantern",
                    "Adjacent allied units to the King gain +1 attack.",
                    100,
                    SynergyTag.Support,
                    SynergyTag.Objective),
                new TrinketSpec(
                    "sanctified_ash",
                    "Sanctified Ash",
                    "The first healing card you play each combat also grants Shield 1.",
                    60,
                    SynergyTag.Sustain,
                    SynergyTag.Support),
                new TrinketSpec(
                    "charm",
                    "Charm",
                    "Units affected by Forced March gain +1 attack on their next attack this turn.",
                    60,
                    SynergyTag.Tempo,
                    SynergyTag.Support),
                new TrinketSpec(
                    "wire",
                    "Wire",
                    "Enemies rooted by your effects take +1 damage from attacks.",
                    60,
                    SynergyTag.Control),
                new TrinketSpec(
                    "purse",
                    "Purse",
                    "Gain +15 gold at the end of each encounter.",
                    60,
                    SynergyTag.Economy),
                new TrinketSpec(
                    "knight_appetite",
                    "Knight Appetite",
                    "The first time a Knight captures each turn, gain +1 movement on that Knight this turn.",
                    95,
                    SynergyTag.Knight,
                    SynergyTag.Tempo),
                new TrinketSpec(
                    "bishop_appetite",
                    "Bishop Appetite",
                    "The first time a Bishop captures each turn, the target's row or diagonal becomes Rooted for 1 turn.",
                    100,
                    SynergyTag.Bishop,
                    SynergyTag.Control),
                new TrinketSpec(
                    "rook_appetite",
                    "Rook Appetite",
                    "The first time a Rook captures each turn, it gains Shield 3.",
                    90,
                    SynergyTag.Rook,
                    SynergyTag.Fortify),
                new TrinketSpec(
                    "queen_appetite",
                    "Queen Appetite",
                    "The first capture each turn by a Queen restores 1 energy.",
                    150,
                    SynergyTag.Queen,
                    SynergyTag.Economy),
                new TrinketSpec(
                    "king_appetite",
                    "King Appetite",
                    "When the King captures, heal 2 HP and gain Shield 2.",
                    250,
                    SynergyTag.Objective,
                    SynergyTag.Sustain),
                new TrinketSpec(
                    "knight_greaves",
                    "Knight Greaves",
                    "Knights deal +2 damage if they moved 3 or more tiles before attacking this turn.",
                    90,
                    SynergyTag.Knight,
                    SynergyTag.Tempo),
                new TrinketSpec(
                    "bishop_greaves",
                    "Bishop Greaves",
                    "Bishops gain +2 damage on their next attack if they moved 2 or more diagonal tiles this turn.",
                    85,
                    SynergyTag.Bishop,
                    SynergyTag.Tempo),
                new TrinketSpec(
                    "rook_greaves",
                    "Rook Greaves",
                    "After a Rook moves 3 or more tiles, it gains Shield 2.",
                    80,
                    SynergyTag.Rook,
                    SynergyTag.Fortify),
                new TrinketSpec(
                    "queen_greaves",
                    "Queen Greaves",
                    "Queens gain +2 damage on their next attack if they moved 2 or more tiles this turn.",
                    115,
                    SynergyTag.Queen,
                    SynergyTag.Tempo),
                new TrinketSpec(
                    "king_greaves",
                    "King Greaves",
                    "After the King moves, gain Shield 2.",
                    95,
                    SynergyTag.Objective,
                    SynergyTag.Fortify),
                new TrinketSpec(
                    "kings_guard",
                    "King's Guard",
                    "If any allied piece ends up in the same column as the King, it gains +1 Shield (not the King).",
                    100,
                    SynergyTag.Objective,
                    SynergyTag.Support)
            };
        }

        private static List<TrinketDefinition> MergeUnique(List<TrinketDefinition> existing, List<TrinketDefinition> incoming)
        {
            var merged = new List<TrinketDefinition>();
            var seen = new HashSet<TrinketDefinition>();

            if (existing != null)
            {
                for (var i = 0; i < existing.Count; i++)
                {
                    var trinket = existing[i];
                    if (trinket == null || !seen.Add(trinket)) continue;
                    merged.Add(trinket);
                }
            }

            if (incoming != null)
            {
                for (var i = 0; i < incoming.Count; i++)
                {
                    var trinket = incoming[i];
                    if (trinket == null || !seen.Add(trinket)) continue;
                    merged.Add(trinket);
                }
            }

            return merged;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parts = path.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private sealed class TrinketSpec
        {
            public readonly string id;
            public readonly string displayName;
            public readonly string description;
            public readonly int shopCost;
            public readonly SynergyTag[] tags;

            public TrinketSpec(string id, string displayName, string description, int shopCost, params SynergyTag[] tags)
            {
                this.id = id;
                this.displayName = displayName;
                this.description = description;
                this.shopCost = shopCost;
                this.tags = tags ?? new SynergyTag[0];
            }
        }
    }
}
#endif
