using UnityEngine;

namespace ChessPrototype.Unity.Data
{
    [CreateAssetMenu(menuName = "ChessPrototype/EnemyDefinition", fileName = "EnemyDefinition")]
    public sealed class EnemyDefinition : ScriptableObject
    {
        public UnitKind kind;
        public string displayName;
        [TextArea] public string description;
        public int maxHp = 1;
        public int attack = 1;
        public string behaviorKey = "default";

        [Header("Visual")]
        public Sprite icon;
        public float spriteYOffset;
        [Min(0.01f)] public float visualScale = 1f;
        public GameObject shadePrefab;
        public float shadeYOffset;

        [Header("Overrides")]
        public GameObject viewOverridePrefab;
    }
}
