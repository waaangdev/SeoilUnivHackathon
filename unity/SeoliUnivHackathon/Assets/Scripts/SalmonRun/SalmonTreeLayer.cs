using UnityEngine;

namespace SalmonRun
{
    /// <summary>
    /// 3스테이지(밤의 강)에서 배경 위에 덮이는 나무 캐노피. 배경보다 느리게 흘러 원근감을 만든다.
    ///
    /// 배경 타일과 달리 상하 반전을 쓰지 않는다 — tree.png 는 위아래 끝 줄이 같아서
    /// 원본 방향 그대로 세로로 이어붙도록 그려져 있다. 겹치는 한 줄만 덜어내면 이음매가 없다.
    /// 하이어라키는 Tools > Salmon Run > 씬 구성 메뉴가 만든다.
    /// </summary>
    public sealed class SalmonTreeLayer : MonoBehaviour
    {
        [SerializeField] private SalmonGame game;
        [Tooltip("아래에서 위 순서로 놓일 타일들")]
        [SerializeField] private SpriteRenderer[] tiles = new SpriteRenderer[0];
        [Tooltip("배경 스크롤 속도에 곱하는 값. 1보다 작아야 배경보다 느리게 흐른다")]
        [Range(0.1f, 1f)]
        [SerializeField] private float scrollFactor = 0.55f;
        [Tooltip("카메라가 비추는 세로 절반 높이")]
        [SerializeField] private float viewHalfHeight = 9f;
        [Tooltip("맞닿는 한 줄이 겹쳐 보이지 않게 덜어낼 간격(유닛)")]
        [SerializeField] private float seamOverlap = 0.07f;

        private float tileHeight;
        private float alpha = -1f;

        public bool HasTiles => tiles != null && tiles.Length > 0;

        private float Spacing => tileHeight - seamOverlap;

        private void Start()
        {
            ResetTiles();
        }

        /// <summary>타일을 처음 위치로 되돌리고 숨긴다.</summary>
        public void ResetTiles()
        {
            if (!HasTiles) return;
            tileHeight = MeasureTileHeight();
            if (tileHeight <= 0.01f) return;

            for (var i = 0; i < tiles.Length; i++)
            {
                if (tiles[i] == null) continue;
                var p = tiles[i].transform.localPosition;
                p.y = (i - (tiles.Length - 1) * 0.5f) * Spacing;
                tiles[i].transform.localPosition = p;
            }
        }

        /// <summary>0이면 완전히 감춘다. SalmonGame 이 스테이지에 맞춰 넣어 준다.</summary>
        public void SetAlpha(float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(value, alpha)) return;
            alpha = value;
            foreach (var tile in tiles)
            {
                if (tile == null) continue;
                var c = tile.color;
                c.a = alpha;
                tile.color = c;
                // 완전히 투명하면 그리지 않는다
                tile.enabled = alpha > 0.002f;
            }
        }

        private void Update()
        {
            if (!HasTiles || tileHeight <= 0.01f || alpha <= 0.002f) return;

            var dt = Mathf.Min(Time.deltaTime, 0.05f);
            var speed = (game != null ? game.BackgroundScrollSpeed : 1.1f) * scrollFactor;
            var recycleBelow = -(viewHalfHeight + tileHeight * 0.5f);

            foreach (var tile in tiles)
            {
                if (tile == null) continue;
                var p = tile.transform.localPosition;
                p.y -= speed * dt;
                tile.transform.localPosition = p;
            }

            foreach (var tile in tiles)
            {
                if (tile == null || tile.transform.localPosition.y >= recycleBelow) continue;
                var p = tile.transform.localPosition;
                p.y = HighestTileY() + Spacing;
                tile.transform.localPosition = p;
            }
        }

        private float HighestTileY()
        {
            var top = float.NegativeInfinity;
            foreach (var tile in tiles)
                if (tile != null) top = Mathf.Max(top, tile.transform.localPosition.y);
            return top;
        }

        private float MeasureTileHeight()
        {
            foreach (var tile in tiles)
            {
                if (tile == null || tile.sprite == null) continue;
                var h = tile.sprite.bounds.size.y * Mathf.Abs(tile.transform.localScale.y);
                if (h > 0.01f) return h;
            }
            return 0f;
        }
    }
}
