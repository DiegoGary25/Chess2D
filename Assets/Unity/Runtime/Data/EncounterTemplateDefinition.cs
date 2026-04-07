using System.Collections.Generic;
using UnityEngine;

namespace ChessPrototype.Unity.Data
{
    [CreateAssetMenu(menuName = "ChessPrototype/EncounterTemplate", fileName = "EncounterTemplate")]
    public sealed class EncounterTemplateDefinition : ScriptableObject
    {
        public string encounterId;
        public int boardSize = 4;
        [Header("Dynamic Generation")]
        public bool useDynamicGeneration;
        [Header("Ruleset")]
        public bool chessOnlyEncounter = true;
        public bool createBeacons = true;
        [Min(0f)] public float difficultyBudget = 4f;
        [Min(0f)] public float caveDifficultyThreshold = 7f;
        public bool requireCaveWhenThresholdReached = true;
        public int dynamicSeedOffset;
        public List<Placement> enemyPlacements = new List<Placement>();
        public List<CaveTemplate> caves = new List<CaveTemplate>();
        public List<BeaconTemplate> beacons = new List<BeaconTemplate>();
    }
}
