using System;
using System.Collections.Generic;
using ChessPrototype.Unity.Enemies;
using UnityEngine;
using UnityEngine.UI;

namespace ChessPrototype.Unity.UI
{
    public sealed class IntentLineRenderer2D : MonoBehaviour
    {
        [Header("Board Binding")]
        [SerializeField] private BoardUiGenerator boardUiGenerator;
        [SerializeField] private RectTransform root;
        [SerializeField] private float boardSizePx = 560f;

        [Header("Colors")]
        [SerializeField] private Color moveColor = new Color(0.2f, 0.9f, 0.85f, 0.9f);
        [SerializeField] private Color attackColor = new Color(1f, 0.3f, 0.4f, 0.95f);

        [Header("Sizing (relative to tile)")]
        [Tooltip("Arrow thickness = tileSize * this factor. Try 0.12 - 0.18 for 64px tiles.")]
        [SerializeField, Range(0.05f, 0.30f)] private float thicknessFactor = 0.14f;

        [Tooltip("Segment size inside a tile (1 = fill tile). Usually 0.85 - 1.0 looks best.")]
        [SerializeField, Range(0.5f, 1.2f)] private float segmentTileScale = 0.92f;

        [Tooltip("Head size relative to thickness.")]
        [SerializeField, Range(1.2f, 3.5f)] private float headLengthMultiplier = 2.2f;

        [SerializeField, Range(1.2f, 3.5f)] private float headWidthMultiplier = 2.4f;

        [Header("Rendering")]
        [SerializeField] private RenderMode renderMode = RenderMode.Segmented;

        [Tooltip("Hide body segments on the starting tile (recommended so you don't cover the piece).")]
        [SerializeField] private bool skipStartTile = true;

        [Tooltip("If move is 1 tile long, you might still want a head.")]
        [SerializeField] private bool showHeadOnOneStep = true;

        [Header("Sprites (pixel art recommended)")]
        [Tooltip("Body segment sprite. Ideally a horizontal chunk with outline, designed for pixel art.")]
        [SerializeField] private Sprite segmentSprite;

        [Tooltip("Arrow head sprite. Ideally pointing RIGHT in the texture.")]
        [SerializeField] private Sprite headSprite;

        [Tooltip("Optional ring/marker on the destination tile (under the head).")]
        [SerializeField] private Sprite destinationMarkerSprite;

        [Header("Attack visualization (recommended: lines unless marker sprites are assigned)")]
        [SerializeField] private AttackVisualization attackVisualization = AttackVisualization.Lines;

        [Tooltip("Optional marker sprite for attacked tiles (X, ring, hazard icon).")]
        [SerializeField] private Sprite attackTileMarkerSprite;

        [SerializeField, Range(0.2f, 1f)] private float attackMarkerAlpha = 0.35f;

        [SerializeField, Range(0.4f, 1.2f)] private float attackMarkerScale = 0.92f;

        [Tooltip("If you still want lines for attack squares, this controls their thickness relative to main arrow.")]
        [SerializeField, Range(0.3f, 1f)] private float attackLineThicknessMultiplier = 0.6f;

        [Header("Pixel snapping")]
        [SerializeField] private bool pixelSnap = true;

        private enum RenderMode { Segmented, RotatedTiled }
        private enum AttackVisualization { None, TileMarkers, Lines }

        private sealed class PooledImage
        {
            public GameObject go;
            public RectTransform rt;
            public Image img;
        }

        private readonly List<PooledImage> _pool = new List<PooledImage>();
        private int _activeCount;

        private RectTransform _container;

        public void RenderPlan(EnemyPlan plan)
        {
            Clear();
            if (plan == null) return;

            EnsureBoardBinding();

            if (boardUiGenerator != null)
            {
                if (root == null) root = boardUiGenerator.RootRect;
                boardSizePx = boardUiGenerator.BoardSizePx;
            }

            if (root == null) root = transform as RectTransform;
            if (root == null) return;

            EnsureContainer();

            var boardSize = InferBoardSize(plan);
            var tile = boardSizePx / boardSize;

            // Thickness is tile-relative so it reads well in pixel art.
            var thickness = Mathf.Max(1f, Mathf.Round(tile * thicknessFactor));

            for (var i = 0; i < plan.intents.Count; i++)
            {
                var it = plan.intents[i];

                var color = it.kind == EnemyIntentKind.Capture ? attackColor : moveColor;

                // Main arrow
                RenderArrow(it.from.row, it.from.col, it.to.row, it.to.col, color, tile, thickness);

                // Attacks
                if (it.attackSquares == null) continue;

                switch (attackVisualization)
                {
                    case AttackVisualization.None:
                        break;

                    case AttackVisualization.TileMarkers:
                        // If no marker sprites are assigned, fallback to lines so intent is still visible.
                        var hasMarkerSprite = attackTileMarkerSprite != null || destinationMarkerSprite != null;
                        if (!hasMarkerSprite)
                        {
                            var fallbackThickness = Mathf.Max(1f, Mathf.Round(thickness * attackLineThicknessMultiplier));
                            for (var s = 0; s < it.attackSquares.Count; s++)
                            {
                                var sq = it.attackSquares[s];
                                RenderArrow(it.to.row, it.to.col, sq.row, sq.col, attackColor, tile, fallbackThickness, isAttackLine: true);
                            }
                            break;
                        }

                        for (var s = 0; s < it.attackSquares.Count; s++)
                        {
                            var sq = it.attackSquares[s];
                            RenderAttackMarker(sq.row, sq.col, tile);
                        }
                        break;

                    case AttackVisualization.Lines:
                        {
                            var atkThickness = Mathf.Max(1f, Mathf.Round(thickness * attackLineThicknessMultiplier));
                            for (var s = 0; s < it.attackSquares.Count; s++)
                            {
                                var sq = it.attackSquares[s];
                                RenderArrow(it.to.row, it.to.col, sq.row, sq.col, attackColor, tile, atkThickness, isAttackLine: true);
                            }
                        }
                        break;
                }
            }
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

        private void RenderArrow(int fromRow, int fromCol, int toRow, int toCol, Color color, float tile, float thickness, bool isAttackLine = false)
        {
            // Validate 8-direction line (rook/bishop style). If not straight/diag, fallback to rotated.
            var dr = toRow - fromRow;
            var dc = toCol - fromCol;
            if (dr == 0 && dc == 0) return;

            var isStraight = (dr == 0) || (dc == 0) || (Mathf.Abs(dr) == Mathf.Abs(dc));

            if (renderMode == RenderMode.Segmented && isStraight && segmentSprite != null && headSprite != null)
            {
                RenderArrowSegmented(fromRow, fromCol, toRow, toCol, color, tile, thickness, isAttackLine);
            }
            else
            {
                // Fallback: rotated UI arrow (still improved: snapped + tile-relative thickness).
                var fromUi = ToUiPos(fromRow, fromCol, tile);
                var toUi = ToUiPos(toRow, toCol, tile);
                RenderArrowRotated(fromUi, toUi, color, thickness, tile, showHead: !isAttackLine);
            }
        }

        private void RenderArrowSegmented(int fromRow, int fromCol, int toRow, int toCol, Color color, float tile, float thickness, bool isAttackLine)
        {
            var dr = toRow - fromRow;
            var dc = toCol - fromCol;

            var stepR = Math.Sign(dr);
            var stepC = Math.Sign(dc);

            var steps = Mathf.Max(Mathf.Abs(dr), Mathf.Abs(dc));
            if (steps <= 0) return;

            // Optional destination ring (under head)
            if (!isAttackLine && destinationMarkerSprite != null)
            {
                var dest = ToUiPos(toRow, toCol, tile);
                RenderTileMarker(dest, destinationMarkerSprite, color * new Color(1f, 1f, 1f, 0.25f), tile * 0.95f);
            }

            // Body segments: place on intermediate tiles.
            // Skipping the start tile prevents covering the piece.
            var startIndex = skipStartTile ? 1 : 0;
            var endIndex = steps - 1; // last index BEFORE the destination tile

            for (var i = startIndex; i <= endIndex; i++)
            {
                // If it's a one-step move (steps==1), there are no body segments; optionally show head only.
                if (steps == 1) break;

                var r = fromRow + stepR * i;
                var c = fromCol + stepC * i;

                var pos = ToUiPos(r, c, tile);
                var p = GetPooled("ArrowSeg");
                p.img.sprite = segmentSprite;
                p.img.type = Image.Type.Simple;
                p.img.preserveAspect = true;
                p.img.color = color;
                p.img.raycastTarget = false;

                // Segment size sits nicely inside each tile.
                var segSize = Mathf.Round(tile * segmentTileScale);
                SetCentered(p.rt, pos, new Vector2(segSize, segSize), rotationDeg: DirectionToAngle(stepR, stepC));
            }

            // Head: on destination tile.
            if (steps == 1 && !showHeadOnOneStep) return;

            {
                var pos = ToUiPos(toRow, toCol, tile);
                var head = GetPooled("ArrowHead");
                head.img.sprite = headSprite;
                head.img.type = Image.Type.Simple;
                head.img.preserveAspect = true;
                head.img.color = color;
                head.img.raycastTarget = false;

                var headLen = Mathf.Round(thickness * headLengthMultiplier);
                var headW = Mathf.Round(thickness * headWidthMultiplier);

                // Head sits inside tile; clamp to tile size so it doesn't explode on small boards.
                headLen = Mathf.Min(headLen, tile * 1.05f);
                headW = Mathf.Min(headW, tile * 1.05f);

                SetCentered(head.rt, pos, new Vector2(headLen, headW), rotationDeg: DirectionToAngle(stepR, stepC));
            }
        }

        private void RenderAttackMarker(int row, int col, float tile)
        {
            var pos = ToUiPos(row, col, tile);
            var sprite = attackTileMarkerSprite != null ? attackTileMarkerSprite : destinationMarkerSprite;
            if (sprite == null) return;

            var c = attackColor;
            c.a = attackMarkerAlpha;

            var size = Mathf.Round(tile * attackMarkerScale);
            RenderTileMarker(pos, sprite, c, size);
        }

        private void RenderTileMarker(Vector2 pos, Sprite sprite, Color color, float size)
        {
            var p = GetPooled("TileMarker");
            p.img.sprite = sprite;
            p.img.type = Image.Type.Simple;
            p.img.preserveAspect = true;
            p.img.color = color;
            p.img.raycastTarget = false;

            SetCentered(p.rt, pos, new Vector2(size, size), rotationDeg: 0f);
        }

        // Fallback rotated arrow (still better: snapped positions, tile-relative sizing)
        private void RenderArrowRotated(Vector2 from, Vector2 to, Color color, float thickness, float tile, bool showHead)
        {
            if (from == to) return;

            if (pixelSnap)
            {
                from = Snap(from);
                to = Snap(to);
                thickness = Mathf.Round(thickness);
            }

            var dir = to - from;
            var len = dir.magnitude;
            if (len <= 0.001f) return;

            var angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

            // Inset so it doesn't cover pieces as much
            var n = dir / len;
            var inset = tile * 0.35f;
            var fromInset = from + n * inset;
            var toInset = to - n * (tile * 0.25f);

            if (pixelSnap)
            {
                fromInset = Snap(fromInset);
                toInset = Snap(toInset);
            }

            dir = toInset - fromInset;
            len = dir.magnitude;
            if (len <= 0.001f) return;

            // BODY
            var body = GetPooled("ArrowBodyRot");
            body.img.sprite = segmentSprite;
            body.img.color = color;
            body.img.raycastTarget = false;

            // For rotated fallback, tiled looks better than sliced for pixel bars.
            body.img.type = (segmentSprite != null) ? Image.Type.Tiled : Image.Type.Simple;
            body.img.preserveAspect = false;

            body.rt.anchorMin = new Vector2(0.5f, 0.5f);
            body.rt.anchorMax = new Vector2(0.5f, 0.5f);
            body.rt.pivot = new Vector2(0f, 0.5f);
            body.rt.anchoredPosition = fromInset;
            body.rt.sizeDelta = new Vector2(pixelSnap ? Mathf.Round(len) : len, thickness);
            body.rt.localRotation = Quaternion.Euler(0f, 0f, angle);

            body.rt.SetAsLastSibling();

            // HEAD
            if (showHead && headSprite != null)
            {
                var head = GetPooled("ArrowHeadRot");
                head.img.sprite = headSprite;
                head.img.color = color;
                head.img.raycastTarget = false;
                head.img.type = Image.Type.Simple;
                head.img.preserveAspect = true;

                var headLen = Mathf.Round(thickness * headLengthMultiplier);
                var headW = Mathf.Round(thickness * headWidthMultiplier);

                headLen = Mathf.Min(headLen, tile * 1.05f);
                headW = Mathf.Min(headW, tile * 1.05f);

                head.rt.anchorMin = new Vector2(0.5f, 0.5f);
                head.rt.anchorMax = new Vector2(0.5f, 0.5f);
                head.rt.pivot = new Vector2(0.5f, 0.5f);
                head.rt.anchoredPosition = toInset;
                head.rt.sizeDelta = new Vector2(headLen, headW);
                head.rt.localRotation = Quaternion.Euler(0f, 0f, angle);

                head.rt.SetAsLastSibling();
            }
        }

        private void EnsureContainer()
        {
            if (_container != null && _container.transform.parent == root) return;

            // Create a dedicated container so arrows always render on top.
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

            return new PooledImage
            {
                go = go,
                rt = rt,
                img = img
            };
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

        private static Vector2 Snap(Vector2 v) => new Vector2(Mathf.Round(v.x), Mathf.Round(v.y));

        // stepR/stepC are in board space (rows increase downward).
        // UI y is inverted, so angle mapping is:
        // East (0,+1) -> 0°
        // NE (-1,+1) -> 45°
        // North (-1,0) -> 90°
        // NW (-1,-1) -> 135°
        // West (0,-1) -> 180°
        // SW (+1,-1) -> -135°
        // South (+1,0) -> -90°
        // SE (+1,+1) -> -45°
        private static float DirectionToAngle(int stepR, int stepC)
        {
            if (stepR == 0 && stepC > 0) return 0f;
            if (stepR < 0 && stepC > 0) return 45f;
            if (stepR < 0 && stepC == 0) return 90f;
            if (stepR < 0 && stepC < 0) return 135f;
            if (stepR == 0 && stepC < 0) return 180f;
            if (stepR > 0 && stepC < 0) return -135f;
            if (stepR > 0 && stepC == 0) return -90f;
            if (stepR > 0 && stepC > 0) return -45f;
            return 0f;
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

        private void EnsureBoardBinding()
        {
            if (boardUiGenerator == null) boardUiGenerator = FindObjectOfType<BoardUiGenerator>();
        }
    }
}
