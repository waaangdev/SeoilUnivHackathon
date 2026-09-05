using UnityEngine;

namespace SalmonRun
{
    public static class SalmonVisuals
    {
        private static Sprite whiteSprite;

        public static Sprite WhiteSprite
        {
            get
            {
                if (whiteSprite != null) return whiteSprite;
                var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                {
                    name = "Runtime White Pixel",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                texture.SetPixel(0, 0, Color.white);
                texture.Apply();
                whiteSprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
                return whiteSprite;
            }
        }

        public static GameObject Rect(string name, Transform parent, Vector2 position, Vector2 size,
            Color color, int order = 0)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(position.x, position.y, 0f);
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = WhiteSprite;
            renderer.color = color;
            renderer.sortingOrder = order;
            return go;
        }

        public static GameObject Circle(string name, Transform parent, Vector2 position, Vector2 size,
            Color color, int order = 0)
        {
            var go = Rect(name, parent, position, size, color, order);
            go.GetComponent<SpriteRenderer>().sprite = MakeCircleSprite();
            return go;
        }

        public static GameObject MakeSalmon(Transform parent)
        {
            var root = new GameObject("Player Salmon");
            root.transform.SetParent(parent, false);

            Circle("Body", root.transform, Vector2.zero, new Vector2(1.15f, 1.75f),
                new Color(1f, 0.36f, 0.30f), 20);
            Circle("Belly", root.transform, new Vector2(0f, -0.15f), new Vector2(0.65f, 1.15f),
                new Color(1f, 0.68f, 0.52f), 21);
            var tail = Rect("Tail", root.transform, new Vector2(0f, -1f), new Vector2(0.72f, 0.72f),
                new Color(0.92f, 0.23f, 0.25f), 19);
            tail.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            Circle("Left Eye", root.transform, new Vector2(-0.25f, 0.52f), Vector2.one * 0.16f, Color.white, 22);
            Circle("Right Eye", root.transform, new Vector2(0.25f, 0.52f), Vector2.one * 0.16f, Color.white, 22);
            Circle("Left Pupil", root.transform, new Vector2(-0.25f, 0.55f), Vector2.one * 0.07f, new Color(0.06f, 0.09f, 0.14f), 23);
            Circle("Right Pupil", root.transform, new Vector2(0.25f, 0.55f), Vector2.one * 0.07f, new Color(0.06f, 0.09f, 0.14f), 23);
            return root;
        }

        private static Sprite circleSprite;
        private static Sprite fogSprite;
        private static Texture2D fogTexture;

        public static Texture2D FogTexture
        {
            get
            {
                if (fogTexture == null) _ = FogSprite;
                return fogTexture;
            }
        }

        public static Sprite FogSprite
        {
            get
            {
                if (fogSprite != null) return fogSprite;
                const int width = 192;
                const int height = 128;
                fogTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
                {
                    name = "Runtime Layered Fog",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                var pixels = new Color[width * height];
                for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var u = x / (float)width;
                    var v = y / (float)height;
                    var broad = Mathf.PerlinNoise(u * 3.2f + 4.1f, v * 3.2f + 7.6f);
                    var detail = Mathf.PerlinNoise(u * 8.5f + 12.7f, v * 8.5f + 2.3f);
                    var wisps = Mathf.SmoothStep(0.2f, 0.9f, broad * 0.72f + detail * 0.28f);
                    var edgeFade = Mathf.SmoothStep(0f, 0.18f, v) * Mathf.SmoothStep(0f, 0.18f, 1f - v);
                    var alpha = Mathf.Lerp(0.58f, 1f, wisps) * edgeFade;
                    pixels[y * width + x] = new Color(0.83f, 0.88f, 0.91f, alpha);
                }
                fogTexture.SetPixels(pixels);
                fogTexture.Apply();
                fogSprite = Sprite.Create(fogTexture, new Rect(0, 0, width, height), Vector2.one * 0.5f, 16f);
                return fogSprite;
            }
        }

        private static Sprite MakeCircleSprite()
        {
            if (circleSprite != null) return circleSprite;
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Runtime Soft Circle",
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var dx = (x + 0.5f) / size * 2f - 1f;
                var dy = (y + 0.5f) / size * 2f - 1f;
                var distance = Mathf.Sqrt(dx * dx + dy * dy);
                pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01((1f - distance) * 8f));
            }
            texture.SetPixels(pixels);
            texture.Apply();
            circleSprite = Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.one * 0.5f, size);
            return circleSprite;
        }
    }
}
