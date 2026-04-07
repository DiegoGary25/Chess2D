using System.Collections.Generic;
using ChessPrototype.Unity.Board;
using ChessPrototype.Unity.Cards;
using ChessPrototype.Unity.Core;
using ChessPrototype.Unity.Data;
using ChessPrototype.Unity.Enemies;
using ChessPrototype.Unity.RunMap;
using ChessPrototype.Unity.UI;
using ChessPrototype.Unity.Units;
using UnityEngine;
using System.Collections;

namespace ChessPrototype.Unity.Encounters
{
    public sealed class EncounterController : MonoBehaviour, ICardPlaySink
    {
        private sealed class ToxicZoneRuntime
        {
            public int row;
            public int col;
            public int turnsRemaining;
            public int damage;
        }

        [SerializeField] private Transform boardRoot;
        [SerializeField] private float tileSize = 0.8f;
        [SerializeField] private BoardUiGenerator boardUiGenerator;
        [Header("Enemy Turn Timing")]
        [SerializeField] private float enemyStepDelaySeconds = 1f;
        [Header("Turn Movement Rules")]
        [Min(0)]
        [SerializeField] private int maxPieceMovesPerSidePerTurn = 0;

        private readonly BoardState _board = new BoardState();
        private readonly List<TrapRuntime> _traps = new List<TrapRuntime>();
        private readonly List<SanctuaryRuntime> _sanctuaries = new List<SanctuaryRuntime>();
        private readonly List<CaveRuntime> _caves = new List<CaveRuntime>();
        private readonly List<string> _beaconIds = new List<string>();
        private readonly HashSet<string> _objectiveEnemyIds = new HashSet<string>();
        private readonly List<UnitRuntime> _enemies = new List<UnitRuntime>();
        private readonly Dictionary<UnitKind, PieceDefinition> _pieces = new Dictionary<UnitKind, PieceDefinition>();
        private readonly Dictionary<UnitKind, EnemyDefinition> _enemyDefinitions = new Dictionary<UnitKind, EnemyDefinition>();
        private readonly Dictionary<string, EnemySpecialType> _pendingAttackSpecialByActorId = new Dictionary<string, EnemySpecialType>();
        private readonly Dictionary<string, List<GridPos>> _pendingSpecialTilesByActorId = new Dictionary<string, List<GridPos>>();
        private readonly List<ToxicZoneRuntime> _toxicZones = new List<ToxicZoneRuntime>();
        private readonly HashSet<string> _moveHighlights = new HashSet<string>();
        private readonly HashSet<string> _attackHighlights = new HashSet<string>();
        private readonly HashSet<string> _specialAttackHighlights = new HashSet<string>();
        private readonly HashSet<string> _attackMarkerPreviewHighlights = new HashSet<string>();
        private readonly HashSet<string> _cardTargetHighlights = new HashSet<string>();

        private GameSessionState _session;
        private TurnStateController _turn;
        private CardRuntimeController _cards;
        private EnemyIntentSystem _enemyIntentSystem;
        private EnemyPlan _currentEnemyPlan;
        private CardDefinition _pendingCard;
        private GridPos? _resolvedCardTarget;
        private string _resolvedCardTargetUnitId;
        private GridPos? _resolvedSecondaryCardTarget;
        private string _resolvedSecondaryCardTargetUnitId;
        private int _idCounter;
        private string _activeNodeId;
        private bool _encounterResolved;
        private Coroutine _enemyTurnRoutine;
        private bool _showEnemyIntents = true;
        private string _activeEnemyIntentActorId;
        private bool _allowEnemyIntentLines = true;
        private EnemyTurnActionOrder _enemyTurnActionOrder = EnemyTurnActionOrder.MoveThenAttack;
        private bool _beaconsRequired;
        private bool _objectiveTargetsRequired;
        private int _playerPieceMovesThisTurn;
        private int _enemyPieceMovesThisTurn;
        private int _enemyActionsThisTurn;
        private bool _pawnBrokerDiscountUsedThisCombat;
        private bool _sanctifiedAshUsedThisCombat;
        private bool _marchingBannerUsedThisTurn;
        private readonly Dictionary<string, int> _playerMoveDistanceByUnitThisTurn = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _forcedMarchRangeBonusByUnitThisTurn = new Dictionary<string, int>();
        private readonly HashSet<UnitKind> _firstCaptureTriggeredByKindThisTurn = new HashSet<UnitKind>();
        private GridPos? _hoveredTargetTile;
        private bool _chessOnlyEncounter;
        private bool _sleepPlayerSummonsOnSpawn;
        private bool _sleepCaveEnemySpawns;

        public BoardState Board => _board;
        public EnemyPlan CurrentEnemyPlan => _currentEnemyPlan;
        public bool ShowEnemyIntents => _showEnemyIntents;
        public string ActiveEnemyIntentActorId => _activeEnemyIntentActorId;
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
        public event System.Action<string, string> OnDebuffApplied;
        public event System.Action<string, GridPos> OnUnitBumped;
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
            _enemyDefinitions.Clear();
            if (_session != null && _session.Config != null)
            {
                foreach (var p in _session.Config.pieceDefinitions) _pieces[p.kind] = p;
                foreach (var e in _session.Config.enemyDefinitions) _enemyDefinitions[e.kind] = e;
                _allowEnemyIntentLines = _session.Config.showEnemyIntentLines;
                _enemyTurnActionOrder = _session.Config.enemyTurnActionOrder;
                _enemyIntentSystem.SetPlannerDepth(_session.Config.enemyPlannerDepth);
                _enemyIntentSystem.SetChessOnlyMode(false);
                _sleepPlayerSummonsOnSpawn = _session.Config.applySleepOnPlayerSpawn;
                _sleepCaveEnemySpawns = _session.Config.applySleepOnCaveEnemySpawn;
            }
            _turn.OnPhaseChanged += HandlePhaseChanged;
        }

        public void StartNode(RuntimeMapNode node)
        {
            _activeNodeId = node.id;
            _encounterResolved = false;
            var isBattle = node.type == MapNodeType.Battle || node.type == MapNodeType.Enemy || node.type == MapNodeType.Elite || node.type == MapNodeType.Boss;
            if (!isBattle)
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

            var encounterCount = _session != null && _session.Config != null && _session.Config.encounters != null
                ? _session.Config.encounters.Count
                : 0;
            var idx = 0;
            if (encounterCount > 0)
            {
                if (IsNoMapEndlessMode())
                {
                    idx = Mathf.Abs(_session.EncounterIndex % encounterCount);
                }
                else
                {
                    idx = Mathf.Clamp(_session.EncounterIndex, 0, encounterCount - 1);
                }
            }
            var tpl = encounterCount > 0 ? _session.Config.encounters[idx] : null;
            _chessOnlyEncounter = tpl == null || tpl.chessOnlyEncounter;
            _enemyIntentSystem.SetChessOnlyMode(_chessOnlyEncounter);
            _sleepPlayerSummonsOnSpawn = _session != null && _session.Config != null && _session.Config.applySleepOnPlayerSpawn;
            _sleepCaveEnemySpawns = _session != null && _session.Config != null && _session.Config.applySleepOnCaveEnemySpawn;
            _board.Reset(tpl != null ? tpl.boardSize : 4);
            List<Placement> generatedEnemyPlacements = null;
            List<CaveTemplate> generatedCaves = null;
            List<BeaconTemplate> generatedBeacons = null;
            if (tpl != null && tpl.useDynamicGeneration)
            {
                GenerateDynamicEncounterLayout(tpl, out generatedEnemyPlacements, out generatedCaves, out generatedBeacons);
            }
            _traps.Clear();
            _sanctuaries.Clear();
            _caves.Clear();
            _beaconIds.Clear();
            _objectiveEnemyIds.Clear();
            _enemies.Clear();
            _idCounter = 0;
            _moveHighlights.Clear();
            _attackHighlights.Clear();
            _specialAttackHighlights.Clear();
            _cardTargetHighlights.Clear();
            _pendingAttackSpecialByActorId.Clear();
            _pendingSpecialTilesByActorId.Clear();
            _toxicZones.Clear();
            _attackMarkerPreviewHighlights.Clear();
            _hoveredTargetTile = null;
            _pendingCard = null;
            _resolvedCardTarget = null;
            _resolvedCardTargetUnitId = null;
            _resolvedSecondaryCardTarget = null;
            _resolvedSecondaryCardTargetUnitId = null;
            _beaconsRequired = false;
            _objectiveTargetsRequired = false;
            _playerPieceMovesThisTurn = 0;
            _enemyPieceMovesThisTurn = 0;
            _enemyActionsThisTurn = 0;
            _pawnBrokerDiscountUsedThisCombat = false;
            _sanctifiedAshUsedThisCombat = false;
            _marchingBannerUsedThisTurn = false;
            _playerMoveDistanceByUnitThisTurn.Clear();
            _forcedMarchRangeBonusByUnitThisTurn.Clear();
            _firstCaptureTriggeredByKindThisTurn.Clear();
            SelectedUnit = null;
            _hoveredTargetTile = null;
            OnSelectionChanged?.Invoke(null);
            OnPendingCardChanged?.Invoke(null);

            SpawnKing();
            ApplyEncounterStartTrinketEffects();
            if (tpl != null)
            {
                var enemyPlacements = generatedEnemyPlacements ?? tpl.enemyPlacements;
                var caves = generatedCaves ?? tpl.caves;
                for (var i = 0; i < enemyPlacements.Count; i++)
                {
                    var pl = enemyPlacements[i];
                    var spawnKind = SanitizeEnemyKindForEncounter(pl.kind);
                    var enemy = NewUnit(spawnKind, Faction.Enemy, new GridPos(pl.row, pl.col));
                    if (_board.Add(enemy))
                    {
                        _enemies.Add(enemy);
                        if (IsObjectiveEnemy(enemy.kind)) _objectiveEnemyIds.Add(enemy.id);
                    }
                }
                for (var c = 0; c < caves.Count; c++)
                {
                    var caveTpl = caves[c];
                    var caveUnit = NewUnit(UnitKind.Cave, Faction.Neutral, new GridPos(caveTpl.row, caveTpl.col));
                    caveUnit.isStructure = true;
                    caveUnit.maxHp = 5;
                    caveUnit.hp = 5;
                    caveUnit.attack = 0;
                    _board.Add(caveUnit);
                    _caves.Add(new CaveRuntime
                    {
                        id = caveTpl.id,
                        unitId = caveUnit.id,
                        row = caveTpl.row,
                        col = caveTpl.col,
                        turnsUntilNextSpawn = caveTpl.turnsUntilNextSpawn,
                        spawnCharges = caveTpl.spawnCharges,
                        maxAliveFromThisCave = caveTpl.maxAliveFromThisCave,
                        spawnPool = new List<SpawnWeight>(caveTpl.spawnPool)
                    });
                }
            }
            _objectiveTargetsRequired = _objectiveEnemyIds.Count > 0;
            if (tpl != null && tpl.useDynamicGeneration)
            {
                SpawnBeacons(generatedBeacons);
            }
            else
            {
                SpawnBeacons(tpl != null && tpl.createBeacons ? tpl.beacons : null);
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
            _hoveredTargetTile = null;
            _attackMarkerPreviewHighlights.Clear();

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
            _hoveredTargetTile = null;
            _attackMarkerPreviewHighlights.Clear();
            _moveHighlights.Clear();
            _attackHighlights.Clear();
            _specialAttackHighlights.Clear();
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
                _resolvedSecondaryCardTarget = null;
                _resolvedSecondaryCardTargetUnitId = null;
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
            _resolvedSecondaryCardTarget = null;
            _resolvedSecondaryCardTargetUnitId = null;
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
            ApplySanctuaryEndOfPlayerTurn();
            _pendingCard = null;
            _resolvedCardTarget = null;
            _resolvedCardTargetUnitId = null;
            _resolvedSecondaryCardTarget = null;
            _resolvedSecondaryCardTargetUnitId = null;
            _cardTargetHighlights.Clear();
            OnPendingCardChanged?.Invoke(null);
            TickStatuses(Faction.Player);
            ApplyToxicZonesEndOfTurn();
            _cards.DiscardHand();
            _forcedMarchRangeBonusByUnitThisTurn.Clear();
            _turn.EndPlayerTurn();
        }

        public void ApplyCard(CardDefinition card)
        {
            if (card == null) return;
            if (TryApplyCustomCard(card))
            {
                _resolvedCardTarget = null;
                _resolvedCardTargetUnitId = null;
                _resolvedSecondaryCardTarget = null;
                _resolvedSecondaryCardTargetUnitId = null;
                return;
            }

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
            _resolvedSecondaryCardTarget = null;
            _resolvedSecondaryCardTargetUnitId = null;
        }

        private void BuildCardTargetHighlights(CardDefinition card)
        {
            _cardTargetHighlights.Clear();
            if (card == null) return;
            var cardId = GetCardId(card);

            if (cardId == "castling")
            {
                foreach (var kv in _board.UnitsById)
                {
                    var u = kv.Value;
                    if (u == null || u.faction != Faction.Player || u.isStructure) continue;
                    if (!string.IsNullOrEmpty(_resolvedCardTargetUnitId) && u.id == _resolvedCardTargetUnitId) continue;
                    _cardTargetHighlights.Add(Key(u.pos));
                }
                return;
            }

            if (IsFriendlyTargetCard(cardId))
            {
                foreach (var kv in _board.UnitsById)
                {
                    var u = kv.Value;
                    if (u == null || u.faction != Faction.Player) continue;
                    _cardTargetHighlights.Add(Key(u.pos));
                }
                return;
            }

            if (IsEnemyTargetCard(cardId))
            {
                foreach (var kv in _board.UnitsById)
                {
                    var u = kv.Value;
                    if (u == null || u.faction != Faction.Enemy) continue;
                    _cardTargetHighlights.Add(Key(u.pos));
                }
                return;
            }

            if (card.kind == CardKind.Summon)
            {
                for (var r = _board.Size - 1; r >= Mathf.Max(0, _board.Size - 2); r--)
                    for (var c = 0; c < _board.Size; c++)
                    {
                        var p = new GridPos(r, c);
                        if (!_board.Occupied(p)) _cardTargetHighlights.Add(Key(p));
                    }
                return;
            }

            if (card.kind == CardKind.Barricade || card.kind == CardKind.BearTrap || card.kind == CardKind.SpikePit)
            {
                for (var r = 0; r < _board.Size; r++)
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
            var cardId = GetCardId(_pendingCard);

            var key = Key(p);
            if (!_cardTargetHighlights.Contains(key))
            {
                OnEncounterMessage?.Invoke("Invalid tile for this card.");
                return;
            }

            var targetUnit = _board.At(p);
            if (cardId == "castling" && string.IsNullOrEmpty(_resolvedCardTargetUnitId))
            {
                if (targetUnit == null || targetUnit.faction != Faction.Player || targetUnit.isStructure)
                {
                    OnEncounterMessage?.Invoke("Select your first ally.");
                    return;
                }

                _resolvedCardTarget = p;
                _resolvedCardTargetUnitId = targetUnit.id;
                BuildCardTargetHighlights(_pendingCard);
                DrawBoard();
                OnEncounterMessage?.Invoke("Select your second ally to swap.");
                OnBoardChanged?.Invoke();
                return;
            }

            var hasCustomRule = IsEmptyTileCard(cardId) || IsFriendlyTargetCard(cardId) || IsEnemyTargetCard(cardId);
            var needsEmpty = hasCustomRule
                ? IsEmptyTileCard(cardId)
                : _pendingCard.kind == CardKind.Summon || _pendingCard.kind == CardKind.Barricade ||
                  _pendingCard.kind == CardKind.BearTrap || _pendingCard.kind == CardKind.SpikePit;
            var needsFriendlyUnit = hasCustomRule
                ? IsFriendlyTargetCard(cardId)
                : _pendingCard.kind == CardKind.HealSmall || _pendingCard.kind == CardKind.Shield;
            var needsEnemyUnit = IsEnemyTargetCard(cardId);

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
            if (needsEnemyUnit && (targetUnit == null || targetUnit.faction != Faction.Enemy))
            {
                OnEncounterMessage?.Invoke("Select an enemy unit.");
                return;
            }

            var effectiveCost = ResolveCardEnergyCost(_pendingCard);
            if (!_turn.SpendEnergy(effectiveCost))
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

            if (cardId == "castling")
            {
                _resolvedSecondaryCardTarget = p;
                _resolvedSecondaryCardTargetUnitId = targetUnit != null ? targetUnit.id : null;
            }
            else
            {
                _resolvedCardTarget = p;
                _resolvedCardTargetUnitId = targetUnit != null ? targetUnit.id : null;
                _resolvedSecondaryCardTarget = null;
                _resolvedSecondaryCardTargetUnitId = null;
            }
            var toPlay = _pendingCard;
            _pendingCard = null;
            _cardTargetHighlights.Clear();
            OnPendingCardChanged?.Invoke(null);

            var ok = _cards.TryPlayCard(toPlay, this);
            if (ok)
            {
                ConsumePerCombatCardDiscounts(toPlay);
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

        private int ResolveCardEnergyCost(CardDefinition card)
        {
            if (card == null) return 0;
            var cost = Mathf.Max(0, card.cost);
            if (!_pawnBrokerDiscountUsedThisCombat && HasTrinket("pawn_brokers_seal") && IsPawnSummonCard(card))
            {
                cost = Mathf.Max(0, cost - 1);
            }
            return cost;
        }

        private void ConsumePerCombatCardDiscounts(CardDefinition card)
        {
            if (card == null) return;
            if (!_pawnBrokerDiscountUsedThisCombat && HasTrinket("pawn_brokers_seal") && IsPawnSummonCard(card))
            {
                _pawnBrokerDiscountUsedThisCombat = true;
            }
        }

        private static bool IsPawnSummonCard(CardDefinition card)
        {
            if (card == null) return false;
            return card.kind == CardKind.Summon && card.summonKind == UnitKind.Pawn;
        }

        private bool TryHandlePlayerCommand(GridPos p, UnitRuntime hit)
        {
            if (SelectedUnit == null || SelectedUnit.faction != Faction.Player) return false;
            if (SelectedUnit.kind == UnitKind.WolfAlpha)
            {
                OnEncounterMessage?.Invoke("Wolf Alpha is not available for the player.");
                return true;
            }
            if (!_board.UnitsById.ContainsKey(SelectedUnit.id))
            {
                ClearSelection();
                return false;
            }

            if (hit != null && hit.id == SelectedUnit.id)
            {
                var selfKey = Key(p);
                if (_specialAttackHighlights.Contains(selfKey) && CanUnitAttackNow(SelectedUnit))
                {
                    if (TryResolvePlayerSpecial(SelectedUnit, p, hit)) return true;
                }
                ClearSelection();
                return true;
            }

            var key = Key(p);
            if (_specialAttackHighlights.Contains(key) && CanUnitAttackNow(SelectedUnit))
            {
                if (TryResolvePlayerSpecial(SelectedUnit, p, hit)) return true;
            }

            if (_moveHighlights.Contains(key) && CanUnitMoveNow(SelectedUnit))
            {
                if (!CanPerformPieceMove(Faction.Player))
                {
                    OnEncounterMessage?.Invoke("You already moved a piece this turn.");
                    return true;
                }
                var from = SelectedUnit.pos;
                if (_board.Move(SelectedUnit.id, p))
                {
                    RegisterPieceMove(Faction.Player);
                    MarkPlayerMoveActionUsed();
                    RegisterPlayerMovementForTurn(SelectedUnit, from, p);
                    var promoted = TryPromotePawn(SelectedUnit);
                    if (_enemyTurnActionOrder == EnemyTurnActionOrder.MoveThenAttack || promoted)
                    {
                        // Chess-like mode: a unit that moves cannot attack this turn.
                        SelectedUnit.moveActionsRemaining = 0;
                        SelectedUnit.attackActionsRemaining = 0;
                        SelectedUnit.canMove = false;
                        SelectedUnit.canAttack = false;
                    }
                    else
                    {
                        SelectedUnit.moveActionsRemaining = Mathf.Max(0, SelectedUnit.moveActionsRemaining - 1);
                        SelectedUnit.canMove = SelectedUnit.moveActionsRemaining > 0;
                    }
                    OnUnitMoved?.Invoke(SelectedUnit, from, p);
                    TriggerTraps();
                    SyncEnemyList();
                    TrimDeadEnemyIntents();
                    CheckWinLose();
                    if (!_board.UnitsById.ContainsKey(SelectedUnit.id))
                    {
                        ClearSelection();
                        return true;
                    }
                    RecomputeSelectionHighlights();
                    DrawBoard();
                    OnBoardChanged?.Invoke();
                }
                return true;
            }

            if (_attackHighlights.Contains(key) && CanUnitAttackNow(SelectedUnit))
            {
                if (hit == null || hit.faction == Faction.Player)
                {
                    OnEncounterMessage?.Invoke("Select a valid target to attack.");
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
            if (attacker != null &&
                attacker.kind == UnitKind.Knight &&
                TryResolveKnightLeapAttack(attacker, clicked, Faction.Enemy))
            {
                return;
            }

            var tiles = ComputePlayerAttackTiles(attacker, attacker.pos, clicked);
            var hits = new List<GridPos>();
            GridPos? firstCaptureSquare = null;
            OnAttackStarted?.Invoke(attacker, tiles);

            for (var i = 0; i < tiles.Count; i++)
            {
                var sq = tiles[i];
                if (!_board.Inside(sq)) continue;

                // For single-target melee cards, constrain to clicked square if it belongs to the pattern.
                if (IsMelee(attacker.kind) && (sq.row != clicked.row || sq.col != clicked.col)) continue;

                var target = _board.At(sq);
                if (target == null || target.faction == Faction.Player) continue;
                var targetId = target.id;
                var dmg = ResolveOutgoingDamage(attacker, target, true);
                _board.ApplyDamage(target.id, dmg);
                OnDamageDealt?.Invoke(target.id, dmg);
                if (attacker.kind == UnitKind.Owl)
                {
                    TryApplySleep(target, 1);
                }
                if (_board.At(sq) == null && !firstCaptureSquare.HasValue) firstCaptureSquare = sq;
                hits.Add(sq);
                if (!_board.UnitsById.ContainsKey(targetId))
                {
                    HandlePlayerCapture(attacker, sq);
                }
            }

            if (hits.Count == 0)
            {
                OnEncounterMessage?.Invoke("No enemy hit.");
                OnAttackResolved?.Invoke(attacker, hits);
                return;
            }

            if (firstCaptureSquare.HasValue &&
                _board.UnitsById.ContainsKey(attacker.id) &&
                _board.At(firstCaptureSquare.Value) == null)
            {
                var from = attacker.pos;
                if (CanPerformPieceMove(Faction.Player) && _board.Move(attacker.id, firstCaptureSquare.Value))
                {
                    RegisterPieceMove(Faction.Player);
                    MarkPlayerMoveActionUsed();
                    RegisterPlayerMovementForTurn(attacker, from, firstCaptureSquare.Value);
                    TryPromotePawn(attacker);
                    OnUnitMoved?.Invoke(attacker, from, firstCaptureSquare.Value);
                }
            }

            if (attacker.kind == UnitKind.Bear)
            {
                attacker.hp = Mathf.Min(attacker.maxHp, attacker.hp + 1);
            }

            attacker.attackActionsRemaining = 0;
            attacker.canAttack = false;
            attacker.moveActionsRemaining = 0;
            attacker.canMove = false;
            OnAttackResolved?.Invoke(attacker, hits);
            SyncEnemyList();
            TrimDeadEnemyIntents();
            ClearSelection();
            CheckWinLose();
            DrawBoard();
            OnBoardChanged?.Invoke();
        }

        public void HoverAt(GridPos? p)
        {
            if (p.HasValue && !_board.Inside(p.Value)) p = null;
            if (_hoveredTargetTile.HasValue == p.HasValue)
            {
                if (!p.HasValue || (_hoveredTargetTile.Value.row == p.Value.row && _hoveredTargetTile.Value.col == p.Value.col))
                {
                    return;
                }
            }

            _hoveredTargetTile = p;
            DrawBoard();
            OnBoardChanged?.Invoke();
        }

        private bool TryResolvePlayerSpecial(UnitRuntime attacker, GridPos clicked, UnitRuntime clickedUnit)
        {
            if (attacker == null || attacker.faction != Faction.Player) return false;

            var affectedTiles = new List<GridPos>();
            var didApply = false;
            switch (attacker.kind)
            {
                case UnitKind.Bat:
                    didApply = TryResolvePlayerBatShriek(attacker, clicked, affectedTiles);
                    break;
                case UnitKind.Coyote:
                    didApply = TryApplyPlayerCoyoteHowl(attacker, affectedTiles);
                    break;
                case UnitKind.Boar:
                    didApply = TryResolvePlayerBoarRam(attacker, clickedUnit, affectedTiles);
                    break;
                case UnitKind.Owl:
                    didApply = TryResolvePlayerOwlSpecial(attacker, clickedUnit, affectedTiles);
                    break;
                case UnitKind.Skunk:
                    didApply = TryResolvePlayerSkunkSpecial(attacker, clicked, affectedTiles);
                    break;
                case UnitKind.Spider:
                    didApply = TryResolvePlayerSpiderSpecial(attacker, clickedUnit, affectedTiles);
                    break;
                case UnitKind.Toad:
                    didApply = TryResolvePlayerToadPull(attacker, clickedUnit, affectedTiles);
                    break;
            }

            if (!didApply) return false;

            OnSpecialStarted?.Invoke(attacker);
            OnAttackStarted?.Invoke(attacker, affectedTiles);
            OnAttackResolved?.Invoke(attacker, affectedTiles);
            attacker.attackActionsRemaining = 0;
            attacker.canAttack = false;
            attacker.moveActionsRemaining = 0;
            attacker.canMove = false;
            SyncEnemyList();
            TrimDeadEnemyIntents();
            ClearSelection();
            CheckWinLose();
            DrawBoard();
            OnBoardChanged?.Invoke();
            return true;
        }

        private bool TryResolvePlayerBatShriek(UnitRuntime attacker, GridPos clicked, List<GridPos> affectedTiles)
        {
            if (attacker == null) return false;
            var step = DirectionStepExtended(attacker.pos, clicked, false, true);
            if (step.row == 0 && step.col == 0) return false;

            var probe = new GridPos(attacker.pos.row + step.row * 2, attacker.pos.col + step.col * 2);
            var didApply = false;
            while (_board.Inside(probe))
            {
                affectedTiles.Add(probe);
                var target = _board.At(probe);
                if (IsPlayerHostile(target) && !target.isStructure)
                {
                    target.status.nextAttackDamageModifier -= 1;
                    OnDebuffApplied?.Invoke(target.id, "-1 ATK");
                    didApply = true;
                }
                probe = new GridPos(probe.row + step.row, probe.col + step.col);
            }

            return didApply;
        }

        private bool TryApplyPlayerCoyoteHowl(UnitRuntime attacker, List<GridPos> affectedTiles)
        {
            if (attacker == null) return false;
            var hasAdjacentCoyote = false;
            foreach (var kv in _board.UnitsById)
            {
                var unit = kv.Value;
                if (unit == null || unit.id == attacker.id || unit.kind != UnitKind.Coyote) continue;
                if (!IsAdjacentAll(attacker.pos, unit.pos)) continue;
                hasAdjacentCoyote = true;
                break;
            }

            if (!hasAdjacentCoyote) return false;

            foreach (var kv in _board.UnitsById)
            {
                var unit = kv.Value;
                if (unit == null || unit.kind != UnitKind.Coyote) continue;
                unit.status.nextAttackDamageModifier += 1;
                OnDebuffApplied?.Invoke(unit.id, "+1 ATK");
                affectedTiles.Add(unit.pos);
            }

            return affectedTiles.Count > 0;
        }

        private bool TryResolvePlayerBoarRam(UnitRuntime attacker, UnitRuntime target, List<GridPos> affectedTiles)
        {
            if (attacker == null || !IsPlayerHostile(target)) return false;
            if (!HasBoarRunningSpace(attacker.pos, target.pos)) return false;

            var step = DirectionStepToward(attacker.pos, target.pos);
            if (step.row == 0 && step.col == 0) return false;

            affectedTiles.Add(target.pos);
            var behind = new GridPos(target.pos.row + step.row, target.pos.col + step.col);
            var behindInside = _board.Inside(behind);
            var behindUnit = behindInside ? _board.At(behind) : null;
            var ramDamage = Mathf.Max(1, ResolveOutgoingDamage(attacker));

            if (behindInside && behindUnit == null)
            {
                var targetFrom = target.pos;
                if (!_board.Move(target.id, behind)) return false;
                affectedTiles.Add(behind);
                OnUnitMoved?.Invoke(target, targetFrom, behind);

                var actorFrom = attacker.pos;
                if (_board.Move(attacker.id, targetFrom))
                {
                    RegisterPieceMove(Faction.Player);
                    OnUnitMoved?.Invoke(attacker, actorFrom, targetFrom);
                    TriggerTraps();
                }

                _board.ApplyDamage(target.id, ramDamage);
                OnDamageDealt?.Invoke(target.id, ramDamage);
                return true;
            }

            OnUnitBumped?.Invoke(target.id, step);
            _board.ApplyDamage(target.id, ramDamage + 1);
            OnDamageDealt?.Invoke(target.id, ramDamage + 1);

            var actorFront = new GridPos(target.pos.row - step.row, target.pos.col - step.col);
            if ((actorFront.row != attacker.pos.row || actorFront.col != attacker.pos.col) &&
                _board.Inside(actorFront) &&
                !_board.Occupied(actorFront))
            {
                var actorFrom = attacker.pos;
                if (_board.Move(attacker.id, actorFront))
                {
                    RegisterPieceMove(Faction.Player);
                    OnUnitMoved?.Invoke(attacker, actorFrom, actorFront);
                    TriggerTraps();
                }
            }

            if (behindUnit != null)
            {
                affectedTiles.Add(behind);
                _board.ApplyDamage(behindUnit.id, 1);
                OnDamageDealt?.Invoke(behindUnit.id, 1);
            }

            return true;
        }

        private bool TryResolvePlayerSkunkSpecial(UnitRuntime attacker, GridPos clicked, List<GridPos> affectedTiles)
        {
            if (attacker == null) return false;
            if (IsAdjacentAll(attacker.pos, clicked)) return false;
            if (!_board.Inside(clicked)) return false;

            var origin = new GridPos(
                Mathf.Clamp(clicked.row, 0, Mathf.Max(0, _board.Size - 2)),
                Mathf.Clamp(clicked.col, 0, Mathf.Max(0, _board.Size - 2)));
            for (var r = 0; r < 2; r++)
            {
                for (var c = 0; c < 2; c++)
                {
                    var tile = new GridPos(origin.row + r, origin.col + c);
                    if (!_board.Inside(tile)) continue;
                    affectedTiles.Add(tile);
                    AddOrRefreshToxicZone(tile, 1, 1);
                }
            }

            return affectedTiles.Count > 0;
        }

        private bool TryResolvePlayerSpiderSpecial(UnitRuntime attacker, UnitRuntime target, List<GridPos> affectedTiles)
        {
            if (attacker == null || !IsPlayerHostile(target)) return false;
            if (IsAdjacentAll(attacker.pos, target.pos)) return false;

            _board.ApplyDamage(target.id, 1);
            OnDamageDealt?.Invoke(target.id, 1);
            if (_board.UnitsById.TryGetValue(target.id, out var aliveTarget) && aliveTarget != null)
            {
                aliveTarget.status.rootedTurns = Mathf.Max(aliveTarget.status.rootedTurns, 1);
                OnDebuffApplied?.Invoke(aliveTarget.id, "Rooted 1");
                affectedTiles.Add(aliveTarget.pos);
            }
            return true;
        }

        private bool TryResolvePlayerToadPull(UnitRuntime attacker, UnitRuntime target, List<GridPos> affectedTiles)
        {
            if (attacker == null || !IsPlayerHostile(target)) return false;
            if (IsAdjacentAll(attacker.pos, target.pos)) return false;
            if (!IsOrthogonalClearLine(attacker.pos, target.pos)) return false;
            var distance = Mathf.Abs(attacker.pos.row - target.pos.row) + Mathf.Abs(attacker.pos.col - target.pos.col);
            if (distance < 2) return false;

            var damage = Mathf.Max(1, ResolveOutgoingDamage(attacker));
            _board.ApplyDamage(target.id, damage);
            OnDamageDealt?.Invoke(target.id, damage);
            if (!_board.UnitsById.TryGetValue(target.id, out var aliveTarget) || aliveTarget == null)
            {
                affectedTiles.Add(target.pos);
                return true;
            }

            var step = DirectionStepToward(attacker.pos, aliveTarget.pos);
            var pullTo = new GridPos(aliveTarget.pos.row - step.row, aliveTarget.pos.col - step.col);
            if (_board.Inside(pullTo) && !_board.Occupied(pullTo))
            {
                var from = aliveTarget.pos;
                if (_board.Move(aliveTarget.id, pullTo))
                {
                    OnUnitMoved?.Invoke(aliveTarget, from, pullTo);
                }
            }

            affectedTiles.Add(aliveTarget.pos);
            return true;
        }

        private bool TryResolvePlayerOwlSpecial(UnitRuntime attacker, UnitRuntime target, List<GridPos> affectedTiles)
        {
            if (attacker == null || target == null) return false;
            if (!CanOwlSpecialTarget(attacker, target)) return false;

            var dmg = Mathf.Max(1, ResolveOutgoingDamage(attacker));
            _board.ApplyDamage(target.id, dmg);
            OnDamageDealt?.Invoke(target.id, dmg);
            if (_board.UnitsById.TryGetValue(target.id, out var aliveTarget) && aliveTarget != null)
            {
                TryApplySleep(aliveTarget, 1);
                affectedTiles.Add(aliveTarget.pos);
            }
            else
            {
                affectedTiles.Add(target.pos);
            }

            return true;
        }

        private bool TryResolveKnightLeapAttack(UnitRuntime attacker, GridPos clicked, Faction targetFaction)
        {
            if (attacker == null || attacker.kind != UnitKind.Knight) return false;
            if (!IsKnightLeap(attacker.pos, clicked)) return false;
            if (!_board.Inside(clicked)) return false;

            var target = _board.At(clicked);
            if (target == null || target.faction != targetFaction) return false;

            var hits = new List<GridPos>();
            OnAttackStarted?.Invoke(attacker, hits);
            var specialDamage = ResolveOutgoingDamage(attacker, target, true);
            _board.ApplyDamage(target.id, specialDamage);
            OnDamageDealt?.Invoke(target.id, specialDamage);
            hits.Add(clicked);

            var targetDied = _board.At(clicked) == null;
            if (targetDied)
            {
                HandlePlayerCapture(attacker, clicked);
            }
            if (targetDied)
            {
                var from = attacker.pos;
                if (CanPerformPieceMove(Faction.Player) && _board.Move(attacker.id, clicked))
                {
                    RegisterPieceMove(Faction.Player);
                    MarkPlayerMoveActionUsed();
                    RegisterPlayerMovementForTurn(attacker, from, clicked);
                    OnUnitMoved?.Invoke(attacker, from, clicked);
                }
            }

            attacker.attackActionsRemaining = 0;
            attacker.canAttack = false;
            attacker.moveActionsRemaining = 0;
            attacker.canMove = false;
            OnAttackResolved?.Invoke(attacker, hits);
            SyncEnemyList();
            TrimDeadEnemyIntents();
            ClearSelection();
            CheckWinLose();
            DrawBoard();
            OnBoardChanged?.Invoke();
            return true;
        }

        private void HandlePhaseChanged()
        {
            if (_turn.Phase == TurnPhase.Enemy)
            {
                _enemyPieceMovesThisTurn = 0;
                _enemyActionsThisTurn = 0;
                if (_enemyTurnRoutine != null) StopCoroutine(_enemyTurnRoutine);
                _enemyTurnRoutine = StartCoroutine(ExecuteEnemyTurnRoutine());
            }
            else if (_turn.Phase == TurnPhase.Player)
            {
                _playerPieceMovesThisTurn = 0;
                _marchingBannerUsedThisTurn = false;
                _playerMoveDistanceByUnitThisTurn.Clear();
                _forcedMarchRangeBonusByUnitThisTurn.Clear();
                _firstCaptureTriggeredByKindThisTurn.Clear();
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
            // Hide all intents at enemy-turn start.
            _showEnemyIntents = false;
            _activeEnemyIntentActorId = null;
            DrawBoard();
            OnBoardChanged?.Invoke();

            if (_currentEnemyPlan != null)
            {
                if (_enemyTurnActionOrder == EnemyTurnActionOrder.MoveThenAttack)
                {
                    // Per-enemy sequence (chess-like): each enemy does either attack or move.
                    // If an enemy has no useful move/attack right now, defer once and revisit later this turn.
                    var actorQueue = new Queue<string>();
                    var deferredOnce = new HashSet<string>();
                    for (var i = 0; i < _currentEnemyPlan.intents.Count; i++)
                    {
                        var it = _currentEnemyPlan.intents[i];
                        if (it == null || string.IsNullOrEmpty(it.actorId)) continue;
                        actorQueue.Enqueue(it.actorId);
                    }

                    while (actorQueue.Count > 0)
                    {
                        if (!CanEnemyActNow()) break;
                        var actorId = actorQueue.Dequeue();
                        if (!_board.UnitsById.TryGetValue(actorId, out var actor)) continue;

                        _currentEnemyPlan = _enemyIntentSystem.BuildPlan();
                        var it = FindIntentByActorId(_currentEnemyPlan, actor.id);
                        if (it == null) continue;
                        var canMoveNow = it.to.row != actor.pos.row || it.to.col != actor.pos.col;
                        var attackSquaresNow = _enemyIntentSystem.BuildImmediateAttackSquares(actor);
                        var canAttackNow = attackSquaresNow.Count > 0;

                        // Prefer moving into pressure when no immediate target; special is preferred only if
                        // the unit can attack now or is unable to move this turn.
                        if (canAttackNow || !canMoveNow)
                        {
                            var didSpecial = TryExecuteEnemySpecial(actor);
                            if (didSpecial)
                            {
                                RegisterEnemyAction();
                                if (TryConsumePendingSpecialTiles(actor.id, out var specialTiles))
                                {
                                    _showEnemyIntents = true;
                                    _activeEnemyIntentActorId = actor.id;
                                    _currentEnemyPlan = new EnemyPlan();
                                    _currentEnemyPlan.intents.Add(new EnemyIntent
                                    {
                                        actorId = actor.id,
                                        actorKind = actor.kind,
                                        kind = EnemyIntentKind.Special,
                                        from = actor.pos,
                                        to = actor.pos,
                                        attackSquares = specialTiles
                                    });
                                    DrawBoard();
                                    OnBoardChanged?.Invoke();
                                    yield return WaitEnemyStepDelay();
                                }

                                DrawBoard();
                                OnBoardChanged?.Invoke();
                                yield return WaitEnemyStepDelay();
                                continue;
                            }
                        }

                        if (!canMoveNow && !canAttackNow && !deferredOnce.Contains(actor.id))
                        {
                            deferredOnce.Add(actor.id);
                            actorQueue.Enqueue(actor.id);
                            continue;
                        }

                        // Hide intent lines while the next enemy starts moving.
                        _showEnemyIntents = false;
                        _activeEnemyIntentActorId = null;
                        DrawBoard();
                        OnBoardChanged?.Invoke();

                        if (canAttackNow)
                        {
                            // In MoveThenAttack mode, show only this actor intent while it attacks.
                            _showEnemyIntents = true;
                            _activeEnemyIntentActorId = actor.id;
                            _currentEnemyPlan = new EnemyPlan();
                            _currentEnemyPlan.intents.Add(new EnemyIntent
                            {
                                actorId = actor.id,
                                actorKind = actor.kind,
                                kind = EnemyIntentKind.Capture,
                                from = actor.pos,
                                to = actor.pos,
                                attackSquares = attackSquaresNow
                            });
                            DrawBoard();
                            OnBoardChanged?.Invoke();
                            var didAttack = ExecuteEnemyAttack(actor, attackSquaresNow);
                            if (didAttack)
                            {
                                RegisterEnemyAction();
                                yield return WaitEnemyStepDelay();
                            }
                            continue;
                        }

                        if (canMoveNow && CanPerformPieceMove(Faction.Enemy))
                        {
                            var from = actor.pos;
                            if (_board.Move(actor.id, it.to))
                            {
                                RegisterPieceMove(Faction.Enemy);
                                if (TryPromotePawn(actor)) ConsumeAllRemainingActions(actor);
                                OnUnitMoved?.Invoke(actor, from, actor.pos);
                                TriggerTraps();
                                SyncEnemyList();
                                TrimDeadEnemyIntents();
                                CheckWinLose();
                                RegisterEnemyAction();
                                DrawBoard();
                                OnBoardChanged?.Invoke();
                                yield return WaitEnemyStepDelay();
                            }
                            else
                            {
                                it.blocked = true;
                                it.to = actor.pos;
                            }
                        }
                    }
                }
                else
                {
                    // Pass 1: enemies attack from current positions (no pre-attack movement).
                    for (var i = 0; i < _currentEnemyPlan.intents.Count; i++)
                    {
                        if (!CanEnemyActNow()) break;
                        var it = _currentEnemyPlan.intents[i];
                        if (!_board.UnitsById.TryGetValue(it.actorId, out var actor)) continue;

                        var canMoveNow = it.to.row != actor.pos.row || it.to.col != actor.pos.col;
                        var immediateAttackSquares = _enemyIntentSystem.BuildImmediateAttackSquares(actor);
                        var hasImmediateAttack = immediateAttackSquares.Count > 0;

                        var didSpecial = false;
                        if (hasImmediateAttack || !canMoveNow)
                        {
                            didSpecial = TryExecuteEnemySpecial(actor);
                        }
                        if (didSpecial)
                        {
                            RegisterEnemyAction();
                            if (TryConsumePendingSpecialTiles(actor.id, out var specialTiles))
                            {
                                _showEnemyIntents = true;
                                _activeEnemyIntentActorId = it.actorId;
                                _currentEnemyPlan = new EnemyPlan();
                                _currentEnemyPlan.intents.Add(new EnemyIntent
                                {
                                    actorId = actor.id,
                                    actorKind = actor.kind,
                                    kind = EnemyIntentKind.Special,
                                    from = actor.pos,
                                    to = actor.pos,
                                    attackSquares = specialTiles
                                });
                                DrawBoard();
                                OnBoardChanged?.Invoke();
                                yield return WaitEnemyStepDelay();
                                continue;
                            }

                            DrawBoard();
                            OnBoardChanged?.Invoke();
                            yield return WaitEnemyStepDelay();
                            continue;
                        }

                        var didAttack = false;
                        if (it.attackSquares != null && it.attackSquares.Count > 0)
                        {
                            // Show only this enemy's intent while it is attacking.
                            if (ShouldShowEnemyIntents())
                            {
                                _showEnemyIntents = true;
                                _activeEnemyIntentActorId = it.actorId;
                                DrawBoard();
                                OnBoardChanged?.Invoke();
                            }
                            didAttack = ExecuteEnemyAttack(actor, it.attackSquares);
                        }

                        if (didAttack)
                        {
                            RegisterEnemyAction();
                            yield return WaitEnemyStepDelay();
                        }
                    }

                    // Pass 2: reposition after all attacks.
                    for (var i = 0; i < _currentEnemyPlan.intents.Count; i++)
                    {
                        if (!CanEnemyActNow()) break;
                        var it = _currentEnemyPlan.intents[i];
                        if (!_board.UnitsById.TryGetValue(it.actorId, out var actor)) continue;
                        if (it.to.row == actor.pos.row && it.to.col == actor.pos.col) continue;
                        if (!CanPerformPieceMove(Faction.Enemy)) continue;

                        var from = actor.pos;
                        if (_board.Move(actor.id, it.to))
                        {
                            RegisterPieceMove(Faction.Enemy);
                            if (TryPromotePawn(actor)) ConsumeAllRemainingActions(actor);
                            OnUnitMoved?.Invoke(actor, from, actor.pos);
                            TriggerTraps();
                            SyncEnemyList();
                            TrimDeadEnemyIntents();
                            CheckWinLose();
                            RegisterEnemyAction();
                            DrawBoard();
                            OnBoardChanged?.Invoke();
                            yield return WaitEnemyStepDelay();
                        }
                        else
                        {
                            it.blocked = true;
                            it.to = actor.pos;
                        }
                    }
                }
            }

            _showEnemyIntents = false;
            _activeEnemyIntentActorId = null;
            _pendingSpecialTilesByActorId.Clear();
            TriggerTraps();
            SyncEnemyList();
            TrimDeadEnemyIntents();
            DrawBoard();
            CheckWinLose();
            _enemyTurnRoutine = null;
            _turn.EndEnemyTurn();
        }

        private bool ExecuteEnemyAttack(UnitRuntime actor, List<GridPos> attackSquares)
        {
            if (actor == null) return false;
            if (actor.kind == UnitKind.Boar || actor.kind == UnitKind.Owl || actor.kind == UnitKind.Skunk)
            {
                // These units are special-only attackers.
                return false;
            }
            var squares = attackSquares ?? new List<GridPos>();
            var hits = new List<GridPos>();
            var totalDamageDealt = 0;
            GridPos? firstCaptureSquare = null;
            _pendingAttackSpecialByActorId.TryGetValue(actor.id, out var pendingSpecial);
            OnAttackStarted?.Invoke(actor, squares);

            if (actor.kind == UnitKind.Knight)
            {
                for (var s = 0; s < squares.Count; s++)
                {
                    var sq = squares[s];
                    if (!IsKnightLeap(actor.pos, sq)) continue;
                    if (!_board.Inside(sq)) continue;
                    var target = _board.At(sq);
                    if (!IsEnemyAttackableTarget(target)) continue;

                    var specialDamage = ResolveOutgoingDamage(actor);
                    _board.ApplyDamage(target.id, specialDamage);
                    OnDamageDealt?.Invoke(target.id, specialDamage);
                    totalDamageDealt += specialDamage;
                    if (pendingSpecial == EnemySpecialType.Sleep)
                    {
                        TryApplySleep(target, 1);
                    }
                    hits.Add(sq);

                    if (_board.At(sq) == null)
                    {
                        var from = actor.pos;
                        if (CanPerformPieceMove(Faction.Enemy) && _board.Move(actor.id, sq))
                        {
                            RegisterPieceMove(Faction.Enemy);
                            if (TryPromotePawn(actor)) ConsumeAllRemainingActions(actor);
                            OnUnitMoved?.Invoke(actor, from, sq);
                            TriggerTraps();
                        }
                    }
                    TryApplyRetaliation(target, actor);
                    break;
                }

                if (hits.Count > 0)
                {
                    if (pendingSpecial == EnemySpecialType.Rend && totalDamageDealt > 0)
                    {
                        actor.hp = Mathf.Min(actor.maxHp, actor.hp + totalDamageDealt);
                    }
                    _pendingAttackSpecialByActorId.Remove(actor.id);
                    OnAttackResolved?.Invoke(actor, hits);
                    DrawBoard();
                    OnBoardChanged?.Invoke();
                    return true;
                }
            }

            if (actor.kind == UnitKind.Owl)
            {
                for (var s = 0; s < squares.Count; s++)
                {
                    var sq = squares[s];
                    if (!_board.Inside(sq)) continue;
                    var target = _board.At(sq);
                    if (!IsEnemyAttackableTarget(target)) continue;

                    var dmg = ResolveOutgoingDamage(actor);
                    _board.ApplyDamage(target.id, dmg);
                    OnDamageDealt?.Invoke(target.id, dmg);
                    totalDamageDealt += dmg;
                    if (_board.At(sq) == null && !firstCaptureSquare.HasValue) firstCaptureSquare = sq;
                    if (pendingSpecial == EnemySpecialType.Sleep)
                    {
                        TryApplySleep(target, 1);
                    }
                    TryApplyRetaliation(target, actor);
                    hits.Add(sq);
                    break;
                }

                if (pendingSpecial == EnemySpecialType.Rend && totalDamageDealt > 0)
                {
                    actor.hp = Mathf.Min(actor.maxHp, actor.hp + totalDamageDealt);
                }
                _pendingAttackSpecialByActorId.Remove(actor.id);
                OnAttackResolved?.Invoke(actor, hits);
                if (firstCaptureSquare.HasValue &&
                    _board.UnitsById.ContainsKey(actor.id) &&
                    _board.At(firstCaptureSquare.Value) == null)
                {
                    var from = actor.pos;
                    if (CanPerformPieceMove(Faction.Enemy) && _board.Move(actor.id, firstCaptureSquare.Value))
                    {
                        RegisterPieceMove(Faction.Enemy);
                        if (TryPromotePawn(actor)) ConsumeAllRemainingActions(actor);
                        OnUnitMoved?.Invoke(actor, from, firstCaptureSquare.Value);
                        TriggerTraps();
                    }
                }
                DrawBoard();
                OnBoardChanged?.Invoke();
                return true;
            }

            if (actor.kind == UnitKind.Toad)
            {
                for (var s = 0; s < squares.Count; s++)
                {
                    var sq = squares[s];
                    if (!_board.Inside(sq)) continue;
                    var target = _board.At(sq);
                    if (!IsEnemyAttackableTarget(target)) continue;

                    var isAdjacent = IsAdjacentAll(actor.pos, target.pos);
                    var canPull = !isAdjacent &&
                                  IsOrthogonalClearLine(actor.pos, target.pos) &&
                                  (Mathf.Abs(actor.pos.row - target.pos.row) + Mathf.Abs(actor.pos.col - target.pos.col) >= 2);

                    var dmg = ResolveOutgoingDamage(actor);
                    _board.ApplyDamage(target.id, dmg);
                    OnDamageDealt?.Invoke(target.id, dmg);
                    totalDamageDealt += dmg;
                    if (pendingSpecial == EnemySpecialType.Sleep)
                    {
                        TryApplySleep(target, 1);
                    }

                    if (_board.UnitsById.TryGetValue(target.id, out var aliveTarget) && aliveTarget != null && canPull)
                    {
                        var step = DirectionStepToward(actor.pos, aliveTarget.pos);
                        var pullTo = new GridPos(aliveTarget.pos.row - step.row, aliveTarget.pos.col - step.col);
                        if (_board.Inside(pullTo) && !_board.Occupied(pullTo))
                        {
                            var from = aliveTarget.pos;
                            if (_board.Move(aliveTarget.id, pullTo))
                            {
                                OnUnitMoved?.Invoke(aliveTarget, from, pullTo);
                            }
                        }
                    }

                    TryApplyRetaliation(target, actor);
                    hits.Add(sq);
                    break;
                }

                if (pendingSpecial == EnemySpecialType.Rend && totalDamageDealt > 0)
                {
                    actor.hp = Mathf.Min(actor.maxHp, actor.hp + totalDamageDealt);
                }
                _pendingAttackSpecialByActorId.Remove(actor.id);
                OnAttackResolved?.Invoke(actor, hits);
                DrawBoard();
                OnBoardChanged?.Invoke();
                return true;
            }

            for (var s = 0; s < squares.Count; s++)
            {
                var sq = squares[s];
                if (!_board.Inside(sq)) continue;
                var target = _board.At(sq);
                if (!IsEnemyAttackableTarget(target)) continue;
                var dmg = ResolveOutgoingDamage(actor);
                _board.ApplyDamage(target.id, dmg);
                OnDamageDealt?.Invoke(target.id, dmg);
                totalDamageDealt += dmg;
                if (_board.At(sq) == null && !firstCaptureSquare.HasValue) firstCaptureSquare = sq;
                if (pendingSpecial == EnemySpecialType.Sleep)
                {
                    TryApplySleep(target, 1);
                }
                TryApplyRetaliation(target, actor);
                hits.Add(sq);
            }

            if (pendingSpecial == EnemySpecialType.Rend && totalDamageDealt > 0)
            {
                actor.hp = Mathf.Min(actor.maxHp, actor.hp + totalDamageDealt);
            }
            _pendingAttackSpecialByActorId.Remove(actor.id);
            OnAttackResolved?.Invoke(actor, hits);
            if (firstCaptureSquare.HasValue &&
                _board.UnitsById.ContainsKey(actor.id) &&
                _board.At(firstCaptureSquare.Value) == null)
            {
                var from = actor.pos;
                if (CanPerformPieceMove(Faction.Enemy) && _board.Move(actor.id, firstCaptureSquare.Value))
                {
                    RegisterPieceMove(Faction.Enemy);
                    if (TryPromotePawn(actor)) ConsumeAllRemainingActions(actor);
                    OnUnitMoved?.Invoke(actor, from, firstCaptureSquare.Value);
                    TriggerTraps();
                }
            }
            DrawBoard();
            OnBoardChanged?.Invoke();
            return true;
        }

        private static bool IsKnightLeap(GridPos from, GridPos to)
        {
            var dr = Mathf.Abs(to.row - from.row);
            var dc = Mathf.Abs(to.col - from.col);
            return (dr == 2 && dc == 1) || (dr == 1 && dc == 2);
        }

        private static bool IsAdjacentAll(GridPos from, GridPos to)
        {
            var dr = Mathf.Abs(to.row - from.row);
            var dc = Mathf.Abs(to.col - from.col);
            return dr <= 1 && dc <= 1 && (dr != 0 || dc != 0);
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

        private static void RemoveSelfTile(List<GridPos> list, GridPos self)
        {
            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].row == self.row && list[i].col == self.col) list.RemoveAt(i);
            }
        }

        private bool TryExecuteEnemySpecial(UnitRuntime actor)
        {
            if (actor == null) return false;
            var enemyDef = GetEnemyDefinition(actor.kind);
            var special = enemyDef != null ? enemyDef.special : null;
            if (special == null || special.type == EnemySpecialType.None) return false;

            var didApply = false;
            switch (special.type)
            {
                case EnemySpecialType.Shriek:
                    didApply = TryApplyShriek(actor, out var shriekTiles);
                    if (didApply) _pendingSpecialTilesByActorId[actor.id] = shriekTiles;
                    break;
                case EnemySpecialType.PackHowl:
                    didApply = TryApplyPackHowl(actor);
                    break;
                case EnemySpecialType.WebTrap:
                    didApply = TryApplyWebTrap(actor);
                    break;
                case EnemySpecialType.SuperLeap:
                    didApply = TryApplySuperLeap(actor);
                    break;
                case EnemySpecialType.StenchMissile:
                    didApply = TryApplyStenchMissile(actor, out var specialTiles);
                    if (didApply)
                    {
                        _pendingSpecialTilesByActorId[actor.id] = specialTiles;
                    }
                    break;
                case EnemySpecialType.Sleep:
                    didApply = TryApplyOwlSleepStrike(actor, out var sleepTiles);
                    if (didApply) _pendingSpecialTilesByActorId[actor.id] = sleepTiles;
                    break;
                case EnemySpecialType.Enrage:
                    actor.attack += Mathf.Max(1, special.amount);
                    didApply = true;
                    break;
                case EnemySpecialType.Rend:
                    _pendingAttackSpecialByActorId[actor.id] = EnemySpecialType.Rend;
                    didApply = true;
                    break;
                case EnemySpecialType.AlphaCall:
                    didApply = TryApplyAlphaCall(actor);
                    break;
                case EnemySpecialType.Ram:
                    didApply = TryApplyRam(actor, out var ramTiles);
                    if (didApply) _pendingSpecialTilesByActorId[actor.id] = ramTiles;
                    break;
            }

            if (!didApply) return false;
            OnSpecialStarted?.Invoke(actor);
            return true;
        }

        private bool TryConsumePendingSpecialTiles(string actorId, out List<GridPos> tiles)
        {
            tiles = null;
            if (string.IsNullOrEmpty(actorId)) return false;
            if (!_pendingSpecialTilesByActorId.TryGetValue(actorId, out var captured) || captured == null || captured.Count == 0) return false;
            tiles = new List<GridPos>(captured);
            _pendingSpecialTilesByActorId.Remove(actorId);
            return true;
        }

        private EnemyDefinition GetEnemyDefinition(UnitKind kind)
        {
            return _enemyDefinitions.TryGetValue(kind, out var def) ? def : null;
        }

        private int ResolveOutgoingDamage(UnitRuntime attacker, UnitRuntime target = null, bool isPlayerAttack = false)
        {
            if (attacker == null) return 0;
            var dmg = Mathf.Max(0, attacker.attack);
            if (attacker.status != null && attacker.status.nextAttackDamageModifier != 0)
            {
                dmg = Mathf.Max(0, dmg + attacker.status.nextAttackDamageModifier);
                // Positive modifiers are "next attack" buffs; negative values (weakness) expire by turn ticks.
                if (attacker.status.nextAttackDamageModifier > 0) attacker.status.nextAttackDamageModifier = 0;
            }
            if (attacker.faction == Faction.Player && HasTrinket("crown_lantern") && IsAdjacentToKing(attacker))
            {
                dmg += 1;
            }
            if (isPlayerAttack &&
                target != null &&
                target.faction == Faction.Enemy &&
                target.status != null &&
                target.status.rootedTurns > 0 &&
                HasTrinket("wire"))
            {
                dmg += 1;
            }
            if (attacker.faction == Faction.Player &&
                attacker.kind == UnitKind.Knight &&
                HasTrinket("knight_greaves") &&
                GetMovedDistanceThisTurn(attacker) >= 3)
            {
                dmg += 2;
            }
            return dmg;
        }

        private static bool IsEnemyAttackableTarget(UnitRuntime target)
        {
            if (target == null) return false;
            if (target.faction == Faction.Player) return true;
            return target.faction == Faction.Neutral &&
                   (target.kind == UnitKind.Rock || target.kind == UnitKind.BeaconTower);
        }

        private void TryApplyRetaliation(UnitRuntime defenderAtHitTime, UnitRuntime attacker)
        {
            if (defenderAtHitTime == null || attacker == null) return;
            if (defenderAtHitTime.status == null || defenderAtHitTime.status.retaliateDamage <= 0) return;
            if (!_board.UnitsById.ContainsKey(attacker.id)) return;
            if (defenderAtHitTime.kind == UnitKind.Rock &&
                defenderAtHitTime.faction == Faction.Neutral &&
                attacker.faction != Faction.Enemy)
            {
                return;
            }

            var dmg = Mathf.Max(1, defenderAtHitTime.status.retaliateDamage);
            defenderAtHitTime.status.retaliateDamage = 0;
            _board.ApplyDamage(attacker.id, dmg);
            OnDamageDealt?.Invoke(attacker.id, dmg);
        }

        private bool TryApplySleep(UnitRuntime target, int turns)
        {
            if (target == null || turns <= 0) return false;
            if (target.status == null) target.status = new UnitStatus();
            if (target.faction == Faction.Enemy && target.status.hasAwakened) return false;

            var beforeSleep = target.status.sleepingTurns;
            target.status.sleepingTurns = Mathf.Max(target.status.sleepingTurns, turns);
            if (target.status.sleepingTurns <= beforeSleep) return false;

            OnDebuffApplied?.Invoke(target.id, $"Sleep {target.status.sleepingTurns - beforeSleep}");
            return true;
        }

        private bool TryApplyShriek(UnitRuntime actor, out List<GridPos> affectedTiles)
        {
            affectedTiles = new List<GridPos>();
            if (!TryFindNearestPlayer(actor.pos, out var nearest)) return false;
            var dr = nearest.pos.row - actor.pos.row;
            var dc = nearest.pos.col - actor.pos.col;
            var didApply = false;

            if (Mathf.Abs(dr) >= Mathf.Abs(dc))
            {
                var step = dr >= 0 ? 1 : -1;
                for (var row = actor.pos.row + step; row >= 0 && row < _board.Size; row += step)
                {
                    var tile = new GridPos(row, actor.pos.col);
                    affectedTiles.Add(tile);
                    var u = _board.At(tile);
                    if (u == null || u.isStructure) continue;
                    u.status.nextAttackDamageModifier -= 1;
                    OnDebuffApplied?.Invoke(u.id, "-1 ATK");
                    didApply = true;
                }
            }
            else
            {
                var step = dc >= 0 ? 1 : -1;
                for (var col = actor.pos.col + step; col >= 0 && col < _board.Size; col += step)
                {
                    var tile = new GridPos(actor.pos.row, col);
                    affectedTiles.Add(tile);
                    var u = _board.At(tile);
                    if (u == null || u.isStructure) continue;
                    u.status.nextAttackDamageModifier -= 1;
                    OnDebuffApplied?.Invoke(u.id, "-1 ATK");
                    didApply = true;
                }
            }

            return didApply;
        }

        private bool TryApplyPackHowl(UnitRuntime actor)
        {
            var coyotes = new List<UnitRuntime>();
            foreach (var kv in _board.UnitsById)
            {
                var u = kv.Value;
                if (u.faction == Faction.Enemy && u.kind == UnitKind.Coyote) coyotes.Add(u);
            }

            if (coyotes.Count < 2) return false;
            for (var i = 0; i < coyotes.Count; i++) coyotes[i].status.nextAttackDamageModifier += 1;
            return true;
        }

        private bool TryApplyWebTrap(UnitRuntime actor)
        {
            if (!TryFindNearestPlayer(actor.pos, out var nearest)) return false;
            _board.ApplyDamage(nearest.id, 1);
            nearest.status.rootedTurns = Mathf.Max(nearest.status.rootedTurns, 1);
            OnDamageDealt?.Invoke(nearest.id, 1);
            OnDebuffApplied?.Invoke(nearest.id, "Rooted 1");
            return true;
        }

        private bool TryApplySuperLeap(UnitRuntime actor)
        {
            var didHit = false;
            for (var dr = -1; dr <= 1; dr++)
            {
                for (var dc = -1; dc <= 1; dc++)
                {
                    if (dr == 0 && dc == 0) continue;
                    var p = new GridPos(actor.pos.row + dr, actor.pos.col + dc);
                    if (!_board.Inside(p)) continue;
                    var u = _board.At(p);
                    if (u == null || u.faction == Faction.Enemy || u.faction == Faction.Neutral) continue;
                    _board.ApplyDamage(u.id, 1);
                    OnDamageDealt?.Invoke(u.id, 1);
                    didHit = true;
                }
            }
            return didHit;
        }

        private bool TryApplyOwlSleepStrike(UnitRuntime actor, out List<GridPos> affectedTiles)
        {
            affectedTiles = new List<GridPos>();
            if (actor == null) return false;
            if (!TryFindNearestPlayer(actor.pos, out var nearest) || nearest == null) return false;

            var step = DirectionStepToward(actor.pos, nearest.pos);
            if (step.row == 0 && step.col == 0) return false;

            var probe = new GridPos(actor.pos.row + step.row, actor.pos.col + step.col);
            var didHit = false;
            while (_board.Inside(probe))
            {
                affectedTiles.Add(probe);
                var target = _board.At(probe);
                if (!IsEnemyAttackableTarget(target))
                {
                    if (target != null) break;
                    probe = new GridPos(probe.row + step.row, probe.col + step.col);
                    continue;
                }

                var dmg = Mathf.Max(1, ResolveOutgoingDamage(actor));
                _board.ApplyDamage(target.id, dmg);
                OnDamageDealt?.Invoke(target.id, dmg);
                if (_board.UnitsById.TryGetValue(target.id, out var aliveTarget) && aliveTarget != null)
                {
                    TryApplySleep(aliveTarget, 1);
                }
                didHit = true;
                break;
            }

            // Only count this special as executed when it actually connects with a valid target.
            return didHit;
        }

        private bool TryApplyStenchMissile(UnitRuntime actor, out List<GridPos> affectedTiles)
        {
            affectedTiles = new List<GridPos>();
            if (actor == null || _board.Size < 2) return false;

            var maxRow = _board.Size - 2;
            var maxCol = _board.Size - 2;
            var origin = new GridPos(Random.Range(0, maxRow + 1), Random.Range(0, maxCol + 1));
            var toxicDamage = 1;
            for (var r = 0; r < 2; r++)
            {
                for (var c = 0; c < 2; c++)
                {
                    var p = new GridPos(origin.row + r, origin.col + c);
                    if (!_board.Inside(p)) continue;
                    affectedTiles.Add(p);
                    AddOrRefreshToxicZone(p, toxicDamage, 1);
                }
            }

            return affectedTiles.Count > 0;
        }

        private bool TryApplyAlphaCall(UnitRuntime actor)
        {
            var movedAny = false;
            var coyoteIds = new List<string>();
            foreach (var kv in _board.UnitsById)
            {
                var u = kv.Value;
                if (u.faction == Faction.Enemy && u.kind == UnitKind.Coyote) coyoteIds.Add(u.id);
            }

            for (var i = 0; i < coyoteIds.Count; i++)
            {
                if (!CanPerformPieceMove(Faction.Enemy)) break;
                if (!_board.UnitsById.TryGetValue(coyoteIds[i], out var coyote)) continue;
                if (!TryFindNearestPlayer(coyote.pos, out var nearest)) continue;
                var step = DirectionStepToward(coyote.pos, nearest.pos);
                var to = new GridPos(coyote.pos.row + step.row, coyote.pos.col + step.col);
                if (!_board.Inside(to) || _board.Occupied(to)) continue;
                var from = coyote.pos;
                if (_board.Move(coyote.id, to))
                {
                    RegisterPieceMove(Faction.Enemy);
                    OnUnitMoved?.Invoke(coyote, from, to);
                    movedAny = true;
                }
            }

            return movedAny;
        }

        private bool TryApplyRam(UnitRuntime actor, out List<GridPos> affectedTiles)
        {
            affectedTiles = new List<GridPos>();
            if (actor == null) return false;
            if (!TryFindNearestPlayer(actor.pos, out var nearest) || nearest == null) return false;

            var step = DirectionStepToward(actor.pos, nearest.pos);
            if (step.row == 0 && step.col == 0) return false;

            UnitRuntime target = null;
            var probe = new GridPos(actor.pos.row + step.row, actor.pos.col + step.col);
            while (_board.Inside(probe))
            {
                var hit = _board.At(probe);
                if (hit == null)
                {
                    probe = new GridPos(probe.row + step.row, probe.col + step.col);
                    continue;
                }

                if (!IsEnemyAttackableTarget(hit)) return false;
                target = hit;
                break;
            }

            if (target == null) return false;
            affectedTiles.Add(target.pos);

            var behind = new GridPos(target.pos.row + step.row, target.pos.col + step.col);
            var behindInside = _board.Inside(behind);
            var behindUnit = behindInside ? _board.At(behind) : null;

            if (behindInside && behindUnit == null)
            {
                var targetFrom = target.pos;
                if (!_board.Move(target.id, behind)) return false;
                affectedTiles.Add(behind);
                OnUnitMoved?.Invoke(target, targetFrom, behind);

                var actorFrom = actor.pos;
                if (_board.Move(actor.id, targetFrom))
                {
                    RegisterPieceMove(Faction.Enemy);
                    OnUnitMoved?.Invoke(actor, actorFrom, targetFrom);
                    TriggerTraps();
                }

                _board.ApplyDamage(target.id, 1);
                OnDamageDealt?.Invoke(target.id, 1);
                return true;
            }

            OnUnitBumped?.Invoke(target.id, step);
            _board.ApplyDamage(target.id, 2);
            OnDamageDealt?.Invoke(target.id, 2);

            var actorFront = new GridPos(target.pos.row - step.row, target.pos.col - step.col);
            var actorCanAdvance =
                _board.Inside(actorFront) &&
                !(_board.Occupied(actorFront) && (actorFront.row != actor.pos.row || actorFront.col != actor.pos.col));
            if (actorCanAdvance && (actorFront.row != actor.pos.row || actorFront.col != actor.pos.col))
            {
                var actorFrom = actor.pos;
                if (_board.Move(actor.id, actorFront))
                {
                    RegisterPieceMove(Faction.Enemy);
                    OnUnitMoved?.Invoke(actor, actorFrom, actorFront);
                    TriggerTraps();
                }
            }

            if (behindUnit != null)
            {
                affectedTiles.Add(behind);
                _board.ApplyDamage(behindUnit.id, 1);
                OnDamageDealt?.Invoke(behindUnit.id, 1);
            }
            return true;
        }

        private bool TryFindNearestPlayer(GridPos from, out UnitRuntime nearest)
        {
            nearest = null;
            var best = int.MaxValue;
            foreach (var kv in _board.UnitsById)
            {
                var u = kv.Value;
                if (u.faction != Faction.Player) continue;
                var d = Mathf.Abs(u.pos.row - from.row) + Mathf.Abs(u.pos.col - from.col);
                if (d >= best) continue;
                best = d;
                nearest = u;
            }
            return nearest != null;
        }

        private static GridPos DirectionStepToward(GridPos from, GridPos to)
        {
            var dr = to.row - from.row;
            var dc = to.col - from.col;
            if (Mathf.Abs(dr) >= Mathf.Abs(dc))
            {
                return new GridPos(dr == 0 ? 0 : (dr > 0 ? 1 : -1), 0);
            }
            return new GridPos(0, dc == 0 ? 0 : (dc > 0 ? 1 : -1));
        }

        private WaitForSeconds WaitEnemyStepDelay()
        {
            return new WaitForSeconds(Mathf.Max(0f, enemyStepDelaySeconds));
        }

        private void ResetPlayerActions()
        {
            var moveBonus = _session != null ? Mathf.Max(0, _session.GetPlayerMoveBonusPerTurn()) : 0;
            foreach (var kv in _board.UnitsById)
            {
                var u = kv.Value;
                if (u.faction != Faction.Player) continue;
                if (u.isStructure) continue;
                var sleeping = u.status != null && u.status.IsSleeping;
                var rooted = u.status != null && u.status.IsRooted;
                if (_enemyTurnActionOrder == EnemyTurnActionOrder.MoveThenAttack)
                {
                    // Chess-like mode: one action total, either move or attack.
                    u.moveActionsRemaining = sleeping || rooted ? 0 : 1;
                    u.attackActionsRemaining = sleeping ? 0 : 1;
                }
                else
                {
                    u.moveActionsRemaining = sleeping || rooted ? 0 : 1 + moveBonus;
                    u.attackActionsRemaining = sleeping ? 0 : 1;
                }
                u.canMove = u.moveActionsRemaining > 0;
                u.canAttack = u.attackActionsRemaining > 0;
            }
        }

        private void RebuildIntents()
        {
            _currentEnemyPlan = _enemyIntentSystem.BuildPlan();
            _showEnemyIntents = ShouldShowEnemyIntents();
            _activeEnemyIntentActorId = null;
            OnIntentsChanged?.Invoke();
        }

        private bool ShouldShowEnemyIntents()
        {
            return _allowEnemyIntentLines && _enemyTurnActionOrder == EnemyTurnActionOrder.AttackThenMove;
        }

        private static EnemyIntent FindIntentByActorId(EnemyPlan plan, string actorId)
        {
            if (plan == null || string.IsNullOrEmpty(actorId)) return null;
            for (var i = 0; i < plan.intents.Count; i++)
            {
                var it = plan.intents[i];
                if (it != null && it.actorId == actorId) return it;
            }
            return null;
        }

        private void RecomputeSelectionHighlights()
        {
            _moveHighlights.Clear();
            _attackHighlights.Clear();
            _specialAttackHighlights.Clear();
            if (SelectedUnit == null) return;

            if (SelectedUnit.faction == Faction.Player && _turn.Phase == TurnPhase.Player)
            {
                if (CanUnitMoveNow(SelectedUnit) && CanPerformPieceMove(Faction.Player))
                {
                    var moves = ComputePlayerMoveTiles(SelectedUnit, SelectedUnit.pos);
                    for (var i = 0; i < moves.Count; i++) _moveHighlights.Add(Key(moves[i]));
                }

                if (CanUnitAttackNow(SelectedUnit))
                {
                    BuildPlayerAttackHighlights(SelectedUnit, _attackHighlights, _specialAttackHighlights);
                }
                return;
            }

            if (SelectedUnit.faction == Faction.Enemy)
            {
                var moves = ComputeEnemyMoveTiles(SelectedUnit, SelectedUnit.pos);
                for (var i = 0; i < moves.Count; i++) _moveHighlights.Add(Key(moves[i]));

                var atks = ComputeEnemyAttackTiles(SelectedUnit);
                for (var i = 0; i < atks.Count; i++)
                {
                    if (_board.Inside(atks[i])) _attackHighlights.Add(Key(atks[i]));
                }
            }
        }

        private bool CanPerformPieceMove(Faction faction)
        {
            if (maxPieceMovesPerSidePerTurn <= 0) return true;
            var movesThisTurn = faction == Faction.Player ? _playerPieceMovesThisTurn : _enemyPieceMovesThisTurn;
            return movesThisTurn < maxPieceMovesPerSidePerTurn;
        }

        private void RegisterPieceMove(Faction faction)
        {
            if (faction == Faction.Player) _playerPieceMovesThisTurn += 1;
            else if (faction == Faction.Enemy) _enemyPieceMovesThisTurn += 1;
            EnforceMoveLimitForFaction(faction);
        }

        private void EnforceMoveLimitForFaction(Faction faction)
        {
            if (CanPerformPieceMove(faction)) return;
            foreach (var kv in _board.UnitsById)
            {
                var unit = kv.Value;
                if (unit == null || unit.faction != faction || unit.isStructure) continue;
                unit.moveActionsRemaining = 0;
                unit.canMove = false;
            }
        }

        private void BuildPlayerAttackHighlights(UnitRuntime attacker, HashSet<string> normal, HashSet<string> special)
        {
            if (attacker == null || normal == null || special == null) return;
            if (attacker.kind == UnitKind.WolfAlpha) return;

            foreach (var kv in _board.UnitsById)
            {
                var target = kv.Value;
                if (!IsPlayerHostile(target)) continue;

                var isAdjacent = IsAdjacentAll(attacker.pos, target.pos);
                switch (attacker.kind)
                {
                    case UnitKind.Bat:
                        if (isAdjacent) normal.Add(Key(target.pos));
                        else if (IsOrthogonalClearLine(attacker.pos, target.pos) &&
                                 Mathf.Abs(attacker.pos.row - target.pos.row) + Mathf.Abs(attacker.pos.col - target.pos.col) >= 2)
                        {
                            special.Add(Key(target.pos));
                        }
                        break;
                    case UnitKind.Bear:
                        if (CanPlayerNormalAttackHit(attacker, target)) normal.Add(Key(target.pos));
                        break;
                    case UnitKind.Owl:
                        if (CanOwlSpecialTarget(attacker, target)) special.Add(Key(target.pos));
                        break;
                    case UnitKind.Boar:
                        if (HasBoarRunningSpace(attacker.pos, target.pos)) special.Add(Key(target.pos));
                        break;
                    case UnitKind.Coyote:
                        if (CanPlayerNormalAttackHit(attacker, target)) normal.Add(Key(target.pos));
                        break;
                    case UnitKind.Skunk:
                        if (!isAdjacent) special.Add(Key(target.pos));
                        break;
                    case UnitKind.Spider:
                        if (isAdjacent) normal.Add(Key(target.pos));
                        else special.Add(Key(target.pos));
                        break;
                    case UnitKind.Toad:
                        if (IsOrthogonalClearLine(attacker.pos, target.pos) &&
                            Mathf.Abs(attacker.pos.row - target.pos.row) + Mathf.Abs(attacker.pos.col - target.pos.col) >= 2)
                        {
                            special.Add(Key(target.pos));
                        }
                        break;
                    default:
                        if (CanPlayerNormalAttackHit(attacker, target)) normal.Add(Key(target.pos));
                        break;
                }
            }

            if (attacker.kind == UnitKind.Coyote)
            {
                var hasAdjacentCoyote = false;
                foreach (var kv in _board.UnitsById)
                {
                    var other = kv.Value;
                    if (other == null || other.id == attacker.id || other.kind != UnitKind.Coyote) continue;
                    if (!IsAdjacentAll(attacker.pos, other.pos)) continue;
                    hasAdjacentCoyote = true;
                    break;
                }

                if (hasAdjacentCoyote) special.Add(Key(attacker.pos));
            }
        }

        private void RebuildAttackMarkerPreviewHighlights()
        {
            _attackMarkerPreviewHighlights.Clear();
            if (SelectedUnit == null || SelectedUnit.faction != Faction.Player) return;
            if (!IsAnimalUnit(SelectedUnit.kind)) return;
            if (_turn == null || _turn.Phase != TurnPhase.Player) return;
            if (!CanUnitAttackNow(SelectedUnit)) return;
            if (!_hoveredTargetTile.HasValue) return;

            var hovered = _hoveredTargetTile.Value;
            var hoveredKey = Key(hovered);
            var isSpecial = _specialAttackHighlights.Contains(hoveredKey);
            if (!isSpecial && !_attackHighlights.Contains(hoveredKey)) return;

            var previewTiles = BuildPlayerActionPreviewTiles(SelectedUnit, hovered, isSpecial);
            for (var i = 0; i < previewTiles.Count; i++)
            {
                var p = previewTiles[i];
                if (_board.Inside(p)) _attackMarkerPreviewHighlights.Add(Key(p));
            }
        }

        private List<GridPos> BuildPlayerActionPreviewTiles(UnitRuntime attacker, GridPos clicked, bool special)
        {
            var outTiles = new List<GridPos>();
            if (attacker == null) return outTiles;

            if (!special)
            {
                return ComputePlayerAttackTiles(attacker, attacker.pos, clicked);
            }

            switch (attacker.kind)
            {
                case UnitKind.Bat:
                {
                    var step = DirectionStepExtended(attacker.pos, clicked, false, true);
                    if (step.row == 0 && step.col == 0) return outTiles;
                    var probe = new GridPos(attacker.pos.row + step.row * 2, attacker.pos.col + step.col * 2);
                    while (_board.Inside(probe))
                    {
                        outTiles.Add(probe);
                        probe = new GridPos(probe.row + step.row, probe.col + step.col);
                    }
                    return outTiles;
                }
                case UnitKind.Coyote:
                {
                    foreach (var kv in _board.UnitsById)
                    {
                        var unit = kv.Value;
                        if (unit != null && unit.kind == UnitKind.Coyote) outTiles.Add(unit.pos);
                    }
                    return outTiles;
                }
                case UnitKind.Boar:
                {
                    outTiles.Add(clicked);
                    var step = DirectionStepToward(attacker.pos, clicked);
                    if (step.row != 0 || step.col != 0)
                    {
                        outTiles.Add(new GridPos(clicked.row + step.row, clicked.col + step.col));
                    }
                    return outTiles;
                }
                case UnitKind.Owl:
                {
                    var step = DirectionStepToward(attacker.pos, clicked);
                    if (step.row == 0 && step.col == 0) return outTiles;
                    var probe = new GridPos(attacker.pos.row + step.row, attacker.pos.col + step.col);
                    while (_board.Inside(probe))
                    {
                        outTiles.Add(probe);
                        if (_board.Occupied(probe)) break;
                        probe = new GridPos(probe.row + step.row, probe.col + step.col);
                    }
                    return outTiles;
                }
                case UnitKind.Skunk:
                {
                    var origin = new GridPos(
                        Mathf.Clamp(clicked.row, 0, Mathf.Max(0, _board.Size - 2)),
                        Mathf.Clamp(clicked.col, 0, Mathf.Max(0, _board.Size - 2)));
                    for (var r = 0; r < 2; r++)
                        for (var c = 0; c < 2; c++)
                            outTiles.Add(new GridPos(origin.row + r, origin.col + c));
                    return outTiles;
                }
                case UnitKind.Spider:
                case UnitKind.Toad:
                    outTiles.Add(clicked);
                    return outTiles;
                default:
                    outTiles.Add(clicked);
                    return outTiles;
            }
        }

        private bool CanPlayerNormalAttackHit(UnitRuntime attacker, UnitRuntime target)
        {
            if (attacker == null || target == null) return false;
            var tiles = ComputePlayerAttackTiles(attacker, attacker.pos, target.pos);
            for (var i = 0; i < tiles.Count; i++)
            {
                var sq = tiles[i];
                if (sq.row == target.pos.row && sq.col == target.pos.col) return true;
            }
            return false;
        }

        private bool IsPlayerHostile(UnitRuntime unit)
        {
            if (unit == null) return false;
            if (unit.faction == Faction.Enemy) return true;
            return unit.faction == Faction.Neutral &&
                   (unit.kind == UnitKind.Rock || unit.kind == UnitKind.Cave);
        }

        private bool IsOrthogonalClearLine(GridPos from, GridPos to)
        {
            var step = DirectionStepExtended(from, to, false, true);
            if (step.row == 0 && step.col == 0) return false;
            var probe = new GridPos(from.row + step.row, from.col + step.col);
            while (_board.Inside(probe))
            {
                if (probe.row == to.row && probe.col == to.col) return true;
                if (_board.Occupied(probe)) return false;
                probe = new GridPos(probe.row + step.row, probe.col + step.col);
            }
            return false;
        }

        private bool HasBoarRunningSpace(GridPos from, GridPos target)
        {
            if (!IsOrthogonalClearLine(from, target)) return false;
            var distance = Mathf.Abs(from.row - target.row) + Mathf.Abs(from.col - target.col);
            return distance >= 2;
        }

        private bool CanOwlSpecialTarget(UnitRuntime owl, UnitRuntime target)
        {
            if (owl == null || target == null) return false;
            if (!IsPlayerHostile(target)) return false;
            if (!IsOrthogonalClearLine(owl.pos, target.pos)) return false;
            var step = DirectionStepToward(owl.pos, target.pos);
            var probe = new GridPos(owl.pos.row + step.row, owl.pos.col + step.col);
            while (_board.Inside(probe))
            {
                var hit = _board.At(probe);
                if (hit == null)
                {
                    probe = new GridPos(probe.row + step.row, probe.col + step.col);
                    continue;
                }

                return hit.id == target.id;
            }

            return false;
        }

        private List<GridPos> ComputePlayerMoveTiles(UnitRuntime piece, GridPos from)
        {
            if (piece != null && IsAnimalUnit(piece.kind))
            {
                return ComputeEnemyMoveTiles(piece, from);
            }

            var outTiles = new List<GridPos>();
            void TryAdd(GridPos p)
            {
                if (_board.Inside(p) && !_board.Occupied(p)) outTiles.Add(p);
            }
            var bonusRange = GetPlayerMovementRangeBonus(piece);

            switch (piece.kind)
            {
                case UnitKind.Pawn:
                    if (IsPromotedPawn(piece))
                    {
                        AddKingMovesWithRange(from, Mathf.Max(1, 1 + bonusRange), TryAdd);
                    }
                    else
                    {
                        AddForwardMoves(from, -1, Mathf.Max(1, 1 + bonusRange), TryAdd);
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
                    AddKingMovesWithRange(from, Mathf.Max(1, 1 + bonusRange), TryAdd);
                    break;
                default:
                    AddOrthogonalMovesWithRange(from, Mathf.Max(1, 1 + bonusRange), TryAdd);
                    break;
            }
            return outTiles;
        }

        private List<GridPos> ComputePlayerAttackTiles(UnitRuntime piece, GridPos from, GridPos? preferredTarget)
        {
            if (piece != null && IsAnimalUnit(piece.kind))
            {
                var focus = preferredTarget ?? ResolveNearestHostilePosition(piece);
                return ComputeEnemyStyleAttackTiles(piece, from, focus);
            }

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
                    AddKnightCaptureTargets(from, piece.faction, outTiles);
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

        private List<GridPos> ComputeEnemyStyleAttackTiles(UnitRuntime unit, GridPos from, GridPos preferredTarget)
        {
            return PieceAttackRules.ComputeAttackSquares(_board, unit, from, preferredTarget);
        }

        private List<GridPos> ComputeEnemyMoveTiles(UnitRuntime piece, GridPos from)
        {
            var outTiles = new List<GridPos>();
            void TryAdd(GridPos p)
            {
                if (_board.Inside(p) && !_board.Occupied(p)) outTiles.Add(p);
            }

            switch (piece.kind)
            {
                case UnitKind.Pawn:
                    TryAdd(new GridPos(from.row + 1, from.col));
                    break;
                case UnitKind.King:
                    AddAdjacentAllMoves(from, TryAdd);
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
                default:
                    AddEnemyStepMoves(from, ResolveEnemyStepRange(piece.kind), outTiles);
                    break;
            }

            return outTiles;
        }

        private List<GridPos> ComputeEnemyAttackTiles(UnitRuntime piece)
        {
            var immediate = _enemyIntentSystem.BuildImmediateAttackSquares(piece);
            if (immediate != null && immediate.Count > 0) return immediate;
            return _enemyIntentSystem.BuildAttackPatternTowardNearest(piece);
        }

        private void AddEnemyStepMoves(GridPos from, int range, List<GridPos> outTiles)
        {
            if (range <= 0) return;
            var frontier = new List<GridPos> { from };
            var visited = new HashSet<string> { Key(from) };

            for (var step = 0; step < range; step++)
            {
                var next = new List<GridPos>();
                for (var i = 0; i < frontier.Count; i++)
                {
                    var p = frontier[i];
                    var neighbors = new[]
                    {
                        new GridPos(p.row + 1, p.col),
                        new GridPos(p.row - 1, p.col),
                        new GridPos(p.row, p.col + 1),
                        new GridPos(p.row, p.col - 1)
                    };

                    for (var n = 0; n < neighbors.Length; n++)
                    {
                        var q = neighbors[n];
                        var k = Key(q);
                        if (visited.Contains(k)) continue;
                        visited.Add(k);
                        if (!_board.Inside(q) || _board.Occupied(q)) continue;
                        outTiles.Add(q);
                        next.Add(q);
                    }
                }
                frontier = next;
                if (frontier.Count == 0) break;
            }
        }

        private static int ResolveEnemyStepRange(UnitKind kind)
        {
            switch (kind)
            {
                case UnitKind.Coyote: return 2;
                case UnitKind.Bat: return 1;
                default: return 1;
            }
        }

        private static bool IsMelee(UnitKind kind)
        {
            return kind == UnitKind.Pawn || kind == UnitKind.Knight || kind == UnitKind.King;
        }

        private static bool IsSpawnableEnemyKind(UnitKind kind)
        {
            return kind != UnitKind.Spider &&
                   kind != UnitKind.WolfPup;
        }

        private static bool IsChessEnemyKind(UnitKind kind)
        {
            return kind == UnitKind.Pawn ||
                   kind == UnitKind.Knight ||
                   kind == UnitKind.Bishop ||
                   kind == UnitKind.Rook ||
                   kind == UnitKind.Queen ||
                   kind == UnitKind.King;
        }

        private UnitKind SanitizeEnemyKindForEncounter(UnitKind kind)
        {
            var sanitized = IsSpawnableEnemyKind(kind) ? kind : UnitKind.Snake;
            if (_chessOnlyEncounter && !IsChessEnemyKind(sanitized)) return UnitKind.Pawn;
            return sanitized;
        }

        private static bool IsAnimalUnit(UnitKind kind)
        {
            switch (kind)
            {
                case UnitKind.Bat:
                case UnitKind.Coyote:
                case UnitKind.Owl:
                case UnitKind.Boar:
                case UnitKind.Snake:
                case UnitKind.Spider:
                case UnitKind.Skunk:
                case UnitKind.WolfAlpha:
                case UnitKind.Bear:
                case UnitKind.Toad:
                    return true;
                default:
                    return false;
            }
        }

        private GridPos ResolveNearestHostilePosition(UnitRuntime unit)
        {
            if (unit == null) return default;
            UnitRuntime nearest = null;
            var best = int.MaxValue;
            foreach (var kv in _board.UnitsById)
            {
                var other = kv.Value;
                if (other == null || other.id == unit.id) continue;
                if (other.faction == unit.faction) continue;
                if (other.faction == Faction.Neutral && other.kind != UnitKind.Rock) continue;
                var d = Mathf.Abs(unit.pos.row - other.pos.row) + Mathf.Abs(unit.pos.col - other.pos.col);
                if (d >= best) continue;
                best = d;
                nearest = other;
            }

            return nearest != null ? nearest.pos : unit.pos;
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

        private void AddSingleTargetOrAdjacentAttack(GridPos from, GridPos preferredTarget, List<GridPos> outSquares)
        {
            if (IsAdjacentAll(from, preferredTarget))
            {
                outSquares.Add(preferredTarget);
                return;
            }

            AddAdjacentAttack(from, outSquares);
        }

        private static void AddPawnDiagonalAttack(GridPos from, GridPos preferredTarget, List<GridPos> outSquares)
        {
            var dr = preferredTarget.row - from.row;
            var forward = dr == 0 ? 1 : (dr > 0 ? 1 : -1);
            outSquares.Add(new GridPos(from.row + forward, from.col - 1));
            outSquares.Add(new GridPos(from.row + forward, from.col + 1));
        }

        private void AddKnightAttackSquares(GridPos from, GridPos preferredTarget, Faction attackerFaction, List<GridPos> outSquares)
        {
            if (IsKnightLeap(from, preferredTarget))
            {
                var target = _board.At(preferredTarget);
                if (target != null && target.faction != attackerFaction && target.faction != Faction.Neutral)
                {
                    outSquares.Add(preferredTarget);
                    return;
                }
            }

            AddKnightCaptureTargets(from, attackerFaction, outSquares);
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

        private bool IsPromotedPawn(UnitRuntime unit)
        {
            return unit != null && unit.kind == UnitKind.Pawn && unit.status != null && unit.status.pawnPromoted;
        }

        private bool TryPromotePawn(UnitRuntime unit)
        {
            if (unit == null || unit.kind != UnitKind.Pawn) return false;
            if (unit.status == null) unit.status = new UnitStatus();
            if (unit.status.pawnPromoted) return false;

            var promotionRow = unit.faction == Faction.Player ? 0 : _board.Size - 1;
            if (unit.pos.row != promotionRow) return false;

            unit.kind = UnitKind.Queen;
            unit.status.pawnPromoted = false;
            ApplyKindStats(unit, UnitKind.Queen);
            if (unit.faction == Faction.Player && HasTrinket("last_rank_medal"))
            {
                unit.status.shieldCharge += 4;
                unit.status.nextAttackDamageModifier += 1;
            }
            OnEncounterMessage?.Invoke($"{unit.id} promoted to Queen.");
            return true;
        }

        private void ConsumeAllRemainingActions(UnitRuntime unit)
        {
            if (unit == null) return;
            unit.moveActionsRemaining = 0;
            unit.attackActionsRemaining = 0;
            unit.canMove = false;
            unit.canAttack = false;
        }

        private void ApplyKindStats(UnitRuntime unit, UnitKind promotedKind)
        {
            if (unit == null) return;
            if (unit.faction == Faction.Player)
            {
                if (_pieces.TryGetValue(promotedKind, out var pieceDef) && pieceDef != null)
                {
                    unit.maxHp = Mathf.Max(1, pieceDef.maxHp);
                    unit.attack = Mathf.Max(0, pieceDef.attack);
                }
            }
            else if (unit.faction == Faction.Enemy)
            {
                if (_enemyDefinitions.TryGetValue(promotedKind, out var enemyDef) && enemyDef != null)
                {
                    unit.maxHp = Mathf.Max(1, enemyDef.maxHp);
                    unit.attack = Mathf.Max(0, enemyDef.attack);
                }
                else if (_pieces.TryGetValue(promotedKind, out var pieceDef) && pieceDef != null)
                {
                    unit.maxHp = Mathf.Max(1, pieceDef.maxHp);
                    unit.attack = Mathf.Max(0, pieceDef.attack);
                }
            }

            unit.hp = Mathf.Clamp(unit.hp, 1, unit.maxHp);
        }

        private void MarkPlayerMoveActionUsed()
        {
            if (!_marchingBannerUsedThisTurn && HasTrinket("marching_banner"))
            {
                _marchingBannerUsedThisTurn = true;
            }
        }

        private int GetPlayerMovementRangeBonus(UnitRuntime piece)
        {
            if (piece == null || piece.faction != Faction.Player) return 0;
            var bonus = 0;
            if (!_marchingBannerUsedThisTurn && HasTrinket("marching_banner"))
            {
                bonus += 1;
            }

            if (!string.IsNullOrEmpty(piece.id) &&
                _forcedMarchRangeBonusByUnitThisTurn.TryGetValue(piece.id, out var forcedMarchBonus))
            {
                bonus += Mathf.Max(0, forcedMarchBonus);
            }

            return bonus;
        }

        private void ApplyEncounterStartTrinketEffects()
        {
            if (!HasTrinket("royal_plate")) return;
            var king = FindKing();
            if (king == null) return;
            king.status.shieldCharge += 1;
        }

        private void ApplyEncounterEndTrinketGoldBonus()
        {
            if (_session == null) return;
            var purseCount = GetOwnedTrinketCount("purse");
            if (purseCount <= 0) return;
            _session.AddGold(15 * purseCount);
        }

        private int GetOwnedTrinketCount(string trinketId)
        {
            if (_session == null || _session.OwnedTrinkets == null || string.IsNullOrWhiteSpace(trinketId)) return 0;
            var wanted = trinketId.Trim();
            var count = 0;
            for (var i = 0; i < _session.OwnedTrinkets.Count; i++)
            {
                var trinket = _session.OwnedTrinkets[i];
                if (trinket == null || string.IsNullOrWhiteSpace(trinket.trinketId)) continue;
                if (!string.Equals(trinket.trinketId.Trim(), wanted, System.StringComparison.OrdinalIgnoreCase)) continue;
                count += 1;
            }
            return count;
        }

        private bool HasTrinket(string trinketId)
        {
            return GetOwnedTrinketCount(trinketId) > 0;
        }

        private bool IsAdjacentToKing(UnitRuntime unit)
        {
            if (unit == null || unit.faction != Faction.Player || unit.kind == UnitKind.King) return false;
            var king = FindKing();
            if (king == null) return false;
            var dr = Mathf.Abs(unit.pos.row - king.pos.row);
            var dc = Mathf.Abs(unit.pos.col - king.pos.col);
            return dr <= 1 && dc <= 1 && (dr != 0 || dc != 0);
        }

        private void AddForwardMoves(GridPos from, int rowDirection, int range, System.Action<GridPos> add)
        {
            if (add == null) return;
            var maxRange = Mathf.Max(1, range);
            for (var step = 1; step <= maxRange; step++)
            {
                var tile = new GridPos(from.row + rowDirection * step, from.col);
                if (!_board.Inside(tile) || _board.Occupied(tile)) break;
                add(tile);
            }
        }

        private static void AddOrthogonalMovesWithRange(GridPos from, int range, System.Action<GridPos> add)
        {
            if (add == null) return;
            var maxRange = Mathf.Max(1, range);
            for (var d = 1; d <= maxRange; d++)
            {
                add(new GridPos(from.row + d, from.col));
                add(new GridPos(from.row - d, from.col));
                add(new GridPos(from.row, from.col + d));
                add(new GridPos(from.row, from.col - d));
            }
        }

        private void AddKingMovesWithRange(GridPos from, int range, System.Action<GridPos> add)
        {
            if (add == null) return;
            var maxRange = Mathf.Max(1, range);
            for (var dr = -maxRange; dr <= maxRange; dr++)
            {
                for (var dc = -maxRange; dc <= maxRange; dc++)
                {
                    if (dr == 0 && dc == 0) continue;
                    var chebyshev = Mathf.Max(Mathf.Abs(dr), Mathf.Abs(dc));
                    if (chebyshev > maxRange) continue;
                    var stepR = dr == 0 ? 0 : (dr > 0 ? 1 : -1);
                    var stepC = dc == 0 ? 0 : (dc > 0 ? 1 : -1);
                    var blocked = false;
                    for (var s = 1; s <= chebyshev; s++)
                    {
                        var probe = new GridPos(from.row + stepR * s, from.col + stepC * s);
                        if (!_board.Inside(probe))
                        {
                            blocked = true;
                            break;
                        }
                        if (s < chebyshev && _board.Occupied(probe))
                        {
                            blocked = true;
                            break;
                        }
                    }
                    if (blocked) continue;
                    add(new GridPos(from.row + dr, from.col + dc));
                }
            }
        }

        private void RegisterPlayerMovementForTurn(UnitRuntime unit, GridPos from, GridPos to)
        {
            if (unit == null || unit.faction != Faction.Player || string.IsNullOrEmpty(unit.id)) return;
            var distance = ComputeMovementDistance(unit.kind, from, to);
            if (distance <= 0) return;
            if (_playerMoveDistanceByUnitThisTurn.TryGetValue(unit.id, out var current))
            {
                _playerMoveDistanceByUnitThisTurn[unit.id] = current + distance;
            }
            else
            {
                _playerMoveDistanceByUnitThisTurn[unit.id] = distance;
            }

            if (unit.kind == UnitKind.Bishop &&
                HasTrinket("bishop_greaves") &&
                Mathf.Abs(to.row - from.row) == Mathf.Abs(to.col - from.col) &&
                distance >= 2)
            {
                unit.status.nextAttackDamageModifier += 2;
            }

            if (unit.kind == UnitKind.Rook && HasTrinket("rook_greaves") && distance >= 3)
            {
                unit.status.shieldCharge += 2;
            }

            if (unit.kind == UnitKind.Queen && HasTrinket("queen_greaves") && distance >= 2)
            {
                unit.status.nextAttackDamageModifier += 2;
            }

            if (unit.kind == UnitKind.King && HasTrinket("king_greaves"))
            {
                unit.status.shieldCharge += 2;
                if (_session != null) _session.SetKingHp(unit.hp);
            }

            TryApplyKingsGuardShield(unit);
        }

        private int GetMovedDistanceThisTurn(UnitRuntime unit)
        {
            if (unit == null || string.IsNullOrEmpty(unit.id)) return 0;
            return _playerMoveDistanceByUnitThisTurn.TryGetValue(unit.id, out var moved) ? Mathf.Max(0, moved) : 0;
        }

        private static int ComputeMovementDistance(UnitKind kind, GridPos from, GridPos to)
        {
            var dr = Mathf.Abs(to.row - from.row);
            var dc = Mathf.Abs(to.col - from.col);
            if (kind == UnitKind.Knight) return dr + dc;
            return Mathf.Max(dr, dc);
        }

        private void HandlePlayerCapture(UnitRuntime attacker, GridPos capturedAt)
        {
            if (attacker == null || attacker.faction != Faction.Player) return;

            if (attacker.kind == UnitKind.King && HasTrinket("king_appetite"))
            {
                attacker.hp = Mathf.Min(attacker.maxHp, attacker.hp + 2);
                attacker.status.shieldCharge += 2;
                if (_session != null) _session.SetKingHp(attacker.hp);
            }

            if (_firstCaptureTriggeredByKindThisTurn.Contains(attacker.kind)) return;
            switch (attacker.kind)
            {
                case UnitKind.Knight:
                    if (!HasTrinket("knight_appetite")) return;
                    attacker.moveActionsRemaining += 1;
                    attacker.canMove = attacker.moveActionsRemaining > 0;
                    break;
                case UnitKind.Bishop:
                    if (!HasTrinket("bishop_appetite")) return;
                    ApplyBishopAppetitePin(capturedAt);
                    break;
                case UnitKind.Rook:
                    if (!HasTrinket("rook_appetite")) return;
                    attacker.status.shieldCharge += 3;
                    break;
                case UnitKind.Queen:
                    if (!HasTrinket("queen_appetite")) return;
                    if (_turn != null) _turn.GainEnergy(1);
                    break;
                default:
                    return;
            }

            _firstCaptureTriggeredByKindThisTurn.Add(attacker.kind);
        }

        private void ApplyBishopAppetitePin(GridPos capturedAt)
        {
            foreach (var kv in _board.UnitsById)
            {
                var unit = kv.Value;
                if (unit == null || unit.faction != Faction.Enemy) continue;
                var sameRow = unit.pos.row == capturedAt.row;
                var sameDiagonal = Mathf.Abs(unit.pos.row - capturedAt.row) == Mathf.Abs(unit.pos.col - capturedAt.col);
                if (!sameRow && !sameDiagonal) continue;
                var before = unit.status.rootedTurns;
                unit.status.rootedTurns = Mathf.Max(unit.status.rootedTurns, 1);
                if (unit.status.rootedTurns > before) OnDebuffApplied?.Invoke(unit.id, "Rooted 1");
            }
        }

        private void TryApplyKingsGuardShield(UnitRuntime unit)
        {
            if (!HasTrinket("kings_guard")) return;
            if (unit == null || unit.faction != Faction.Player || unit.kind == UnitKind.King) return;
            var king = FindKing();
            if (king == null) return;
            if (unit.pos.col != king.pos.col) return;
            unit.status.shieldCharge += 1;
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

        private void AddKnightCaptureTargets(GridPos from, Faction attackerFaction, List<GridPos> outTiles)
        {
            void TryAdd(GridPos p)
            {
                if (!_board.Inside(p)) return;
                var target = _board.At(p);
                if (target == null || target.faction == attackerFaction || target.faction == Faction.Neutral) return;
                outTiles.Add(p);
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
                if (u.status.nextAttackDamageModifier < 0)
                {
                    // Weakness decays by turn regardless of whether the unit attacked.
                    u.status.nextAttackDamageModifier = Mathf.Min(0, u.status.nextAttackDamageModifier + 1);
                }
                var wasSleeping = u.status.sleepingTurns > 0;
                u.status.sleepingTurns = Mathf.Max(0, u.status.sleepingTurns - 1);
                if (u.faction == Faction.Enemy && wasSleeping && u.status.sleepingTurns == 0)
                {
                    u.status.hasAwakened = true;
                }
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
                if (t.damage > 0)
                {
                    _board.ApplyDamage(u.id, Mathf.Max(1, t.damage));
                    OnDamageDealt?.Invoke(u.id, Mathf.Max(1, t.damage));
                }
                if (_board.UnitsById.TryGetValue(u.id, out var stillAlive) && stillAlive != null)
                {
                    var beforeRoot = stillAlive.status.rootedTurns;
                    TryApplySleep(stillAlive, t.sleepTurns);
                    stillAlive.status.rootedTurns = Mathf.Max(stillAlive.status.rootedTurns, t.rootTurns);
                    if (stillAlive.status.rootedTurns > beforeRoot) OnDebuffApplied?.Invoke(stillAlive.id, $"Rooted {stillAlive.status.rootedTurns - beforeRoot}");
                }
                _traps.RemoveAt(i);
                OnTrapTriggered?.Invoke($"{t.kind}@{t.row},{t.col}");
            }
        }

        private void AddOrRefreshToxicZone(GridPos tile, int damage, int turns)
        {
            if (!_board.Inside(tile)) return;
            for (var i = 0; i < _toxicZones.Count; i++)
            {
                var zone = _toxicZones[i];
                if (zone.row != tile.row || zone.col != tile.col) continue;
                zone.damage = Mathf.Max(zone.damage, Mathf.Max(1, damage));
                zone.turnsRemaining = Mathf.Max(zone.turnsRemaining, Mathf.Max(1, turns));
                return;
            }

            _toxicZones.Add(new ToxicZoneRuntime
            {
                row = tile.row,
                col = tile.col,
                damage = Mathf.Max(1, damage),
                turnsRemaining = Mathf.Max(1, turns)
            });
        }

        private void ApplyToxicZonesEndOfTurn()
        {
            for (var i = _toxicZones.Count - 1; i >= 0; i--)
            {
                var zone = _toxicZones[i];
                var target = _board.At(new GridPos(zone.row, zone.col));
                if (target != null)
                {
                    var dmg = Mathf.Max(1, zone.damage);
                    _board.ApplyDamage(target.id, dmg);
                    OnDamageDealt?.Invoke(target.id, dmg);
                }

                zone.turnsRemaining -= 1;
                if (zone.turnsRemaining <= 0) _toxicZones.RemoveAt(i);
            }
        }

        private void GenerateDynamicEncounterLayout(
            EncounterTemplateDefinition template,
            out List<Placement> enemyPlacements,
            out List<CaveTemplate> caves,
            out List<BeaconTemplate> beacons)
        {
            enemyPlacements = new List<Placement>();
            caves = new List<CaveTemplate>();
            beacons = new List<BeaconTemplate>();
            if (template == null) return;

            var size = Mathf.Max(4, template.boardSize);
            var spawnRows = Mathf.Max(1, size / 2);
            var seed = (_session != null ? _session.Seed : 1) +
                       (_session != null ? _session.EncounterIndex * 7919 : 0) +
                       template.dynamicSeedOffset;
            var rng = new System.Random(seed);
            var occupied = new HashSet<string>();
            float budget = ResolveDynamicDifficultyBudget(template);
            if (template.createBeacons)
            {
                // Encounters with beacons should be harder.
                budget += 1.75f;
                beacons = BuildDynamicBeacons(size, template.encounterId);
            }

            if (template.requireCaveWhenThresholdReached && budget >= template.caveDifficultyThreshold)
            {
                var caveCell = PickFreeEnemySpawnCell(size, spawnRows, occupied, rng);
                if (caveCell.HasValue)
                {
                    var charges = Mathf.Clamp(Mathf.RoundToInt(2f + budget * 0.35f), 2, 6);
                    var interval = budget >= template.caveDifficultyThreshold + 3f ? 1 : 2;
                    caves.Add(new CaveTemplate
                    {
                        id = $"DYN_CAVE_{template.encounterId}",
                        row = caveCell.Value.row,
                        col = caveCell.Value.col,
                        turnsUntilNextSpawn = interval,
                        spawnCharges = charges,
                        maxAliveFromThisCave = budget >= template.caveDifficultyThreshold + 2f ? 3 : 2,
                        spawnPool = BuildDynamicCaveSpawnPool(budget)
                    });
                    occupied.Add(Key(caveCell.Value));
                    budget = Mathf.Max(0f, budget - 1.5f);
                }
            }

            var pool = BuildDynamicEnemyPool();
            const int maxRolls = 256;
            var rolls = 0;
            while (budget > 0.49f && rolls++ < maxRolls)
            {
                var affordable = new List<(UnitKind kind, float cost)>();
                for (var i = 0; i < pool.Count; i++)
                {
                    if (pool[i].cost <= budget) affordable.Add(pool[i]);
                }
                if (affordable.Count == 0) break;

                var pick = affordable[rng.Next(affordable.Count)];
                var spawn = PickFreeEnemySpawnCell(size, spawnRows, occupied, rng);
                if (!spawn.HasValue) break;

                enemyPlacements.Add(new Placement
                {
                    kind = pick.kind,
                    row = spawn.Value.row,
                    col = spawn.Value.col
                });
                occupied.Add(Key(spawn.Value));
                budget -= pick.cost;
            }

            if (enemyPlacements.Count == 0)
            {
                var fallback = PickFreeEnemySpawnCell(size, spawnRows, occupied, rng) ?? new GridPos(0, size / 2);
                enemyPlacements.Add(new Placement
                {
                    kind = UnitKind.Pawn,
                    row = fallback.row,
                    col = fallback.col
                });
            }
        }

        private static List<BeaconTemplate> BuildDynamicBeacons(int boardSize, string encounterId)
        {
            var safeSize = Mathf.Max(4, boardSize);
            var id = string.IsNullOrEmpty(encounterId) ? "DYN" : encounterId;
            return new List<BeaconTemplate>
            {
                new BeaconTemplate { id = $"{id}_B_L", row = 0, col = 1, maxHp = 12 },
                new BeaconTemplate { id = $"{id}_B_R", row = 0, col = Mathf.Max(0, safeSize - 2), maxHp = 12 }
            };
        }

        private List<(UnitKind kind, float cost)> BuildDynamicEnemyPool()
        {
            var candidates = _chessOnlyEncounter
                ? new[]
                {
                    UnitKind.Pawn,
                    UnitKind.Knight,
                    UnitKind.Bishop,
                    UnitKind.Rook,
                    UnitKind.Queen
                }
                : new[]
                {
                    UnitKind.Pawn,
                    UnitKind.Knight,
                    UnitKind.Bishop,
                    UnitKind.Rook,
                    UnitKind.Queen,
                    UnitKind.Bat,
                    UnitKind.Coyote,
                    UnitKind.Owl,
                    UnitKind.Boar,
                    UnitKind.Snake,
                    UnitKind.Skunk,
                    UnitKind.Bear,
                    UnitKind.Toad
                };

            var pool = new List<(UnitKind kind, float cost)>(candidates.Length);
            for (var i = 0; i < candidates.Length; i++)
            {
                var kind = candidates[i];
                var fallback = DefaultDynamicDifficultyWeight(kind);
                var cost = fallback;
                if (_pieces.TryGetValue(kind, out var def) && def != null && def.weightDifficulty > 0f)
                {
                    cost = def.weightDifficulty;
                }
                pool.Add((kind, Mathf.Max(0.1f, cost)));
            }

            return pool;
        }

        private static float DefaultDynamicDifficultyWeight(UnitKind kind)
        {
            switch (kind)
            {
                case UnitKind.Pawn: return 0.8f;
                case UnitKind.Knight: return 1.6f;
                case UnitKind.Bishop: return 1.7f;
                case UnitKind.Rook: return 2.2f;
                case UnitKind.Queen: return 3.2f;
                case UnitKind.Bat: return 0.9f;
                case UnitKind.Coyote: return 1.3f;
                case UnitKind.Owl: return 1.8f;
                case UnitKind.Boar: return 2.1f;
                case UnitKind.Snake: return 1.0f;
                case UnitKind.Skunk: return 2.0f;
                case UnitKind.Bear: return 3.4f;
                case UnitKind.Toad: return 2.1f;
                default: return 1f;
            }
        }

        private List<SpawnWeight> BuildDynamicCaveSpawnPool(float difficulty)
        {
            if (_chessOnlyEncounter)
            {
                return new List<SpawnWeight>
                {
                    new SpawnWeight { kind = UnitKind.Pawn, weight = 4 },
                    new SpawnWeight { kind = UnitKind.Knight, weight = difficulty >= 8f ? 3 : 2 },
                    new SpawnWeight { kind = UnitKind.Bishop, weight = difficulty >= 10f ? 3 : 2 },
                    new SpawnWeight { kind = UnitKind.Rook, weight = difficulty >= 12f ? 2 : 1 }
                };
            }

            var pool = new List<SpawnWeight>
            {
                new SpawnWeight { kind = UnitKind.Bat, weight = 4 },
                new SpawnWeight { kind = UnitKind.Pawn, weight = 3 },
                new SpawnWeight { kind = UnitKind.Coyote, weight = 3 },
                new SpawnWeight { kind = UnitKind.Knight, weight = difficulty >= 8f ? 2 : 1 },
                new SpawnWeight { kind = UnitKind.Bishop, weight = difficulty >= 10f ? 2 : 1 }
            };

            if (difficulty >= 12f)
            {
                pool.Add(new SpawnWeight { kind = UnitKind.Skunk, weight = 1 });
                pool.Add(new SpawnWeight { kind = UnitKind.Toad, weight = 1 });
            }

            return pool;
        }

        private static GridPos? PickFreeEnemySpawnCell(int boardSize, int spawnRows, HashSet<string> occupied, System.Random rng)
        {
            var candidates = new List<GridPos>();
            for (var r = 0; r < spawnRows; r++)
            {
                for (var c = 0; c < boardSize; c++)
                {
                    var p = new GridPos(r, c);
                    if (occupied != null && occupied.Contains(Key(p))) continue;
                    candidates.Add(p);
                }
            }
            if (candidates.Count == 0) return null;
            return candidates[rng.Next(candidates.Count)];
        }

        private float ResolveDynamicDifficultyBudget(EncounterTemplateDefinition template)
        {
            var templateBudget = template != null ? Mathf.Max(1f, template.difficultyBudget) : 1f;
            if (!IsNoMapEndlessMode()) return templateBudget;
            if (_session == null || _session.Config == null) return templateBudget;

            var cfg = _session.Config;
            var baseBudget = Mathf.Max(1f, cfg.endlessDifficultyBaseBudget);
            var growth = Mathf.Max(1.01f, cfg.endlessDifficultyGrowthFactor);
            var encounterNumber = Mathf.Max(0, _session.EncounterIndex);
            var scaled = Mathf.Max(1f, Mathf.Round(baseBudget * Mathf.Pow(growth, encounterNumber)));
            return Mathf.Max(templateBudget, scaled);
        }

        private bool IsNoMapEndlessMode()
        {
            return _session != null && _session.Config != null && !_session.Config.useRunMap;
        }

        private void TickCaves()
        {
            for (var i = _caves.Count - 1; i >= 0; i--)
            {
                var c = _caves[i];
                if (string.IsNullOrEmpty(c.unitId) || !_board.UnitsById.TryGetValue(c.unitId, out var caveUnit) || caveUnit == null || caveUnit.kind != UnitKind.Cave)
                {
                    _caves.RemoveAt(i);
                    continue;
                }
                caveUnit.status.caveSpawnPrimed = c.spawnCharges > 0 && c.turnsUntilNextSpawn <= 2;
                c.turnsUntilNextSpawn -= 1;
                if (c.turnsUntilNextSpawn > 0 || c.spawnCharges <= 0) continue;
                if (AliveFromCave(c.id) >= c.maxAliveFromThisCave)
                {
                    c.turnsUntilNextSpawn = 1;
                    caveUnit.status.caveSpawnPrimed = true;
                    continue;
                }
                var spawn = AdjacentEmpty(new GridPos(c.row, c.col));
                if (spawn == null)
                {
                    caveUnit.status.caveSpawnPrimed = true;
                    continue;
                }
                var kind = PickSpawn(c.spawnPool);
                kind = SanitizeEnemyKindForEncounter(kind);
                var e = NewUnit(kind, Faction.Enemy, spawn.Value);
                if (_sleepCaveEnemySpawns)
                {
                    e.status.sleepingTurns = Mathf.Max(e.status.sleepingTurns, 1);
                }
                if (!CanPerformPieceMove(Faction.Enemy))
                {
                    e.moveActionsRemaining = 0;
                    e.canMove = false;
                }
                e.spawnedByCaveId = c.id;
                if (_board.Add(e))
                {
                    _enemies.Add(e);
                    EnforceMoveLimitForFaction(Faction.Enemy);
                    OnSpecialStarted?.Invoke(caveUnit);
                }
                c.spawnCharges -= 1;
                c.turnsUntilNextSpawn = 2;
                // Return to idle right after spawning; it will re-enter walking loop on the next pre-spawn turn.
                caveUnit.status.caveSpawnPrimed = false;
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
            if (!AreAllBeaconsAlive())
            {
                OnEncounterMessage?.Invoke("Game over: A beacon was destroyed.");
                if (!_encounterResolved)
                {
                    _encounterResolved = true;
                    OnEncounterResolved?.Invoke(false, _activeNodeId);
                }
                return;
            }
            if (_objectiveTargetsRequired && AreAllObjectiveTargetsDefeated())
            {
                _session.SetKingHp(king.hp);
                var objectiveGoldReward = _session != null ? _session.RollEncounterGoldReward() : 0;
                if (_session != null && objectiveGoldReward > 0) _session.AddGold(objectiveGoldReward);
                ApplyEncounterEndTrinketGoldBonus();
                OnEncounterMessage?.Invoke(objectiveGoldReward > 0 ? $"Encounter won. +{objectiveGoldReward} gold." : "Encounter won.");
                if (!_encounterResolved)
                {
                    _encounterResolved = true;
                    OnEncounterResolved?.Invoke(true, _activeNodeId);
                }
                return;
            }
            if (_enemies.Count == 0)
            {
                _session.SetKingHp(king.hp);
                var goldReward = _session != null ? _session.RollEncounterGoldReward() : 0;
                if (_session != null && goldReward > 0) _session.AddGold(goldReward);
                ApplyEncounterEndTrinketGoldBonus();
                OnEncounterMessage?.Invoke(goldReward > 0 ? $"Encounter won. +{goldReward} gold." : "Encounter won.");
                if (!_encounterResolved)
                {
                    _encounterResolved = true;
                    OnEncounterResolved?.Invoke(true, _activeNodeId);
                }
            }
        }

        private static string GetCardId(CardDefinition card)
        {
            return card != null && !string.IsNullOrWhiteSpace(card.cardId)
                ? card.cardId.Trim().ToLowerInvariant()
                : string.Empty;
        }

        private static bool IsFriendlyTargetCard(string cardId)
        {
            switch (cardId)
            {
                case "fortify_line":
                case "royal_guard":
                case "forced_march":
                case "battle_focus":
                case "counter_stance":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsEnemyTargetCard(string cardId)
        {
            return cardId == "pinning_shot";
        }

        private static bool IsEmptyTileCard(string cardId)
        {
            switch (cardId)
            {
                case "zone_of_control":
                case "spiked_barricade":
                case "sanctuary_tile":
                    return true;
                default:
                    return false;
            }
        }

        private bool TryApplyCustomCard(CardDefinition card)
        {
            var cardId = GetCardId(card);
            switch (cardId)
            {
                case "fortify_line":
                    ApplyRowShieldFromTarget(Mathf.Max(1, card.amount));
                    return true;
                case "royal_guard":
                    ApplyShieldToTarget(Mathf.Max(1, card.amount));
                    return true;
                case "forced_march":
                    ApplyForcedMarchToTarget();
                    return true;
                case "battle_focus":
                    ApplyBattleFocusToTarget(Mathf.Max(1, card.amount));
                    return true;
                case "pinning_shot":
                    ApplyPinningShotToTarget(Mathf.Max(1, card.amount), 1);
                    return true;
                case "counter_stance":
                    ApplyCounterStanceToTarget(Mathf.Max(1, card.amount));
                    return true;
                case "castling":
                    ApplyCastlingSwap();
                    return true;
                case "zone_of_control":
                    TryPlaceZoneControl(_resolvedCardTarget);
                    return true;
                case "spiked_barricade":
                    TryPlaceSpikedBarricade(_resolvedCardTarget, Mathf.Max(1, card.amount));
                    return true;
                case "sanctuary_tile":
                    TryPlaceSanctuaryTile(_resolvedCardTarget, 2);
                    return true;
                default:
                    return false;
            }
        }

        private UnitRuntime ResolveEnemyCardTargetUnit()
        {
            if (!string.IsNullOrEmpty(_resolvedCardTargetUnitId) &&
                _board.UnitsById.TryGetValue(_resolvedCardTargetUnitId, out var byId) &&
                byId != null &&
                byId.faction == Faction.Enemy)
            {
                return byId;
            }

            if (_resolvedCardTarget.HasValue)
            {
                var unit = _board.At(_resolvedCardTarget.Value);
                if (unit != null && unit.faction == Faction.Enemy) return unit;
            }

            return null;
        }

        private void ApplyRowShieldFromTarget(int charges)
        {
            var target = ResolveCardTargetUnit();
            if (target == null) return;
            var row = target.pos.row;
            foreach (var kv in _board.UnitsById)
            {
                var u = kv.Value;
                if (u == null || u.faction != Faction.Player || u.pos.row != row) continue;
                u.status.shieldCharge = Mathf.Max(u.status.shieldCharge, Mathf.Max(1, charges));
                OnCardEffectApplied?.Invoke(u.id, CardKind.Shield);
            }
        }

        private void ApplyForcedMarchToTarget()
        {
            var target = ResolveCardTargetUnit();
            if (target == null) return;
            if (!string.IsNullOrEmpty(target.id))
            {
                if (_forcedMarchRangeBonusByUnitThisTurn.TryGetValue(target.id, out var current))
                {
                    _forcedMarchRangeBonusByUnitThisTurn[target.id] = current + 1;
                }
                else
                {
                    _forcedMarchRangeBonusByUnitThisTurn[target.id] = 1;
                }
            }
            target.moveActionsRemaining = Mathf.Max(target.moveActionsRemaining, 1);
            target.canMove = target.moveActionsRemaining > 0;
            if (HasTrinket("charm"))
            {
                target.status.nextAttackDamageModifier += 1;
                target.canAttack = target.attackActionsRemaining > 0;
            }
            else
            {
                target.attackActionsRemaining = 0;
                target.canAttack = false;
            }
        }

        private void ApplyBattleFocusToTarget(int bonusDamage)
        {
            var target = ResolveCardTargetUnit();
            if (target == null) return;
            target.status.nextAttackDamageModifier += Mathf.Max(1, bonusDamage);
        }

        private void ApplyPinningShotToTarget(int damage, int rootTurns)
        {
            var target = ResolveEnemyCardTargetUnit();
            if (target == null) return;
            _board.ApplyDamage(target.id, Mathf.Max(1, damage));
            OnDamageDealt?.Invoke(target.id, Mathf.Max(1, damage));
            if (_board.UnitsById.TryGetValue(target.id, out var stillAlive) && stillAlive != null)
            {
                stillAlive.status.rootedTurns = Mathf.Max(stillAlive.status.rootedTurns, Mathf.Max(1, rootTurns));
            }
        }

        private void ApplyCounterStanceToTarget(int retaliationDamage)
        {
            var target = ResolveCardTargetUnit();
            if (target == null) return;
            target.status.retaliateDamage = Mathf.Max(target.status.retaliateDamage, Mathf.Max(1, retaliationDamage));
        }

        private void ApplyCastlingSwap()
        {
            if (string.IsNullOrEmpty(_resolvedCardTargetUnitId) || string.IsNullOrEmpty(_resolvedSecondaryCardTargetUnitId)) return;
            if (!_board.UnitsById.TryGetValue(_resolvedCardTargetUnitId, out var first) || first == null) return;
            if (!_board.UnitsById.TryGetValue(_resolvedSecondaryCardTargetUnitId, out var second) || second == null) return;
            if (first.faction != Faction.Player || second.faction != Faction.Player) return;
            if (first.isStructure || second.isStructure) return;

            var firstPos = first.pos;
            var secondPos = second.pos;
            if (firstPos.row == secondPos.row && firstPos.col == secondPos.col) return;

            if (!_board.Remove(first.id) || !_board.Remove(second.id)) return;
            first.pos = secondPos;
            second.pos = firstPos;
            _board.Add(first);
            _board.Add(second);
            TryApplyKingsGuardShield(first);
            TryApplyKingsGuardShield(second);
            OnUnitMoved?.Invoke(first, firstPos, first.pos);
            OnUnitMoved?.Invoke(second, secondPos, second.pos);
        }

        private void TryPlaceZoneControl(GridPos? target)
        {
            var p = target ?? FirstEmptyAnywhere();
            if (p == null) return;
            for (var dc = -1; dc <= 1; dc++)
            {
                var q = new GridPos(p.Value.row, p.Value.col + dc);
                if (!_board.Inside(q) || _board.Occupied(q)) continue;
                _traps.Add(new TrapRuntime
                {
                    row = q.row,
                    col = q.col,
                    kind = CardKind.BearTrap,
                    damage = 0,
                    sleepTurns = 0,
                    rootTurns = 1
                });
            }
        }

        private void TryPlaceSpikedBarricade(GridPos? target, int retaliationDamage)
        {
            var p = target ?? FirstEmptyAnywhere();
            if (p == null || _board.Occupied(p.Value)) return;
            var rock = NewUnit(UnitKind.Rock, Faction.Neutral, p.Value);
            rock.isStructure = true;
            rock.maxHp = 12;
            rock.hp = 12;
            rock.attack = 0;
            rock.status.retaliateDamage = Mathf.Max(1, retaliationDamage);
            _board.Add(rock);
        }

        private void TryPlaceSanctuaryTile(GridPos? target, int turns)
        {
            var p = target ?? FirstEmptyAnywhere();
            if (p == null || _board.Occupied(p.Value)) return;
            _sanctuaries.Add(new SanctuaryRuntime
            {
                row = p.Value.row,
                col = p.Value.col,
                turnsRemaining = Mathf.Max(1, turns),
                healAmount = 1,
                shieldAmount = 1
            });
        }

        private void ApplySanctuaryEndOfPlayerTurn()
        {
            for (var i = _sanctuaries.Count - 1; i >= 0; i--)
            {
                var s = _sanctuaries[i];
                var unit = _board.At(new GridPos(s.row, s.col));
                if (unit != null && unit.faction == Faction.Player)
                {
                    unit.hp = Mathf.Min(unit.maxHp, unit.hp + Mathf.Max(0, s.healAmount));
                    unit.status.shieldCharge = Mathf.Max(unit.status.shieldCharge, Mathf.Max(0, s.shieldAmount));
                    if (unit.kind == UnitKind.King) _session.SetKingHp(unit.hp);
                }
                s.turnsRemaining -= 1;
                if (s.turnsRemaining <= 0) _sanctuaries.RemoveAt(i);
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
            if (!_sanctifiedAshUsedThisCombat && HasTrinket("sanctified_ash"))
            {
                target.status.shieldCharge = Mathf.Max(target.status.shieldCharge, 1);
                _sanctifiedAshUsedThisCombat = true;
            }
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
            if (_chessOnlyEncounter && !IsChessEnemyKind(kind))
            {
                OnEncounterMessage?.Invoke("This encounter is chess-only. Only chess pieces can be summoned.");
                return;
            }
            var p = target ?? FirstEmptyBottom();
            if (p == null || _board.Occupied(p.Value)) return;
            var summoned = NewUnit(kind, Faction.Player, p.Value);
            if (_sleepPlayerSummonsOnSpawn)
            {
                summoned.status.sleepingTurns = Mathf.Max(summoned.status.sleepingTurns, 1);
            }
            if (!CanPerformPieceMove(Faction.Player))
            {
                summoned.moveActionsRemaining = 0;
                summoned.canMove = false;
            }
            _board.Add(summoned);
            TryApplyKingsGuardShield(summoned);
            EnforceMoveLimitForFaction(Faction.Player);
        }

        private void TryPlaceRock(GridPos? target)
        {
            var p = target ?? FirstEmptyAnywhere();
            if (p == null || _board.Occupied(p.Value)) return;
            var rock = NewUnit(UnitKind.Rock, Faction.Neutral, p.Value);
            rock.isStructure = true;
            rock.maxHp = 12; rock.hp = 12; rock.attack = 0;
            _board.Add(rock);
        }

        private void TryPlaceTrap(CardKind kind, int damage, int sleepTurns, GridPos? target, int rootTurns = 0)
        {
            var p = target ?? FirstEmptyAnywhere();
            if (p == null || _board.Occupied(p.Value)) return;
            _traps.Add(new TrapRuntime
            {
                row = p.Value.row,
                col = p.Value.col,
                kind = kind,
                damage = damage,
                sleepTurns = sleepTurns,
                rootTurns = rootTurns
            });
        }

        private void SpawnKing()
        {
            var pos = new GridPos(_board.Size - 1, _board.Size / 2);
            var king = NewUnit(UnitKind.King, Faction.Player, pos);
            king.maxHp = 5;
            king.hp = Mathf.Clamp(_session.PersistentKingHp, 1, 5);
            _board.Add(king);
        }

        private void SpawnBeacons(List<BeaconTemplate> beacons)
        {
            _beaconIds.Clear();
            _beaconsRequired = beacons != null && beacons.Count > 0;
            if (!_beaconsRequired) return;

            for (var i = 0; i < beacons.Count; i++)
            {
                var beaconTpl = beacons[i];
                if (TrySpawnBeacon(beaconTpl, i, out var beaconId)) _beaconIds.Add(beaconId);
                else Debug.LogWarning($"[ChessPrototype] Failed to place beacon #{i}.");
            }
        }

        private bool TrySpawnBeacon(BeaconTemplate beaconTpl, int index, out string beaconId)
        {
            beaconId = null;
            if (!TryFindBeaconSpawnTile(beaconTpl, index, out var tile)) return false;

            var beacon = NewUnit(UnitKind.BeaconTower, Faction.Player, tile);
            beacon.isStructure = true;
            beacon.maxHp = beaconTpl != null ? Mathf.Max(1, beaconTpl.maxHp) : 12;
            beacon.hp = beacon.maxHp;
            beacon.attack = 0;
            beacon.moveActionsRemaining = 0;
            beacon.attackActionsRemaining = 0;
            beacon.canMove = false;
            beacon.canAttack = false;

            if (!_board.Add(beacon)) return false;
            beaconId = beacon.id;
            return true;
        }

        private bool TryFindBeaconSpawnTile(BeaconTemplate beaconTpl, int index, out GridPos tile)
        {
            var fallbackRow = Mathf.Clamp(_board.Size - 2, 0, Mathf.Max(0, _board.Size - 1));
            var fallbackCol = index % 2 == 0 ? 1 : _board.Size - 2;
            var preferredRow = beaconTpl != null ? beaconTpl.row : fallbackRow;
            var preferredCol = beaconTpl != null ? beaconTpl.col : fallbackCol;
            preferredRow = Mathf.Clamp(preferredRow, 0, Mathf.Max(0, _board.Size - 1));
            preferredCol = Mathf.Clamp(preferredCol, 0, Mathf.Max(0, _board.Size - 1));

            var leftSide = preferredCol <= (_board.Size - 1) / 2;
            if (TryFindEmptyOnRowFromColumn(preferredRow, preferredCol, leftSide, out tile)) return true;

            var upper = preferredRow - 1;
            if (TryFindEmptyOnRowFromColumn(upper, preferredCol, leftSide, out tile)) return true;

            var lower = preferredRow + 1;
            if (TryFindEmptyOnRowFromColumn(lower, preferredCol, leftSide, out tile)) return true;

            return TryFindAnyEmptyNearPreferredRow(preferredRow, preferredCol, leftSide, out tile);
        }

        private bool TryFindEmptyOnRowFromColumn(int row, int startCol, bool leftSide, out GridPos tile)
        {
            tile = default;
            if (row < 0 || row >= _board.Size) return false;

            var colStart = Mathf.Clamp(startCol, 0, Mathf.Max(0, _board.Size - 1));
            if (leftSide)
            {
                for (var col = colStart; col < _board.Size; col++)
                {
                    var p = new GridPos(row, col);
                    if (_board.Inside(p) && !_board.Occupied(p))
                    {
                        tile = p;
                        return true;
                    }
                }
                return false;
            }

            for (var col = colStart; col >= 0; col--)
            {
                var p = new GridPos(row, col);
                if (_board.Inside(p) && !_board.Occupied(p))
                {
                    tile = p;
                    return true;
                }
            }

            return false;
        }

        private bool TryFindAnyEmptyNearPreferredRow(int preferredRow, int startCol, bool leftSide, out GridPos tile)
        {
            tile = default;
            for (var d = 0; d < _board.Size; d++)
            {
                var rowA = preferredRow - d;
                if (TryFindEmptyOnRowFromColumn(rowA, startCol, leftSide, out tile)) return true;

                var rowB = preferredRow + d;
                if (rowB != rowA && TryFindEmptyOnRowFromColumn(rowB, startCol, leftSide, out tile)) return true;
            }
            return false;
        }

        private bool AreAllBeaconsAlive()
        {
            if (!_beaconsRequired) return true;
            if (_beaconIds.Count == 0) return false;
            for (var i = 0; i < _beaconIds.Count; i++)
            {
                var id = _beaconIds[i];
                if (string.IsNullOrEmpty(id)) return false;
                if (!_board.UnitsById.TryGetValue(id, out var beacon) || beacon == null) return false;
                if (beacon.kind != UnitKind.BeaconTower || beacon.faction != Faction.Player || beacon.hp <= 0) return false;
            }
            return true;
        }

        private bool IsObjectiveEnemy(UnitKind kind)
        {
            if (kind == UnitKind.King) return true;
            return _enemyDefinitions.TryGetValue(kind, out var def) && def != null && def.defeatObjective;
        }

        private bool AreAllObjectiveTargetsDefeated()
        {
            if (!_objectiveTargetsRequired || _objectiveEnemyIds.Count == 0) return false;
            foreach (var id in _objectiveEnemyIds)
            {
                if (_board.UnitsById.TryGetValue(id, out var alive) && alive != null) return false;
            }
            return true;
        }

        private UnitRuntime NewUnit(UnitKind kind, Faction faction, GridPos pos)
        {
            _idCounter += 1;
            var def = _pieces.TryGetValue(kind, out var pdef) ? pdef : null;
            var enemyDef = _enemyDefinitions.TryGetValue(kind, out var edef) ? edef : null;
            var hp = def != null ? def.maxHp : enemyDef != null ? enemyDef.maxHp : 1;
            var atk = def != null ? def.attack : enemyDef != null ? enemyDef.attack : 1;
            var isStructure = kind == UnitKind.Rock || kind == UnitKind.Cave || kind == UnitKind.BeaconTower;
            if (kind == UnitKind.BeaconTower)
            {
                hp = 12;
                atk = 0;
            }
            var isPlayerActor = faction == Faction.Player && !isStructure;
            return new UnitRuntime
            {
                id = $"{faction}_{kind}_{_idCounter}",
                kind = kind,
                faction = faction,
                pos = pos,
                maxHp = hp,
                hp = hp,
                attack = atk,
                moveActionsRemaining = isPlayerActor ? 1 : 0,
                attackActionsRemaining = isPlayerActor ? 1 : 0,
                canMove = isPlayerActor,
                canAttack = isPlayerActor,
                isStructure = isStructure
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
            if (weights == null || weights.Count == 0) return _chessOnlyEncounter ? UnitKind.Pawn : UnitKind.Bat;
            var filtered = new List<SpawnWeight>(weights.Count);
            for (var i = 0; i < weights.Count; i++)
            {
                var w = weights[i];
                if (!IsSpawnableEnemyKind(w.kind)) continue;
                if (_chessOnlyEncounter && !IsChessEnemyKind(w.kind)) continue;
                filtered.Add(w);
            }
            if (filtered.Count == 0) return _chessOnlyEncounter ? UnitKind.Pawn : UnitKind.Bat;
            var total = 0;
            for (var i = 0; i < filtered.Count; i++) total += Mathf.Max(1, filtered[i].weight);
            var rng = new System.Random(_session.Seed + _session.EncounterIndex + total);
            var roll = rng.Next(0, total);
            for (var i = 0; i < filtered.Count; i++)
            {
                roll -= Mathf.Max(1, filtered[i].weight);
                if (roll < 0) return filtered[i].kind;
            }
            return filtered[0].kind;
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

        private GridPos? FirstEmptyAnywhere()
        {
            for (var r = 0; r < _board.Size; r++)
                for (var c = 0; c < _board.Size; c++)
                    if (!_board.Occupied(new GridPos(r, c))) return new GridPos(r, c);
            return null;
        }

        private static bool CanUnitMoveNow(UnitRuntime unit)
        {
            return unit != null &&
                   unit.canMove &&
                   unit.moveActionsRemaining > 0 &&
                   (unit.status == null || (!unit.status.IsSleeping && !unit.status.IsRooted));
        }

        private static bool CanUnitAttackNow(UnitRuntime unit)
        {
            return unit != null &&
                   unit.canAttack &&
                   unit.attackActionsRemaining > 0 &&
                   (unit.status == null || !unit.status.IsSleeping);
        }

        private bool CanEnemyActNow()
        {
            if (maxPieceMovesPerSidePerTurn <= 0) return true;
            return _enemyActionsThisTurn < maxPieceMovesPerSidePerTurn;
        }

        private void RegisterEnemyAction()
        {
            if (maxPieceMovesPerSidePerTurn <= 0) return;
            _enemyActionsThisTurn += 1;
        }

        private void AddAllEnemyIntentPreviews(
            HashSet<string> intentMove,
            HashSet<string> intentAttack,
            HashSet<string> intentSpecial,
            HashSet<string> emphasizedEnemyIntent,
            string selectedEnemyId)
        {
            foreach (var kv in _board.UnitsById)
            {
                var enemy = kv.Value;
                if (enemy == null || enemy.faction != Faction.Enemy) continue;
                if (!string.IsNullOrEmpty(_activeEnemyIntentActorId) && enemy.id != _activeEnemyIntentActorId) continue;

                var moves = ComputeEnemyMoveTiles(enemy, enemy.pos);
                for (var i = 0; i < moves.Count; i++)
                {
                    var key = Key(moves[i]);
                    intentMove.Add(key);
                    if (emphasizedEnemyIntent != null && enemy.id == selectedEnemyId) emphasizedEnemyIntent.Add(key);
                }

                var attackTiles = BuildAllEnemyAttackPreviewTiles(enemy);
                for (var i = 0; i < attackTiles.Count; i++)
                {
                    var key = Key(attackTiles[i]);
                    intentAttack.Add(key);
                    if (emphasizedEnemyIntent != null && enemy.id == selectedEnemyId) emphasizedEnemyIntent.Add(key);
                }

                var specialTiles = BuildAllEnemySpecialPreviewTiles(enemy);
                for (var i = 0; i < specialTiles.Count; i++)
                {
                    var key = Key(specialTiles[i]);
                    intentSpecial.Add(key);
                    if (emphasizedEnemyIntent != null && enemy.id == selectedEnemyId) emphasizedEnemyIntent.Add(key);
                }
            }
        }

        private List<GridPos> BuildAllEnemyAttackPreviewTiles(UnitRuntime enemy)
        {
            var outTiles = new List<GridPos>();
            if (enemy == null) return outTiles;
            if (enemy.kind == UnitKind.Boar || enemy.kind == UnitKind.Owl || enemy.kind == UnitKind.Skunk || enemy.kind == UnitKind.Toad)
            {
                return outTiles;
            }

            foreach (var kv in _board.UnitsById)
            {
                var target = kv.Value;
                if (!IsEnemyAttackableTarget(target)) continue;
                var pattern = ComputeEnemyStyleAttackTiles(enemy, enemy.pos, target.pos);
                for (var i = 0; i < pattern.Count; i++)
                {
                    var p = pattern[i];
                    if (_board.Inside(p) && !ContainsGridPos(outTiles, p)) outTiles.Add(p);
                }
            }

            return outTiles;
        }

        private List<GridPos> BuildAllEnemySpecialPreviewTiles(UnitRuntime enemy)
        {
            var outTiles = new List<GridPos>();
            if (enemy == null) return outTiles;
            var def = GetEnemyDefinition(enemy.kind);
            var special = def != null ? def.special : null;
            if (special == null || special.type == EnemySpecialType.None) return outTiles;

            switch (special.type)
            {
                case EnemySpecialType.Shriek:
                    for (var r = 0; r < _board.Size; r++)
                    {
                        var p = new GridPos(r, enemy.pos.col);
                        if ((p.row != enemy.pos.row || p.col != enemy.pos.col) && !ContainsGridPos(outTiles, p)) outTiles.Add(p);
                    }
                    for (var c = 0; c < _board.Size; c++)
                    {
                        var p = new GridPos(enemy.pos.row, c);
                        if ((p.row != enemy.pos.row || p.col != enemy.pos.col) && !ContainsGridPos(outTiles, p)) outTiles.Add(p);
                    }
                    break;
                case EnemySpecialType.WebTrap:
                case EnemySpecialType.Ram:
                    foreach (var kv in _board.UnitsById)
                    {
                        var target = kv.Value;
                        if (!IsEnemyAttackableTarget(target)) continue;
                        if (!ContainsGridPos(outTiles, target.pos)) outTiles.Add(target.pos);
                    }
                    break;
                case EnemySpecialType.SuperLeap:
                    for (var dr = -1; dr <= 1; dr++)
                    {
                        for (var dc = -1; dc <= 1; dc++)
                        {
                            if (dr == 0 && dc == 0) continue;
                            var p = new GridPos(enemy.pos.row + dr, enemy.pos.col + dc);
                            if (_board.Inside(p) && !ContainsGridPos(outTiles, p)) outTiles.Add(p);
                        }
                    }
                    break;
                case EnemySpecialType.StenchMissile:
                    for (var r = 0; r < _board.Size; r++)
                        for (var c = 0; c < _board.Size; c++)
                        {
                            var p = new GridPos(r, c);
                            if (!ContainsGridPos(outTiles, p)) outTiles.Add(p);
                        }
                    break;
                case EnemySpecialType.PackHowl:
                case EnemySpecialType.AlphaCall:
                case EnemySpecialType.Enrage:
                case EnemySpecialType.Rend:
                    outTiles.Add(enemy.pos);
                    break;
                case EnemySpecialType.Sleep:
                    // Owl sleep strike previews all orthogonal shot lines.
                    for (var r = enemy.pos.row + 1; r < _board.Size; r++) outTiles.Add(new GridPos(r, enemy.pos.col));
                    for (var r = enemy.pos.row - 1; r >= 0; r--) outTiles.Add(new GridPos(r, enemy.pos.col));
                    for (var c = enemy.pos.col + 1; c < _board.Size; c++) outTiles.Add(new GridPos(enemy.pos.row, c));
                    for (var c = enemy.pos.col - 1; c >= 0; c--) outTiles.Add(new GridPos(enemy.pos.row, c));
                    break;
            }

            if (enemy.kind == UnitKind.Toad)
            {
                foreach (var kv in _board.UnitsById)
                {
                    var target = kv.Value;
                    if (!IsEnemyAttackableTarget(target)) continue;
                    if (!IsOrthogonalClearLine(enemy.pos, target.pos)) continue;
                    var dist = Mathf.Abs(enemy.pos.row - target.pos.row) + Mathf.Abs(enemy.pos.col - target.pos.col);
                    if (dist < 2) continue;
                    if (!ContainsGridPos(outTiles, target.pos)) outTiles.Add(target.pos);
                }
            }

            return outTiles;
        }

        private static bool ContainsGridPos(List<GridPos> list, GridPos p)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].row == p.row && list[i].col == p.col) return true;
            }
            return false;
        }

        private void DrawBoard()
        {
            RebuildAttackMarkerPreviewHighlights();
            var intentMove = new HashSet<string>();
            var intentAttack = new HashSet<string>();
            var intentSpecial = new HashSet<string>();
            var specialAttackHighlights = new HashSet<string>(_specialAttackHighlights);
            var toxicHighlights = new HashSet<string>();
            HashSet<string> emphasizedEnemyIntent = null;
            if (_currentEnemyPlan != null && _showEnemyIntents)
            {
                var selectedEnemyId = SelectedUnit != null && SelectedUnit.faction == Faction.Enemy ? SelectedUnit.id : null;
                if (!string.IsNullOrEmpty(selectedEnemyId)) emphasizedEnemyIntent = new HashSet<string>();
                for (var i = 0; i < _currentEnemyPlan.intents.Count; i++)
                {
                    var it = _currentEnemyPlan.intents[i];
                    if (!string.IsNullOrEmpty(_activeEnemyIntentActorId) && it.actorId != _activeEnemyIntentActorId) continue;
                    for (var j = 0; j < it.attackSquares.Count; j++)
                    {
                        var k = Key(it.attackSquares[j]);
                        var isSpecialAttackActor =
                            it.actorKind == UnitKind.Boar ||
                            it.actorKind == UnitKind.Owl ||
                            it.actorKind == UnitKind.Skunk ||
                            it.actorKind == UnitKind.Toad;
                        if (it.kind == EnemyIntentKind.Special || isSpecialAttackActor) intentSpecial.Add(k);
                        else intentAttack.Add(k);
                        if (emphasizedEnemyIntent != null && it.actorId == selectedEnemyId) emphasizedEnemyIntent.Add(k);
                    }
                }

                AddAllEnemyIntentPreviews(intentMove, intentAttack, intentSpecial, emphasizedEnemyIntent, selectedEnemyId);
            }

            for (var i = 0; i < _toxicZones.Count; i++)
            {
                var z = _toxicZones[i];
                toxicHighlights.Add(Key(new GridPos(z.row, z.col)));
            }
            foreach (var k in toxicHighlights) intentAttack.Add(k);

            if (boardUiGenerator != null)
            {
                var move = new HashSet<string>(_moveHighlights);
                foreach (var k in _cardTargetHighlights) move.Add(k);
                boardUiGenerator.Render(_board, SelectedUnit, SelectAt, HoverAt, move, _attackHighlights, specialAttackHighlights, _attackMarkerPreviewHighlights, intentMove, intentAttack, intentSpecial, toxicHighlights, emphasizedEnemyIntent, _traps);
                return;
            }

            DrawBoardFallback();
        }

        private void TrimDeadEnemyIntents()
        {
            if (_currentEnemyPlan == null) return;
            for (var i = _currentEnemyPlan.intents.Count - 1; i >= 0; i--)
            {
                var it = _currentEnemyPlan.intents[i];
                if (!_board.UnitsById.TryGetValue(it.actorId, out var actor) || actor == null || actor.faction != Faction.Enemy)
                {
                    _currentEnemyPlan.intents.RemoveAt(i);
                }
            }
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













