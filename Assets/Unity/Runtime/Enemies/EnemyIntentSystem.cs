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
        public UnitKind actorKind;
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
            var stable = new EnemyPlan();
            for (var i = 0; i < plan.intents.Count; i++)
            {
                var it = plan.intents[i];
                if (_board.UnitsById.TryGetValue(it.actorId, out var actor) && actor != null)
                {
                    stable.intents.Add(it);
                    if (it.to.row != it.from.row || it.to.col != it.from.col)
                    {
                        stable.reserved.Add(Key(it.to));
                    }
                }
            }
            return stable;
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
            var nearestPlayer = PickPreferredPlayer(enemy);
            if (nearestPlayer == null)
            {
                return new EnemyIntent
                {
                    actorId = enemy.id,
                    actorKind = enemy.kind,
                    kind = EnemyIntentKind.Wait,
                    from = enemy.pos,
                    to = enemy.pos,
                    attackSquares = ComputeAttackSquares(enemy, enemy.pos, enemy.pos),
                    reason = "no_target"
                };
            }

            var nowAttacks = ComputeAttackSquares(enemy, enemy.pos, nearestPlayer.pos);
            RemoveSelfTile(nowAttacks, enemy.pos);
            var to = ComputeBestReposition(enemy, nearestPlayer.pos, reserved);
            var blocked = to.row == enemy.pos.row && to.col == enemy.pos.col;
            return new EnemyIntent
            {
                actorId = enemy.id,
                actorKind = enemy.kind,
                kind = Contains(nowAttacks, nearestPlayer.pos) ? EnemyIntentKind.Capture : EnemyIntentKind.Move,
                from = enemy.pos,
                to = to,
                blocked = blocked,
                attackSquares = nowAttacks,
                reason = blocked ? "hold_then_attack" : "attack_then_reposition"
            };
        }

        private UnitRuntime PickPreferredPlayer(UnitRuntime enemy)
        {
            var from = enemy.pos;
            var tied = new List<UnitRuntime>();
            var bestCanAttack = false;
            var best = int.MaxValue;
            foreach (var kv in _board.UnitsById)
            {
                var p = kv.Value;
                if (p.faction != Faction.Player) continue;

                var attacks = ComputeAttackSquares(enemy, from, p.pos);
                var canAttack = Contains(attacks, p.pos);
                var d = Dist(from, p.pos);

                if (tied.Count == 0 || IsBetterTarget(canAttack, d, bestCanAttack, best))
                {
                    bestCanAttack = canAttack;
                    best = d;
                    tied.Clear();
                    tied.Add(p);
                }
                else if (canAttack == bestCanAttack && d == best)
                {
                    tied.Add(p);
                }
            }

            if (tied.Count == 0) return null;
            if (tied.Count == 1) return tied[0];
            return tied[Random.Range(0, tied.Count)];
        }

        private static bool IsBetterTarget(bool canAttack, int dist, bool bestCanAttack, int bestDist)
        {
            if (canAttack != bestCanAttack) return canAttack;
            return dist < bestDist;
        }

        private GridPos ComputeBestReposition(UnitRuntime enemy, GridPos target, HashSet<string> reserved)
        {
            var maxMove = ResolveMoveRange(enemy.kind);
            if (maxMove <= 0) return enemy.pos;

            var candidates = new List<GridPos> { enemy.pos };
            var current = enemy.pos;
            for (var step = 0; step < maxMove; step++)
            {
                var next = NextStep(current, target);
                if (!_board.Inside(next) || _board.Occupied(next) || reserved.Contains(Key(next))) break;
                candidates.Add(next);
                current = next;
            }

            var best = candidates[0];
            var bestCanAttack = false;
            var bestDist = int.MaxValue;
            var bestSteps = int.MaxValue;

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                var attacks = ComputeAttackSquares(enemy, candidate, target);
                var canAttack = Contains(attacks, target);
                var dist = Dist(candidate, target);
                var steps = Mathf.Abs(candidate.row - enemy.pos.row) + Mathf.Abs(candidate.col - enemy.pos.col);

                if (!IsBetterReposition(canAttack, dist, steps, bestCanAttack, bestDist, bestSteps)) continue;

                best = candidate;
                bestCanAttack = canAttack;
                bestDist = dist;
                bestSteps = steps;
            }

            return best;
        }

        private static bool IsBetterReposition(
            bool canAttack,
            int dist,
            int steps,
            bool bestCanAttack,
            int bestDist,
            int bestSteps)
        {
            if (canAttack != bestCanAttack) return canAttack;
            if (dist != bestDist) return dist < bestDist;
            return steps < bestSteps;
        }

        private static int ResolveMoveRange(UnitKind kind)
        {
            switch (kind)
            {
                case UnitKind.Coyote: return 2;
                case UnitKind.Bat: return 1;
                default: return 1;
            }
        }

        private static GridPos NextStep(GridPos from, GridPos to)
        {
            var dr = to.row - from.row;
            var dc = to.col - from.col;
            if (Mathf.Abs(dr) >= Mathf.Abs(dc)) return new GridPos(from.row + (dr == 0 ? 0 : dr > 0 ? 1 : -1), from.col);
            return new GridPos(from.row, from.col + (dc == 0 ? 0 : dc > 0 ? 1 : -1));
        }

        private List<GridPos> ComputeAttackSquares(UnitRuntime enemy, GridPos from, GridPos preferredTarget)
        {
            // Shared source-of-truth for preview + execution.
            var outSquares = new List<GridPos>();
            if (enemy.kind == UnitKind.Bat)
            {
                AddLineAttack(from, preferredTarget, outSquares, 1);
                return outSquares;
            }
            if (enemy.kind == UnitKind.Coyote)
            {
                AddFrontConeAttack(from, preferredTarget, outSquares);
                return outSquares;
            }
            if (enemy.kind == UnitKind.WolfAlpha || enemy.kind == UnitKind.WolfPup)
            {
                AddFrontConeAttack(from, preferredTarget, outSquares);
                return outSquares;
            }
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
            if (enemy.kind == UnitKind.Owl)
            {
                AddLineAttackToBoardEdge(from, preferredTarget, outSquares);
                return outSquares;
            }
            outSquares.Add(new GridPos(from.row + 1, from.col));
            outSquares.Add(new GridPos(from.row - 1, from.col));
            outSquares.Add(new GridPos(from.row, from.col + 1));
            outSquares.Add(new GridPos(from.row, from.col - 1));
            return outSquares;
        }

        private void AddLineAttackToBoardEdge(GridPos from, GridPos preferredTarget, List<GridPos> outSquares)
        {
            var step = DirectionStep(from, preferredTarget);
            if (step.row == 0 && step.col == 0) step = new GridPos(-1, 0);

            var probe = new GridPos(from.row + step.row, from.col + step.col);
            while (_board.Inside(probe))
            {
                outSquares.Add(probe);
                probe = new GridPos(probe.row + step.row, probe.col + step.col);
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

        private static void AddFrontConeAttack(GridPos from, GridPos preferredTarget, List<GridPos> outSquares)
        {
            var step = DirectionStep(from, preferredTarget);
            if (step.row == 0 && step.col == 0) step = new GridPos(-1, 0);

            // Three adjacent "front" squares, oriented toward preferred target.
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
