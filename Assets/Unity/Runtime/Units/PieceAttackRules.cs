using System.Collections.Generic;
using ChessPrototype.Unity.Board;
using ChessPrototype.Unity.Data;
using UnityEngine;

namespace ChessPrototype.Unity.Units
{
    public static class PieceAttackRules
    {
        public static List<GridPos> ComputeAttackSquares(BoardState board, UnitRuntime unit, GridPos from, GridPos preferredTarget)
        {
            var outSquares = new List<GridPos>();
            if (board == null || unit == null) return outSquares;

            switch (unit.kind)
            {
                case UnitKind.King:
                    AddSingleTargetOrAdjacentAttack(from, preferredTarget, outSquares);
                    break;
                case UnitKind.Pawn:
                    AddPawnDiagonalAttack(from, preferredTarget, outSquares);
                    break;
                case UnitKind.Knight:
                    AddKnightAttackSquares(board, unit.faction, from, preferredTarget, outSquares);
                    break;
                case UnitKind.Bishop:
                    AddRayAttackTowardTarget(board, from, preferredTarget, outSquares, true, false);
                    break;
                case UnitKind.Queen:
                    AddRayAttackTowardTarget(board, from, preferredTarget, outSquares, true, true);
                    break;
                case UnitKind.Rook:
                    AddRayAttackTowardTarget(board, from, preferredTarget, outSquares, false, true);
                    break;
                case UnitKind.Bat:
                    AddLineAttack(from, preferredTarget, outSquares, 1);
                    break;
                case UnitKind.Coyote:
                case UnitKind.WolfAlpha:
                    AddFrontConeAttack(from, preferredTarget, outSquares);
                    break;
                case UnitKind.Skunk:
                    // Skunk uses special-only actions; no basic attack footprint.
                    break;
                case UnitKind.Spider:
                    outSquares.Add(new GridPos(from.row + 1, from.col));
                    outSquares.Add(new GridPos(from.row - 1, from.col));
                    break;
                case UnitKind.Owl:
                case UnitKind.Boar:
                    // Owl and Boar use special-only actions; no basic attack footprint.
                    break;
                case UnitKind.Toad:
                    AddToadAttackSquares(board, from, preferredTarget, outSquares);
                    break;
                default:
                    outSquares.Add(new GridPos(from.row + 1, from.col));
                    outSquares.Add(new GridPos(from.row - 1, from.col));
                    outSquares.Add(new GridPos(from.row, from.col + 1));
                    outSquares.Add(new GridPos(from.row, from.col - 1));
                    break;
            }

            RemoveSelfTile(outSquares, from);
            return outSquares;
        }

        private static void AddToadAttackSquares(BoardState board, GridPos from, GridPos preferredTarget, List<GridPos> outSquares)
        {
            if (from.row == preferredTarget.row || from.col == preferredTarget.col)
            {
                AddLineAttackToBoardEdge(board, from, preferredTarget, outSquares);
            }
        }

        private static void AddSingleTargetOrAdjacentAttack(GridPos from, GridPos preferredTarget, List<GridPos> outSquares)
        {
            if (IsAdjacentAll(from, preferredTarget))
            {
                outSquares.Add(preferredTarget);
                return;
            }
            AddAdjacentAttackSquares(from, outSquares);
        }

        private static void AddKnightAttackSquares(BoardState board, Faction attackerFaction, GridPos from, GridPos preferredTarget, List<GridPos> outSquares)
        {
            if (IsKnightLeap(from, preferredTarget))
            {
                var target = board.At(preferredTarget);
                if (target != null && target.faction != attackerFaction && target.faction != Faction.Neutral)
                {
                    outSquares.Add(preferredTarget);
                    return;
                }
            }
            AddKnightCaptureTargets(board, from, attackerFaction, outSquares);
        }

        private static void AddKnightCaptureTargets(BoardState board, GridPos from, Faction attackerFaction, List<GridPos> outSquares)
        {
            void TryAdd(GridPos p)
            {
                if (!board.Inside(p)) return;
                var target = board.At(p);
                if (target == null || target.faction == attackerFaction || target.faction == Faction.Neutral) return;
                if (!Contains(outSquares, p)) outSquares.Add(p);
            }

            TryAdd(new GridPos(from.row + 2, from.col + 1));
            TryAdd(new GridPos(from.row + 2, from.col - 1));
            TryAdd(new GridPos(from.row - 2, from.col + 1));
            TryAdd(new GridPos(from.row - 2, from.col - 1));
            TryAdd(new GridPos(from.row + 1, from.col + 2));
            TryAdd(new GridPos(from.row + 1, from.col - 2));
            TryAdd(new GridPos(from.row - 1, from.col + 2));
            TryAdd(new GridPos(from.row - 1, from.col - 2));
        }

        private static void AddPawnDiagonalAttack(GridPos from, GridPos preferredTarget, List<GridPos> outSquares)
        {
            var dr = preferredTarget.row - from.row;
            var forward = dr == 0 ? 1 : (dr > 0 ? 1 : -1);
            outSquares.Add(new GridPos(from.row + forward, from.col - 1));
            outSquares.Add(new GridPos(from.row + forward, from.col + 1));
        }

        private static void AddLineAttackToBoardEdge(BoardState board, GridPos from, GridPos preferredTarget, List<GridPos> outSquares)
        {
            var step = DirectionStep(from, preferredTarget);
            if (step.row == 0 && step.col == 0) step = new GridPos(-1, 0);

            var probe = new GridPos(from.row + step.row, from.col + step.col);
            while (board.Inside(probe))
            {
                outSquares.Add(probe);
                if (board.Occupied(probe)) break;
                probe = new GridPos(probe.row + step.row, probe.col + step.col);
            }
        }

        private static void AddRayAttackTowardTarget(BoardState board, GridPos from, GridPos preferredTarget, List<GridPos> outSquares, bool diag, bool ortho)
        {
            var step = DirectionStepExtended(from, preferredTarget, diag, ortho);
            if (step.row == 0 && step.col == 0)
            {
                AddRayAttackFallback(from, outSquares, diag, ortho);
                return;
            }

            var probe = new GridPos(from.row + step.row, from.col + step.col);
            while (board.Inside(probe))
            {
                outSquares.Add(probe);
                if (board.Occupied(probe)) break;
                probe = new GridPos(probe.row + step.row, probe.col + step.col);
            }
        }

        private static void AddRayAttackFallback(GridPos from, List<GridPos> outSquares, bool diag, bool ortho)
        {
            if (ortho)
            {
                outSquares.Add(new GridPos(from.row + 1, from.col));
                outSquares.Add(new GridPos(from.row - 1, from.col));
                outSquares.Add(new GridPos(from.row, from.col + 1));
                outSquares.Add(new GridPos(from.row, from.col - 1));
            }
            if (diag)
            {
                outSquares.Add(new GridPos(from.row + 1, from.col + 1));
                outSquares.Add(new GridPos(from.row + 1, from.col - 1));
                outSquares.Add(new GridPos(from.row - 1, from.col + 1));
                outSquares.Add(new GridPos(from.row - 1, from.col - 1));
            }
        }

        private static void AddLineAttack(GridPos from, GridPos preferredTarget, List<GridPos> outSquares, int range)
        {
            var step = DirectionStep(from, preferredTarget);
            if (step.row == 0 && step.col == 0) step = new GridPos(-1, 0);
            for (var i = 1; i <= Mathf.Max(1, range); i++)
            {
                outSquares.Add(new GridPos(from.row + step.row * i, from.col + step.col * i));
            }
        }

        private static void AddFrontConeAttack(GridPos from, GridPos preferredTarget, List<GridPos> outSquares)
        {
            var step = DirectionStep(from, preferredTarget);
            if (step.row == 0 && step.col == 0) step = new GridPos(-1, 0);
            if (step.row != 0)
            {
                var frontRow = from.row + step.row;
                outSquares.Add(new GridPos(frontRow, from.col));
                outSquares.Add(new GridPos(frontRow, from.col - 1));
                outSquares.Add(new GridPos(frontRow, from.col + 1));
                return;
            }

            var frontCol = from.col + step.col;
            outSquares.Add(new GridPos(from.row, frontCol));
            outSquares.Add(new GridPos(from.row - 1, frontCol));
            outSquares.Add(new GridPos(from.row + 1, frontCol));
        }

        private static void AddAdjacentAttackSquares(GridPos from, List<GridPos> outSquares)
        {
            outSquares.Add(new GridPos(from.row + 1, from.col));
            outSquares.Add(new GridPos(from.row - 1, from.col));
            outSquares.Add(new GridPos(from.row, from.col + 1));
            outSquares.Add(new GridPos(from.row, from.col - 1));
            outSquares.Add(new GridPos(from.row + 1, from.col + 1));
            outSquares.Add(new GridPos(from.row + 1, from.col - 1));
            outSquares.Add(new GridPos(from.row - 1, from.col + 1));
            outSquares.Add(new GridPos(from.row - 1, from.col - 1));
        }

        private static GridPos DirectionStep(GridPos from, GridPos to)
        {
            var dr = to.row - from.row;
            var dc = to.col - from.col;
            if (Mathf.Abs(dr) >= Mathf.Abs(dc))
            {
                return new GridPos(dr == 0 ? 0 : (dr > 0 ? 1 : -1), 0);
            }
            return new GridPos(0, dc == 0 ? 0 : (dc > 0 ? 1 : -1));
        }

        private static GridPos DirectionStepExtended(GridPos from, GridPos to, bool diag, bool ortho)
        {
            var dr = to.row - from.row;
            var dc = to.col - from.col;
            if (diag && Mathf.Abs(dr) == Mathf.Abs(dc) && dr != 0)
            {
                return new GridPos(dr > 0 ? 1 : -1, dc > 0 ? 1 : -1);
            }
            if (ortho)
            {
                if (dr == 0 && dc != 0) return new GridPos(0, dc > 0 ? 1 : -1);
                if (dc == 0 && dr != 0) return new GridPos(dr > 0 ? 1 : -1, 0);
            }
            return new GridPos(0, 0);
        }

        private static bool IsAdjacentAll(GridPos from, GridPos to)
        {
            var dr = Mathf.Abs(to.row - from.row);
            var dc = Mathf.Abs(to.col - from.col);
            return dr <= 1 && dc <= 1 && (dr != 0 || dc != 0);
        }

        private static bool IsKnightLeap(GridPos from, GridPos to)
        {
            var dr = Mathf.Abs(to.row - from.row);
            var dc = Mathf.Abs(to.col - from.col);
            return (dr == 2 && dc == 1) || (dr == 1 && dc == 2);
        }

        private static bool Contains(List<GridPos> list, GridPos p)
        {
            for (var i = 0; i < list.Count; i++) if (list[i].row == p.row && list[i].col == p.col) return true;
            return false;
        }

        private static void RemoveSelfTile(List<GridPos> list, GridPos self)
        {
            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].row == self.row && list[i].col == self.col) list.RemoveAt(i);
            }
        }
    }
}
