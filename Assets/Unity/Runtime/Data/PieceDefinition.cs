using UnityEngine;

namespace ChessPrototype.Unity.Data
{
    [CreateAssetMenu(menuName = "ChessPrototype/PieceDefinition", fileName = "PieceDefinition")]
    public sealed class PieceDefinition : ScriptableObject
    {
        public UnitKind kind;
        public string displayName;
        [TextArea] public string description;
        public int maxHp = 1;
        public int attack = 1;

        [Header("Visual")]
        public Sprite icon;
        public float spriteYOffset;
        [Min(0.01f)] public float visualScale = 1f;
        public GameObject shadePrefab;
        public float shadeYOffset;

        [Header("Animation")]
        public UnitAnimationDefinition animations = new UnitAnimationDefinition();
    }
}
