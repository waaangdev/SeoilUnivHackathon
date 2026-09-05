using System.Collections.Generic;
using System.IO;
using SalmonRun;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// 예전에 SalmonGame이 런타임에 만들던 오브젝트들을 에셋·씬으로 옮기는 도구.
///  1. 스프라이트·프리팹 생성 — 흰색/원/안개 PNG, 플레이어 프리팹, 장애물 프리팹 20종
///  2. 씬 구성 — 카메라, World(물·강둑·길·물결), 플레이어, 캔버스 UI, EventSystem 을 현재 씬에 만들고 SalmonGame에 연결
/// 다시 실행하면 기존 것을 지우고 새로 만든다.
/// </summary>
public static class SalmonSceneBuilder
{
    const string SpriteFolder = "Assets/Art/Sprites/Generated";
    const string WhitePath = SpriteFolder + "/white.png";
    const string CirclePath = SpriteFolder + "/circle.png";
    const string FogPath = SpriteFolder + "/fog.png";
    const string PlayerPrefabPath = "Assets/Prefabs/Player/PlayerSalmon.prefab";
    const string HazardFolder = "Assets/Prefabs/Hazards";
    const string FontPath = "Assets/Art/Fonts/Pretendard-Bold SDF.asset";
    const string GameRootName = "Salmon Run";

    // 배경 아트 — 941×779. PPU 27.7 → 34.0 × 28.1 유닛 (화면 32×18 + 카메라 흔들림 여유)
    const string RiverBgPath = "Assets/Art/Sprites/background1.png";
    const string CoastBgPath = "Assets/Art/Sprites/background2.png";
    const string SeaBgPath = "Assets/Art/Sprites/background3.png";
    const float BackgroundPpu = 27.7f;
    // 강 그림의 물길(293~690px)이 화면 중앙에 오도록 타일 전체를 살짝 왼쪽으로
    const float BackgroundOffsetX = -0.76f;
    const int BackgroundTileCount = 3;

    static Sprite white, circle, fog;
    static TMP_FontAsset font;

    // ================================================================ 메뉴

    [MenuItem("Tools/Salmon Run/1. 스프라이트·프리팹 생성")]
    public static void GenerateAssets()
    {
        LoadSprites(create: true);
        BuildPlayerPrefab();
        foreach (HazardKind kind in System.Enum.GetValues(typeof(HazardKind)))
            BuildHazardPrefab(kind);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[SalmonSceneBuilder] 스프라이트 3개, 플레이어 프리팹, 장애물 프리팹 " +
                  System.Enum.GetValues(typeof(HazardKind)).Length + "개 생성 완료");
    }

    [MenuItem("Tools/Salmon Run/2. 씬 구성")]
    public static void BuildScene()
    {
        LoadSprites(create: false);
        if (white == null || circle == null || fog == null)
        {
            Debug.LogError("[SalmonSceneBuilder] 스프라이트가 없습니다. 먼저 '1. 스프라이트·프리팹 생성'을 실행하세요.");
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
            Debug.LogError("[SalmonSceneBuilder] 플레이어 프리팹이 없습니다. 먼저 '1. 스프라이트·프리팹 생성'을 실행하세요.");
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
        so.FindProperty("waterRenderer").objectReferenceValue = water.GetComponent<SpriteRenderer>();
        SetArray(so.FindProperty("bankRenderers"), leftBank.GetComponent<SpriteRenderer>(), rightBank.GetComponent<SpriteRenderer>());
        SetArray(so.FindProperty("laneRenderers"), lanes.ToArray());
        so.FindProperty("flowSparkRoot").objectReferenceValue = sparkRoot;
        so.FindProperty("ui").objectReferenceValue = ui;
        so.FindProperty("background").objectReferenceValue = background;
        so.FindProperty("seaBackground").objectReferenceValue = seaSprite;
        so.FindProperty("coastBackground").objectReferenceValue = coastSprite;
        so.FindProperty("riverBackground").objectReferenceValue = riverSprite;

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

    // ================================================================ 플레이어 프리팹

    static void BuildPlayerPrefab()
    {
        var root = new GameObject("Player Salmon");
        WorldCircle("Body", root.transform, Vector2.zero, new Vector2(1.15f, 1.75f), new Color(1f, 0.36f, 0.30f), 20);
        WorldCircle("Belly", root.transform, new Vector2(0f, -0.15f), new Vector2(0.65f, 1.15f), new Color(1f, 0.68f, 0.52f), 21);
        var tail = WorldRect("Tail", root.transform, new Vector2(0f, -1f), new Vector2(0.72f, 0.72f), new Color(0.92f, 0.23f, 0.25f), 19);
        tail.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        WorldCircle("Left Eye", root.transform, new Vector2(-0.25f, 0.52f), Vector2.one * 0.16f, Color.white, 22);
        WorldCircle("Right Eye", root.transform, new Vector2(0.25f, 0.52f), Vector2.one * 0.16f, Color.white, 22);
        WorldCircle("Left Pupil", root.transform, new Vector2(-0.25f, 0.55f), Vector2.one * 0.07f, new Color(0.06f, 0.09f, 0.14f), 23);
        WorldCircle("Right Pupil", root.transform, new Vector2(0.25f, 0.55f), Vector2.one * 0.07f, new Color(0.06f, 0.09f, 0.14f), 23);
        SavePrefab(root, PlayerPrefabPath);
    }

    // ================================================================ 장애물 프리팹

    static void BuildHazardPrefab(HazardKind kind)
    {
        var root = new GameObject(kind.ToString());
        var hazard = root.AddComponent<SalmonHazard>();
        hazard.Kind = kind;
        var t = root.transform;

        switch (kind)
        {
            case HazardKind.Seaweed:
                hazard.Radius = 0.8f;
                float[] heights = { 1.35f, 1.6f, 1.45f };
                for (var i = -1; i <= 1; i++)
                {
                    var weed = WorldRect("Seaweed", t, new Vector2(i * 0.28f, 0f), new Vector2(0.18f, heights[i + 1]), new Color(0.08f, 0.48f, 0.28f), 5);
                    weed.transform.localRotation = Quaternion.Euler(0f, 0f, i * 12f);
                }
                break;
            case HazardKind.Branch:
                hazard.Radius = 1.25f; hazard.Damage = 7f;
                WorldRect("Branch", t, Vector2.zero, new Vector2(2.5f, 0.28f), new Color(0.35f, 0.18f, 0.07f), 7);
                break;
            case HazardKind.Leaf:
                hazard.Radius = 0f;
                WorldCircle("Leaf", t, Vector2.zero, new Vector2(1.4f, 0.8f), new Color(0.33f, 0.67f, 0.25f, 0.78f), 28);
                break;
            case HazardKind.Jellyfish:
                hazard.Radius = 0.62f; hazard.Damage = 13f;
                WorldCircle("Jellyfish", t, Vector2.zero, new Vector2(1.1f, 0.9f), new Color(0.82f, 0.62f, 1f, 0.88f), 8);
                for (var i = -1; i <= 1; i++) WorldRect("Tentacle", t, new Vector2(i * 0.28f, -0.6f), new Vector2(0.08f, 0.75f), new Color(0.75f, 0.45f, 0.92f), 7);
                break;
            case HazardKind.Boulder:
                hazard.Radius = 1.75f; hazard.Damage = 15f;
                WorldCircle("Boulder", t, Vector2.zero, new Vector2(3.1f, 2.5f), new Color(0.25f, 0.28f, 0.30f), 8);
                WorldCircle("Highlight", t, new Vector2(-0.55f, 0.5f), new Vector2(0.65f, 0.4f), new Color(0.42f, 0.44f, 0.43f), 9);
                break;
            case HazardKind.Rapid:
                hazard.Radius = 1.6f; hazard.Velocity = Vector2.right * 0.35f; // 방향은 스폰 시 랜덤
                for (var i = -1; i <= 1; i++) WorldRect("Rapid", t, new Vector2(0f, i * 0.42f), new Vector2(2.7f, 0.12f), new Color(0.82f, 0.96f, 1f, 0.75f), 3);
                break;
            case HazardKind.Log:
                hazard.Radius = 1.2f; hazard.Damage = 18f; hazard.Velocity = Vector2.down * 2.8f;
                WorldRect("Log", t, Vector2.zero, new Vector2(2.4f, 0.62f), new Color(0.42f, 0.23f, 0.08f), 9);
                WorldCircle("Cut", t, new Vector2(1.12f, 0f), new Vector2(0.25f, 0.58f), new Color(0.68f, 0.45f, 0.2f), 10);
                break;
            case HazardKind.Whirlpool:
                hazard.Radius = 1.15f;
                for (var i = 0; i < 4; i++) WorldCircle("Whirl", t, new Vector2(i * 0.2f - 0.3f, 0f), Vector2.one * (2.5f - i * 0.5f), new Color(0.05f, 0.31f, 0.48f, 0.35f), 4 + i);
                break;
            case HazardKind.FishSchool:
                hazard.Radius = 1.1f; hazard.Damage = 9f; // 속도는 스폰 시 랜덤
                for (var i = 0; i < 5; i++) WorldCircle("Small Fish", t, new Vector2((i % 3) * 0.55f - 0.55f, (i / 3) * 0.48f - 0.25f), new Vector2(0.58f, 0.28f), new Color(0.85f, 0.82f, 0.38f), 8);
                break;
            case HazardKind.Stone:
                hazard.Radius = 0.5f; hazard.Damage = 11f;
                WorldCircle("Stone", t, Vector2.zero, new Vector2(0.82f, 0.75f), new Color(0.31f, 0.35f, 0.39f), 8);
                break;
            case HazardKind.Bird:
                hazard.Radius = 0.75f; hazard.Damage = 18f; hazard.Velocity = Vector2.down * 3.4f;
                WorldCircle("Bird", t, Vector2.zero, new Vector2(1.0f, 0.65f), new Color(0.08f, 0.09f, 0.13f), 18);
                WorldRect("Left Wing", t, new Vector2(-0.55f, 0f), new Vector2(0.85f, 0.2f), new Color(0.12f, 0.13f, 0.18f), 17).transform.localRotation = Quaternion.Euler(0f, 0f, 22f);
                WorldRect("Right Wing", t, new Vector2(0.55f, 0f), new Vector2(0.85f, 0.2f), new Color(0.12f, 0.13f, 0.18f), 17).transform.localRotation = Quaternion.Euler(0f, 0f, -22f);
                break;
            case HazardKind.Fog:
                hazard.Radius = 0f; hazard.Life = 5.5f; // 속도·가로 크기는 스폰 시
                var fogGo = new GameObject("Dense Natural Fog");
                fogGo.transform.SetParent(t, false);
                fogGo.transform.localScale = new Vector3(3f, 2.8f, 1f);
                hazard.FogRenderer = fogGo.AddComponent<SpriteRenderer>();
                hazard.FogRenderer.sprite = fog;
                hazard.FogRenderer.color = new Color(0.84f, 0.89f, 0.92f, 0f);
                hazard.FogRenderer.sortingOrder = 45;
                break;
            case HazardKind.Debris:
                hazard.Radius = 0.75f; hazard.Damage = 14f; hazard.Velocity = Vector2.right * 7f; // 방향은 스폰 시
                WorldRect("Debris", t, Vector2.zero, new Vector2(1.5f, 0.55f), new Color(0.39f, 0.25f, 0.16f), 9).transform.localRotation = Quaternion.Euler(0f, 0f, 25f);
                break;
            case HazardKind.DarkPool:
                hazard.Radius = 1.3f;
                WorldCircle("Dark Pool", t, Vector2.zero, new Vector2(2.5f, 1.8f), new Color(0.015f, 0.04f, 0.12f, 0.78f), 2);
                break;
            case HazardKind.Piranha:
                hazard.Radius = 0.72f; hazard.Damage = 16f; hazard.Life = 12f;
                WorldCircle("Piranha", t, Vector2.zero, new Vector2(1.25f, 0.75f), new Color(0.64f, 0.08f, 0.12f), 16);
                WorldCircle("Eye", t, new Vector2(-0.25f, 0.12f), Vector2.one * 0.13f, Color.white, 17);
                break;
            case HazardKind.FallenTree:
            {
                var w = SalmonHazard.NominalFallenTreeWidth;
                hazard.HalfExtents = new Vector2(w * 0.5f, 0.48f);
                hazard.Damage = 20f;
                WorldRect("Full Width Trunk", t, Vector2.zero, new Vector2(w, 0.88f), new Color(0.30f, 0.14f, 0.055f), 13);
                WorldRect("Wet Bark", t, new Vector2(0f, 0.08f), new Vector2(w - 0.35f, 0.18f), new Color(0.48f, 0.27f, 0.10f), 14);
                float[] knotY = { 0.1f, -0.14f, 0.05f, 0.16f, -0.08f };
                float[] knotRot = { -18f, 12f, 22f, -8f, 15f };
                for (var i = -2; i <= 2; i++)
                {
                    var knot = WorldCircle("Bark Knot", t, new Vector2(i * w / 5f, knotY[i + 2]), new Vector2(0.38f, 0.28f), new Color(0.18f, 0.08f, 0.035f), 15);
                    knot.transform.localRotation = Quaternion.Euler(0f, 0f, knotRot[i + 2]);
                }
                break;
            }
            case HazardKind.HealingReward:
                hazard.Radius = 0.62f; hazard.Life = 18f;
                WorldCircle("Healing Glow", t, Vector2.zero, Vector2.one * 1.65f, new Color(0.25f, 1f, 0.58f, 0.28f), 23);
                WorldCircle("Healing Pearl", t, Vector2.zero, Vector2.one * 0.92f, new Color(0.23f, 0.94f, 0.52f), 24);
                WorldRect("Cross Vertical", t, Vector2.zero, new Vector2(0.18f, 0.58f), Color.white, 25);
                WorldRect("Cross Horizontal", t, Vector2.zero, new Vector2(0.58f, 0.18f), Color.white, 25);
                break;
            case HazardKind.ElectricEel:
                hazard.Radius = 1.15f; hazard.Damage = 22f; hazard.Velocity = Vector2.down * 1.1f;
                for (var i = 0; i < 7; i++)
                    WorldCircle("Eel Segment", t, new Vector2(Mathf.Sin(i * 1.15f) * 0.34f, i * -0.34f + 1f), new Vector2(0.62f, 0.48f),
                        i % 2 == 0 ? new Color(0.92f, 0.93f, 0.16f) : new Color(0.16f, 0.74f, 0.82f), 18);
                WorldCircle("Electric Aura", t, Vector2.zero, new Vector2(2.4f, 3.2f), new Color(0.45f, 0.95f, 1f, 0.16f), 16);
                float[] boltY = { 0.4f, -0.5f, 0.15f, -0.25f };
                for (var i = 0; i < 4; i++)
                {
                    var bolt = WorldRect("Lightning", t, new Vector2((i - 1.5f) * 0.45f, boltY[i]), new Vector2(0.07f, 0.75f), new Color(0.75f, 1f, 1f), 20);
                    bolt.transform.localRotation = Quaternion.Euler(0f, 0f, (i % 2 == 0 ? 1f : -1f) * 28f);
                }
                break;
            case HazardKind.BearSwipe:
                hazard.HalfExtents = new Vector2(2.15f, 1.05f); hazard.Damage = 26f; hazard.Velocity = Vector2.right * 8.2f; // 방향은 스폰 시
                WorldCircle("Bear Paw", t, Vector2.zero, new Vector2(2.8f, 2.2f), new Color(0.34f, 0.18f, 0.08f), 19);
                for (var i = -1; i <= 1; i++)
                {
                    WorldCircle("Toe", t, new Vector2(i * 0.72f, 0.9f), new Vector2(0.72f, 0.82f), new Color(0.42f, 0.23f, 0.11f), 20);
                    var claw = WorldRect("Claw", t, new Vector2(i * 0.72f, 1.38f), new Vector2(0.16f, 0.65f), new Color(0.94f, 0.88f, 0.68f), 21);
                    claw.transform.localRotation = Quaternion.Euler(0f, 0f, i * -8f);
                }
                break;
            case HazardKind.SpinningNet:
                hazard.Radius = 1.35f; hazard.Damage = 19f; hazard.Velocity = Vector2.down * 0.65f;
                WorldCircle("Net Ring", t, Vector2.zero, Vector2.one * 2.65f, new Color(0.74f, 0.69f, 0.52f, 0.32f), 17);
                for (var i = 0; i < 4; i++)
                    WorldRect("Spinning Rope", t, Vector2.zero, new Vector2(2.75f, 0.12f), new Color(0.92f, 0.84f, 0.61f), 19)
                        .transform.localRotation = Quaternion.Euler(0f, 0f, i * 45f);
                WorldCircle("Weighted Core", t, Vector2.zero, Vector2.one * 0.52f, new Color(0.72f, 0.12f, 0.09f), 21);
                break;
        }
        hazard.InitialLife = hazard.Life;

        SavePrefab(root, $"{HazardFolder}/{kind}.prefab");
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

        // ---------- 로비 ----------
        var lobby = Group("Lobby Panel", c);
        Full(Img("Dim", lobby, new Color(0.01f, 0.08f, 0.14f, 0.34f)));
        Text("Title", lobby, new Rect(360, 160, 1200, 110), "SALMON RUN", 78, Color.white, TextAlignmentOptions.Center, true);
        Text("Subtitle", lobby, new Rect(440, 272, 1040, 55), "거슬러 올라가, 고향으로", 28, subtitleColor, TextAlignmentOptions.Center);
        Panel("Panel", lobby, new Rect(610, 400, 700, 360));

        var menu = Group("Menu Group", lobby);
        var startBtn = MakeButton("Start Button", menu, new Rect(735, 470, 450, 82), "게임 시작");
        var settingsBtn = MakeButton("Settings Button", menu, new Rect(735, 575, 450, 70), "설정");
        Text("Hint", menu, new Rect(650, 685, 620, 40), "WASD / 방향키 이동  ·  SPACE 점프", 27, Color.white, TextAlignmentOptions.Center, true);

        var settings = Group("Settings Group", lobby);
        Text("Settings Title", settings, new Rect(705, 438, 510, 50), "사운드 설정", 27, Color.white, TextAlignmentOptions.Center, true);
        Text("Volume Label", settings, new Rect(700, 520, 180, 42), "전체 음량", 21, smallColor, TextAlignmentOptions.Left);
        var slider = MakeSlider("Volume Slider", settings, new Rect(880, 530, 290, 30));
        var volumeText = Text("Volume Value", settings, new Rect(1178, 516, 70, 42), "75%", 21, smallColor, TextAlignmentOptions.Left);
        var backBtn = MakeButton("Back Button", settings, new Rect(800, 630, 320, 65), "돌아가기");
        settings.gameObject.SetActive(false);

        var lobbyBest = Text("Best Score", lobby, new Rect(710, 815, 500, 45), "최고 점수  0", 27, Color.white, TextAlignmentOptions.Center, true);

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
        Bind(so, "lobbyBestScoreText", lobbyBest);
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

    static Button MakeButton(string name, Transform parent, Rect r, string label)
    {
        var bg = Img(name, parent, new Color(0.10f, 0.32f, 0.42f, 0.95f));
        bg.raycastTarget = true;
        Place(bg.rectTransform, r);
        var button = bg.gameObject.AddComponent<Button>();
        var colors = button.colors;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
        button.colors = colors;
        var text = Text("Label", bg.transform, new Rect(0, 0, r.width, r.height), label, 28, Color.white, TextAlignmentOptions.Center, true);
        Stretch(text.rectTransform);
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

    static GameObject WorldCircle(string name, Transform parent, Vector2 position, Vector2 size, Color color, int order)
    {
        var go = WorldRect(name, parent, position, size, color, order);
        go.GetComponent<SpriteRenderer>().sprite = circle;
        return go;
    }

    // ================================================================ 스프라이트 에셋

    static void LoadSprites(bool create)
    {
        if (create)
        {
            EnsureFolder(SpriteFolder);
            if (!File.Exists(WhitePath)) WritePng(WhitePath, MakeWhite());
            if (!File.Exists(CirclePath)) WritePng(CirclePath, MakeCircle());
            if (!File.Exists(FogPath)) WritePng(FogPath, MakeFog());
            ConfigureSprite(WhitePath, 1f);      // 1×1 → 1 유닛
            ConfigureSprite(CirclePath, 64f);    // 64px → 1 유닛
            ConfigureSprite(FogPath, 16f);       // 192×128 → 12×8 유닛 (원본 코드와 동일)
        }
        white = AssetDatabase.LoadAssetAtPath<Sprite>(WhitePath);
        circle = AssetDatabase.LoadAssetAtPath<Sprite>(CirclePath);
        fog = AssetDatabase.LoadAssetAtPath<Sprite>(FogPath);
    }

    static Texture2D MakeWhite()
    {
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        return tex;
    }

    static Texture2D MakeCircle()
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color[size * size];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var dx = (x + 0.5f) / size * 2f - 1f;
            var dy = (y + 0.5f) / size * 2f - 1f;
            var d = Mathf.Sqrt(dx * dx + dy * dy);
            px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01((1f - d) * 8f));
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    static Texture2D MakeFog()
    {
        const int width = 192, height = 128;
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var px = new Color[width * height];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var u = x / (float)width;
            var v = y / (float)height;
            var broad = Mathf.PerlinNoise(u * 3.2f + 4.1f, v * 3.2f + 7.6f);
            var detail = Mathf.PerlinNoise(u * 8.5f + 12.7f, v * 8.5f + 2.3f);
            var wisps = Mathf.SmoothStep(0.2f, 0.9f, broad * 0.72f + detail * 0.28f);
            var edgeFade = Mathf.SmoothStep(0f, 0.18f, v) * Mathf.SmoothStep(0f, 0.18f, 1f - v);
            var alpha = Mathf.Lerp(0.58f, 1f, wisps) * edgeFade;
            px[y * width + x] = new Color(0.83f, 0.88f, 0.91f, alpha);
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    static void WritePng(string path, Texture2D tex)
    {
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
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

    // ================================================================ 공용

    static void SavePrefab(GameObject go, string path)
    {
        EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
        PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
    }

    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
