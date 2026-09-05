using System.Collections.Generic;
using SalmonRun;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// 씬 구성 — 카메라, World(배경·물·강둑·길·물결), 플레이어, 캔버스 UI, EventSystem 을
/// 현재 씬에 만들고 SalmonGame 에 연결한다. 다시 실행하면 기존 것을 지우고 새로 만든다.
///
/// 프리팹(플레이어·장애물 20종)은 여기서 만들지 않는다 — 프리팹 자체가 원본이다.
/// 장애물을 고칠 때는 프리팹을 직접 편집한다. 예전의 '스프라이트·프리팹 생성' 메뉴는
/// 손으로 붙인 아트를 덮어써 버려서 없앴다.
/// </summary>
public static class SalmonSceneBuilder
{
    const string SpriteFolder = "Assets/Art/Sprites/Generated";
    const string WhitePath = SpriteFolder + "/white.png";
    const string FogPath = SpriteFolder + "/fog.png";
    const string PlayerPrefabPath = "Assets/Prefabs/Player/PlayerSalmon.prefab";
    const string HazardFolder = "Assets/Prefabs/Hazards";
    const string FontPath = "Assets/Art/Fonts/Pretendard-Bold SDF.asset";
    const string GameRootName = "Salmon Run";

    // 배경 아트 — 941×779. PPU 27.7 → 34.0 × 28.1 유닛 (화면 32×18 + 카메라 흔들림 여유)
    const string RiverBgPath = "Assets/Art/Sprites/background1.png";
    const string CoastBgPath = "Assets/Art/Sprites/background2.png";
    const string SeaBgPath = "Assets/Art/Sprites/background3.png";
    const string LobbyMusicPath = "Assets/Resources/Audio/Morning_s_First_Leap.mp3";
    const string GameplayMusicPath = "Assets/Resources/Audio/Morning_at_the_Riverbend.mp3";
    const string GameOverMusicPath = "Assets/Resources/Audio/Light_on_the_Riverbed.mp3";
    const string MovementSoundPath = "Assets/Resources/Audio/MovementSwim.mp3";
    const string JumpSoundPath = "Assets/Resources/Audio/JumpSplash.mp3";
    const string LobbyBackgroundPath = "Assets/Resources/UI/LobbyBackground.png";
    const string LobbyStartButtonPath = "Assets/Resources/UI/LobbyStartButton.png";
    const string SoundButtonPath = "Assets/Resources/UI/SoundSettingsButton.png";
    const string ButtonClickSoundPath = "Assets/Resources/Audio/UIButtonClick.mp3";
    const float BackgroundPpu = 27.7f;
    // 강 그림의 물길(293~690px)이 화면 중앙에 오도록 타일 전체를 살짝 왼쪽으로
    const float BackgroundOffsetX = -0.76f;
    const int BackgroundTileCount = 3;

    // 3스테이지 나무 캐노피 — 512×1751, 위아래 끝 줄이 같아 원본 방향 그대로 세로로 이어진다.
    // 가운데 투명 통로가 폭의 42% 로 강 그림의 물길과 같아서, 배경 타일과 같은 34유닛 폭으로 맞춘다.
    const string TreePath = "Assets/Art/Sprites/tree.png";
    const float TreeWidth = 34f;
    const int TreeTileCount = 2;
    // 통로 중심(258.5px)이 이미지 중심(256px)에서 살짝 오른쪽이라 그만큼 왼쪽으로 민다
    const float TreeOffsetX = -0.17f;

    static Sprite white, fog;
    static TMP_FontAsset font;

    // ================================================================ 메뉴

    [MenuItem("Tools/Salmon Run/씬 구성")]
    public static void BuildScene()
    {
        LoadSprites();
        if (white == null || fog == null)
        {
            Debug.LogError("[SalmonSceneBuilder] 기본 스프라이트가 없습니다. " +
                           "Assets/Art/Sprites/Generated 의 white.png · fog.png 를 확인하세요 (git 에 들어 있습니다).");
            return;
        }
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null) Debug.LogWarning("[SalmonSceneBuilder] Pretendard 폰트가 없어 TMP 기본 폰트를 씁니다: " + FontPath);

        // 이전 결과물 제거
        var old = GameObject.Find(GameRootName);
        if (old != null) Undo.DestroyObjectImmediate(old);

        var camera = SetupCamera();

        var root = new GameObject(GameRootName);
        var game = root.AddComponent<SalmonGame>();

        // ---- World ----
        var world = new GameObject("World").transform;
        world.SetParent(root.transform, false);

        var background = BuildBackground(world, out var backgroundTiles, out var seaSprite, out var coastSprite,
            out var riverSprite);

        var treeLayer = BuildTreeLayer(world, out var treeTiles);

        var water = WorldRect("Water", world, Vector2.zero, new Vector2(36f, 22f), new Color(0.08f, 0.56f, 0.75f), -20);
        var leftBank = WorldRect("Left Bank", world, new Vector2(-13.1f, 0f), new Vector2(12f, 22f), new Color(0.25f, 0.57f, 0.35f), -10);
        var rightBank = WorldRect("Right Bank", world, new Vector2(13.1f, 0f), new Vector2(12f, 22f), new Color(0.25f, 0.57f, 0.35f), -10);

        var lanes = new List<SpriteRenderer>();
        var laneRoot = new GameObject("Route Guides").transform;
        laneRoot.SetParent(world, false);
        for (var lane = -1; lane <= 1; lane++)
            lanes.Add(WorldRect("Route Guide " + (lane + 2), laneRoot, new Vector2(lane * 3.5f, 0f),
                new Vector2(0.05f, 22f), new Color(1f, 1f, 1f, 0.09f), -8).GetComponent<SpriteRenderer>());

        var sparkRoot = new GameObject("Flow Sparks").transform;
        sparkRoot.SetParent(world, false);
        for (var i = 0; i < 24; i++)
            WorldRect("Flow Spark", sparkRoot,
                new Vector2(Random.Range(-6.5f, 6.5f), Random.Range(-10.5f, 10.5f)),
                new Vector2(Random.Range(0.025f, 0.08f), Random.Range(0.25f, 0.75f)),
                new Color(0.75f, 0.95f, 1f, Random.Range(0.16f, 0.42f)), -5);

        var hazardRoot = new GameObject("Random Hazards").transform;
        hazardRoot.SetParent(world, false);

        var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (playerPrefab == null)
        {
            Debug.LogError("[SalmonSceneBuilder] 플레이어 프리팹이 없습니다: " + PlayerPrefabPath + " (git 에 들어 있습니다).");
            return;
        }
        var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, world);
        player.name = "Player Salmon";
        player.transform.position = new Vector3(0f, -5.8f, 0f);

        // ---- UI ----
        var ui = BuildCanvas(root.transform);
        EnsureEventSystem();

        // ---- SalmonGame 연결 ----
        var so = new SerializedObject(game);
        so.FindProperty("gameCamera").objectReferenceValue = camera;
        so.FindProperty("world").objectReferenceValue = world;
        so.FindProperty("hazardRoot").objectReferenceValue = hazardRoot;
        so.FindProperty("player").objectReferenceValue = player.transform;
        so.FindProperty("playerBody").objectReferenceValue = player.transform.Find("Body").GetComponent<SpriteRenderer>();
        so.FindProperty("playerAnimator").objectReferenceValue = player.GetComponent<SalmonPlayerAnimator>();
        so.FindProperty("waterRenderer").objectReferenceValue = water.GetComponent<SpriteRenderer>();
        SetArray(so.FindProperty("bankRenderers"), leftBank.GetComponent<SpriteRenderer>(), rightBank.GetComponent<SpriteRenderer>());
        SetArray(so.FindProperty("laneRenderers"), lanes.ToArray());
        so.FindProperty("flowSparkRoot").objectReferenceValue = sparkRoot;
        so.FindProperty("ui").objectReferenceValue = ui;
        so.FindProperty("background").objectReferenceValue = background;
        so.FindProperty("seaBackground").objectReferenceValue = seaSprite;
        so.FindProperty("coastBackground").objectReferenceValue = coastSprite;
        so.FindProperty("riverBackground").objectReferenceValue = riverSprite;
        so.FindProperty("treeLayer").objectReferenceValue = treeLayer;

        if (treeLayer != null)
        {
            var tso = new SerializedObject(treeLayer);
            tso.FindProperty("game").objectReferenceValue = game;
            SetArray(tso.FindProperty("tiles"), treeTiles);
            tso.FindProperty("viewHalfHeight").floatValue = camera.orthographicSize;
            tso.ApplyModifiedPropertiesWithoutUndo();
        }

        var bso = new SerializedObject(background);
        bso.FindProperty("game").objectReferenceValue = game;
        SetArray(bso.FindProperty("tiles"), backgroundTiles);
        bso.FindProperty("viewHalfHeight").floatValue = camera.orthographicSize;
        bso.ApplyModifiedPropertiesWithoutUndo();

        var prefabs = new List<Object>();
        foreach (HazardKind kind in System.Enum.GetValues(typeof(HazardKind)))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<SalmonHazard>($"{HazardFolder}/{kind}.prefab");
            if (prefab != null) prefabs.Add(prefab);
            else Debug.LogWarning($"[SalmonSceneBuilder] 장애물 프리팹 없음: {kind}");
        }
        SetArray(so.FindProperty("hazardPrefabs"), prefabs.ToArray());
        so.ApplyModifiedPropertiesWithoutUndo();

        Undo.RegisterCreatedObjectUndo(root, "Build Salmon Run scene");
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("[SalmonSceneBuilder] 씬 구성 완료 — 장애물 프리팹 " + prefabs.Count + "개 연결");
    }

    // ================================================================ 배경 이미지

    /// <summary>
    /// 배경 아트 3장을 임포트 설정까지 맞춘 뒤, 세로로 쌓인 타일 3장과 SalmonBackground 를 만든다.
    /// 타일은 정렬 순서 -40 — 물 보정 레이어(-20)와 게임플레이(2~45) 아래에 깔린다.
    /// </summary>
    static SalmonBackground BuildBackground(Transform world, out Object[] tiles,
        out Sprite sea, out Sprite coast, out Sprite river)
    {
        ConfigureSprite(SeaBgPath, BackgroundPpu);
        ConfigureSprite(CoastBgPath, BackgroundPpu);
        ConfigureSprite(RiverBgPath, BackgroundPpu);
        sea = AssetDatabase.LoadAssetAtPath<Sprite>(SeaBgPath);
        coast = AssetDatabase.LoadAssetAtPath<Sprite>(CoastBgPath);
        river = AssetDatabase.LoadAssetAtPath<Sprite>(RiverBgPath);
        if (sea == null || coast == null || river == null)
            Debug.LogWarning("[SalmonSceneBuilder] 배경 이미지를 못 읽었습니다. " +
                             "Assets/Art/Sprites/background1~3.png 를 확인하세요.");

        var root = new GameObject("Background");
        root.transform.SetParent(world, false);
        root.transform.localPosition = new Vector3(BackgroundOffsetX, 0f, 0f);
        var scroller = root.AddComponent<SalmonBackground>();

        // 런타임 SalmonBackground.seamOverlap 과 같은 값만큼 겹쳐 둔다
        var height = (sea != null ? sea.bounds.size.y : 28.1f) - 0.12f;
        var renderers = new List<Object>();
        for (var i = 0; i < BackgroundTileCount; i++)
        {
            var go = new GameObject("Background Tile " + (i + 1));
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3(0f, (i - (BackgroundTileCount - 1) * 0.5f) * height, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sea;
            sr.flipY = (i & 1) == 1;
            sr.sortingOrder = -40;
            renderers.Add(sr);
        }
        tiles = renderers.ToArray();
        return scroller;
    }

    /// <summary>
    /// 3스테이지 나무 캐노피 레이어. 배경 타일(-40)과 물 보정(-20) 사이에 깔아 배경 위로 얹는다.
    /// 상하 반전 없이 원본 방향으로만 이어붙인다.
    /// </summary>
    static SalmonTreeLayer BuildTreeLayer(Transform world, out Object[] tiles)
    {
        tiles = new Object[0];
        // 512px 이 34유닛이 되도록 PPU 를 맞춰 두면 타일 스케일이 1로 떨어진다
        ConfigureSprite(TreePath, 512f / TreeWidth);
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(TreePath);
        if (sprite == null)
        {
            Debug.LogWarning("[SalmonSceneBuilder] 나무 그림을 못 읽었습니다: " + TreePath);
            return null;
        }

        var root = new GameObject("Tree Overlay");
        root.transform.SetParent(world, false);
        root.transform.localPosition = new Vector3(TreeOffsetX, 0f, 0f);
        var layer = root.AddComponent<SalmonTreeLayer>();

        var height = sprite.bounds.size.y;
        var renderers = new List<Object>();
        for (var i = 0; i < TreeTileCount; i++)
        {
            var go = new GameObject("Tree Tile " + (i + 1));
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3(0f, (i - (TreeTileCount - 1) * 0.5f) * height, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = -30;
            sr.color = new Color(1f, 1f, 1f, 0f);   // 3스테이지에 들어설 때까지 숨어 있는다
            sr.enabled = false;
            renderers.Add(sr);
        }
        tiles = renderers.ToArray();
        return layer;
    }

    // ================================================================ 카메라 / EventSystem

    static Camera SetupCamera()
    {
        var camera = Camera.main;
        if (camera == null)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            camera = go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();
        }
        camera.orthographic = true;
        camera.orthographicSize = 9f;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.03f, 0.12f, 0.2f);
        EditorUtility.SetDirty(camera);
        return camera;
    }

    static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<InputSystemUIInputModule>();
    }

    // ================================================================ 캔버스 UI

    static SalmonUI BuildCanvas(Transform parent)
    {
        var canvasGo = new GameObject("Salmon Run Canvas");
        canvasGo.transform.SetParent(parent, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        var ui = canvasGo.AddComponent<SalmonUI>();
        var so = new SerializedObject(ui);
        var c = canvasGo.transform;

        var subtitleColor = new Color(0.85f, 0.96f, 1f);
        var smallColor = new Color(0.85f, 0.94f, 0.97f);
        var lobbyBackgroundArt = AssetDatabase.LoadAssetAtPath<Sprite>(LobbyBackgroundPath);
        var lobbyStartButtonArt = AssetDatabase.LoadAssetAtPath<Sprite>(LobbyStartButtonPath);
        var soundButtonArt = AssetDatabase.LoadAssetAtPath<Sprite>(SoundButtonPath);
        var buttonClickSound = AssetDatabase.LoadAssetAtPath<AudioClip>(ButtonClickSoundPath);

        // ---------- 로비 ----------
        var lobby = Group("Lobby Panel", c);
        Full(Img("Dim", lobby, lobbyBackgroundArt != null ? Color.white : new Color(0.01f, 0.08f, 0.14f, 0.34f), lobbyBackgroundArt));
        Text("Title", lobby, new Rect(360, 160, 1200, 110), "SALMON RUN", 78, Color.white, TextAlignmentOptions.Center, true);
        Text("Subtitle", lobby, new Rect(440, 272, 1040, 55), "거슬러 올라가, 고향으로", 28, subtitleColor, TextAlignmentOptions.Center);
        Panel("Panel", lobby, new Rect(610, 400, 700, 360));

        var menu = Group("Menu Group", lobby);
        var startBtn = MakeButton("Start Button", menu, new Rect(980, 500, 620, 384), lobbyStartButtonArt == null ? "게임 시작" : "", lobbyStartButtonArt);
        var settingsBtn = MakeButton("Settings Button", menu, new Rect(1235, 785, 110, 110), soundButtonArt == null ? "음향" : "", soundButtonArt);
        Text("Hint", menu, new Rect(760, 982, 1000, 38), "WASD / 방향키 이동  ·  SPACE 점프  ·  ESC 일시정지 / 음향 설정", 27, Color.white, TextAlignmentOptions.Center, true);

        var settings = Group("Settings Group", lobby);
        Text("Settings Title", settings, new Rect(705, 438, 510, 50), "사운드 설정", 27, Color.white, TextAlignmentOptions.Center, true);
        Text("Volume Label", settings, new Rect(700, 520, 180, 42), "BGM 음량", 21, smallColor, TextAlignmentOptions.Left);
        var slider = MakeSlider("Volume Slider", settings, new Rect(880, 530, 290, 30));
        var volumeText = Text("Volume Value", settings, new Rect(1178, 516, 70, 42), "75%", 21, smallColor, TextAlignmentOptions.Left);
        Text("Effects Volume Label", settings, new Rect(700, 580, 180, 42), "효과음 음량", 21, smallColor, TextAlignmentOptions.Left);
        var effectsSlider = MakeSlider("Effects Volume Slider", settings, new Rect(880, 590, 290, 30));
        effectsSlider.value = 0.85f;
        var effectsVolumeText = Text("Effects Volume Value", settings, new Rect(1178, 576, 70, 42), "85%", 21, smallColor, TextAlignmentOptions.Left);
        var backBtn = MakeButton("Back Button", settings, new Rect(800, 660, 320, 65), "돌아가기");
        settings.gameObject.SetActive(false);

        var lobbyBest = Text("Best Score", lobby, new Rect(1080, 918, 420, 42), "최고 점수  0", 27, Color.white, TextAlignmentOptions.Center, true);

        // ---------- 일시정지 ----------
        var pause = Group("Pause Panel", c);
        Full(Img("Dim", pause, new Color(0.005f, 0.018f, 0.035f, 0.78f))).raycastTarget = true;
        Panel("Panel", pause, new Rect(610, 400, 700, 360));
        Text("Pause Title", pause, new Rect(705, 438, 510, 50), "일시정지", 27, Color.white, TextAlignmentOptions.Center, true);
        Text("Volume Label", pause, new Rect(700, 520, 180, 42), "BGM 음량", 21, smallColor, TextAlignmentOptions.Left);
        var pauseSlider = MakeSlider("Volume Slider", pause, new Rect(880, 530, 290, 30));
        var pauseVolumeText = Text("Volume Value", pause, new Rect(1178, 516, 70, 42), "75%", 21, smallColor, TextAlignmentOptions.Left);
        Text("Effects Volume Label", pause, new Rect(700, 580, 180, 42), "효과음 음량", 21, smallColor, TextAlignmentOptions.Left);
        var pauseEffectsSlider = MakeSlider("Effects Volume Slider", pause, new Rect(880, 590, 290, 30));
        pauseEffectsSlider.value = 0.85f;
        var pauseEffectsVolumeText = Text("Effects Volume Value", pause, new Rect(1178, 576, 70, 42), "85%", 21, smallColor, TextAlignmentOptions.Left);
        var resumeBtn = MakeButton("Resume Button", pause, new Rect(660, 660, 280, 65), "게임 계속");
        var pauseLobbyBtn = MakeButton("Pause Lobby Button", pause, new Rect(980, 660, 280, 65), "로비로 가기");
        pause.gameObject.SetActive(false);

        // ---------- HUD ----------
        var hud = Group("HUD Panel", c);
        var fogOverlay = Img("Fog Overlay", hud, Color.white, fog);
        Place(fogOverlay.rectTransform, new Rect(-120, -80, 2160, 1240));
        fogOverlay.gameObject.SetActive(false);
        var fogTint = Full(Img("Fog Tint", hud, new Color(0.82f, 0.87f, 0.9f, 0f)));
        fogTint.gameObject.SetActive(false);
        var hurt = Full(Img("Hurt Flash", hud, new Color(0.85f, 0.03f, 0.02f, 0f)));
        hurt.gameObject.SetActive(false);
        var heal = Full(Img("Heal Flash", hud, new Color(0.08f, 1f, 0.42f, 0f)));
        heal.gameObject.SetActive(false);

        Panel("Status Panel", hud, new Rect(36, 30, 520, 138), 0.76f);
        var stageText = Text("Stage", hud, new Rect(62, 44, 470, 42), "STAGE 1", 27, Color.white, TextAlignmentOptions.Left, true);
        Text("Health Label", hud, new Rect(62, 92, 220, 38), "체력", 21, smallColor, TextAlignmentOptions.Left);
        Place(Img("Health Background", hud, new Color(0.02f, 0.04f, 0.08f, 0.75f)).rectTransform, new Rect(145, 100, 360, 24));
        var healthFill = FillBar("Health Fill", hud, new Rect(149, 104, 352, 16), new Color(0.35f, 0.94f, 0.48f));
        var healthText = Text("Health Value", hud, new Rect(150, 124, 350, 28), "100 / 100", 21, smallColor, TextAlignmentOptions.Left);

        Panel("Score Panel", hud, new Rect(1450, 30, 430, 138), 0.76f);
        var scoreText = Text("Score", hud, new Rect(1480, 48, 370, 42), "SCORE  0", 27, Color.white, TextAlignmentOptions.Left, true);
        var progressText = Text("Progress", hud, new Rect(1480, 98, 370, 35), "구간 진행  0%", 21, smallColor, TextAlignmentOptions.Left);
        Place(Img("Progress Background", hud, new Color(1f, 1f, 1f, 0.18f)).rectTransform, new Rect(1480, 138, 350, 8));
        var progressFill = FillBar("Progress Fill", hud, new Rect(1480, 138, 350, 8), new Color(1f, 0.75f, 0.25f));

        var banner = Group("Stage Banner", hud);
        var bannerGroup = banner.gameObject.AddComponent<CanvasGroup>();
        Place(Img("Banner Background", banner, new Color(0.015f, 0.06f, 0.1f, 0.68f)).rectTransform, new Rect(480, 195, 960, 118));
        var bannerTitle = Text("Banner Title", banner, new Rect(520, 205, 880, 46), "", 27, Color.white, TextAlignmentOptions.Center, true);
        var bannerSub = Text("Banner Subtitle", banner, new Rect(520, 252, 880, 42), "", 28, subtitleColor, TextAlignmentOptions.Center);
        banner.gameObject.SetActive(false);

        var eventText = Text("Event Text", hud, new Rect(500, 215, 920, 65), "", 27, Color.white, TextAlignmentOptions.Center, true);
        eventText.gameObject.SetActive(false);

        var fogWarning = Group("Fog Warning", hud);
        Place(Img("Warning Background", fogWarning, new Color(0.06f, 0.09f, 0.12f, 0.82f)).rectTransform, new Rect(455, 320, 1010, 105));
        Text("Warning Text", fogWarning, new Rect(500, 336, 920, 72), "안개 구간입니다!", 27, Color.white, TextAlignmentOptions.Center, true);
        fogWarning.gameObject.SetActive(false);

        Text("Controls Hint", hud, new Rect(40, 1018, 920, 35), "WASD / 방향키 이동   SPACE 점프   초록 구슬 체력 +25", 21, smallColor, TextAlignmentOptions.Left);
        hud.gameObject.SetActive(false);

        // ---------- 게임 오버 ----------
        var over = Group("Game Over Panel", c);
        Full(Img("Dim", over, new Color(0.015f, 0.025f, 0.06f, 0.72f)));
        Text("Title", over, new Rect(350, 205, 1220, 100), "여정이 끝났습니다", 78, Color.white, TextAlignmentOptions.Center, true);
        Panel("Panel", over, new Rect(630, 360, 660, 390));
        var finalScore = Text("Final Score", over, new Rect(700, 405, 520, 55), "최종 점수  0", 27, Color.white, TextAlignmentOptions.Center, true);
        var overBest = Text("Best Score", over, new Rect(700, 470, 520, 45), "최고 점수  0", 27, Color.white, TextAlignmentOptions.Center, true);
        var overStage = Text("Stage", over, new Rect(700, 525, 520, 40), "", 21, smallColor, TextAlignmentOptions.Left);
        var restartBtn = MakeButton("Restart Button", over, new Rect(755, 600, 410, 68), "다시 시작");
        var lobbyBtn = MakeButton("Lobby Button", over, new Rect(755, 682, 410, 55), "로비로");
        over.gameObject.SetActive(false);

        // ---------- 참조 연결 ----------
        Bind(so, "lobbyPanel", lobby.gameObject);
        Bind(so, "hudPanel", hud.gameObject);
        Bind(so, "gameOverPanel", over.gameObject);
        Bind(so, "lobbyMenuGroup", menu.gameObject);
        Bind(so, "lobbySettingsGroup", settings.gameObject);
        Bind(so, "startButton", startBtn);
        Bind(so, "settingsButton", settingsBtn);
        Bind(so, "backButton", backBtn);
        Bind(so, "volumeSlider", slider);
        Bind(so, "volumeText", volumeText);
        Bind(so, "effectsVolumeSlider", effectsSlider);
        Bind(so, "effectsVolumeText", effectsVolumeText);
        Bind(so, "lobbyBestScoreText", lobbyBest);
        Bind(so, "lobbyBackgroundArtwork", lobbyBackgroundArt);
        Bind(so, "startButtonArtwork", lobbyStartButtonArt);
        Bind(so, "soundButtonArtwork", soundButtonArt);
        Bind(so, "buttonClickSound", buttonClickSound);
        Bind(so, "pausePanel", pause.gameObject);
        Bind(so, "pauseVolumeSlider", pauseSlider);
        Bind(so, "pauseVolumeText", pauseVolumeText);
        Bind(so, "pauseEffectsVolumeSlider", pauseEffectsSlider);
        Bind(so, "pauseEffectsVolumeText", pauseEffectsVolumeText);
        Bind(so, "resumeButton", resumeBtn);
        Bind(so, "pauseLobbyButton", pauseLobbyBtn);
        Bind(so, "stageText", stageText);
        Bind(so, "healthFill", healthFill);
        Bind(so, "healthText", healthText);
        Bind(so, "scoreText", scoreText);
        Bind(so, "progressText", progressText);
        Bind(so, "progressFill", progressFill);
        Bind(so, "banner", bannerGroup);
        Bind(so, "bannerTitle", bannerTitle);
        Bind(so, "bannerSubtitle", bannerSub);
        Bind(so, "eventText", eventText);
        Bind(so, "fogWarning", fogWarning.gameObject);
        Bind(so, "fogOverlay", fogOverlay);
        Bind(so, "fogTint", fogTint);
        Bind(so, "hurtFlash", hurt);
        Bind(so, "healFlash", heal);
        Bind(so, "finalScoreText", finalScore);
        Bind(so, "gameOverBestText", overBest);
        Bind(so, "gameOverStageText", overStage);
        Bind(so, "restartButton", restartBtn);
        Bind(so, "lobbyButton", lobbyBtn);
        so.ApplyModifiedPropertiesWithoutUndo();
        return ui;
    }

    // ---- UI 조립 도우미: OnGUI 좌표계(1920×1080, 좌상단 원점)를 그대로 쓴다 ----

    static RectTransform Group(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        Stretch(rt);
        return rt;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static void Place(RectTransform rt, Rect r)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(r.x, -r.y);
        rt.sizeDelta = new Vector2(r.width, r.height);
    }

    static Image Img(string name, Transform parent, Color color, Sprite sprite = null)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        img.sprite = sprite;
        img.raycastTarget = false;
        return img;
    }

    static Image Full(Image img)
    {
        Stretch(img.rectTransform);
        return img;
    }

    static void Panel(string name, Transform parent, Rect r, float alpha = 0.86f)
    {
        var bg = Img(name, parent, new Color(0.015f, 0.07f, 0.12f, alpha));
        Place(bg.rectTransform, r);
        var accent = Img("Accent", bg.transform, new Color(0.26f, 0.87f, 0.94f, 0.8f));
        var art = accent.rectTransform;
        art.anchorMin = new Vector2(0f, 1f);
        art.anchorMax = new Vector2(1f, 1f);
        art.pivot = new Vector2(0.5f, 1f);
        art.anchoredPosition = Vector2.zero;
        art.sizeDelta = new Vector2(0f, 3f);
    }

    static TMP_Text Text(string name, Transform parent, Rect r, string text, float size, Color color,
        TextAlignmentOptions alignment, bool bold = false)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        Place(tmp.rectTransform, r);
        return tmp;
    }

    static Button MakeButton(string name, Transform parent, Rect r, string label, Sprite artwork = null)
    {
        var bg = Img(name, parent, artwork != null ? Color.white : new Color(0.10f, 0.32f, 0.42f, 0.95f), artwork);
        bg.raycastTarget = true;
        bg.preserveAspect = artwork != null;
        Place(bg.rectTransform, r);
        var button = bg.gameObject.AddComponent<Button>();
        var colors = button.colors;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
        button.colors = colors;
        if (!string.IsNullOrEmpty(label))
        {
            var text = Text("Label", bg.transform, new Rect(0, 0, r.width, r.height), label, 28, Color.white, TextAlignmentOptions.Center, true);
            Stretch(text.rectTransform);
        }
        return button;
    }

    static Slider MakeSlider(string name, Transform parent, Rect r)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        Place(rt, r);

        var bg = Img("Background", go.transform, new Color(0.02f, 0.05f, 0.09f, 0.9f));
        Stretch(bg.rectTransform);
        bg.rectTransform.offsetMin = new Vector2(0f, r.height * 0.3f);
        bg.rectTransform.offsetMax = new Vector2(0f, -r.height * 0.3f);

        var fillArea = Group("Fill Area", go.transform);
        fillArea.offsetMin = new Vector2(0f, r.height * 0.3f);
        fillArea.offsetMax = new Vector2(0f, -r.height * 0.3f);
        var fill = Img("Fill", fillArea, new Color(0.26f, 0.87f, 0.94f));
        Stretch(fill.rectTransform);

        var handleArea = Group("Handle Slide Area", go.transform);
        var handle = Img("Handle", handleArea, Color.white);
        handle.raycastTarget = true;
        handle.rectTransform.anchorMin = new Vector2(0f, 0f);
        handle.rectTransform.anchorMax = new Vector2(0f, 1f);
        handle.rectTransform.sizeDelta = new Vector2(r.height * 0.8f, 0f);

        var slider = go.AddComponent<Slider>();
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 0.75f;
        return slider;
    }

    static Image FillBar(string name, Transform parent, Rect r, Color color)
    {
        var img = Img(name, parent, color, white);
        Place(img.rectTransform, r);
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        img.fillOrigin = (int)Image.OriginHorizontal.Left;
        img.fillAmount = 1f;
        return img;
    }

    static void Bind(SerializedObject so, string field, Object value)
    {
        var p = so.FindProperty(field);
        if (p == null) { Debug.LogWarning("[SalmonSceneBuilder] SalmonUI에 필드가 없습니다: " + field); return; }
        p.objectReferenceValue = value;
    }

    static void SetArray(SerializedProperty array, params Object[] values)
    {
        array.arraySize = values.Length;
        for (var i = 0; i < values.Length; i++)
            array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    // ================================================================ 월드 스프라이트 도우미 (SalmonVisuals와 같은 규격, 에셋 스프라이트 사용)

    static GameObject WorldRect(string name, Transform parent, Vector2 position, Vector2 size, Color color, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(position.x, position.y, 0f);
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = white;
        sr.color = color;
        sr.sortingOrder = order;
        return go;
    }

    // ================================================================ 스프라이트 에셋

    static void LoadSprites()
    {
        white = AssetDatabase.LoadAssetAtPath<Sprite>(WhitePath);
        fog = AssetDatabase.LoadAssetAtPath<Sprite>(FogPath);
    }

    static void ConfigureSprite(string path, float ppu)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer == null) return;
        if (importer.textureType == TextureImporterType.Sprite && Mathf.Approximately(importer.spritePixelsPerUnit, ppu)) return;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = ppu;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();
    }

}
