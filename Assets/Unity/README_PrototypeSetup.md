# Chess Prototype Unity Scaffold - Wiring Guide

This guide documents the expected scene wiring for the generated Unity scaffold.

## One-click flow
1. Run `Tools/ChessPrototype/Generate All (Prototype)`.
2. Open `Assets/Unity/Scenes/EncounterScene.unity` or `Assets/Unity/Scenes/RunMapScene.unity`.
3. Press Play.
4. If something is missing, run `Tools/ChessPrototype/Validate Setup` and then `Tools/ChessPrototype/Auto-Link References` on the open scene.

## Required scene objects
- `Main Camera` (Orthographic)
- `EventSystem`
- `Managers` with:
  - `GameSessionState`
  - `TurnStateController`
  - `CardRuntimeController`
  - `EncounterController`
  - `RunMapController`
  - `PrototypeUIController`
  - `PrototypeBootstrap`
  - `IntentLineRenderer2D`
- `BoardRoot` (assigned to `EncounterController.boardRoot`)
- `Canvas` root UI hierarchy
- `__PrefabStaging` (optional but recommended)

## Panels and contents
- `MapPanel`
  - `MapStatusText`
  - `MapNodeRoot`
  - `MapNodeTemplateButton` (inactive template)
- `BattlePanel`
  - `BattleHud`
    - `PhaseText`
    - `EnergyText`
    - `KingHpText`
    - `IntentsText`
  - `CardsPanel`
    - `CardRoot`
    - `CardTemplateButton` (inactive template)
    - `EndTurnButton`
  - `PiecePanel`
    - `PieceTitleText`
    - `PieceStatsText`
    - `BackButton`

## Prefab staging contract
`__PrefabStaging` contains placeholders named with `(Prefab)`:
- UI Prefabs (Create Me)
  - `EnemyView (Prefab)` (generic enemy view)
  - `PlayerPieceView (Prefab)`
  - `CardButtonView (Prefab)`
  - `MapNodeView (Prefab)`
- World Prefabs (Create Me)
  - `RockView (Prefab)`
  - `CaveView (Prefab)`
- FX Prefabs (Create Me)
  - `TileHighlightView (Prefab)`
  - `IntentionArrowView (Prefab)`

You can drag these into a Prefabs folder and replace visuals later.

## Data assets expected
`Assets/Unity/Data/`:
- `Config/GameConfig.asset`
- `Cards/*.asset`
- `Enemies/*.asset`
- `Pieces/*.asset`
- `Encounters/E01..E10.asset`
- `Maps/RunMap.asset`

## Behavior guarantees in this scaffold
- Run map is graph-based pathing (`RunMapDefinition.edges` are legal moves).
- Start: tier 1 nodes only. After completion: only directly linked next nodes.
- Enemy intents use one plan object for preview and execution.
- Enemy phase executes sequentially enemy-by-enemy.
- If enemy planned move tile is blocked at execution, enemy stays in place (bounce-back behavior).
- Clicking same selected piece calls the same close flow as Back button.

## If validation fails
- Missing refs in scene: open scene, run `Auto-Link References`, save scene.
- Missing assets: run `Generate Data Assets`.
- Missing scenes: run scene generators again.
