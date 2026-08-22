using UnityEngine;

namespace LowPolyTerrain
{
    /// <summary>
    /// Procedural textures for the lava world: the flat ground colours the terrain layers use, and
    /// the starfield that goes in the skybox.
    ///
    /// Ground layers are generated rather than borrowed from a texture pack on purpose. A
    /// photographed ground texture fights faceted geometry - it puts high-frequency detail on a
    /// surface whose whole point is large flat faces. Flat colour with a trace of low-frequency
    /// variation is what the look actually wants, and generating it means the palette can be art
    /// directed exactly rather than whatever the pack happened to ship.
    ///
    /// Pure colour maths - no scene objects, no asset loading - so both run headlessly.
    /// </summary>
    public static class LowPolyTerrainTextures
    {
        /// <summary>
        /// A near-flat ground colour with a trace of large-scale mottling, so it reads as a surface
        /// rather than a fill without ever competing with the facets.
        /// </summary>
        public static Color[] FlatGround(Color baseColor, int size, int seed, float variation, float mottleScale)
        {
            var pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Sample on a torus so the texture tiles without a seam.
                    float u = (float)x / size, v = (float)y / size;
                    float n = TilingNoise(u, v, mottleScale, seed);

                    float k = 1f + (n * 2f - 1f) * variation;
                    Color c = baseColor * k;
                    c.a = 1f;
                    pixels[y * size + x] = c;
                }
            }

            return pixels;
        }

        /// <summary>
        /// Cooling lava crust: hot rock veined with darker crust, so the molten layer reads as a
        /// surface rather than a slab of orange.
        ///
        /// Deliberately fine-grained and low contrast. A tiling texture repeats across a wide lava
        /// field however large you set the tile, so the only thing that decides whether the repeat
        /// is visible is how distinctive one tile looks. Big high-contrast plates stamp a recognisable
        /// blob on a grid; small, low-contrast veins read as noise and the eye never locks on.
        /// It also has to stay quiet enough not to compete with the facets, which are what the
        /// low-poly look is actually made of.
        /// </summary>
        public static Color[] MoltenCrust(Color hot, Color crust, int size, int seed, float crustAmount)
        {
            var pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size, v = (float)y / size;

                    // Many small plates rather than a few big ones - the single biggest factor in
                    // whether the tiling shows.
                    float plates = TilingNoise(u, v, 13f, seed);
                    float fine = TilingNoise(u, v, 31f, seed + 733);

                    float crustMask = Mathf.Clamp01((plates - 0.5f) * 2.1f + 0.5f);
                    float vein = 1f - Mathf.Clamp01(Mathf.Abs(plates - 0.5f) * 7f);

                    float t = Mathf.Clamp01(crustMask * crustAmount - vein * 0.35f);
                    Color c = Color.Lerp(hot, crust, t);

                    c *= 0.95f + fine * 0.10f;
                    c.a = 1f;
                    pixels[y * size + x] = c;
                }
            }

            return pixels;
        }

        /// <summary>
        /// An equirectangular night sky: graded background, a faint galactic band, and stars drawn
        /// with a soft falloff so they survive mip-mapping instead of twinkling into aliasing.
        ///
        /// Stars are sampled uniformly <b>on the sphere</b>, not uniformly in the image. Sampling in
        /// image space would crowd them at the poles, because an equirectangular row near a pole
        /// covers far less sky than one at the equator.
        /// </summary>
        public static Color[] Starfield(
            int width, int height, int seed,
            int starCount,
            Color zenith, Color horizon, Color emberGlow,
            float galaxyStrength)
        {
            var acc = new Vector3[width * height];
            var rng = new Rng(seed);

            // --- background ------------------------------------------------------------------
            for (int y = 0; y < height; y++)
            {
                float lat = ((float)y / (height - 1) - 0.5f) * Mathf.PI;   // -pi/2 .. +pi/2
                float up = Mathf.Sin(lat);                                  // -1 at nadir, +1 at zenith

                // Sky darkens with altitude; the ember glow is what a caldera does to a night
                // horizon, and it only lives in the bottom half of the sky.
                Color bg = Color.Lerp(horizon, zenith, Mathf.Clamp01(up * 1.15f));
                float ember = Mathf.Clamp01(1f - Mathf.Abs(up) * 4.5f) * Mathf.Clamp01(1f - up * 2.2f);
                bg += emberGlow * ember;

                for (int x = 0; x < width; x++)
                {
                    float lon = ((float)x / width) * Mathf.PI * 2f;

                    float g = 0f;
                    if (galaxyStrength > 0f)
                    {
                        // A great circle tilted off the horizon, thickened with noise.
                        Vector3 d = Direction(lat, lon);
                        Vector3 n = new Vector3(0.34f, 0.87f, -0.36f).normalized;
                        float band = Mathf.Exp(-Mathf.Pow(Vector3.Dot(d, n) / 0.20f, 2f));
                        float clump = TilingNoise((float)x / width, (float)y / height, 9f, seed + 4231);
                        g = band * (0.45f + clump * 0.9f) * galaxyStrength;
                    }

                    Color c = bg + new Color(0.16f, 0.15f, 0.26f) * g;
                    acc[y * width + x] = new Vector3(c.r, c.g, c.b);
                }
            }

            // --- stars -----------------------------------------------------------------------
            for (int i = 0; i < starCount; i++)
            {
                // Uniform on the sphere: latitude from asin of a uniform, not a uniform angle.
                float lat = Mathf.Asin(rng.Range(-1f, 1f));
                float lon = rng.Range(0f, Mathf.PI * 2f);

                float fx = (lon / (Mathf.PI * 2f)) * width;
                float fy = (lat / Mathf.PI + 0.5f) * (height - 1);

                // Power law: mostly faint stars, a handful of bright ones.
                float u = rng.Value();
                float bright = 0.14f + Mathf.Pow(u, 4.5f) * 1.5f;

                // Slight colour spread between cool and warm stars.
                float warm = rng.Value();
                var tint = new Vector3(
                    Mathf.Lerp(0.78f, 1f, warm),
                    Mathf.Lerp(0.86f, 0.93f, warm),
                    Mathf.Lerp(1f, 0.76f, warm));

                float radius = 0.6f + Mathf.Pow(u, 6f) * 2.6f;

                // Rows near a pole are horizontally compressed, so stretch the stamp in x to keep
                // the star round on the sphere. Clamped, or a pole star would smear across the row.
                float stretch = Mathf.Min(6f, 1f / Mathf.Max(0.04f, Mathf.Cos(lat)));
                float rx = radius * stretch;

                int x0 = Mathf.FloorToInt(fx - rx) - 1, x1 = Mathf.CeilToInt(fx + rx) + 1;
                int y0 = Mathf.Max(0, Mathf.FloorToInt(fy - radius) - 1);
                int y1 = Mathf.Min(height - 1, Mathf.CeilToInt(fy + radius) + 1);

                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = (x - fx) / rx;
                        float dy = (y - fy) / radius;
                        float d2 = dx * dx + dy * dy;
                        if (d2 > 9f) continue;

                        float fall = Mathf.Exp(-d2 * 1.6f);

                        // Wrap in longitude so stars crossing the seam are not clipped.
                        int wx = x % width;
                        if (wx < 0) wx += width;

                        acc[y * width + wx] += tint * (bright * fall);
                    }
                }
            }

            var pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                Vector3 v = acc[i];
                pixels[i] = new Color(v.x, v.y, v.z, 1f);
            }

            return pixels;
        }

        /// <summary>
        /// An equirectangular daytime sky: a graded blue dome, threshold cumulus, and a sun disc
        /// with a bloom around it.
        ///
        /// <b>The sun is painted where the directional light actually points, and getting there
        /// needs the longitude run backwards.</b> Skybox/Panoramic samples a latlong map as
        /// <c>u = 0.5 - atan2(z, x) / 2pi</c>, not the obvious <c>u = atan2(z, x) / 2pi</c>. The
        /// difference between those two is a MIRROR, not an offset, so it cannot be corrected with
        /// the material's _Rotation however carefully it is tuned - painting in the shader's own
        /// convention here is the only fix. Get it wrong and the sun renders on the opposite side
        /// of the sky from the light, which looks exactly like a broken skybox.
        ///
        /// Azimuth is measured from +X turning toward +Z, matching <c>atan2(z, x)</c>. That is NOT
        /// a Unity yaw - see FarmWorldSetup.SunAzimuth for the conversion.
        /// </summary>
        public static Color[] DaySky(
            int width, int height, int seed,
            Color zenith, Color horizon, Color sunColor,
            float sunAltitudeDegrees, float sunAzimuthDegrees,
            float cloudCover, float cloudSharpness)
        {
            var pixels = new Color[width * height];

            Vector3 sun = Direction(
                sunAltitudeDegrees * Mathf.Deg2Rad,
                sunAzimuthDegrees * Mathf.Deg2Rad);

            for (int y = 0; y < height; y++)
            {
                float lat = ((float)y / (height - 1) - 0.5f) * Mathf.PI;
                float up = Mathf.Sin(lat);

                // Biased toward the horizon colour: a linear ramp puts mid-blue at 45 degrees,
                // which is far higher than a real sky turns over and reads as a painted dome.
                float t = Mathf.Clamp01(Mathf.Pow(Mathf.Clamp01(up), 0.55f));
                Color bg = Color.Lerp(horizon, zenith, t);

                for (int x = 0; x < width; x++)
                {
                    float lon = (0.5f - (float)x / width) * Mathf.PI * 2f;
                    Vector3 dir = Direction(lat, lon);

                    Color c = bg;

                    if (cloudCover > 0f && up > -0.05f)
                    {
                        // Threshold the noise so there is clear sky between clouds instead of a
                        // uniform haze, then square the edge up with the sharpness.
                        float n = TilingNoise((float)x / width * 2f, (float)y / height * 2f, 6f, seed + 1777);
                        float cut = 1f - cloudCover;
                        float amount = Mathf.Clamp01((n - cut) / Mathf.Max(1e-3f, 1f - cut));
                        amount = Mathf.Pow(amount, Mathf.Max(0.1f, cloudSharpness) * 0.5f);

                        // Fade them out toward the horizon, where a flat noise field would tile
                        // into visible stripes as the projection squeezes it.
                        amount *= Mathf.Clamp01(up * 5f);

                        if (amount > 0f)
                        {
                            // Lit on top, shaded underneath, so a cloud reads as a solid rather
                            // than as a white smear.
                            float lit = Mathf.Clamp01(0.55f + Vector3.Dot(dir, sun) * 0.45f);
                            Color cloud = Color.Lerp(
                                new Color(0.62f, 0.65f, 0.72f), Color.white, lit);
                            c = Color.Lerp(c, cloud, amount * 0.92f);
                        }
                    }

                    float angle = Vector3.Angle(dir, sun);

                    // Bloom first, then the disc on top of it.
                    c += sunColor * (Mathf.Exp(-angle / 16f) * 0.55f);
                    c += sunColor * (1f - SmoothStep(1.6f, 2.6f, angle)) * 1.4f;

                    c.a = 1f;
                    pixels[y * width + x] = c;
                }
            }

            return pixels;
        }

        static float SmoothStep(float edge0, float edge1, float v)
        {
            if (edge1 - edge0 < 1e-6f) return v < edge0 ? 0f : 1f;
            float t = Mathf.Clamp01((v - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        static Vector3 Direction(float lat, float lon)
        {
            float cl = Mathf.Cos(lat);
            return new Vector3(cl * Mathf.Cos(lon), Mathf.Sin(lat), cl * Mathf.Sin(lon));
        }

        /// <summary>
        /// Value noise on a torus, so the result tiles seamlessly in both axes. Built by blending
        /// the four wrapped corners rather than by sampling a plane, which would seam.
        /// </summary>
        static float TilingNoise(float u, float v, float scale, int seed)
        {
            float sum = 0f, amp = 1f, norm = 0f, s = scale;

            for (int o = 0; o < 3; o++)
            {
                int period = Mathf.Max(1, Mathf.RoundToInt(s));
                sum += WrappedValue(u * period, v * period, period, seed + o * 5171) * amp;
                norm += amp;
                amp *= 0.5f;
                s *= 2f;
            }

            return norm > 0f ? sum / norm : 0f;
        }

        static float WrappedValue(float x, float y, int period, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float xf = x - xi, yf = y - yi;
            xf = xf * xf * (3f - 2f * xf);
            yf = yf * yf * (3f - 2f * yf);

            float v00 = TerrainNoise.Hash(Wrap(xi, period), Wrap(yi, period), seed);
            float v10 = TerrainNoise.Hash(Wrap(xi + 1, period), Wrap(yi, period), seed);
            float v01 = TerrainNoise.Hash(Wrap(xi, period), Wrap(yi + 1, period), seed);
            float v11 = TerrainNoise.Hash(Wrap(xi + 1, period), Wrap(yi + 1, period), seed);

            return Mathf.Lerp(Mathf.Lerp(v00, v10, xf), Mathf.Lerp(v01, v11, xf), yf);
        }

        static int Wrap(int v, int period)
        {
            int m = v % period;
            return m < 0 ? m + period : m;
        }
    }
}
