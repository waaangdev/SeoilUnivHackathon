using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SalmonRun
{
    /// <summary>
    /// 캔버스 UI. SalmonGame이 매 프레임 상태를 넘겨주면 표시만 담당한다.
    /// 버튼/슬라이더 입력은 이벤트로 게임에 알린다.
    /// 하이어라키는 Tools > Salmon Run > 씬 구성 메뉴가 만든다.
    /// </summary>
    public sealed class SalmonUI : MonoBehaviour
    {
        public struct HudData
        {
            public string StageName;
            public float Health;
            public int Score;
            public float StageProgress;     // 0~1
            public float BannerAlpha;       // 0이면 배너 숨김
            public string BannerText;
            public string EventText;        // 비어 있으면 숨김
            public bool FogWarning;
            public float FogIntensity;      // 0~1
            public float HurtFlash;
            public float HealFlash;
        }

        [Header("패널")]
        [SerializeField] GameObject lobbyPanel;
        [SerializeField] GameObject hudPanel;
        [SerializeField] GameObject gameOverPanel;

        [Header("로비")]
        [SerializeField] GameObject lobbyMenuGroup;
        [SerializeField] GameObject lobbySettingsGroup;
        [SerializeField] Button startButton;
        [SerializeField] Button settingsButton;
        [SerializeField] Button backButton;
        [SerializeField] Slider volumeSlider;
        [SerializeField] TMP_Text volumeText;
        [SerializeField] Slider effectsVolumeSlider;
        [SerializeField] TMP_Text effectsVolumeText;
        [SerializeField] TMP_Text lobbyBestScoreText;
        [SerializeField] Sprite lobbyBackgroundArtwork;
        [SerializeField] Sprite startButtonArtwork;
        [SerializeField] Sprite soundButtonArtwork;
        [SerializeField] AudioClip buttonClickSound;

        [Header("일시정지")]
        [SerializeField] GameObject pausePanel;
        [SerializeField] Slider pauseVolumeSlider;
        [SerializeField] TMP_Text pauseVolumeText;
        [SerializeField] Slider pauseEffectsVolumeSlider;
        [SerializeField] TMP_Text pauseEffectsVolumeText;
        [SerializeField] Button resumeButton;
        [SerializeField] Button pauseLobbyButton;

        [Header("HUD")]
        [SerializeField] TMP_Text stageText;
        [SerializeField] Image healthFill;
        [SerializeField] TMP_Text healthText;
        [SerializeField] TMP_Text scoreText;
        [SerializeField] TMP_Text progressText;
        [SerializeField] Image progressFill;
        [SerializeField] CanvasGroup banner;
        [SerializeField] TMP_Text bannerTitle;
        [SerializeField] TMP_Text bannerSubtitle;
        [SerializeField] TMP_Text eventText;
        [SerializeField] GameObject fogWarning;
        [SerializeField] Image fogOverlay;
        [SerializeField] Image fogTint;
        [SerializeField] Image hurtFlash;
        [SerializeField] Image healFlash;

        [Header("게임 오버")]
        [SerializeField] TMP_Text finalScoreText;
        [SerializeField] TMP_Text gameOverBestText;
        [SerializeField] TMP_Text gameOverStageText;
        [SerializeField] Button restartButton;
        [SerializeField] Button lobbyButton;

        static readonly Color HealthGood = new Color(0.35f, 0.94f, 0.48f);
        static readonly Color HealthLow = new Color(1f, 0.25f, 0.2f);

        public event Action StartClicked;
        public event Action SettingsClicked;
        public event Action BackClicked;
        public event Action RestartClicked;
        public event Action LobbyClicked;
        public event Action ResumeClicked;
        public event Action PauseLobbyClicked;
        public event Action<float> VolumeChanged;
        public event Action<float> EffectsVolumeChanged;

        Vector2 fogBasePosition;
        CanvasGroup transitionFade;
        Coroutine transitionRoutine;
        AudioSource buttonClickSource;
        GameObject cutscenePanel;
        Image cutsceneFront;
        Image cutsceneBack;
        Button cutsceneSkipButton;
        Toggle cutsceneNeverAgainToggle;
        Sprite[] cutsceneFrames;
        bool cutsceneSkipRequested;

        const float FadeOutDuration = 0.32f;
        const float FadeInDuration = 0.42f;
        const string SkipCutscenePref = "SalmonRunSkipIntroCutscene";

        void Awake()
        {
            ApplyLobbyArtwork();
            EnsureSeparateVolumeControls();
            EnsurePauseMenu();
            EnsureTransitionFade();
            EnsureButtonClickSound();
            EnsureCutscene();
            BindButton(startButton, BeginStartSequence);
            BindButton(settingsButton, () => SettingsClicked?.Invoke());
            BindButton(backButton, () => BackClicked?.Invoke());
            BindButton(restartButton, () => RestartClicked?.Invoke());
            BindButton(lobbyButton, () => PlayTransition(() => LobbyClicked?.Invoke()));
            BindButton(resumeButton, () => ResumeClicked?.Invoke());
            BindButton(pauseLobbyButton, () => PlayTransition(() => PauseLobbyClicked?.Invoke()));
            BindButton(cutsceneSkipButton, () => cutsceneSkipRequested = true);
            if (cutsceneNeverAgainToggle != null)
                cutsceneNeverAgainToggle.onValueChanged.AddListener(OnCutscenePreferenceChanged);
            if (volumeSlider != null) volumeSlider.onValueChanged.AddListener(v => VolumeChanged?.Invoke(v));
            if (pauseVolumeSlider != null) pauseVolumeSlider.onValueChanged.AddListener(v => VolumeChanged?.Invoke(v));
            if (effectsVolumeSlider != null) effectsVolumeSlider.onValueChanged.AddListener(OnEffectsVolumeChanged);
            if (pauseEffectsVolumeSlider != null) pauseEffectsVolumeSlider.onValueChanged.AddListener(OnEffectsVolumeChanged);
            if (fogOverlay != null) fogBasePosition = fogOverlay.rectTransform.anchoredPosition;
        }

        // ---------------------------------------------------------------- 로비

        public void ShowLobby(int bestScore, bool settingsOpen, float musicVolume, float effectsVolume)
        {
            SetPanels(lobby: true);
            if (lobbyMenuGroup != null) lobbyMenuGroup.SetActive(!settingsOpen);
            if (lobbySettingsGroup != null) lobbySettingsGroup.SetActive(settingsOpen);
            var sharedPanel = lobbyPanel != null ? lobbyPanel.transform.Find("Panel") : null;
            if (sharedPanel != null) sharedPanel.gameObject.SetActive(settingsOpen);
            if (lobbyBestScoreText != null) lobbyBestScoreText.text = "최고 점수  " + bestScore.ToString("N0");
            SetSliderValue(volumeSlider, volumeText, musicVolume);
            SetSliderValue(effectsVolumeSlider, effectsVolumeText, effectsVolume);
        }

        void ApplyLobbyArtwork()
        {
            if (lobbyBackgroundArtwork == null)
                lobbyBackgroundArtwork = Resources.Load<Sprite>("UI/LobbyBackground");
            if (startButtonArtwork == null)
                startButtonArtwork = Resources.Load<Sprite>("UI/LobbyStartButton");
            if (soundButtonArtwork == null)
                soundButtonArtwork = Resources.Load<Sprite>("UI/SoundSettingsButton");
            if (lobbyPanel == null) return;

            var background = lobbyPanel.transform.Find("Dim")?.GetComponent<Image>();
            if (background != null && lobbyBackgroundArtwork != null)
            {
                background.sprite = lobbyBackgroundArtwork;
                background.color = Color.white;
                background.preserveAspect = false;
                background.raycastTarget = false;

                SetDirectChildActive(lobbyPanel.transform, "Title", false);
                SetDirectChildActive(lobbyPanel.transform, "Subtitle", false);
                SetDirectChildActive(lobbyPanel.transform, "Panel", false);
            }

            if (startButton != null && startButtonArtwork != null)
            {
                var image = startButton.GetComponent<Image>();
                if (image != null)
                {
                    image.sprite = startButtonArtwork;
                    image.color = Color.white;
                    image.preserveAspect = true;
                    startButton.targetGraphic = image;
                }
                SetDirectChildActive(startButton.transform, "Label", false);
                SetTopLeft(startButton.transform as RectTransform, new Rect(980f, 500f, 620f, 384f));
            }

            if (settingsButton != null && soundButtonArtwork != null)
            {
                var image = settingsButton.GetComponent<Image>();
                if (image != null)
                {
                    image.sprite = soundButtonArtwork;
                    image.color = Color.white;
                    image.preserveAspect = true;
                    settingsButton.targetGraphic = image;
                }
                SetDirectChildActive(settingsButton.transform, "Label", false);
            }
            SetTopLeft(settingsButton != null ? settingsButton.transform as RectTransform : null,
                new Rect(1235f, 785f, 110f, 110f));
            SetTopLeft(lobbyBestScoreText != null ? lobbyBestScoreText.rectTransform : null,
                new Rect(1080f, 918f, 420f, 42f));
            var hint = lobbyMenuGroup != null ? lobbyMenuGroup.transform.Find("Hint") as RectTransform : null;
            SetTopLeft(hint, new Rect(890f, 982f, 720f, 38f));
            var hintText = hint != null ? hint.GetComponent<TMP_Text>() : null;
            if (hintText != null) hintText.text = "WASD / 방향키 이동  ·  SPACE 점프  ·  ESC 일시정지 / 음향 설정";
        }

        static void SetDirectChildActive(Transform parent, string name, bool active)
        {
            var child = parent.Find(name);
            if (child != null) child.gameObject.SetActive(active);
        }

        static void SetTopLeft(RectTransform rt, Rect rect)
        {
            if (rt == null) return;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(rect.x, -rect.y);
            rt.sizeDelta = new Vector2(rect.width, rect.height);
        }

        // ---------------------------------------------------------------- HUD

        public void ShowHud(in HudData d)
        {
            SetPanels(hud: true);

            if (stageText != null) stageText.text = d.StageName;
            if (healthFill != null)
            {
                healthFill.fillAmount = d.Health / 100f;
                healthFill.color = d.Health > 35f ? HealthGood : HealthLow;
            }
            if (healthText != null) healthText.text = Mathf.CeilToInt(d.Health) + " / 100";
            if (scoreText != null) scoreText.text = "SCORE  " + d.Score.ToString("N0");
            if (progressText != null) progressText.text = "구간 진행  " + Mathf.RoundToInt(d.StageProgress * 100f) + "%";
            if (progressFill != null) progressFill.fillAmount = d.StageProgress;

            bool showBanner = d.BannerAlpha > 0f;
            if (banner != null)
            {
                banner.gameObject.SetActive(showBanner);
                banner.alpha = d.BannerAlpha;
            }
            if (bannerTitle != null) bannerTitle.text = d.StageName;
            if (bannerSubtitle != null) bannerSubtitle.text = d.BannerText ?? "";

            if (eventText != null)
            {
                bool showEvent = !showBanner && !string.IsNullOrEmpty(d.EventText);
                eventText.gameObject.SetActive(showEvent);
                if (showEvent) eventText.text = d.EventText;
            }

            if (fogWarning != null) fogWarning.SetActive(d.FogWarning);

            if (fogOverlay != null)
            {
                bool fog = d.FogIntensity > 0.001f;
                fogOverlay.gameObject.SetActive(fog);
                if (fog)
                {
                    float drift = Mathf.Sin(Time.time * 0.32f) * 95f;
                    fogOverlay.rectTransform.anchoredPosition = fogBasePosition + new Vector2(drift, 0f);
                    SetAlpha(fogOverlay, d.FogIntensity * 0.92f);
                }
            }
            if (fogTint != null)
            {
                fogTint.gameObject.SetActive(d.FogIntensity > 0.001f);
                SetAlpha(fogTint, d.FogIntensity * 0.38f);
            }

            SetFlash(hurtFlash, d.HurtFlash * 0.22f);
            SetFlash(healFlash, d.HealFlash * 0.18f);
        }

        // ---------------------------------------------------------------- 게임 오버

        public void ShowGameOver(int score, int bestScore, string stageName)
        {
            SetPanels(gameOver: true);
            if (finalScoreText != null) finalScoreText.text = "최종 점수  " + score.ToString("N0");
            if (gameOverBestText != null) gameOverBestText.text = "최고 점수  " + bestScore.ToString("N0");
            if (gameOverStageText != null) gameOverStageText.text = stageName + "에서 도전 종료";
        }

        // ---------------------------------------------------------------- 도우미

        void EnsureButtonClickSound()
        {
            if (buttonClickSound == null)
                buttonClickSound = Resources.Load<AudioClip>("Audio/UIButtonClick");
            if (buttonClickSound == null) return;

            buttonClickSource = gameObject.AddComponent<AudioSource>();
            buttonClickSource.playOnAwake = false;
            buttonClickSource.loop = false;
            buttonClickSource.spatialBlend = 0f;
            buttonClickSource.volume = 0.85f;
            buttonClickSource.ignoreListenerPause = true;
        }

        public void SetEffectsVolume(float volume)
        {
            if (buttonClickSource != null) buttonClickSource.volume = Mathf.Clamp01(volume);
        }

        void OnEffectsVolumeChanged(float volume)
        {
            SetEffectsVolume(volume);
            EffectsVolumeChanged?.Invoke(volume);
        }

        void BindButton(Button button, Action action)
        {
            if (button == null) return;
            button.onClick.AddListener(() =>
            {
                PlayButtonClickSound();
                action?.Invoke();
            });
        }

        void PlayButtonClickSound()
        {
            if (buttonClickSource != null && buttonClickSound != null)
                buttonClickSource.PlayOneShot(buttonClickSound);
        }

        void EnsureCutscene()
        {
            cutsceneFrames = new Sprite[9];
            for (int i = 0; i < cutsceneFrames.Length; i++)
                cutsceneFrames[i] = Resources.Load<Sprite>($"Cutscene/Intro_{i + 1:00}");

            cutscenePanel = new GameObject("Intro Cutscene", typeof(RectTransform));
            cutscenePanel.transform.SetParent(transform, false);
            Stretch(cutscenePanel.GetComponent<RectTransform>());

            var background = RuntimeImage("Background", cutscenePanel.transform, Color.white);
            Stretch(background.rectTransform);
            background.raycastTarget = true;

            cutsceneBack = RuntimeImage("Frame Back", cutscenePanel.transform, Color.white);
            Stretch(cutsceneBack.rectTransform);
            cutsceneBack.preserveAspect = true;

            cutsceneFront = RuntimeImage("Frame Front", cutscenePanel.transform, Color.white);
            Stretch(cutsceneFront.rectTransform);
            cutsceneFront.preserveAspect = true;

            cutsceneSkipButton = RuntimeButton("Skip Button", cutscenePanel.transform,
                new Rect(1695f, 48f, 165f, 58f), "스킵");

            var toggleRoot = new GameObject("Never Show Again", typeof(RectTransform), typeof(Toggle));
            toggleRoot.transform.SetParent(cutscenePanel.transform, false);
            SetTopLeft(toggleRoot.GetComponent<RectTransform>(), new Rect(1400f, 1000f, 460f, 42f));
            cutsceneNeverAgainToggle = toggleRoot.GetComponent<Toggle>();

            var togglePanel = RuntimeImage("Control Background", toggleRoot.transform,
                new Color(0.015f, 0.06f, 0.09f, 0.82f));
            Stretch(togglePanel.rectTransform);

            var toggleBg = RuntimeImage("Background", toggleRoot.transform, new Color(0.04f, 0.12f, 0.17f, 0.95f));
            SetTopLeft(toggleBg.rectTransform, new Rect(0f, 4f, 34f, 34f));
            toggleBg.raycastTarget = true;
            var checkmark = RuntimeImage("Checkmark", toggleBg.transform, new Color(0.22f, 0.88f, 0.94f, 1f));
            var checkRt = checkmark.rectTransform;
            checkRt.anchorMin = new Vector2(0.2f, 0.2f);
            checkRt.anchorMax = new Vector2(0.8f, 0.8f);
            checkRt.offsetMin = checkRt.offsetMax = Vector2.zero;
            cutsceneNeverAgainToggle.targetGraphic = toggleBg;
            cutsceneNeverAgainToggle.graphic = checkmark;
            cutsceneNeverAgainToggle.isOn = false;

            RuntimeText("Label", toggleRoot.transform, new Rect(46f, 0f, 414f, 42f),
                "다음부터 이 컷신 보지 않기", 24f, TextAlignmentOptions.Left);
            cutscenePanel.SetActive(false);
        }

        void OnCutscenePreferenceChanged(bool neverShowAgain)
        {
            PlayButtonClickSound();
            PlayerPrefs.SetInt(SkipCutscenePref, neverShowAgain ? 1 : 0);
            PlayerPrefs.Save();
        }

        void BeginStartSequence()
        {
            bool missingFrame = cutsceneFrames == null || Array.Exists(cutsceneFrames, frame => frame == null);
            if (PlayerPrefs.GetInt(SkipCutscenePref, 0) == 1 || missingFrame)
            {
                PlayTransition(() => StartClicked?.Invoke());
                return;
            }
            if (transitionRoutine == null)
                transitionRoutine = StartCoroutine(PlayIntroCutscene());
        }

        IEnumerator PlayIntroCutscene()
        {
            cutsceneSkipRequested = false;
            cutsceneNeverAgainToggle.isOn = false;
            transitionFade.blocksRaycasts = true;
            transitionFade.transform.SetAsLastSibling();
            yield return FadeTo(1f, FadeOutDuration);

            cutsceneFront.sprite = cutsceneFrames[0];
            cutsceneFront.color = Color.white;
            cutsceneBack.color = new Color(1f, 1f, 1f, 0f);
            cutscenePanel.SetActive(true);
            cutscenePanel.transform.SetAsLastSibling();
            transitionFade.transform.SetAsLastSibling();
            yield return FadeTo(0f, 0.52f);
            transitionFade.blocksRaycasts = false;
            yield return WaitCutscene(0.45f);

            for (int i = 1; i < cutsceneFrames.Length && !cutsceneSkipRequested; i++)
            {
                if (i == 5) // 0025 -> 0027은 장면 변화가 커서 검은 화면을 거쳐 전환한다.
                {
                    transitionFade.blocksRaycasts = true;
                    yield return FadeTo(1f, 0.3f);
                    cutsceneFront.sprite = cutsceneFrames[i];
                    cutsceneFront.color = Color.white;
                    cutsceneBack.color = new Color(1f, 1f, 1f, 0f);
                    yield return FadeTo(0f, 0.38f);
                    transitionFade.blocksRaycasts = false;
                }
                else
                {
                    yield return CrossFadeCutsceneFrame(cutsceneFrames[i], 0.38f);
                }
                yield return WaitCutscene(i == cutsceneFrames.Length - 1 ? 0.65f : 1.25f);
            }

            if (cutsceneNeverAgainToggle.isOn)
            {
                PlayerPrefs.SetInt(SkipCutscenePref, 1);
                PlayerPrefs.Save();
            }

            transitionFade.blocksRaycasts = true;
            transitionFade.transform.SetAsLastSibling();
            yield return FadeTo(1f, FadeOutDuration);
            cutscenePanel.SetActive(false);
            StartClicked?.Invoke();
            yield return null;
            yield return FadeTo(0f, FadeInDuration);
            transitionFade.blocksRaycasts = false;
            transitionRoutine = null;
        }

        IEnumerator CrossFadeCutsceneFrame(Sprite next, float duration)
        {
            cutsceneBack.sprite = next;
            cutsceneBack.color = new Color(1f, 1f, 1f, 0f);
            float elapsed = 0f;
            while (elapsed < duration && !cutsceneSkipRequested)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                cutsceneFront.color = new Color(1f, 1f, 1f, 1f - t);
                cutsceneBack.color = new Color(1f, 1f, 1f, t);
                yield return null;
            }
            if (cutsceneSkipRequested) yield break;
            var swap = cutsceneFront;
            cutsceneFront = cutsceneBack;
            cutsceneBack = swap;
            cutsceneFront.color = Color.white;
            cutsceneBack.color = new Color(1f, 1f, 1f, 0f);
        }

        IEnumerator WaitCutscene(float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration && !cutsceneSkipRequested)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        Image RuntimeImage(string objectName, Transform parent, Color color)
        {
            var go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        Button RuntimeButton(string objectName, Transform parent, Rect rect, string label)
        {
            var image = RuntimeImage(objectName, parent, new Color(0.025f, 0.14f, 0.2f, 0.92f));
            image.raycastTarget = true;
            SetTopLeft(image.rectTransform, rect);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var text = RuntimeText("Label", image.transform, new Rect(0f, 0f, rect.width, rect.height),
                label, 26f, TextAlignmentOptions.Center);
            Stretch(text.rectTransform);
            return button;
        }

        TMP_Text RuntimeText(string objectName, Transform parent, Rect rect, string value, float size,
            TextAlignmentOptions alignment)
        {
            var go = new GameObject(objectName, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = lobbyBestScoreText != null ? lobbyBestScoreText.font : volumeText != null ? volumeText.font : null;
            text.text = value;
            text.fontSize = size;
            text.fontStyle = FontStyles.Bold;
            text.color = Color.white;
            text.alignment = alignment;
            text.raycastTarget = false;
            SetTopLeft(text.rectTransform, rect);
            return text;
        }

        public void ShowPause(bool visible, float musicVolume, float effectsVolume)
        {
            if (pausePanel != null && pausePanel.activeSelf != visible) pausePanel.SetActive(visible);
            if (!visible) return;
            pausePanel.transform.SetAsLastSibling();
            if (transitionFade != null) transitionFade.transform.SetAsLastSibling();
            SetSliderValue(pauseVolumeSlider, pauseVolumeText, musicVolume);
            SetSliderValue(pauseEffectsVolumeSlider, pauseEffectsVolumeText, effectsVolume);
        }

        void EnsureSeparateVolumeControls()
        {
            if (effectsVolumeSlider != null || lobbySettingsGroup == null || volumeSlider == null) return;
            var parent = lobbySettingsGroup.transform;

            var musicLabel = parent.Find("Volume Label")?.GetComponent<TMP_Text>();
            if (musicLabel != null) musicLabel.text = "BGM 음량";

            var effectsLabel = musicLabel != null ? Instantiate(musicLabel, parent) : null;
            if (effectsLabel != null)
            {
                effectsLabel.name = "Effects Volume Label";
                effectsLabel.text = "효과음 음량";
                SetTopLeft(effectsLabel.rectTransform, new Rect(700f, 580f, 180f, 42f));
            }

            effectsVolumeSlider = Instantiate(volumeSlider, parent);
            effectsVolumeSlider.name = "Effects Volume Slider";
            SetTopLeft(effectsVolumeSlider.transform as RectTransform, new Rect(880f, 590f, 290f, 30f));

            effectsVolumeText = volumeText != null ? Instantiate(volumeText, parent) : null;
            if (effectsVolumeText != null)
            {
                effectsVolumeText.name = "Effects Volume Value";
                SetTopLeft(effectsVolumeText.rectTransform, new Rect(1178f, 576f, 70f, 42f));
            }
            SetTopLeft(backButton != null ? backButton.transform as RectTransform : null,
                new Rect(800f, 660f, 320f, 65f));
        }

        void EnsurePauseMenu()
        {
            if (pausePanel != null || lobbyPanel == null || lobbySettingsGroup == null) return;

            pausePanel = new GameObject("Pause Panel", typeof(RectTransform)).gameObject;
            pausePanel.transform.SetParent(transform, false);
            Stretch(pausePanel.GetComponent<RectTransform>());

            var dimGo = new GameObject("Dim", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            dimGo.transform.SetParent(pausePanel.transform, false);
            Stretch(dimGo.GetComponent<RectTransform>());
            var dim = dimGo.GetComponent<Image>();
            dim.color = new Color(0.005f, 0.018f, 0.035f, 0.78f);
            dim.raycastTarget = true;

            var sourcePanel = lobbyPanel.transform.Find("Panel");
            if (sourcePanel != null)
            {
                var panelClone = Instantiate(sourcePanel.gameObject, pausePanel.transform);
                panelClone.name = "Panel";
                panelClone.SetActive(true);
            }

            var menu = Instantiate(lobbySettingsGroup, pausePanel.transform);
            menu.name = "Pause Menu Group";
            menu.SetActive(true);
            var title = menu.transform.Find("Settings Title")?.GetComponent<TMP_Text>();
            if (title != null) title.text = "일시정지";

            pauseVolumeSlider = menu.transform.Find("Volume Slider")?.GetComponent<Slider>();
            pauseVolumeText = menu.transform.Find("Volume Value")?.GetComponent<TMP_Text>();
            pauseEffectsVolumeSlider = menu.transform.Find("Effects Volume Slider")?.GetComponent<Slider>();
            pauseEffectsVolumeText = menu.transform.Find("Effects Volume Value")?.GetComponent<TMP_Text>();
            resumeButton = menu.transform.Find("Back Button")?.GetComponent<Button>();
            if (resumeButton != null)
            {
                resumeButton.name = "Resume Button";
                SetTopLeft(resumeButton.transform as RectTransform, new Rect(660f, 660f, 280f, 65f));
                SetButtonLabel(resumeButton, "게임 계속");

                pauseLobbyButton = Instantiate(resumeButton, menu.transform);
                pauseLobbyButton.name = "Pause Lobby Button";
                SetTopLeft(pauseLobbyButton.transform as RectTransform, new Rect(980f, 660f, 280f, 65f));
                SetButtonLabel(pauseLobbyButton, "로비로 가기");
            }
            pausePanel.SetActive(false);
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static void SetButtonLabel(Button button, string value)
        {
            var label = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
            if (label != null) label.text = value;
        }

        static void SetSliderValue(Slider slider, TMP_Text valueText, float value)
        {
            if (slider != null && !Mathf.Approximately(slider.value, value)) slider.SetValueWithoutNotify(value);
            if (valueText != null) valueText.text = Mathf.RoundToInt(value * 100f) + "%";
        }

        void EnsureTransitionFade()
        {
            var go = new GameObject("Screen Transition Fade", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
            go.transform.SetParent(transform, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var image = go.GetComponent<Image>();
            image.color = new Color(0.005f, 0.018f, 0.035f, 1f);
            image.raycastTarget = true;

            transitionFade = go.GetComponent<CanvasGroup>();
            transitionFade.alpha = 0f;
            transitionFade.interactable = false;
            transitionFade.blocksRaycasts = false;
            go.transform.SetAsLastSibling();
        }

        void PlayTransition(Action changeScreen)
        {
            if (transitionRoutine != null) return;
            if (transitionFade == null)
            {
                changeScreen?.Invoke();
                return;
            }
            transitionFade.transform.SetAsLastSibling();
            transitionRoutine = StartCoroutine(FadeTransition(changeScreen));
        }

        IEnumerator FadeTransition(Action changeScreen)
        {
            transitionFade.blocksRaycasts = true;
            yield return FadeTo(1f, FadeOutDuration);
            changeScreen?.Invoke();
            yield return null;
            yield return FadeTo(0f, FadeInDuration);
            transitionFade.blocksRaycasts = false;
            transitionRoutine = null;
        }

        IEnumerator FadeTo(float target, float duration)
        {
            float start = transitionFade.alpha;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                transitionFade.alpha = Mathf.Lerp(start, target, t * t * (3f - 2f * t));
                yield return null;
            }
            transitionFade.alpha = target;
        }

        void SetPanels(bool lobby = false, bool hud = false, bool gameOver = false)
        {
            if (lobbyPanel != null && lobbyPanel.activeSelf != lobby) lobbyPanel.SetActive(lobby);
            if (hudPanel != null && hudPanel.activeSelf != hud) hudPanel.SetActive(hud);
            if (gameOverPanel != null && gameOverPanel.activeSelf != gameOver) gameOverPanel.SetActive(gameOver);
        }

        static void SetFlash(Image image, float alpha)
        {
            if (image == null) return;
            bool on = alpha > 0.001f;
            if (image.gameObject.activeSelf != on) image.gameObject.SetActive(on);
            if (on) SetAlpha(image, alpha);
        }

        static void SetAlpha(Graphic g, float alpha)
        {
            var c = g.color;
            c.a = alpha;
            g.color = c;
        }
    }
}
