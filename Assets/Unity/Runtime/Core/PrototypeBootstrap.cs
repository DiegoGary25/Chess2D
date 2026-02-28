using ChessPrototype.Unity.Cards;
using ChessPrototype.Unity.Data;
using ChessPrototype.Unity.Encounters;
using ChessPrototype.Unity.RunMap;
using ChessPrototype.Unity.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ChessPrototype.Unity.Core
{
    public sealed class PrototypeBootstrap : MonoBehaviour
    {
        [SerializeField] private GameSessionState session;
        [SerializeField] private TurnStateController turn;
        [SerializeField] private CardRuntimeController cards;
        [SerializeField] private EncounterController encounter;
        [SerializeField] private RunMapController map;
        [SerializeField] private PrototypeUIController ui;
        [SerializeField] private Camera mainCamera;
        [Header("Debug")]
        [SerializeField] private bool autoStartEncounterOnPlay;

        private bool _ready;

        private void Awake()
        {
            // Fix EventSystem input module first so UI doesn't throw with Input System projects.
            EnsureEventSystemInputModule();

            if (session == null) session = FindObjectOfType<GameSessionState>();
            if (turn == null) turn = FindObjectOfType<TurnStateController>();
            if (cards == null) cards = FindObjectOfType<CardRuntimeController>();
            if (encounter == null) encounter = FindObjectOfType<EncounterController>();
            if (map == null) map = FindObjectOfType<RunMapController>();
            if (ui == null) ui = FindObjectOfType<PrototypeUIController>();
            if (mainCamera == null) mainCamera = Camera.main;

            if (session == null || turn == null || cards == null || encounter == null || map == null || ui == null)
            {
                Debug.LogWarning("[ChessPrototype] Missing manager references in scene. Run Auto-Link References.");
                return;
            }

            #if UNITY_EDITOR
            if (session.Config == null)
            {
                var cfg = UnityEditor.AssetDatabase.LoadAssetAtPath<GameConfigDefinition>("Assets/Unity/Data/Config/GameConfig.asset");
                if (cfg != null) session.SetConfig(cfg);
            }
            #endif

            session.EnsureInitialized();
            if (session.Config == null)
            {
                Debug.LogWarning("[ChessPrototype] Missing GameConfigDefinition. Run Generate Data Assets and Auto-Link References.");
                return;
            }

            turn.Configure(3, 1, session.Config.maxElixir);
            cards.Configure(session.Config, session.Seed);
            encounter.Configure(session, turn, cards);
            map.Configure(session, session.Config.runMap);
            ui.Bind(session, turn, cards, encounter, map);
            _ready = true;
        }

        private void Start()
        {
            if (!_ready || !autoStartEncounterOnPlay || encounter == null || ui == null) return;
            var debugNode = new RuntimeMapNode
            {
                id = "DEBUG_ENCOUNTER",
                tier = 0,
                lane = 0,
                type = MapNodeType.Battle,
                available = true,
                completed = false,
                current = true
            };
            encounter.StartNode(debugNode);
            ui.ShowBattle();
        }

        private static void EnsureEventSystemInputModule()
        {
            var es = Object.FindObjectOfType<EventSystem>();
            if (es == null) return;

            var inputSystemUiType = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (inputSystemUiType == null) return;

            var inputModule = es.GetComponent(inputSystemUiType);
            if (inputModule == null) inputModule = es.gameObject.AddComponent(inputSystemUiType);

            var standalone = es.GetComponent<StandaloneInputModule>();
            if (standalone != null)
            {
                standalone.enabled = false;
                Object.Destroy(standalone);
            }

            var touch = es.GetComponent<TouchInputModule>();
            if (touch != null)
            {
                touch.enabled = false;
                Object.Destroy(touch);
            }
        }
    }
}


