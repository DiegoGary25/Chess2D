using System.Collections.Generic;
using ChessPrototype.Unity.Board;
using ChessPrototype.Unity.Cards;
using ChessPrototype.Unity.Core;
using ChessPrototype.Unity.Data;
using ChessPrototype.Unity.Enemies;
using ChessPrototype.Unity.RunMap;
using ChessPrototype.Unity.UI;
using UnityEngine;
using System.Collections;

namespace ChessPrototype.Unity.Encounters
{
    public sealed class EncounterController : MonoBehaviour, ICardPlaySink
    {
        [SerializeField] private Transform boardRoot;
        [SerializeField] private float tileSize = 0.8f;
        [SerializeField] private BoardUiGenerator boardUiGenerator;
        [Header("Enemy Turn Timing")]
        [SerializeField] private float enemyStepDelaySeconds = 1f;

        private readonly BoardState _board = new BoardState();
        private readonly List<TrapRuntime> _traps = new List<TrapRuntime>();
        private readonly List<CaveRuntime> _caves = new List<CaveRuntime>();
        private readonly List<UnitRuntime> _enemies = new List<UnitRuntime>();
        private readonly Dictionary<UnitKind, PieceDefinition> _pieces = new Dictionary<UnitKind, PieceDefinition>();
        private readonly HashSet<string> _moveHighlights = new HashSet<string>();
        private readonly HashSet<string> _attackHighlights = new HashSet<string>();
        private readonly HashSet<string> _cardTargetHighlights = new HashSet<string>();

        private GameSessionState _session;
        private TurnStateController _turn;
        private CardRuntimeController _cards;
        private EnemyIntentSystem _enemyIntentSystem;
        private EnemyPlan _currentEnemyPlan;
        private CardDefinition _pendingCard;
        private GridPos? _resolvedCardTarget;
        private string _resolvedCardTargetUnitId;
        private int _idCounter;
        private string _activeNodeId;
        private bool _encounterResolved;
        private Coroutine _enemyTurnRoutine;

        public BoardState Board => _board;
        public EnemyPlan CurrentEnemyPlan => _currentEnemyPlan;
        public CardDefinition PendingCard => _pendingCard;
        public UnitRuntime SelectedUnit { get; private set; }

        public event System.Action OnBoardChanged;
        public event System.Action OnIntentsChanged;
        public event System.Action<UnitRuntime> OnSelectionChanged;
        public event System.Action<bool, string> OnEncounterResolved;
        public event System.Action<string> OnEncounterMessage;

        public event System.Action<UnitRuntime, GridPos, GridPos> OnUnitMoved;
        public event System.Action<UnitRuntime, List<GridPos>> OnAttackStarted;
        public event System.Action<UnitRuntime, List<GridPos>> OnAttackResolved;
        public event System.Action<UnitRuntime> OnSpecialStarted;
        public event System.Action<string, int> OnDamageDealt;
        public event System.Action<string> OnTrapTriggered;
        public event System.Action<string, CardKind> OnCardEffectApplied;
        public event System.Action<CardDefinition> OnPendingCardChanged;

        private static string Key(GridPos p) => $"{p.row}:{p.col}";

        public void Configure(GameSessionState session, TurnStateController turn, CardRuntimeController cards)
        {
            _session = session;
            _turn = turn;
            _cards = cards;
            _enemyIntentSystem = new EnemyIntentSystem(_board);
            _pieces.Clear();
            if (_session != null && _session.Config != null)
            {
                foreach (var p in _session.Config.pieceDefinitions) _pieces[p.kind] = p;
            }
            _turn.OnPhaseChanged += HandlePhaseChanged;
        }

        public void StartNode(RuntimeMapNode node)
        {
            _activeNodeId = node.id;
            _encounterResolved = false;
            if (node.type != MapNodeType.Battle && node.type != MapNodeType.Elite && node.type != MapNodeType.Boss)
            {
                OnEncounterMessage?.Invoke($"Resolved {node.type} node.");
                _encounterResolved = true;
                OnEncounterResolved?.Invoke(true, _activeNodeId);
                return;
            }
            BuildEncounter();
        }

        public void BuildEncounter()
        {
            if (_session == null || _turn == null || _cards == null)
            {
                Debug.LogWarning("[ChessPrototype] EncounterController not configured. Run Auto-Link and ensure PrototypeBootstrap initialized.");
                return;
            }

            if (_enemyTurnRoutine != null)
            {
                StopCoroutine(_enemyTurnRoutine);
                _enemyTurnRoutine = null;
            }

            var idx = Mathf.Clamp(_session.EncounterIndex, 0, Mathf.Max(0, (_session.Config?.encounters?.Count ?? 1) - 1));
            var tpl = _session.Config != null && _session.Config.encounters.Count > 0 ? _session.Config.encounters[idx] : null;
            _board.Reset(tpl != null ? tpl.boardSize : 4);
            _traps.Clear();
            _caves.Clear();
            _enemies.Clear();
            _idCounter = 0;
            _moveHighlights.Clear();
            _attackHighlights.Clear();
            _cardTargetHighlights.Clear();
            _pendingCard = null;
            _resolvedCardTarget = null;
            _resolvedCardTargetUnitId = null;
            SelectedUnit = null;
            OnSelectionChanged?.Invoke(null);
            OnPendingCardChanged?.Invoke(null);

            SpawnKing();
            if (tpl != null)
            {
                for (var i = 0; i < tpl.enemyPlacements.Count; i++)
                {
                    var pl = tpl.enemyPlacements[i];
                    var enemy = NewUnit(pl.kind, Faction.Enemy, new GridPos(pl.row, pl.col));
                    if (_board.Add(enemy)) _enemies.Add(enemy);
                }
                for (var c = 0; c < tpl.caves.Count; c++)
                {
                    var caveTpl = tpl.caves[c];
                    var caveUnit = NewUnit(UnitKind.Cave, Faction.Neutral, new GridPos(caveTpl.row, caveTpl.col));
                    caveUnit.isStructure = true;
                    _board.Add(caveUnit);
                    _caves.Add(new CaveRuntime
                    {
                        id = caveTpl.id,
                        row = caveTpl.row,
                        col = caveTpl.col,
                        turnsUntilNextSpawn = caveTpl.turnsUntilNextSpawn,
                        spawnCharges = caveTpl.spawnCharges,
                        maxAliveFromThisCave = caveTpl.maxAliveFromThisCave,
                        spawnPool = new List<SpawnWeight>(caveTpl.spawnPool)
                    });
                }
            }

            _cards.DiscardHand();
            _turn.BeginEncounter();
            ResetPlayerActions();
            _cards.DrawFreshTurnHand();
            RebuildIntents();
            DrawBoard();
            OnBoardChanged?.Invoke();
        }

        public void SelectAt(GridPos p)
        {
            if (!_board.Inside(p)) return;

            if (_turn.Phase == TurnPhase.Player && _pendingCard != null)
            {
                TryResolvePendingCardAt(p);
                return;
            }

            var hit = _board.At(p);
            if (_turn.Phase == TurnPhase.Player)
            {
                if (TryHandlePlayerCommand(p, hit)) return;
            }

            if (SelectedUnit != null && hit != null && hit.id == SelectedUnit.id)
            {
                ClearSelection();
                return;
            }

            SelectedUnit = hit;
            RecomputeSelectionHighlights();
            DrawBoard();
            OnSelectionChanged?.Invoke(SelectedUnit);
            OnBoardChanged?.Invoke();
        }

        public void ClearSelection()
        {
            SelectedUnit = null;
            _moveHighlights.Clear();
            _attackHighlights.Clear();
            if (_pendingCard == null) _cardTargetHighlights.Clear();
            DrawBoard();
            OnSelectionChanged?.Invoke(null);
            OnBoardChanged?.Invoke();
        }

        public bool TryPlayCard(CardDefinition card)
        {
            if (_turn.Phase != TurnPhase.Player || card == null) return false;
            if (!HasCardInHand(card)) return false;

            if (_pendingCard == card)
            {
                _pendingCard = null;
                _resolvedCardTarget = null;
                _resolvedCardTargetUnitId = null;
                _cardTargetHighlights.Clear();
                OnPendingCardChanged?.Invoke(null);
                DrawBoard();
                OnEncounterMessage?.Invoke($"{card.displayName} deselected.");
                OnBoardChanged?.Invoke();
                return true;
            }

            _pendingCard = card;
            _resolvedCardTarget = null;
            _resolvedCardTargetUnitId = null;
            BuildCardTargetHighlights(card);
            OnPendingCardChanged?.Invoke(card);
            DrawBoard();
            OnEncounterMessage?.Invoke($"Select a target for {card.displayName}.");
            OnBoardChanged?.Invoke();
            return true;
        }

        public void EndPlayerTurn()
        {
            if (_turn.Phase != TurnPhase.Player) return;
            _pendingCard = null;
            _resolvedCardTarget = null;
            _resolvedCardTargetUnitId = null;
            _cardTargetHighlights.Clear();
            OnPendingCardChanged?.Invoke(null);
            TickStatuses(Faction.Player);
            _cards.DiscardHand();
            _turn.EndPlayerTurn();
        }

        public void ApplyCard(CardDefinition card)
        {
            if (card == null) return;
            switch (card.kind)
            {
                case CardKind.HealSmall:
                    ApplyHealToTarget(Mathf.Max(1, card.amount));
                    break;
                case CardKind.Shield:
                    ApplyShieldToTarget(Mathf.Max(1, card.amount));
                    break;
                case CardKind.Summon:
                    TrySummon(card.summonKind, _resolvedCardTarget);
                    break;
                case CardKind.Barricade:
                    TryPlaceRock(_resolvedCardTarget);
                    break;
                case CardKind.BearTrap:
                case CardKind.SpikePit:
                    TryPlaceTrap(card.kind, 1, card.kind == CardKind.BearTrap ? 1 : 0, _resolvedCardTarget);
                    break;
            }
            _resolvedCardTarget = null;
            _resolvedCardTargetUnitId = null;
        }

        private void BuildCardTargetHighlights(CardDefinition card)
        {
            _cardTargetHighlights.Clear();
            if (card == null) return;

            if (card.kind == CardKind.Summon || card.kind == CardKind.Barricade)
            {
                for (var r = _board.Size - 1; r >= Mathf.Max(0, _board.Size - 2); r--)
                    for (var c = 0; c < _board.Size; c++)
                    {
                        var p = new GridPos(r, c);
                        if (!_board.Occupied(p)) _cardTargetHighlights.Add(Key(p));
                    }
                return;
            }

            if (card.kind == CardKind.BearTrap || card.kind == CardKind.SpikePit)
            {
                var a = Mathf.Max(1, _board.Size / 2 - 1);
                var b = Mathf.Min(_board.Size - 2, _board.Size / 2 + 1);
                for (var r = a; r <= b; r++)
                    for (var c = 0; c < _board.Size; c++)
                    {
                        var p = new GridPos(r, c);
                        if (!_board.Occupied(p)) _cardTargetHighlights.Add(Key(p));
                    }
                return;
            }

            if (card.kind == CardKind.HealSmall || card.kind == CardKind.Shield)
            {
                foreach (var kv in _board.UnitsById)
                {
                    var u = kv.Value;
                    if (u.faction != Faction.Player) continue;
                    _cardTargetHighlights.Add(Key(u.pos));
                }
            }
        }

        private void TryResolvePendingCardAt(GridPos p)
        {
            if (_pendingCard == null) return;

            var key = Key(p);
            if (!_cardTargetHighlights.Contains(key))
            {
                OnEncounterMessage?.Invoke("Invalid tile for this card.");
                return;
            }

            var targetUnit = _board.At(p);
            var needsEmpty = _pendingCard.kind == CardKind.Summon || _pendingCard.kind == CardKind.Barricade ||
                             _pendingCard.kind == CardKind.BearTrap || _pendingCard.kind == CardKind.SpikePit;
            var needsFriendlyUnit = _pendingCard.kind == CardKind.HealSmall || _pendingCard.kind == CardKind.Shield;

            if (needsEmpty && _board.Occupied(p))
            {
                OnEncounterMessage?.Invoke("Tile is occupied.");
                return;
            }
            if (needsFriendlyUnit && (targetUnit == null || targetUnit.faction != Faction.Player))
            {
                OnEncounterMessage?.Invoke("Select one of your units.");
                return;
            }

            if (!_turn.SpendEnergy(_pendingCard.cost))
            {
                OnEncounterMessage?.Invoke("Not enough energy.");
                return;
            }

            if (!HasCardInHand(_pendingCard))
            {
                _pendingCard = null;
                _cardTargetHighlights.Clear();
                OnPendingCardChanged?.Invoke(null);
                DrawBoard();
                return;
            }

            _resolvedCardTarget = p;
            _resolvedCardTargetUnitId = targetUnit != null ? targetUnit.id : null;
            var toPlay = _pendingCard;
            _pendingCard = null;
            _cardTargetHighlights.Clear();
            OnPendingCardChanged?.Invoke(null);

            var ok = _cards.TryPlayCard(toPlay, this);
            if (ok)
            {
                RebuildIntents();
                DrawBoard();
                OnBoardChanged?.Invoke();
            }
        }

        private bool HasCardInHand(CardDefinition card)
        {
            var hand = _cards.Hand;
            for (var i = 0; i < hand.Count; i++) if (hand[i] == card) return true;
            return false;
        }
        private bool TryHandlePlayerCommand(GridPos p, UnitRuntime hit)
        {
            if (SelectedUnit == null || SelectedUnit.faction != Faction.Player) return false;
            if (!_board.UnitsById.ContainsKey(SelectedUnit.id))
            {
                ClearSelection();
                return false;
            }

            if (hit != null && hit.id == SelectedUnit.id)
            {
                ClearSelection();
                return true;
            }

            var key = Key(p);
            if (_moveHighlights.Contains(key) && SelectedUnit.canMove)
            {
                var from = SelectedUnit.pos;
                if (_board.Move(SelectedUnit.id, p))
                {
                    TryPromotePawn(SelectedUnit);
                    SelectedUnit.canMove = false;
                    OnUnitMoved?.Invoke(SelectedUnit, from, p);
                    RecomputeSelectionHighlights();
                    RebuildIntents();
                    DrawBoard();
                    OnBoardChanged?.Invoke();
                }
                return true;
            }

            if (_attackHighlights.Contains(key) && SelectedUnit.canAttack)
            {
                if (hit == null || hit.faction != Faction.Enemy)
                {
                    OnEncounterMessage?.Invoke("Select an enemy to attack.");
                    return true;
                }
                ResolvePlayerAttack(SelectedUnit, p);
                return true;
            }

            if (hit != null && hit.faction == Faction.Player)
            {
                SelectedUnit = hit;
                RecomputeSelectionHighlights();
                DrawBoard();
                OnSelectionChanged?.Invoke(SelectedUnit);
                OnBoardChanged?.Invoke();
                return true;
            }

            return false;
        }

        private void ResolvePlayerAttack(UnitRuntime attacker, GridPos clicked)
        {
            var tiles = ComputePlayerAttackTiles(attacker, attacker.pos);
            var hits = new List<GridPos>();
            OnAttackStarted?.Invoke(attacker, tiles);

            for (var i = 0; i < tiles.Count; i++)
            {
                var sq = tiles[i];
                if (!_board.Inside(sq)) continue;

                // For single-target melee cards, constrain to clicked square if it belongs to the pattern.
                if (IsMelee(attacker.kind) && (sq.row != clicked.row || sq.col != clicked.col)) continue;

                var target = _board.At(sq);
                if (target == null || target.faction == Faction.Player) continue;
                var dmg = Mathf.Max(1, attacker.attack);
                _board.ApplyDamage(target.id, dmg);
                OnDamageDealt?.Invoke(target.id, dmg);
                hits.Add(sq);
            }

            if (hits.Count == 0)
            {
                OnEncounterMessage?.Invoke("No enemy hit.");
                OnAttackResolved?.Invoke(attacker, hits);
                return;
            }

            attacker.canAttack = false;
            attacker.canMove = false;
            OnAttackResolved?.Invoke(attacker, hits);
            SyncEnemyList();
            RebuildIntents();
            ClearSelection();
            CheckWinLose();
            DrawBoard();
            OnBoardChanged?.Invoke();
        }

        private void HandlePhaseChanged()
        {
            if (_turn.Phase == TurnPhase.Enemy)
            {
                if (_enemyTurnRoutine != null) StopCoroutine(_enemyTurnRoutine);
                _enemyTurnRoutine = StartCoroutine(ExecuteEnemyTurnRoutine());
            }
            else if (_turn.Phase == TurnPhase.Player)
            {
                ResetPlayerActions();
                _cards.DrawFreshTurnHand();
                RebuildIntents();
                DrawBoard();
                OnBoardChanged?.Invoke();
            }
        }

        private IEnumerator ExecuteEnemyTurnRoutine()
        {
            TickCaves();
            _currentEnemyPlan = _enemyIntentSystem.ValidateOrRecompute(_currentEnemyPlan);
            DrawBoard();
            OnBoardChanged?.Invoke();

            if (_currentEnemyPlan != null)
            {
                for (var i = 0; i < _currentEnemyPlan.intents.Count; i++)
                {
                    var it = _currentEnemyPlan.intents[i];
                    if (!_board.UnitsById.TryGetValue(it.actorId, out var actor)) continue;

                    var didMove = false;
                    if (it.to.row != actor.pos.row || it.to.col != actor.pos.col)
                    {
                        var from = actor.pos;
                        if (_board.Move(actor.id, it.to))
                        {
                            TryPromotePawn(actor);
                            didMove = true;
                            OnUnitMoved?.Invoke(actor, from, actor.pos);
                            DrawBoard();
                            OnBoardChanged?.Invoke();
                        }
                        else
                        {
                            it.blocked = true;
                            it.to = actor.pos;
                        }
                    }

                    if (didMove) yield return WaitEnemyStepDelay();

                    var didAttack = false;
                    if (it.kind == EnemyIntentKind.Capture || it.kind == EnemyIntentKind.Web)
                    {
                        didAttack = ExecuteEnemyAttack(actor, it.attackSquares);
                    }

                    if (didAttack) yield return WaitEnemyStepDelay();

                    var didSpecial = TryExecuteEnemySpecial(actor);
                    if (didSpecial)
                    {
                        DrawBoard();
                        OnBoardChanged?.Invoke();
                        yield return WaitEnemyStepDelay();
                    }
                }
            }

            TriggerTraps();
            SyncEnemyList();
            DrawBoard();
            CheckWinLose();
            _enemyTurnRoutine = null;
            _turn.EndEnemyTurn();
        }

        private bool ExecuteEnemyAttack(UnitRuntime actor, List<GridPos> attackSquares)
        {
            if (actor == null) return false;
            var squares = attackSquares ?? new List<GridPos>();
            var hits = new List<GridPos>();
            OnAttackStarted?.Invoke(actor, squares);

            for (var s = 0; s < squares.Count; s++)
            {
                var sq = squares[s];
                if (!_board.Inside(sq)) continue;
                var target = _board.At(sq);
                if (target == null || target.faction == Faction.Enemy || target.faction == Faction.Neutral) continue;
                var dmg = Mathf.Max(1, actor.attack);
                _board.ApplyDamage(target.id, dmg);
                OnDamageDealt?.Invoke(target.id, dmg);
                hits.Add(sq);
            }

            OnAttackResolved?.Invoke(actor, hits);
            DrawBoard();
            OnBoardChanged?.Invoke();
            return true;
        }

        private bool TryExecuteEnemySpecial(UnitRuntime actor)
        {
            if (actor == null || actor.kind != UnitKind.WolfAlpha) return false;
            var spawnPos = AdjacentEmpty(actor.pos);
            if (!spawnPos.HasValue) return false;

            var pup = NewUnit(UnitKind.WolfPup, Faction.Enemy, spawnPos.Value);
            if (!_board.Add(pup)) return false;

            _enemies.Add(pup);
            OnSpecialStarted?.Invoke(actor);
            return true;
        }

        private WaitForSeconds WaitEnemyStepDelay()
        {
            return new WaitForSeconds(Mathf.Max(0f, enemyStepDelaySeconds));
        }

        private void ResetPlayerActions()
        {
            foreach (var kv in _board.UnitsById)
            {
                var u = kv.Value;
                if (u.faction != Faction.Player) continue;
                if (u.isStructure) continue;
                u.canMove = true;
                u.canAttack = true;
            }
        }

        private void RebuildIntents()
        {
            _currentEnemyPlan = _enemyIntentSystem.BuildPlan();
            OnIntentsChanged?.Invoke();
        }

        private void RecomputeSelectionHighlights()
        {
            _moveHighlights.Clear();
            _attackHighlights.Clear();
            if (SelectedUnit == null || SelectedUnit.faction != Faction.Player || _turn.Phase != TurnPhase.Player) return;

            if (SelectedUnit.canMove)
            {
                var moves = ComputePlayerMoveTiles(SelectedUnit, SelectedUnit.pos);
                for (var i = 0; i < moves.Count; i++) _moveHighlights.Add(Key(moves[i]));
            }

            if (SelectedUnit.canAttack)
            {
                var atks = ComputePlayerAttackTiles(SelectedUnit, SelectedUnit.pos);
                for (var i = 0; i < atks.Count; i++) _attackHighlights.Add(Key(atks[i]));
            }
        }

        private List<GridPos> ComputePlayerMoveTiles(UnitRuntime piece, GridPos from)
        {
            var outTiles = new List<GridPos>();
            void TryAdd(GridPos p)
            {
                if (_board.Inside(p) && !_board.Occupied(p)) outTiles.Add(p);
            }

            switch (piece.kind)
            {
                case UnitKind.Pawn:
                    if (IsPromotedPawn(piece))
                    {
                        AddAdjacentAllMoves(from, TryAdd);
                    }
                    else
                    {
                        TryAdd(new GridPos(from.row - 1, from.col));
                    }
                    break;
                case UnitKind.Knight:
                    AddKnightMoves(from, TryAdd);
                    break;
                case UnitKind.Bishop:
                    AddRayMoves(from, outTiles, true, false);
                    break;
                case UnitKind.Rook:
                    AddRayMoves(from, outTiles, false, true);
                    break;
                case UnitKind.Queen:
                    AddRayMoves(from, outTiles, true, true);
                    break;
                case UnitKind.King:
                    AddAdjacentMoves(from, TryAdd);
                    break;
                default:
                    AddAdjacentMoves(from, TryAdd);
                    break;
            }
            return outTiles;
        }

        private List<GridPos> ComputePlayerAttackTiles(UnitRuntime piece, GridPos from)
        {
            var outTiles = new List<GridPos>();
            void AddIfInside(GridPos p)
            {
                if (_board.Inside(p)) outTiles.Add(p);
            }

            switch (piece.kind)
            {
                case UnitKind.Pawn:
                    if (IsPromotedPawn(piece))
                    {
                        AddAdjacentAttack(from, outTiles);
                    }
                    else
                    {
                        AddIfInside(new GridPos(from.row - 1, from.col - 1));
                        AddIfInside(new GridPos(from.row - 1, from.col + 1));
                    }
                    break;
                case UnitKind.Knight:
                    AddAdjacentAttack(from, outTiles);
                    break;
                case UnitKind.Bishop:
                    AddRayAttack(from, outTiles, true, false);
                    break;
                case UnitKind.Rook:
                    AddRayAttack(from, outTiles, false, true);
                    break;
                case UnitKind.Queen:
                    AddRayAttack(from, outTiles, true, true);
                    break;
                case UnitKind.King:
                    AddAdjacentAttack(from, outTiles);
                    break;
                default:
                    AddAdjacentAttack(from, outTiles);
                    break;
            }
            return outTiles;
        }

        private static bool IsMelee(UnitKind kind)
        {
            return kind == UnitKind.Pawn || kind == UnitKind.Knight || kind == UnitKind.King;
        }

        private void AddAdjacentMoves(GridPos from, System.Action<GridPos> add)
        {
            add(new GridPos(from.row + 1, from.col));
            add(new GridPos(from.row - 1, from.col));
            add(new GridPos(from.row, from.col + 1));
            add(new GridPos(from.row, from.col - 1));
        }

        private void AddAdjacentAllMoves(GridPos from, System.Action<GridPos> add)
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

        private void AddAdjacentAttack(GridPos from, List<GridPos> outTiles)
        {
            outTiles.Add(new GridPos(from.row + 1, from.col));
            outTiles.Add(new GridPos(from.row - 1, from.col));
            outTiles.Add(new GridPos(from.row, from.col + 1));
            outTiles.Add(new GridPos(from.row, from.col - 1));
            outTiles.Add(new GridPos(from.row + 1, from.col + 1));
            outTiles.Add(new GridPos(from.row + 1, from.col - 1));
            outTiles.Add(new GridPos(from.row - 1, from.col + 1));
            outTiles.Add(new GridPos(from.row - 1, from.col - 1));
        }

        private bool IsPromotedPawn(UnitRuntime unit)
        {
            return unit != null && unit.kind == UnitKind.Pawn && unit.status != null && unit.status.pawnPromoted;
        }

        private void TryPromotePawn(UnitRuntime unit)
        {
            if (unit == null || unit.kind != UnitKind.Pawn) return;
            if (unit.status == null) unit.status = new UnitStatus();
            if (unit.status.pawnPromoted) return;

            var promotionRow = unit.faction == Faction.Player ? 0 : _board.Size - 1;
            if (unit.pos.row != promotionRow) return;

            unit.status.pawnPromoted = true;
            OnEncounterMessage?.Invoke($"{unit.id} promoted.");
        }

        private void AddKnightMoves(GridPos from, System.Action<GridPos> add)
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

        private void AddRayMoves(GridPos from, List<GridPos> outTiles, bool diag, bool ortho)
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
                    outTiles.Add(p);
                    r += d.row;
                    c += d.col;
                }
            }
        }

        private void AddRayAttack(GridPos from, List<GridPos> outTiles, bool diag, bool ortho)
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
                    outTiles.Add(p);
                    if (_board.Occupied(p)) break;
                    r += d.row;
                    c += d.col;
                }
            }
        }

        private void TickStatuses(Faction faction)
        {
            var ids = new List<string>(_board.UnitsById.Keys);
            for (var i = 0; i < ids.Count; i++)
            {
                if (!_board.UnitsById.TryGetValue(ids[i], out var u) || u.faction != faction) continue;
                u.status.sleepingTurns = Mathf.Max(0, u.status.sleepingTurns - 1);
                u.status.rootedTurns = Mathf.Max(0, u.status.rootedTurns - 1);
                if (u.status.poisonedTurns > 0)
                {
                    u.status.poisonedTurns -= 1;
                    _board.ApplyDamage(u.id, 1);
                    OnDamageDealt?.Invoke(u.id, 1);
                }
            }
        }

        private void TriggerTraps()
        {
            for (var i = _traps.Count - 1; i >= 0; i--)
            {
                var t = _traps[i];
                var u = _board.At(new GridPos(t.row, t.col));
                if (u == null || u.faction != Faction.Enemy) continue;
                _board.ApplyDamage(u.id, Mathf.Max(1, t.damage));
                u.status.sleepingTurns = Mathf.Max(u.status.sleepingTurns, t.sleepTurns);
                _traps.RemoveAt(i);
                OnTrapTriggered?.Invoke($"{t.kind}@{t.row},{t.col}");
            }
        }

        private void TickCaves()
        {
            for (var i = 0; i < _caves.Count; i++)
            {
                var c = _caves[i];
                c.turnsUntilNextSpawn -= 1;
                if (c.turnsUntilNextSpawn > 0 || c.spawnCharges <= 0) continue;
                if (AliveFromCave(c.id) >= c.maxAliveFromThisCave)
                {
                    c.turnsUntilNextSpawn = 1;
                    continue;
                }
                var spawn = AdjacentEmpty(new GridPos(c.row, c.col));
                if (spawn == null) continue;
                var kind = PickSpawn(c.spawnPool);
                var e = NewUnit(kind, Faction.Enemy, spawn.Value);
                e.spawnedByCaveId = c.id;
                if (_board.Add(e)) _enemies.Add(e);
                c.spawnCharges -= 1;
                c.turnsUntilNextSpawn = 2;
            }
        }

        private void CheckWinLose()
        {
            var king = FindKing();
            if (king == null || king.hp <= 0)
            {
                OnEncounterMessage?.Invoke("Game over: King dead.");
                if (!_encounterResolved)
                {
                    _encounterResolved = true;
                    OnEncounterResolved?.Invoke(false, _activeNodeId);
                }
                return;
            }
            if (_enemies.Count == 0)
            {
                _session.SetKingHp(king.hp);
                OnEncounterMessage?.Invoke("Encounter won.");
                if (!_encounterResolved)
                {
                    _encounterResolved = true;
                    OnEncounterResolved?.Invoke(true, _activeNodeId);
                }
            }
        }

        private void ApplyHealToTarget(int amount)
        {
            var target = ResolveCardTargetUnit();
            if (target == null) return;
            var before = target.hp;
            target.hp = Mathf.Min(target.maxHp, target.hp + amount);
            if (target.kind == UnitKind.King) _session.SetKingHp(target.hp);
            if (target.hp > before) OnCardEffectApplied?.Invoke(target.id, CardKind.HealSmall);
        }

        private void ApplyShieldToTarget(int charges)
        {
            var target = ResolveCardTargetUnit();
            if (target == null) return;
            target.status.shieldCharge = Mathf.Max(target.status.shieldCharge, Mathf.Max(1, charges));
            OnCardEffectApplied?.Invoke(target.id, CardKind.Shield);
        }

        private UnitRuntime ResolveCardTargetUnit()
        {
            if (!string.IsNullOrEmpty(_resolvedCardTargetUnitId) &&
                _board.UnitsById.TryGetValue(_resolvedCardTargetUnitId, out var byId))
            {
                return byId;
            }

            if (_resolvedCardTarget.HasValue)
            {
                var unit = _board.At(_resolvedCardTarget.Value);
                if (unit != null && unit.faction == Faction.Player) return unit;
            }

            return FindKing();
        }

        private void TrySummon(UnitKind kind, GridPos? target)
        {
            var p = target ?? FirstEmptyBottom();
            if (p == null || _board.Occupied(p.Value)) return;
            _board.Add(NewUnit(kind, Faction.Player, p.Value));
        }

        private void TryPlaceRock(GridPos? target)
        {
            var p = target ?? FirstEmptyBottom();
            if (p == null || _board.Occupied(p.Value)) return;
            var rock = NewUnit(UnitKind.Rock, Faction.Neutral, p.Value);
            rock.isStructure = true;
            rock.maxHp = 12; rock.hp = 12; rock.attack = 0;
            _board.Add(rock);
        }

        private void TryPlaceTrap(CardKind kind, int damage, int sleepTurns, GridPos? target)
        {
            var p = target ?? FirstEmptyMiddle();
            if (p == null || _board.Occupied(p.Value)) return;
            _traps.Add(new TrapRuntime { row = p.Value.row, col = p.Value.col, kind = kind, damage = damage, sleepTurns = sleepTurns });
        }

        private void SpawnKing()
        {
            var pos = new GridPos(_board.Size - 1, _board.Size / 2);
            var king = NewUnit(UnitKind.King, Faction.Player, pos);
            king.maxHp = 5;
            king.hp = Mathf.Clamp(_session.PersistentKingHp, 1, 5);
            _board.Add(king);
        }

        private UnitRuntime NewUnit(UnitKind kind, Faction faction, GridPos pos)
        {
            _idCounter += 1;
            var def = _pieces.TryGetValue(kind, out var pdef) ? pdef : null;
            return new UnitRuntime
            {
                id = $"{faction}_{kind}_{_idCounter}",
                kind = kind,
                faction = faction,
                pos = pos,
                maxHp = def != null ? def.maxHp : 1,
                hp = def != null ? def.maxHp : 1,
                attack = def != null ? def.attack : 1,
                canMove = faction == Faction.Player,
                canAttack = faction == Faction.Player,
                isStructure = kind == UnitKind.Rock || kind == UnitKind.Cave
            };
        }

        private UnitRuntime FindKing()
        {
            foreach (var kv in _board.UnitsById)
                if (kv.Value.faction == Faction.Player && kv.Value.kind == UnitKind.King) return kv.Value;
            return null;
        }

        private void SyncEnemyList()
        {
            _enemies.Clear();
            foreach (var kv in _board.UnitsById) if (kv.Value.faction == Faction.Enemy) _enemies.Add(kv.Value);
        }

        private int AliveFromCave(string caveId)
        {
            var n = 0;
            for (var i = 0; i < _enemies.Count; i++) if (_enemies[i].spawnedByCaveId == caveId) n += 1;
            return n;
        }

        private UnitKind PickSpawn(List<SpawnWeight> weights)
        {
            if (weights == null || weights.Count == 0) return UnitKind.Bat;
            var total = 0;
            for (var i = 0; i < weights.Count; i++) total += Mathf.Max(1, weights[i].weight);
            var rng = new System.Random(_session.Seed + _session.EncounterIndex + total);
            var roll = rng.Next(0, total);
            for (var i = 0; i < weights.Count; i++)
            {
                roll -= Mathf.Max(1, weights[i].weight);
                if (roll < 0) return weights[i].kind;
            }
            return weights[0].kind;
        }

        private GridPos? AdjacentEmpty(GridPos p)
        {
            var neighbors = new[] { new GridPos(p.row + 1, p.col), new GridPos(p.row - 1, p.col), new GridPos(p.row, p.col + 1), new GridPos(p.row, p.col - 1) };
            for (var i = 0; i < neighbors.Length; i++) if (_board.Inside(neighbors[i]) && !_board.Occupied(neighbors[i])) return neighbors[i];
            return null;
        }

        private GridPos? FirstEmptyBottom()
        {
            for (var r = _board.Size - 1; r >= Mathf.Max(0, _board.Size - 2); r--)
                for (var c = 0; c < _board.Size; c++)
                    if (!_board.Occupied(new GridPos(r, c))) return new GridPos(r, c);
            return null;
        }

        private GridPos? FirstEmptyMiddle()
        {
            var a = Mathf.Max(1, _board.Size / 2 - 1);
            var b = Mathf.Min(_board.Size - 2, _board.Size / 2 + 1);
            for (var r = a; r <= b; r++)
                for (var c = 0; c < _board.Size; c++)
                    if (!_board.Occupied(new GridPos(r, c))) return new GridPos(r, c);
            return null;
        }

        private void DrawBoard()
        {
            var intentMove = new HashSet<string>();
            var intentAttack = new HashSet<string>();
            if (_currentEnemyPlan != null)
            {
                for (var i = 0; i < _currentEnemyPlan.intents.Count; i++)
                {
                    var it = _currentEnemyPlan.intents[i];
                    intentMove.Add(Key(it.to));
                    for (var j = 0; j < it.attackSquares.Count; j++) intentAttack.Add(Key(it.attackSquares[j]));
                }
            }

            if (boardUiGenerator != null)
            {
                var move = new HashSet<string>(_moveHighlights);
                foreach (var k in _cardTargetHighlights) move.Add(k);
                boardUiGenerator.Render(_board, SelectedUnit, SelectAt, move, _attackHighlights, intentMove, intentAttack, _traps);
                return;
            }

            DrawBoardFallback();
        }

        private void DrawBoardFallback()
        {
            if (boardRoot == null) return;
            for (var i = boardRoot.childCount - 1; i >= 0; i--) Destroy(boardRoot.GetChild(i).gameObject);
            foreach (var kv in _board.UnitsById)
            {
                var u = kv.Value;
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = u.id;
                go.transform.SetParent(boardRoot, false);
                go.transform.localPosition = new Vector3(u.pos.col * tileSize, -u.pos.row * tileSize, 0f);
                go.transform.localScale = Vector3.one * tileSize * 0.9f;
                var mat = new Material(Shader.Find("Sprites/Default"));
                mat.color = u.faction == Faction.Player ? Color.cyan : u.faction == Faction.Enemy ? Color.red : Color.gray;
                go.GetComponent<MeshRenderer>().sharedMaterial = mat;
                var click = go.AddComponent<UnitClickProxy>();
                click.Bind(this, u.pos);
            }
        }
    }
}













