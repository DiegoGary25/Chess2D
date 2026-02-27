using UnityEngine;

namespace ChessPrototype.Unity.Data
{
    public enum GridEffectType
    {
        Web,
        Spray,
        Cloud
    }

    [CreateAssetMenu(menuName = "ChessPrototype/GridEffectDefinition", fileName = "GridEffectDefinition")]
    public sealed class GridEffectDefinition : ScriptableObject
    {
        public GridEffectType type;
        public string displayName;
        [Min(1)] public int turnsRemaining = 1;
        [Min(0)] public int damageOnEnter = 1;
        [Min(0)] public int damageOnStartTurn = 0;
        [Min(0)] public int rootedTurns = 0;
        public bool destroyOnFirstTrigger = true;
        public Vector2Int footprint = Vector2Int.one;
        public GameObject viewPrefab;
        public Sprite tileIcon;
    }
}
