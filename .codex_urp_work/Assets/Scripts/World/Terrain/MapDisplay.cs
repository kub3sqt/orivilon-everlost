using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Orivilon.World.Terrain
{
    /// <summary>
    /// Pomocná třída pro vizualizaci noise mapy jako textury na Renderer komponentu.
    /// Primárně určena pro ladění v Unity editoru – umožňuje vizuálně zkontrolovat
    /// tvar a distribuci noise mapy před aplikací na terén.
    /// Texture se cachuje a přealokuje pouze při změně rozlišení.
    /// </summary>
    public class MapDisplay : MonoBehaviour
    {
        /// <summary>Renderer komponenta, na jejíž materiál se aplikuje vygenerovaná textura.</summary>
        public Renderer textureRenderer;

        /// <summary>Cachovaná textura pro vyhnutí se opakované alokaci při stejném rozlišení.</summary>
        private Texture2D cachedTexture;

        /// <summary>
        /// Převede 2D pole noise hodnot (0–1) na černobílou texturu a aplikuje ji na Renderer.
        /// Hodnota 0 = černá, hodnota 1 = bílá. Použije FilterMode.Point pro ostré hrany pixelů.
        /// Také nastaví localScale rendereru na rozměry textury pro správné zobrazení.
        /// </summary>
        /// <param name="noiseMap">2D pole noise hodnot v rozsahu 0–1.</param>
        public void DrawNoiseMap(float[,] noiseMap)
        {
            int width = noiseMap.GetLength(0);
            int height = noiseMap.GetLength(1);

            if (cachedTexture == null || cachedTexture.width != width || cachedTexture.height != height)
            {
                cachedTexture = new Texture2D(width, height);
                cachedTexture.filterMode = FilterMode.Point;
            }

            Color[] colorMap = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    colorMap[y * width + x] = Color.Lerp(Color.black, Color.white, noiseMap[x, y]);
                }
            }

            cachedTexture.SetPixels(colorMap);
            cachedTexture.Apply();

            textureRenderer.sharedMaterial.mainTexture = cachedTexture;
            textureRenderer.transform.localScale = new Vector3(width, 1, height);
        }
    }
}