using System.Collections.Generic;
using UnityEngine;

namespace ChessPrototype.Unity.Data
{
    [CreateAssetMenu(menuName = "ChessPrototype/RunMapDefinition", fileName = "RunMapDefinition")]
    public sealed class RunMapDefinition : ScriptableObject
    {
        public List<RunMapNodeData> nodes = new List<RunMapNodeData>();
        public List<RunMapEdgeData> edges = new List<RunMapEdgeData>();
    }
}
