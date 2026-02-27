using System;
using System.Collections.Generic;

namespace ChessPrototype.Unity.Data
{
    public enum Faction { Player, Enemy, Neutral }
    public enum TurnPhase { Player, Enemy, Resolving }
    public enum InputMode { Idle, SelectingPiece, MoveTargeting, AttackTargeting, CardTargeting, Animating }
    public enum MapNodeType
    {
        // Legacy values kept for compatibility with existing data/assets.
        Battle,
        Elite,
        Boss,
        Shop,
        Event,
        Rest,
        // Slay-the-Spire style map types.
        Enemy,
        Unknown,
        Merchant,
        Treasure
    }
    public enum CardKind { Summon, HealSmall, Shield, BearTrap, Barricade, SpikePit }
    public enum UnitKind
    {
        King, Pawn, Knight, Bishop, Rook, Queen,
        Bat, Coyote, Owl, Boar, Snake, Spider, Skunk, WolfAlpha, Bear, Toad, WolfPup,
        Rock, Cave
    }

    [Serializable]
    public struct GridPos
    {
        public int row;
        public int col;
        public GridPos(int r, int c) { row = r; col = c; }
        public override string ToString() => $"({row},{col})";
    }

    [Serializable]
    public sealed class UnitStatus
    {
        public int sleepingTurns;
        public int rootedTurns;
        public int poisonedTurns;
        public int shieldCharge;
        public int nextAttackDamageModifier;
        public bool pawnPromoted;
        public bool IsSleeping => sleepingTurns > 0;
        public bool IsRooted => rootedTurns > 0;
    }

    [Serializable]
    public sealed class UnitRuntime
    {
        public string id;
        public UnitKind kind;
        public Faction faction;
        public GridPos pos;
        public int hp;
        public int maxHp;
        public int attack;
        public bool canMove;
        public bool canAttack;
        public bool isStructure;
        public string spawnedByCaveId;
        public UnitStatus status = new UnitStatus();
    }

    [Serializable]
    public sealed class TrapRuntime
    {
        public int row;
        public int col;
        public CardKind kind;
        public int damage = 1;
        public int sleepTurns;
    }

    [Serializable]
    public sealed class CaveRuntime
    {
        public string id;
        public int row;
        public int col;
        public int turnsUntilNextSpawn = 2;
        public int spawnCharges = 3;
        public int maxAliveFromThisCave = 2;
        public List<SpawnWeight> spawnPool = new List<SpawnWeight>();
    }

    [Serializable]
    public sealed class SpawnWeight
    {
        public UnitKind kind;
        public int weight = 1;
    }

    [Serializable]
    public sealed class Placement
    {
        public UnitKind kind;
        public int row;
        public int col;
    }

    [Serializable]
    public sealed class CaveTemplate
    {
        public string id;
        public int row;
        public int col;
        public int turnsUntilNextSpawn = 2;
        public int spawnCharges = 3;
        public int maxAliveFromThisCave = 2;
        public List<SpawnWeight> spawnPool = new List<SpawnWeight>();
    }

    [Serializable]
    public sealed class RunMapNodeData
    {
        public string id;
        public int tier;
        public int lane;
        public MapNodeType type;
    }

    [Serializable]
    public sealed class RunMapEdgeData
    {
        public string from;
        public string to;
    }
}
