using UnityEngine;

namespace ChessPrototype.Unity.Data
{
    public enum EnemySpecialType
    {
        None,
        Shriek,
        PackHowl,
        WebTrap,
        SuperLeap,
        StenchMissile,
        Sleep,
        Enrage,
        Rend,
        AlphaCall,
        Lunge
    }

    [CreateAssetMenu(menuName = "ChessPrototype/EnemySpecialDefinition", fileName = "EnemySpecialDefinition")]
    public sealed class EnemySpecialDefinition : ScriptableObject
    {
        public UnitKind kind;
        public EnemySpecialType type = EnemySpecialType.None;
        [Range(0f, 1f)] public float triggerChance = 0f;
        public Sprite intentIcon;

        [Header("Generic Params")]
        public int range = 1;
        public int radius = 1;
        public int turns = 1;
        public int amount = 1;
        public Vector2Int footprint = Vector2Int.one;
    }
}
