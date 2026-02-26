using UnityEngine;
using ChessPrototype.Unity.Data;

namespace ChessPrototype.Unity.Core
{
    public sealed class InputModeController : MonoBehaviour
    {
        [SerializeField] private InputMode mode = InputMode.Idle;
        public InputMode Mode => mode;
        public event System.Action<InputMode> OnModeChanged;

        public void SetMode(InputMode next)
        {
            if (mode == next) return;
            mode = next;
            OnModeChanged?.Invoke(mode);
        }
    }
}
