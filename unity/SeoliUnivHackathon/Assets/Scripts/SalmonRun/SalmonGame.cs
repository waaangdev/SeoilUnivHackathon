using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SalmonRun
{
    public sealed class SalmonGame : MonoBehaviour
    {
        private enum GameState { Lobby, Playing, GameOver }

        private readonly List<SalmonHazard> hazards = new();
        private readonly List<Transform> flowMarks = new();
        private readonly List<SpriteRenderer> bankRenderers = new();
        private readonly List<SpriteRenderer> laneRenderers = new();

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
        private float masterVolume = 0.75f;
        private string eventText = "";
        private float eventTimer;

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

        private float ScrollSpeed => 3.7f + (stage - 1) * 0.65f + nightLoop * 0.3f;
        private float HalfWidth => gameCamera.orthographicSize * gameCamera.aspect;
        private float RiverHalfWidth => Mathf.Min(HalfWidth - 1.1f, stage == 1 ? 7.1f : 6.1f);

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
            gameCamera.backgroundColor = new Color(0.03f, 0.12f, 0.2f);
        }

        private void BuildWorld()
        {
            world = new GameObject("World").transform;
            world.SetParent(transform, false);
            hazardRoot = new GameObject("Random Hazards").transform;
            hazardRoot.SetParent(world, false);

            waterRenderer = SalmonVisuals.Rect("Water", world, Vector2.zero, new Vector2(30f, 22f),
                new Color(0.08f, 0.56f, 0.75f), -20).GetComponent<SpriteRenderer>();

            for (var i = 0; i < 2; i++)
            {
                var x = i == 0 ? -9.8f : 9.8f;
                var bank = SalmonVisuals.Rect(i == 0 ? "Left Bank" : "Right Bank", world,
                    new Vector2(x, 0f), new Vector2(6f, 22f), new Color(0.25f, 0.57f, 0.35f), -10);
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
            if (keyboard == null) return;

            var input = Vector2.zero;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) input.x -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) input.x += 1f;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) input.y += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) input.y -= 1f;

            if (keyboard.spaceKey.wasPressedThisFrame && jumpTimer <= 0f)
            {
                jumpTimer = 0.68f;
                ShowEvent("JUMP!", 0.55f);
            }

            var slowed = IsInsideSlowZone();
            var speed = slowed ? 3.1f : 5.8f;
            var next = (Vector2)player.position + input.normalized * speed * dt;
            next.x = Mathf.Clamp(next.x, -RiverHalfWidth + 0.7f, RiverHalfWidth - 0.7f);
            next.y = Mathf.Clamp(next.y, -7.2f, 5.5f);
            player.position = next;

            var tilt = -input.x * 13f;
            player.rotation = Quaternion.Lerp(player.rotation, Quaternion.Euler(0f, 0f, tilt), dt * 10f);
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
            spawnTimer -= dt;

            scoreRemainder += (18f + ScrollSpeed * 2f) * dt;
            if (scoreRemainder >= 1f)
            {
                var earned = Mathf.FloorToInt(scoreRemainder);
                score += earned;
                scoreRemainder -= earned;
            }
            player.localScale = jumpTimer > 0f
                ? Vector3.one * (1.12f + Mathf.Sin((0.68f - jumpTimer) / 0.68f * Mathf.PI) * 0.34f)
                : Vector3.Lerp(player.localScale, Vector3.one, dt * 10f);
            playerBody.color = hurtFlash > 0f && Mathf.FloorToInt(hurtFlash * 18f) % 2 == 0
                ? Color.white
                : new Color(1f, 0.36f, 0.30f);

            if (stage == 1)
            {
                waveTimer -= dt;
                if (waveTimer <= 0f)
                {
                    waveTimer = Random.Range(6.5f, 9f);
                    player.position += Vector3.down * 1.5f;
                    ShowEvent("파도가 밀어냅니다!", 1.5f);
                }
            }

            if (spawnTimer <= 0f)
            {
                SpawnHazard();
                var baseInterval = stage == 1 ? 1.45f : stage == 2 ? 1.05f : 0.82f;
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

                if (distance > hazard.Radius + 0.52f || hazard.Hit) continue;
                if (jumpTimer > 0f && CanJumpOver(hazard.Kind)) continue;

                switch (hazard.Kind)
                {
                    case HazardKind.Seaweed:
                    case HazardKind.DarkPool:
                    case HazardKind.Fog:
                    case HazardKind.Rapid:
                    case HazardKind.Whirlpool:
                        break;
                    case HazardKind.Branch:
                    case HazardKind.Boulder:
                    case HazardKind.FishSchool:
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
                or HazardKind.DarkPool or HazardKind.Debris;
        }

        private void DealDamage(float amount, string message)
        {
            if (amount <= 0f || damageCooldown > 0f) return;
            health = Mathf.Max(0f, health - amount);
            damageCooldown = 0.8f;
            hurtFlash = 0.55f;
            ShowEvent(message + "  -" + Mathf.RoundToInt(amount), 1f);
            if (health <= 0f) EndGame();
        }

        private void UpdateStage(float dt)
        {
            if (stageTime < StageDuration) return;
            stageTime = 0f;
            score += 500;
            if (stage < 3) stage++;
            else nightLoop++;
            ClearHazards();
            SetTheme(stage);
            stageBannerTimer = 3.2f;
            spawnTimer = 1.2f;
            ShowEvent(stage < 3 ? "스테이지 클리어! +500" : "밤의 강이 더 거세집니다! +500", 2f);
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
                var pool = new[] { HazardKind.Stone, HazardKind.Bird, HazardKind.Fog, HazardKind.Debris,
                    HazardKind.DarkPool, HazardKind.Piranha };
                kind = pool[Random.Range(0, pool.Length)];
            }

            var x = Random.Range(-RiverHalfWidth + 0.7f, RiverHalfWidth - 0.7f);
            var y = kind is HazardKind.Debris or HazardKind.Piranha ? Random.Range(-1f, 5f) : WorldTop + 1f;
            if (kind == HazardKind.Debris) x = Random.value < 0.5f ? -RiverHalfWidth - 1f : RiverHalfWidth + 1f;
            CreateHazard(kind, new Vector2(x, y));
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
                    hazard.Radius = 0f; hazard.Life = 8f;
                    SalmonVisuals.Circle("Fog", root.transform, Vector2.zero, new Vector2(7f, 3.6f), new Color(0.72f, 0.78f, 0.82f, 0.32f), 30);
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
            }
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
            jumpTimer = 0f;
            damageCooldown = 0f;
            stageBannerTimer = 3.2f;
            player.position = new Vector3(0f, -5.8f, 0f);
            player.localScale = Vector3.one;
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
            SetTheme(1);
            player.position = new Vector3(0f, -5.8f, 0f);
            player.localScale = Vector3.one;
        }

        private void ClearHazards()
        {
            foreach (var hazard in hazards) if (hazard != null) Destroy(hazard.gameObject);
            hazards.Clear();
        }

        private void SetTheme(int targetStage)
        {
            Color water;
            Color bank;
            Color camera;
            if (targetStage == 1)
            {
                water = new Color(0.06f, 0.55f, 0.76f);
                bank = new Color(0.23f, 0.60f, 0.36f);
                camera = new Color(0.50f, 0.82f, 0.91f);
            }
            else if (targetStage == 2)
            {
                water = new Color(0.13f, 0.39f, 0.52f);
                bank = new Color(0.50f, 0.31f, 0.18f);
                camera = new Color(0.94f, 0.43f, 0.25f);
            }
            else
            {
                water = new Color(0.025f, 0.13f, 0.25f);
                bank = new Color(0.055f, 0.10f, 0.14f);
                camera = new Color(0.015f, 0.025f, 0.08f);
            }
            waterRenderer.color = water;
            foreach (var renderer in bankRenderers) renderer.color = bank;
            foreach (var renderer in laneRenderers) renderer.color = new Color(1f, 1f, 1f, targetStage == 3 ? 0.035f : 0.08f);
            gameCamera.backgroundColor = camera;
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
                DrawRect(new Rect(0, 405, 1920, 180), new Color(0f, 0f, 0f, 0.48f));
                GUI.Label(new Rect(300, 420, 1320, 80), StageName(), titleStyle);
                GUI.Label(new Rect(400, 505, 1120, 45), StageDescription(), subtitleStyle);
            }
            else if (eventTimer > 0f)
            {
                GUI.Label(new Rect(500, 215, 920, 65), eventText, centeredStyle);
            }

            GUI.Label(new Rect(40, 1018, 700, 35), "WASD / 방향키 이동   SPACE 점프", smallStyle);
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
                _ => "STAGE 3  ·  밤의 강" + (nightLoop > 0 ? "  × " + (nightLoop + 1) : "")
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
