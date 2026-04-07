using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ChessPrototype.Unity.UI
{
    public sealed class UiClickBlocker : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        private Button[] _parentButtons;

        private void Awake()
        {
            CacheParentButtons();
        }

        private void OnTransformParentChanged()
        {
            CacheParentButtons();
        }

        private void CacheParentButtons()
        {
            var all = GetComponentsInParent<Button>(true);
            if (all == null || all.Length == 0)
            {
                _parentButtons = System.Array.Empty<Button>();
                return;
            }

            var selfButton = GetComponent<Button>();
            var tmp = new System.Collections.Generic.List<Button>(all.Length);
            for (var i = 0; i < all.Length; i++)
            {
                var b = all[i];
                if (b == null || b == selfButton) continue;
                tmp.Add(b);
            }
            _parentButtons = tmp.ToArray();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            SetParentButtonsEnabled(false);
            StartCoroutine(ReenableParentButtonsNextFrame());
            eventData.Use();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            eventData.Use();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            eventData.Use();
        }

        private void SetParentButtonsEnabled(bool enabled)
        {
            if (_parentButtons == null) return;
            for (var i = 0; i < _parentButtons.Length; i++)
            {
                var button = _parentButtons[i];
                if (button != null) button.enabled = enabled;
            }
        }

        private System.Collections.IEnumerator ReenableParentButtonsNextFrame()
        {
            yield return null;
            SetParentButtonsEnabled(true);
        }
    }
}
