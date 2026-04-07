using ChessPrototype.Unity.Core;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ChessPrototype.Unity.UI
{
    public sealed class GameOverPanelController : MonoBehaviour
    {
        [SerializeField] private GameObject rootPanel;
        [SerializeField] private TMP_Text encounterReachedText;
        [SerializeField] private Button restartGameButton;

        private GameSessionState _session;

        public void Bind(GameSessionState session)
        {
            _session = session;

            if (restartGameButton != null)
            {
                restartGameButton.onClick.RemoveAllListeners();
                restartGameButton.onClick.AddListener(RestartGame);
            }

            if (rootPanel == null) rootPanel = gameObject;
            if (rootPanel != null) rootPanel.SetActive(false);
        }

        public void Open()
        {
            var encounterReached = Mathf.Max(1, (_session != null ? _session.EncounterIndex : 0) + 1);
            Open(encounterReached);
        }

        public void Open(int encounterReached)
        {
            if (encounterReachedText != null)
            {
                encounterReachedText.text = $"You reached encounter {Mathf.Max(1, encounterReached)}";
            }

            if (rootPanel != null) rootPanel.SetActive(true);
        }

        public void RestartGame()
        {
            var activeScene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(activeScene.buildIndex);
        }
    }
}
