using ChessPrototype.Unity.Data;
using UnityEngine;
using System.Collections.Generic;

namespace ChessPrototype.Unity.Core
{
    public sealed class GameSessionState : MonoBehaviour
    {
        [SerializeField] private GameConfigDefinition config;
        [SerializeField] private int seed = 1;
        [SerializeField] private int persistentKingHp = 5;
        [SerializeField] private int gold;
        [SerializeField] private string lastCompletedNodeId;
        [SerializeField] private int encounterIndex;
        [SerializeField] private List<TrinketDefinition> ownedTrinkets = new List<TrinketDefinition>();

        public GameConfigDefinition Config => config;
        public int Seed => seed;
        public int PersistentKingHp => persistentKingHp;
        public int Gold => gold;
        public string LastCompletedNodeId => lastCompletedNodeId;
        public int EncounterIndex => encounterIndex;
        public IReadOnlyList<TrinketDefinition> OwnedTrinkets => ownedTrinkets;

        public void SetConfig(GameConfigDefinition value) => config = value;

        public void EnsureInitialized()
        {
            if (seed <= 0) seed = Random.Range(1, int.MaxValue);
            if (config != null && persistentKingHp <= 0) persistentKingHp = config.kingPersistentHp;
            if (config != null && gold <= 0) gold = Mathf.Max(0, config.startingGold);
        }

        public void SetKingHp(int hp) => persistentKingHp = Mathf.Max(0, hp);
        public void SetGold(int value) => gold = Mathf.Max(0, value);
        public void AddGold(int amount) => gold = Mathf.Max(0, gold + amount);
        public bool TrySpendGold(int amount)
        {
            if (amount < 0 || gold < amount) return false;
            gold -= amount;
            return true;
        }

        public bool OwnsTrinket(TrinketDefinition trinket)
        {
            return trinket != null && ownedTrinkets.Contains(trinket);
        }

        public bool TryAddTrinket(TrinketDefinition trinket)
        {
            if (trinket == null) return false;
            if (!trinket.allowDuplicates && ownedTrinkets.Contains(trinket)) return false;
            ownedTrinkets.Add(trinket);
            return true;
        }

        public int GetPlayerMoveBonusPerTurn()
        {
            var total = 0;
            for (var i = 0; i < ownedTrinkets.Count; i++)
            {
                var trinket = ownedTrinkets[i];
                if (trinket == null || trinket.effectType != TrinketEffectType.BonusMovePerTurn) continue;
                total += Mathf.Max(0, trinket.amount);
            }
            return total;
        }

        public int RollEncounterGoldReward()
        {
            var min = config != null ? Mathf.Max(0, config.encounterGoldMin) : 30;
            var max = config != null ? Mathf.Max(min, config.encounterGoldMax) : 100;
            var rng = new System.Random(seed + encounterIndex * 48611 + 17);
            return rng.Next(min, max + 1);
        }

        public void MarkNodeComplete(string nodeId) { lastCompletedNodeId = nodeId; encounterIndex += 1; }
    }
}



