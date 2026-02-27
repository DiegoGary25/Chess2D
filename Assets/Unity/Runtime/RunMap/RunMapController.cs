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
        private readonly Dictionary<int, List<RuntimeMapNode>> _nodesByTier = new Dictionary<int, List<RuntimeMapNode>>();
        private GameSessionState _session;
        public IReadOnlyDictionary<string, RuntimeMapNode> Nodes => _nodes;
        public IReadOnlyDictionary<string, List<string>> Edges => _edges;

        public void Configure(GameSessionState session, RunMapDefinition def)
        {
            _session = session;
            _nodes.Clear();
            _edges.Clear();
            _nodesByTier.Clear();
            if (def == null) return;

            var useProcedural = def.useProceduralGeneration || def.nodes == null || def.nodes.Count == 0;
            if (useProcedural) BuildProcedural(def);
            else BuildFromDefinition(def);

            if (_session != null && !string.IsNullOrEmpty(_session.LastCompletedNodeId) &&
                _nodes.TryGetValue(_session.LastCompletedNodeId, out var completed))
            {
                completed.completed = true;
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

        private void BuildFromDefinition(RunMapDefinition def)
        {
            foreach (var n in def.nodes)
            {
                var runtime = new RuntimeMapNode { id = n.id, tier = n.tier, lane = n.lane, type = n.type };
                _nodes[n.id] = runtime;
                _edges[n.id] = new List<string>();
                AddToTier(runtime);
            }

            foreach (var e in def.edges)
            {
                if (_edges.TryGetValue(e.from, out var outList)) outList.Add(e.to);
            }
        }

        private void BuildProcedural(RunMapDefinition def)
        {
            var rngSeed = (_session != null ? _session.Seed : 1) * 73856093 +
                          (_session != null ? _session.EncounterIndex : 0) * 19349663;
            var rng = new System.Random(rngSeed);
            var layerCount = RandomRangeInclusive(rng, Mathf.Max(2, def.minLayers), Mathf.Max(def.minLayers, def.maxLayers));
            var laneSlots = Mathf.Max(2, def.laneSlots);

            for (var tier = 0; tier < layerCount; tier++)
            {
                var count = ResolveNodeCountForTier(def, tier, layerCount, rng);
                var lanes = PickUniqueLanes(rng, laneSlots, count);
                lanes.Sort();

                for (var i = 0; i < lanes.Count; i++)
                {
                    var id = $"L{tier}_N{i}";
                    var type = ResolveNodeTypeForTier(def, tier, layerCount, rng);
                    var runtime = new RuntimeMapNode
                    {
                        id = id,
                        tier = tier,
                        lane = lanes[i],
                        type = type
                    };
                    _nodes[id] = runtime;
                    _edges[id] = new List<string>();
                    AddToTier(runtime);
                }
            }

            for (var tier = 0; tier < layerCount - 1; tier++)
            {
                if (!_nodesByTier.TryGetValue(tier, out var fromNodes)) continue;
                if (!_nodesByTier.TryGetValue(tier + 1, out var toNodes)) continue;
                ConnectLayerForward(def, fromNodes, toNodes, rng);
            }

            EnsureIncomingPerNode(layerCount);
        }

        private static int ResolveNodeCountForTier(RunMapDefinition def, int tier, int layerCount, System.Random rng)
        {
            if (tier == layerCount - 1) return 1;
            if (tier == 0)
            {
                return RandomRangeInclusive(rng, Mathf.Max(1, def.minEntryNodes), Mathf.Max(def.minEntryNodes, def.maxEntryNodes));
            }
            return RandomRangeInclusive(rng, Mathf.Max(1, def.minNodesPerLayer), Mathf.Max(def.minNodesPerLayer, def.maxNodesPerLayer));
        }

        private static MapNodeType ResolveNodeTypeForTier(RunMapDefinition def, int tier, int layerCount, System.Random rng)
        {
            if (tier == layerCount - 1) return MapNodeType.Boss;

            var total = def.enemyWeight + def.unknownWeight + def.restWeight + def.merchantWeight + def.treasureWeight + def.eliteWeight;
            if (total <= 0f) return MapNodeType.Enemy;

            var roll = (float)(rng.NextDouble() * total);
            if (roll < def.enemyWeight) return MapNodeType.Enemy;
            roll -= def.enemyWeight;
            if (roll < def.unknownWeight) return MapNodeType.Unknown;
            roll -= def.unknownWeight;
            if (roll < def.restWeight) return MapNodeType.Rest;
            roll -= def.restWeight;
            if (roll < def.merchantWeight) return MapNodeType.Merchant;
            roll -= def.merchantWeight;
            if (roll < def.treasureWeight) return MapNodeType.Treasure;
            return MapNodeType.Elite;
        }

        private static List<int> PickUniqueLanes(System.Random rng, int laneSlots, int count)
        {
            var wanted = Mathf.Clamp(count, 1, laneSlots);
            var pool = new List<int>(laneSlots);
            for (var i = 0; i < laneSlots; i++) pool.Add(i);

            for (var i = 0; i < pool.Count; i++)
            {
                var j = rng.Next(i, pool.Count);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }

            var picked = new List<int>(wanted);
            for (var i = 0; i < wanted; i++) picked.Add(pool[i]);
            return picked;
        }

        private void ConnectLayerForward(RunMapDefinition def, List<RuntimeMapNode> fromNodes, List<RuntimeMapNode> toNodes, System.Random rng)
        {
            fromNodes.Sort((a, b) => a.lane.CompareTo(b.lane));
            toNodes.Sort((a, b) => a.lane.CompareTo(b.lane));
            var existing = new List<(int fromLane, int toLane)>();

            for (var i = 0; i < fromNodes.Count; i++)
            {
                var from = fromNodes[i];
                var outList = _edges[from.id];
                var wanted = RandomRangeInclusive(rng, Mathf.Max(1, def.minConnectionsPerNode), Mathf.Max(def.minConnectionsPerNode, def.maxConnectionsPerNode));
                wanted = Mathf.Clamp(wanted, 1, toNodes.Count);
                var targetOrder = BuildTargetOrder(i, fromNodes.Count, toNodes.Count);

                for (var k = 0; k < targetOrder.Count && outList.Count < wanted; k++)
                {
                    var idx = targetOrder[k];
                    var to = toNodes[idx];
                    if (outList.Contains(to.id)) continue;
                    if (WouldCross(existing, from.lane, to.lane)) continue;
                    outList.Add(to.id);
                    existing.Add((from.lane, to.lane));
                }

                for (var k = 0; k < targetOrder.Count && outList.Count < wanted; k++)
                {
                    var to = toNodes[targetOrder[k]];
                    if (outList.Contains(to.id)) continue;
                    outList.Add(to.id);
                    existing.Add((from.lane, to.lane));
                }
            }
        }

        private void EnsureIncomingPerNode(int layerCount)
        {
            for (var tier = 1; tier < layerCount; tier++)
            {
                if (!_nodesByTier.TryGetValue(tier, out var toNodes)) continue;
                if (!_nodesByTier.TryGetValue(tier - 1, out var fromNodes)) continue;

                for (var i = 0; i < toNodes.Count; i++)
                {
                    var to = toNodes[i];
                    if (HasIncoming(to.id, tier - 1)) continue;

                    RuntimeMapNode bestFrom = null;
                    var bestDist = int.MaxValue;
                    for (var f = 0; f < fromNodes.Count; f++)
                    {
                        var from = fromNodes[f];
                        var d = Mathf.Abs(from.lane - to.lane);
                        if (d >= bestDist) continue;
                        bestDist = d;
                        bestFrom = from;
                    }

                    if (bestFrom != null && !_edges[bestFrom.id].Contains(to.id))
                    {
                        _edges[bestFrom.id].Add(to.id);
                    }
                }
            }
        }

        private bool HasIncoming(string nodeId, int fromTier)
        {
            if (!_nodesByTier.TryGetValue(fromTier, out var fromNodes)) return false;
            for (var i = 0; i < fromNodes.Count; i++)
            {
                var from = fromNodes[i];
                if (_edges.TryGetValue(from.id, out var outs) && outs.Contains(nodeId)) return true;
            }
            return false;
        }

        private static bool WouldCross(List<(int fromLane, int toLane)> existing, int newFrom, int newTo)
        {
            for (var i = 0; i < existing.Count; i++)
            {
                var e = existing[i];
                var crossing = (newFrom < e.fromLane && newTo > e.toLane) || (newFrom > e.fromLane && newTo < e.toLane);
                if (crossing) return true;
            }
            return false;
        }

        private static List<int> BuildTargetOrder(int fromIndex, int fromCount, int toCount)
        {
            var target = fromCount <= 1 ? 0 : Mathf.RoundToInt((float)fromIndex / (fromCount - 1) * (toCount - 1));
            var order = new List<int>(toCount);
            order.Add(Mathf.Clamp(target, 0, toCount - 1));
            for (var r = 1; r < toCount; r++)
            {
                var left = target - r;
                var right = target + r;
                if (left >= 0) order.Add(left);
                if (right < toCount) order.Add(right);
            }
            return order;
        }

        private void AddToTier(RuntimeMapNode node)
        {
            if (!_nodesByTier.TryGetValue(node.tier, out var list))
            {
                list = new List<RuntimeMapNode>();
                _nodesByTier[node.tier] = list;
            }
            list.Add(node);
        }

        private static int RandomRangeInclusive(System.Random rng, int min, int max)
        {
            var a = Mathf.Min(min, max);
            var b = Mathf.Max(min, max);
            return rng.Next(a, b + 1);
        }
    }
}
