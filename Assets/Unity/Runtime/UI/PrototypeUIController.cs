using System.Collections.Generic;
using ChessPrototype.Unity.Cards;
using ChessPrototype.Unity.Core;
using ChessPrototype.Unity.Data;
using ChessPrototype.Unity.Encounters;
using ChessPrototype.Unity.RunMap;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ChessPrototype.Unity.UI
{
    public sealed class PrototypeUIController : MonoBehaviour
    {
        [Header("Panels")]
        [SerializeField] private GameObject mapPanel;
        [SerializeField] private GameObject battlePanel;
        [SerializeField] private GameObject cardsPanel;
        [SerializeField] private GameObject piecePanel;
        [Header("Battle")]
        [SerializeField] private TMP_Text phaseText;
        [SerializeField] private TMP_Text energyText;
        [SerializeField] private RectTransform elixirBarRoot;
        [SerializeField] private Color elixirBarTint = new Color(0.45f, 0.9f, 1f, 1f);
        [SerializeField] private TMP_Text kingHpText;
        [SerializeField] private TMP_Text intentsText;
        [SerializeField] private Transform cardRoot;
        [SerializeField] private Button cardTemplateButton;
        [SerializeField] private Button endTurnButton;
        [SerializeField] private Button backButton;
        [SerializeField] private TMP_Text pieceTitleText;
        [SerializeField] private TMP_Text pieceStatsText;
        [SerializeField] private TMP_Text pieceStatusText;
        [SerializeField] private Image piecePreviewImage;
        [SerializeField] private RectTransform pieceHpBarRoot;
        [SerializeField] private RectTransform pieceAttackBarRoot;
        [SerializeField] private RectTransform pieceMoveLeftBarRoot;
        [SerializeField] private RectTransform pieceAttacksLeftBarRoot;
        [SerializeField] private Color playerHpBarTint = new Color(0.25f, 0.95f, 0.35f, 1f);
        [SerializeField] private Color enemyHpBarTint = new Color(1f, 0.35f, 0.35f, 1f);
        [SerializeField] private Color neutralHpBarTint = new Color(0.8f, 0.8f, 0.8f, 1f);
        [SerializeField] private Color playerAttackBarTint = new Color(1f, 0.75f, 0.25f, 1f);
        [SerializeField] private Color enemyAttackBarTint = new Color(1f, 0.45f, 0.2f, 1f);
        [SerializeField] private Color neutralAttackBarTint = new Color(0.8f, 0.8f, 0.8f, 1f);
        [SerializeField] private Color moveLeftBarTint = new Color(0.35f, 0.85f, 1f, 1f);
        [SerializeField] private Color attacksLeftBarTint = new Color(1f, 0.6f, 0.25f, 1f);
        [SerializeField] private Color cardNormalColor = new Color(1f, 1f, 1f, 0.95f);
        [SerializeField] private Color cardSelectedColor = new Color(1f, 0.86f, 0.28f, 1f);
        [Header("Map")]
        [SerializeField] private TMP_Text mapStatusText;
        [SerializeField] private Transform mapNodeRoot;
        [SerializeField] private Button mapNodeTemplateButton;
        [Header("Map Layout")]
        [SerializeField] private Vector2 mapOrigin = new Vector2(28f, -24f);
        [SerializeField] private float mapLaneSpacing = 110f;
        [SerializeField] private float mapTierSpacing = 84f;
        [SerializeField] private bool mapUsePrefabNodeSize = true;
        [SerializeField] private Vector2 mapNodeSize = new Vector2(102f, 28f);
        [SerializeField] private Vector2 mapLineNodeAnchorOffset = new Vector2(50f, -14f);
        [Header("Map Node Visuals")]
        [SerializeField] private bool mapUsePrefabNodeText = true;
        [SerializeField] private bool mapShowNodeTypeInLabel = true;
        [SerializeField] private Sprite mapUnknownIcon;
        [SerializeField] private Sprite mapMerchantIcon;
        [SerializeField] private Sprite mapTreasureIcon;
        [SerializeField] private Sprite mapRestIcon;
        [SerializeField] private Sprite mapEnemyIcon;
        [SerializeField] private Sprite mapEliteIcon;
        [SerializeField] private Sprite mapBossIcon;
        [SerializeField] private Color mapNodeCompletedColor = new Color(0.2f, 0.65f, 0.25f, 0.95f);
        [SerializeField] private Color mapNodeAvailableColor = new Color(0.2f, 0.4f, 0.9f, 0.95f);
        [SerializeField] private Color mapNodeLockedColor = new Color(0.15f, 0.15f, 0.2f, 0.7f);
        [SerializeField] private Color mapNodeCurrentOutlineColor = new Color(1f, 0.85f, 0.2f, 1f);
        [SerializeField] private bool mapTintNodeText = true;
        [SerializeField] private Color mapNodeCompletedTextColor = Color.white;
        [SerializeField] private Color mapNodeAvailableTextColor = Color.white;
        [SerializeField] private Color mapNodeLockedTextColor = new Color(0.85f, 0.85f, 0.85f, 0.95f);
        [Header("Map Edge Visuals")]
        [SerializeField] private GameObject mapLinePrefab;
        [SerializeField] private float mapLineThickness = 2f;
        [SerializeField] private Color mapLineDefaultColor = new Color(1f, 1f, 1f, 0.2f);
        [SerializeField] private Color mapLineHighlightedColor = new Color(0.85f, 0.45f, 0.15f, 0.9f);
        [Header("Map Node Overlays")]
        [SerializeField] private Sprite mapCurrentNodeOverlaySprite;
        [SerializeField] private Sprite mapVisitedNodeOverlaySprite;
        [SerializeField] private Color mapCurrentNodeOverlayColor = new Color(1f, 1f, 1f, 0.95f);
        [SerializeField] private Color mapVisitedNodeOverlayColor = new Color(1f, 1f, 1f, 0.65f);
        [Header("Map Interaction")]
        [SerializeField] private bool mapPulseSelectableNodes = true;
        [SerializeField, Min(0f)] private float mapPulseSpeed = 3f;
        [SerializeField, Range(1f, 1.3f)] private float mapPulseScale = 1.08f;
        [Header("Map Scroll Content")]
        [SerializeField] private bool mapAutoResizeScrollContent = true;
        [SerializeField] private bool preserveMapNodeRootTransformOnStart = true;
        [SerializeField] private float mapContentPaddingTop = 48f;
        [SerializeField] private float mapContentPaddingBottom = 48f;
        [SerializeField] private float mapContentPaddingLeft = 24f;
        [SerializeField] private float mapContentPaddingRight = 24f;
        [Header("Debug Refresh")]
        [SerializeField] private bool enableRuntimeRedrawHotkey = true;
        [SerializeField] private KeyCode runtimeRedrawHotkey = KeyCode.H;
        [SerializeField] private bool autoRedrawInPlayMode = false;
        [SerializeField] private IntentLineRenderer2D intentLines;

        private TurnStateController _turn;
        private CardRuntimeController _cards;
        private EncounterController _encounter;
        private RunMapController _map;
        private GameSessionState _session;
        private readonly List<Button> _cardButtons = new List<Button>();
        private readonly Dictionary<Button, CardDefinition> _cardByButton = new Dictionary<Button, CardDefinition>();
        private readonly List<Button> _mapButtons = new List<Button>();
        private readonly List<GameObject> _mapLines = new List<GameObject>();
        private readonly Dictionary<string, Button> _mapButtonByNodeId = new Dictionary<string, Button>();
        private readonly Dictionary<string, Vector3> _mapButtonBaseScaleByNodeId = new Dictionary<string, Vector3>();
        private readonly Dictionary<UnitKind, string> _descriptionByKind = new Dictionary<UnitKind, string>();
        private readonly Dictionary<UnitKind, Sprite> _previewSpriteByKind = new Dictionary<UnitKind, Sprite>();
        private bool _mapInitialRefreshDone;

        public void Bind(GameSessionState session, TurnStateController turn, CardRuntimeController cards, EncounterController encounter, RunMapController map)
        {
            _session = session; _turn = turn; _cards = cards; _encounter = encounter; _map = map;
            if (intentLines == null) intentLines = FindObjectOfType<IntentLineRenderer2D>();
            if (endTurnButton != null) endTurnButton.onClick.AddListener(_encounter.EndPlayerTurn);
            if (backButton != null) backButton.onClick.AddListener(ClosePieceAndShowCards);
            _cards.OnHandChanged += RebuildHandButtons;
            _turn.OnPhaseChanged += RefreshHud;
            _turn.OnEnergyChanged += RefreshHud;
            _encounter.OnBoardChanged += RefreshHud;
            _encounter.OnIntentsChanged += RefreshHud;
            _encounter.OnSelectionChanged += HandleSelectionChanged;
            _encounter.OnEncounterResolved += HandleEncounterResolved;
            _encounter.OnEncounterMessage += ShowMessage;
            _encounter.OnPendingCardChanged += _ => RefreshCardButtonSelection();
            BuildDescriptionCache();
            RebuildHandButtons();
            ShowMap();
            RefreshMap();
            RefreshHud();
        }

        public void ShowBattle()
        {
            if (mapPanel != null) mapPanel.SetActive(false);
            if (battlePanel != null) battlePanel.SetActive(true);
            ClosePieceAndShowCards();
            RefreshHud();
        }

        public void ShowMap()
        {
            if (battlePanel != null) battlePanel.SetActive(false);
            if (mapPanel != null) mapPanel.SetActive(true);
            RefreshMap();
        }

        public void ShowPiece(UnitRuntime unit)
        {
            if (unit == null) { ClosePieceAndShowCards(); return; }
            if (cardsPanel != null) cardsPanel.SetActive(false);
            if (piecePanel != null) piecePanel.SetActive(true);
            if (pieceTitleText != null) pieceTitleText.text = unit.kind.ToString();
            if (pieceStatsText != null)
            {
                var description = ResolveDescription(unit.kind);
                pieceStatsText.text = string.IsNullOrWhiteSpace(description)
                    ? "Piece Description:\n-"
                    : $"Piece Description:\n{description}";
            }
            if (pieceStatusText != null) pieceStatusText.text = BuildPieceStatsText(unit);
            if (piecePreviewImage != null)
            {
                piecePreviewImage.sprite = ResolvePreviewSprite(unit.kind);
                piecePreviewImage.enabled = piecePreviewImage.sprite != null;
            }
            UpdatePieceHpBar(unit);
            UpdatePieceAttackBar(unit);
            UpdatePieceActionBars(unit);
        }

        public void ClosePieceAndShowCards()
        {
            if (_encounter != null && _encounter.SelectedUnit != null) _encounter.ClearSelection();
            if (piecePanel != null) piecePanel.SetActive(false);
            if (cardsPanel != null) cardsPanel.SetActive(true);
            if (pieceStatusText != null) pieceStatusText.text = "Piece Stats Text:\n";
            if (piecePreviewImage != null)
            {
                piecePreviewImage.sprite = null;
                piecePreviewImage.enabled = false;
            }
            SetSegmentedBarValue(pieceHpBarRoot, 0, 0, playerHpBarTint);
            SetSegmentedBarValue(pieceAttackBarRoot, 0, 0, playerAttackBarTint);
            SetSegmentedBarValue(pieceMoveLeftBarRoot, 0, 0, moveLeftBarTint);
            SetSegmentedBarValue(pieceAttacksLeftBarRoot, 0, 0, attacksLeftBarTint);
        }

        public void RefreshMap()
        {
            if (_map == null || mapNodeTemplateButton == null || mapNodeRoot == null) return;
            for (var i = 0; i < _mapButtons.Count; i++) if (_mapButtons[i] != null) Destroy(_mapButtons[i].gameObject);
            _mapButtons.Clear();
            _mapButtonByNodeId.Clear();
            _mapButtonBaseScaleByNodeId.Clear();
            for (var i = 0; i < _mapLines.Count; i++) if (_mapLines[i] != null) Destroy(_mapLines[i]);
            _mapLines.Clear();

            var nodePos = new Dictionary<string, Vector2>();
            foreach (var kv in _map.Nodes)
            {
                var n = kv.Value;
                var b = Instantiate(mapNodeTemplateButton, mapNodeRoot);
                b.gameObject.SetActive(true);
                var txt = b.GetComponentInChildren<TMP_Text>();
                if (txt != null && !mapUsePrefabNodeText)
                {
                    txt.text = mapShowNodeTypeInLabel ? $"{n.id} [{n.type}]" : n.id;
                    if (mapTintNodeText) txt.color = ResolveMapNodeTextColor(n);
                }
                b.interactable = n.available && !n.completed;
                var bg = b.GetComponent<Image>();
                if (bg != null)
                {
                    bg.color = ResolveMapNodeColor(n);
                }
                ApplyMapNodeIcon(b, n.type);
                ApplyMapNodeOverlay(b, n);
                var rt = b.transform as RectTransform;
                var p = new Vector2(
                    mapOrigin.x + n.lane * mapLaneSpacing,
                    mapOrigin.y - n.tier * mapTierSpacing);
                if (rt != null)
                {
                    rt.anchorMin = new Vector2(0, 1);
                    rt.anchorMax = new Vector2(0, 1);
                    rt.pivot = new Vector2(0, 1);
                    rt.anchoredPosition = p;
                    if (!mapUsePrefabNodeSize) rt.sizeDelta = mapNodeSize;
                }
                ApplyMapNodeSelectionOutline(b, n.current);
                var captured = n.id;
                b.onClick.AddListener(() =>
                {
                    if (!_map.SelectNode(captured, out var selected)) return;
                    _encounter.StartNode(selected);
                    if (IsBattleNode(selected.type)) ShowBattle();
                    else { _map.CompleteNode(selected.id); RefreshMap(); }
                });
                _mapButtons.Add(b);
                _mapButtonByNodeId[n.id] = b;
                if (rt != null) _mapButtonBaseScaleByNodeId[n.id] = rt.localScale;
                nodePos[n.id] = p;
            }

            var activeFrom = _session != null ? _session.LastCompletedNodeId : null;
            foreach (var kv in _map.Edges)
            {
                var from = kv.Key;
                var toList = kv.Value;
                if (!nodePos.TryGetValue(from, out var fromPos)) continue;
                for (var i = 0; i < toList.Count; i++)
                {
                    var to = toList[i];
                    if (!nodePos.TryGetValue(to, out var toPos)) continue;
                    var highlighted = !string.IsNullOrEmpty(activeFrom) && activeFrom == from &&
                        _map.Nodes.TryGetValue(to, out var nextNode) && nextNode.available && !nextNode.completed;
                    CreateMapLine(fromPos + mapLineNodeAnchorOffset, toPos + mapLineNodeAnchorOffset, highlighted);
                }
            }

            var allowRootResize = !preserveMapNodeRootTransformOnStart || _mapInitialRefreshDone;
            UpdateMapContentSize(nodePos, allowRootResize);
            _mapInitialRefreshDone = true;
        }

        private void CreateMapLine(Vector2 from, Vector2 to, bool highlighted)
        {
            GameObject go;
            RectTransform rt;
            Image img;
            if (mapLinePrefab != null)
            {
                go = Instantiate(mapLinePrefab, mapNodeRoot);
                go.name = "MapLine";
                rt = go.transform as RectTransform;
                img = go.GetComponent<Image>();
                if (rt == null)
                {
                    rt = go.AddComponent<RectTransform>();
                }
            }
            else
            {
                go = new GameObject("MapLine", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(mapNodeRoot, false);
                rt = (RectTransform)go.transform;
                img = go.GetComponent<Image>();
            }

            go.transform.SetAsFirstSibling();
            if (img != null) img.color = highlighted ? mapLineHighlightedColor : mapLineDefaultColor;
            var dir = to - from;
            var len = dir.magnitude;
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(len, Mathf.Max(1f, mapLineThickness));
            rt.anchoredPosition = from;
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
            _mapLines.Add(go);
        }

        private void ApplyMapNodeOverlay(Button button, RuntimeMapNode node)
        {
            if (button == null || node == null) return;

            Sprite overlaySprite = null;
            Color overlayColor = Color.white;
            if (node.completed)
            {
                overlaySprite = mapVisitedNodeOverlaySprite;
                overlayColor = mapVisitedNodeOverlayColor;
            }
            else if (node.current)
            {
                overlaySprite = mapCurrentNodeOverlaySprite;
                overlayColor = mapCurrentNodeOverlayColor;
            }

            var overlay = EnsureNodeOverlayImage(button.transform);
            if (overlay == null) return;

            overlay.sprite = overlaySprite;
            overlay.color = overlayColor;
            overlay.enabled = overlaySprite != null;
            overlay.raycastTarget = false;
            overlay.preserveAspect = false;
            overlay.transform.SetAsLastSibling();
        }

        private static Image EnsureNodeOverlayImage(Transform buttonTransform)
        {
            if (buttonTransform == null) return null;

            var existing = buttonTransform.Find("MapStateOverlay");
            if (existing != null)
            {
                return existing.GetComponent<Image>();
            }

            var go = new GameObject("MapStateOverlay", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(buttonTransform, false);
            var rt = go.transform as RectTransform;
            if (rt != null)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
            var image = go.GetComponent<Image>();
            if (image != null) image.enabled = false;
            return image;
        }

        private void ApplyMapNodeIcon(Button button, MapNodeType type)
        {
            if (button == null) return;
            var icon = ResolveMapIcon(type);
            var images = button.GetComponentsInChildren<Image>(true);
            for (var i = 0; i < images.Length; i++)
            {
                var img = images[i];
                if (img == null || img.gameObject == button.gameObject) continue;
                var objName = img.gameObject.name;
                if (objName.IndexOf("icon", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                img.sprite = icon;
                img.enabled = icon != null;
                img.preserveAspect = true;
                return;
            }
        }

        private Sprite ResolveMapIcon(MapNodeType type)
        {
            switch (type)
            {
                case MapNodeType.Boss: return mapBossIcon;
                case MapNodeType.Elite: return mapEliteIcon;
                case MapNodeType.Battle:
                case MapNodeType.Enemy: return mapEnemyIcon;
                case MapNodeType.Rest: return mapRestIcon;
                case MapNodeType.Shop:
                case MapNodeType.Merchant: return mapMerchantIcon;
                case MapNodeType.Treasure: return mapTreasureIcon;
                case MapNodeType.Event:
                case MapNodeType.Unknown: return mapUnknownIcon;
                default: return mapUnknownIcon;
            }
        }

        private void UpdateMapContentSize(Dictionary<string, Vector2> nodePos, bool allowRootResize)
        {
            if (!allowRootResize) return;
            if (!mapAutoResizeScrollContent || nodePos == null || nodePos.Count == 0) return;
            if (!(mapNodeRoot is RectTransform contentRt)) return;

            var minX = float.MaxValue;
            var maxX = float.MinValue;
            var minY = float.MaxValue;
            var maxY = float.MinValue;
            foreach (var kv in nodePos)
            {
                var p = kv.Value;
                minX = Mathf.Min(minX, p.x);
                maxX = Mathf.Max(maxX, p.x + mapNodeSize.x);
                minY = Mathf.Min(minY, p.y - mapNodeSize.y);
                maxY = Mathf.Max(maxY, p.y);
            }

            var width = Mathf.Max(contentRt.sizeDelta.x, (maxX - minX) + mapContentPaddingLeft + mapContentPaddingRight);
            var height = Mathf.Max(200f, (maxY - minY) + mapContentPaddingTop + mapContentPaddingBottom);
            contentRt.sizeDelta = new Vector2(width, height);
        }

        private static bool IsBattleNode(MapNodeType type)
        {
            return type == MapNodeType.Battle || type == MapNodeType.Enemy || type == MapNodeType.Elite || type == MapNodeType.Boss;
        }

        private void AnimateSelectableNodePulse()
        {
            if (!mapPulseSelectableNodes || _map == null) return;
            if (mapPanel != null && !mapPanel.activeInHierarchy) return;

            var pulse = 1f + (Mathf.Sin(Time.unscaledTime * mapPulseSpeed) * 0.5f + 0.5f) * (mapPulseScale - 1f);
            foreach (var kv in _mapButtonByNodeId)
            {
                if (!_map.Nodes.TryGetValue(kv.Key, out var node)) continue;
                var button = kv.Value;
                if (button == null) continue;
                if (!(button.transform is RectTransform rt)) continue;

                if (!_mapButtonBaseScaleByNodeId.TryGetValue(kv.Key, out var baseScale)) baseScale = Vector3.one;
                rt.localScale = (node.available && !node.completed) ? baseScale * pulse : baseScale;
            }
        }

        private Color ResolveMapNodeColor(RuntimeMapNode node)
        {
            if (node == null) return mapNodeLockedColor;
            if (node.completed) return mapNodeCompletedColor;
            if (node.available) return mapNodeAvailableColor;
            return mapNodeLockedColor;
        }

        private Color ResolveMapNodeTextColor(RuntimeMapNode node)
        {
            if (node == null) return mapNodeLockedTextColor;
            if (node.completed) return mapNodeCompletedTextColor;
            if (node.available) return mapNodeAvailableTextColor;
            return mapNodeLockedTextColor;
        }

        private void ApplyMapNodeSelectionOutline(Button button, bool isCurrent)
        {
            if (button == null) return;
            var colors = button.colors;
            if (isCurrent)
            {
                colors.normalColor = mapNodeCurrentOutlineColor;
                colors.highlightedColor = mapNodeCurrentOutlineColor;
                colors.selectedColor = mapNodeCurrentOutlineColor;
            }
            else
            {
                colors.normalColor = Color.white;
                colors.highlightedColor = Color.white;
                colors.selectedColor = Color.white;
            }
            button.colors = colors;
        }

        private void HandleSelectionChanged(UnitRuntime selected)
        {
            if (selected == null) ClosePieceAndShowCards();
            else ShowPiece(selected);
        }

        private void HandleEncounterResolved(bool won, string nodeId)
        {
            if (won && _map != null && !string.IsNullOrEmpty(nodeId))
            {
                _map.CompleteNode(nodeId);
                RefreshMap();
                ShowMap();
                return;
            }
            if (!won) ShowMap();
        }

        private void RebuildHandButtons()
        {
            if (_cards == null || cardTemplateButton == null || cardRoot == null) return;
            for (var i = 0; i < _cardButtons.Count; i++) if (_cardButtons[i] != null) Destroy(_cardButtons[i].gameObject);
            _cardButtons.Clear();
            _cardByButton.Clear();
            foreach (var card in _cards.Hand)
            {
                var b = Instantiate(cardTemplateButton, cardRoot);
                b.gameObject.SetActive(true);
                ApplyCardButtonVisuals(b, card);
                var captured = card;
                b.onClick.AddListener(() =>
                {
                    if (_encounter == null) return;
                    _encounter.TryPlayCard(captured);
                    RefreshCardButtonSelection();
                });
                _cardButtons.Add(b);
                _cardByButton[b] = card;
            }
            RefreshCardButtonSelection();
        }

        private static void ApplyCardButtonVisuals(Button button, CardDefinition card)
        {
            if (button == null || card == null) return;

            TMP_Text nameText = null;
            TMP_Text fallbackText = null;
            TMP_Text costText = null;
            var texts = button.GetComponentsInChildren<TMP_Text>(true);
            for (var i = 0; i < texts.Length; i++)
            {
                var t = texts[i];
                if (t == null) continue;
                var name = t.gameObject.name;
                if (costText == null && name.IndexOf("cost", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    costText = t;
                    continue;
                }
                if (nameText == null && name.IndexOf("name", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    nameText = t;
                    continue;
                }
                if (fallbackText == null) fallbackText = t;
            }

            var titleText = nameText != null ? nameText : fallbackText;
            if (titleText != null) titleText.text = card.displayName;
            if (costText != null) costText.text = card.cost.ToString();
            else if (titleText != null) titleText.text = $"{card.displayName} ({card.cost})";

            Image iconImage = null;
            var images = button.GetComponentsInChildren<Image>(true);
            for (var i = 0; i < images.Length; i++)
            {
                var img = images[i];
                if (img == null) continue;
                if (img.gameObject == button.gameObject) continue;
                var name = img.gameObject.name;
                if (name.IndexOf("icon", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    iconImage = img;
                    break;
                }
            }

            if (iconImage != null)
            {
                iconImage.sprite = card.icon;
                iconImage.enabled = card.icon != null;
            }
        }

        private void RefreshHud()
        {
            if (_turn == null || _session == null) return;
            if (phaseText != null) phaseText.text = $"Phase: {_turn.Phase}";
            if (energyText != null) energyText.text = $"Energy: {_turn.PlayerEnergy}";
            var cfg = _session.Config;
            var maxElixir = cfg != null
                ? Mathf.Max(1, cfg.maxElixir > 0 ? cfg.maxElixir : cfg.energyPerRound)
                : Mathf.Max(1, _turn.PlayerEnergy);
            SetSegmentedBarValue(elixirBarRoot, _turn.PlayerEnergy, maxElixir, elixirBarTint);
            if (kingHpText != null) kingHpText.text = $"King HP: {_session.PersistentKingHp}";
            UpdatePieceHpBar(_encounter != null ? _encounter.SelectedUnit : null);
            UpdatePieceAttackBar(_encounter != null ? _encounter.SelectedUnit : null);
            UpdatePieceActionBars(_encounter != null ? _encounter.SelectedUnit : null);

            var plan = _encounter != null ? _encounter.CurrentEnemyPlan : null;
            if (intentsText != null)
            {
                if (plan == null || plan.intents.Count == 0) intentsText.text = "Enemy Intents: None";
                else
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("Enemy Intents:");
                    for (var i = 0; i < plan.intents.Count; i++)
                    {
                        var it = plan.intents[i];
                        sb.AppendLine($"{it.actorId} {it.kind} {it.from}->{it.to}");
                    }
                    intentsText.text = sb.ToString();
                }
            }
            if (intentLines != null)
            {
                var showIntents = _encounter == null || _encounter.ShowEnemyIntents;
                var emphasizedEnemyId = (_encounter != null &&
                                         _encounter.SelectedUnit != null &&
                                         _encounter.SelectedUnit.faction == Faction.Enemy)
                    ? _encounter.SelectedUnit.id
                    : null;
                intentLines.SetIntentEmphasis(emphasizedEnemyId);
                intentLines.RenderPlan(showIntents ? plan : null);
            }
            RefreshCardButtonSelection();
        }

        private void RefreshCardButtonSelection()
        {
            var pending = _encounter != null ? _encounter.PendingCard : null;
            foreach (var kv in _cardByButton)
            {
                var button = kv.Key;
                if (button == null) continue;
                var image = button.GetComponent<Image>();
                if (image == null) continue;
                image.color = kv.Value == pending ? cardSelectedColor : cardNormalColor;
            }
        }

        private void ShowMessage(string msg)
        {
            if (mapStatusText != null) mapStatusText.text = msg;
        }

        private void UpdatePieceHpBar(UnitRuntime unit)
        {
            if (unit == null)
            {
                SetSegmentedBarValue(pieceHpBarRoot, 0, 0, playerHpBarTint);
                return;
            }

            var tint = unit.faction == Faction.Player
                ? playerHpBarTint
                : unit.faction == Faction.Enemy
                    ? enemyHpBarTint
                    : neutralHpBarTint;
            SetSegmentedBarValue(pieceHpBarRoot, unit.hp, unit.maxHp, tint);
        }

        private void UpdatePieceAttackBar(UnitRuntime unit)
        {
            if (unit == null)
            {
                SetSegmentedBarValue(pieceAttackBarRoot, 0, 0, playerAttackBarTint);
                return;
            }

            var tint = unit.faction == Faction.Player
                ? playerAttackBarTint
                : unit.faction == Faction.Enemy
                    ? enemyAttackBarTint
                    : neutralAttackBarTint;

            var max = pieceAttackBarRoot != null ? pieceAttackBarRoot.childCount : unit.attack;
            SetSegmentedBarValue(pieceAttackBarRoot, unit.attack, max, tint);
        }

        private void UpdatePieceActionBars(UnitRuntime unit)
        {
            if (unit == null)
            {
                SetSegmentedBarValue(pieceMoveLeftBarRoot, 0, 0, moveLeftBarTint);
                SetSegmentedBarValue(pieceAttacksLeftBarRoot, 0, 0, attacksLeftBarTint);
                return;
            }

            var moveMax = pieceMoveLeftBarRoot != null ? Mathf.Max(1, pieceMoveLeftBarRoot.childCount) : 1;
            var attackMax = pieceAttacksLeftBarRoot != null ? Mathf.Max(1, pieceAttacksLeftBarRoot.childCount) : 1;
            var moveCurrent = unit.canMove ? 1 : 0;
            var attackCurrent = unit.canAttack ? 1 : 0;

            SetSegmentedBarValue(pieceMoveLeftBarRoot, moveCurrent, moveMax, moveLeftBarTint);
            SetSegmentedBarValue(pieceAttacksLeftBarRoot, attackCurrent, attackMax, attacksLeftBarTint);
        }

        private static void SetSegmentedBarValue(RectTransform barRoot, int current, int max, Color activeTint)
        {
            if (barRoot == null) return;
            var clampedMax = Mathf.Clamp(max, 0, barRoot.childCount);
            var clampedCurrent = Mathf.Clamp(current, 0, clampedMax);

            for (var i = 0; i < barRoot.childCount; i++)
            {
                var child = barRoot.GetChild(i);
                var isVisible = i < clampedCurrent && i < clampedMax;
                child.gameObject.SetActive(isVisible);
                if (!isVisible) continue;
                TintSegmentChild(child, activeTint);
            }
        }

        private static void TintSegmentChild(Transform segmentRoot, Color tint)
        {
            if (segmentRoot == null) return;
            var graphics = segmentRoot.GetComponentsInChildren<Graphic>(true);
            for (var i = 0; i < graphics.Length; i++)
            {
                if (graphics[i] != null) graphics[i].color = tint;
            }
        }

        private void BuildDescriptionCache()
        {
            _descriptionByKind.Clear();
            _previewSpriteByKind.Clear();
            var cfg = _session != null ? _session.Config : null;
            if (cfg == null) return;

            if (cfg.pieceDefinitions != null)
            {
                for (var i = 0; i < cfg.pieceDefinitions.Count; i++)
                {
                    var def = cfg.pieceDefinitions[i];
                    if (def == null) continue;
                    _descriptionByKind[def.kind] = def.description;
                    if (def.animations != null && def.animations.idleFrames != null && def.animations.idleFrames.Length > 0 && def.animations.idleFrames[0] != null)
                        _previewSpriteByKind[def.kind] = def.animations.idleFrames[0];
                    else if (def.icon != null) _previewSpriteByKind[def.kind] = def.icon;
                }
            }

            if (cfg.enemyDefinitions != null)
            {
                for (var i = 0; i < cfg.enemyDefinitions.Count; i++)
                {
                    var def = cfg.enemyDefinitions[i];
                    if (def == null) continue;
                    if (string.IsNullOrWhiteSpace(def.description)) continue;
                    _descriptionByKind[def.kind] = def.description;
                    if (!_previewSpriteByKind.ContainsKey(def.kind) && def.icon != null) _previewSpriteByKind[def.kind] = def.icon;
                }
            }
        }

        private string ResolveDescription(UnitKind kind)
        {
            return _descriptionByKind.TryGetValue(kind, out var description) ? description : string.Empty;
        }

        private Sprite ResolvePreviewSprite(UnitKind kind)
        {
            return _previewSpriteByKind.TryGetValue(kind, out var sprite) ? sprite : null;
        }

        private static string BuildPieceStatsText(UnitRuntime unit)
        {
            if (unit == null) return "Piece Stats Text:\n";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Piece Stats Text:");
            sb.AppendLine($"HP {unit.hp}/{unit.maxHp}");
            sb.AppendLine($"ATK {unit.attack}");

            var hasAny = false;
            var status = unit.status;
            if (status != null)
            {
                if (status.poisonedTurns > 0) { sb.AppendLine("Poison"); hasAny = true; }
                if (status.sleepingTurns > 0) { sb.AppendLine("Sleeping"); hasAny = true; }
                if (status.rootedTurns > 0) { sb.AppendLine("Rooted"); hasAny = true; }
                if (status.shieldCharge > 0) { sb.AppendLine("Shielded"); hasAny = true; }
                if (status.pawnPromoted) { sb.AppendLine("Promoted"); hasAny = true; }
            }

            if (!hasAny) sb.AppendLine("Status: None");
            return sb.ToString();
        }

        private void Update()
        {
            AnimateSelectableNodePulse();

            if (autoRedrawInPlayMode)
            {
                ForceRuntimeRedraw();
                return;
            }

            if (!enableRuntimeRedrawHotkey) return;
            if (!IsRedrawHotkeyPressed()) return;
            ForceRuntimeRedraw();
        }

        private bool IsRedrawHotkeyPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                switch (runtimeRedrawHotkey)
                {
                    case KeyCode.H: return keyboard.hKey.wasPressedThisFrame;
                    case KeyCode.R: return keyboard.rKey.wasPressedThisFrame;
                    case KeyCode.Space: return keyboard.spaceKey.wasPressedThisFrame;
                    default: break;
                }
            }
#endif
            return Input.GetKeyDown(runtimeRedrawHotkey);
        }

        [ContextMenu("Force Runtime Redraw")]
        private void ForceRuntimeRedraw()
        {
            RefreshMap();
            RefreshHud();
        }
    }
}
