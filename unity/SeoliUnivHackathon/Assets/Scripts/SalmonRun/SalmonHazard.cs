using UnityEngine;

namespace SalmonRun
{
    public enum HazardKind
    {
        Seaweed, Branch, Leaf, Jellyfish, Boulder, Rapid, Log, Whirlpool, FishSchool,
        Stone, Bird, Fog, Debris, DarkPool, Piranha
    }

    public sealed class SalmonHazard : MonoBehaviour
    {
        public HazardKind Kind;
        public Vector2 Velocity;
        public float Radius = 0.7f;
        public float Damage;
        public float Life = 20f;
        public float Phase;
        public bool Hit;

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
            }
            transform.position += (Vector3)((movement + Vector2.down * scrollSpeed) * deltaTime);
        }
    }
}
