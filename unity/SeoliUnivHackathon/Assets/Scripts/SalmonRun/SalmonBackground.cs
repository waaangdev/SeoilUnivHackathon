using UnityEngine;

namespace SalmonRun
{
    /// <summary>
    /// 배경 이미지를 세로로 무한 스크롤시킨다.
    /// 타일을 아래로 흘려보내고, 화면 아래로 빠진 타일을 맨 위로 다시 올리면서
    /// 홀짝으로 상하 반전(flipY)을 걸어 위아래 이음매가 정확히 맞물리게 한다.
    /// 어떤 그림을 쓸지는 SalmonGame.BackgroundSpriteForNextTile 이 정한다.
    /// 하이어라키는 Tools > Salmon Run > 2. 씬 구성 메뉴가 만든다.
    /// </summary>
    public sealed class SalmonBackground : MonoBehaviour
    {
        [SerializeField] private SalmonGame game;
        [Tooltip("아래에서 위 순서로 놓일 타일들. 3장이면 흔들림 여유까지 덮는다")]
        [SerializeField] private SpriteRenderer[] tiles = new SpriteRenderer[0];
        [Tooltip("카메라가 비추는 세로 절반 높이. 이보다 아래로 내려간 타일을 재활용한다")]
        [SerializeField] private float viewHalfHeight = 9f;
        [Tooltip("타일을 이만큼(유닛) 겹쳐 놓는다. background3 의 맨 아랫줄 1px 이 투명해서, " +
                 "딱 맞붙이면 반전 이음매에서 그 줄이 겹쳐 흰 선으로 보인다. 0.12 ≈ 3px")]
        [SerializeField] private float seamOverlap = 0.12f;

        private float tileHeight;
        private Sprite lastSprite;      // 바로 아래(직전에 배치한) 타일의 그림
        private bool lastFlipped;       // 그 타일이 뒤집혀 있는지

        /// <summary>타일 간 실제 간격 — 겹침만큼 뺀다.</summary>
        private float Spacing => tileHeight - seamOverlap;

        public bool HasTiles => tiles != null && tiles.Length > 0;

        private void Start()
        {
            ResetTiles();
        }

        /// <summary>게임을 새로 시작할 때 첫 스테이지 배경으로 되돌린다.</summary>
        public void ResetTiles()
        {
            if (!HasTiles) return;

            tileHeight = MeasureTileHeight();
            if (tileHeight <= 0.01f) return;

            lastSprite = null;
            lastFlipped = false;
            for (var i = 0; i < tiles.Length; i++)
            {
                var tile = tiles[i];
                if (tile == null) continue;
                var p = tile.transform.localPosition;
                // 가운데 타일이 화면 중앙에 오도록 아래→위로 쌓는다
                p.y = (i - (tiles.Length - 1) * 0.5f) * Spacing;
                tile.transform.localPosition = p;
                Dress(tile);
            }
        }

        private void Update()
        {
            if (!HasTiles || tileHeight <= 0.01f) return;

            var dt = Mathf.Min(Time.deltaTime, 0.05f);
            var speed = game != null ? game.BackgroundScrollSpeed : 1.1f;
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
                if (tile == null) continue;
                if (tile.transform.localPosition.y >= recycleBelow) continue;

                var p = tile.transform.localPosition;
                p.y = HighestTileY() + Spacing;
                tile.transform.localPosition = p;
                Dress(tile);
            }
        }

        /// <summary>재배치되는 타일에 그림과 반전 여부를 배정한다.</summary>
        /// <summary>
        /// 맨 위로 올라온 타일에 그림과 반전 여부를 정한다. 규칙은 두 줄이다.
        ///  - 아래 타일과 같은 그림 → 반전을 뒤집는다. 맞닿는 변이 같은 픽셀 줄이 되어 이음매가 사라진다.
        ///  - 아래 타일과 다른 그림 → 원본 방향(반전 없음)으로 둔다.
        ///    배경 3장은 bg3(바다) → bg2(해안) → bg1(강) 순서로 위로 이어붙도록 그려져 있어서,
        ///    아래 타일도 반전이 없어야 맞물린다. 그래서 그림 교체는 lastFlipped 가 false 일 때만 허용한다.
        /// </summary>
        private void Dress(SpriteRenderer tile)
        {
            var travel = game != null && game.BackgroundScrollSpeed > 0.01f
                ? tileHeight / game.BackgroundScrollSpeed
                : 0f;
            var sprite = game != null ? game.BackgroundSpriteForNextTile(travel, !lastFlipped) : null;
            if (sprite == null) sprite = tile.sprite;
            if (sprite == null) return;

            var flip = lastSprite != null && sprite == lastSprite && !lastFlipped;

            tile.sprite = sprite;
            tile.flipY = flip;
            lastSprite = sprite;
            lastFlipped = flip;
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
