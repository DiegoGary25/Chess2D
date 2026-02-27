using System.Collections.Generic;
using UnityEngine;

namespace ChessPrototype.Unity.Data
{
    [CreateAssetMenu(menuName = "ChessPrototype/RunMapDefinition", fileName = "RunMapDefinition")]
    public sealed class RunMapDefinition : ScriptableObject
    {
        [Header("Source")]
        public bool useProceduralGeneration = true;

        [Header("Procedural Layout")]
        [Min(2)] public int minLayers = 6;
        [Min(2)] public int maxLayers = 8;
        [Min(1)] public int laneSlots = 4;
        [Min(1)] public int minNodesPerLayer = 3;
        [Min(1)] public int maxNodesPerLayer = 4;
        [Min(1)] public int minEntryNodes = 1;
        [Min(1)] public int maxEntryNodes = 2;
        [Min(1)] public int minConnectionsPerNode = 1;
        [Min(1)] public int maxConnectionsPerNode = 3;

        [Header("Node Type Weights (Non-boss)")]
        [Min(0f)] public float enemyWeight = 40f;
        [Min(0f)] public float unknownWeight = 20f;
        [Min(0f)] public float restWeight = 15f;
        [Min(0f)] public float merchantWeight = 10f;
        [Min(0f)] public float treasureWeight = 10f;
        [Min(0f)] public float eliteWeight = 5f;

        [Header("Manual Fallback")]
        public List<RunMapNodeData> nodes = new List<RunMapNodeData>();
        public List<RunMapEdgeData> edges = new List<RunMapEdgeData>();
    }
}
