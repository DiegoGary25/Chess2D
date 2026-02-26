using ChessPrototype.Unity.Data;
using ChessPrototype.Unity.Encounters;
using UnityEngine;

namespace ChessPrototype.Unity.UI
{
    public sealed class UnitClickProxy : MonoBehaviour
    {
        private EncounterController _encounter;
        private GridPos _pos;

        public void Bind(EncounterController encounter, GridPos pos)
        {
            _encounter = encounter;
            _pos = pos;
        }

        private void OnMouseDown()
        {
            if (_encounter == null) return;
            _encounter.SelectAt(_pos);
        }
    }
}
