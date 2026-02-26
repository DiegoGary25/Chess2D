#if UNITY_EDITOR
using System.Collections.Generic;
using ChessPrototype.Unity.Data;
using UnityEditor;
using UnityEngine;

namespace ChessPrototype.Unity.EditorTools
{
    public static class PrototypeDataGenerator
    {
        public const string Root = "Assets/Unity/Data";
        private const string CardsDir = "Assets/Unity/Data/Cards";
        private const string PiecesDir = "Assets/Unity/Data/Pieces";
        private const string EnemiesDir = "Assets/Unity/Data/Enemies";
        private const string EncountersDir = "Assets/Unity/Data/Encounters";
        private const string ConfigDir = "Assets/Unity/Data/Config";
        private const string MapsDir = "Assets/Unity/Data/Maps";

        public static void GenerateAll()
        {
            EnsureDirs();
            var pieceDefs = GeneratePieceDefs();
            var enemyDefs = GenerateEnemyDefs();
            var cards = GenerateCardDefs();
            var encounters = GenerateEncounters();
            var map = GenerateRunMap();
            GenerateConfig(pieceDefs, enemyDefs, cards, encounters, map);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ChessPrototype] Data assets generated.");
        }

        private static void EnsureDirs()
        {
            Ensure("Assets/Unity");
            Ensure(Root);
            Ensure(CardsDir); Ensure(PiecesDir); Ensure(EnemiesDir); Ensure(EncountersDir); Ensure(ConfigDir); Ensure(MapsDir);
        }

        private static List<PieceDefinition> GeneratePieceDefs()
        {
            var list = new List<PieceDefinition>();
            list.Add(Piece(UnitKind.King, 5, 2));
            list.Add(Piece(UnitKind.Pawn, 1, 1));
            list.Add(Piece(UnitKind.Knight, 3, 2));
            list.Add(Piece(UnitKind.Bishop, 2, 2));
            list.Add(Piece(UnitKind.Rook, 2, 3));
            list.Add(Piece(UnitKind.Queen, 3, 4));
            list.Add(Piece(UnitKind.Bat, 1, 1));
            list.Add(Piece(UnitKind.Coyote, 2, 1));
            list.Add(Piece(UnitKind.Owl, 2, 1));
            list.Add(Piece(UnitKind.Boar, 3, 2));
            list.Add(Piece(UnitKind.Snake, 1, 1));
            list.Add(Piece(UnitKind.Spider, 2, 1));
            list.Add(Piece(UnitKind.Skunk, 3, 1));
            list.Add(Piece(UnitKind.WolfAlpha, 6, 2));
            list.Add(Piece(UnitKind.Bear, 10, 3));
            list.Add(Piece(UnitKind.Toad, 3, 2));
            list.Add(Piece(UnitKind.Rock, 12, 0));
            list.Add(Piece(UnitKind.Cave, 12, 0));
            return list;
        }

        private static PieceDefinition Piece(UnitKind kind, int hp, int atk)
        {
            var a = GetOrCreate<PieceDefinition>($"{PiecesDir}/Piece_{kind}.asset");
            a.kind = kind;
            a.displayName = kind.ToString();
            a.maxHp = hp;
            a.attack = atk;
            EditorUtility.SetDirty(a);
            return a;
        }

        private static List<EnemyDefinition> GenerateEnemyDefs()
        {
            var list = new List<EnemyDefinition>();
            list.Add(Enemy(UnitKind.Bat, "bat"));
            list.Add(Enemy(UnitKind.Coyote, "coyote"));
            list.Add(Enemy(UnitKind.Owl, "owl"));
            list.Add(Enemy(UnitKind.Boar, "boar"));
            list.Add(Enemy(UnitKind.Snake, "snake"));
            list.Add(Enemy(UnitKind.Spider, "spider"));
            list.Add(Enemy(UnitKind.Skunk, "skunk"));
            list.Add(Enemy(UnitKind.WolfAlpha, "wolf_alpha"));
            list.Add(Enemy(UnitKind.Bear, "bear"));
            list.Add(Enemy(UnitKind.Toad, "toad"));
            return list;
        }

        private static EnemyDefinition Enemy(UnitKind kind, string behavior)
        {
            var a = GetOrCreate<EnemyDefinition>($"{EnemiesDir}/Enemy_{kind}.asset");
            a.kind = kind;
            a.displayName = kind.ToString();
            a.behaviorKey = behavior;
            EditorUtility.SetDirty(a);
            return a;
        }

        private static List<CardDefinition> GenerateCardDefs()
        {
            var list = new List<CardDefinition>();
            list.Add(Card("heal_small", "Heal Small", CardKind.HealSmall, 1, UnitKind.King, 1));
            list.Add(Card("shield", "Shield", CardKind.Shield, 1, UnitKind.King, 1));
            list.Add(Card("summon_pawn_a", "Summon Pawn", CardKind.Summon, 1, UnitKind.Pawn, 1));
            list.Add(Card("summon_pawn_b", "Summon Pawn", CardKind.Summon, 1, UnitKind.Pawn, 1));
            list.Add(Card("summon_knight", "Summon Knight", CardKind.Summon, 2, UnitKind.Knight, 1));
            list.Add(Card("summon_bishop", "Summon Bishop", CardKind.Summon, 2, UnitKind.Bishop, 1));
            list.Add(Card("bear_trap", "Bear Trap", CardKind.BearTrap, 1, UnitKind.Pawn, 1));
            list.Add(Card("barricade", "Barricade", CardKind.Barricade, 1, UnitKind.Rock, 1));
            list.Add(Card("spike_pit", "Spike Pit", CardKind.SpikePit, 1, UnitKind.Pawn, 1));
            return list;
        }

        private static CardDefinition Card(string id, string name, CardKind kind, int cost, UnitKind summonKind, int amount)
        {
            var c = GetOrCreate<CardDefinition>($"{CardsDir}/Card_{id}.asset");
            c.cardId = id; c.displayName = name; c.kind = kind; c.cost = cost; c.summonKind = summonKind; c.amount = amount;
            c.description = name;
            EditorUtility.SetDirty(c);
            return c;
        }

        private static List<EncounterTemplateDefinition> GenerateEncounters()
        {
            var list = new List<EncounterTemplateDefinition>();
            for (var i = 1; i <= 10; i++)
            {
                var e = GetOrCreate<EncounterTemplateDefinition>($"{EncountersDir}/E{i:00}.asset");
                e.encounterId = $"E{i:00}";
                e.boardSize = Mathf.Clamp(4 + ((i - 1) / 2), 4, 8);
                e.enemyPlacements = BuildEnemyPlacements(i, e.boardSize);
                e.caves = BuildCaves(i, e.boardSize);
                EditorUtility.SetDirty(e);
                list.Add(e);
            }
            return list;
        }

        private static List<Placement> BuildEnemyPlacements(int idx, int size)
        {
            var p = new List<Placement>();
            void Add(UnitKind k, int r, int c) => p.Add(new Placement { kind = k, row = r, col = c });
            switch (idx)
            {
                case 1: Add(UnitKind.Bat, 0, size / 2); Add(UnitKind.Coyote, 1, size / 2); break;
                case 2: Add(UnitKind.Bat, 0, 1); Add(UnitKind.Owl, 1, size / 2); Add(UnitKind.Bat, 0, size - 2); break;
                case 3: Add(UnitKind.Boar, 0, size / 2); Add(UnitKind.Spider, 1, 1); Add(UnitKind.Snake, 1, size - 2); break;
                case 4: Add(UnitKind.Skunk, 0, size / 2); Add(UnitKind.Coyote, 1, 1); Add(UnitKind.Coyote, 1, size - 2); break;
                case 5: Add(UnitKind.Boar, 1, size / 2); Add(UnitKind.Spider, 0, 1); Add(UnitKind.Spider, 0, size - 2); break;
                case 6: Add(UnitKind.WolfAlpha, 0, size / 2); Add(UnitKind.Coyote, 1, 1); Add(UnitKind.Coyote, 1, size - 2); break;
                case 7: Add(UnitKind.Bear, 0, size / 2); Add(UnitKind.Skunk, 1, 1); Add(UnitKind.Snake, 1, size - 2); break;
                case 8: Add(UnitKind.Bear, 0, size / 2); Add(UnitKind.Toad, 1, 1); Add(UnitKind.Toad, 1, size - 2); break;
                case 9: Add(UnitKind.WolfAlpha, 0, 1); Add(UnitKind.WolfAlpha, 0, size - 2); Add(UnitKind.Bear, 1, size / 2); break;
                case 10: Add(UnitKind.Bear, 0, size / 2); Add(UnitKind.WolfAlpha, 1, 1); Add(UnitKind.WolfAlpha, 1, size - 2); Add(UnitKind.Skunk, 0, 0); break;
            }
            return p;
        }

        private static List<CaveTemplate> BuildCaves(int idx, int size)
        {
            var outCaves = new List<CaveTemplate>();
            if (idx < 5) return outCaves;
            outCaves.Add(new CaveTemplate
            {
                id = $"CAVE_{idx}",
                row = 0,
                col = size / 2,
                turnsUntilNextSpawn = 2,
                spawnCharges = idx >= 8 ? 4 : 3,
                maxAliveFromThisCave = 2,
                spawnPool = new List<SpawnWeight>
                {
                    new SpawnWeight { kind = UnitKind.Bat, weight = 4 },
                    new SpawnWeight { kind = UnitKind.Coyote, weight = 3 },
                    new SpawnWeight { kind = UnitKind.Spider, weight = 2 },
                    new SpawnWeight { kind = idx >= 7 ? UnitKind.Skunk : UnitKind.Snake, weight = 1 }
                }
            });
            return outCaves;
        }

        private static RunMapDefinition GenerateRunMap()
        {
            var map = GetOrCreate<RunMapDefinition>($"{MapsDir}/RunMap.asset");
            map.nodes = new List<RunMapNodeData>();
            map.edges = new List<RunMapEdgeData>();
            const int tiers = 5; const int lanes = 3;
            for (var t = 0; t < tiers; t++)
                for (var l = 0; l < lanes; l++)
                    map.nodes.Add(new RunMapNodeData { id = $"T{t}L{l}", tier = t, lane = l, type = t == tiers - 1 ? MapNodeType.Boss : (l == 1 ? MapNodeType.Battle : MapNodeType.Event) });
            for (var t = 0; t < tiers - 1; t++)
                for (var l = 0; l < lanes; l++)
                    for (var to = Mathf.Max(0, l - 1); to <= Mathf.Min(lanes - 1, l + 1); to++)
                        map.edges.Add(new RunMapEdgeData { from = $"T{t}L{l}", to = $"T{t + 1}L{to}" });
            EditorUtility.SetDirty(map);
            return map;
        }

        private static void GenerateConfig(
            List<PieceDefinition> pieces, List<EnemyDefinition> enemies, List<CardDefinition> cards,
            List<EncounterTemplateDefinition> encounters, RunMapDefinition map)
        {
            var cfg = GetOrCreate<GameConfigDefinition>($"{ConfigDir}/GameConfig.asset");
            cfg.handSize = 4;
            cfg.energyPerRound = 3;
            cfg.maxElixir = 5;
            cfg.kingPersistentHp = 5;
            cfg.starterDeck = new List<CardDefinition>(cards);
            cfg.pieceDefinitions = new List<PieceDefinition>(pieces);
            cfg.enemyDefinitions = new List<EnemyDefinition>(enemies);
            cfg.encounters = new List<EncounterTemplateDefinition>(encounters);
            cfg.runMap = map;
            EditorUtility.SetDirty(cfg);
        }

        private static T GetOrCreate<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) return a;

            if (System.IO.File.Exists(path))
            {
                AssetDatabase.DeleteAsset(path);
            }

            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        private static void Ensure(string dir)
        {
            if (AssetDatabase.IsValidFolder(dir)) return;
            var parent = System.IO.Path.GetDirectoryName(dir)?.Replace("\\", "/");
            var name = System.IO.Path.GetFileName(dir);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) Ensure(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
#endif



