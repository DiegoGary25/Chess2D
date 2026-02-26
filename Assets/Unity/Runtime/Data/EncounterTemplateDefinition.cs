using System.Collections.Generic;
using UnityEngine;

namespace ChessPrototype.Unity.Data
{
    [CreateAssetMenu(menuName = "ChessPrototype/EncounterTemplate", fileName = "EncounterTemplate")]
    public sealed class EncounterTemplateDefinition : ScriptableObject
    {
        public string encounterId;
        public int boardSize = 4;
        public List<Placement> enemyPlacements = new List<Placement>();
        public List<CaveTemplate> caves = new List<CaveTemplate>();
    }
}
