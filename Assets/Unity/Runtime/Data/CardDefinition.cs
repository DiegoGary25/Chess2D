using UnityEngine;

namespace ChessPrototype.Unity.Data
{
    [CreateAssetMenu(menuName = "ChessPrototype/CardDefinition", fileName = "CardDefinition")]
    public sealed class CardDefinition : ScriptableObject
    {
        public string cardId;
        public string displayName;
        public CardKind kind;
        public UnitKind summonKind;
        public int cost = 1;
        public int amount = 1;
        [TextArea] public string description;
        public Sprite icon;
    }
}
