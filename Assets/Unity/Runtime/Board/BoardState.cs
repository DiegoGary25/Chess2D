using System.Collections.Generic;
using ChessPrototype.Unity.Data;

namespace ChessPrototype.Unity.Board
{
    public sealed class BoardState
    {
        public int Size { get; private set; } = 4;
        public readonly Dictionary<string, UnitRuntime> UnitsById = new Dictionary<string, UnitRuntime>();
        private readonly Dictionary<string, string> _occ = new Dictionary<string, string>();

        private static string Key(GridPos p) => $"{p.row}:{p.col}";

        public void Reset(int size)
        {
            Size = size;
            UnitsById.Clear();
            _occ.Clear();
        }

        public bool Inside(GridPos p) => p.row >= 0 && p.col >= 0 && p.row < Size && p.col < Size;
        public bool Occupied(GridPos p) => _occ.ContainsKey(Key(p));

        public UnitRuntime At(GridPos p)
        {
            if (!_occ.TryGetValue(Key(p), out var id)) return null;
            return UnitsById.TryGetValue(id, out var u) ? u : null;
        }

        public bool Add(UnitRuntime unit)
        {
            if (unit == null || string.IsNullOrWhiteSpace(unit.id)) return false;
            if (!Inside(unit.pos) || Occupied(unit.pos) || UnitsById.ContainsKey(unit.id)) return false;
            UnitsById[unit.id] = unit;
            _occ[Key(unit.pos)] = unit.id;
            return true;
        }

        public bool Move(string id, GridPos to)
        {
            if (!UnitsById.TryGetValue(id, out var unit)) return false;
            if (!Inside(to) || Occupied(to)) return false;
            _occ.Remove(Key(unit.pos));
            unit.pos = to;
            _occ[Key(to)] = id;
            return true;
        }

        public bool Remove(string id)
        {
            if (!UnitsById.TryGetValue(id, out var unit)) return false;
            UnitsById.Remove(id);
            _occ.Remove(Key(unit.pos));
            return true;
        }

        public bool ApplyDamage(string id, int damage)
        {
            if (!UnitsById.TryGetValue(id, out var unit)) return false;
            var incoming = damage <= 0 ? 0 : damage;
            if (unit.status.shieldCharge > 0)
            {
                incoming = incoming > 0 ? incoming - 1 : 0;
                unit.status.shieldCharge = 0;
            }
            if (incoming > 0) unit.hp -= incoming;
            if (unit.hp <= 0) Remove(id);
            return true;
        }
    }
}

