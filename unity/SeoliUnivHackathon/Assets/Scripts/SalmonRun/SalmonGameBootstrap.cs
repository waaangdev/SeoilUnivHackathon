using UnityEngine;

namespace SalmonRun
{
    public static class SalmonGameBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateGame()
        {
            if (Object.FindAnyObjectByType<SalmonGame>() != null)
            {
                return;
            }

            var root = new GameObject("Salmon Run - Runtime Game");
            root.AddComponent<SalmonGame>();
        }
    }
}
