using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SalmonRun
{
    public sealed class SalmonGame : MonoBehaviour
    {
        private enum GameState { Lobby, Playing, GameOver }

        private sealed class JuiceParticle
        {
            public Transform Transform;
            public SpriteRenderer Renderer;
            public Vector2 Velocity;
            public float Life;
            public float MaxLife;
        }

        private readonly List<SalmonHazard> hazards = new();
        private readonly List<Transform> flowMarks = new();
        private readonly List<SpriteRenderer> bankRenderers = new();
        private readonly List<SpriteRenderer> laneRenderers = new();
        private readonly List<JuiceParticle> juiceParticles = new();

        private GameState state = GameState.Lobby;
        private Transform world;
        private Transform hazardRoot;
        private Transform player;
        private SpriteRenderer playerBody;
        private SpriteRenderer waterRenderer;
        private Camera gameCamera;

        private int stage = 1;
        private int nightLoop;
        private int score;
        private int bestScore;
        private float scoreRemainder;
        private float health = 100f;
        private float stageTime;
        private float totalTime;
        private float spawnTimer;
        private float damageCooldown;
        private float jumpTimer;
        private float waveTimer;
        private float stageBannerTimer;
        private float hurtFlash;
        private float forwardPulse;
        private bool settingsOpen;
        private bool waveWarningShown;
        private float masterVolume = 0.75f;
        private string eventText = "";
        private float eventTimer;
        private float rewardTimer;
        private float treeTimer;
        private float trailTimer;
        private float shakeStrength;
        private float healFlash;
        private float fogIntensity;
        private float fogWarningTimer;
        private float fogTimer;
        private Vector2 playerVelocity;
        private Vector3 cameraBasePosition;
        private float currentRiverHalfWidth = 7.1f;
        private float targetRiverHalfWidth = 7.1f;
        private float terrainTransitionTimer;
        private Color currentWaterColor;
        private Color currentBankColor;
        private Color currentCameraColor;
        private Color targetWaterColor;
        private Color targetBankColor;
        private Color targetCameraColor;
        private string transitionText = "";

        private GUIStyle titleStyle;
        private GUIStyle subtitleStyle;
        private GUIStyle hudStyle;
        private GUIStyle smallStyle;
        private GUIStyle buttonStyle;
        private GUIStyle centeredStyle;
        private Texture2D whiteTexture;

        private const float StageDuration = 34f;
        private const float WorldTop = 10.5f;
        private const float WorldBottom = -10.5f;

        private float ScrollSpeed => 4.65f + (stage - 1) * 0.58f + nightLoop * 0.28f;
        private int EndlessDifficulty => stage < 3 ? 0 : nightLoop + 1;
        private float HalfWidth => gameCamera.orthographicSize * gameCamera.aspect;
        private float RiverHalfWidth => Mathf.Min(HalfWidth - 1.1f, currentRiverHalfWidth);

        private void Awake()
        {
            Application.targetFrameRate = 60;
            bestScore = PlayerPrefs.GetInt("SalmonRunBest", 0);
            SetupCamera();
            BuildWorld();
            SetTheme(1);
        }

        private void SetupCamera()
        {
            gameCamera = Camera.main;
            if (gameCamera == null)
            {
                gameCamera = new GameObject("Main Camera").AddComponent<Camera>();
                gameCamera.tag = "MainCamera";
                gameCamera.gameObject.AddComponent<AudioListener>();
            }
            gameCamera.orthographic = true;
            gameCamera.orthographicSize = 9f;
            gameCamera.transform.position = new Vector3(0f, 0f, -10f);
            cameraBasePosition = gameCamera.transform.position;
            gameCamera.backgroundColor = new Color(0.03f, 0.12f, 0.2f);
        }

        private void BuildWorld()
        {
            world = new GameObject("World").transform;
            world.SetParent(transform, false);
            hazardRoot = new GameObject("Random Hazards").transform;
            hazardRoot.SetParent(world, false);

            waterRenderer = SalmonVisuals.Rect("Water", world, Vector2.zero, new Vector2(36f, 22f),
                new Color(0.08f, 0.56f, 0.75f), -20).GetComponent<SpriteRenderer>();

            for (var i = 0; i < 2; i++)
            {
                var x = i == 0 ? -13.1f : 13.1f;
                var bank = SalmonVisuals.Rect(i == 0 ? "Left Bank" : "Right Bank", world,
                    new Vector2(x, 0f), new Vector2(12f, 22f), new Color(0.25f, 0.57f, 0.35f), -10);
                bankRenderers.Add(bank.GetComponent<SpriteRenderer>());
            }

            for (var lane = -1; lane <= 1; lane++)
            {
                var marker = SalmonVisuals.Rect("Route Guide", world, new Vector2(lane * 3.5f, 0f),
                    new Vector2(0.05f, 22f), new Color(1f, 1f, 1f, 0.09f), -8);
                laneRenderers.Add(marker.GetComponent<SpriteRenderer>());
            }

            for (var i = 0; i < 24; i++)
            {
                var mark = SalmonVisuals.Rect("Flow Spark", world,
                    new Vector2(Random.Range(-6.5f, 6.5f), Random.Range(WorldBottom, WorldTop)),
                    new Vector2(Random.Range(0.025f, 0.08f), Random.Range(0.25f, 0.75f)),
                    new Color(0.75f, 0.95f, 1f, Random.Range(0.16f, 0.42f)), -5);
                flowMarks.Add(mark.transform);
            }

            player = SalmonVisuals.MakeSalmon(world).transform;
            player.position = new Vector3(0f, -5.8f, 0f);
            playerBody = player.Find("Body").GetComponent<SpriteRenderer>();
        }

        private void Update()
        {
            var dt = Mathf.Min(Time.deltaTime, 0.05f);
            AnimateWater(dt);
            UpdateEnvironmentTransition(dt);
            UpdateJuice(dt);
            if (state != GameState.Playing) return;

            ReadMovement(dt);
            UpdateRun(dt);
            UpdateHazards(dt);
            CheckHazards(dt);
            UpdateStage(dt);
        }

        private void AnimateWater(float dt)
        {
            var speed = state == GameState.Playing ? ScrollSpeed : 1.1f;
            foreach (var mark in flowMarks)
            {
                mark.position += Vector3.down * speed * dt;
                if (mark.position.y < WorldBottom)
                {
                    mark.position = new Vector3(Random.Range(-RiverHalfWidth + 0.4f, RiverHalfWidth - 0.4f),
                        WorldTop, 0f);
                }
            }

            forwardPulse += dt * 4f;
            if (player != null && state != GameState.Playing)
                player.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(forwardPulse) * 3f);
        }

        private void ReadMovement(float dt)
        {
            var keyboard = Keyboard.current;
            var input = Vector2.zero;
            if (keyboard != null)
            {
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) input.x -= 1f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) input.x += 1f;
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) input.y += 1f;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) input.y -= 1f;

                if (keyboard.spaceKey.wasPressedThisFrame && jumpTimer <= 0f)
                {
                    jumpTimer = 0.68f;
                    shakeStrength = Mathf.Max(shakeStrength, 0.12f);
                    Burst(player.position + Vector3.down * 0.55f, new Color(0.72f, 0.94f, 1f), 9, 2.4f);
                    ShowEvent("JUMP!", 0.55f);
                }
            }

            var slowed = IsInsideSlowZone();
            var speed = slowed ? 3.2f : 6.4f;
            var desiredVelocity = input.normalized * speed;
            var acceleration = input.sqrMagnitude > 0f ? 30f : 21f;
            playerVelocity = Vector2.MoveTowards(playerVelocity, desiredVelocity, acceleration * dt);
            var next = (Vector2)player.position + playerVelocity * dt;
            next.x = Mathf.Clamp(next.x, -RiverHalfWidth + 0.7f, RiverHalfWidth - 0.7f);
            next.y = Mathf.Clamp(next.y, -7.2f, 5.5f);
            if (!Mathf.Approximately(next.x, player.position.x + playerVelocity.x * dt)) playerVelocity.x *= -0.2f;
            if (!Mathf.Approximately(next.y, player.position.y + playerVelocity.y * dt)) playerVelocity.y *= -0.2f;
            player.position = next;

            var tilt = -playerVelocity.x * 3.1f;
            player.rotation = Quaternion.Lerp(player.rotation, Quaternion.Euler(0f, 0f, tilt), dt * 13f);
        }

        private void UpdateRun(float dt)
        {
            totalTime += dt;
            stageTime += dt;
            damageCooldown -= dt;
            jumpTimer -= dt;
            stageBannerTimer -= dt;
            eventTimer -= dt;
            hurtFlash -= dt;
            healFlash -= dt;
            spawnTimer -= dt;
            rewardTimer -= dt;
            treeTimer -= dt;
            trailTimer -= dt;
            fogWarningTimer -= dt;
            fogTimer -= dt;

            var fogIsActive = hazards.Exists(hazard => hazard != null && hazard.Kind == HazardKind.Fog && hazard.Life > 0f);
            fogIntensity = Mathf.MoveTowards(fogIntensity, fogIsActive ? 1f : 0f,
                dt * (fogIsActive ? 1.8f : 0.62f));

            scoreRemainder += (18f + ScrollSpeed * 2f) * dt;
            if (scoreRemainder >= 1f)
            {
                var earned = Mathf.FloorToInt(scoreRemainder);
                score += earned;
                scoreRemainder -= earned;
            }
            var movementStretch = Mathf.Clamp01(playerVelocity.magnitude / 6.4f);
            var jumpArc = jumpTimer > 0f ? Mathf.Sin((0.68f - jumpTimer) / 0.68f * Mathf.PI) : 0f;
            var desiredScale = new Vector3(1f - movementStretch * 0.08f + jumpArc * 0.22f,
                1f + movementStretch * 0.13f + jumpArc * 0.38f, 1f);
            player.localScale = Vector3.Lerp(player.localScale, desiredScale, dt * 14f);
            playerBody.color = hurtFlash > 0f && Mathf.FloorToInt(hurtFlash * 18f) % 2 == 0
                ? Color.white
                : new Color(1f, 0.36f, 0.30f);

            if (stage == 1)
            {
                waveTimer -= dt;
                if (waveTimer <= 1.15f && !waveWarningShown)
                {
                    waveWarningShown = true;
                    ShowEvent("큰 파도 접근!  점프하면 밀림 감소", 1.1f);
                    shakeStrength = Mathf.Max(shakeStrength, 0.08f);
                }
                if (waveTimer <= 0f)
                {
                    waveTimer = Random.Range(6.5f, 9f);
                    waveWarningShown = false;
                    var push = jumpTimer > 0f ? 0.48f : 1.65f;
                    player.position += Vector3.down * push;
                    playerVelocity += Vector2.down * (jumpTimer > 0f ? 0.9f : 3.5f);
                    shakeStrength = Mathf.Max(shakeStrength, 0.32f);
                    Burst(player.position + Vector3.up, new Color(0.75f, 0.95f, 1f), 15, 4f);
                    ShowEvent(jumpTimer > 0f ? "파도를 뛰어넘었습니다!  +30" : "파도가 밀어냅니다!", 1.5f);
                    if (jumpTimer > 0f) score += 30;
                }
            }

            if (trailTimer <= 0f && playerVelocity.sqrMagnitude > 0.4f)
            {
                trailTimer = 0.075f;
                SpawnParticle(player.position + Vector3.down * 0.8f + Vector3.right * Random.Range(-0.3f, 0.3f),
                    new Color(0.75f, 0.95f, 1f, 0.7f), -playerVelocity * 0.12f + Vector2.down * 0.7f,
                    Random.Range(0.12f, 0.24f), 0.55f, 18);
            }

            if (rewardTimer <= 0f)
            {
                rewardTimer = Random.Range(8f, 12f);
                CreateHazard(HazardKind.HealingReward,
                    new Vector2(Random.Range(-RiverHalfWidth + 1f, RiverHalfWidth - 1f), WorldTop + 1f));
            }

            if (stage >= 2 && treeTimer <= 0f)
            {
                treeTimer = Random.Range(11f, 15f);
                CreateHazard(HazardKind.FallenTree, new Vector2(0f, WorldTop + 1.5f));
                ShowEvent("쓰러진 나무! 점프로 넘으세요", 1.5f);
            }

            if (stage == 3 && fogTimer <= 0f)
            {
                fogTimer = Mathf.Max(8f, Random.Range(13f, 18f) - nightLoop * 0.45f);
                CreateHazard(HazardKind.Fog, Vector2.zero);
                fogWarningTimer = 2.8f;
                ShowEvent("안개 구간입니다!", 2.8f);
            }

            if (spawnTimer <= 0f)
            {
                SpawnHazard();
                var baseInterval = stage == 1 ? 1.12f : stage == 2 ? 0.90f : 0.82f;
                spawnTimer = Mathf.Max(0.42f, baseInterval - nightLoop * 0.06f) * Random.Range(0.75f, 1.25f);
            }
        }

        private void UpdateHazards(float dt)
        {
            for (var i = hazards.Count - 1; i >= 0; i--)
            {
                var hazard = hazards[i];
                if (hazard == null)
                {
                    hazards.RemoveAt(i);
                    continue;
                }

                hazard.Tick(dt, ScrollSpeed, player.position);
                var passedPlayer = hazard.transform.position.y < player.position.y - 0.9f;
                var closePass = hazard.Kind == HazardKind.FallenTree ||
                                Mathf.Abs(hazard.transform.position.x - player.position.x) < hazard.Radius + 1.1f;
                if (!hazard.NearMissAwarded && !hazard.Hit && hazard.Damage > 0f && passedPlayer && closePass)
                {
                    hazard.NearMissAwarded = true;
                    score += 25;
                    Burst(player.position, new Color(1f, 0.83f, 0.25f), 6, 1.8f);
                    ShowEvent("아슬아슬!  +25", 0.75f);
                }
                if (hazard.Life <= 0f || hazard.transform.position.y < WorldBottom - 2f ||
                    Mathf.Abs(hazard.transform.position.x) > HalfWidth + 4f)
                {
                    Destroy(hazard.gameObject);
                    hazards.RemoveAt(i);
                }
            }
        }

        private void CheckHazards(float dt)
        {
            var playerPosition = (Vector2)player.position;
            foreach (var hazard in hazards)
            {
                var offset = playerPosition - (Vector2)hazard.transform.position;
                var distance = offset.magnitude;

                if (hazard.Kind == HazardKind.Whirlpool && distance < 3.2f)
                    player.position -= (Vector3)(offset.normalized * (3.2f - distance) * 1.15f * dt);
                if (hazard.Kind == HazardKind.Rapid && distance < hazard.Radius)
                    player.position += (Vector3)(hazard.Velocity.normalized * 3.7f * dt);

                var touching = hazard.HalfExtents.sqrMagnitude > 0f
                    ? Mathf.Abs(offset.x) < hazard.HalfExtents.x + 0.48f &&
                      Mathf.Abs(offset.y) < hazard.HalfExtents.y + 0.55f
                    : distance <= hazard.Radius + 0.52f;
                if (!touching || hazard.Hit) continue;
                if (jumpTimer > 0f && CanJumpOver(hazard.Kind)) continue;

                switch (hazard.Kind)
                {
                    case HazardKind.HealingReward:
                        var recovered = Mathf.Min(25f, 100f - health);
                        health += recovered;
                        score += 100;
                        healFlash = 0.65f;
                        shakeStrength = Mathf.Max(shakeStrength, 0.1f);
                        hazard.Hit = true;
                        hazard.Life = 0f;
                        Burst(hazard.transform.position, new Color(0.3f, 1f, 0.58f), 18, 3.5f);
                        ShowEvent(recovered > 0f ? "체력 회복!  +" + Mathf.RoundToInt(recovered) : "완벽한 체력!  +100", 1.25f);
                        break;
                    case HazardKind.Seaweed:
                    case HazardKind.DarkPool:
                    case HazardKind.Fog:
                    case HazardKind.Rapid:
                    case HazardKind.Whirlpool:
                        break;
                    case HazardKind.Branch:
                    case HazardKind.Boulder:
                    case HazardKind.FishSchool:
                    case HazardKind.FallenTree:
                        player.position += (Vector3)(offset.normalized * 1.25f);
                        DealDamage(hazard.Damage, "충돌!");
                        hazard.Hit = true;
                        break;
                    default:
                        DealDamage(hazard.Damage, "체력 감소!");
                        hazard.Hit = true;
                        break;
                }
            }

            var p = player.position;
            p.x = Mathf.Clamp(p.x, -RiverHalfWidth + 0.7f, RiverHalfWidth - 0.7f);
            p.y = Mathf.Clamp(p.y, -7.2f, 5.5f);
            player.position = p;
        }

        private bool IsInsideSlowZone()
        {
            foreach (var hazard in hazards)
            {
                if (hazard.Kind != HazardKind.Seaweed && hazard.Kind != HazardKind.DarkPool) continue;
                if (Vector2.Distance(player.position, hazard.transform.position) < hazard.Radius + 0.45f)
                    return true;
            }
            return false;
        }

        private static bool CanJumpOver(HazardKind kind)
        {
            return kind is HazardKind.Seaweed or HazardKind.Branch or HazardKind.Log or HazardKind.Stone
                or HazardKind.DarkPool or HazardKind.Debris or HazardKind.FallenTree;
        }

        private void DealDamage(float amount, string message)
        {
            if (amount <= 0f || damageCooldown > 0f) return;
            health = Mathf.Max(0f, health - amount);
            damageCooldown = 0.8f;
            hurtFlash = 0.55f;
            shakeStrength = Mathf.Max(shakeStrength, 0.46f);
            playerVelocity += Vector2.down * 2.2f;
            Burst(player.position, new Color(1f, 0.22f, 0.16f), 14, 3.8f);
            ShowEvent(message + "  -" + Mathf.RoundToInt(amount), 1f);
            if (health <= 0f) EndGame();
        }

        private void UpdateStage(float dt)
        {
            if (stageTime < StageDuration) return;
            stageTime = 0f;
            score += 500;
            var previousStage = stage;
            if (stage < 3) stage++;
            else nightLoop++;
            BeginEnvironmentTransition(stage);
            transitionText = previousStage == 1
                ? "바다가 좁아지며 강 하류로 이어집니다"
                : previousStage == 2
                    ? "노을이 저물고 밤의 강으로 들어갑니다"
                    : "물살이 더욱 거세집니다 · 난이도 " + EndlessDifficulty;
            stageBannerTimer = 5.8f;
            if (stage == 3) fogTimer = Mathf.Min(fogTimer, 3.2f);
            Burst(new Vector2(0f, WorldTop - 1f), new Color(0.76f, 0.95f, 1f), 26, 4.8f);
            ShowEvent("구간 통과 +500", 2f);
        }

        private void SpawnHazard()
        {
            HazardKind kind;
            if (stage == 1)
            {
                var pool = new[] { HazardKind.Seaweed, HazardKind.Branch, HazardKind.Leaf, HazardKind.Jellyfish };
                kind = pool[Random.Range(0, pool.Length)];
            }
            else if (stage == 2)
            {
                var pool = new[] { HazardKind.Boulder, HazardKind.Rapid, HazardKind.Log, HazardKind.Whirlpool, HazardKind.FishSchool };
                kind = pool[Random.Range(0, pool.Length)];
            }
            else
            {
                var pool = new[] { HazardKind.Stone, HazardKind.Bird, HazardKind.Debris,
                    HazardKind.DarkPool, HazardKind.Piranha, HazardKind.ElectricEel,
                    HazardKind.BearSwipe, HazardKind.SpinningNet };
                kind = pool[Random.Range(0, pool.Length)];
            }

            var x = Random.Range(-RiverHalfWidth + 0.7f, RiverHalfWidth - 0.7f);
            var y = kind is HazardKind.Debris or HazardKind.Piranha or HazardKind.BearSwipe
                ? Random.Range(-1f, 5f) : WorldTop + 1f;
            if (kind is HazardKind.Debris or HazardKind.BearSwipe)
                x = Random.value < 0.5f ? -RiverHalfWidth - 1f : RiverHalfWidth + 1f;
            if (kind == HazardKind.Fog) { x = 0f; y = 0f; }
            CreateHazard(kind, new Vector2(x, y));
            var doublePattern = stage == 3 && nightLoop > 0 &&
                                kind is HazardKind.Stone or HazardKind.DarkPool or HazardKind.ElectricEel &&
                                Random.value < Mathf.Min(0.48f, nightLoop * 0.12f);
            if (doublePattern)
                CreateHazard(kind, new Vector2(-x, y + Random.Range(1.7f, 2.8f)));
        }

        private void CreateHazard(HazardKind kind, Vector2 position)
        {
            var root = new GameObject(kind.ToString());
            root.transform.SetParent(hazardRoot, false);
            root.transform.position = position;
            var hazard = root.AddComponent<SalmonHazard>();
            hazard.Kind = kind;
            hazard.Phase = Random.Range(0f, 6.28f);

            switch (kind)
            {
                case HazardKind.Seaweed:
                    hazard.Radius = 0.8f;
                    for (var i = -1; i <= 1; i++)
                    {
                        var weed = SalmonVisuals.Rect("Seaweed", root.transform, new Vector2(i * 0.28f, 0f),
                            new Vector2(0.18f, 1.45f + Random.Range(-0.2f, 0.25f)), new Color(0.08f, 0.48f, 0.28f), 5);
                        weed.transform.localRotation = Quaternion.Euler(0f, 0f, i * 12f);
                    }
                    break;
                case HazardKind.Branch:
                    hazard.Radius = 1.25f; hazard.Damage = 7f;
                    var branch = SalmonVisuals.Rect("Branch", root.transform, Vector2.zero, new Vector2(2.5f, 0.28f), new Color(0.35f, 0.18f, 0.07f), 7);
                    branch.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(-20f, 20f));
                    break;
                case HazardKind.Leaf:
                    hazard.Radius = 0f;
                    SalmonVisuals.Circle("Leaf", root.transform, Vector2.zero, new Vector2(1.4f, 0.8f), new Color(0.33f, 0.67f, 0.25f, 0.78f), 28);
                    break;
                case HazardKind.Jellyfish:
                    hazard.Radius = 0.62f; hazard.Damage = 13f;
                    SalmonVisuals.Circle("Jellyfish", root.transform, Vector2.zero, new Vector2(1.1f, 0.9f), new Color(0.82f, 0.62f, 1f, 0.88f), 8);
                    for (var i = -1; i <= 1; i++) SalmonVisuals.Rect("Tentacle", root.transform, new Vector2(i * 0.28f, -0.6f), new Vector2(0.08f, 0.75f), new Color(0.75f, 0.45f, 0.92f), 7);
                    break;
                case HazardKind.Boulder:
                    hazard.Radius = 1.75f; hazard.Damage = 15f;
                    SalmonVisuals.Circle("Boulder", root.transform, Vector2.zero, new Vector2(3.1f, 2.5f), new Color(0.25f, 0.28f, 0.30f), 8);
                    SalmonVisuals.Circle("Highlight", root.transform, new Vector2(-0.55f, 0.5f), new Vector2(0.65f, 0.4f), new Color(0.42f, 0.44f, 0.43f), 9);
                    break;
                case HazardKind.Rapid:
                    hazard.Radius = 1.6f; hazard.Velocity = (Random.value < 0.5f ? Vector2.left : Vector2.right) * 0.35f;
                    for (var i = -1; i <= 1; i++) SalmonVisuals.Rect("Rapid", root.transform, new Vector2(0f, i * 0.42f), new Vector2(2.7f, 0.12f), new Color(0.82f, 0.96f, 1f, 0.75f), 3);
                    break;
                case HazardKind.Log:
                    hazard.Radius = 1.2f; hazard.Damage = 18f; hazard.Velocity = Vector2.down * 2.8f;
                    SalmonVisuals.Rect("Log", root.transform, Vector2.zero, new Vector2(2.4f, 0.62f), new Color(0.42f, 0.23f, 0.08f), 9);
                    SalmonVisuals.Circle("Cut", root.transform, new Vector2(1.12f, 0f), new Vector2(0.25f, 0.58f), new Color(0.68f, 0.45f, 0.2f), 10);
                    break;
                case HazardKind.Whirlpool:
                    hazard.Radius = 1.15f;
                    for (var i = 0; i < 4; i++) SalmonVisuals.Circle("Whirl", root.transform, new Vector2(i * 0.2f - 0.3f, 0f), Vector2.one * (2.5f - i * 0.5f), new Color(0.05f, 0.31f, 0.48f, 0.35f), 4 + i);
                    break;
                case HazardKind.FishSchool:
                    hazard.Radius = 1.1f; hazard.Damage = 9f; hazard.Velocity = Vector2.right * Random.Range(-1.2f, 1.2f);
                    for (var i = 0; i < 5; i++) SalmonVisuals.Circle("Small Fish", root.transform, new Vector2((i % 3) * 0.55f - 0.55f, (i / 3) * 0.48f - 0.25f), new Vector2(0.58f, 0.28f), new Color(0.85f, 0.82f, 0.38f), 8);
                    break;
                case HazardKind.Stone:
                    hazard.Radius = 0.5f; hazard.Damage = 11f;
                    SalmonVisuals.Circle("Stone", root.transform, Vector2.zero, new Vector2(0.82f, 0.75f), new Color(0.31f, 0.35f, 0.39f), 8);
                    break;
                case HazardKind.Bird:
                    hazard.Radius = 0.75f; hazard.Damage = 18f; hazard.Velocity = Vector2.down * 3.4f;
                    SalmonVisuals.Circle("Bird", root.transform, Vector2.zero, new Vector2(1.0f, 0.65f), new Color(0.08f, 0.09f, 0.13f), 18);
                    SalmonVisuals.Rect("Left Wing", root.transform, new Vector2(-0.55f, 0f), new Vector2(0.85f, 0.2f), new Color(0.12f, 0.13f, 0.18f), 17).transform.localRotation = Quaternion.Euler(0f, 0f, 22f);
                    SalmonVisuals.Rect("Right Wing", root.transform, new Vector2(0.55f, 0f), new Vector2(0.85f, 0.2f), new Color(0.12f, 0.13f, 0.18f), 17).transform.localRotation = Quaternion.Euler(0f, 0f, -22f);
                    break;
                case HazardKind.Fog:
                    hazard.Radius = 0f; hazard.Life = 5.5f; hazard.Velocity = Vector2.up * ScrollSpeed;
                    var fog = new GameObject("Dense Natural Fog");
                    fog.transform.SetParent(root.transform, false);
                    fog.transform.localScale = new Vector3((HalfWidth * 2f + 3f) / 12f, 2.8f, 1f);
                    hazard.FogRenderer = fog.AddComponent<SpriteRenderer>();
                    hazard.FogRenderer.sprite = SalmonVisuals.FogSprite;
                    hazard.FogRenderer.color = new Color(0.84f, 0.89f, 0.92f, 0f);
                    hazard.FogRenderer.sortingOrder = 45;
                    break;
                case HazardKind.Debris:
                    hazard.Radius = 0.75f; hazard.Damage = 14f; hazard.Velocity = (position.x < 0f ? Vector2.right : Vector2.left) * 7f;
                    SalmonVisuals.Rect("Debris", root.transform, Vector2.zero, new Vector2(1.5f, 0.55f), new Color(0.39f, 0.25f, 0.16f), 9).transform.localRotation = Quaternion.Euler(0f, 0f, 25f);
                    break;
                case HazardKind.DarkPool:
                    hazard.Radius = 1.3f;
                    SalmonVisuals.Circle("Dark Pool", root.transform, Vector2.zero, new Vector2(2.5f, 1.8f), new Color(0.015f, 0.04f, 0.12f, 0.78f), 2);
                    break;
                case HazardKind.Piranha:
                    hazard.Radius = 0.72f; hazard.Damage = 16f; hazard.Life = 12f;
                    SalmonVisuals.Circle("Piranha", root.transform, Vector2.zero, new Vector2(1.25f, 0.75f), new Color(0.64f, 0.08f, 0.12f), 16);
                    SalmonVisuals.Circle("Eye", root.transform, new Vector2(-0.25f, 0.12f), Vector2.one * 0.13f, Color.white, 17);
                    break;
                case HazardKind.FallenTree:
                    var treeWidth = RiverHalfWidth * 2f - 0.25f;
                    hazard.HalfExtents = new Vector2(treeWidth * 0.5f, 0.48f);
                    hazard.Damage = 20f;
                    SalmonVisuals.Rect("Full Width Trunk", root.transform, Vector2.zero,
                        new Vector2(treeWidth, 0.88f), new Color(0.30f, 0.14f, 0.055f), 13);
                    SalmonVisuals.Rect("Wet Bark", root.transform, new Vector2(0f, 0.08f),
                        new Vector2(treeWidth - 0.35f, 0.18f), new Color(0.48f, 0.27f, 0.10f), 14);
                    for (var i = -2; i <= 2; i++)
                    {
                        var knot = SalmonVisuals.Circle("Bark Knot", root.transform,
                            new Vector2(i * treeWidth / 5f, Random.Range(-0.17f, 0.17f)),
                            new Vector2(0.38f, 0.28f), new Color(0.18f, 0.08f, 0.035f), 15);
                        knot.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(-25f, 25f));
                    }
                    break;
                case HazardKind.HealingReward:
                    hazard.Radius = 0.62f;
                    hazard.Life = 18f;
                    SalmonVisuals.Circle("Healing Glow", root.transform, Vector2.zero, Vector2.one * 1.65f,
                        new Color(0.25f, 1f, 0.58f, 0.28f), 23);
                    SalmonVisuals.Circle("Healing Pearl", root.transform, Vector2.zero, Vector2.one * 0.92f,
                        new Color(0.23f, 0.94f, 0.52f), 24);
                    SalmonVisuals.Rect("Cross Vertical", root.transform, Vector2.zero, new Vector2(0.18f, 0.58f), Color.white, 25);
                    SalmonVisuals.Rect("Cross Horizontal", root.transform, Vector2.zero, new Vector2(0.58f, 0.18f), Color.white, 25);
                    break;
                case HazardKind.ElectricEel:
                    hazard.Radius = 1.15f; hazard.Damage = 22f; hazard.Velocity = Vector2.down * 1.1f;
                    for (var i = 0; i < 7; i++)
                    {
                        var segmentPosition = new Vector2(Mathf.Sin(i * 1.15f) * 0.34f, i * -0.34f + 1f);
                        SalmonVisuals.Circle("Eel Segment", root.transform, segmentPosition,
                            new Vector2(0.62f, 0.48f), i % 2 == 0 ? new Color(0.92f, 0.93f, 0.16f) : new Color(0.16f, 0.74f, 0.82f), 18);
                    }
                    SalmonVisuals.Circle("Electric Aura", root.transform, Vector2.zero, new Vector2(2.4f, 3.2f),
                        new Color(0.45f, 0.95f, 1f, 0.16f), 16);
                    for (var i = 0; i < 4; i++)
                    {
                        var bolt = SalmonVisuals.Rect("Lightning", root.transform,
                            new Vector2((i - 1.5f) * 0.45f, Random.Range(-0.7f, 0.7f)),
                            new Vector2(0.07f, 0.75f), new Color(0.75f, 1f, 1f), 20);
                        bolt.transform.localRotation = Quaternion.Euler(0f, 0f, (i % 2 == 0 ? 1f : -1f) * 28f);
                    }
                    break;
                case HazardKind.BearSwipe:
                    hazard.HalfExtents = new Vector2(2.15f, 1.05f); hazard.Damage = 26f;
                    hazard.Velocity = (position.x < 0f ? Vector2.right : Vector2.left) * 8.2f;
                    SalmonVisuals.Circle("Bear Paw", root.transform, Vector2.zero, new Vector2(2.8f, 2.2f),
                        new Color(0.34f, 0.18f, 0.08f), 19);
                    for (var i = -1; i <= 1; i++)
                    {
                        SalmonVisuals.Circle("Toe", root.transform, new Vector2(i * 0.72f, 0.9f),
                            new Vector2(0.72f, 0.82f), new Color(0.42f, 0.23f, 0.11f), 20);
                        var claw = SalmonVisuals.Rect("Claw", root.transform, new Vector2(i * 0.72f, 1.38f),
                            new Vector2(0.16f, 0.65f), new Color(0.94f, 0.88f, 0.68f), 21);
                        claw.transform.localRotation = Quaternion.Euler(0f, 0f, i * -8f);
                    }
                    ShowEvent("강둑에서 곰이 공격합니다!", 1.1f);
                    break;
                case HazardKind.SpinningNet:
                    hazard.Radius = 1.35f; hazard.Damage = 19f; hazard.Velocity = Vector2.down * 0.65f;
                    SalmonVisuals.Circle("Net Ring", root.transform, Vector2.zero, Vector2.one * 2.65f,
                        new Color(0.74f, 0.69f, 0.52f, 0.32f), 17);
                    for (var i = 0; i < 4; i++)
                    {
                        var rope = SalmonVisuals.Rect("Spinning Rope", root.transform, Vector2.zero,
                            new Vector2(2.75f, 0.12f), new Color(0.92f, 0.84f, 0.61f), 19);
                        rope.transform.localRotation = Quaternion.Euler(0f, 0f, i * 45f);
                    }
                    SalmonVisuals.Circle("Weighted Core", root.transform, Vector2.zero, Vector2.one * 0.52f,
                        new Color(0.72f, 0.12f, 0.09f), 21);
                    break;
            }
            if (stage == 3 && kind is not HazardKind.Fog and not HazardKind.HealingReward)
            {
                hazard.Damage *= 1f + nightLoop * 0.1f;
                hazard.Velocity *= 1f + nightLoop * 0.055f;
            }
            hazard.InitialLife = hazard.Life;
            hazards.Add(hazard);
        }

        private void StartGame()
        {
            state = GameState.Playing;
            settingsOpen = false;
            stage = 1;
            nightLoop = 0;
            score = 0;
            scoreRemainder = 0f;
            health = 100f;
            stageTime = 0f;
            totalTime = 0f;
            spawnTimer = 1.2f;
            waveTimer = 6f;
            waveWarningShown = false;
            rewardTimer = 4.5f;
            treeTimer = 8f;
            fogTimer = 999f;
            fogIntensity = 0f;
            fogWarningTimer = 0f;
            transitionText = "아침 바다의 물살을 타고 출발합니다";
            trailTimer = 0f;
            jumpTimer = 0f;
            damageCooldown = 0f;
            playerVelocity = Vector2.zero;
            shakeStrength = 0f;
            healFlash = 0f;
            stageBannerTimer = 3.2f;
            player.position = new Vector3(0f, -5.8f, 0f);
            player.localScale = Vector3.one;
            ClearJuice();
            ClearHazards();
            SetTheme(1);
        }

        private void EndGame()
        {
            state = GameState.GameOver;
            bestScore = Mathf.Max(bestScore, score);
            PlayerPrefs.SetInt("SalmonRunBest", bestScore);
            PlayerPrefs.Save();
        }

        private void ReturnToLobby()
        {
            state = GameState.Lobby;
            settingsOpen = false;
            ClearHazards();
            stage = 1;
            nightLoop = 0;
            SetTheme(1);
            player.position = new Vector3(0f, -5.8f, 0f);
            player.localScale = Vector3.one;
            playerVelocity = Vector2.zero;
            ClearJuice();
        }

        private void ClearHazards()
        {
            foreach (var hazard in hazards) if (hazard != null) Destroy(hazard.gameObject);
            hazards.Clear();
        }

        private void SetTheme(int targetStage)
        {
            GetTheme(targetStage, out currentWaterColor, out currentBankColor, out currentCameraColor,
                out currentRiverHalfWidth);
            targetWaterColor = currentWaterColor;
            targetBankColor = currentBankColor;
            targetCameraColor = currentCameraColor;
            targetRiverHalfWidth = currentRiverHalfWidth;
            terrainTransitionTimer = 0f;
            ApplyEnvironment();
        }

        private void BeginEnvironmentTransition(int targetStage)
        {
            GetTheme(targetStage, out targetWaterColor, out targetBankColor, out targetCameraColor,
                out targetRiverHalfWidth);
            terrainTransitionTimer = 6.5f;
        }

        private void GetTheme(int targetStage, out Color water, out Color bank, out Color camera, out float riverWidth)
        {
            if (targetStage == 1)
            {
                water = new Color(0.06f, 0.55f, 0.76f);
                bank = new Color(0.23f, 0.60f, 0.36f);
                camera = new Color(0.50f, 0.82f, 0.91f);
                riverWidth = 7.1f;
            }
            else if (targetStage == 2)
            {
                water = new Color(0.13f, 0.39f, 0.52f);
                bank = new Color(0.50f, 0.31f, 0.18f);
                camera = new Color(0.94f, 0.43f, 0.25f);
                riverWidth = 6.45f;
            }
            else
            {
                var darkness = Mathf.Clamp01(nightLoop * 0.025f);
                water = Color.Lerp(new Color(0.035f, 0.18f, 0.31f), new Color(0.012f, 0.07f, 0.16f), darkness);
                bank = Color.Lerp(new Color(0.09f, 0.14f, 0.17f), new Color(0.035f, 0.06f, 0.09f), darkness);
                camera = Color.Lerp(new Color(0.025f, 0.045f, 0.12f), new Color(0.008f, 0.012f, 0.05f), darkness);
                riverWidth = Mathf.Max(5.55f, 6.05f - nightLoop * 0.035f);
            }
        }

        private void UpdateEnvironmentTransition(float dt)
        {
            if (terrainTransitionTimer > 0f)
            {
                var step = Mathf.Clamp01(dt / terrainTransitionTimer);
                currentWaterColor = Color.Lerp(currentWaterColor, targetWaterColor, step);
                currentBankColor = Color.Lerp(currentBankColor, targetBankColor, step);
                currentCameraColor = Color.Lerp(currentCameraColor, targetCameraColor, step);
                currentRiverHalfWidth = Mathf.Lerp(currentRiverHalfWidth, targetRiverHalfWidth, step);
                terrainTransitionTimer = Mathf.Max(0f, terrainTransitionTimer - dt);
                if (terrainTransitionTimer <= 0f)
                {
                    currentWaterColor = targetWaterColor;
                    currentBankColor = targetBankColor;
                    currentCameraColor = targetCameraColor;
                    currentRiverHalfWidth = targetRiverHalfWidth;
                }
            }
            ApplyEnvironment();
        }

        private void ApplyEnvironment()
        {
            waterRenderer.color = currentWaterColor;
            gameCamera.backgroundColor = currentCameraColor;
            for (var i = 0; i < bankRenderers.Count; i++)
            {
                var renderer = bankRenderers[i];
                renderer.color = currentBankColor;
                var sign = i == 0 ? -1f : 1f;
                renderer.transform.position = new Vector3(sign * (RiverHalfWidth + 6f), 0f, 0f);
                renderer.transform.localScale = new Vector3(12f, 22f, 1f);
            }
            for (var i = 0; i < laneRenderers.Count; i++)
            {
                laneRenderers[i].color = new Color(1f, 1f, 1f, stage == 3 ? 0.035f : 0.08f);
                laneRenderers[i].transform.position = new Vector3((i - 1) * RiverHalfWidth * 0.52f, 0f, 0f);
            }
        }

        private void UpdateJuice(float dt)
        {
            shakeStrength = Mathf.MoveTowards(shakeStrength, 0f, dt * 1.7f);
            if (gameCamera != null)
            {
                var shake = Random.insideUnitCircle * shakeStrength;
                gameCamera.transform.position = cameraBasePosition + new Vector3(shake.x, shake.y, 0f);
            }

            for (var i = juiceParticles.Count - 1; i >= 0; i--)
            {
                var particle = juiceParticles[i];
                if (particle.Transform == null)
                {
                    juiceParticles.RemoveAt(i);
                    continue;
                }
                particle.Life -= dt;
                particle.Transform.position += (Vector3)(particle.Velocity * dt);
                particle.Velocity = Vector2.Lerp(particle.Velocity, Vector2.down * 0.35f, dt * 2.5f);
                var ratio = Mathf.Clamp01(particle.Life / particle.MaxLife);
                var color = particle.Renderer.color;
                color.a = ratio * 0.82f;
                particle.Renderer.color = color;
                particle.Transform.localScale *= 1f - dt * 1.35f;
                if (particle.Life > 0f) continue;
                Destroy(particle.Transform.gameObject);
                juiceParticles.RemoveAt(i);
            }
        }

        private void SpawnParticle(Vector2 position, Color color, Vector2 velocity, float size, float life, int order)
        {
            var go = SalmonVisuals.Circle("Splash", world, position, Vector2.one * size, color, order);
            juiceParticles.Add(new JuiceParticle
            {
                Transform = go.transform,
                Renderer = go.GetComponent<SpriteRenderer>(),
                Velocity = velocity,
                Life = life,
                MaxLife = life
            });
        }

        private void Burst(Vector2 position, Color color, int count, float speed)
        {
            for (var i = 0; i < count; i++)
            {
                var direction = Random.insideUnitCircle.normalized;
                SpawnParticle(position, color, direction * Random.Range(speed * 0.45f, speed),
                    Random.Range(0.10f, 0.28f), Random.Range(0.35f, 0.7f), 35);
            }
        }

        private void ClearJuice()
        {
            foreach (var particle in juiceParticles)
                if (particle.Transform != null) Destroy(particle.Transform.gameObject);
            juiceParticles.Clear();
            if (gameCamera != null) gameCamera.transform.position = cameraBasePosition;
        }

        private void ShowEvent(string text, float duration)
        {
            eventText = text;
            eventTimer = duration;
        }

        private void EnsureStyles()
        {
            if (titleStyle != null) return;
            whiteTexture = Texture2D.whiteTexture;
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 78, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            subtitleStyle = new GUIStyle(GUI.skin.label) { fontSize = 28, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.85f, 0.96f, 1f) } };
            hudStyle = new GUIStyle(GUI.skin.label) { fontSize = 27, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 21, normal = { textColor = new Color(0.85f, 0.94f, 0.97f) } };
            centeredStyle = new GUIStyle(hudStyle) { alignment = TextAnchor.MiddleCenter };
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(18, 18, 10, 10) };
        }

        private void OnGUI()
        {
            EnsureStyles();
            var scale = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
            var offsetX = (Screen.width - 1920f * scale) * 0.5f;
            var offsetY = (Screen.height - 1080f * scale) * 0.5f;
            GUI.matrix = Matrix4x4.TRS(new Vector3(offsetX, offsetY, 0f), Quaternion.identity, Vector3.one * scale);

            if (state == GameState.Lobby) DrawLobby();
            else if (state == GameState.Playing) DrawHud();
            else DrawGameOver();
        }

        private void DrawLobby()
        {
            DrawRect(new Rect(0, 0, 1920, 1080), new Color(0.01f, 0.08f, 0.14f, 0.34f));
            GUI.Label(new Rect(360, 160, 1200, 110), "SALMON RUN", titleStyle);
            GUI.Label(new Rect(440, 272, 1040, 55), "거슬러 올라가, 고향으로", subtitleStyle);

            DrawPanel(new Rect(610, 400, 700, 360));
            if (!settingsOpen)
            {
                if (GUI.Button(new Rect(735, 470, 450, 82), "게임 시작", buttonStyle)) StartGame();
                if (GUI.Button(new Rect(735, 575, 450, 70), "설정", buttonStyle)) settingsOpen = true;
                GUI.Label(new Rect(650, 685, 620, 40), "WASD / 방향키 이동  ·  SPACE 점프", centeredStyle);
            }
            else
            {
                GUI.Label(new Rect(705, 438, 510, 50), "사운드 설정", centeredStyle);
                GUI.Label(new Rect(700, 520, 180, 42), "전체 음량", smallStyle);
                masterVolume = GUI.HorizontalSlider(new Rect(880, 530, 290, 30), masterVolume, 0f, 1f);
                AudioListener.volume = masterVolume;
                GUI.Label(new Rect(1178, 516, 70, 42), Mathf.RoundToInt(masterVolume * 100) + "%", smallStyle);
                if (GUI.Button(new Rect(800, 630, 320, 65), "돌아가기", buttonStyle)) settingsOpen = false;
            }
            GUI.Label(new Rect(710, 815, 500, 45), "최고 점수  " + bestScore.ToString("N0"), centeredStyle);
        }

        private void DrawHud()
        {
            DrawFogOverlay();
            if (hurtFlash > 0f)
                DrawRect(new Rect(0, 0, 1920, 1080), new Color(0.85f, 0.03f, 0.02f, hurtFlash * 0.22f));
            if (healFlash > 0f)
                DrawRect(new Rect(0, 0, 1920, 1080), new Color(0.08f, 1f, 0.42f, healFlash * 0.18f));

            DrawPanel(new Rect(36, 30, 520, 138), 0.76f);
            GUI.Label(new Rect(62, 44, 470, 42), StageName(), hudStyle);
            GUI.Label(new Rect(62, 92, 220, 38), "체력", smallStyle);
            DrawRect(new Rect(145, 100, 360, 24), new Color(0.02f, 0.04f, 0.08f, 0.75f));
            DrawRect(new Rect(149, 104, 352 * health / 100f, 16), health > 35f ? new Color(0.35f, 0.94f, 0.48f) : new Color(1f, 0.25f, 0.2f));
            GUI.Label(new Rect(150, 124, 350, 28), Mathf.CeilToInt(health) + " / 100", smallStyle);

            DrawPanel(new Rect(1450, 30, 430, 138), 0.76f);
            GUI.Label(new Rect(1480, 48, 370, 42), "SCORE  " + score.ToString("N0"), hudStyle);
            GUI.Label(new Rect(1480, 98, 370, 35), "구간 진행  " + Mathf.RoundToInt(stageTime / StageDuration * 100f) + "%", smallStyle);
            DrawRect(new Rect(1480, 138, 350, 8), new Color(1f, 1f, 1f, 0.18f));
            DrawRect(new Rect(1480, 138, 350 * stageTime / StageDuration, 8), new Color(1f, 0.75f, 0.25f));

            if (stageBannerTimer > 0f)
            {
                var bannerAlpha = Mathf.Clamp01(stageBannerTimer) * 0.68f;
                DrawRect(new Rect(480, 195, 960, 118), new Color(0.015f, 0.06f, 0.1f, bannerAlpha));
                GUI.Label(new Rect(520, 205, 880, 46), StageName(), centeredStyle);
                GUI.Label(new Rect(520, 252, 880, 42), transitionText, subtitleStyle);
            }
            else if (eventTimer > 0f)
            {
                GUI.Label(new Rect(500, 215, 920, 65), eventText, centeredStyle);
            }

            if (fogWarningTimer > 0f)
            {
                DrawRect(new Rect(455, 320, 1010, 105), new Color(0.06f, 0.09f, 0.12f, 0.82f));
                GUI.Label(new Rect(500, 336, 920, 72), "⚠  안개 구간입니다!  ⚠", centeredStyle);
            }

            GUI.Label(new Rect(40, 1018, 920, 35), "WASD / 방향키 이동   SPACE 점프   초록 구슬 체력 +25", smallStyle);
        }

        private void DrawGameOver()
        {
            DrawRect(new Rect(0, 0, 1920, 1080), new Color(0.015f, 0.025f, 0.06f, 0.72f));
            GUI.Label(new Rect(350, 205, 1220, 100), "여정이 끝났습니다", titleStyle);
            DrawPanel(new Rect(630, 360, 660, 390));
            GUI.Label(new Rect(700, 405, 520, 55), "최종 점수  " + score.ToString("N0"), centeredStyle);
            GUI.Label(new Rect(700, 470, 520, 45), "최고 점수  " + bestScore.ToString("N0"), centeredStyle);
            GUI.Label(new Rect(700, 525, 520, 40), StageName() + "에서 도전 종료", smallStyle);
            if (GUI.Button(new Rect(755, 600, 410, 68), "다시 시작", buttonStyle)) StartGame();
            if (GUI.Button(new Rect(755, 682, 410, 55), "로비로", buttonStyle)) ReturnToLobby();
        }

        private string StageName()
        {
            return stage switch
            {
                1 => "STAGE 1  ·  바다 / 아침",
                2 => "STAGE 2  ·  강 하류와 상류 / 노을",
                _ => "STAGE 3  ·  끝없는 밤의 강  ·  난이도 " + EndlessDifficulty
            };
        }

        private string StageDescription()
        {
            return stage switch
            {
                1 => "해초와 해파리를 피해 물살을 익히세요",
                2 => "급류, 통나무, 소용돌이가 길을 가로막습니다",
                _ => "짙은 안개 속 피라냐를 조심하세요"
            };
        }

        private void DrawFogOverlay()
        {
            if (fogIntensity <= 0.001f) return;
            var previous = GUI.color;
            var drift = Mathf.Sin(Time.time * 0.32f) * 95f;
            GUI.color = new Color(1f, 1f, 1f, fogIntensity * 0.92f);
            GUI.DrawTexture(new Rect(-120f + drift, -80f, 2160f, 1240f), SalmonVisuals.FogTexture,
                ScaleMode.StretchToFill, true);
            GUI.color = new Color(0.82f, 0.87f, 0.9f, fogIntensity * 0.38f);
            GUI.DrawTexture(new Rect(0f, 0f, 1920f, 1080f), whiteTexture);
            GUI.color = previous;
        }

        private void DrawPanel(Rect rect, float alpha = 0.86f)
        {
            DrawRect(rect, new Color(0.015f, 0.07f, 0.12f, alpha));
            DrawRect(new Rect(rect.x, rect.y, rect.width, 3f), new Color(0.26f, 0.87f, 0.94f, 0.8f));
        }

        private void DrawRect(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, whiteTexture);
            GUI.color = previous;
        }
    }
}
