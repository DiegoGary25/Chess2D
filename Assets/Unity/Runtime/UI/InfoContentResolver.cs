using ChessPrototype.Unity.Core;
using ChessPrototype.Unity.Data;
using UnityEngine;

namespace ChessPrototype.Unity.UI
{
    public static class InfoContentResolver
    {
        public static CardInfoPanelData ForCard(CardDefinition card)
        {
            if (card == null)
            {
                return new CardInfoPanelData
                {
                    title = "Unknown Card",
                    image = null,
                    description = "No data.",
                    hasUnitStats = false,
                    healthMax = 0,
                    damageStandard = 0
                };
            }

            var description = BuildCardDescription(card);
            var hasStats = false;
            var hp = 0;
            var damage = 0;
            if (card.kind == CardKind.Summon)
            {
                if (TryResolvePieceStats(card.summonKind, out hp, out damage))
                {
                    hasStats = true;
                }
            }

            return new CardInfoPanelData
            {
                title = string.IsNullOrWhiteSpace(card.displayName) ? card.kind.ToString() : card.displayName,
                image = card.icon,
                description = description,
                hasUnitStats = hasStats,
                healthMax = hp,
                damageStandard = damage
            };
        }

        public static TrinketInfoPanelData ForTrinket(TrinketDefinition trinket)
        {
            if (trinket == null)
            {
                return new TrinketInfoPanelData
                {
                    title = "Unknown Trinket",
                    image = null,
                    description = "No data."
                };
            }

            return new TrinketInfoPanelData
            {
                title = string.IsNullOrWhiteSpace(trinket.displayName) ? trinket.trinketId : trinket.displayName,
                image = trinket.icon,
                description = BuildTrinketDescription(trinket)
            };
        }

        private static string BuildCardDescription(CardDefinition card)
        {
            var effect = BuildCardEffectLine(card);
            var movement = card.kind == CardKind.Summon ? BuildMovementSummary(card.summonKind) : "No unit movement; this card applies an effect.";
            var special = card.kind == CardKind.Summon ? BuildSpecialSummary(card.summonKind) : BuildSpellSpecialSummary(card.kind);
            return $"Effect: {effect}\nMovement: {movement}\nSpecial: {special}";
        }

        private static string BuildCardEffectLine(CardDefinition card)
        {
            switch (card.kind)
            {
                case CardKind.HealSmall:
                    return $"Restore {Mathf.Max(1, card.amount)} HP to one ally.";
                case CardKind.Shield:
                    return $"Grant {Mathf.Max(1, card.amount)} shield charge to one ally.";
                case CardKind.Summon:
                    return $"Summon {card.summonKind} on a valid empty tile.";
                case CardKind.Barricade:
                    return "Place a Rock blocker on an empty tile.";
                case CardKind.BearTrap:
                    return "Place a trap that damages and can sleep the unit that steps on it.";
                case CardKind.SpikePit:
                    return "Place a trap that damages and can root the unit that steps on it.";
                default:
                    return !string.IsNullOrWhiteSpace(card.description) ? card.description.Trim() : card.kind.ToString();
            }
        }

        private static string BuildSpellSpecialSummary(CardKind kind)
        {
            switch (kind)
            {
                case CardKind.HealSmall:
                    return "Single-target sustain.";
                case CardKind.Shield:
                    return "Mitigation before incoming damage.";
                case CardKind.Barricade:
                    return "Board control by blocking lanes.";
                case CardKind.BearTrap:
                    return "Trigger-based control with damage/sleep.";
                case CardKind.SpikePit:
                    return "Trigger-based control with damage/root.";
                default:
                    return "No additional special behavior.";
            }
        }

        private static string BuildTrinketDescription(TrinketDefinition trinket)
        {
            var effect = BuildTrinketEffectLine(trinket);
            var notes = string.IsNullOrWhiteSpace(trinket.description)
                ? "Passive effect active while this trinket is owned."
                : trinket.description.Trim();
            return $"Effect: {effect}\nNotes: {notes}";
        }

        private static string BuildTrinketEffectLine(TrinketDefinition trinket)
        {
            switch (trinket.effectType)
            {
                case TrinketEffectType.BonusMovePerTurn:
                    return $"+{Mathf.Max(0, trinket.amount)} move action(s) per player turn.";
                case TrinketEffectType.None:
                default:
                    return !string.IsNullOrWhiteSpace(trinket.description) ? trinket.description.Trim() : "No active gameplay effect.";
            }
        }

        private static string BuildMovementSummary(UnitKind kind)
        {
            switch (kind)
            {
                case UnitKind.Pawn: return "Moves 1 forward.";
                case UnitKind.Knight: return "Moves in L-shapes.";
                case UnitKind.Bishop: return "Moves on diagonals.";
                case UnitKind.Rook: return "Moves orthogonally in straight lines.";
                case UnitKind.Queen: return "Moves diagonally and orthogonally in straight lines.";
                case UnitKind.King: return "Moves 1 tile in any direction.";
                case UnitKind.Bat: return "Short-step movement; lines up shriek pressure.";
                case UnitKind.Coyote: return "Mobile flanker that repositions for pack play.";
                case UnitKind.Owl: return "Repositions for line-of-fire sleep shots.";
                case UnitKind.Boar: return "Positions for charge lanes.";
                case UnitKind.Snake: return "Short-range skirmisher movement.";
                case UnitKind.Skunk: return "Kites for area special placement.";
                case UnitKind.Bear: return "Frontline bruiser movement.";
                case UnitKind.Toad: return "Positions on orthogonal lines for pulls.";
                default: return "Uses standard unit movement rules.";
            }
        }

        private static string BuildSpecialSummary(UnitKind kind)
        {
            switch (kind)
            {
                case UnitKind.Bat: return "Shriek weakens targets along a line; adjacent tiles are normal attacks.";
                case UnitKind.Coyote: return "Pack howl grants +1 damage for the next attack turn to all coyotes.";
                case UnitKind.Owl: return "Sleep strike on straight-line target; awakened enemies cannot be slept again.";
                case UnitKind.Boar: return "Charge only with run-up space; otherwise cannot perform charge.";
                case UnitKind.Skunk: return "Area stench special except adjacent tiles, where normal attack rules apply.";
                case UnitKind.Toad: return "Pulls targets 1 tile on clear orthogonal lines at distance 2+; adjacent uses normal attack.";
                case UnitKind.Bear: return "Heals 1 HP when damaging an enemy (max-heal capped by max HP).";
                case UnitKind.Snake: return "Applies pressure through close-range attrition.";
                case UnitKind.Pawn: return "No extra special beyond core piece rules.";
                case UnitKind.Knight: return "No extra special beyond leap attack pattern.";
                case UnitKind.Bishop: return "No extra special beyond line control.";
                case UnitKind.Rook: return "No extra special beyond line control.";
                case UnitKind.Queen: return "No extra special beyond combined line control.";
                case UnitKind.King: return "No extra special beyond core role.";
                default: return "No additional special behavior.";
            }
        }

        private static bool TryResolvePieceStats(UnitKind kind, out int hp, out int attack)
        {
            hp = 0;
            attack = 0;
            var session = Object.FindObjectOfType<GameSessionState>();
            var cfg = session != null ? session.Config : null;
            if (cfg == null || cfg.pieceDefinitions == null) return false;
            for (var i = 0; i < cfg.pieceDefinitions.Count; i++)
            {
                var def = cfg.pieceDefinitions[i];
                if (def == null || def.kind != kind) continue;
                hp = Mathf.Max(0, def.maxHp);
                attack = Mathf.Max(0, def.attack);
                return true;
            }
            return false;
        }
    }
}
