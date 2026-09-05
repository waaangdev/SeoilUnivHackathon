using System;
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
        [SerializeField] TMP_Text lobbyBestScoreText;

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
        public event Action<float> VolumeChanged;

        Vector2 fogBasePosition;

        void Awake()
        {
            if (startButton != null) startButton.onClick.AddListener(() => StartClicked?.Invoke());
            if (settingsButton != null) settingsButton.onClick.AddListener(() => SettingsClicked?.Invoke());
            if (backButton != null) backButton.onClick.AddListener(() => BackClicked?.Invoke());
            if (restartButton != null) restartButton.onClick.AddListener(() => RestartClicked?.Invoke());
            if (lobbyButton != null) lobbyButton.onClick.AddListener(() => LobbyClicked?.Invoke());
            if (volumeSlider != null) volumeSlider.onValueChanged.AddListener(v => VolumeChanged?.Invoke(v));
            if (fogOverlay != null) fogBasePosition = fogOverlay.rectTransform.anchoredPosition;
        }

        // ---------------------------------------------------------------- 로비

        public void ShowLobby(int bestScore, bool settingsOpen, float volume)
        {
            SetPanels(lobby: true);
            if (lobbyMenuGroup != null) lobbyMenuGroup.SetActive(!settingsOpen);
            if (lobbySettingsGroup != null) lobbySettingsGroup.SetActive(settingsOpen);
            if (lobbyBestScoreText != null) lobbyBestScoreText.text = "최고 점수  " + bestScore.ToString("N0");
            if (volumeSlider != null && !Mathf.Approximately(volumeSlider.value, volume)) volumeSlider.SetValueWithoutNotify(volume);
            if (volumeText != null) volumeText.text = Mathf.RoundToInt(volume * 100f) + "%";
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
