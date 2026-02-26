using System.Collections.Generic;
using ChessPrototype.Unity.Board;
using ChessPrototype.Unity.Data;
using UnityEngine;

namespace ChessPrototype.Unity.Enemies
{
    public enum EnemyIntentKind { Move, Capture, Web, Spawn, Wait }

    public sealed class EnemyIntent
    {
        public string actorId;
        public EnemyIntentKind kind;
        public GridPos from;
        public GridPos to;
        public List<GridPos> attackSquares = new List<GridPos>();
        public bool blocked;
        public string reason;
    }

    public sealed class EnemyPlan
    {
        public readonly List<EnemyIntent> intents = new List<EnemyIntent>();
        public readonly HashSet<string> reserved = new HashSet<string>();
    }

    public sealed class EnemyIntentSystem
    {
        private readonly BoardState _board;
        public EnemyIntentSystem(BoardState board) { _board = board; }

        private static string Key(GridPos p) => $"{p.row}:{p.col}";
        private static int Dist(GridPos a, GridPos b) => Mathf.Abs(a.row - b.row) + Mathf.Abs(a.col - b.col);

        public EnemyPlan BuildPlan()
        {
            var plan = new EnemyPlan();
            foreach (var kv in _board.UnitsById)
            {
                var u = kv.Value;
                if (u.faction != Faction.Enemy || u.status.IsSleeping) continue;
                var intent = BuildIntent(u, plan.reserved);
                if (intent == null) continue;
                plan.intents.Add(intent);
                if (intent.to.row != intent.from.row || intent.to.col != intent.from.col) plan.reserved.Add(Key(intent.to));
            }
            return plan;
        }

        public EnemyPlan ValidateOrRecompute(EnemyPlan plan)
        {
            if (plan == null) return BuildPlan();
            for (var i = 0; i < plan.intents.Count; i++)
            {
                var it = plan.intents[i];
                var live = _board.UnitsById.TryGetValue(it.actorId, out var actor) ? actor : null;
                if (live == null || live.pos.row != it.from.row || live.pos.col != it.from.col)
                {
                    return BuildPlan();
                }
            }
            return plan;
        }

        public void ExecuteSequential(
            EnemyPlan plan,
            System.Action<UnitRuntime, List<GridPos>> onAttack = null,
            System.Action<string, int> onDamage = null)
        {
            if (plan == null) return;
            for (var i = 0; i < plan.intents.Count; i++)
            {
                var it = plan.intents[i];
                if (!_board.UnitsById.TryGetValue(it.actorId, out var actor)) continue;

                // Move step with bounce-back stay if blocked.
                if (it.to.row != actor.pos.row || it.to.col != actor.pos.col)
                {
                    if (!_board.Move(actor.id, it.to))
                    {
                        it.blocked = true;
                        it.to = actor.pos;
                    }
                }

                if (it.kind == EnemyIntentKind.Capture || it.kind == EnemyIntentKind.Web)
                {
                    onAttack?.Invoke(actor, it.attackSquares);
                    for (var s = 0; s < it.attackSquares.Count; s++)
                    {
                        var sq = it.attackSquares[s];
                        if (!_board.Inside(sq)) continue;
                        var target = _board.At(sq);
                        if (target == null || target.faction == Faction.Enemy || target.faction == Faction.Neutral) continue;
                        var dmg = Mathf.Max(1, actor.attack);
                        _board.ApplyDamage(target.id, dmg);
                        onDamage?.Invoke(target.id, dmg);
                    }
                }
            }
        }

        private EnemyIntent BuildIntent(UnitRuntime enemy, HashSet<string> reserved)
        {
            UnitRuntime nearestPlayer = null;
            var best = int.MaxValue;
            foreach (var kv in _board.UnitsById)
            {
                var p = kv.Value;
                if (p.faction != Faction.Player) continue;
                var d = Dist(enemy.pos, p.pos);
                if (d < best) { best = d; nearestPlayer = p; }
            }
            if (nearestPlayer == null)
            {
                return new EnemyIntent { actorId = enemy.id, kind = EnemyIntentKind.Wait, from = enemy.pos, to = enemy.pos, reason = "no_target" };
            }

            var nowAttacks = ComputeAttackSquares(enemy, enemy.pos);
            if (Contains(nowAttacks, nearestPlayer.pos))
            {
                return new EnemyIntent
                {
                    actorId = enemy.id,
                    kind = EnemyIntentKind.Capture,
                    from = enemy.pos,
                    to = enemy.pos,
                    attackSquares = nowAttacks,
                    reason = "in_range"
                };
            }

            var step = NextStep(enemy.pos, nearestPlayer.pos);
            var blocked = !_board.Inside(step) || _board.Occupied(step) || reserved.Contains(Key(step));
            var to = blocked ? enemy.pos : step;
            var atk = ComputeAttackSquares(enemy, to);
            return new EnemyIntent
            {
                actorId = enemy.id,
                kind = Contains(atk, nearestPlayer.pos) ? EnemyIntentKind.Capture : EnemyIntentKind.Move,
                from = enemy.pos,
                to = to,
                blocked = blocked,
                attackSquares = atk,
                reason = blocked ? "blocked_bounce" : "advance"
            };
        }

        private static GridPos NextStep(GridPos from, GridPos to)
        {
            var dr = to.row - from.row;
            var dc = to.col - from.col;
            if (Mathf.Abs(dr) >= Mathf.Abs(dc)) return new GridPos(from.row + (dr == 0 ? 0 : dr > 0 ? 1 : -1), from.col);
            return new GridPos(from.row, from.col + (dc == 0 ? 0 : dc > 0 ? 1 : -1));
        }

        private static List<GridPos> ComputeAttackSquares(UnitRuntime enemy, GridPos from)
        {
            // Shared source-of-truth for preview + execution.
            var outSquares = new List<GridPos>();
            if (enemy.kind == UnitKind.Pawn && enemy.status != null && enemy.status.pawnPromoted)
            {
                AddAdjacentAttackSquares(from, outSquares);
                return outSquares;
            }
            if (enemy.kind == UnitKind.Skunk)
            {
                outSquares.Add(new GridPos(from.row + 1, from.col));
                outSquares.Add(new GridPos(from.row + 1, from.col + 1));
                outSquares.Add(new GridPos(from.row, from.col + 1));
                outSquares.Add(new GridPos(from.row + 1, from.col - 1));
                return outSquares;
            }
            if (enemy.kind == UnitKind.Spider)
            {
                outSquares.Add(new GridPos(from.row + 1, from.col));
                outSquares.Add(new GridPos(from.row - 1, from.col));
                return outSquares;
            }
            outSquares.Add(new GridPos(from.row + 1, from.col));
            outSquares.Add(new GridPos(from.row - 1, from.col));
            outSquares.Add(new GridPos(from.row, from.col + 1));
            outSquares.Add(new GridPos(from.row, from.col - 1));
            return outSquares;
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

        private static bool Contains(List<GridPos> list, GridPos p)
        {
            for (var i = 0; i < list.Count; i++) if (list[i].row == p.row && list[i].col == p.col) return true;
            return false;
        }
    }
}
