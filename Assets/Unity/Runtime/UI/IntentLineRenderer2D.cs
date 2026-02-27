using System.Collections.Generic;
using ChessPrototype.Unity.Data;
using ChessPrototype.Unity.Enemies;
using ChessPrototype.Unity.Encounters;
using UnityEngine;
using UnityEngine.UI;

namespace ChessPrototype.Unity.UI
{
    public sealed class IntentLineRenderer2D : MonoBehaviour
    {
        [Header("Board Binding")]
        [SerializeField] private BoardUiGenerator boardUiGenerator;
        [SerializeField] private EncounterController encounterController;
        [SerializeField] private RectTransform root;
        [SerializeField] private float boardSizePx = 560f;

        [Header("Colors")]
        [SerializeField] private Color attackColor = new Color(1f, 0.28f, 0.2f, 0.95f);
        [SerializeField] private Color rangedArrowColor = new Color(0.62f, 0.35f, 0.95f, 0.95f);
        [SerializeField] private Color attackMarkerColor = new Color(1f, 0.2f, 0.2f, 0.35f);
        [SerializeField] private Color selectedEnemyIntentColor = new Color(1f, 0.55f, 0.15f, 0.95f);
        [SerializeField] private Color selectedEnemyIntentMarkerColor = new Color(1f, 0.55f, 0.15f, 0.35f);

        [Header("Sprites")]
        [SerializeField] private Sprite meleeEdgeArrowSprite;
        [SerializeField] private Sprite rangedArcSprite;
        [SerializeField] private Sprite rangedMiddleSprite;
        [SerializeField] private Sprite landingArrowSprite;
        [SerializeField] private Sprite attackTileMarkerSprite;
        [Header("RAM Sprites (Start / Middle / End)")]
        [SerializeField] private Sprite ramStartSprite;
        [SerializeField] private Sprite ramMiddleSprite;
        [SerializeField] private Sprite ramEndSprite;

        [Header("Animation (Optional)")]
        [SerializeField] private Sprite[] meleeEdgeArrowFrames;
        [SerializeField] private Sprite[] rangedArcFrames;
        [SerializeField] private Sprite[] rangedMiddleFrames;
        [SerializeField] private Sprite[] landingArrowFrames;
        [SerializeField] private Sprite[] attackTileMarkerFrames;
        [SerializeField] private Sprite[] ramStartFrames;
        [SerializeField] private Sprite[] ramMiddleFrames;
        [SerializeField] private Sprite[] ramEndFrames;
        [SerializeField, Min(0f)] private float animationFps = 2f;

        [Header("Sizing")]
        [SerializeField, Range(0.2f, 0.6f)] private float meleeArrowSizeFactor = 0.34f;
        [SerializeField, Range(0.2f, 0.6f)] private float rangedSegmentSizeFactor = 0.34f;
        [SerializeField, Range(0.2f, 0.6f)] private float rangedArrowSizeFactor = 0.34f;
        [SerializeField, Range(0.2f, 0.6f)] private float rangedMiddleSizeFactor = 0.34f;
        [SerializeField, Range(0.25f, 0.6f)] private float landingArrowSizeFactor = 0.34f;
        [SerializeField, Range(0.2f, 0.8f)] private float edgeOffsetFactor = 0.33f;
        [SerializeField, Range(0.3f, 1f)] private float markerSizeFactor = 0.75f;
        [Header("Alignment Tuning (pixels)")]
        [SerializeField] private Vector2 arrowGlobalOffsetPx = Vector2.zero;
        [SerializeField] private Vector2 markerGlobalOffsetPx = Vector2.zero;
        [SerializeField] private Vector2 meleeArrowOffsetPx = Vector2.zero;
        [SerializeField] private Vector2 rangedArrowOffsetPx = Vector2.zero;
        [SerializeField] private Vector2 rangedMiddleOffsetPx = Vector2.zero;
        [SerializeField] private Vector2 landingArrowOffsetPx = Vector2.zero;
        [SerializeField] private Vector2 ramArrowOffsetPx = Vector2.zero;
        [SerializeField] private Vector2 markerOffsetPx = Vector2.zero;
        [SerializeField] private bool fitSpritesToRect = true;

        [Header("Pixel Snapping")]
        [SerializeField] private bool pixelSnap = true;

        private sealed class PooledImage
        {
            public GameObject go;
            public RectTransform rt;
            public Image img;
        }

        private enum TelegraphMode
        {
            Melee,
            Ranged,
            Ram
        }

        private readonly List<PooledImage> _pool = new List<PooledImage>();
        private int _activeCount;
        private RectTransform _container;
        private static Sprite _solidSprite;
        private EnemyPlan _lastPlan;
        private int _lastAnimFrame = -1;
        private string _emphasizedActorId;

        public void RenderPlan(EnemyPlan plan)
        {
            _lastPlan = plan;
            _lastAnimFrame = ResolveAnimationFrameIndex();
            RenderCurrentPlan();
        }

        public void SetIntentEmphasis(string emphasizedActorId)
        {
            if (_emphasizedActorId == emphasizedActorId) return;
            _emphasizedActorId = emphasizedActorId;
            RenderCurrentPlan();
        }

        private void Update()
        {
            if (_lastPlan == null || animationFps <= 0f) return;

            var frame = ResolveAnimationFrameIndex();
            if (frame == _lastAnimFrame) return;
            _lastAnimFrame = frame;
            RenderCurrentPlan();
        }

        private void RenderCurrentPlan()
        {
            Clear();
            if (_lastPlan == null) return;

            EnsureBoardBinding();
            if (boardUiGenerator != null)
            {
                if (root == null) root = boardUiGenerator.RootRect;
                boardSizePx = boardUiGenerator.BoardSizePx;
            }

            if (root == null) root = transform as RectTransform;
            if (root == null) return;

            EnsureContainer();
            if (_container != null) _container.SetAsLastSibling();

            var boardSize = ResolveBoardSize(_lastPlan);
            var tile = boardSizePx / boardSize;

            for (var i = 0; i < _lastPlan.intents.Count; i++)
            {
                var it = _lastPlan.intents[i];
                if (it.attackSquares == null || it.attackSquares.Count == 0) continue;
                var emphasized = !string.IsNullOrEmpty(_emphasizedActorId) && it.actorId == _emphasizedActorId;

                var focus = PickFocusSquare(it);
                var line = TryResolveLineAttack(it.from, it.attackSquares, focus, out var step, out var maxLen);
                var mode = ResolveMode(it, line, maxLen);
                var markerTarget = focus;

                if (mode == TelegraphMode.Ranged)
                {
                    var effectiveLen = ResolveRangedImpactLen(it.from, step, maxLen, boardSize);
                    if (effectiveLen <= 0) continue;
                    var first = new GridPos(it.from.row + step.row, it.from.col + step.col);
                    var target = new GridPos(it.from.row + step.row * effectiveLen, it.from.col + step.col * effectiveLen);
                    markerTarget = target;
                    RenderStepArrow(it.from, first, tile, rangedArcFrames, rangedArcSprite, rangedArrowOffsetPx, "IntentRangedStartArrow", emphasized, rangedArrowColor, rangedArrowSizeFactor);
                    if (effectiveLen > 1)
                    {
                        var lastBefore = new GridPos(it.from.row + step.row * (effectiveLen - 1), it.from.col + step.col * (effectiveLen - 1));
                        RenderStepArrow(lastBefore, target, tile, landingArrowFrames, landingArrowSprite, landingArrowOffsetPx, "IntentRangedLandingArrow", emphasized, rangedArrowColor, landingArrowSizeFactor);
                    }
                    RenderRangedMiddleSprites(it.from, step, effectiveLen, tile, emphasized);
                }
                else if (mode == TelegraphMode.Ram)
                {
                    var clampedLen = ClampLenToBoard(it.from, step, maxLen, boardSize);
                    if (clampedLen > 0)
                    {
                        var first = new GridPos(it.from.row + step.row, it.from.col + step.col);
                        var target = new GridPos(it.from.row + step.row * clampedLen, it.from.col + step.col * clampedLen);
                        RenderStepArrow(it.from, first, tile, ramStartFrames, ramStartSprite, ramArrowOffsetPx, "IntentRamStartArrow", emphasized, attackColor, rangedSegmentSizeFactor);
                        if (clampedLen > 1)
                        {
                            var lastBefore = new GridPos(it.from.row + step.row * (clampedLen - 1), it.from.col + step.col * (clampedLen - 1));
                            RenderStepArrow(lastBefore, target, tile, ramEndFrames, ramEndSprite, ramArrowOffsetPx, "IntentRamEndArrow", emphasized, attackColor, rangedSegmentSizeFactor);
                        }
                        RenderRamMiddleSprites(it.from, step, clampedLen, tile, emphasized);
                    }
                }
                else
                {
                    if (IsInsideBoard(focus, boardSize))
                    {
                        RenderMeleeEdgeArrow(it.from, focus, tile, emphasized);
                    }
                }

                if (mode == TelegraphMode.Ranged)
                {
                    if (IsInsideBoard(markerTarget, boardSize))
                    {
                        RenderAttackMarker(markerTarget, tile, emphasized);
                    }
                }
                else
                {
                    for (var s = 0; s < it.attackSquares.Count; s++)
                    {
                        var sq = it.attackSquares[s];
                        if (!IsInsideBoard(sq, boardSize)) continue;
                        RenderAttackMarker(sq, tile, emphasized);
                    }
                }
            }
        }

        private int ResolveRangedImpactLen(GridPos from, GridPos step, int wantedLen, int boardSize)
        {
            var clampedLen = ClampLenToBoard(from, step, wantedLen, boardSize);
            if (clampedLen <= 0) return 0;

            if (encounterController == null || encounterController.Board == null) return clampedLen;

            var board = encounterController.Board;
            for (var i = 1; i <= clampedLen; i++)
            {
                var p = new GridPos(from.row + step.row * i, from.col + step.col * i);
                if (board.Occupied(p)) return i;
            }

            return clampedLen;
        }

        public void Clear()
        {
            for (var i = 0; i < _activeCount; i++)
            {
                var p = _pool[i];
                if (p != null && p.go != null) p.go.SetActive(false);
            }
            _activeCount = 0;
        }

        private static TelegraphMode ResolveMode(EnemyIntent it, bool lineAttack, int maxLen)
        {
            if (lineAttack && maxLen > 1)
            {
                return it.actorKind == UnitKind.Bear ? TelegraphMode.Ram : TelegraphMode.Ranged;
            }
            return TelegraphMode.Melee;
        }

        private void RenderMeleeEdgeArrow(GridPos from, GridPos to, float tile, bool emphasized)
        {
            var fromPos = ToUiPos(from.row, from.col, tile);
            var toPos = ToUiPos(to.row, to.col, tile);
            var dir = toPos - fromPos;
            if (dir.sqrMagnitude <= 0.001f) dir = new Vector2(0f, 1f);
            else dir.Normalize();

            var angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            var step = DirectionStep(from, to);
            var axisOffset = StepToUiAxis(step) * (tile * edgeOffsetFactor);
            var edgePos = fromPos + axisOffset + arrowGlobalOffsetPx + meleeArrowOffsetPx;
            var size = tile * meleeArrowSizeFactor;

            var arrow = GetPooled("IntentMeleeEdgeArrow");
            arrow.img.sprite = ResolveAnimatedSprite(meleeEdgeArrowFrames, meleeEdgeArrowSprite);
            arrow.img.type = Image.Type.Simple;
            arrow.img.preserveAspect = !fitSpritesToRect;
            arrow.img.color = ResolveArrowColor(emphasized, attackColor);
            arrow.img.raycastTarget = false;

            SetCentered(arrow.rt, edgePos, new Vector2(size, size), angle);
        }

        private void RenderStepArrow(
            GridPos from,
            GridPos to,
            float tile,
            Sprite[] frames,
            Sprite fallback,
            Vector2 typeOffsetPx,
            string poolName,
            bool emphasized,
            Color baseColor,
            float sizeFactor)
        {
            var fromPos = ToUiPos(from.row, from.col, tile);
            var toPos = ToUiPos(to.row, to.col, tile);
            var dir = toPos - fromPos;
            if (dir.sqrMagnitude <= 0.001f) dir = new Vector2(0f, 1f);
            else dir.Normalize();

            var angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            var size = tile * sizeFactor;
            var step = DirectionStep(from, to);
            var axisOffset = StepToUiAxis(step) * (tile * edgeOffsetFactor);
            var edgePos = toPos - axisOffset + arrowGlobalOffsetPx + typeOffsetPx;

            var arrow = GetPooled(poolName);
            arrow.img.sprite = ResolveAnimatedSprite(frames, fallback);
            arrow.img.type = Image.Type.Simple;
            arrow.img.preserveAspect = !fitSpritesToRect;
            arrow.img.color = ResolveArrowColor(emphasized, baseColor);
            arrow.img.raycastTarget = false;

            SetCentered(arrow.rt, edgePos, new Vector2(size, size), angle);
        }

        private void RenderRangedMiddleSprites(GridPos from, GridPos step, int len, float tile, bool emphasized)
        {
            if (len <= 1) return;
            var angle = Mathf.Atan2(-step.row, step.col) * Mathf.Rad2Deg;
            var size = tile * rangedMiddleSizeFactor;

            // Center markers on each traversed tile before impact.
            for (var i = 1; i <= len - 1; i++)
            {
                var p = new GridPos(from.row + step.row * i, from.col + step.col * i);
                var pos = ToUiPos(p.row, p.col, tile) + arrowGlobalOffsetPx + rangedMiddleOffsetPx;

                var img = GetPooled("IntentRangedMiddleCenter");
                img.img.sprite = ResolveAnimatedSprite(rangedMiddleFrames, rangedMiddleSprite);
                img.img.type = Image.Type.Simple;
                img.img.preserveAspect = !fitSpritesToRect;
                img.img.color = ResolveArrowColor(emphasized, rangedArrowColor);
                img.img.raycastTarget = false;

                SetCentered(img.rt, pos, new Vector2(size, size), angle);
            }

            // Edge markers between traversed tiles (excluding start/landing arrow edges).
            for (var i = 1; i <= len - 2; i++)
            {
                var a = new GridPos(from.row + step.row * i, from.col + step.col * i);
                var b = new GridPos(from.row + step.row * (i + 1), from.col + step.col * (i + 1));
                var aPos = ToUiPos(a.row, a.col, tile);
                var bPos = ToUiPos(b.row, b.col, tile);
                var edgePos = (aPos + bPos) * 0.5f + arrowGlobalOffsetPx + rangedMiddleOffsetPx;

                var img = GetPooled("IntentRangedMiddleEdge");
                img.img.sprite = ResolveAnimatedSprite(rangedMiddleFrames, rangedMiddleSprite);
                img.img.type = Image.Type.Simple;
                img.img.preserveAspect = !fitSpritesToRect;
                img.img.color = ResolveArrowColor(emphasized, rangedArrowColor);
                img.img.raycastTarget = false;

                SetCentered(img.rt, edgePos, new Vector2(size, size), angle);
            }
        }

        private void RenderRamMiddleSprites(GridPos from, GridPos step, int len, float tile, bool emphasized)
        {
            if (len <= 2) return;
            var angle = Mathf.Atan2(-step.row, step.col) * Mathf.Rad2Deg;
            var size = tile * rangedSegmentSizeFactor;

            for (var i = 2; i <= len - 1; i++)
            {
                var p = new GridPos(from.row + step.row * i, from.col + step.col * i);
                var pos = ToUiPos(p.row, p.col, tile) + arrowGlobalOffsetPx + ramArrowOffsetPx;

                var img = GetPooled("IntentRamMiddle");
                img.img.sprite = ResolveAnimatedSprite(ramMiddleFrames, ramMiddleSprite);

                img.img.type = Image.Type.Simple;
                img.img.preserveAspect = !fitSpritesToRect;
                img.img.color = ResolveArrowColor(emphasized, attackColor);
                img.img.raycastTarget = false;

                SetCentered(img.rt, pos, new Vector2(size, size), angle);
            }
        }

        private void RenderAttackMarker(GridPos target, float tile, bool emphasized)
        {
            var pos = ToUiPos(target.row, target.col, tile) + markerGlobalOffsetPx + markerOffsetPx;
            var size = tile * markerSizeFactor;

            var marker = GetPooled("IntentAttackMarker");
            marker.img.sprite = ResolveAnimatedSprite(attackTileMarkerFrames, attackTileMarkerSprite);
            marker.img.type = Image.Type.Simple;
            marker.img.preserveAspect = !fitSpritesToRect;
            marker.img.color = ResolveMarkerColor(emphasized);
            marker.img.raycastTarget = false;

            SetCentered(marker.rt, pos, new Vector2(size, size), 0f);
        }

        private void EnsureContainer()
        {
            if (_container != null && _container.transform.parent == root)
            {
                _container.SetAsLastSibling();
                return;
            }

            var go = new GameObject("IntentArrows", typeof(RectTransform));
            go.transform.SetParent(root, false);

            _container = (RectTransform)go.transform;
            _container.anchorMin = new Vector2(0.5f, 0.5f);
            _container.anchorMax = new Vector2(0.5f, 0.5f);
            _container.pivot = new Vector2(0.5f, 0.5f);
            _container.sizeDelta = Vector2.zero;
            _container.anchoredPosition = Vector2.zero;
            _container.SetAsLastSibling();
        }

        private PooledImage GetPooled(string name)
        {
            if (_activeCount < _pool.Count)
            {
                var p = _pool[_activeCount++];
                p.go.name = name;
                p.go.SetActive(true);
                p.rt.SetAsLastSibling();
                return p;
            }

            var created = CreatePooled(name);
            _pool.Add(created);
            _activeCount++;
            return created;
        }

        private PooledImage CreatePooled(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_container != null ? _container : root, false);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.sprite = GetSolidSprite();

            return new PooledImage { go = go, rt = rt, img = img };
        }

        private void SetCentered(RectTransform rt, Vector2 pos, Vector2 size, float rotationDeg)
        {
            if (pixelSnap)
            {
                pos = Snap(pos);
                size = new Vector2(Mathf.Round(size.x), Mathf.Round(size.y));
            }

            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            rt.localRotation = Quaternion.Euler(0f, 0f, rotationDeg);
            rt.SetAsLastSibling();
        }

        private Vector2 ToUiPos(int row, int col, float tileSize)
        {
            var x = -boardSizePx * 0.5f + col * tileSize + tileSize * 0.5f;
            var y = boardSizePx * 0.5f - row * tileSize - tileSize * 0.5f;
            return new Vector2(x, y);
        }

        private static GridPos PickFocusSquare(EnemyIntent it)
        {
            var best = it.attackSquares[0];
            var bestDist = -1;
            for (var i = 0; i < it.attackSquares.Count; i++)
            {
                var sq = it.attackSquares[i];
                var d = ChebyshevDistance(it.from, sq);
                if (d > bestDist)
                {
                    bestDist = d;
                    best = sq;
                }
            }
            return best;
        }

        private static bool TryResolveLineAttack(GridPos from, List<GridPos> squares, GridPos focus, out GridPos step, out int maxLen)
        {
            step = DirectionStep(from, focus);
            maxLen = 0;
            if (step.row == 0 && step.col == 0) return false;

            for (var i = 0; i < squares.Count; i++)
            {
                var relR = squares[i].row - from.row;
                var relC = squares[i].col - from.col;

                int k;
                if (step.row != 0)
                {
                    if (relC != 0) return false;
                    if (step.row > 0 && relR <= 0) return false;
                    if (step.row < 0 && relR >= 0) return false;
                    if (Mathf.Abs(relR) % Mathf.Abs(step.row) != 0) return false;
                    k = Mathf.Abs(relR / step.row);
                }
                else
                {
                    if (relR != 0) return false;
                    if (step.col > 0 && relC <= 0) return false;
                    if (step.col < 0 && relC >= 0) return false;
                    if (Mathf.Abs(relC) % Mathf.Abs(step.col) != 0) return false;
                    k = Mathf.Abs(relC / step.col);
                }

                if (k > maxLen) maxLen = k;
            }

            return maxLen > 0;
        }

        private static GridPos DirectionStep(GridPos from, GridPos to)
        {
            var dr = to.row - from.row;
            var dc = to.col - from.col;
            if (Mathf.Abs(dr) >= Mathf.Abs(dc)) return new GridPos(dr == 0 ? 0 : (dr > 0 ? 1 : -1), 0);
            return new GridPos(0, dc == 0 ? 0 : (dc > 0 ? 1 : -1));
        }

        private static Vector2 StepToUiAxis(GridPos step)
        {
            return new Vector2(step.col, -step.row);
        }

        private static int ClampLenToBoard(GridPos from, GridPos step, int wantedLen, int boardSize)
        {
            var len = 0;
            for (var i = 1; i <= wantedLen; i++)
            {
                var p = new GridPos(from.row + step.row * i, from.col + step.col * i);
                if (!IsInsideBoard(p, boardSize)) break;
                len = i;
            }
            return len;
        }

        private static bool IsInsideBoard(GridPos p, int boardSize)
        {
            return p.row >= 0 && p.col >= 0 && p.row < boardSize && p.col < boardSize;
        }

        private static int ChebyshevDistance(GridPos a, GridPos b)
        {
            return Mathf.Max(Mathf.Abs(a.row - b.row), Mathf.Abs(a.col - b.col));
        }

        private int ResolveAnimationFrameIndex()
        {
            if (animationFps <= 0f) return 0;
            return Mathf.FloorToInt(Time.unscaledTime * animationFps);
        }

        private Sprite ResolveAnimatedSprite(Sprite[] frames, Sprite fallback)
        {
            if (frames != null && frames.Length > 0)
            {
                var frame = ResolveAnimationFrameIndex();
                var idx = Mathf.Abs(frame) % frames.Length;
                var sprite = frames[idx];
                if (sprite != null) return sprite;
            }

            if (fallback != null) return fallback;
            return GetSolidSprite();
        }

        private Color ResolveArrowColor(bool emphasized, Color baseColor)
        {
            return emphasized ? selectedEnemyIntentColor : baseColor;
        }

        private Color ResolveMarkerColor(bool emphasized)
        {
            return emphasized ? selectedEnemyIntentMarkerColor : attackMarkerColor;
        }

        private static Vector2 Snap(Vector2 v)
        {
            return new Vector2(Mathf.Round(v.x), Mathf.Round(v.y));
        }

        private static Sprite GetSolidSprite()
        {
            if (_solidSprite != null) return _solidSprite;
            var tex = Texture2D.whiteTexture;
            _solidSprite = Sprite.Create(
                tex,
                new Rect(0f, 0f, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                100f);
            return _solidSprite;
        }

        private static int InferBoardSize(EnemyPlan plan)
        {
            var maxIndex = 3;
            for (var i = 0; i < plan.intents.Count; i++)
            {
                var it = plan.intents[i];
                maxIndex = Mathf.Max(maxIndex, Mathf.Max(it.from.row, it.from.col));
                maxIndex = Mathf.Max(maxIndex, Mathf.Max(it.to.row, it.to.col));
                if (it.attackSquares == null) continue;
                for (var s = 0; s < it.attackSquares.Count; s++)
                {
                    maxIndex = Mathf.Max(maxIndex, Mathf.Max(it.attackSquares[s].row, it.attackSquares[s].col));
                }
            }
            return Mathf.Max(4, maxIndex + 1);
        }

                private int ResolveBoardSize(EnemyPlan plan)
        {
            if (encounterController != null && encounterController.Board != null && encounterController.Board.Size > 0)
            {
                return encounterController.Board.Size;
            }

            return InferBoardSize(plan);
        }
        private void EnsureBoardBinding()
        {
            if (boardUiGenerator == null) boardUiGenerator = FindObjectOfType<BoardUiGenerator>();
            if (encounterController == null) encounterController = FindObjectOfType<EncounterController>();
        }
    }
}

