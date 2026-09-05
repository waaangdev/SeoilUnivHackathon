using UnityEngine;

namespace SalmonRun
{
    /// <summary>
    /// 플레이어 연어의 유영 애니메이션. 스프라이트 시트 프레임을 순서대로 갈아 끼운다.
    /// 이 프로젝트에는 Animator를 쓰는 곳이 없어서 .anim/.controller 대신 스프라이트 교체로 처리한다.
    /// 재생 속도는 SalmonGame이 SpeedScale로 넣어 준다 — 빨리 헤엄칠수록 꼬리도 빨라진다.
    /// </summary>
    public sealed class SalmonPlayerAnimator : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer body;
        [Tooltip("player-Sheet 의 프레임 7장 (순서대로)")]
        [SerializeField] private Sprite[] frames = new Sprite[0];
        [Tooltip("SpeedScale 이 1일 때의 초당 프레임 수")]
        [SerializeField] private float baseFps = 9f;

        private float timer;
        private int index = -1;

        /// <summary>재생 속도 배율. 0이면 멈춘다.</summary>
        public float SpeedScale { get; set; } = 1f;

        private void Awake()
        {
            Show(0);
        }

        private void Update()
        {
            if (frames.Length < 2 || body == null) return;

            var dt = Mathf.Min(Time.deltaTime, 0.05f);
            timer += dt * baseFps * Mathf.Max(0f, SpeedScale);
            if (timer < 1f) return;

            var steps = Mathf.FloorToInt(timer);
            timer -= steps;
            Show((index + steps) % frames.Length);
        }

        private void Show(int next)
        {
            if (body == null || frames.Length == 0) return;
            next = Mathf.Clamp(next, 0, frames.Length - 1);
            if (next == index) return;
            index = next;
            if (frames[index] != null) body.sprite = frames[index];
        }
    }
}
