using ChessPrototype.Unity.Data;
using UnityEngine;

namespace ChessPrototype.Unity.Core
{
    public sealed class GameSessionState : MonoBehaviour
    {
        [SerializeField] private GameConfigDefinition config;
        [SerializeField] private int seed = 1;
        [SerializeField] private int persistentKingHp = 5;
        [SerializeField] private string lastCompletedNodeId;
        [SerializeField] private int encounterIndex;

        public GameConfigDefinition Config => config;
        public int Seed => seed;
        public int PersistentKingHp => persistentKingHp;
        public string LastCompletedNodeId => lastCompletedNodeId;
        public int EncounterIndex => encounterIndex;

        public void SetConfig(GameConfigDefinition value) => config = value;

        public void EnsureInitialized()
        {
            if (seed <= 0) seed = Random.Range(1, int.MaxValue);
            if (config != null && persistentKingHp <= 0) persistentKingHp = config.kingPersistentHp;
        }

        public void SetKingHp(int hp) => persistentKingHp = Mathf.Max(0, hp);
        public void MarkNodeComplete(string nodeId) { lastCompletedNodeId = nodeId; encounterIndex += 1; }
    }
}



