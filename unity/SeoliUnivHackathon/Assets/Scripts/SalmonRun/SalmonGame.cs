using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SalmonRun
{
    /// <summary>
    /// 게임 진행 전체. 씬에 배치된 오브젝트(카메라·World·플레이어·UI)를 참조로 받고,
    /// 장애물은 프리팹을 Instantiate 한다. 하이어라키는 Tools > Salmon Run > 씬 구성 메뉴가 만든다.
    /// </summary>
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

        [Header("씬 참조")]
        [SerializeField] private Camera gameCamera;
        [SerializeField] private Transform world;
        [SerializeField] private Transform hazardRoot;
        [SerializeField] private Transform player;
        [SerializeField] private SpriteRenderer playerBody;
        [SerializeField] private SalmonPlayerAnimator playerAnimator;
        [SerializeField] private SpriteRenderer waterRenderer;
        [Tooltip("0 = 왼쪽 강둑, 1 = 오른쪽 강둑")]
        [SerializeField] private SpriteRenderer[] bankRenderers = new SpriteRenderer[0];
        [Tooltip("왼쪽 → 오른쪽 순서의 길 안내선 3개")]
        [SerializeField] private SpriteRenderer[] laneRenderers = new SpriteRenderer[0];
        [Tooltip("자식들을 물결 반짝임으로 흘려보낸다")]
        [SerializeField] private Transform flowSparkRoot;
        [SerializeField] private SalmonUI ui;

        [Header("배경 이미지")]
        [SerializeField] private SalmonBackground background;
        [Tooltip("스테이지 1 · 외해")]
        [SerializeField] private Sprite seaBackground;
        [Tooltip("바다 → 강 전환 구간에서 한 번만 지나가는 해안")]
        [SerializeField] private Sprite coastBackground;
        [Tooltip("스테이지 2·3 · 강")]
        [SerializeField] private Sprite riverBackground;

        [Header("로비 배경음악")]
        [Tooltip("비어 있으면 Resources/Audio/Morning_s_First_Leap 을 자동으로 불러온다")]
        [SerializeField] private AudioClip lobbyMusic;
        [Range(0f, 1f)]
        [SerializeField] private float lobbyMusicVolume = 0.65f;
        [Tooltip("비어 있으면 Resources/Audio/Morning_at_the_Riverbend 를 자동으로 불러온다")]
        [SerializeField] private AudioClip gameplayMusic;
        [Range(0f, 1f)]
        [SerializeField] private float gameplayMusicVolume = 0.65f;
        [Tooltip("비어 있으면 Resources/Audio/Light_on_the_Riverbed 를 자동으로 불러온다")]
        [SerializeField] private AudioClip gameOverMusic;
        [Range(0f, 1f)]
        [SerializeField] private float gameOverMusicVolume = 0.65f;

        [Header("테스트 — 플레이 중에도 인스펙터에서 바로 조절된다")]
        [Tooltip("한 스테이지가 끝나기까지의 시간(초). 기본 34")]
        [SerializeField] private float stageDuration = 34f;
        [Tooltip("진행 속도 배율. 스크롤·장애물·배경이 함께 빨라진다")]
        [Range(0.25f, 5f)]
        [SerializeField] private float speedMultiplier = 1f;
        [Tooltip("장애물 스폰 간격 배율. 값이 작을수록 빽빽해진다")]
        [Range(0.2f, 3f)]
        [SerializeField] private float spawnIntervalMultiplier = 1f;
        [Tooltip("켜면 체력이 닳지 않는다 — 뒷 스테이지 확인용")]
        [SerializeField] private bool invincible;

        [Header("장애물 프리팹 (HazardKind 별 하나씩)")]
        [SerializeField] private List<SalmonHazard> hazardPrefabs = new();

        private readonly Dictionary<HazardKind, SalmonHazard> hazardPrefabByKind = new();
        private readonly List<SalmonHazard> hazards = new();
        private readonly List<Transform> flowMarks = new();
        private readonly List<JuiceParticle> juiceParticles = new();

        private GameState state = GameState.Lobby;

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
        private AudioSource lobbyMusicSource;
        private AudioSource gameplayMusicSource;
        private AudioSource gameOverMusicSource;
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
        private float currentTintAlpha = 0.14f;
        private float targetTintAlpha = 0.14f;
        private bool coastTileSpawned;
        private bool coastPending;
        private string transitionText = "";

        private const float WorldTop = 10.5f;
        private const float WorldBottom = -10.5f;

        private float StageDuration => Mathf.Max(1f, stageDuration);
        private float ScrollSpeed =>
            (4.65f + (stage - 1) * 0.58f + nightLoop * 0.28f) * Mathf.Max(0.05f, speedMultiplier);
        private int EndlessDifficulty => stage < 3 ? 0 : nightLoop + 1;
        private float HalfWidth => gameCamera.orthographicSize * gameCamera.aspect;
        private float RiverHalfWidth => Mathf.Min(HalfWidth - 1.1f, currentRiverHalfWidth);

        /// <summary>배경 타일이 내려가는 속도. 물결 반짝임과 같은 속도라야 화면이 하나로 움직인다.</summary>
        public float BackgroundScrollSpeed => state == GameState.Playing ? ScrollSpeed : 1.1f;

        /// <summary>
        /// 화면 위로 새로 올라가는 배경 타일에 어떤 그림을 넣을지 정한다.
        /// travelSeconds 는 그 타일이 화면을 가로지르는 데 걸리는 시간 — 이걸로
        /// 스테이지가 바뀌는 순간에 해안 그림이 딱 한 번 지나가도록 미리 앞당겨 배정한다.
        /// </summary>
        public Sprite BackgroundSpriteForNextTile(float travelSeconds, bool canSwitch)
        {
            if (state != GameState.Playing) return SpriteForStage(stage);
            if (coastTileSpawned) return SpriteForStage(stage);
            if (coastBackground == null) return SpriteForStage(stage);

            // 스테이지 1이 끝나갈 무렵 해안을 '예약'한다. 스테이지가 이미 넘어갔다면 즉시 예약 —
            // 전환 가능한 타일은 두 장에 한 번뿐이라, 시간만 보고 판단하면 기회를 놓쳐 건너뛴다.
            if (stage >= 2 || StageDuration - stageTime <= travelSeconds) coastPending = true;
            if (!coastPending) return seaBackground;

            // 아직 그림을 바꿀 수 없는 타일이면 바다를 한 장 더 깔고 다음 타일에서 전환한다
            if (!canSwitch) return seaBackground;

            coastTileSpawned = true;
            return coastBackground;
        }

        private Sprite SpriteForStage(int targetStage)
        {
            var sprite = targetStage == 1 ? seaBackground : riverBackground;
            return sprite != null ? sprite : seaBackground;
        }

        private void Awake()
        {
            Application.targetFrameRate = 60;
            bestScore = PlayerPrefs.GetInt("SalmonRunBest", 0);

            if (gameCamera == null) gameCamera = Camera.main;
            if (gameCamera == null || world == null || hazardRoot == null || player == null || waterRenderer == null)
            {
                Debug.LogError("[SalmonGame] 씬 참조가 비어 있습니다. Tools > Salmon Run > 씬 구성 을 실행하세요.", this);
                enabled = false;
                return;
            }
            if (playerBody == null) playerBody = player.Find("Body")?.GetComponent<SpriteRenderer>();
            cameraBasePosition = gameCamera.transform.position;

            flowMarks.Clear();
            if (flowSparkRoot != null)
                foreach (Transform child in flowSparkRoot) flowMarks.Add(child);

            hazardPrefabByKind.Clear();
            foreach (var prefab in hazardPrefabs)
                if (prefab != null) hazardPrefabByKind[prefab.Kind] = prefab;

            if (ui != null)
            {
                ui.StartClicked += StartGame;
                ui.SettingsClicked += () => settingsOpen = true;
                ui.BackClicked += () => settingsOpen = false;
                ui.RestartClicked += StartGame;
                ui.LobbyClicked += ReturnToLobby;
                ui.VolumeChanged += v => { masterVolume = v; AudioListener.volume = v; };
            }
            AudioListener.volume = masterVolume;

            SetupLobbyMusic();

            SetTheme(1);
            ResetBackground();
        }

        private void SetupLobbyMusic()
        {
            if (lobbyMusic == null)
                lobbyMusic = Resources.Load<AudioClip>("Audio/Morning_s_First_Leap");
            if (gameplayMusic == null)
                gameplayMusic = Resources.Load<AudioClip>("Audio/Morning_at_the_Riverbend");
            if (gameOverMusic == null)
                gameOverMusic = Resources.Load<AudioClip>("Audio/Light_on_the_Riverbed");

            if (lobbyMusic == null)
            {
                Debug.LogWarning("[SalmonGame] 로비 음악을 찾을 수 없습니다: Resources/Audio/Morning_s_First_Leap.mp3", this);
            }
            else
            {
                lobbyMusicSource = gameObject.AddComponent<AudioSource>();
                ConfigureMusicSource(lobbyMusicSource, lobbyMusic, lobbyMusicVolume);
                lobbyMusicSource.Play();
            }

            if (gameplayMusic == null)
            {
                Debug.LogWarning("[SalmonGame] 인게임 음악을 찾을 수 없습니다: Resources/Audio/Morning_at_the_Riverbend.mp3", this);
            }
            else
            {
                gameplayMusicSource = gameObject.AddComponent<AudioSource>();
                ConfigureMusicSource(gameplayMusicSource, gameplayMusic, 0f);
            }

            if (gameOverMusic == null)
            {
                Debug.LogWarning("[SalmonGame] 게임오버 음악을 찾을 수 없습니다: Resources/Audio/Light_on_the_Riverbed.mp3", this);
            }
            else
            {
                gameOverMusicSource = gameObject.AddComponent<AudioSource>();
                ConfigureMusicSource(gameOverMusicSource, gameOverMusic, 0f);
            }
        }

        private static void ConfigureMusicSource(AudioSource source, AudioClip clip, float volume)
        {
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = volume;
        }

        private void UpdateLobbyMusic(float dt)
        {
            var inLobby = state == GameState.Lobby;
            var inGameplay = state == GameState.Playing;
            var gameIsOver = state == GameState.GameOver;
            var fadeStep = dt * 0.55f;

            if (lobbyMusicSource != null)
            {
                if (inLobby && !lobbyMusicSource.isPlaying)
                {
                    lobbyMusicSource.volume = 0f;
                    lobbyMusicSource.Play();
                }
                lobbyMusicSource.volume = Mathf.MoveTowards(lobbyMusicSource.volume,
                    inLobby ? lobbyMusicVolume : 0f, fadeStep);
                if (!inLobby && lobbyMusicSource.isPlaying && lobbyMusicSource.volume <= 0.001f)
                    lobbyMusicSource.Stop();
            }

            if (gameplayMusicSource != null)
            {
                if (inGameplay && !gameplayMusicSource.isPlaying)
                {
                    gameplayMusicSource.volume = 0f;
                    gameplayMusicSource.Play();
                }
                gameplayMusicSource.volume = Mathf.MoveTowards(gameplayMusicSource.volume,
                    inGameplay ? gameplayMusicVolume : 0f, fadeStep);
                if (!inGameplay && gameplayMusicSource.isPlaying && gameplayMusicSource.volume <= 0.001f)
                    gameplayMusicSource.Stop();
            }

            if (gameOverMusicSource != null)
            {
                if (gameIsOver && !gameOverMusicSource.isPlaying)
                {
                    gameOverMusicSource.volume = 0f;
                    gameOverMusicSource.Play();
                }
                gameOverMusicSource.volume = Mathf.MoveTowards(gameOverMusicSource.volume,
                    gameIsOver ? gameOverMusicVolume : 0f, fadeStep);
                if (!gameIsOver && gameOverMusicSource.isPlaying && gameOverMusicSource.volume <= 0.001f)
                    gameOverMusicSource.Stop();
            }
        }

        private void Update()
        {
            var dt = Mathf.Min(Time.deltaTime, 0.05f);
            UpdateLobbyMusic(dt);
            AnimateWater(dt);
            UpdateEnvironmentTransition(dt);
            UpdateJuice(dt);
            if (state == GameState.Playing)
            {
                ReadMovement(dt);
                UpdateRun(dt);
                UpdateHazards(dt);
                CheckHazards(dt);
                UpdateStage(dt);
            }
            UpdateUI();
        }

        private void AnimateWater(float dt)
        {
            var speed = state == GameState.Playing ? ScrollSpeed : 1.1f;
            foreach (var mark in flowMarks)
            {
                if (mark == null) continue;
                mark.position += Vector3.down * speed * dt;
                if (mark.position.y < WorldBottom)
                {
                    mark.position = new Vector3(Random.Range(-RiverHalfWidth + 0.4f, RiverHalfWidth - 0.4f),
                        WorldTop, 0f);
                }
            }

            forwardPulse += dt * 4f;
            if (player != null && state != GameState.Playing)
            {
                player.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(forwardPulse) * 3f);
                if (playerAnimator != null) playerAnimator.SpeedScale = 0.65f;
            }
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
            // 스프라이트라서 평소에는 흰색(원본 그대로), 피격 때만 붉게 점멸시킨다
            if (playerBody != null)
                playerBody.color = hurtFlash > 0f && Mathf.FloorToInt(hurtFlash * 18f) % 2 == 0
                    ? new Color(1f, 0.35f, 0.30f)
                    : Color.white;

            // 좌우로 헤엄치면 진행 방향으로 살짝 눕는다
            var tilt = Mathf.Clamp(-playerVelocity.x / 6.4f, -1f, 1f) * 20f;
            player.localRotation = Quaternion.Lerp(player.localRotation,
                Quaternion.Euler(0f, 0f, tilt), dt * 10f);

            // 빨리 헤엄칠수록, 점프 중일수록 꼬리가 빨라진다
            if (playerAnimator != null)
                playerAnimator.SpeedScale = 1f + movementStretch * 0.9f + (jumpTimer > 0f ? 0.8f : 0f);

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
                spawnTimer = Mathf.Max(0.42f, baseInterval - nightLoop * 0.06f) * Random.Range(0.75f, 1.25f)
                             * Mathf.Max(0.05f, spawnIntervalMultiplier);
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
            if (amount <= 0f || damageCooldown > 0f || invincible) return;
            health = Mathf.Max(0f, health - amount);
            damageCooldown = 0.8f;
            hurtFlash = 0.55f;
            shakeStrength = Mathf.Max(shakeStrength, 0.46f);
            playerVelocity += Vector2.down * 2.2f;
            Burst(player.position, new Color(1f, 0.22f, 0.16f), 14, 3.8f);
            ShowEvent(message + "  -" + Mathf.RoundToInt(amount), 1f);
            if (health <= 0f) EndGame();
        }

        // 컴포넌트 헤더 우클릭 메뉴. 플레이 중에 눌러 원하는 구간을 바로 확인한다.
        [ContextMenu("테스트: 다음 스테이지로")]
        private void DebugSkipStage()
        {
            stageTime = StageDuration;
        }

        [ContextMenu("테스트: 바다→강 전환 직전으로")]
        private void DebugJumpToCoast()
        {
            stage = 1;
            nightLoop = 0;
            coastTileSpawned = false;
            coastPending = false;
            SetTheme(1);
            stageTime = StageDuration - (28.1f / ScrollSpeed) - 0.4f;
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

        /// <summary>
        /// 프리팹을 복제한다. 크기·피해·기본 속도·수명은 프리팹(SalmonHazard) 값을 쓰고,
        /// 스폰 위치나 강폭에 따라 달라지는 값만 여기서 정한다.
        /// </summary>
        private void CreateHazard(HazardKind kind, Vector2 position)
        {
            if (!hazardPrefabByKind.TryGetValue(kind, out var prefab) || prefab == null)
            {
                Debug.LogWarning($"[SalmonGame] {kind} 프리팹이 등록되지 않았습니다. Tools > Salmon Run > 스프라이트·프리팹 생성 후 SalmonGame에 연결하세요.", this);
                return;
            }

            var hazard = Instantiate(prefab, position, Quaternion.identity, hazardRoot);
            hazard.name = kind.ToString();
            hazard.Phase = Random.Range(0f, 6.28f);

            switch (kind)
            {
                case HazardKind.Rapid:
                    hazard.Velocity = (Random.value < 0.5f ? Vector2.left : Vector2.right) * 0.35f;
                    break;
                case HazardKind.FishSchool:
                    hazard.Velocity = Vector2.right * Random.Range(-1.2f, 1.2f);
                    break;
                case HazardKind.Debris:
                    hazard.Velocity = (position.x < 0f ? Vector2.right : Vector2.left) * 7f;
                    break;
                case HazardKind.BearSwipe:
                    hazard.Velocity = (position.x < 0f ? Vector2.right : Vector2.left) * 8.2f;
                    ShowEvent("강둑에서 곰이 공격합니다!", 1.1f);
                    break;
                case HazardKind.Fog:
                    hazard.Velocity = Vector2.up * ScrollSpeed;
                    if (hazard.FogRenderer != null)
                        hazard.FogRenderer.transform.localScale = new Vector3((HalfWidth * 2f + 3f) / 12f, 2.8f, 1f);
                    break;
                case HazardKind.FallenTree:
                    // 프리팹은 1스테이지 강폭(NominalFallenTreeWidth) 기준으로 만들어져 있고, 현재 강폭에 맞춰 늘린다
                    var treeWidth = RiverHalfWidth * 2f - 0.25f;
                    hazard.transform.localScale = new Vector3(treeWidth / SalmonHazard.NominalFallenTreeWidth, 1f, 1f);
                    hazard.HalfExtents = new Vector2(treeWidth * 0.5f, 0.48f);
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
            ResetBackground();
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
            ResetBackground();
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
                out currentRiverHalfWidth, out currentTintAlpha);
            targetWaterColor = currentWaterColor;
            targetBankColor = currentBankColor;
            targetCameraColor = currentCameraColor;
            targetRiverHalfWidth = currentRiverHalfWidth;
            targetTintAlpha = currentTintAlpha;
            terrainTransitionTimer = 0f;
            ApplyEnvironment();
        }

        /// <summary>배경 타일을 첫 스테이지(바다)로 되돌리고 해안 전환 그림을 다시 쓸 수 있게 한다.</summary>
        private void ResetBackground()
        {
            coastTileSpawned = false;
            coastPending = false;
            if (background != null) background.ResetTiles();
        }

        private void BeginEnvironmentTransition(int targetStage)
        {
            GetTheme(targetStage, out targetWaterColor, out targetBankColor, out targetCameraColor,
                out targetRiverHalfWidth, out targetTintAlpha);
            terrainTransitionTimer = 6.5f;
        }

        private void GetTheme(int targetStage, out Color water, out Color bank, out Color camera, out float riverWidth,
            out float tintAlpha)
        {
            // riverWidth 는 배경 그림에 실제로 그려진 물길에 맞춘 값이다.
            // 강 그림(941px)의 가장 좁은 물길은 293~690px → 타일 폭 34유닛 기준 약 14.3유닛(반폭 7.1).
            if (targetStage == 1)
            {
                water = new Color(0.06f, 0.55f, 0.76f);
                bank = new Color(0.23f, 0.60f, 0.36f);
                camera = new Color(0.50f, 0.82f, 0.91f);
                riverWidth = 10.5f;   // 육지가 없는 외해 — 넓게 쓴다
                tintAlpha = 0.14f;
            }
            else if (targetStage == 2)
            {
                water = new Color(0.13f, 0.39f, 0.52f);
                bank = new Color(0.50f, 0.31f, 0.18f);
                camera = new Color(0.94f, 0.43f, 0.25f);
                riverWidth = 7.0f;
                tintAlpha = 0.30f;    // 노을빛
            }
            else
            {
                var darkness = Mathf.Clamp01(nightLoop * 0.025f);
                water = Color.Lerp(new Color(0.035f, 0.18f, 0.31f), new Color(0.012f, 0.07f, 0.16f), darkness);
                bank = Color.Lerp(new Color(0.09f, 0.14f, 0.17f), new Color(0.035f, 0.06f, 0.09f), darkness);
                camera = Color.Lerp(new Color(0.025f, 0.045f, 0.12f), new Color(0.008f, 0.012f, 0.05f), darkness);
                riverWidth = Mathf.Max(6.4f, 7.0f - nightLoop * 0.035f);
                tintAlpha = Mathf.Min(0.82f, 0.68f + darkness * 0.8f);   // 밤
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
                currentTintAlpha = Mathf.Lerp(currentTintAlpha, targetTintAlpha, step);
                terrainTransitionTimer = Mathf.Max(0f, terrainTransitionTimer - dt);
                if (terrainTransitionTimer <= 0f)
                {
                    currentWaterColor = targetWaterColor;
                    currentBankColor = targetBankColor;
                    currentCameraColor = targetCameraColor;
                    currentRiverHalfWidth = targetRiverHalfWidth;
                    currentTintAlpha = targetTintAlpha;
                }
            }
            ApplyEnvironment();
        }

        private void ApplyEnvironment()
        {
            // 배경 그림이 깔려 있으면 Water 사각형은 그 위에 얹히는 '분위기 보정' 레이어가 된다.
            // (Water 오브젝트 자체는 지우면 안 된다 — Awake 의 씬 참조 검사에서 게임이 멈춘다)
            var usingArt = background != null && background.HasTiles;
            // 그림 위에 얹을 때는 하늘빛(아침 하늘 / 노을 / 밤)을 써야 시간대가 읽힌다.
            // 그림이 없으면 예전처럼 물 색을 그대로 칠한다.
            var tint = usingArt ? currentCameraColor : currentWaterColor;
            tint.a = usingArt ? currentTintAlpha : 1f;
            waterRenderer.color = tint;
            gameCamera.backgroundColor = currentCameraColor;
            for (var i = 0; i < bankRenderers.Length; i++)
            {
                var renderer = bankRenderers[i];
                if (renderer == null) continue;
                // 강둑은 그림에 이미 그려져 있으므로 절차적 강둑은 숨긴다
                var bankColor = currentBankColor;
                bankColor.a = usingArt ? 0f : 1f;
                renderer.color = bankColor;
                var sign = i == 0 ? -1f : 1f;
                renderer.transform.position = new Vector3(sign * (RiverHalfWidth + 6f), 0f, 0f);
                renderer.transform.localScale = new Vector3(12f, 22f, 1f);
            }
            for (var i = 0; i < laneRenderers.Length; i++)
            {
                if (laneRenderers[i] == null) continue;
                var laneAlpha = usingArt ? 0f : (stage == 3 ? 0.035f : 0.08f);
                laneRenderers[i].color = new Color(1f, 1f, 1f, laneAlpha);
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

        // 물보라 파티클은 수명이 짧은 이펙트라 런타임 생성으로 둔다
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

        // ---------------------------------------------------------------- UI

        private void UpdateUI()
        {
            if (ui == null) return;
            switch (state)
            {
                case GameState.Lobby:
                    ui.ShowLobby(bestScore, settingsOpen, masterVolume);
                    break;
                case GameState.Playing:
                    ui.ShowHud(new SalmonUI.HudData
                    {
                        StageName = StageName(),
                        Health = health,
                        Score = score,
                        StageProgress = Mathf.Clamp01(stageTime / StageDuration),
                        BannerAlpha = stageBannerTimer > 0f ? Mathf.Clamp01(stageBannerTimer) : 0f,
                        BannerText = transitionText,
                        EventText = eventTimer > 0f ? eventText : "",
                        FogWarning = fogWarningTimer > 0f,
                        FogIntensity = fogIntensity,
                        HurtFlash = Mathf.Max(0f, hurtFlash),
                        HealFlash = Mathf.Max(0f, healFlash),
                    });
                    break;
                case GameState.GameOver:
                    ui.ShowGameOver(score, bestScore, StageName());
                    break;
            }
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
    }
}
