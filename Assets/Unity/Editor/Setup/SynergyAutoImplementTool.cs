#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using ChessPrototype.Unity.Data;
using UnityEditor;
using UnityEngine;

namespace ChessPrototype.Unity.EditorSetup
{
    public sealed class SynergyAutoImplementTool : EditorWindow
    {
        private const string CardsRoot = "Assets/Unity/Data/Cards";
        private const string SynergyCardsDir = "Assets/Unity/Data/Cards/Synergy";
        private const string TrinketsDir = "Assets/Unity/Data/Trinkets";
        private const string GameConfigAssetPath = "Assets/Unity/Data/Config/GameConfig.asset";

        [MenuItem("Tools/Add Last Changes/Auto Implement Synergy...")]
        public static void Open()
        {
            var w = GetWindow<SynergyAutoImplementTool>("Auto Synergy");
            w.minSize = new Vector2(440f, 210f);
        }

        [MenuItem("Tools/Add Last Changes/Auto Implement Synergy (Run)")]
        public static void AutoImplementMenu()
        {
            AutoImplement();
        }

        [MenuItem("Tools/Add Last Changes/Rollback Synergy (Run)")]
        public static void RollbackMenu()
        {
            RollbackLastSynergy();
        }

        [MenuItem("Tools/Add Last Changes/Add All Cards To Starter Deck (Run)")]
        public static void AddAllCardsToStarterDeckMenu()
        {
            AddAllCardsToStarterDeck();
        }

        [MenuItem("Tools/Add Last Changes/Create Vibe Trinkets (Run)")]
        public static void CreateVibeTrinketsMenu()
        {
            CreateVibeTrinketsOnly();
        }

        private void OnGUI()
        {
            GUILayout.Label("Synergy Content Auto-Implement", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Creates/updates synergy content in data assets:\n" +
                "- Adds tags to existing cards\n" +
                "- Creates custom cards + animal summon cards\n" +
                "- Creates 15 new trinkets\n" +
                "- Updates GameConfig shop pools",
                MessageType.Info);

            GUILayout.Space(8f);
            if (GUILayout.Button("Auto Implement Synergy Content"))
            {
                AutoImplement();
            }

            GUILayout.Space(6f);
            if (GUILayout.Button("Rollback Last Synergy Content"))
            {
                RollbackLastSynergy();
            }

            GUILayout.Space(6f);
            if (GUILayout.Button("Add All Cards To Starter Deck"))
            {
                AddAllCardsToStarterDeck();
            }

            GUILayout.Space(6f);
            if (GUILayout.Button("Create Vibe Trinkets (SO + Shop)"))
            {
                CreateVibeTrinketsOnly();
            }
        }

        public static void AutoImplement()
        {
            EnsureFolder(CardsRoot);
            EnsureFolder(SynergyCardsDir);
            EnsureFolder(TrinketsDir);

            var cfg = AssetDatabase.LoadAssetAtPath<GameConfigDefinition>(GameConfigAssetPath);
            if (cfg == null)
            {
                Debug.LogError($"[Synergy Auto] Missing config asset at {GameConfigAssetPath}");
                return;
            }

            var allCards = LoadAllCards();
            TagExistingCards(allCards);

            var createdCards = CreateOrUpdateNewCards(allCards);
            RemoveLegacyGeneratedCards();
            allCards = LoadAllCards();

            var createdTrinkets = CreateOrUpdateTrinkets();
            RemoveLegacyGeneratedTrinkets();
            var allTrinkets = LoadAllTrinkets();

            ApplyRecommendedStarterDeck(cfg, allCards);
            cfg.shopCardPool = MergeUniqueCards(cfg.shopCardPool, allCards);
            cfg.trinketDefinitions = MergeUniqueTrinkets(cfg.trinketDefinitions, allTrinkets);
            cfg.shopTrinketPool = MergeUniqueTrinkets(cfg.shopTrinketPool, createdTrinkets);
            EditorUtility.SetDirty(cfg);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                $"[Synergy Auto] Done. Tagged existing cards, created/updated {createdCards.Count} cards and {createdTrinkets.Count} trinkets.");
        }

        private static void RemoveLegacyGeneratedCards()
        {
            var current = new HashSet<string>(NewCardIds(), StringComparer.OrdinalIgnoreCase);
            var legacy = LegacyGeneratedCardIds();
            for (var i = 0; i < legacy.Length; i++)
            {
                var id = legacy[i];
                if (string.IsNullOrWhiteSpace(id) || current.Contains(id)) continue;
                var path = $"{SynergyCardsDir}/Card_{id}.asset";
                if (AssetDatabase.LoadAssetAtPath<CardDefinition>(path) == null) continue;
                AssetDatabase.DeleteAsset(path);
            }
        }

        private static void RemoveLegacyGeneratedTrinkets()
        {
            var current = new HashSet<string>(NewTrinketIds(), StringComparer.OrdinalIgnoreCase);
            var legacy = LegacyGeneratedTrinketIds();
            for (var i = 0; i < legacy.Length; i++)
            {
                var id = legacy[i];
                if (string.IsNullOrWhiteSpace(id) || current.Contains(id)) continue;
                var path = $"{TrinketsDir}/Trinket_{id}.asset";
                if (AssetDatabase.LoadAssetAtPath<TrinketDefinition>(path) == null) continue;
                AssetDatabase.DeleteAsset(path);
            }
        }

        public static void RollbackLastSynergy()
        {
            EnsureFolder(CardsRoot);
            EnsureFolder(TrinketsDir);

            var cfg = AssetDatabase.LoadAssetAtPath<GameConfigDefinition>(GameConfigAssetPath);
            if (cfg == null)
            {
                Debug.LogError($"[Synergy Rollback] Missing config asset at {GameConfigAssetPath}");
                return;
            }

            var removedCardAssets = 0;
            var removedTrinketAssets = 0;

            var newCardIds = NewCardIds();
            for (var i = 0; i < newCardIds.Length; i++)
            {
                var path = $"{SynergyCardsDir}/Card_{newCardIds[i]}.asset";
                if (AssetDatabase.LoadAssetAtPath<CardDefinition>(path) == null) continue;
                if (AssetDatabase.DeleteAsset(path)) removedCardAssets += 1;
            }
            var legacyCardIds = LegacyGeneratedCardIds();
            for (var i = 0; i < legacyCardIds.Length; i++)
            {
                var path = $"{SynergyCardsDir}/Card_{legacyCardIds[i]}.asset";
                if (AssetDatabase.LoadAssetAtPath<CardDefinition>(path) == null) continue;
                if (AssetDatabase.DeleteAsset(path)) removedCardAssets += 1;
            }

            var trinketIds = NewTrinketIds();
            for (var i = 0; i < trinketIds.Length; i++)
            {
                var path = $"{TrinketsDir}/Trinket_{trinketIds[i]}.asset";
                if (AssetDatabase.LoadAssetAtPath<TrinketDefinition>(path) == null) continue;
                if (AssetDatabase.DeleteAsset(path)) removedTrinketAssets += 1;
            }
            var legacyTrinketIds = LegacyGeneratedTrinketIds();
            for (var i = 0; i < legacyTrinketIds.Length; i++)
            {
                var path = $"{TrinketsDir}/Trinket_{legacyTrinketIds[i]}.asset";
                if (AssetDatabase.LoadAssetAtPath<TrinketDefinition>(path) == null) continue;
                if (AssetDatabase.DeleteAsset(path)) removedTrinketAssets += 1;
            }

            var remainingCards = LoadAllCards();
            for (var i = 0; i < remainingCards.Count; i++)
            {
                var card = remainingCards[i];
                if (card == null) continue;
                if (card.tags == null || card.tags.Count == 0) continue;
                card.tags.Clear();
                EditorUtility.SetDirty(card);
            }

            var remainingTrinkets = LoadAllTrinkets();
            for (var i = 0; i < remainingTrinkets.Count; i++)
            {
                var trinket = remainingTrinkets[i];
                if (trinket == null) continue;
                if (trinket.tags == null || trinket.tags.Count == 0) continue;
                trinket.tags.Clear();
                EditorUtility.SetDirty(trinket);
            }

            cfg.shopCardPool = MergeUniqueCards(new List<CardDefinition>(), remainingCards);
            cfg.trinketDefinitions = MergeUniqueTrinkets(new List<TrinketDefinition>(), remainingTrinkets);
            cfg.shopTrinketPool = new List<TrinketDefinition>(remainingTrinkets);
            RestoreBaselineStarterDeck(cfg, remainingCards);
            EditorUtility.SetDirty(cfg);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Synergy Rollback] Removed {removedCardAssets} card assets and {removedTrinketAssets} trinket assets. Cleared tags and restored baseline starter deck.");
        }

        public static void AddAllCardsToStarterDeck()
        {
            var cfg = AssetDatabase.LoadAssetAtPath<GameConfigDefinition>(GameConfigAssetPath);
            if (cfg == null)
            {
                Debug.LogError($"[Synergy Auto] Missing config asset at {GameConfigAssetPath}");
                return;
            }

            var allCards = LoadAllCards();
            cfg.starterDeck = MergeUniqueCards(new List<CardDefinition>(), allCards);
            cfg.shopCardPool = MergeUniqueCards(new List<CardDefinition>(), allCards);
            // Keep this as a deck-builder action, not a hand-size mutation.
            cfg.handSize = 4;
            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Synergy Auto] Added {cfg.starterDeck.Count} cards to starter deck. Hand size set to {cfg.handSize}.");
        }

        public static void CreateVibeTrinketsOnly()
        {
            EnsureFolder(TrinketsDir);
            var cfg = AssetDatabase.LoadAssetAtPath<GameConfigDefinition>(GameConfigAssetPath);
            if (cfg == null)
            {
                Debug.LogError($"[Synergy Auto] Missing config asset at {GameConfigAssetPath}");
                return;
            }

            var created = CreateOrUpdateTrinkets();
            RemoveLegacyGeneratedTrinkets();
            var all = LoadAllTrinkets();
            cfg.trinketDefinitions = MergeUniqueTrinkets(cfg.trinketDefinitions, all);
            cfg.shopTrinketPool = MergeUniqueTrinkets(cfg.shopTrinketPool, created);
            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Synergy Auto] Created/updated {created.Count} vibe trinkets and added them to shop pool.");
        }

        private static void ApplyRecommendedStarterDeck(GameConfigDefinition cfg, List<CardDefinition> allCards)
        {
            if (cfg == null) return;
            var byId = new Dictionary<string, CardDefinition>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < allCards.Count; i++)
            {
                var card = allCards[i];
                if (card == null || string.IsNullOrWhiteSpace(card.cardId)) continue;
                byId[card.cardId] = card;
            }

            // Starter deck tuned for this card set: control + utility + a few summons.
            var recommendedIds = new[]
            {
                "fortify_line",
                "royal_guard",
                "forced_march",
                "battle_focus",
                "pinning_shot",
                "counter_stance",
                "castling",
                "zone_of_control",
                "spiked_barricade",
                "sanctuary_tile",
                "summon_knight",
                "summon_rook_tactical"
            };

            var deck = new List<CardDefinition>();
            for (var i = 0; i < recommendedIds.Length; i++)
            {
                if (!byId.TryGetValue(recommendedIds[i], out var card) || card == null) continue;
                deck.Add(card);
            }

            if (deck.Count == 0)
            {
                Debug.LogWarning("[Synergy Auto] Recommended starter deck could not be built; keeping existing starter deck.");
                return;
            }

            cfg.starterDeck = deck;
            cfg.handSize = Mathf.Max(3, cfg.handSize);
            EditorUtility.SetDirty(cfg);
        }

        private static List<CardDefinition> LoadAllCards()
        {
            var guids = AssetDatabase.FindAssets("t:CardDefinition", new[] { CardsRoot });
            var list = new List<CardDefinition>(guids.Length);
            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var card = AssetDatabase.LoadAssetAtPath<CardDefinition>(path);
                if (card != null) list.Add(card);
            }
            return list;
        }

        private static List<TrinketDefinition> LoadAllTrinkets()
        {
            var guids = AssetDatabase.FindAssets("t:TrinketDefinition", new[] { TrinketsDir });
            var list = new List<TrinketDefinition>(guids.Length);
            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var trinket = AssetDatabase.LoadAssetAtPath<TrinketDefinition>(path);
                if (trinket != null) list.Add(trinket);
            }
            return list;
        }

        private static void TagExistingCards(List<CardDefinition> cards)
        {
            for (var i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                if (card == null) continue;
                card.tags = BuildCardTags(card.kind, card.summonKind, card.cardId);
                EditorUtility.SetDirty(card);
            }
        }

        private static List<CardDefinition> CreateOrUpdateNewCards(List<CardDefinition> existingCards)
        {
            var output = new List<CardDefinition>();
            var specs = NewCardSpecs();
            specs.AddRange(AnimalSummonCardSpecs());
            for (var i = 0; i < specs.Count; i++)
            {
                var spec = specs[i];
                var card = FindCardById(existingCards, spec.id);
                if (card == null)
                {
                    card = ScriptableObject.CreateInstance<CardDefinition>();
                    AssetDatabase.CreateAsset(card, $"{SynergyCardsDir}/Card_{spec.id}.asset");
                    existingCards.Add(card);
                }

                card.cardId = spec.id;
                card.displayName = spec.name;
                card.kind = spec.kind;
                card.summonKind = spec.summonKind;
                card.cost = Mathf.Max(0, spec.cost);
                card.shopCost = Mathf.Max(0, spec.shopCost);
                card.amount = Mathf.Max(1, spec.amount);
                card.description = spec.description;
                card.tags = new List<SynergyTag>(spec.tags);
                EditorUtility.SetDirty(card);
                output.Add(card);
            }
            return output;
        }

        private static List<TrinketDefinition> CreateOrUpdateTrinkets()
        {
            var output = new List<TrinketDefinition>();
            var specs = NewTrinketSpecs();
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

                trinket.trinketId = spec.id;
                trinket.displayName = spec.name;
                trinket.description = spec.description;
                trinket.shopCost = Mathf.Max(0, spec.shopCost);
                trinket.allowDuplicates = spec.allowDuplicates;
                trinket.effectType = spec.effectType;
                trinket.amount = Mathf.Max(1, spec.amount);
                trinket.tags = new List<SynergyTag>(spec.tags);
                EditorUtility.SetDirty(trinket);
                output.Add(trinket);
            }
            return output;
        }

        private static CardDefinition FindCardById(List<CardDefinition> cards, string cardId)
        {
            if (string.IsNullOrWhiteSpace(cardId)) return null;
            for (var i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                if (card == null) continue;
                if (string.Equals(card.cardId, cardId, StringComparison.OrdinalIgnoreCase)) return card;
            }
            return null;
        }

        private static List<CardDefinition> MergeUniqueCards(List<CardDefinition> existing, List<CardDefinition> incoming)
        {
            var merged = new List<CardDefinition>();
            var seen = new HashSet<CardDefinition>();
            AddRangeUnique(merged, seen, existing);
            AddRangeUnique(merged, seen, incoming);
            return merged;
        }

        private static List<TrinketDefinition> MergeUniqueTrinkets(List<TrinketDefinition> existing, List<TrinketDefinition> incoming)
        {
            var merged = new List<TrinketDefinition>();
            var seen = new HashSet<TrinketDefinition>();
            AddRangeUnique(merged, seen, existing);
            AddRangeUnique(merged, seen, incoming);
            return merged;
        }

        private static void AddRangeUnique<T>(List<T> list, HashSet<T> seen, List<T> source) where T : UnityEngine.Object
        {
            if (source == null) return;
            for (var i = 0; i < source.Count; i++)
            {
                var item = source[i];
                if (item == null || !seen.Add(item)) continue;
                list.Add(item);
            }
        }

        private static List<SynergyTag> BuildCardTags(CardKind kind, UnitKind summonKind, string cardId)
        {
            var tags = new HashSet<SynergyTag>();
            switch (kind)
            {
                case CardKind.Summon:
                    tags.Add(SynergyTag.Summon);
                    tags.Add(SynergyTag.Tempo);
                    break;
                case CardKind.HealSmall:
                    tags.Add(SynergyTag.Support);
                    tags.Add(SynergyTag.Sustain);
                    break;
                case CardKind.Shield:
                    tags.Add(SynergyTag.Support);
                    tags.Add(SynergyTag.Fortify);
                    break;
                case CardKind.BearTrap:
                case CardKind.SpikePit:
                    tags.Add(SynergyTag.Trap);
                    tags.Add(SynergyTag.Control);
                    break;
                case CardKind.Barricade:
                    tags.Add(SynergyTag.Fortify);
                    tags.Add(SynergyTag.Control);
                    break;
            }

            switch (summonKind)
            {
                case UnitKind.Pawn:
                    tags.Add(SynergyTag.Pawn);
                    tags.Add(SynergyTag.Swarm);
                    break;
                case UnitKind.Knight:
                    tags.Add(SynergyTag.Knight);
                    break;
                case UnitKind.Bishop:
                    tags.Add(SynergyTag.Bishop);
                    break;
                case UnitKind.Rook:
                    tags.Add(SynergyTag.Rook);
                    break;
                case UnitKind.Queen:
                    tags.Add(SynergyTag.Queen);
                    break;
                case UnitKind.BeaconTower:
                    tags.Add(SynergyTag.Objective);
                    break;
            }

            var id = (cardId ?? string.Empty).ToLowerInvariant();
            if (id.Contains("beacon")) tags.Add(SynergyTag.Objective);
            if (id.Contains("economy") || id.Contains("gold")) tags.Add(SynergyTag.Economy);

            return new List<SynergyTag>(tags);
        }

        private static List<CardSpec> NewCardSpecs()
        {
            return new List<CardSpec>
            {
                new CardSpec("fortify_line", "Fortify Line", CardKind.Shield, UnitKind.King, 2, 48, 2, "Target a friendly row. All allies on that row gain 2 shield.", SynergyTag.Fortify, SynergyTag.Support, SynergyTag.Control),
                new CardSpec("royal_guard", "Royal Guard", CardKind.Shield, UnitKind.King, 2, 52, 2, "Target ally gains guard: reduced damage for next 2 hits.", SynergyTag.Fortify, SynergyTag.Support),
                new CardSpec("forced_march", "Forced March", CardKind.Shield, UnitKind.King, 1, 42, 1, "Target ally gains +1 move this turn but cannot attack.", SynergyTag.Tempo, SynergyTag.Support),
                new CardSpec("battle_focus", "Battle Focus", CardKind.Shield, UnitKind.King, 1, 40, 1, "Target ally gains +1 attack for next attack.", SynergyTag.Tempo, SynergyTag.Support),
                new CardSpec("pinning_shot", "Pinning Shot", CardKind.SpikePit, UnitKind.Pawn, 1, 44, 1, "Deal 1 damage and Root enemy for 1 turn.", SynergyTag.Control),
                new CardSpec("counter_stance", "Counter Stance", CardKind.Shield, UnitKind.King, 2, 50, 2, "Target ally retaliates for 2 when next hit.", SynergyTag.Control, SynergyTag.Fortify),
                new CardSpec("castling", "Castling", CardKind.Shield, UnitKind.King, 2, 54, 1, "Select two allies. Swap their positions.", SynergyTag.Tempo, SynergyTag.Control),
                new CardSpec("zone_of_control", "Zone of Control", CardKind.BearTrap, UnitKind.Pawn, 2, 56, 1, "Create a 3-tile control line that roots enemies entering it.", SynergyTag.Control, SynergyTag.Trap),
                new CardSpec("spiked_barricade", "Spiked Barricade", CardKind.Barricade, UnitKind.Rock, 1, 46, 2, "Place barricade. First enemy that hits it takes 2.", SynergyTag.Fortify, SynergyTag.Control, SynergyTag.Trap),
                new CardSpec("sanctuary_tile", "Sanctuary Tile", CardKind.HealSmall, UnitKind.King, 1, 45, 1, "Bless a tile for 2 turns. Ally ending turn there heals and gains shield.", SynergyTag.Sustain, SynergyTag.Support),
                new CardSpec("summon_rook_tactical", "Summon Rook", CardKind.Summon, UnitKind.Rook, 3, 60, 1, "Summon a Rook.", SynergyTag.Summon, SynergyTag.Rook, SynergyTag.Control),
                new CardSpec("summon_queen_royal", "Summon Queen", CardKind.Summon, UnitKind.Queen, 5, 85, 1, "Summon a Queen.", SynergyTag.Summon, SynergyTag.Queen, SynergyTag.Tempo)
            };
        }

        private static List<CardSpec> AnimalSummonCardSpecs()
        {
            return new List<CardSpec>
            {
                new CardSpec("summon_bat", "Summon Bat", CardKind.Summon, UnitKind.Bat, 1, 28, 1, "Summon a Bat.", SynergyTag.Summon, SynergyTag.Tempo),
                new CardSpec("summon_coyote", "Summon Coyote", CardKind.Summon, UnitKind.Coyote, 2, 36, 1, "Summon a Coyote.", SynergyTag.Summon, SynergyTag.Control),
                new CardSpec("summon_owl", "Summon Owl", CardKind.Summon, UnitKind.Owl, 2, 38, 1, "Summon an Owl.", SynergyTag.Summon, SynergyTag.Control),
                new CardSpec("summon_boar", "Summon Boar", CardKind.Summon, UnitKind.Boar, 3, 48, 1, "Summon a Boar.", SynergyTag.Summon, SynergyTag.Tempo),
                new CardSpec("summon_snake", "Summon Snake", CardKind.Summon, UnitKind.Snake, 1, 30, 1, "Summon a Snake.", SynergyTag.Summon, SynergyTag.Control),
                new CardSpec("summon_spider", "Summon Spider", CardKind.Summon, UnitKind.Spider, 2, 34, 1, "Summon a Spider.", SynergyTag.Summon, SynergyTag.Control),
                new CardSpec("summon_skunk", "Summon Skunk", CardKind.Summon, UnitKind.Skunk, 3, 50, 1, "Summon a Skunk.", SynergyTag.Summon, SynergyTag.Control),
                new CardSpec("summon_wolf_alpha", "Summon Wolf Alpha", CardKind.Summon, UnitKind.WolfAlpha, 4, 72, 1, "Summon a Wolf Alpha.", SynergyTag.Summon, SynergyTag.Tempo),
                new CardSpec("summon_bear", "Summon Bear", CardKind.Summon, UnitKind.Bear, 4, 78, 1, "Summon a Bear.", SynergyTag.Summon, SynergyTag.Fortify),
                new CardSpec("summon_toad", "Summon Toad", CardKind.Summon, UnitKind.Toad, 2, 40, 1, "Summon a Toad.", SynergyTag.Summon, SynergyTag.Control)
            };
        }

        private static string[] NewCardIds()
        {
            var specs = NewCardSpecs();
            var ids = new List<string>(specs.Count);
            for (var i = 0; i < specs.Count; i++) ids.Add(specs[i].id);
            var summonSpecs = AnimalSummonCardSpecs();
            for (var i = 0; i < summonSpecs.Count; i++) ids.Add(summonSpecs[i].id);
            return ids.ToArray();
        }

        private static string[] LegacyGeneratedCardIds()
        {
            return new[]
            {
                "summon_pawn_scout",
                "summon_pawn_phalanx",
                "summon_pawn_banner",
                "summon_knight_lancer",
                "summon_knight_vanguard",
                "summon_bishop_seer",
                "summon_bishop_warden",
                "summon_rook_bulwark",
                "summon_rook_ram",
                "summon_queen_command",
                "heal_battlefield",
                "heal_rally",
                "shield_phalanx",
                "barricade_stonewall",
                "spike_pit_mk2"
            };
        }

        private static List<TrinketSpec> NewTrinketSpecs()
        {
            return new List<TrinketSpec>
            {
                new TrinketSpec("royal_decree", "Royal Decree", "First card each turn costs 0, but second card costs +1.", 90, false, TrinketEffectType.None, 1, SynergyTag.Tempo, SynergyTag.Economy),
                new TrinketSpec("war_banner", "War Banner", "First summoned unit each turn gets +1 attack and +1 move this turn.", 84, false, TrinketEffectType.None, 1, SynergyTag.Summon, SynergyTag.Tempo),
                new TrinketSpec("field_surgeon_kit", "Field Surgeon Kit", "First ally below half HP each encounter heals 2 instantly.", 72, false, TrinketEffectType.None, 1, SynergyTag.Sustain, SynergyTag.Support),
                new TrinketSpec("trap_engineer_gloves", "Trap Engineer Gloves", "Traps gain +1 trigger radius (design intent).", 70, false, TrinketEffectType.None, 1, SynergyTag.Trap, SynergyTag.Control),
                new TrinketSpec("siege_bolts", "Siege Bolts", "Barricades retaliate harder and are tougher (design intent).", 76, false, TrinketEffectType.None, 1, SynergyTag.Fortify, SynergyTag.Control),
                new TrinketSpec("knights_oath_spurs", "Knight's Oath Spurs", "Knights can keep tempo after attacks (design intent).", 82, false, TrinketEffectType.BonusMovePerTurn, 1, SynergyTag.Knight, SynergyTag.Tempo),
                new TrinketSpec("bishops_prism", "Bishop's Prism", "Bishops apply attack debuffs (design intent).", 74, false, TrinketEffectType.None, 1, SynergyTag.Bishop, SynergyTag.Control),
                new TrinketSpec("rook_anchor", "Rook Anchor", "Rooks become stable anchors (design intent).", 78, false, TrinketEffectType.None, 1, SynergyTag.Rook, SynergyTag.Fortify),
                new TrinketSpec("queens_gambit_seal", "Queen's Gambit Seal", "Queen kills grant tempo value (design intent).", 94, false, TrinketEffectType.None, 1, SynergyTag.Queen, SynergyTag.Tempo),
                new TrinketSpec("pawn_drill_manual", "Pawn Drill Manual", "Promoted pawns scale harder (design intent).", 66, false, TrinketEffectType.None, 1, SynergyTag.Pawn, SynergyTag.Swarm),
                new TrinketSpec("command_whistle", "Command Whistle", "Choose one ally each turn for action boost (design intent).", 80, false, TrinketEffectType.None, 1, SynergyTag.Support, SynergyTag.Tempo),
                new TrinketSpec("fog_locket", "Fog Locket", "Delay enemy intent visibility (design intent).", 74, false, TrinketEffectType.None, 1, SynergyTag.Control),
                new TrinketSpec("sanctum_relay", "Sanctum Relay", "Beacon-adjacent allies gain protection (design intent).", 78, false, TrinketEffectType.None, 1, SynergyTag.Objective, SynergyTag.Fortify),
                new TrinketSpec("reinforcement_ledger", "Reinforcement Ledger", "Summon chains grant value spikes (design intent).", 86, false, TrinketEffectType.None, 1, SynergyTag.Summon, SynergyTag.Economy),
                new TrinketSpec("blood_price_coin", "Blood Price Coin", "Pay king HP to bypass energy costs (design intent).", 96, false, TrinketEffectType.None, 1, SynergyTag.Economy, SynergyTag.Tempo)
            };
        }

        private static string[] NewTrinketIds()
        {
            return new[]
            {
                "royal_decree",
                "war_banner",
                "field_surgeon_kit",
                "trap_engineer_gloves",
                "siege_bolts",
                "knights_oath_spurs",
                "bishops_prism",
                "rook_anchor",
                "queens_gambit_seal",
                "pawn_drill_manual",
                "command_whistle",
                "fog_locket",
                "sanctum_relay",
                "reinforcement_ledger",
                "blood_price_coin"
            };
        }

        private static string[] LegacyGeneratedTrinketIds()
        {
            return new[]
            {
                "pawn_standard",
                "knight_spurs",
                "bishop_lens",
                "rook_plating",
                "queen_sigil",
                "swarm_banner",
                "trap_kit",
                "fortifier_nails",
                "supply_cache",
                "beacon_relay",
                "scout_compass",
                "war_drum",
                "formation_chalk",
                "medic_satchel",
                "field_manual"
            };
        }

        private static void RestoreBaselineStarterDeck(GameConfigDefinition cfg, List<CardDefinition> allCards)
        {
            if (cfg == null) return;
            var byId = new Dictionary<string, CardDefinition>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < allCards.Count; i++)
            {
                var card = allCards[i];
                if (card == null || string.IsNullOrWhiteSpace(card.cardId)) continue;
                byId[card.cardId] = card;
            }

            var baselineIds = new[]
            {
                "heal_small",
                "shield",
                "summon_pawn_a",
                "summon_pawn_b",
                "summon_knight",
                "summon_bishop",
                "bear_trap",
                "barricade",
                "spike_pit"
            };

            var deck = new List<CardDefinition>();
            for (var i = 0; i < baselineIds.Length; i++)
            {
                if (!byId.TryGetValue(baselineIds[i], out var card) || card == null) continue;
                deck.Add(card);
            }

            if (deck.Count > 0) cfg.starterDeck = deck;
            cfg.handSize = Mathf.Max(3, cfg.handSize);
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

        private sealed class CardSpec
        {
            public readonly string id;
            public readonly string name;
            public readonly CardKind kind;
            public readonly UnitKind summonKind;
            public readonly int cost;
            public readonly int shopCost;
            public readonly int amount;
            public readonly string description;
            public readonly List<SynergyTag> tags;

            public CardSpec(
                string id,
                string name,
                CardKind kind,
                UnitKind summonKind,
                int cost,
                int shopCost,
                int amount,
                string description,
                params SynergyTag[] tags)
            {
                this.id = id;
                this.name = name;
                this.kind = kind;
                this.summonKind = summonKind;
                this.cost = cost;
                this.shopCost = shopCost;
                this.amount = amount;
                this.description = description;
                this.tags = new List<SynergyTag>(tags ?? Array.Empty<SynergyTag>());
            }
        }

        private sealed class TrinketSpec
        {
            public readonly string id;
            public readonly string name;
            public readonly string description;
            public readonly int shopCost;
            public readonly bool allowDuplicates;
            public readonly TrinketEffectType effectType;
            public readonly int amount;
            public readonly List<SynergyTag> tags;

            public TrinketSpec(
                string id,
                string name,
                string description,
                int shopCost,
                bool allowDuplicates,
                TrinketEffectType effectType,
                int amount,
                params SynergyTag[] tags)
            {
                this.id = id;
                this.name = name;
                this.description = description;
                this.shopCost = shopCost;
                this.allowDuplicates = allowDuplicates;
                this.effectType = effectType;
                this.amount = amount;
                this.tags = new List<SynergyTag>(tags ?? Array.Empty<SynergyTag>());
            }
        }
    }
}
#endif
