using UnityEngine;

namespace ChessPrototype.Unity.Data
{
    public enum TrinketEffectType
    {
        None,
        BonusMovePerTurn
    }

    [CreateAssetMenu(menuName = "ChessPrototype/TrinketDefinition", fileName = "TrinketDefinition")]
    public sealed class TrinketDefinition : ScriptableObject
    {
        public string trinketId;
        public string displayName;
        [TextArea] public string description;
        public Sprite icon;
        public int shopCost = 60;
        public bool allowDuplicates;
        public TrinketEffectType effectType = TrinketEffectType.None;
        public int amount = 1;
    }
}
