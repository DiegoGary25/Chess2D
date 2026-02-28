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
        public string targetId;
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

    public sealed partial class EnemyIntentSystem
    {
        private readonly BoardState _board;

        public EnemyIntentSystem(BoardState board) { _board = board; }

        private static string Key(GridPos p) => $"{p.row}:{p.col}";
        private static int Dist(GridPos a, GridPos b) => Mathf.Abs(a.row - b.row) + Mathf.Abs(a.col - b.col);

        public EnemyPlan BuildPlan()
        {
            var plan = new EnemyPlan();
            var targetCommitCounts = new Dictionary<string, int>();
            var squareCommitCounts = new Dictionary<string, int>();
            var playerPressureSquares = ComputeLikelyPlayerMoveSquares();
            var enemies = GetEnemiesInStableOrder();

            for (var i = 0; i < enemies.Count; i++)
            {
                var enemy = enemies[i];
                var intent = BuildIntent(enemy, plan.reserved, targetCommitCounts, squareCommitCounts, playerPressureSquares);
                if (intent == null) continue;
                plan.intents.Add(intent);

                if (intent.to.row != intent.from.row || intent.to.col != intent.from.col)
                {
                    plan.reserved.Add(Key(intent.to));
                }

                if (!string.IsNullOrEmpty(intent.targetId))
                {
                    targetCommitCounts[intent.targetId] = targetCommitCounts.TryGetValue(intent.targetId, out var targetCount)
                        ? targetCount + 1
                        : 1;
                }

                for (var s = 0; s < intent.attackSquares.Count; s++)
                {
                    var key = Key(intent.attackSquares[s]);
                    squareCommitCounts[key] = squareCommitCounts.TryGetValue(key, out var squareCount)
                        ? squareCount + 1
                        : 1;
                }
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

                if (it.to.row != actor.pos.row || it.to.col != actor.pos.col)
                {
                    if (!_board.Move(actor.id, it.to))
                    {
                        it.blocked = true;
                        it.to = actor.pos;
                    }
                }

                if (it.kind != EnemyIntentKind.Capture && it.kind != EnemyIntentKind.Web) continue;
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

        private EnemyIntent BuildIntent(
            UnitRuntime enemy,
            HashSet<string> reserved,
            Dictionary<string, int> targetCommitCounts,
            Dictionary<string, int> squareCommitCounts,
            HashSet<string> playerPressureSquares)
        {
            var immediateAttackTarget = PickImmediateAttackTarget(enemy, targetCommitCounts, squareCommitCounts, playerPressureSquares);
            var repositionTarget = PickPreferredRepositionTarget(enemy, reserved, targetCommitCounts, squareCommitCounts, playerPressureSquares);

            var attackSquares = immediateAttackTarget != null
                ? ComputeAttackSquares(enemy, enemy.pos, immediateAttackTarget.pos)
                : new List<GridPos>();
            RemoveSelfTile(attackSquares, enemy.pos);

            var moveTarget = repositionTarget ?? immediateAttackTarget;
            var destination = moveTarget != null
                ? ComputeBestReposition(enemy, moveTarget.pos, reserved, playerPressureSquares)
                : enemy.pos;

            var didAttack = immediateAttackTarget != null && Contains(attackSquares, immediateAttackTarget.pos);
            return new EnemyIntent
            {
                actorId = enemy.id,
                targetId = didAttack ? immediateAttackTarget.id : moveTarget != null ? moveTarget.id : null,
                actorKind = enemy.kind,
                kind = didAttack
                    ? EnemyIntentKind.Capture
                    : destination.row != enemy.pos.row || destination.col != enemy.pos.col
                        ? EnemyIntentKind.Move
                        : EnemyIntentKind.Wait,
                from = enemy.pos,
                to = destination,
                attackSquares = attackSquares,
                blocked = destination.row == enemy.pos.row && destination.col == enemy.pos.col,
                reason = didAttack ? "attack" : destination.row == enemy.pos.row && destination.col == enemy.pos.col ? "hold" : "reposition"
            };
        }

        private UnitRuntime PickImmediateAttackTarget(
            UnitRuntime enemy,
            Dictionary<string, int> targetCommitCounts,
            Dictionary<string, int> squareCommitCounts,
            HashSet<string> playerPressureSquares)
        {
            UnitRuntime bestTarget = null;
            var bestScore = int.MinValue;

            foreach (var kv in _board.UnitsById)
            {
                var player = kv.Value;
                if (player == null || player.faction != Faction.Player) continue;

                var attacks = ComputeAttackSquares(enemy, enemy.pos, player.pos);
                if (!Contains(attacks, player.pos)) continue;

                var score = EvaluateAttackScore(enemy, player, attacks, targetCommitCounts, squareCommitCounts, playerPressureSquares);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestTarget = player;
                }
            }

            return bestTarget;
        }

        private UnitRuntime PickPreferredRepositionTarget(
            UnitRuntime enemy,
            HashSet<string> reserved,
            Dictionary<string, int> targetCommitCounts,
            Dictionary<string, int> squareCommitCounts,
            HashSet<string> playerPressureSquares)
        {
            UnitRuntime bestTarget = null;
            var bestScore = int.MinValue;

            foreach (var kv in _board.UnitsById)
            {
                var player = kv.Value;
                if (player == null || player.faction != Faction.Player) continue;

                var score = EvaluateRepositionTargetScore(enemy, player, reserved, targetCommitCounts, squareCommitCounts, playerPressureSquares);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestTarget = player;
                }
            }

            return bestTarget;
        }

        private int EvaluateAttackScore(
            UnitRuntime enemy,
            UnitRuntime player,
            List<GridPos> attacks,
            Dictionary<string, int> targetCommitCounts,
            Dictionary<string, int> squareCommitCounts,
            HashSet<string> playerPressureSquares)
        {
            var score = 250;
            score += player.kind == UnitKind.King ? 10000 : 0;
            score += player.hp <= ResolveExpectedDamage(enemy, enemy.pos, player.pos) ? 400 : 0;
            score -= Dist(enemy.pos, player.pos) * 10;

            if (targetCommitCounts.TryGetValue(player.id, out var targetCount))
            {
                score -= player.kind == UnitKind.King ? targetCount * 20 : targetCount * 120;
            }

            if (squareCommitCounts.TryGetValue(Key(player.pos), out var squareCount))
            {
                score -= player.kind == UnitKind.King ? squareCount * 10 : squareCount * 70;
            }

            score += CountPressureCoverage(attacks, playerPressureSquares) * 15;
            return score;
        }

        private int EvaluateRepositionTargetScore(
            UnitRuntime enemy,
            UnitRuntime player,
            HashSet<string> reserved,
            Dictionary<string, int> targetCommitCounts,
            Dictionary<string, int> squareCommitCounts,
            HashSet<string> playerPressureSquares)
        {
            var candidates = GenerateMoveCandidates(enemy, player.pos, reserved);
            var bestScore = int.MinValue;

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                var attacks = ComputeAttackSquares(enemy, candidate, player.pos);
                var score = player.kind == UnitKind.King ? 3000 : 0;
                score += Contains(attacks, player.pos) ? 600 : 0;
                score -= Dist(candidate, player.pos) * 12;
                score -= Dist(enemy.pos, candidate) * 3;
                score += CountPressureCoverage(attacks, playerPressureSquares) * 12;

                if (targetCommitCounts.TryGetValue(player.id, out var targetCount))
                {
                    score -= player.kind == UnitKind.King ? targetCount * 10 : targetCount * 50;
                }

                if (Contains(attacks, player.pos) && squareCommitCounts.TryGetValue(Key(player.pos), out var squareCount))
                {
                    score -= player.kind == UnitKind.King ? squareCount * 5 : squareCount * 35;
                }

                if (score > bestScore) bestScore = score;
            }

            return bestScore;
        }

        private GridPos ComputeBestReposition(UnitRuntime enemy, GridPos target, HashSet<string> reserved, HashSet<string> playerPressureSquares)
        {
            var candidates = GenerateMoveCandidates(enemy, target, reserved);
            var best = enemy.pos;
            var bestScore = int.MinValue;

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                var attacks = ComputeAttackSquares(enemy, candidate, target);
                var score = Contains(attacks, target) ? 400 : 0;
                score -= Dist(candidate, target) * 10;
                score -= Dist(enemy.pos, candidate) * 2;
                score += CountPressureCoverage(attacks, playerPressureSquares) * 12;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best;
        }

        private List<GridPos> GenerateMoveCandidates(UnitRuntime enemy, GridPos target, HashSet<string> reserved)
        {
            var candidates = new List<GridPos> { enemy.pos };

            void TryAdd(GridPos p)
            {
                if (!_board.Inside(p)) return;
                if (_board.Occupied(p)) return;
                if (reserved != null && reserved.Contains(Key(p))) return;
                if (Contains(candidates, p)) return;
                candidates.Add(p);
            }

            switch (enemy.kind)
            {
                case UnitKind.Pawn:
                case UnitKind.King:
                    AddAdjacentAllMoves(enemy.pos, TryAdd);
                    break;
                case UnitKind.Knight:
                    AddKnightMoves(enemy.pos, TryAdd);
                    break;
                case UnitKind.Bishop:
                    AddRayMoves(enemy.pos, candidates, true, false, reserved);
                    break;
                case UnitKind.Queen:
                    AddRayMoves(enemy.pos, candidates, true, true, reserved);
                    break;
                case UnitKind.Rook:
                    AddRayMoves(enemy.pos, candidates, false, true, reserved);
                    break;
                default:
                    AddLegacyStepMoves(enemy, target, candidates, reserved);
                    break;
            }

            return candidates;
        }

        private void AddLegacyStepMoves(UnitRuntime enemy, GridPos target, List<GridPos> candidates, HashSet<string> reserved)
        {
            var maxMove = ResolveLegacyMoveRange(enemy.kind);
            if (maxMove <= 0) return;

            var current = enemy.pos;
            for (var step = 0; step < maxMove; step++)
            {
                var next = NextStep(current, target);
                if (!_board.Inside(next) || _board.Occupied(next) || (reserved != null && reserved.Contains(Key(next)))) break;
                if (!Contains(candidates, next)) candidates.Add(next);
                current = next;
            }
        }

        private List<GridPos> ComputeAttackSquares(UnitRuntime enemy, GridPos from, GridPos preferredTarget)
        {
            var outSquares = new List<GridPos>();
            switch (enemy.kind)
            {
                case UnitKind.King:
                case UnitKind.Pawn:
                    AddSingleTargetOrAdjacentAttack(from, preferredTarget, outSquares);
                    return outSquares;
                case UnitKind.Knight:
                    AddKnightAttackSquares(enemy, from, preferredTarget, outSquares);
                    return outSquares;
                case UnitKind.Bishop:
                    AddRayAttackTowardTarget(from, preferredTarget, outSquares, true, false);
                    return outSquares;
                case UnitKind.Queen:
                    AddRayAttackTowardTarget(from, preferredTarget, outSquares, true, true);
                    return outSquares;
                case UnitKind.Rook:
                    AddRayAttackTowardTarget(from, preferredTarget, outSquares, false, true);
                    return outSquares;
                case UnitKind.Bat:
                    AddLineAttack(from, preferredTarget, outSquares, 1);
                    return outSquares;
                case UnitKind.Coyote:
                case UnitKind.WolfAlpha:
                case UnitKind.WolfPup:
                    AddFrontConeAttack(from, preferredTarget, outSquares);
                    return outSquares;
                case UnitKind.Skunk:
                    outSquares.Add(new GridPos(from.row + 1, from.col));
                    outSquares.Add(new GridPos(from.row + 1, from.col + 1));
                    outSquares.Add(new GridPos(from.row, from.col + 1));
                    outSquares.Add(new GridPos(from.row + 1, from.col - 1));
                    return outSquares;
                case UnitKind.Spider:
                    outSquares.Add(new GridPos(from.row + 1, from.col));
                    outSquares.Add(new GridPos(from.row - 1, from.col));
                    return outSquares;
                case UnitKind.Owl:
                    AddLineAttackToBoardEdge(from, preferredTarget, outSquares);
                    return outSquares;
                default:
                    outSquares.Add(new GridPos(from.row + 1, from.col));
                    outSquares.Add(new GridPos(from.row - 1, from.col));
                    outSquares.Add(new GridPos(from.row, from.col + 1));
                    outSquares.Add(new GridPos(from.row, from.col - 1));
                    return outSquares;
            }
        }

        private void AddSingleTargetOrAdjacentAttack(GridPos from, GridPos preferredTarget, List<GridPos> outSquares)
        {
            if (IsAdjacentAll(from, preferredTarget))
            {
                outSquares.Add(preferredTarget);
                return;
            }
            AddAdjacentAttackSquares(from, outSquares);
        }

        private void AddKnightAttackSquares(UnitRuntime enemy, GridPos from, GridPos preferredTarget, List<GridPos> outSquares)
        {
            if (IsKnightLeap(from, preferredTarget))
            {
                var target = _board.At(preferredTarget);
                if (target != null && target.faction != enemy.faction && target.faction != Faction.Neutral)
                {
                    outSquares.Add(preferredTarget);
                    return;
                }
            }

            if (IsAdjacentAll(from, preferredTarget))
            {
                outSquares.Add(preferredTarget);
                return;
            }

            AddAdjacentAttackSquares(from, outSquares);
            AddKnightCaptureTargets(from, enemy.faction, outSquares);
        }

        private void AddKnightCaptureTargets(GridPos from, Faction attackerFaction, List<GridPos> outSquares)
        {
            void TryAdd(GridPos p)
            {
                if (!_board.Inside(p)) return;
                var target = _board.At(p);
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

        private void AddLineAttackToBoardEdge(GridPos from, GridPos preferredTarget, List<GridPos> outSquares)
        {
            var step = DirectionStep(from, preferredTarget);
            if (step.row == 0 && step.col == 0) step = new GridPos(-1, 0);

            var probe = new GridPos(from.row + step.row, from.col + step.col);
            while (_board.Inside(probe))
            {
                outSquares.Add(probe);
                if (_board.Occupied(probe)) break;
                probe = new GridPos(probe.row + step.row, probe.col + step.col);
            }
        }

        private void AddRayAttackTowardTarget(GridPos from, GridPos preferredTarget, List<GridPos> outSquares, bool diag, bool ortho)
        {
            var step = DirectionStepExtended(from, preferredTarget, diag, ortho);
            if (step.row == 0 && step.col == 0)
            {
                AddRayAttackFallback(from, outSquares, diag, ortho);
                return;
            }

            var probe = new GridPos(from.row + step.row, from.col + step.col);
            while (_board.Inside(probe))
            {
                outSquares.Add(probe);
                if (_board.Occupied(probe)) break;
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

        private void AddRayMoves(GridPos from, List<GridPos> outTiles, bool diag, bool ortho, HashSet<string> reserved)
        {
            var dirs = new List<GridPos>();
            if (ortho)
            {
                dirs.Add(new GridPos(1, 0)); dirs.Add(new GridPos(-1, 0));
                dirs.Add(new GridPos(0, 1)); dirs.Add(new GridPos(0, -1));
            }
            if (diag)
            {
                dirs.Add(new GridPos(1, 1)); dirs.Add(new GridPos(1, -1));
                dirs.Add(new GridPos(-1, 1)); dirs.Add(new GridPos(-1, -1));
            }

            for (var i = 0; i < dirs.Count; i++)
            {
                var d = dirs[i];
                var r = from.row + d.row;
                var c = from.col + d.col;
                while (_board.Inside(new GridPos(r, c)))
                {
                    var p = new GridPos(r, c);
                    if (_board.Occupied(p)) break;
                    if (reserved == null || !reserved.Contains(Key(p)))
                    {
                        if (!Contains(outTiles, p)) outTiles.Add(p);
                    }
                    r += d.row;
                    c += d.col;
                }
            }
        }

        private HashSet<string> ComputeLikelyPlayerMoveSquares()
        {
            var result = new HashSet<string>();
            foreach (var kv in _board.UnitsById)
            {
                var unit = kv.Value;
                if (unit == null || unit.faction != Faction.Player) continue;
                if (!unit.canMove || (unit.status != null && (unit.status.IsSleeping || unit.status.IsRooted))) continue;
                var moves = ComputePlayerMoveTiles(unit);
                for (var i = 0; i < moves.Count; i++) result.Add(Key(moves[i]));
            }
            return result;
        }

        private List<GridPos> ComputePlayerMoveTiles(UnitRuntime piece)
        {
            var outTiles = new List<GridPos>();
            void TryAdd(GridPos p)
            {
                if (_board.Inside(p) && !_board.Occupied(p)) outTiles.Add(p);
            }

            switch (piece.kind)
            {
                case UnitKind.Pawn:
                    if (piece.status != null && piece.status.pawnPromoted) AddAdjacentAllMoves(piece.pos, TryAdd);
                    else TryAdd(new GridPos(piece.pos.row - 1, piece.pos.col));
                    break;
                case UnitKind.Knight:
                    AddKnightMoves(piece.pos, TryAdd);
                    break;
                case UnitKind.Bishop:
                    AddRayMoves(piece.pos, outTiles, true, false, null);
                    break;
                case UnitKind.Rook:
                    AddRayMoves(piece.pos, outTiles, false, true, null);
                    break;
                case UnitKind.Queen:
                    AddRayMoves(piece.pos, outTiles, true, true, null);
                    break;
                case UnitKind.King:
                    AddAdjacentAllMoves(piece.pos, TryAdd);
                    break;
                default:
                    AddAdjacentAllMoves(piece.pos, TryAdd);
                    break;
            }

            return outTiles;
        }

        private List<UnitRuntime> GetEnemiesInStableOrder()
        {
            var list = new List<UnitRuntime>();
            foreach (var kv in _board.UnitsById)
            {
                var unit = kv.Value;
                if (unit == null || unit.faction != Faction.Enemy) continue;
                if (unit.status != null && unit.status.IsSleeping) continue;
                list.Add(unit);
            }
            list.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            return list;
        }

        private static int ResolveExpectedDamage(UnitRuntime enemy, GridPos from, GridPos target)
        {
            if (enemy == null) return 0;
            if (enemy.kind == UnitKind.Knight && IsKnightLeap(from, target)) return Mathf.Max(2, enemy.attack);
            return Mathf.Max(1, enemy.attack);
        }

        private static int CountPressureCoverage(List<GridPos> attackSquares, HashSet<string> playerPressureSquares)
        {
            if (attackSquares == null || playerPressureSquares == null || playerPressureSquares.Count == 0) return 0;
            var total = 0;
            for (var i = 0; i < attackSquares.Count; i++)
            {
                if (playerPressureSquares.Contains(Key(attackSquares[i]))) total += 1;
            }
            return total;
        }

        private static int ResolveLegacyMoveRange(UnitKind kind)
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

        private static void AddAdjacentAllMoves(GridPos from, System.Action<GridPos> add)
        {
            add(new GridPos(from.row + 1, from.col));
            add(new GridPos(from.row - 1, from.col));
            add(new GridPos(from.row, from.col + 1));
            add(new GridPos(from.row, from.col - 1));
            add(new GridPos(from.row + 1, from.col + 1));
            add(new GridPos(from.row + 1, from.col - 1));
            add(new GridPos(from.row - 1, from.col + 1));
            add(new GridPos(from.row - 1, from.col - 1));
        }

        private static void AddKnightMoves(GridPos from, System.Action<GridPos> add)
        {
            add(new GridPos(from.row + 2, from.col + 1));
            add(new GridPos(from.row + 2, from.col - 1));
            add(new GridPos(from.row - 2, from.col + 1));
            add(new GridPos(from.row - 2, from.col - 1));
            add(new GridPos(from.row + 1, from.col + 2));
            add(new GridPos(from.row + 1, from.col - 2));
            add(new GridPos(from.row - 1, from.col + 2));
            add(new GridPos(from.row - 1, from.col - 2));
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
