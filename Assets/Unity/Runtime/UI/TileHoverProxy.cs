using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ChessPrototype.Unity.UI
{
    public sealed class TileHoverProxy : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private Action _onEnter;
        private Action _onExit;

        public void Bind(Action onEnter, Action onExit)
        {
            _onEnter = onEnter;
            _onExit = onExit;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _onEnter?.Invoke();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _onExit?.Invoke();
        }
    }
}
