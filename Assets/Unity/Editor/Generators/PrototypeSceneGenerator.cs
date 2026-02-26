#if UNITY_EDITOR
using ChessPrototype.Unity.Cards;
using ChessPrototype.Unity.Core;
using ChessPrototype.Unity.Data;
using ChessPrototype.Unity.Encounters;
using ChessPrototype.Unity.RunMap;
using ChessPrototype.Unity.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ChessPrototype.Unity.EditorTools
{
    public static class PrototypeSceneGenerator
    {
        public const string ScenesDir = "Assets/Unity/Scenes";
        private const string DataConfigPath = "Assets/Unity/Data/Config/GameConfig.asset";

        public static void GenerateEncounterScene()
        {
            EnsureDir(ScenesDir);
            var s = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildMergedScene(s, true);
            EditorSceneManager.SaveScene(s, $"{ScenesDir}/EncounterScene.unity");
        }

        public static void GenerateRunMapScene()
        {
            EnsureDir(ScenesDir);
            var s = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildMergedScene(s, true);
            EditorSceneManager.SaveScene(s, $"{ScenesDir}/RunMapScene.unity");
        }

        public static void GeneratePrototypeScene()
        {
            EnsureDir(ScenesDir);
            var s = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildMergedScene(s, true);
            EditorSceneManager.SaveScene(s, $"{ScenesDir}/PrototypeScene.unity");
        }

        public static void GeneratePrefabStaging()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid()) return;
            var existing = GameObject.Find("__PrefabStaging");
            if (existing != null) Object.DestroyImmediate(existing);
            var root = new GameObject("__PrefabStaging");
            var ui = new GameObject("UI Prefabs (Create Me)"); ui.transform.SetParent(root.transform, false);
            var world = new GameObject("World Prefabs (Create Me)"); world.transform.SetParent(root.transform, false);
            var fx = new GameObject("FX Prefabs (Create Me)"); fx.transform.SetParent(root.transform, false);
            MakeImagePrefab("EnemyView (Prefab)", ui.transform, new Color(1f, 0.3f, 0.3f), true);
            MakeImagePrefab("PlayerPieceView (Prefab)", ui.transform, Color.cyan, true);
            MakeImagePrefab("CardButtonView (Prefab)", ui.transform, Color.yellow, true);
            MakeImagePrefab("MapNodeView (Prefab)", ui.transform, Color.green, true);
            MakeImagePrefab("RockView (Prefab)", world.transform, Color.gray, false);
            MakeImagePrefab("CaveView (Prefab)", world.transform, new Color(0.6f, 0.6f, 0.7f), false);
            MakeImagePrefab("TileHighlightView (Prefab)", fx.transform, new Color(0.2f, 0.8f, 1f), false);
            MakeImagePrefab("IntentionArrowView (Prefab)", fx.transform, Color.magenta, false);
            Debug.Log("[ChessPrototype] Prefab staging root generated in active scene.");
        }

        public static void AutoLinkOpenScene()
        {
            var bootstrap = Object.FindObjectOfType<PrototypeBootstrap>(true);
            if (bootstrap == null)
            {
                Debug.LogWarning("[ChessPrototype] Auto-link skipped. PrototypeBootstrap not found in open scene.");
                return;
            }

            var session = Object.FindObjectOfType<GameSessionState>(true);
            var turn = Object.FindObjectOfType<TurnStateController>(true);
            var cards = Object.FindObjectOfType<CardRuntimeController>(true);
            var encounter = Object.FindObjectOfType<EncounterController>(true);
            var map = Object.FindObjectOfType<RunMapController>(true);
            var ui = Object.FindObjectOfType<PrototypeUIController>(true);
            var boardUi = Object.FindObjectOfType<BoardUiGenerator>(true);
            var cam = Camera.main != null ? Camera.main : Object.FindObjectOfType<Camera>(true);

            SetRef(encounter, "boardUiGenerator", boardUi);
            SetRef(bootstrap, "session", session);
            SetRef(bootstrap, "turn", turn);
            SetRef(bootstrap, "cards", cards);
            SetRef(bootstrap, "encounter", encounter);
            SetRef(bootstrap, "map", map);
            SetRef(bootstrap, "ui", ui);
            SetRef(bootstrap, "mainCamera", cam);
            var isEncounterScene = SceneManager.GetActiveScene().name.Contains("Encounter");
            SetBool(bootstrap, "autoStartEncounterOnPlay", isEncounterScene);

            if (session != null)
            {
                var cfg = AssetDatabase.LoadAssetAtPath<GameConfigDefinition>(DataConfigPath);
                if (cfg != null) SetRef(session, "config", cfg);
            }

            if (ui != null)
            {
                SetRef(ui, "mapPanel", FindGO("MapPanel"));
                SetRef(ui, "battlePanel", FindGO("BattlePanel"));
                SetRef(ui, "cardsPanel", FindGO("CardsPanel"));
                SetRef(ui, "piecePanel", FindGO("PiecePanel"));
                SetRef(ui, "phaseText", FindComponent<TMP_Text>("PhaseText"));
                SetRef(ui, "energyText", FindComponent<TMP_Text>("EnergyText"));
                SetRef(ui, "kingHpText", FindComponent<TMP_Text>("KingHpText"));
                SetRef(ui, "intentsText", FindComponent<TMP_Text>("IntentsText"));
                SetRef(ui, "mapStatusText", FindComponent<TMP_Text>("MapStatusText"));
                SetRef(ui, "pieceTitleText", FindComponent<TMP_Text>("PieceTitleText"));
                SetRef(ui, "pieceStatsText", FindComponent<TMP_Text>("PieceStatsText"));
                SetRef(ui, "cardRoot", FindComponent<RectTransform>("CardRoot"));
                SetRef(ui, "cardTemplateButton", FindComponent<Button>("CardTemplateButton"));
                SetRef(ui, "endTurnButton", FindComponent<Button>("EndTurnButton"));
                SetRef(ui, "backButton", FindComponent<Button>("BackButton"));
                SetRef(ui, "mapNodeRoot", FindComponent<RectTransform>("MapNodeRoot"));
                SetRef(ui, "mapNodeTemplateButton", FindComponent<Button>("MapNodeTemplateButton"));
                SetRef(ui, "intentLines", Object.FindObjectOfType<IntentLineRenderer2D>(true));
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[ChessPrototype] Auto-link complete for open scene.");
        }

        private static void BuildMergedScene(Scene scene, bool autoStartEncounter)
        {
            var cam = new GameObject("Main Camera");
            cam.tag = "MainCamera";
            var camera = cam.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.backgroundColor = new Color(0.08f, 0.09f, 0.12f);

            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            var inputSystemUiType = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (inputSystemUiType != null) es.AddComponent(inputSystemUiType);
            else es.AddComponent<StandaloneInputModule>();

            var managers = new GameObject("Managers");
            var session = managers.AddComponent<GameSessionState>();
            managers.AddComponent<InputModeController>();
            var turn = managers.AddComponent<TurnStateController>();
            var cards = managers.AddComponent<CardRuntimeController>();
            var encounter = managers.AddComponent<EncounterController>();
            var map = managers.AddComponent<RunMapController>();
            var ui = managers.AddComponent<PrototypeUIController>();
            var bootstrap = managers.AddComponent<PrototypeBootstrap>();

            var boardRoot = new GameObject("BoardRoot").transform;
            boardRoot.SetParent(managers.transform, false);
            SetRef(encounter, "boardRoot", boardRoot);

            var canvasGO = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

            var root = Panel("Root", canvasGO.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var mapPanel = Panel("MapPanel", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var battlePanel = Panel("BattlePanel", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var boardPanel = Panel("BoardPanel", battlePanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-290, -290), new Vector2(290, 290));
            boardPanel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.05f);
            var boardUi = boardPanel.gameObject.AddComponent<BoardUiGenerator>();
            var cardsPanel = Panel("CardsPanel", battlePanel, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 140));
            var piecePanel = Panel("PiecePanel", battlePanel, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 140));
            var hud = Panel("BattleHud", battlePanel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -90), Vector2.zero);
            var mapNodeRoot = Panel("MapNodeRoot", mapPanel, new Vector2(0, 0), new Vector2(0, 1), new Vector2(10, -210), new Vector2(320, -10));
            var cardRoot = Panel("CardRoot", cardsPanel, new Vector2(0, 0), new Vector2(1, 1), new Vector2(10, 10), new Vector2(-10, -10));
            var lineRoot = new GameObject("IntentLines").transform; lineRoot.SetParent(managers.transform, false);
            var line = managers.AddComponent<IntentLineRenderer2D>();
            SetRef(line, "root", lineRoot);

            var phaseText = TmpText("PhaseText", hud, "Phase: Player", new Vector2(10, -8), new Vector2(220, 24));
            var energyText = TmpText("EnergyText", hud, "Energy: 3", new Vector2(240, -8), new Vector2(220, 24));
            var kingText = TmpText("KingHpText", hud, "King HP: 5", new Vector2(470, -8), new Vector2(220, 24));
            var intentsText = TmpText("IntentsText", hud, "Enemy Intents", new Vector2(10, -34), new Vector2(700, 54));
            var mapStatus = TmpText("MapStatusText", mapPanel, "Run Map", new Vector2(10, -10), new Vector2(600, 24));
            var pieceTitle = TmpText("PieceTitleText", piecePanel, "Piece", new Vector2(10, -10), new Vector2(280, 30));
            var pieceStats = TmpText("PieceStatsText", piecePanel, "Stats", new Vector2(10, -45), new Vector2(420, 60));

            var endTurn = Button("EndTurnButton", cardsPanel, "End Turn", new Vector2(-120, 20), new Vector2(200, 36), new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0));
            var back = Button("BackButton", piecePanel, "Back", new Vector2(-120, 20), new Vector2(200, 36), new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0));
            var cardTpl = Button("CardTemplateButton", cardRoot, "Card Template", new Vector2(0, -20), new Vector2(220, 32)); cardTpl.gameObject.SetActive(false);
            var mapTpl = Button("MapNodeTemplateButton", mapNodeRoot, "Node Template", new Vector2(0, -20), new Vector2(290, 30)); mapTpl.gameObject.SetActive(false);

            SetRef(ui, "mapPanel", mapPanel.gameObject);
            SetRef(ui, "battlePanel", battlePanel.gameObject);
            SetRef(ui, "cardsPanel", cardsPanel.gameObject);
            SetRef(ui, "piecePanel", piecePanel.gameObject);
            SetRef(ui, "phaseText", phaseText);
            SetRef(ui, "energyText", energyText);
            SetRef(ui, "kingHpText", kingText);
            SetRef(ui, "intentsText", intentsText);
            SetRef(ui, "mapStatusText", mapStatus);
            SetRef(ui, "cardRoot", cardRoot);
            SetRef(ui, "cardTemplateButton", cardTpl);
            SetRef(ui, "endTurnButton", endTurn);
            SetRef(ui, "backButton", back);
            SetRef(ui, "pieceTitleText", pieceTitle);
            SetRef(ui, "pieceStatsText", pieceStats);
            SetRef(ui, "mapNodeRoot", mapNodeRoot);
            SetRef(ui, "mapNodeTemplateButton", mapTpl);
            SetRef(ui, "intentLines", line);

            SetRef(encounter, "boardUiGenerator", boardUi);

            SetRef(bootstrap, "session", session);
            SetRef(bootstrap, "turn", turn);
            SetRef(bootstrap, "cards", cards);
            SetRef(bootstrap, "encounter", encounter);
            SetRef(bootstrap, "map", map);
            SetRef(bootstrap, "ui", ui);
            SetRef(bootstrap, "mainCamera", camera);
            SetBool(bootstrap, "autoStartEncounterOnPlay", autoStartEncounter);

            var cfg = AssetDatabase.LoadAssetAtPath<GameConfigDefinition>(DataConfigPath);
            if (cfg != null) SetRef(session, "config", cfg);

            mapPanel.gameObject.SetActive(true);
            battlePanel.gameObject.SetActive(true);

            GeneratePrefabStaging();
        }

        private static RectTransform Panel(string name, Transform parent, Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = offMin; rt.offsetMax = offMax;
            go.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);
            return rt;
        }

        private static TextMeshProUGUI TmpText(string name, Transform parent, string value, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            var t = go.GetComponent<TextMeshProUGUI>();
            t.color = Color.white;
            t.text = value;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.enableAutoSizing = true;
            return t;
        }

        private static Button Button(string name, Transform parent, string text, Vector2 pos, Vector2 size, Vector2? anchorMin = null, Vector2? anchorMax = null, Vector2? pivot = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin ?? new Vector2(0, 1);
            rt.anchorMax = anchorMax ?? new Vector2(0, 1);
            rt.pivot = pivot ?? new Vector2(0, 1);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            go.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.25f, 0.95f);
            var label = TmpText($"{name}_Label", go.transform, text, new Vector2(8, -6), new Vector2(size.x - 16, size.y - 10));
            label.alignment = TextAlignmentOptions.Center;
            return go.GetComponent<Button>();
        }

        private static void MakeImagePrefab(string name, Transform parent, Color c, bool clickable)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(170, 28);
            go.GetComponent<Image>().color = c;
            if (clickable)
            {
                var b = go.AddComponent<Button>();
                b.onClick.AddListener(() => Debug.Log($"[ChessPrototype] Clicked {name}"));
            }
            TmpText($"{name}_Text", go.transform, name, new Vector2(4, -3), new Vector2(162, 22)).alignment = TextAlignmentOptions.MidlineLeft;
        }

        private static T FindComponent<T>(string gameObjectName) where T : Component
        {
            var go = GameObject.Find(gameObjectName);
            return go != null ? go.GetComponent<T>() : null;
        }

        private static GameObject FindGO(string name)
        {
            return GameObject.Find(name);
        }

        private static void SetRef(Object target, string field, Object value)
        {
            if (target == null) return;
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) return;
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void SetBool(Object target, string field, bool value)
        {
            if (target == null) return;
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) return;
            p.boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void EnsureDir(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path)?.Replace("\\", "/");
            var name = System.IO.Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureDir(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
#endif

