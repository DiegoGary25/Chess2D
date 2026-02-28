using System;
using System.Collections.Generic;
using ChessPrototype.Unity.Cards;
using ChessPrototype.Unity.Core;
using ChessPrototype.Unity.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ChessPrototype.Unity.UI
{
    public sealed class ShopPanelController : MonoBehaviour
    {
        [Header("Panel")]
        [SerializeField] private GameObject rootPanel;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button buyButton;
        [SerializeField] private TMP_Text goldText;
        [SerializeField] private TMP_Text selectionNameText;
        [SerializeField] private TMP_Text selectionDescriptionText;
        [SerializeField] private TMP_Text selectionCostText;
        [Header("Cards")]
        [SerializeField] private Transform shopCardsRoot;
        [SerializeField] private Button shopCardTemplateButton;
        [Header("Trinkets")]
        [SerializeField] private Transform shopTrinketsRoot;
        [SerializeField] private GameObject shopTrinketTemplate;
        [SerializeField] private Transform currentTrinketsRoot;
        [SerializeField] private GameObject currentTrinketTemplate;

        private readonly List<GameObject> _spawnedShopCards = new List<GameObject>();
        private readonly List<GameObject> _spawnedShopTrinkets = new List<GameObject>();
        private readonly List<GameObject> _spawnedCurrentTrinkets = new List<GameObject>();
        private readonly HashSet<CardDefinition> _purchasedCards = new HashSet<CardDefinition>();
        private readonly HashSet<TrinketDefinition> _purchasedTrinkets = new HashSet<TrinketDefinition>();

        private GameSessionState _session;
        private CardRuntimeController _cards;
        private string _activeNodeId;
        private Action<string> _onClosed;
        private List<CardDefinition> _cardOffers = new List<CardDefinition>();
        private List<TrinketDefinition> _trinketOffers = new List<TrinketDefinition>();
        private TrinketDefinition _debugTrinket;
        private CardDefinition _selectedCard;
        private TrinketDefinition _selectedTrinket;

        public void Bind(GameSessionState session, CardRuntimeController cards)
        {
            _session = session;
            _cards = cards;
            if (closeButton != null)
            {
                closeButton.onClick.RemoveAllListeners();
                closeButton.onClick.AddListener(Close);
            }
            if (buyButton != null)
            {
                buyButton.onClick.RemoveAllListeners();
                buyButton.onClick.AddListener(BuySelected);
            }
            if (rootPanel == null) rootPanel = gameObject;
            if (rootPanel != null) rootPanel.SetActive(false);
            ClearSelection();
        }

        public void Open(string nodeId, Action<string> onClosed)
        {
            _activeNodeId = nodeId;
            _onClosed = onClosed;
            _purchasedCards.Clear();
            _purchasedTrinkets.Clear();
            _cardOffers = BuildCardOffers(nodeId);
            _trinketOffers = BuildTrinketOffers(nodeId);
            ClearSelection();
            RefreshAll();
            if (rootPanel != null) rootPanel.SetActive(true);
        }

        public void Close()
        {
            if (rootPanel != null) rootPanel.SetActive(false);
            var nodeId = _activeNodeId;
            _activeNodeId = null;
            var onClosed = _onClosed;
            _onClosed = null;
            onClosed?.Invoke(nodeId);
        }

        private void RefreshAll()
        {
            RefreshGold();
            RebuildCardOffers();
            RebuildTrinketOffers();
            RebuildCurrentTrinkets();
            RefreshSelectionUi();
        }

        private void RefreshGold()
        {
            if (goldText != null)
            {
                goldText.text = _session != null ? $"Gold: {_session.Gold}" : "Gold: 0";
            }
        }

        private void RebuildCardOffers()
        {
            ClearSpawned(_spawnedShopCards);
            if (shopCardsRoot == null || shopCardTemplateButton == null) return;

            for (var i = 0; i < _cardOffers.Count; i++)
            {
                var card = _cardOffers[i];
                if (card == null) continue;

                var button = Instantiate(shopCardTemplateButton, shopCardsRoot);
                button.gameObject.SetActive(true);
                ApplyCardVisuals(button, card, true);
                var purchased = _purchasedCards.Contains(card);
                var captured = card;
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => SelectCard(captured));
                button.interactable = !purchased;
                ApplyPurchasedState(button.gameObject, purchased);
                ApplySelectedState(button.gameObject, _selectedCard == card && _selectedTrinket == null);
                _spawnedShopCards.Add(button.gameObject);
            }
        }

        private void RebuildTrinketOffers()
        {
            ClearSpawned(_spawnedShopTrinkets);
            if (shopTrinketsRoot == null || shopTrinketTemplate == null) return;

            for (var i = 0; i < _trinketOffers.Count; i++)
            {
                var trinket = _trinketOffers[i];
                if (trinket == null) continue;

                var go = Instantiate(shopTrinketTemplate, shopTrinketsRoot);
                go.SetActive(true);
                var button = EnsureButton(go);
                ApplyTrinketVisuals(go, trinket, true);
                var alreadyOwned = _session != null && _session.OwnsTrinket(trinket) && !trinket.allowDuplicates;
                var purchased = _purchasedTrinkets.Contains(trinket) || alreadyOwned;
                var captured = trinket;
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => SelectTrinket(captured));
                button.interactable = !purchased;
                ApplyPurchasedState(go, purchased);
                ApplySelectedState(go, _selectedTrinket == trinket && _selectedCard == null);
                _spawnedShopTrinkets.Add(go);
            }
        }

        private void RebuildCurrentTrinkets()
        {
            ClearSpawned(_spawnedCurrentTrinkets);
            if (currentTrinketsRoot == null || currentTrinketTemplate == null || _session == null) return;

            var owned = _session.OwnedTrinkets;
            for (var i = 0; i < owned.Count; i++)
            {
                var trinket = owned[i];
                if (trinket == null) continue;
                var go = Instantiate(currentTrinketTemplate, currentTrinketsRoot);
                go.SetActive(true);
                ApplyTrinketVisuals(go, trinket, false);
                _spawnedCurrentTrinkets.Add(go);
            }
        }

        private void TryBuyCard(CardDefinition card)
        {
            if (_session == null || _cards == null || card == null) return;
            var price = Mathf.Max(0, card.shopCost);
            if (!_session.TrySpendGold(price)) return;
            _cards.AddCardToDeck(card);
            _purchasedCards.Add(card);
            if (_selectedCard == card) ClearSelection();
            RefreshAll();
        }

        private void TryBuyTrinket(TrinketDefinition trinket)
        {
            if (_session == null || trinket == null) return;
            var price = Mathf.Max(0, trinket.shopCost);
            if (!_session.TrySpendGold(price)) return;
            if (!_session.TryAddTrinket(trinket))
            {
                _session.AddGold(price);
                return;
            }
            _purchasedTrinkets.Add(trinket);
            if (_selectedTrinket == trinket) ClearSelection();
            RefreshAll();
        }

        private void SelectCard(CardDefinition card)
        {
            if (card == null || _purchasedCards.Contains(card)) return;
            _selectedCard = card;
            _selectedTrinket = null;
            RefreshAll();
        }

        private void SelectTrinket(TrinketDefinition trinket)
        {
            if (trinket == null) return;
            var alreadyOwned = _session != null && _session.OwnsTrinket(trinket) && !trinket.allowDuplicates;
            if (_purchasedTrinkets.Contains(trinket) || alreadyOwned) return;
            _selectedTrinket = trinket;
            _selectedCard = null;
            RefreshAll();
        }

        private void BuySelected()
        {
            if (_selectedCard != null)
            {
                TryBuyCard(_selectedCard);
                return;
            }

            if (_selectedTrinket != null)
            {
                TryBuyTrinket(_selectedTrinket);
            }
        }

        private List<CardDefinition> BuildCardOffers(string nodeId)
        {
            var config = _session != null ? _session.Config : null;
            var pool = config != null && config.shopCardPool != null && config.shopCardPool.Count > 0
                ? config.shopCardPool
                : config != null ? config.starterDeck : null;
            var count = config != null ? Mathf.Max(1, config.shopCardOfferCount) : 3;
            return PickDeterministic(pool, count, nodeId, item => item);
        }

        private List<TrinketDefinition> BuildTrinketOffers(string nodeId)
        {
            var config = _session != null ? _session.Config : null;
            var pool = config != null && config.shopTrinketPool != null && config.shopTrinketPool.Count > 0
                ? config.shopTrinketPool
                : config != null && config.trinketDefinitions != null && config.trinketDefinitions.Count > 0
                    ? config.trinketDefinitions
                    : new List<TrinketDefinition> { GetDebugTrinket() };
            var count = config != null ? Mathf.Max(1, config.shopTrinketOfferCount) : 3;
            return PickDeterministic(pool, count, nodeId, item => item);
        }

        private List<T> PickDeterministic<T>(IList<T> pool, int count, string nodeId, Func<T, T> selector) where T : class
        {
            var results = new List<T>();
            if (pool == null || pool.Count == 0) return results;

            var indices = new List<int>(pool.Count);
            for (var i = 0; i < pool.Count; i++) indices.Add(i);

            var rng = new System.Random((_session != null ? _session.Seed : 1) + StableHash(nodeId) + pool.Count * 31);
            for (var i = indices.Count - 1; i > 0; i--)
            {
                var j = rng.Next(0, i + 1);
                (indices[i], indices[j]) = (indices[j], indices[i]);
            }

            var wanted = Mathf.Min(count, indices.Count);
            for (var i = 0; i < wanted; i++)
            {
                var item = pool[indices[i]];
                if (item == null) continue;
                results.Add(selector(item));
            }

            return results;
        }

        private TrinketDefinition GetDebugTrinket()
        {
            if (_debugTrinket != null) return _debugTrinket;

            _debugTrinket = ScriptableObject.CreateInstance<TrinketDefinition>();
            _debugTrinket.name = "Debug_TestTrinket";
            _debugTrinket.trinketId = "debug_test_trinket";
            _debugTrinket.displayName = "Test Trinket";
            _debugTrinket.description = "+1 move to all troops each player turn.";
            _debugTrinket.shopCost = 40;
            _debugTrinket.allowDuplicates = true;
            _debugTrinket.effectType = TrinketEffectType.BonusMovePerTurn;
            _debugTrinket.amount = 1;
            return _debugTrinket;
        }

        private void RefreshSelectionUi()
        {
            var hasCard = _selectedCard != null;
            var hasTrinket = _selectedTrinket != null;
            var hasSelection = hasCard || hasTrinket;

            if (selectionNameText != null)
            {
                selectionNameText.text = hasCard
                    ? _selectedCard.displayName
                    : hasTrinket
                        ? _selectedTrinket.displayName
                        : "Select an item";
            }

            if (selectionDescriptionText != null)
            {
                selectionDescriptionText.text = hasCard
                    ? BuildCardDescription(_selectedCard)
                    : hasTrinket
                        ? _selectedTrinket.description
                        : string.Empty;
            }

            if (selectionCostText != null)
            {
                selectionCostText.text = hasCard
                    ? $"Cost: {_selectedCard.shopCost}"
                    : hasTrinket
                        ? $"Cost: {_selectedTrinket.shopCost}"
                        : string.Empty;
            }

            if (buyButton != null)
            {
                var canAfford = hasCard
                    ? _session != null && _session.Gold >= Mathf.Max(0, _selectedCard.shopCost)
                    : hasTrinket
                        ? _session != null && _session.Gold >= Mathf.Max(0, _selectedTrinket.shopCost)
                        : false;
                buyButton.interactable = hasSelection && canAfford;
            }
        }

        private void ClearSelection()
        {
            _selectedCard = null;
            _selectedTrinket = null;
            RefreshSelectionUi();
        }

        private static string BuildCardDescription(CardDefinition card)
        {
            if (card == null) return string.Empty;
            if (!string.IsNullOrWhiteSpace(card.description)) return card.description;
            return $"{card.kind}";
        }

        private static Button EnsureButton(GameObject go)
        {
            if (go == null) return null;
            var button = go.GetComponent<Button>();
            return button != null ? button : go.AddComponent<Button>();
        }

        private static void ApplyPurchasedState(GameObject go, bool purchased)
        {
            if (go == null) return;
            var canvasGroup = go.GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = go.AddComponent<CanvasGroup>();
            canvasGroup.alpha = purchased ? 0.55f : 1f;
        }

        private static void ApplySelectedState(GameObject go, bool selected)
        {
            if (go == null) return;
            var graphics = go.GetComponentsInChildren<Graphic>(true);
            for (var i = 0; i < graphics.Length; i++)
            {
                var graphic = graphics[i];
                if (graphic == null || graphic.gameObject != go) continue;
                graphic.color = selected ? new Color(1f, 0.9f, 0.55f, 1f) : Color.white;
                break;
            }
        }

        private static void ApplyCardVisuals(Button button, CardDefinition card, bool showShopCost)
        {
            if (button == null || card == null) return;

            var shopInfo = FindChildByName(button.gameObject, "shopinfo");
            if (shopInfo != null) shopInfo.SetActive(showShopCost);

            TMP_Text nameText = null;
            TMP_Text fallbackText = null;
            TMP_Text costText = null;
            TMP_Text shopCostText = null;
            var texts = button.GetComponentsInChildren<TMP_Text>(true);
            for (var i = 0; i < texts.Length; i++)
            {
                var text = texts[i];
                if (text == null) continue;
                var objectName = text.gameObject.name;
                if (shopCostText == null && MatchesToken(objectName, "shopcost"))
                {
                    shopCostText = text;
                    continue;
                }
                if (costText == null &&
                    MatchesToken(objectName, "cost") &&
                    !MatchesToken(objectName, "shopcost"))
                {
                    costText = text;
                    continue;
                }
                if (nameText == null && MatchesToken(objectName, "name"))
                {
                    nameText = text;
                    continue;
                }
                if (fallbackText == null) fallbackText = text;
            }

            var title = nameText != null ? nameText : fallbackText;
            if (title != null) title.text = card.displayName;
            if (costText != null) costText.text = card.cost.ToString();
            if (shopCostText != null)
            {
                shopCostText.gameObject.SetActive(showShopCost);
                shopCostText.text = card.shopCost.ToString();
            }

            var iconImage = FindImageByName(button.gameObject, "icon");
            if (iconImage != null)
            {
                iconImage.sprite = card.icon;
                iconImage.enabled = card.icon != null;
            }
        }

        private static void ApplyTrinketVisuals(GameObject go, TrinketDefinition trinket, bool showShopCost)
        {
            if (go == null || trinket == null) return;

            var shopInfo = FindChildByName(go, "shopinfo");
            if (shopInfo != null) shopInfo.SetActive(showShopCost);

            var texts = go.GetComponentsInChildren<TMP_Text>(true);
            TMP_Text nameText = null;
            TMP_Text descriptionText = null;
            TMP_Text shopCostText = null;
            TMP_Text fallbackText = null;
            for (var i = 0; i < texts.Length; i++)
            {
                var text = texts[i];
                if (text == null) continue;
                var objectName = text.gameObject.name;
                if (shopCostText == null && MatchesToken(objectName, "shopcost"))
                {
                    shopCostText = text;
                    continue;
                }
                if (nameText == null && MatchesToken(objectName, "name"))
                {
                    nameText = text;
                    continue;
                }
                if (descriptionText == null &&
                    (MatchesToken(objectName, "description") || MatchesToken(objectName, "desc")))
                {
                    descriptionText = text;
                    continue;
                }
                if (fallbackText == null) fallbackText = text;
            }

            if (nameText != null) nameText.text = trinket.displayName;
            else if (fallbackText != null) fallbackText.text = trinket.displayName;
            if (descriptionText != null) descriptionText.text = trinket.description;
            if (shopCostText != null)
            {
                shopCostText.gameObject.SetActive(showShopCost);
                shopCostText.text = trinket.shopCost.ToString();
            }

            var iconImage = FindImageByName(go, "icon");
            if (iconImage != null)
            {
                iconImage.sprite = trinket.icon;
                iconImage.enabled = trinket.icon != null;
            }
        }

        private static Image FindImageByName(GameObject go, string token)
        {
            if (go == null || string.IsNullOrEmpty(token)) return null;
            var images = go.GetComponentsInChildren<Image>(true);
            for (var i = 0; i < images.Length; i++)
            {
                var image = images[i];
                if (image == null || image.gameObject == go) continue;
                if (MatchesToken(image.gameObject.name, token))
                {
                    return image;
                }
            }
            return null;
        }

        private static GameObject FindChildByName(GameObject go, string token)
        {
            if (go == null || string.IsNullOrEmpty(token)) return null;
            var transforms = go.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                var child = transforms[i];
                if (child == null || child.gameObject == go) continue;
                if (MatchesToken(child.gameObject.name, token))
                {
                    return child.gameObject;
                }
            }
            return null;
        }

        private static bool MatchesToken(string source, string token)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(token)) return false;
            return NormalizeName(source).Contains(NormalizeName(token));
        }

        private static string NormalizeName(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var chars = new char[value.Length];
            var count = 0;
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (!char.IsLetterOrDigit(c)) continue;
                chars[count++] = char.ToLowerInvariant(c);
            }
            return new string(chars, 0, count);
        }

        private static void ClearSpawned(List<GameObject> spawned)
        {
            for (var i = 0; i < spawned.Count; i++)
            {
                if (spawned[i] != null) Destroy(spawned[i]);
            }
            spawned.Clear();
        }

        private static int StableHash(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            unchecked
            {
                var hash = 23;
                for (var i = 0; i < value.Length; i++) hash = hash * 31 + value[i];
                return hash;
            }
        }
    }
}
