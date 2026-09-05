using UnityEngine;

namespace SalmonRun
{
    public enum HazardKind
    {
        Seaweed, Branch, Leaf, Jellyfish, Boulder, Rapid, Log, Whirlpool, FishSchool,
        Stone, Bird, Fog, Debris, DarkPool, Piranha, FallenTree, HealingReward,
        ElectricEel, BearSwipe, SpinningNet
    }

    /// <summary>
    /// 장애물 하나. 종류별 크기·피해·기본 속도·수명은 프리팹(Prefabs/Hazards)에 저장되고,
    /// 스폰 위치에 따라 달라지는 값만 SalmonGame.CreateHazard가 덮어쓴다.
    /// </summary>
    public sealed class SalmonHazard : MonoBehaviour
    {
        /// <summary>쓰러진 나무 프리팹의 기준 폭 (1스테이지 강폭 7.1 × 2 − 0.25)</summary>
        public const float NominalFallenTreeWidth = 13.95f;

        public HazardKind Kind;
        public Vector2 Velocity;
        public float Radius = 0.7f;
        public Vector2 HalfExtents;
        public float Damage;
        public float Life = 20f;
        public float InitialLife = 20f;
        public float Phase;
        public bool Hit;
        public bool NearMissAwarded;
        public SpriteRenderer FogRenderer;

        public void Tick(float deltaTime, float scrollSpeed, Vector2 playerPosition)
        {
            Life -= deltaTime;
            var movement = Velocity;
            if (Kind == HazardKind.Piranha)
            {
                var direction = (playerPosition - (Vector2)transform.position).normalized;
                movement += direction * 2.6f;
                var angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
                transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(0f, 0f, angle), deltaTime * 6f);
                var bite = 1f + Mathf.Sin(Time.time * 13f + Phase) * 0.09f;
                transform.localScale = new Vector3(bite, 2f - bite, 1f);
            }
            else if (Kind == HazardKind.Bird)
            {
                movement.x += Mathf.Sin(Time.time * 4f + Phase) * 1.2f;
            }
            else if (Kind == HazardKind.FishSchool)
            {
                movement.x += Mathf.Sin(Time.time * 3f + Phase) * 0.8f;
            }
            else if (Kind == HazardKind.Whirlpool)
            {
                transform.Rotate(0f, 0f, -120f * deltaTime);
                transform.localScale = Vector3.one * (1f + Mathf.Sin(Time.time * 4.5f + Phase) * 0.12f);
            }
            else if (Kind == HazardKind.Seaweed)
            {
                transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 3.4f + Phase) * 10f);
                movement.x += Mathf.Sin(Time.time * 2.2f + Phase) * 0.22f;
            }
            else if (Kind == HazardKind.Branch)
            {
                transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 2.8f + Phase) * 7f);
                movement.x += Mathf.Sin(Time.time * 1.9f + Phase) * 0.32f;
            }
            else if (Kind == HazardKind.Leaf)
            {
                transform.Rotate(0f, 0f, 95f * deltaTime);
                movement.x += Mathf.Sin(Time.time * 3.8f + Phase) * 0.7f;
            }
            else if (Kind == HazardKind.Jellyfish)
            {
                var pulse = Mathf.Sin(Time.time * 6.5f + Phase);
                transform.localScale = new Vector3(1f + pulse * 0.15f, 1f - pulse * 0.1f, 1f);
                movement.x += Mathf.Sin(Time.time * 2.7f + Phase) * 0.48f;
            }
            else if (Kind == HazardKind.Boulder)
            {
                transform.Rotate(0f, 0f, 22f * deltaTime);
            }
            else if (Kind == HazardKind.Rapid)
            {
                transform.localScale = new Vector3(1f + Mathf.Sin(Time.time * 7f + Phase) * 0.18f, 1f, 1f);
            }
            else if (Kind == HazardKind.Log)
            {
                transform.Rotate(0f, 0f, 58f * deltaTime);
                movement.x += Mathf.Sin(Time.time * 2.5f + Phase) * 0.35f;
            }
            else if (Kind == HazardKind.Stone)
            {
                transform.Rotate(0f, 0f, 34f * deltaTime);
            }
            else if (Kind == HazardKind.DarkPool)
            {
                transform.localScale = Vector3.one * (1f + Mathf.Sin(Time.time * 3.7f + Phase) * 0.1f);
            }
            else if (Kind == HazardKind.Debris)
            {
                transform.Rotate(0f, 0f, 110f * deltaTime);
            }
            else if (Kind == HazardKind.HealingReward)
            {
                transform.Rotate(0f, 0f, 75f * deltaTime);
                transform.localScale = Vector3.one * (1f + Mathf.Sin(Time.time * 5f + Phase) * 0.08f);
            }
            else if (Kind == HazardKind.ElectricEel)
            {
                movement.x += Mathf.Sin(Time.time * 7.5f + Phase) * 2.7f;
                transform.localScale = Vector3.one * (1f + Mathf.Sin(Time.time * 12f + Phase) * 0.11f);
            }
            else if (Kind == HazardKind.BearSwipe)
            {
                transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 5f + Phase) * 12f);
            }
            else if (Kind == HazardKind.SpinningNet)
            {
                transform.Rotate(0f, 0f, 185f * deltaTime);
            }

            if (Kind == HazardKind.Fog && FogRenderer != null)
            {
                var elapsed = InitialLife - Life;
                var fade = Mathf.Min(Mathf.Clamp01(elapsed / 0.8f), Mathf.Clamp01(Life / 1.2f));
                var color = FogRenderer.color;
                color.a = 0.82f * fade;
                FogRenderer.color = color;
            }
            transform.position += (Vector3)((movement + Vector2.down * scrollSpeed) * deltaTime);
        }
    }
}
