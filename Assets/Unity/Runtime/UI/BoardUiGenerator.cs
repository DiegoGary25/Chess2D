using System;
using System.Collections.Generic;
using ChessPrototype.Unity.Board;
using ChessPrototype.Unity.Core;
using ChessPrototype.Unity.Data;
using ChessPrototype.Unity.Encounters;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ChessPrototype.Unity.UI
{
    public sealed class BoardUiGenerator : MonoBehaviour
    {
        private sealed class TileView
        {
            public RectTransform rect;
            public Image background;
            public Image overlay;
            public Button button;
        }

        private sealed class PieceView
        {
            public string unitId;
            public GameObject shadeInstance;
            public GameObject moveReadyIndicator;
            public GameObject attackReadyIndicator;
            public RectTransform rect;
            public Image image;
            public TMP_Text label;
            public Button button;
            public UnitFrameAnimator animator;
            public SegmentedBarView hpBar;
            public Image statusIcon;
            public Image intentIcon;
            public Image flashOverlay;
            public UnitAnimationDefinition currentAnimation;
            public Sprite currentIcon;
            public GameObject currentShadePrefab;
            public bool useTintFallback;
        }

        private sealed class TrapView
        {
            public RectTransform rect;
            public Image image;
        }

        [SerializeField] private RectTransform root;
        [SerializeField] private float boardSizePx = 560f;
        [SerializeField] private float tileGap = 2f;
        [SerializeField] private Color tileLight = new Color(0.82f, 0.82f, 0.86f, 0.95f);
        [SerializeField] private Color tileDark = new Color(0.62f, 0.62f, 0.68f, 0.95f);
        [Header("Tile Sprites (Optional)")]
        [SerializeField] private Sprite lightTileSprite;
        [SerializeField] private Sprite darkTileSprite;

        [Header("Overlay Colors")]
        [SerializeField] private Color moveOverlay = new Color(0.2f, 0.6f, 1f, 0.28f);
        [SerializeField] private Color attackOverlay = new Color(1f, 0.3f, 0.3f, 0.35f);
        [SerializeField] private Color intentMoveOverlay = new Color(0.2f, 1f, 0.8f, 0.2f);
        [SerializeField] private Color intentAttackOverlay = new Color(1f, 0.1f, 0.1f, 0.26f);
        [SerializeField] private Color intentSelectedEnemyOverlay = new Color(1f, 0.55f, 0.15f, 0.35f);
        [SerializeField] private Color selectedOverlay = new Color(1f, 0.9f, 0.2f, 0.35f);
        [Header("Overlay Frames")]
        [SerializeField] private Sprite overlayDefaultFrame;
        [SerializeField] private Sprite[] overlayAnimationFrames;
        [SerializeField] private float overlayAnimationFps = 8f;
        [Header("Piece HP Bar")]
        [SerializeField] private SegmentedBarView pieceHpBarPrefab;
        [SerializeField] private Color playerHpBarTint = new Color(0.25f, 0.95f, 0.35f, 1f);
        [SerializeField] private Color enemyHpBarTint = new Color(1f, 0.35f, 0.35f, 1f);
        [SerializeField] private Color neutralHpBarTint = new Color(0.8f, 0.8f, 0.8f, 1f);
        [Header("Movement")]
        [SerializeField, Min(0f)] private float pieceMoveLerpDuration = 0.2f;
        [SerializeField, Min(0.01f)] private float enemyMoveDurationMultiplier = 2f;
        [Header("Action Ready Indicators (Optional)")]
        [SerializeField] private GameObject moveReadyIndicatorPrefab;
        [SerializeField] private GameObject attackReadyIndicatorPrefab;
        [Header("Enemy Intent Icons (Optional)")]
        [SerializeField] private Sprite defaultEnemyAttackIntentIcon;

        private readonly Dictionary<string, TileView> _tileViews = new Dictionary<string, TileView>();
        private readonly Dictionary<string, PieceView> _pieceViews = new Dictionary<string, PieceView>();
        private readonly Dictionary<string, TrapView> _trapViews = new Dictionary<string, TrapView>();
        private readonly Dictionary<UnitKind, Sprite> _iconByKind = new Dictionary<UnitKind, Sprite>();
        private readonly Dictionary<UnitKind, UnitAnimationDefinition> _animationsByKind = new Dictionary<UnitKind, UnitAnimationDefinition>();
        private readonly Dictionary<UnitKind, float> _spriteYOffsetByKind = new Dictionary<UnitKind, float>();
        private readonly Dictionary<UnitKind, float> _visualScaleByKind = new Dictionary<UnitKind, float>();
        private readonly Dictionary<UnitKind, GameObject> _shadePrefabByKind = new Dictionary<UnitKind, GameObject>();
        private readonly Dictionary<UnitKind, GameObject> _playerShadePrefabByKind = new Dictionary<UnitKind, GameObject>();
        private readonly Dictionary<UnitKind, GameObject> _enemyShadePrefabByKind = new Dictionary<UnitKind, GameObject>();
        private readonly Dictionary<UnitKind, float> _shadeYOffsetByKind = new Dictionary<UnitKind, float>();
        private readonly Dictionary<UnitKind, Sprite> _enemySpecialIntentIconByKind = new Dictionary<UnitKind, Sprite>();
        private readonly Dictionary<CardKind, Sprite> _cardIconByKind = new Dictionary<CardKind, Sprite>();
        private readonly Dictionary<string, PieceMoveTween> _pieceMoveTweens = new Dictionary<string, PieceMoveTween>();
        private readonly Dictionary<string, int> _healFlashFrames = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _damageFlashFrames = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _specialIconFrames = new Dictionary<string, int>();
        private readonly HashSet<string> _animatedOverlayKeys = new HashSet<string>();
        private readonly HashSet<string> _defaultOverlayKeys = new HashSet<string>();
        private GameConfigDefinition _cachedConfig;
        private bool _iconCacheInitialized;
        private int _cachedBoardSize = -1;
        private EncounterController _encounter;
        private bool _encounterSubscribed;
        private static readonly Color HealFlashColor = new Color(0.25f, 1f, 0.4f, 0.65f);
        private static readonly Color DamageFlashColor = new Color(1f, 0.2f, 0.2f, 0.75f);
        private const int SpecialIntentIconFrames = 30;
        private float _overlayAnimTimer;

        private sealed class PieceMoveTween
        {
            public Vector2 pieceStart;
            public Vector2 pieceEnd;
            public Vector2 shadeStart;
            public Vector2 shadeEnd;
            public float elapsed;
            public float duration;
        }

        private static string K(GridPos p) => $"{p.row}:{p.col}";
        public float BoardSizePx => boardSizePx;
        public RectTransform RootRect => root != null ? root : transform as RectTransform;

        public void Render(
            BoardState board,
            UnitRuntime selected,
            Action<GridPos> onTileClicked,
            ISet<string> moveTiles,
            ISet<string> attackTiles,
            ISet<string> intentMoveTiles,
            ISet<string> intentAttackTiles,
            ISet<string> emphasizedEnemyIntentTiles,
            IList<TrapRuntime> traps)
        {
            if (root == null) root = transform as RectTransform;
            if (root == null || board == null || board.Size <= 0) return;
            EnsureIconCache();
            EnsureEncounterBinding();

            var size = board.Size;
            var tileSize = boardSizePx / size;
            root.sizeDelta = new Vector2(boardSizePx, boardSizePx);
            EnsureTileViews(size);
            _animatedOverlayKeys.Clear();
            _defaultOverlayKeys.Clear();

            for (var r = 0; r < size; r++)
            {
                for (var c = 0; c < size; c++)
                {
                    var p = new GridPos(r, c);
                    var key = K(p);
                    if (!_tileViews.TryGetValue(key, out var tile)) continue;
                    var captured = p;
                    tile.button.onClick.RemoveAllListeners();
                    tile.button.onClick.AddListener(() => onTileClicked?.Invoke(captured));
                    LayoutCell(tile.rect, r, c, tileSize, tileGap);
                    ApplyTileVisual(tile.background, r, c);
                    var overlayColor = ResolveOverlayColor(p, key, selected, moveTiles, attackTiles, intentMoveTiles, intentAttackTiles, emphasizedEnemyIntentTiles);
                    tile.overlay.color = overlayColor;
                    ApplyOverlayFrameState(tile.overlay, key, overlayColor, moveTiles, attackTiles);
                }
            }

            SyncTrapViews(traps, tileSize);
            SyncPieceViews(board);
            foreach (var kv in board.UnitsById)
            {
                var u = kv.Value;
                if (!_pieceViews.TryGetValue(u.id, out var view)) continue;
                ConfigurePieceVisual(view, u);
                var captured = u.pos;
                view.button.onClick.RemoveAllListeners();
                view.button.onClick.AddListener(() => onTileClicked?.Invoke(captured));
                var spriteYOffset = ResolveBoardScaledSpriteYOffset(u.kind, u.faction, size);
                var visualScale = ResolveVisualScale(u.kind);
                var shadeYOffset = ResolveBoardScaledShadeYOffset(u.kind, u.faction, size);
                LayoutCell(view.rect, u.pos.row, u.pos.col, tileSize, tileGap + 6f, spriteYOffset, visualScale);
                if (view.shadeInstance != null)
                {
                    var showShade = view.currentShadePrefab != null;
                    view.shadeInstance.SetActive(showShade);
                    if (showShade)
                    {
                        if (view.shadeInstance.transform is RectTransform shadeRt)
                        {
                            LayoutCell(shadeRt, u.pos.row, u.pos.col, tileSize, tileGap + 10f, shadeYOffset, preserveScale: true);
                            shadeRt.SetAsLastSibling();
                        }
                    }
                }

                if (_pieceMoveTweens.TryGetValue(u.id, out var tween))
                {
                    var t = tween.duration <= 0.0001f ? 1f : Mathf.Clamp01(tween.elapsed / tween.duration);
                    view.rect.anchoredPosition = Vector2.Lerp(tween.pieceStart, tween.pieceEnd, t);
                    if (view.shadeInstance != null && view.shadeInstance.transform is RectTransform shadeTweenRt)
                    {
                        shadeTweenRt.anchoredPosition = Vector2.Lerp(tween.shadeStart, tween.shadeEnd, t);
                    }
                }

                view.rect.SetAsLastSibling();
                view.animator.SetSleeping(u.status != null && u.status.IsSleeping);
                UpdateShieldOverlay(view, u);
                UpdatePieceHpBar(view, u);
                UpdateActionIndicators(view, u);
                UpdateIntentIcon(view, u);
            }
        }

        private void EnsureIconCache()
        {
            var session = FindObjectOfType<GameSessionState>();
            var cfg = session != null ? session.Config : null;
            if (_iconCacheInitialized && cfg == _cachedConfig) return;

            _cachedConfig = cfg;
            _iconCacheInitialized = true;
            _iconByKind.Clear();
            _animationsByKind.Clear();
            _spriteYOffsetByKind.Clear();
            _visualScaleByKind.Clear();
            _shadePrefabByKind.Clear();
            _playerShadePrefabByKind.Clear();
            _enemyShadePrefabByKind.Clear();
            _shadeYOffsetByKind.Clear();
            _enemySpecialIntentIconByKind.Clear();
            _cardIconByKind.Clear();
            if (cfg == null) return;

            if (cfg.pieceDefinitions != null)
            {
                for (var i = 0; i < cfg.pieceDefinitions.Count; i++)
                {
                    var def = cfg.pieceDefinitions[i];
                    if (def == null) continue;
                    if (def.icon != null) _iconByKind[def.kind] = def.icon;
                    if (def.animations != null) _animationsByKind[def.kind] = def.animations;
                    _spriteYOffsetByKind[def.kind] = def.spriteYOffset;
                    _visualScaleByKind[def.kind] = def.visualScale;
                    _shadePrefabByKind[def.kind] = def.shadePrefab;
                    if (def.playerShadePrefab != null) _playerShadePrefabByKind[def.kind] = def.playerShadePrefab;
                    if (def.enemyShadePrefab != null) _enemyShadePrefabByKind[def.kind] = def.enemyShadePrefab;
                    _shadeYOffsetByKind[def.kind] = def.shadeYOffset;
                }
            }

            if (cfg.enemyDefinitions != null)
            {
                for (var i = 0; i < cfg.enemyDefinitions.Count; i++)
                {
                    var def = cfg.enemyDefinitions[i];
                    if (def == null) continue;
                    if (def.icon != null) _iconByKind[def.kind] = def.icon;
                    // Preserve piece visual settings unless enemy explicitly overrides.
                    if (def.spriteYOffset != 0f) _spriteYOffsetByKind[def.kind] = def.spriteYOffset;
                    if (def.visualScale > 0.01f && def.visualScale != 1f) _visualScaleByKind[def.kind] = def.visualScale;
                    if (def.shadePrefab != null) _shadePrefabByKind[def.kind] = def.shadePrefab;
                    if (def.shadeYOffset != 0f) _shadeYOffsetByKind[def.kind] = def.shadeYOffset;
                    if (def.special != null && def.special.intentIcon != null)
                    {
                        _enemySpecialIntentIconByKind[def.kind] = def.special.intentIcon;
                    }
                }
            }

            if (cfg.starterDeck != null)
            {
                for (var i = 0; i < cfg.starterDeck.Count; i++)
                {
                    var card = cfg.starterDeck[i];
                    if (card == null || card.icon == null) continue;
                    _cardIconByKind[card.kind] = card.icon;
                }
            }
        }

        private Sprite ResolveIcon(UnitKind kind)
        {
            return _iconByKind.TryGetValue(kind, out var sprite) ? sprite : null;
        }

        private UnitAnimationDefinition ResolveAnimation(UnitKind kind)
        {
            return _animationsByKind.TryGetValue(kind, out var animation) ? animation : null;
        }

        private float ResolveSpriteYOffset(UnitKind kind)
        {
            return _spriteYOffsetByKind.TryGetValue(kind, out var y) ? y : 0f;
        }

        private GameObject ResolveShadePrefab(UnitRuntime unit)
        {
            if (unit != null)
            {
                if (unit.faction == Faction.Player &&
                    _playerShadePrefabByKind.TryGetValue(unit.kind, out var playerPrefab) &&
                    playerPrefab != null)
                {
                    return playerPrefab;
                }

                if (unit.faction == Faction.Enemy &&
                    _enemyShadePrefabByKind.TryGetValue(unit.kind, out var enemyPrefab) &&
                    enemyPrefab != null)
                {
                    return enemyPrefab;
                }
            }

            return unit != null && _shadePrefabByKind.TryGetValue(unit.kind, out var prefab) ? prefab : null;
        }

        private float ResolveVisualScale(UnitKind kind)
        {
            if (!_visualScaleByKind.TryGetValue(kind, out var scale)) return 1f;
            return Mathf.Max(0.01f, scale);
        }

        private float ResolveShadeYOffset(UnitKind kind)
        {
            return _shadeYOffsetByKind.TryGetValue(kind, out var y) ? y : 0f;
        }

        private Color ResolveOverlayColor(
            GridPos p,
            string key,
            UnitRuntime selected,
            ISet<string> moveTiles,
            ISet<string> attackTiles,
            ISet<string> intentMoveTiles,
            ISet<string> intentAttackTiles,
            ISet<string> emphasizedEnemyIntentTiles)
        {
            var emphasizeEnemyIntent = emphasizedEnemyIntentTiles != null;
            if (selected != null && selected.pos.row == p.row && selected.pos.col == p.col) return selectedOverlay;
            if (attackTiles != null && attackTiles.Contains(key)) return attackOverlay;
            if (moveTiles != null && moveTiles.Contains(key)) return moveOverlay;
            if (intentAttackTiles != null && intentAttackTiles.Contains(key))
            {
                if (emphasizeEnemyIntent && emphasizedEnemyIntentTiles.Contains(key)) return intentSelectedEnemyOverlay;
                return intentAttackOverlay;
            }
            if (intentMoveTiles != null && intentMoveTiles.Contains(key))
            {
                if (emphasizeEnemyIntent && emphasizedEnemyIntentTiles.Contains(key)) return intentSelectedEnemyOverlay;
                return intentMoveOverlay;
            }
            return new Color(0f, 0f, 0f, 0f);
        }

        private static string ShortKind(UnitKind kind)
        {
            var s = kind.ToString();
            return string.IsNullOrEmpty(s) ? "?" : s.Substring(0, 1);
        }

        private void EnsureTileViews(int size)
        {
            if (_cachedBoardSize == size) return;
            ClearAllViews();
            _cachedBoardSize = size;

            for (var r = 0; r < size; r++)
            {
                for (var c = 0; c < size; c++)
                {
                    var key = K(new GridPos(r, c));
                    var tile = NewRect($"Tile_{r}_{c}", root);
                    var tileImg = tile.gameObject.AddComponent<Image>();
                    ApplyTileVisual(tileImg, r, c);
                    var btn = tile.gameObject.AddComponent<Button>();
                    var overlay = NewRect("Overlay", tile);
                    overlay.anchorMin = Vector2.zero;
                    overlay.anchorMax = Vector2.one;
                    overlay.offsetMin = Vector2.zero;
                    overlay.offsetMax = Vector2.zero;
                    var ov = overlay.gameObject.AddComponent<Image>();
                    ov.raycastTarget = false;
                    _tileViews[key] = new TileView { rect = tile, background = tileImg, button = btn, overlay = ov };
                }
            }
        }

        private void ApplyTileVisual(Image image, int row, int col)
        {
            if (image == null) return;
            var isLight = ((row + col) % 2) == 0;
            var sprite = isLight ? lightTileSprite : darkTileSprite;
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Simple;
                image.color = Color.white;
                image.preserveAspect = false;
            }
            else
            {
                image.sprite = null;
                image.color = isLight ? tileLight : tileDark;
                image.preserveAspect = false;
            }
        }

        private void SyncPieceViews(BoardState board)
        {
            var stale = new List<string>();
            foreach (var kv in _pieceViews)
            {
                if (!board.UnitsById.ContainsKey(kv.Key)) stale.Add(kv.Key);
            }
            for (var i = 0; i < stale.Count; i++)
            {
                var id = stale[i];
                if (_pieceViews.TryGetValue(id, out var view) && view.rect != null) Destroy(view.rect.gameObject);
                if (_pieceViews.TryGetValue(id, out var staleView) && staleView.shadeInstance != null) Destroy(staleView.shadeInstance);
                _pieceMoveTweens.Remove(id);
                _specialIconFrames.Remove(id);
                _pieceViews.Remove(id);
            }

            foreach (var kv in board.UnitsById)
            {
                var id = kv.Key;
                if (_pieceViews.ContainsKey(id)) continue;
                var piece = NewRect($"Piece_{id}", root);
                piece.SetAsLastSibling();
                var img = piece.gameObject.AddComponent<Image>();
                var btn = piece.gameObject.AddComponent<Button>();
                var animator = piece.gameObject.AddComponent<UnitFrameAnimator>();
                SegmentedBarView hpBar = null;
                if (pieceHpBarPrefab != null)
                {
                    hpBar = Instantiate(pieceHpBarPrefab, piece);
                    hpBar.name = "HpBar";
                }

                var labelRt = NewRect("Label", piece);
                labelRt.anchorMin = Vector2.zero;
                labelRt.anchorMax = Vector2.one;
                labelRt.offsetMin = Vector2.zero;
                labelRt.offsetMax = Vector2.zero;
                var label = labelRt.gameObject.AddComponent<TextMeshProUGUI>();
                label.alignment = TextAlignmentOptions.Center;
                label.enableAutoSizing = true;
                label.color = Color.black;

                var statusRt = NewRect("StatusIcon", piece);
                statusRt.anchorMin = new Vector2(0f, 1f);
                statusRt.anchorMax = new Vector2(0f, 1f);
                statusRt.pivot = new Vector2(0f, 1f);
                statusRt.anchoredPosition = new Vector2(2f, -2f);
                statusRt.sizeDelta = new Vector2(20f, 20f);
                var statusIcon = statusRt.gameObject.AddComponent<Image>();
                statusIcon.raycastTarget = false;
                statusIcon.enabled = false;

                var intentRt = NewRect("IntentIcon", piece);
                intentRt.anchorMin = new Vector2(1f, 1f);
                intentRt.anchorMax = new Vector2(1f, 1f);
                intentRt.pivot = new Vector2(1f, 1f);
                intentRt.anchoredPosition = new Vector2(-2f, -2f);
                intentRt.sizeDelta = new Vector2(20f, 20f);
                var intentIcon = intentRt.gameObject.AddComponent<Image>();
                intentIcon.raycastTarget = false;
                intentIcon.enabled = false;

                var flashRt = NewRect("HealFlash", piece);
                flashRt.anchorMin = Vector2.zero;
                flashRt.anchorMax = Vector2.one;
                flashRt.offsetMin = Vector2.zero;
                flashRt.offsetMax = Vector2.zero;
                var flashOverlay = flashRt.gameObject.AddComponent<Image>();
                flashOverlay.raycastTarget = false;
                flashOverlay.color = new Color(0f, 0f, 0f, 0f);

                GameObject moveIndicator = null;
                if (moveReadyIndicatorPrefab != null)
                {
                    moveIndicator = Instantiate(moveReadyIndicatorPrefab, piece);
                    moveIndicator.name = "MoveReadyIndicator";
                    moveIndicator.SetActive(false);
                }

                GameObject attackIndicator = null;
                if (attackReadyIndicatorPrefab != null)
                {
                    attackIndicator = Instantiate(attackReadyIndicatorPrefab, piece);
                    attackIndicator.name = "AttackReadyIndicator";
                    attackIndicator.SetActive(false);
                }

                _pieceViews[id] = new PieceView
                {
                    unitId = id,
                    rect = piece,
                    moveReadyIndicator = moveIndicator,
                    attackReadyIndicator = attackIndicator,
                    image = img,
                    button = btn,
                    animator = animator,
                    hpBar = hpBar,
                    label = label,
                    statusIcon = statusIcon,
                    intentIcon = intentIcon,
                    flashOverlay = flashOverlay
                };
            }
        }

        private void SyncTrapViews(IList<TrapRuntime> traps, float tileSize)
        {
            var activeKeys = new HashSet<string>();
            if (traps != null)
            {
                for (var i = 0; i < traps.Count; i++)
                {
                    var trap = traps[i];
                    var p = new GridPos(trap.row, trap.col);
                    var key = K(p);
                    activeKeys.Add(key);

                    if (!_trapViews.TryGetValue(key, out var view))
                    {
                        var rect = NewRect($"Trap_{key}", root);
                        rect.SetAsLastSibling();
                        var image = rect.gameObject.AddComponent<Image>();
                        image.raycastTarget = false;
                        view = new TrapView { rect = rect, image = image };
                        _trapViews[key] = view;
                    }

                    var icon = ResolveCardIcon(trap.kind);
                    if (icon != null)
                    {
                        view.image.sprite = icon;
                        view.image.color = Color.white;
                        view.image.preserveAspect = true;
                    }
                    else
                    {
                        view.image.sprite = null;
                        view.image.preserveAspect = false;
                        view.image.color = new Color(0.9f, 0.6f, 0.15f, 0.95f);
                    }
                    LayoutCell(view.rect, trap.row, trap.col, tileSize, tileGap + 22f);
                }
            }

            var stale = new List<string>();
            foreach (var kv in _trapViews)
            {
                if (!activeKeys.Contains(kv.Key)) stale.Add(kv.Key);
            }
            for (var i = 0; i < stale.Count; i++)
            {
                var key = stale[i];
                if (_trapViews.TryGetValue(key, out var view) && view.rect != null) Destroy(view.rect.gameObject);
                _trapViews.Remove(key);
            }
        }

        private void ConfigurePieceVisual(PieceView view, UnitRuntime unit)
        {
            var icon = ResolveIcon(unit.kind);
            var animations = ResolveAnimation(unit.kind);
                var shadePrefab = ResolveShadePrefab(unit);
            var hasAnimationFrames = HasAnimationFrames(animations);
            var useTintFallback = !hasAnimationFrames && icon == null;

            if (view.currentAnimation != animations || view.currentIcon != icon || view.currentShadePrefab != shadePrefab || view.useTintFallback != useTintFallback)
            {
                view.currentAnimation = animations;
                view.currentIcon = icon;
                view.currentShadePrefab = shadePrefab;
                view.useTintFallback = useTintFallback;
                view.animator.Configure(view.image, animations, icon);
                EnsureShadeInstance(view, shadePrefab);
            }

            if (useTintFallback)
            {
                view.image.sprite = null;
                view.image.preserveAspect = false;
                view.image.color = unit.faction == Faction.Player
                    ? new Color(0.1f, 0.75f, 1f, 0.95f)
                    : unit.faction == Faction.Enemy
                        ? new Color(1f, 0.28f, 0.28f, 0.95f)
                        : new Color(0.55f, 0.55f, 0.55f, 0.95f);
                view.label.enabled = true;
                view.label.text = ShortKind(unit.kind);
            }
            else
            {
                view.image.color = Color.white;
                view.image.preserveAspect = true;
                view.label.enabled = false;
            }
        }

        private void UpdateShieldOverlay(PieceView view, UnitRuntime unit)
        {
            if (view.statusIcon == null) return;
            var hasShield = unit != null && unit.status != null && unit.status.shieldCharge > 0;
            var shieldIcon = ResolveCardIcon(CardKind.Shield);
            view.statusIcon.enabled = hasShield && shieldIcon != null;
            if (view.statusIcon.enabled) view.statusIcon.sprite = shieldIcon;
        }

        private void UpdatePieceHpBar(PieceView view, UnitRuntime unit)
        {
            if (view == null || view.hpBar == null || unit == null) return;
            var tint = unit.faction == Faction.Player
                ? playerHpBarTint
                : unit.faction == Faction.Enemy
                    ? enemyHpBarTint
                    : neutralHpBarTint;
            view.hpBar.SetValue(unit.hp, unit.maxHp, tint);
        }

        private static void UpdateActionIndicators(PieceView view, UnitRuntime unit)
        {
            if (view == null || unit == null) return;

            var showPlayerIndicators = unit.faction == Faction.Player;
            if (view.moveReadyIndicator != null)
            {
                view.moveReadyIndicator.SetActive(showPlayerIndicators && unit.canMove);
            }

            if (view.attackReadyIndicator != null)
            {
                view.attackReadyIndicator.SetActive(showPlayerIndicators && unit.canAttack);
            }
        }

        private void UpdateIntentIcon(PieceView view, UnitRuntime unit)
        {
            if (view == null || view.intentIcon == null || unit == null)
            {
                return;
            }

            if (unit.faction != Faction.Enemy)
            {
                view.intentIcon.enabled = false;
                view.intentIcon.sprite = null;
                return;
            }

            Sprite icon = null;
            if (_specialIconFrames.TryGetValue(unit.id, out var framesLeft) &&
                framesLeft > 0 &&
                _enemySpecialIntentIconByKind.TryGetValue(unit.kind, out var specialIcon))
            {
                icon = specialIcon;
            }

            if (icon == null)
            {
                icon = defaultEnemyAttackIntentIcon;
            }

            view.intentIcon.sprite = icon;
            view.intentIcon.enabled = icon != null;
        }

        private static void EnsureShadeInstance(PieceView view, GameObject shadePrefab)
        {
            if (view == null) return;

            if (shadePrefab == null)
            {
                if (view.shadeInstance != null) Destroy(view.shadeInstance);
                view.shadeInstance = null;
                return;
            }

            if (view.shadeInstance != null) Destroy(view.shadeInstance);
            var instance = Instantiate(shadePrefab, view.rect.parent, false);
            instance.name = string.IsNullOrEmpty(view.unitId) ? "Shade" : $"Shade_{view.unitId}";
            view.shadeInstance = instance;
        }

        private static bool HasAnimationFrames(UnitAnimationDefinition animation)
        {
            if (animation == null) return false;
            return HasFrames(animation.idleFrames) ||
                   HasFrames(animation.movingFrames) ||
                   HasFrames(animation.attackFrames) ||
                   HasFrames(animation.actionFrames) ||
                   HasFrames(animation.hitFrames) ||
                   HasFrames(animation.sleepFrames);
        }

        private static bool HasFrames(Sprite[] frames)
        {
            return frames != null && frames.Length > 0;
        }

        private Sprite ResolveCardIcon(CardKind kind)
        {
            return _cardIconByKind.TryGetValue(kind, out var sprite) ? sprite : null;
        }

        private void EnsureEncounterBinding()
        {
            var encounter = FindObjectOfType<EncounterController>();
            if (encounter == _encounter && _encounterSubscribed) return;

            if (_encounter != null && _encounterSubscribed)
            {
                _encounter.OnUnitMoved -= HandleUnitMoved;
                _encounter.OnAttackStarted -= HandleAttackStarted;
                _encounter.OnSpecialStarted -= HandleSpecialStarted;
                _encounter.OnDamageDealt -= HandleDamageDealt;
                _encounter.OnCardEffectApplied -= HandleCardEffectApplied;
                _encounterSubscribed = false;
            }

            _encounter = encounter;
            if (_encounter == null) return;

            _encounter.OnUnitMoved += HandleUnitMoved;
            _encounter.OnAttackStarted += HandleAttackStarted;
            _encounter.OnSpecialStarted += HandleSpecialStarted;
            _encounter.OnDamageDealt += HandleDamageDealt;
            _encounter.OnCardEffectApplied += HandleCardEffectApplied;
            _encounterSubscribed = true;
        }

        private void HandleUnitMoved(UnitRuntime unit, GridPos _, GridPos __)
        {
            if (unit == null) return;
            if (_pieceViews.TryGetValue(unit.id, out var view) && view.animator != null)
            {
                view.animator.PlayMoveOneShot();
            }

            if (pieceMoveLerpDuration <= 0f) return;
            if (!_pieceViews.TryGetValue(unit.id, out var tweenView) || tweenView.rect == null) return;
            if (_cachedBoardSize <= 0) return;

            var tileSize = boardSizePx / _cachedBoardSize;
            var pieceEnd = ComputeCellAnchoredPosition(
                unit.pos.row,
                unit.pos.col,
                tileSize,
                ResolveBoardScaledSpriteYOffset(unit.kind, unit.faction, _cachedBoardSize));
            var shadeEnd = ComputeCellAnchoredPosition(
                unit.pos.row,
                unit.pos.col,
                tileSize,
                ResolveBoardScaledShadeYOffset(unit.kind, unit.faction, _cachedBoardSize));
            var pieceStart = tweenView.rect.anchoredPosition;
            var shadeStart = shadeEnd;
            if (tweenView.shadeInstance != null && tweenView.shadeInstance.transform is RectTransform shadeRt)
            {
                shadeStart = shadeRt.anchoredPosition;
            }

            _pieceMoveTweens[unit.id] = new PieceMoveTween
            {
                pieceStart = pieceStart,
                pieceEnd = pieceEnd,
                shadeStart = shadeStart,
                shadeEnd = shadeEnd,
                elapsed = 0f,
                duration = pieceMoveLerpDuration * (unit.faction == Faction.Enemy ? enemyMoveDurationMultiplier : 1f)
            };
        }

        private void HandleAttackStarted(UnitRuntime attacker, List<GridPos> _)
        {
            if (attacker == null) return;
            if (_pieceViews.TryGetValue(attacker.id, out var view) && view.animator != null)
            {
                view.animator.PlayAttackOneShot();
            }
        }

        private void HandleSpecialStarted(UnitRuntime actor)
        {
            if (actor == null) return;
            if (_pieceViews.TryGetValue(actor.id, out var view) && view.animator != null)
            {
                view.animator.PlayActionOneShot();
            }
            _specialIconFrames[actor.id] = SpecialIntentIconFrames;
        }

        private void HandleDamageDealt(string unitId, int _)
        {
            if (string.IsNullOrEmpty(unitId)) return;
            if (_pieceViews.TryGetValue(unitId, out var view) && view.animator != null)
            {
                view.animator.PlayHitOneShot();
            }
            _damageFlashFrames[unitId] = 5;
        }

        private void HandleCardEffectApplied(string unitId, CardKind kind)
        {
            if (string.IsNullOrEmpty(unitId)) return;
            if (kind == CardKind.HealSmall) _healFlashFrames[unitId] = 3;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private void LayoutCell(
            RectTransform rt,
            int row,
            int col,
            float tileSize,
            float inset,
            float offsetY = 0f,
            float scale = 1f,
            bool preserveScale = false)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(tileSize - inset, tileSize - inset);
            rt.anchoredPosition = new Vector2(-boardSizePx * 0.5f + col * tileSize, boardSizePx * 0.5f - row * tileSize + offsetY);
            if (!preserveScale)
            {
                rt.localScale = Vector3.one * Mathf.Max(0.01f, scale);
            }
        }

        private void ClearAllViews()
        {
            foreach (var kv in _tileViews)
            {
                if (kv.Value.rect != null) Destroy(kv.Value.rect.gameObject);
            }
            _tileViews.Clear();

            foreach (var kv in _pieceViews)
            {
                if (kv.Value.shadeInstance != null) Destroy(kv.Value.shadeInstance);
                if (kv.Value.rect != null) Destroy(kv.Value.rect.gameObject);
            }
            _pieceViews.Clear();
            _pieceMoveTweens.Clear();

            foreach (var kv in _trapViews)
            {
                if (kv.Value.rect != null) Destroy(kv.Value.rect.gameObject);
            }
            _trapViews.Clear();
            _healFlashFrames.Clear();
            _damageFlashFrames.Clear();
            _specialIconFrames.Clear();
            _animatedOverlayKeys.Clear();
            _defaultOverlayKeys.Clear();
        }

        private void OnDestroy()
        {
            if (_encounter != null && _encounterSubscribed)
            {
                _encounter.OnUnitMoved -= HandleUnitMoved;
                _encounter.OnAttackStarted -= HandleAttackStarted;
                _encounter.OnSpecialStarted -= HandleSpecialStarted;
                _encounter.OnDamageDealt -= HandleDamageDealt;
                _encounter.OnCardEffectApplied -= HandleCardEffectApplied;
            }
        }

        private void Update()
        {
            UpdatePieceMoveTweens(Time.deltaTime);
            UpdateOverlayAnimation(Time.deltaTime);
            UpdateSpecialIntentIconFrames();
            if (_healFlashFrames.Count == 0 && _damageFlashFrames.Count == 0) return;

            var damageFinished = new List<string>();
            var damageUpdates = new List<KeyValuePair<string, int>>();
            foreach (var kv in _damageFlashFrames)
            {
                var unitId = kv.Key;
                var framesLeft = kv.Value;
                if (!_pieceViews.TryGetValue(unitId, out var view) || view.flashOverlay == null)
                {
                    damageFinished.Add(unitId);
                    continue;
                }

                view.flashOverlay.color = (framesLeft % 2 == 0)
                    ? DamageFlashColor
                    : new Color(DamageFlashColor.r, DamageFlashColor.g, DamageFlashColor.b, 0.35f);

                framesLeft -= 1;
                if (framesLeft <= 0)
                {
                    view.flashOverlay.color = new Color(0f, 0f, 0f, 0f);
                    damageFinished.Add(unitId);
                }
                else
                {
                    damageUpdates.Add(new KeyValuePair<string, int>(unitId, framesLeft));
                }
            }
            for (var i = 0; i < damageUpdates.Count; i++) _damageFlashFrames[damageUpdates[i].Key] = damageUpdates[i].Value;
            for (var i = 0; i < damageFinished.Count; i++) _damageFlashFrames.Remove(damageFinished[i]);

            if (_healFlashFrames.Count == 0) return;

            var finished = new List<string>();
            var updates = new List<KeyValuePair<string, int>>();
            foreach (var kv in _healFlashFrames)
            {
                var unitId = kv.Key;
                var framesLeft = kv.Value;
                if (!_pieceViews.TryGetValue(unitId, out var view) || view.flashOverlay == null)
                {
                    finished.Add(unitId);
                    continue;
                }

                // Damage flash has priority.
                if (_damageFlashFrames.ContainsKey(unitId)) continue;

                view.flashOverlay.color = (framesLeft % 2 == 0)
                    ? HealFlashColor
                    : new Color(HealFlashColor.r, HealFlashColor.g, HealFlashColor.b, 0.3f);

                framesLeft -= 1;
                if (framesLeft <= 0)
                {
                    view.flashOverlay.color = new Color(0f, 0f, 0f, 0f);
                    finished.Add(unitId);
                }
                else
                {
                    updates.Add(new KeyValuePair<string, int>(unitId, framesLeft));
                }
            }

            for (var i = 0; i < updates.Count; i++) _healFlashFrames[updates[i].Key] = updates[i].Value;
            for (var i = 0; i < finished.Count; i++) _healFlashFrames.Remove(finished[i]);
        }

        private void UpdateSpecialIntentIconFrames()
        {
            if (_specialIconFrames.Count == 0) return;

            var finished = new List<string>();
            var updates = new List<KeyValuePair<string, int>>();
            foreach (var kv in _specialIconFrames)
            {
                var unitId = kv.Key;
                var framesLeft = kv.Value - 1;
                if (framesLeft <= 0)
                {
                    finished.Add(unitId);
                }
                else
                {
                    updates.Add(new KeyValuePair<string, int>(unitId, framesLeft));
                }
            }

            for (var i = 0; i < updates.Count; i++) _specialIconFrames[updates[i].Key] = updates[i].Value;
            for (var i = 0; i < finished.Count; i++) _specialIconFrames.Remove(finished[i]);
        }

        private void UpdatePieceMoveTweens(float deltaTime)
        {
            if (_pieceMoveTweens.Count == 0) return;

            var done = new List<string>();
            foreach (var kv in _pieceMoveTweens)
            {
                var id = kv.Key;
                var tween = kv.Value;
                tween.elapsed += Mathf.Max(0f, deltaTime);
                var t = tween.duration <= 0.0001f ? 1f : Mathf.Clamp01(tween.elapsed / tween.duration);

                if (_pieceViews.TryGetValue(id, out var view) && view.rect != null)
                {
                    view.rect.anchoredPosition = Vector2.Lerp(tween.pieceStart, tween.pieceEnd, t);
                    if (view.shadeInstance != null && view.shadeInstance.transform is RectTransform shadeRt)
                    {
                        shadeRt.anchoredPosition = Vector2.Lerp(tween.shadeStart, tween.shadeEnd, t);
                    }
                }
                else
                {
                    done.Add(id);
                    continue;
                }

                if (t >= 1f) done.Add(id);
            }

            for (var i = 0; i < done.Count; i++) _pieceMoveTweens.Remove(done[i]);
        }

        private Vector2 ComputeCellAnchoredPosition(int row, int col, float tileSize, float offsetY)
        {
            return new Vector2(
                -boardSizePx * 0.5f + col * tileSize,
                boardSizePx * 0.5f - row * tileSize + offsetY);
        }

        private static float ResolveCombatUnitOffsetScale(int boardSize)
        {
            var safeSize = Mathf.Max(1, boardSize);
            // 4x4 is the authored baseline; larger boards reduce offset proportionally.
            return 4f / safeSize;
        }

        private float ResolveBoardScaledSpriteYOffset(UnitKind kind, Faction faction, int boardSize)
        {
            var offset = ResolveSpriteYOffset(kind);
            return IsCombatFaction(faction) ? offset * ResolveCombatUnitOffsetScale(boardSize) : offset;
        }

        private float ResolveBoardScaledShadeYOffset(UnitKind kind, Faction faction, int boardSize)
        {
            var offset = ResolveShadeYOffset(kind);
            return IsCombatFaction(faction) ? offset * ResolveCombatUnitOffsetScale(boardSize) : offset;
        }

        private static bool IsCombatFaction(Faction faction)
        {
            return faction == Faction.Player || faction == Faction.Enemy;
        }

        private void ApplyOverlayFrameState(Image overlayImage, string tileKey, Color overlayColor, ISet<string> moveTiles, ISet<string> attackTiles)
        {
            if (overlayImage == null || string.IsNullOrEmpty(tileKey)) return;

            var hasOverlay = overlayColor.a > 0.0001f;
            if (!hasOverlay)
            {
                overlayImage.sprite = null;
                overlayImage.enabled = false;
                return;
            }

            overlayImage.enabled = true;
            overlayImage.type = Image.Type.Simple;
            overlayImage.preserveAspect = false;

            var isAnimated = (moveTiles != null && moveTiles.Contains(tileKey)) ||
                             (attackTiles != null && attackTiles.Contains(tileKey));

            if (isAnimated)
            {
                _animatedOverlayKeys.Add(tileKey);
                overlayImage.sprite = ResolveAnimatedOverlayFrame();
            }
            else
            {
                _defaultOverlayKeys.Add(tileKey);
                overlayImage.sprite = ResolveDefaultOverlayFrame();
            }
        }

        private void UpdateOverlayAnimation(float deltaTime)
        {
            if (_animatedOverlayKeys.Count == 0) return;
            if (overlayAnimationFrames == null || overlayAnimationFrames.Length == 0) return;
            if (overlayAnimationFps <= 0f) return;

            _overlayAnimTimer += deltaTime;
            var frame = ResolveAnimatedOverlayFrame();
            if (frame == null) return;

            foreach (var key in _animatedOverlayKeys)
            {
                if (!_tileViews.TryGetValue(key, out var tile) || tile.overlay == null) continue;
                if (!tile.overlay.enabled) continue;
                tile.overlay.sprite = frame;
            }
        }

        private Sprite ResolveDefaultOverlayFrame()
        {
            if (overlayDefaultFrame != null) return overlayDefaultFrame;
            if (overlayAnimationFrames != null && overlayAnimationFrames.Length > 0) return overlayAnimationFrames[0];
            return null;
        }

        private Sprite ResolveAnimatedOverlayFrame()
        {
            if (overlayAnimationFrames == null || overlayAnimationFrames.Length == 0) return ResolveDefaultOverlayFrame();
            if (overlayAnimationFps <= 0f) return overlayAnimationFrames[0];

            var frameIndex = Mathf.FloorToInt(_overlayAnimTimer * overlayAnimationFps) % overlayAnimationFrames.Length;
            if (frameIndex < 0) frameIndex = 0;
            return overlayAnimationFrames[frameIndex];
        }
    }
}
