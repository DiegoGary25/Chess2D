using System.Collections.Generic;
using ChessPrototype.Unity.Core;
using ChessPrototype.Unity.Data;
using UnityEngine;

namespace ChessPrototype.Unity.RunMap
{
    public sealed class RuntimeMapNode
    {
        public string id;
        public int tier;
        public int lane;
        public MapNodeType type;
        public bool available;
        public bool completed;
        public bool current;
    }

    public sealed class RunMapController : MonoBehaviour
    {
        private readonly Dictionary<string, RuntimeMapNode> _nodes = new Dictionary<string, RuntimeMapNode>();
        private readonly Dictionary<string, List<string>> _edges = new Dictionary<string, List<string>>();
        private GameSessionState _session;
        public IReadOnlyDictionary<string, RuntimeMapNode> Nodes => _nodes;
        public IReadOnlyDictionary<string, List<string>> Edges => _edges;

        public void Configure(GameSessionState session, RunMapDefinition def)
        {
            _session = session;
            _nodes.Clear();
            _edges.Clear();
            if (def == null) return;
            foreach (var n in def.nodes)
            {
                _nodes[n.id] = new RuntimeMapNode { id = n.id, tier = n.tier, lane = n.lane, type = n.type };
                _edges[n.id] = new List<string>();
            }
            foreach (var e in def.edges)
            {
                if (_edges.TryGetValue(e.from, out var outList)) outList.Add(e.to);
            }
            RefreshAvailability();
        }

        public void RefreshAvailability()
        {
            foreach (var kv in _nodes) kv.Value.available = false;
            var last = _session.LastCompletedNodeId;
            if (string.IsNullOrEmpty(last))
            {
                foreach (var kv in _nodes) if (!kv.Value.completed && kv.Value.tier == 0) kv.Value.available = true;
                return;
            }
            if (!_edges.TryGetValue(last, out var next)) return;
            for (var i = 0; i < next.Count; i++)
            {
                if (_nodes.TryGetValue(next[i], out var node) && !node.completed) node.available = true;
            }
        }

        public bool SelectNode(string id, out RuntimeMapNode node)
        {
            node = null;
            if (!_nodes.TryGetValue(id, out var n) || !n.available || n.completed) return false;
            foreach (var kv in _nodes) kv.Value.current = false;
            n.current = true;
            node = n;
            return true;
        }

        public void CompleteNode(string id)
        {
            if (!_nodes.TryGetValue(id, out var n)) return;
            n.completed = true;
            n.current = false;
            _session.MarkNodeComplete(id);
            RefreshAvailability();
        }
    }
}

