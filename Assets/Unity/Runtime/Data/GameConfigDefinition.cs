using System.Collections.Generic;
using UnityEngine;

namespace ChessPrototype.Unity.Data
{
    public enum EnemyTurnActionOrder
    {
        AttackThenMove,
        MoveThenAttack
    }

    [CreateAssetMenu(menuName = "ChessPrototype/GameConfig", fileName = "GameConfig")]
    public sealed class GameConfigDefinition : ScriptableObject
    {
        [Header("Prototype constants inferred from JS")]
        public int handSize = 4;
        [Min(0)] public int startingElixir = 3;
        [Min(1)] public int energyPerRound = 1;
        public int maxElixir = 5;
        public int kingPersistentHp = 5;
        public int startingGold = 100;
        public int encounterGoldMin = 70;
        public int encounterGoldMax = 100;
        [Header("Enemy Turn Rules")]
        public bool showEnemyIntentLines = true;
        public EnemyTurnActionOrder enemyTurnActionOrder = EnemyTurnActionOrder.MoveThenAttack;
        [Range(1, 4)] public int enemyPlannerDepth = 1;
        [Header("Run Seed")]
        public bool randomizeSeedOnPlay = true;
        [Header("Run Flow")]
        public bool useRunMap = true;
        [Min(1)] public int encountersPerShop = 3;
        [Min(1f)] public float endlessDifficultyBaseBudget = 5f;
        [Min(1.01f)] public float endlessDifficultyGrowthFactor = 1.4f;
        [Header("Spawn Status Rules")]
        public bool applySleepOnPlayerSpawn = false;
        public bool applySleepOnCaveEnemySpawn = false;
        public int shopCardOfferCount = 3;
        public int shopTrinketOfferCount = 3;
        public List<CardDefinition> starterDeck = new List<CardDefinition>();
        public List<CardDefinition> shopCardPool = new List<CardDefinition>();
        public List<TrinketDefinition> trinketDefinitions = new List<TrinketDefinition>();
        public List<TrinketDefinition> shopTrinketPool = new List<TrinketDefinition>();
        public List<PieceDefinition> pieceDefinitions = new List<PieceDefinition>();
        public List<EnemyDefinition> enemyDefinitions = new List<EnemyDefinition>();
        public List<EncounterTemplateDefinition> encounters = new List<EncounterTemplateDefinition>();
        public RunMapDefinition runMap;
    }
}

