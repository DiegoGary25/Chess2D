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
            public RectTransform rect;
            public Image image;
            public TMP_Text label;
            public Button button;
            public UnitFrameAnimator animator;
            public SegmentedBarView hpBar;
            public Image statusIcon;
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

        private readonly Dictionary<string, TileView> _tileViews = new Dictionary<string, TileView>();
        private readonly Dictionary<string, PieceView> _pieceViews = new Dictionary<string, PieceView>();
        private readonly Dictionary<string, TrapView> _trapViews = new Dictionary<string, TrapView>();
        private readonly Dictionary<UnitKind, Sprite> _iconByKind = new Dictionary<UnitKind, Sprite>();
        private readonly Dictionary<UnitKind, UnitAnimationDefinition> _animationsByKind = new Dictionary<UnitKind, UnitAnimationDefinition>();
        private readonly Dictionary<UnitKind, float> _spriteYOffsetByKind = new Dictionary<UnitKind, float>();
        private readonly Dictionary<UnitKind, float> _visualScaleByKind = new Dictionary<UnitKind, float>();
        private readonly Dictionary<UnitKind, GameObject> _shadePrefabByKind = new Dictionary<UnitKind, GameObject>();
        private readonly Dictionary<UnitKind, float> _shadeYOffsetByKind = new Dictionary<UnitKind, float>();
        private readonly Dictionary<CardKind, Sprite> _cardIconByKind = new Dictionary<CardKind, Sprite>();
        private readonly Dictionary<string, int> _healFlashFrames = new Dictionary<string, int>();
        private readonly HashSet<string> _animatedOverlayKeys = new HashSet<string>();
        private readonly HashSet<string> _defaultOverlayKeys = new HashSet<string>();
        private GameConfigDefinition _cachedConfig;
        private bool _iconCacheInitialized;
        private int _cachedBoardSize = -1;
        private EncounterController _encounter;
        private bool _encounterSubscribed;
        private static readonly Color HealFlashColor = new Color(0.25f, 1f, 0.4f, 0.65f);
        private float _overlayAnimTimer;

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
                    var overlayColor = ResolveOverlayColor(p, key, selected, moveTiles, attackTiles, intentMoveTiles, intentAttackTiles);
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
                var spriteYOffset = ResolveSpriteYOffset(u.kind);
                var visualScale = ResolveVisualScale(u.kind);
                var shadeYOffset = ResolveShadeYOffset(u.kind);
                LayoutCell(view.rect, u.pos.row, u.pos.col, tileSize, tileGap + 6f, spriteYOffset, visualScale);
                if (view.shadeInstance != null)
                {
                    var showShade = view.currentShadePrefab != null;
                    view.shadeInstance.SetActive(showShade);
                    if (showShade)
                    {
                        if (view.shadeInstance.transform is RectTransform shadeRt)
                        {
                            LayoutCell(shadeRt, u.pos.row, u.pos.col, tileSize, tileGap + 10f, shadeYOffset, visualScale);
                            shadeRt.SetAsLastSibling();
                        }
                    }
                }
                view.rect.SetAsLastSibling();
                view.animator.SetSleeping(u.status != null && u.status.IsSleeping);
                UpdateShieldOverlay(view, u);
                UpdatePieceHpBar(view, u);
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
            _shadeYOffsetByKind.Clear();
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

        private GameObject ResolveShadePrefab(UnitKind kind)
        {
            return _shadePrefabByKind.TryGetValue(kind, out var prefab) ? prefab : null;
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
            ISet<string> intentAttackTiles)
        {
            if (selected != null && selected.pos.row == p.row && selected.pos.col == p.col) return selectedOverlay;
            if (attackTiles != null && attackTiles.Contains(key)) return attackOverlay;
            if (moveTiles != null && moveTiles.Contains(key)) return moveOverlay;
            if (intentAttackTiles != null && intentAttackTiles.Contains(key)) return intentAttackOverlay;
            if (intentMoveTiles != null && intentMoveTiles.Contains(key)) return intentMoveOverlay;
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
                statusRt.anchorMin = new Vector2(1f, 1f);
                statusRt.anchorMax = new Vector2(1f, 1f);
                statusRt.pivot = new Vector2(1f, 1f);
                statusRt.anchoredPosition = new Vector2(-2f, -2f);
                statusRt.sizeDelta = new Vector2(20f, 20f);
                var statusIcon = statusRt.gameObject.AddComponent<Image>();
                statusIcon.raycastTarget = false;
                statusIcon.enabled = false;

                var flashRt = NewRect("HealFlash", piece);
                flashRt.anchorMin = Vector2.zero;
                flashRt.anchorMax = Vector2.one;
                flashRt.offsetMin = Vector2.zero;
                flashRt.offsetMax = Vector2.zero;
                var flashOverlay = flashRt.gameObject.AddComponent<Image>();
                flashOverlay.raycastTarget = false;
                flashOverlay.color = new Color(0f, 0f, 0f, 0f);

                _pieceViews[id] = new PieceView
                {
                    unitId = id,
                    rect = piece,
                    image = img,
                    button = btn,
                    animator = animator,
                    hpBar = hpBar,
                    label = label,
                    statusIcon = statusIcon,
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
            var shadePrefab = ResolveShadePrefab(unit.kind);
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
        }

        private void HandleDamageDealt(string unitId, int _)
        {
            if (string.IsNullOrEmpty(unitId)) return;
            if (_pieceViews.TryGetValue(unitId, out var view) && view.animator != null)
            {
                view.animator.PlayHitOneShot();
            }
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

        private void LayoutCell(RectTransform rt, int row, int col, float tileSize, float inset, float offsetY = 0f, float scale = 1f)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(tileSize - inset, tileSize - inset);
            rt.anchoredPosition = new Vector2(-boardSizePx * 0.5f + col * tileSize, boardSizePx * 0.5f - row * tileSize + offsetY);
            rt.localScale = Vector3.one * Mathf.Max(0.01f, scale);
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

            foreach (var kv in _trapViews)
            {
                if (kv.Value.rect != null) Destroy(kv.Value.rect.gameObject);
            }
            _trapViews.Clear();
            _healFlashFrames.Clear();
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
            UpdateOverlayAnimation(Time.deltaTime);
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
