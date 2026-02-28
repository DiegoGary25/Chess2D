using System.Collections.Generic;
using UnityEngine;

namespace ChessPrototype.Unity.Data
{
    [CreateAssetMenu(menuName = "ChessPrototype/GameConfig", fileName = "GameConfig")]
    public sealed class GameConfigDefinition : ScriptableObject
    {
        [Header("Prototype constants inferred from JS")]
        public int handSize = 4;
        public int energyPerRound = 3;
        public int maxElixir = 5;
        public int kingPersistentHp = 5;
        public int startingGold = 100;
        public int encounterGoldMin = 30;
        public int encounterGoldMax = 100;
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

