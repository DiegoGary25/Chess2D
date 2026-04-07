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
    public sealed class EncounterRewardPanelController : MonoBehaviour
    {
        [Header("Panel")]
        [SerializeField] private GameObject rootPanel;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text selectionText;
        [SerializeField] private Button skipRewardButton;
        [SerializeField] private Button selectRewardButton;
        [Header("Cards")]
        [SerializeField] private Transform cardsRoot;
        [SerializeField] private Button cardTemplateButton;
        [SerializeField] private Color cardNormalColor = Color.white;
        [SerializeField] private Color cardSelectedColor = new Color(1f, 0.9f, 0.55f, 1f);

        private readonly List<Button> _spawnedButtons = new List<Button>();
        private readonly Dictionary<Button, CardDefinition> _cardByButton = new Dictionary<Button, CardDefinition>();
        private readonly List<CardDefinition> _offers = new List<CardDefinition>();

        private GameSessionState _session;
        private CardRuntimeController _cards;
        private Action _onClosed;
        private string _activeNodeId;
        private CardDefinition _selectedCard;

        public void Bind(GameSessionState session, CardRuntimeController cards)
        {
            _session = session;
            _cards = cards;

            if (skipRewardButton != null)
            {
                skipRewardButton.onClick.RemoveAllListeners();
                skipRewardButton.onClick.AddListener(SkipReward);
            }

            if (selectRewardButton != null)
            {
                selectRewardButton.onClick.RemoveAllListeners();
                selectRewardButton.onClick.AddListener(SelectReward);
            }

            if (rootPanel == null) rootPanel = gameObject;
            if (rootPanel != null) rootPanel.SetActive(false);
            ClearSelection();
        }

        public void Open(string nodeId, Action onClosed)
        {
            _activeNodeId = nodeId;
            _onClosed = onClosed;
            BuildOffers(nodeId);
            RebuildOfferButtons();
            ClearSelection();
            if (titleText != null) titleText.text = "Pick a Reward";
            if (rootPanel != null) rootPanel.SetActive(true);
        }

        public void Close()
        {
            if (rootPanel != null) rootPanel.SetActive(false);
            var onClosed = _onClosed;
            _onClosed = null;
            _activeNodeId = null;
            onClosed?.Invoke();
        }

        private void SkipReward()
        {
            Close();
        }

        private void SelectReward()
        {
            if (_selectedCard == null || _cards == null)
            {
                Close();
                return;
            }

            _cards.AddCardToDeck(_selectedCard);
            Close();
        }

        private void BuildOffers(string nodeId)
        {
            _offers.Clear();
            var cfg = _session != null ? _session.Config : null;
            if (cfg == null) return;

            var pool = cfg.shopCardPool != null && cfg.shopCardPool.Count > 0
                ? cfg.shopCardPool
                : cfg.starterDeck;
            if (pool == null || pool.Count == 0) return;

            var idx = new List<int>(pool.Count);
            for (var i = 0; i < pool.Count; i++) idx.Add(i);

            var seed = (_session != null ? _session.Seed : 1) + StableHash(nodeId) + (_session != null ? _session.EncounterIndex : 0) * 97;
            var rng = new System.Random(seed);
            for (var i = idx.Count - 1; i > 0; i--)
            {
                var j = rng.Next(0, i + 1);
                (idx[i], idx[j]) = (idx[j], idx[i]);
            }

            var wanted = Mathf.Min(3, idx.Count);
            for (var i = 0; i < wanted; i++)
            {
                var card = pool[idx[i]];
                if (card == null) continue;
                _offers.Add(card);
            }
        }

        private void RebuildOfferButtons()
        {
            for (var i = 0; i < _spawnedButtons.Count; i++)
            {
                if (_spawnedButtons[i] != null) Destroy(_spawnedButtons[i].gameObject);
            }
            _spawnedButtons.Clear();
            _cardByButton.Clear();

            if (cardsRoot == null || cardTemplateButton == null) return;
            for (var i = 0; i < _offers.Count; i++)
            {
                var card = _offers[i];
                if (card == null) continue;

                var button = Instantiate(cardTemplateButton, cardsRoot);
                button.gameObject.SetActive(true);
                ApplyCardButtonVisuals(button, card);
                var captured = card;
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => SelectCard(captured));
                BindCardInfoButton(button.gameObject, captured);
                _spawnedButtons.Add(button);
                _cardByButton[button] = card;
            }
            RefreshSelectionVisuals();
        }

        private static void BindCardInfoButton(GameObject cardRoot, CardDefinition card)
        {
            if (cardRoot == null || card == null) return;
            var infoGo = FindChildByName(cardRoot, "infobutton") ?? FindChildByName(cardRoot, "info");
            if (infoGo == null) return;
            var infoButton = infoGo.GetComponent<Button>();
            if (infoButton == null) infoButton = infoGo.AddComponent<Button>();
            EnsureInfoButtonConsumesParentClick(infoGo);
            infoButton.onClick.RemoveAllListeners();
            infoButton.onClick.AddListener(() => CardInfoPanelController.ShowGlobal(InfoContentResolver.ForCard(card)));
        }

        private static void EnsureInfoButtonConsumesParentClick(GameObject infoGo)
        {
            if (infoGo == null) return;
            if (infoGo.GetComponent<UiClickBlocker>() == null) infoGo.AddComponent<UiClickBlocker>();
        }

        private void SelectCard(CardDefinition card)
        {
            _selectedCard = card;
            RefreshSelectionVisuals();
        }

        private void ClearSelection()
        {
            _selectedCard = null;
            RefreshSelectionVisuals();
        }

        private void RefreshSelectionVisuals()
        {
            if (selectionText != null)
            {
                selectionText.text = _selectedCard != null ? _selectedCard.displayName : "Select 1 reward card";
            }

            if (selectRewardButton != null)
            {
                selectRewardButton.interactable = _selectedCard != null;
            }

            foreach (var kv in _cardByButton)
            {
                var button = kv.Key;
                var card = kv.Value;
                if (button == null) continue;
                var img = button.GetComponent<Image>();
                if (img != null) img.color = card == _selectedCard ? cardSelectedColor : cardNormalColor;
            }
        }

        private static void ApplyCardButtonVisuals(Button button, CardDefinition card)
        {
            if (button == null || card == null) return;

            var shopInfo = FindChildByName(button.gameObject, "shopinfo");
            if (shopInfo != null) shopInfo.SetActive(false);

            TMP_Text nameText = null;
            TMP_Text fallbackText = null;
            TMP_Text costText = null;
            TMP_Text shopCostText = null;
            var texts = button.GetComponentsInChildren<TMP_Text>(true);
            for (var i = 0; i < texts.Length; i++)
            {
                var t = texts[i];
                if (t == null) continue;
                var name = t.gameObject.name;
                if (shopCostText == null && name.IndexOf("shopcost", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    shopCostText = t;
                    continue;
                }
                if (costText == null && name.IndexOf("cost", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    costText = t;
                    continue;
                }
                if (nameText == null && name.IndexOf("name", StringComparison.OrdinalIgnoreCase) >= 0)
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
            if (shopCostText != null) shopCostText.gameObject.SetActive(false);

            Image iconImage = null;
            var images = button.GetComponentsInChildren<Image>(true);
            for (var i = 0; i < images.Length; i++)
            {
                var img = images[i];
                if (img == null || img.gameObject == button.gameObject) continue;
                var name = img.gameObject.name;
                if (name.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0)
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
