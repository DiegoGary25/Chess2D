using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ChessPrototype.Unity.UI
{
    public struct TrinketInfoPanelData
    {
        public string title;
        public Sprite image;
        public string description;
    }

    public sealed class TrinketInfoPanelController : MonoBehaviour
    {
        [SerializeField] private GameObject rootPanel;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private Image imageView;
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField] private Button closeButton;

        private static TrinketInfoPanelController _instance;

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

        public static void ShowGlobal(TrinketInfoPanelData data)
        {
            if (_instance == null) _instance = FindObjectOfType<TrinketInfoPanelController>(true);
            if (_instance == null) return;
            _instance.Show(data);
        }

        public static void HideGlobal()
        {
            if (_instance == null) _instance = FindObjectOfType<TrinketInfoPanelController>(true);
            if (_instance == null) return;
            _instance.Hide();
        }

        public void Show(TrinketInfoPanelData data)
        {
            if (titleText != null) titleText.text = string.IsNullOrWhiteSpace(data.title) ? "-" : data.title;
            if (imageView != null)
            {
                imageView.sprite = data.image;
                imageView.enabled = data.image != null;
            }
            if (descriptionText != null) descriptionText.text = string.IsNullOrWhiteSpace(data.description) ? "-" : data.description;
            if (rootPanel != null) rootPanel.SetActive(true);
        }

        public void Hide()
        {
            if (rootPanel != null) rootPanel.SetActive(false);
        }
    }
}
