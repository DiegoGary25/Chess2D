using UnityEngine;

namespace ChessPrototype.Unity.Data
{
    public enum EnemyAttackMode
    {
        MeleeSingle,
        MeleeCluster,
        LinearProjectile,
        Ram,
        Area2x2Front,
        LineHazard
    }

    public enum EnemyMoveMode
    {
        Step,
        Leap,
        Fly,
        RamOnly
    }

    [CreateAssetMenu(menuName = "ChessPrototype/EnemyBehaviorDefinition", fileName = "EnemyBehaviorDefinition")]
    public sealed class EnemyBehaviorDefinition : ScriptableObject
    {
        public UnitKind kind;
        [Header("Attack")]
        public EnemyAttackMode attackMode = EnemyAttackMode.MeleeSingle;
        [Min(1)] public int baseDamage = 1;
        [Min(1)] public int attackRange = 1;
        [Min(1)] public int attackTiles = 1;
        public bool attackStopsAtFirstObject = true;
        public bool attackRetargetsOnResolve = true;
        [Header("Movement")]
        public EnemyMoveMode moveMode = EnemyMoveMode.Step;
        [Min(0)] public int moveRange = 1;
        public bool ignoresOccupiedTiles;
        [Header("Facing/Targeting")]
        public bool faceNearestPlayer = true;
    }
}
