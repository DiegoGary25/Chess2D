using System.Collections.Generic;
using ChessPrototype.Unity.Board;
using ChessPrototype.Unity.Data;
using ChessPrototype.Unity.Units;
using UnityEngine;

namespace ChessPrototype.Unity.Enemies
{
    public enum EnemyIntentKind { Move, Capture, Web, Spawn, Special, Wait }

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
        private int _plannerDepth = 1;
        private bool _chessOnlyMode;

        public EnemyIntentSystem(BoardState board) { _board = board; }

        public void SetPlannerDepth(int depth)
        {
            _plannerDepth = Mathf.Clamp(depth, 1, MaxPlannerDepth);
        }

        public void SetChessOnlyMode(bool enabled)
        {
            _chessOnlyMode = enabled;
        }

        private static string Key(GridPos p) => $"{p.row}:{p.col}";
        private static int Dist(GridPos a, GridPos b) => Mathf.Abs(a.row - b.row) + Mathf.Abs(a.col - b.col);
        private const int PriorityKing = 12000;
        private const int PriorityBeacon = 8000;
        private const int PriorityPlayerUnit = 2000;
        private const int PriorityRock = 10;
        private const int BonusLethal = 900;
        private const int BonusCanHitNow = 700;
        private const int BonusCanHitAfterReposition = 520;
        private const int BonusLineSetup = 260;
        private const int BonusClearLineSetup = 160;
        private const int BonusFocusTargetThreat = 220;
        private const int BonusStatusBase = 260;
        private const int BonusStatusNearbyEnemy = 140;
        private const int BonusStatusRangedSupport = 180;
        private const int MaxPlannerDepth = 4;
        private const int LookaheadBranchFactor = 6;
        private const float LookaheadDiscount = 0.55f;
        private const int MinimaxBranchLimit = 16;
        private const int MinimaxSearchDepthLimit = 3;

        private struct SimAction
        {
            public string actorId;
            public GridPos to;
            public string targetId;
            public bool isAttack;
            public int orderingScore;
        }

        private struct DamageUndo
        {
            public UnitRuntime unit;
            public int hpBefore;
            public int shieldBefore;
            public GridPos posBefore;
            public bool existedBefore;
        }

        private struct SimUndo
        {
            public bool moved;
            public string actorId;
            public GridPos from;
            public List<DamageUndo> damageUndos;
        }
        private static bool IsEnemyTargetCandidate(UnitRuntime unit)
        {
            if (unit == null) return false;
            if (unit.faction == Faction.Player) return true;
            return unit.faction == Faction.Neutral &&
                   (unit.kind == UnitKind.Rock || unit.kind == UnitKind.BeaconTower);
        }

        private static bool IsChessPiece(UnitKind kind)
        {
            return kind == UnitKind.King ||
                   kind == UnitKind.Pawn ||
                   kind == UnitKind.Knight ||
                   kind == UnitKind.Bishop ||
                   kind == UnitKind.Rook ||
                   kind == UnitKind.Queen;
        }

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

        public List<GridPos> BuildImmediateAttackSquares(UnitRuntime enemy)
        {
            var outSquares = new List<GridPos>();
            if (enemy == null) return outSquares;

            var players = new List<UnitRuntime>();
            foreach (var kv in _board.UnitsById)
            {
                var u = kv.Value;
                if (!IsEnemyTargetCandidate(u)) continue;
                players.Add(u);
            }

            players.Sort((a, b) =>
            {
                var da = Dist(enemy.pos, a.pos);
                var db = Dist(enemy.pos, b.pos);
                if (da != db) return da.CompareTo(db);
                return string.CompareOrdinal(a.id, b.id);
            });

            for (var i = 0; i < players.Count; i++)
            {
                var target = players[i];
                var attacks = ComputeAttackSquares(enemy, enemy.pos, target.pos);
                RemoveSelfTile(attacks, enemy.pos);
                if (!Contains(attacks, target.pos)) continue;
                outSquares = attacks;
                break;
            }

            return outSquares;
        }

        public List<GridPos> BuildAttackPatternTowardNearest(UnitRuntime enemy)
        {
            var outSquares = new List<GridPos>();
            if (enemy == null) return outSquares;

            UnitRuntime nearest = null;
            var best = int.MaxValue;
            foreach (var kv in _board.UnitsById)
            {
                var u = kv.Value;
                if (!IsEnemyTargetCandidate(u)) continue;
                var d = Dist(enemy.pos, u.pos);
                if (d >= best) continue;
                best = d;
                nearest = u;
            }

            var preferredTarget = nearest != null ? nearest.pos : enemy.pos;
            outSquares = ComputeAttackSquares(enemy, enemy.pos, preferredTarget);
            RemoveSelfTile(outSquares, enemy.pos);
            return outSquares;
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
                    if (target == null || target.faction == Faction.Enemy) continue;
                    if (target.faction == Faction.Neutral && target.kind != UnitKind.Rock) continue;
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
            var tactical = ChooseBestActionForEnemy(enemy, reserved);
            var destination = tactical.to;
            var attackSquares = new List<GridPos>();
            EnemyIntentKind kind;
            string reason;
            string targetId = null;

            if (tactical.isAttack && !string.IsNullOrEmpty(tactical.targetId) &&
                _board.UnitsById.TryGetValue(tactical.targetId, out var attackTarget))
            {
                attackSquares = ComputeAttackSquares(enemy, enemy.pos, attackTarget.pos);
                RemoveSelfTile(attackSquares, enemy.pos);
                kind = EnemyIntentKind.Capture;
                reason = "minimax_attack";
                targetId = attackTarget.id;
            }
            else
            {
                var strategicTarget = PickStrategicTarget(enemy);
                var focus = strategicTarget != null ? strategicTarget.pos : enemy.pos;
                attackSquares = ComputeAttackSquares(enemy, destination, focus);
                RemoveSelfTile(attackSquares, destination);
                kind = destination.row != enemy.pos.row || destination.col != enemy.pos.col
                    ? EnemyIntentKind.Move
                    : EnemyIntentKind.Wait;
                reason = kind == EnemyIntentKind.Move ? "minimax_reposition" : "minimax_hold";
                targetId = strategicTarget != null ? strategicTarget.id : null;
            }

            return new EnemyIntent
            {
                actorId = enemy.id,
                targetId = targetId,
                actorKind = enemy.kind,
                kind = kind,
                from = enemy.pos,
                to = destination,
                attackSquares = attackSquares,
                blocked = destination.row == enemy.pos.row && destination.col == enemy.pos.col,
                reason = reason
            };
        }

        private SimAction ChooseBestActionForEnemy(UnitRuntime enemy, HashSet<string> reserved)
        {
            var fallback = new SimAction
            {
                actorId = enemy != null ? enemy.id : null,
                to = enemy != null ? enemy.pos : new GridPos(0, 0),
                targetId = null,
                isAttack = false,
                orderingScore = int.MinValue
            };
            if (enemy == null) return fallback;

            var actions = GenerateActionsForSide(Faction.Enemy, reserved, enemy.id);
            if (actions.Count == 0) return fallback;
            var depth = Mathf.Clamp(_plannerDepth, 1, MinimaxSearchDepthLimit);
            var bestScore = int.MinValue;
            var best = fallback;
            for (var i = 0; i < actions.Count; i++)
            {
                var action = actions[i];
                if (action.actorId != enemy.id) continue;
                if (!TryApplySimAction(action, out var undo)) continue;
                var score = Minimax(depth - 1, false, int.MinValue / 4, int.MaxValue / 4, null);
                UndoSimAction(undo);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = action;
                }
            }

            return best;
        }

        private int Minimax(int depth, bool enemyTurn, int alpha, int beta, HashSet<string> reserved)
        {
            if (depth <= 0) return EvaluateBoardStateForEnemy();
            var side = enemyTurn ? Faction.Enemy : Faction.Player;
            var actions = GenerateActionsForSide(side, reserved, null);
            if (actions.Count == 0) return EvaluateBoardStateForEnemy();

            if (enemyTurn)
            {
                var best = int.MinValue;
                for (var i = 0; i < actions.Count; i++)
                {
                    if (!TryApplySimAction(actions[i], out var undo)) continue;
                    var score = Minimax(depth - 1, false, alpha, beta, null);
                    UndoSimAction(undo);
                    if (score > best) best = score;
                    if (best > alpha) alpha = best;
                    if (beta <= alpha) break;
                }
                return best;
            }

            var worst = int.MaxValue;
            for (var i = 0; i < actions.Count; i++)
            {
                if (!TryApplySimAction(actions[i], out var undo)) continue;
                var score = Minimax(depth - 1, true, alpha, beta, null);
                UndoSimAction(undo);
                if (score < worst) worst = score;
                if (worst < beta) beta = worst;
                if (beta <= alpha) break;
            }
            return worst;
        }

        private List<SimAction> GenerateActionsForSide(Faction side, HashSet<string> reserved, string onlyActorId)
        {
            var actions = new List<SimAction>();
            foreach (var kv in _board.UnitsById)
            {
                var actor = kv.Value;
                if (actor == null || actor.faction != side) continue;
                if (_chessOnlyMode && !IsChessPiece(actor.kind)) continue;
                if (!string.IsNullOrEmpty(onlyActorId) && actor.id != onlyActorId) continue;
                if (actor.status != null && actor.status.IsSleeping) continue;

                var targets = GetAttackableTargetsForFaction(side);
                for (var t = 0; t < targets.Count; t++)
                {
                    var target = targets[t];
                    if (target == null) continue;
                    var attacks = ComputeAttackSquares(actor, actor.pos, target.pos);
                    RemoveSelfTile(attacks, actor.pos);
                    if (!Contains(attacks, target.pos)) continue;
                    actions.Add(new SimAction
                    {
                        actorId = actor.id,
                        to = actor.pos,
                        targetId = target.id,
                        isAttack = true,
                        orderingScore = QuickActionOrderScore(actor, target, true)
                    });
                }

                var moveCandidates = side == Faction.Enemy
                    ? GenerateEnemyMoveCandidatesForSearch(actor, reserved)
                    : GeneratePlayerMoveCandidatesForSearch(actor, reserved);
                for (var m = 0; m < moveCandidates.Count; m++)
                {
                    var to = moveCandidates[m];
                    if (to.row == actor.pos.row && to.col == actor.pos.col) continue;
                    actions.Add(new SimAction
                    {
                        actorId = actor.id,
                        to = to,
                        targetId = null,
                        isAttack = false,
                        orderingScore = QuickMoveOrderScore(actor, to, side)
                    });
                }
            }

            actions.Sort((a, b) => b.orderingScore.CompareTo(a.orderingScore));
            if (actions.Count > MinimaxBranchLimit) actions.RemoveRange(MinimaxBranchLimit, actions.Count - MinimaxBranchLimit);
            return actions;
        }

        private List<UnitRuntime> GetAttackableTargetsForFaction(Faction attacker, bool includeBarricades = true)
        {
            var targets = new List<UnitRuntime>();
            foreach (var kv in _board.UnitsById)
            {
                var u = kv.Value;
                if (u == null) continue;
                if (attacker == Faction.Enemy)
                {
                    if (!includeBarricades && u.faction == Faction.Neutral && u.kind == UnitKind.Rock) continue;
                    if (u.faction == Faction.Player || (u.faction == Faction.Neutral && (u.kind == UnitKind.Rock || u.kind == UnitKind.BeaconTower)))
                        targets.Add(u);
                }
                else
                {
                    if (!includeBarricades && u.faction == Faction.Neutral && u.kind == UnitKind.Rock) continue;
                    if (u.faction == Faction.Enemy || (u.faction == Faction.Neutral && (u.kind == UnitKind.Rock || u.kind == UnitKind.BeaconTower)))
                        targets.Add(u);
                }
            }
            return targets;
        }

        private List<GridPos> GenerateEnemyMoveCandidatesForSearch(UnitRuntime enemy, HashSet<string> reserved)
        {
            var outTiles = new List<GridPos> { enemy.pos };
            var targets = GetAttackableTargetsForFaction(Faction.Enemy);
            if (targets.Count == 0)
            {
                var fallback = GenerateMoveCandidates(enemy, enemy.pos, reserved);
                for (var i = 0; i < fallback.Count; i++) if (!Contains(outTiles, fallback[i])) outTiles.Add(fallback[i]);
                return outTiles;
            }

            for (var i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                if (target == null) continue;
                var candidates = GenerateMoveCandidates(enemy, target.pos, reserved);
                for (var c = 0; c < candidates.Count; c++)
                {
                    if (!Contains(outTiles, candidates[c])) outTiles.Add(candidates[c]);
                }
            }
            return outTiles;
        }

        private List<GridPos> GeneratePlayerMoveCandidatesForSearch(UnitRuntime player, HashSet<string> reserved)
        {
            var baseMoves = ComputePlayerMoveTiles(player);
            var outTiles = new List<GridPos> { player.pos };
            for (var i = 0; i < baseMoves.Count; i++)
            {
                var p = baseMoves[i];
                if (reserved != null && reserved.Contains(Key(p))) continue;
                if (!Contains(outTiles, p)) outTiles.Add(p);
            }
            return outTiles;
        }

        private int QuickActionOrderScore(UnitRuntime actor, UnitRuntime target, bool isAttack)
        {
            var score = ResolveTargetPriority(target);
            if (target != null && target.kind == UnitKind.Rock) score -= 900;
            if (isAttack)
            {
                score += 500;
                score += target.hp <= Mathf.Max(1, actor.attack) ? BonusLethal : 0;
                if (target != null && target.kind == UnitKind.Rock) score -= 600;
            }
            score -= Dist(actor.pos, target.pos) * 5;
            return score;
        }

        private int QuickMoveOrderScore(UnitRuntime actor, GridPos to, Faction side)
        {
            var score = 0;
            var targets = GetAttackableTargetsForFaction(side, includeBarricades: false);
            if (targets.Count == 0) targets = GetAttackableTargetsForFaction(side, includeBarricades: true);
            var best = int.MaxValue;
            var bestCreatedThreat = 0;
            var bestLineSetup = 0;
            for (var i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                if (target == null) continue;
                var d = Dist(to, target.pos);
                if (d < best) best = d;
                var attacks = ComputeAttackSquares(actor, to, target.pos);
                RemoveSelfTile(attacks, to);
                var setup = EvaluateLineSetupScore(actor, to, target.pos);
                if (target.kind == UnitKind.King) setup += 120;
                if (setup > bestLineSetup) bestLineSetup = setup;
                if (!Contains(attacks, target.pos)) continue;
                var threat = ResolveTargetPriority(target) + 350;
                if (target.hp <= Mathf.Max(1, actor.attack)) threat += BonusLethal;
                if (threat > bestCreatedThreat) bestCreatedThreat = threat;
            }
            if (best != int.MaxValue) score -= best * 5;
            score += bestCreatedThreat;
            score += bestLineSetup;
            return score;
        }

        private bool TryApplySimAction(SimAction action, out SimUndo undo)
        {
            undo = new SimUndo
            {
                moved = false,
                actorId = action.actorId,
                from = new GridPos(0, 0),
                damageUndos = new List<DamageUndo>()
            };
            if (string.IsNullOrEmpty(action.actorId)) return false;
            if (!_board.UnitsById.TryGetValue(action.actorId, out var actor) || actor == null) return false;

            if (action.isAttack)
            {
                if (string.IsNullOrEmpty(action.targetId)) return false;
                if (!_board.UnitsById.TryGetValue(action.targetId, out var target) || target == null) return false;
                var attacks = ComputeAttackSquares(actor, actor.pos, target.pos);
                RemoveSelfTile(attacks, actor.pos);
                if (!Contains(attacks, target.pos)) return false;

                undo.damageUndos.Add(new DamageUndo
                {
                    unit = target,
                    hpBefore = target.hp,
                    shieldBefore = target.status != null ? target.status.shieldCharge : 0,
                    posBefore = target.pos,
                    existedBefore = true
                });
                _board.ApplyDamage(target.id, Mathf.Max(1, actor.attack));
                return true;
            }

            if (action.to.row == actor.pos.row && action.to.col == actor.pos.col) return true;
            if (_board.Occupied(action.to)) return false;
            undo.moved = true;
            undo.from = actor.pos;
            return _board.Move(actor.id, action.to);
        }

        private void UndoSimAction(SimUndo undo)
        {
            if (undo.moved &&
                !string.IsNullOrEmpty(undo.actorId) &&
                _board.UnitsById.TryGetValue(undo.actorId, out var actor) &&
                actor != null &&
                (actor.pos.row != undo.from.row || actor.pos.col != undo.from.col))
            {
                _board.Move(actor.id, undo.from);
            }

            for (var i = undo.damageUndos.Count - 1; i >= 0; i--)
            {
                var d = undo.damageUndos[i];
                if (d.unit == null) continue;
                if (!_board.UnitsById.ContainsKey(d.unit.id))
                {
                    d.unit.pos = d.posBefore;
                    _board.Add(d.unit);
                }
                d.unit.hp = d.hpBefore;
                if (d.unit.status != null) d.unit.status.shieldCharge = d.shieldBefore;
            }
        }

        private int EvaluateBoardStateForEnemy()
        {
            var score = 0;
            foreach (var kv in _board.UnitsById)
            {
                var unit = kv.Value;
                if (unit == null) continue;
                if (_chessOnlyMode && unit.faction != Faction.Neutral && !IsChessPiece(unit.kind)) continue;
                var unitScore = ResolveTargetPriority(unit);
                if (unitScore <= 0) unitScore = 700;
                unitScore += Mathf.Max(0, unit.hp) * 40;
                unitScore += Mathf.Max(0, unit.attack) * 55;
                if (unit.faction == Faction.Enemy) score += unitScore;
                else if (unit.faction == Faction.Player) score -= unitScore;
                else if (unit.kind == UnitKind.BeaconTower) score -= unitScore / 2;
                else if (unit.kind == UnitKind.Rock) score -= 5;
                else score -= unitScore / 4;
            }

            score += EvaluateThreatMapDelta(Faction.Enemy) * 12;
            score -= EvaluateThreatMapDelta(Faction.Player) * 10;
            return score;
        }

        private int EvaluateThreatMapDelta(Faction side)
        {
            var total = 0;
            var targets = GetAttackableTargetsForFaction(side);
            foreach (var kv in _board.UnitsById)
            {
                var actor = kv.Value;
                if (actor == null || actor.faction != side) continue;
                if (_chessOnlyMode && !IsChessPiece(actor.kind)) continue;
                if (actor.status != null && actor.status.IsSleeping) continue;
                for (var i = 0; i < targets.Count; i++)
                {
                    var target = targets[i];
                    if (target == null) continue;
                    var attacks = ComputeAttackSquares(actor, actor.pos, target.pos);
                    if (!Contains(attacks, target.pos)) continue;
                    total += ResolveTargetPriority(target) / 40;
                }
            }
            return total;
        }

        private UnitRuntime PickStrategicTarget(UnitRuntime enemy)
        {
            UnitRuntime best = null;
            var bestScore = int.MinValue;
            var hasNonBarricade = false;
            foreach (var kv in _board.UnitsById)
            {
                var candidate = kv.Value;
                if (!IsEnemyTargetCandidate(candidate)) continue;
                if (candidate.faction == Faction.Player || candidate.kind == UnitKind.BeaconTower)
                {
                    hasNonBarricade = true;
                    break;
                }
            }
            foreach (var kv in _board.UnitsById)
            {
                var target = kv.Value;
                if (!IsEnemyTargetCandidate(target)) continue;
                if (hasNonBarricade && target.kind == UnitKind.Rock) continue;
                var score = ResolveTargetPriority(target) - Dist(enemy.pos, target.pos) * 10;
                if (score <= bestScore) continue;
                bestScore = score;
                best = target;
            }
            return best;
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
                if (!IsEnemyTargetCandidate(player)) continue;

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
                if (!IsEnemyTargetCandidate(player)) continue;

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
            var score = ResolveTargetPriority(player);
            if (player != null && player.kind == UnitKind.Rock) score -= 850;
            score += Contains(attacks, player.pos) ? BonusCanHitNow : 0;
            score += player.hp <= ResolveExpectedDamage(enemy, enemy.pos, player.pos) ? BonusLethal : 0;
            score -= Dist(enemy.pos, player.pos) * 8;
            score += EvaluateStatusApplicationValue(enemy, player);

            if (targetCommitCounts.TryGetValue(player.id, out var targetCount))
            {
                score -= IsCriticalObjective(player) ? targetCount * 25 : targetCount * 110;
            }

            if (squareCommitCounts.TryGetValue(Key(player.pos), out var squareCount))
            {
                score -= IsCriticalObjective(player) ? squareCount * 12 : squareCount * 60;
            }

            score += CountPressureCoverage(attacks, playerPressureSquares) * 12;
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
            var distanceWeight = ResolveRepositionDistanceWeight(enemy.kind);
            var depth = Mathf.Clamp(_plannerDepth, 1, MaxPlannerDepth);

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                var attacks = ComputeAttackSquares(enemy, candidate, player.pos);
                var score = ResolveTargetPriority(player);
                score += EvaluateRepositionCandidateWithLookahead(enemy, candidate, player.pos, playerPressureSquares, distanceWeight, depth, true);
                score -= Dist(enemy.pos, candidate) * 3;
                score += EvaluateStatusApplicationValue(enemy, player);
                if (enemy.kind == UnitKind.Skunk)
                {
                    // Skunk prefers kiting away from player pressure.
                    score += DistToNearestTargetCandidate(candidate) * 25;
                }

                if (targetCommitCounts.TryGetValue(player.id, out var targetCount))
                {
                    score -= IsCriticalObjective(player) ? targetCount * 10 : targetCount * 45;
                }

                if (Contains(attacks, player.pos) && squareCommitCounts.TryGetValue(Key(player.pos), out var squareCount))
                {
                    score -= IsCriticalObjective(player) ? squareCount * 6 : squareCount * 30;
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
            var distanceWeight = ResolveRepositionDistanceWeight(enemy.kind);
            var depth = Mathf.Clamp(_plannerDepth, 1, MaxPlannerDepth);

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                var score = EvaluateRepositionCandidateWithLookahead(enemy, candidate, target, playerPressureSquares, distanceWeight, depth, true);
                score -= Dist(enemy.pos, candidate) * 2;
                if (enemy.kind == UnitKind.Skunk)
                {
                    // Keep distance from pieces so Skunk behaves like a ranged harasser.
                    score += DistToNearestTargetCandidate(candidate) * 30;
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best;
        }

        private int EvaluateRepositionCandidateWithLookahead(
            UnitRuntime enemy,
            GridPos candidate,
            GridPos target,
            HashSet<string> playerPressureSquares,
            int distanceWeight,
            int depthRemaining,
            bool includeDistancePenalty)
        {
            var attacks = ComputeAttackSquares(enemy, candidate, target);
            var score = EvaluateImmediateThreatFromTile(enemy, candidate, target, playerPressureSquares);
            score += EvaluateLineSetupScore(enemy, candidate, target);
            score += CountPressureCoverage(attacks, playerPressureSquares) * 10;
            if (includeDistancePenalty) score -= Dist(candidate, target) * distanceWeight;
            if (depthRemaining <= 1) return score;

            var future = EvaluateFutureRepositionValue(enemy, candidate, target, playerPressureSquares, distanceWeight, depthRemaining - 1);
            if (future > 0) score += Mathf.RoundToInt(future * LookaheadDiscount);
            return score;
        }

        private int EvaluateFutureRepositionValue(
            UnitRuntime enemy,
            GridPos from,
            GridPos target,
            HashSet<string> playerPressureSquares,
            int distanceWeight,
            int depthRemaining)
        {
            if (depthRemaining <= 0 || enemy == null) return 0;
            if (!_board.UnitsById.TryGetValue(enemy.id, out var runtimeEnemy) || runtimeEnemy == null) return 0;

            var original = runtimeEnemy.pos;
            var movedToAnchor = original.row != from.row || original.col != from.col;
            if (movedToAnchor && !_board.Move(runtimeEnemy.id, from)) return 0;

            try
            {
                var futureCandidates = GenerateMoveCandidates(runtimeEnemy, target, null);
                if (futureCandidates.Count == 0) return 0;

                var ranked = new List<(GridPos pos, int score)>(futureCandidates.Count);
                for (var i = 0; i < futureCandidates.Count; i++)
                {
                    var candidate = futureCandidates[i];
                    var quick = EvaluateImmediateThreatFromTile(runtimeEnemy, candidate, target, playerPressureSquares);
                    quick += EvaluateLineSetupScore(runtimeEnemy, candidate, target);
                    quick -= Dist(candidate, target) * distanceWeight;
                    if (runtimeEnemy.kind == UnitKind.Skunk)
                    {
                        quick += DistToNearestTargetCandidate(candidate) * 15;
                    }
                    ranked.Add((candidate, quick));
                }

                ranked.Sort((a, b) => b.score.CompareTo(a.score));
                var consider = Mathf.Min(LookaheadBranchFactor, ranked.Count);
                var best = int.MinValue;
                for (var i = 0; i < consider; i++)
                {
                    var candidate = ranked[i].pos;
                    var score = EvaluateRepositionCandidateWithLookahead(
                        runtimeEnemy,
                        candidate,
                        target,
                        playerPressureSquares,
                        distanceWeight,
                        depthRemaining,
                        false);
                    if (score > best) best = score;
                }

                return best == int.MinValue ? 0 : best;
            }
            finally
            {
                if (movedToAnchor)
                {
                    _board.Move(runtimeEnemy.id, original);
                }
            }
        }

        private int EvaluateImmediateThreatFromTile(UnitRuntime enemy, GridPos from, GridPos focusTarget, HashSet<string> playerPressureSquares)
        {
            var bestScore = 0;

            foreach (var kv in _board.UnitsById)
            {
                var target = kv.Value;
                if (!IsEnemyTargetCandidate(target)) continue;

                var attacks = ComputeAttackSquares(enemy, from, target.pos);
                RemoveSelfTile(attacks, from);
                if (!Contains(attacks, target.pos)) continue;

                var score = ResolveTargetPriority(target);
                score += BonusCanHitAfterReposition;
                score += target.hp <= ResolveExpectedDamage(enemy, from, target.pos) ? BonusLethal : 0;
                score += EvaluateStatusApplicationValue(enemy, target);
                score += CountPressureCoverage(attacks, playerPressureSquares) * 12;
                if (target.pos.row == focusTarget.row && target.pos.col == focusTarget.col)
                {
                    score += BonusFocusTargetThreat;
                }

                if (score > bestScore) bestScore = score;
            }

            return bestScore;
        }

        private int EvaluateLineSetupScore(UnitRuntime enemy, GridPos from, GridPos target)
        {
            if (enemy == null) return 0;

            var dr = Mathf.Abs(target.row - from.row);
            var dc = Mathf.Abs(target.col - from.col);
            var aligned = false;
            var clear = false;

            switch (enemy.kind)
            {
                case UnitKind.Rook:
                    aligned = (dr == 0 && dc > 0) || (dc == 0 && dr > 0);
                    clear = aligned && IsUnblockedStraightLine(from, target);
                    break;
                case UnitKind.Queen:
                    aligned = (dr == dc && dr > 0) || (dr == 0 && dc > 0) || (dc == 0 && dr > 0);
                    clear = aligned && IsUnblockedStraightLine(from, target);
                    break;
            }

            if (!aligned) return 0;
            return clear ? BonusLineSetup + BonusClearLineSetup : BonusLineSetup;
        }

        private bool IsUnblockedStraightLine(GridPos from, GridPos to)
        {
            var dr = to.row - from.row;
            var dc = to.col - from.col;
            var stepRow = dr == 0 ? 0 : (dr > 0 ? 1 : -1);
            var stepCol = dc == 0 ? 0 : (dc > 0 ? 1 : -1);

            if (dr != 0 && dc != 0 && Mathf.Abs(dr) != Mathf.Abs(dc)) return false;
            if (dr == 0 && dc == 0) return false;

            var probe = new GridPos(from.row + stepRow, from.col + stepCol);
            while (probe.row != to.row || probe.col != to.col)
            {
                if (!_board.Inside(probe)) return false;
                if (_board.Occupied(probe)) return false;
                probe = new GridPos(probe.row + stepRow, probe.col + stepCol);
            }

            return _board.Inside(to);
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
                    TryAdd(new GridPos(enemy.pos.row + 1, enemy.pos.col));
                    break;
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
            return PieceAttackRules.ComputeAttackSquares(_board, enemy, from, preferredTarget);
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
            AddKnightCaptureTargets(from, enemy.faction, outSquares);
        }

        private static void AddPawnDiagonalAttack(GridPos from, GridPos preferredTarget, List<GridPos> outSquares)
        {
            var dr = preferredTarget.row - from.row;
            var forward = dr == 0 ? 1 : (dr > 0 ? 1 : -1);
            outSquares.Add(new GridPos(from.row + forward, from.col - 1));
            outSquares.Add(new GridPos(from.row + forward, from.col + 1));
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
                if (_chessOnlyMode && !IsChessPiece(unit.kind)) continue;
                if (unit.status != null && unit.status.IsSleeping) continue;
                list.Add(unit);
            }
            list.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            return list;
        }

        private static int ResolveExpectedDamage(UnitRuntime enemy, GridPos from, GridPos target)
        {
            if (enemy == null) return 0;
            return Mathf.Max(1, enemy.attack);
        }

        private static bool IsCriticalObjective(UnitRuntime unit)
        {
            if (unit == null) return false;
            return unit.kind == UnitKind.King || unit.kind == UnitKind.BeaconTower;
        }

        private static int ResolveTargetPriority(UnitRuntime unit)
        {
            if (unit == null) return 0;
            if (unit.kind == UnitKind.King) return PriorityKing;
            if (unit.kind == UnitKind.BeaconTower) return PriorityBeacon;
            if (unit.faction == Faction.Player) return PriorityPlayerUnit;
            if (unit.kind == UnitKind.Rock) return PriorityRock;
            return 0;
        }

        private int EvaluateStatusApplicationValue(UnitRuntime enemy, UnitRuntime target)
        {
            if (enemy == null || target == null) return 0;
            if (!CanApplyStatusPressure(enemy.kind)) return 0;

            var value = BonusStatusBase;
            value += CountNearbyEnemyAllies(target.pos, enemy.id) * BonusStatusNearbyEnemy;
            value += CountRangedSupportersForTile(target.pos, enemy.id) * BonusStatusRangedSupport;
            return value;
        }

        private static bool CanApplyStatusPressure(UnitKind kind)
        {
            return kind == UnitKind.Bat ||
                   kind == UnitKind.Spider ||
                   kind == UnitKind.Owl ||
                   kind == UnitKind.Skunk;
        }

        private int CountNearbyEnemyAllies(GridPos around, string excludeId)
        {
            var count = 0;
            foreach (var kv in _board.UnitsById)
            {
                var unit = kv.Value;
                if (unit == null || unit.faction != Faction.Enemy) continue;
                if (!string.IsNullOrEmpty(excludeId) && unit.id == excludeId) continue;
                var d = Dist(unit.pos, around);
                if (d <= 1) count += 1;
            }
            return count;
        }

        private int CountRangedSupportersForTile(GridPos targetTile, string excludeId)
        {
            var count = 0;
            foreach (var kv in _board.UnitsById)
            {
                var ally = kv.Value;
                if (ally == null || ally.faction != Faction.Enemy) continue;
                if (!string.IsNullOrEmpty(excludeId) && ally.id == excludeId) continue;
                if (!IsRangedAttacker(ally.kind)) continue;

                var squares = ComputeAttackSquares(ally, ally.pos, targetTile);
                if (Contains(squares, targetTile)) count += 1;
            }
            return count;
        }

        private static bool IsRangedAttacker(UnitKind kind)
        {
            switch (kind)
            {
                case UnitKind.Bishop:
                case UnitKind.Rook:
                case UnitKind.Queen:
                case UnitKind.Bat:
                case UnitKind.Owl:
                case UnitKind.Boar:
                case UnitKind.Toad:
                case UnitKind.Skunk:
                    return true;
                default:
                    return false;
            }
        }

        private int DistToNearestTargetCandidate(GridPos from)
        {
            var best = int.MaxValue;
            foreach (var kv in _board.UnitsById)
            {
                var u = kv.Value;
                if (!IsEnemyTargetCandidate(u)) continue;
                var d = Dist(from, u.pos);
                if (d < best) best = d;
            }

            return best == int.MaxValue ? 0 : best;
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

        private static int ResolveRepositionDistanceWeight(UnitKind kind)
        {
            switch (kind)
            {
                case UnitKind.Bishop:
                case UnitKind.Rook:
                case UnitKind.Queen:
                    return 4;
                default:
                    return 10;
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
