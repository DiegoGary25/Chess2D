using UnityEngine;

namespace ChessPrototype.Unity.Data
{
    public enum IntentTelegraphMode
    {
        Melee,
        Ranged,
        Ram
    }

    [CreateAssetMenu(menuName = "ChessPrototype/EnemyIntentVisualDefinition", fileName = "EnemyIntentVisualDefinition")]
    public sealed class EnemyIntentVisualDefinition : ScriptableObject
    {
        public UnitKind kind;
        public IntentTelegraphMode mode = IntentTelegraphMode.Melee;
        [Header("Static Sprites")]
        public Sprite startSprite;
        public Sprite middleSprite;
        public Sprite endSprite;
        public Sprite markerSprite;
        [Header("Animated Frames")]
        public Sprite[] startFrames;
        public Sprite[] middleFrames;
        public Sprite[] endFrames;
        public Sprite[] markerFrames;
        [Min(0f)] public float fps = 2f;
    }
}
