using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ChessPrototype.Unity.UI
{
    public struct CardInfoPanelData
    {
        public string title;
        public Sprite image;
        public string description;
        public bool hasUnitStats;
        public int healthMax;
        public int damageStandard;
    }

    public sealed class CardInfoPanelController : MonoBehaviour
    {
        [SerializeField] private GameObject rootPanel;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private Image cardImage;
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField] private RectTransform healthMaxBarRoot;
        [SerializeField] private RectTransform damageStandardBarRoot;
        [SerializeField] private Color healthBarTint = new Color(0.25f, 0.95f, 0.35f, 1f);
        [SerializeField] private Color damageBarTint = new Color(1f, 0.75f, 0.25f, 1f);
        [SerializeField] private Button closeButton;

        private static CardInfoPanelController _instance;

        private void Awake()
        {
            _instance = this;
            if (closeButton != null)
            {
                closeButton.onClick.RemoveAllListeners();
                closeButton.onClick.AddListener(Hide);
            }
            if (rootPanel == null) rootPanel = gameObject;
            Hide();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        public static void ShowGlobal(CardInfoPanelData data)
        {
            if (_instance == null) _instance = FindObjectOfType<CardInfoPanelController>(true);
            if (_instance == null) return;
            _instance.Show(data);
        }

        public static void HideGlobal()
        {
            if (_instance == null) _instance = FindObjectOfType<CardInfoPanelController>(true);
            if (_instance == null) return;
            _instance.Hide();
        }

        public void Show(CardInfoPanelData data)
        {
            if (titleText != null) titleText.text = string.IsNullOrWhiteSpace(data.title) ? "-" : data.title;
            if (cardImage != null)
            {
                cardImage.sprite = data.image;
                cardImage.enabled = data.image != null;
            }
            if (descriptionText != null) descriptionText.text = string.IsNullOrWhiteSpace(data.description) ? "-" : data.description;

            if (data.hasUnitStats)
            {
                SetSegmentedBarValue(healthMaxBarRoot, data.healthMax, data.healthMax, healthBarTint);
                var damageBarMax = damageStandardBarRoot != null ? damageStandardBarRoot.childCount : data.damageStandard;
                SetSegmentedBarValue(damageStandardBarRoot, data.damageStandard, damageBarMax, damageBarTint);
            }
            else
            {
                SetSegmentedBarValue(healthMaxBarRoot, 0, 0, healthBarTint);
                SetSegmentedBarValue(damageStandardBarRoot, 0, 0, damageBarTint);
            }

            if (rootPanel != null) rootPanel.SetActive(true);
        }

        public void Hide()
        {
            if (rootPanel != null) rootPanel.SetActive(false);
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
    }
}
