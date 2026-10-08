using UnityEngine;

namespace VolumeRendering.Runtime
{
    // Računa gradient magnitude direktno iz float gustoća sliceova — puna preciznost,
    // prije 8-bit kvantizacije u Color32. Isti algoritam i ×10 amplifikacija kao u
    // Texture2DArrayToTexture3DConverterEditor.
    // Poziva se iz background threada — čisti CPU rad, thread-safe.
    public static class GradientBaker
    {
        // Central differences nad susjednim voxelima, clampano na rubovima volumena.
        public static float Compute(float[][] density, int x, int y, int z, int w, int h, int d)
        {
            float dx = Sample(density, x + 1, y, z, w, h, d) - Sample(density, x - 1, y, z, w, h, d);
            float dy = Sample(density, x, y + 1, z, w, h, d) - Sample(density, x, y - 1, z, w, h, d);
            float dz = Sample(density, x, y, z + 1, w, h, d) - Sample(density, x, y, z - 1, w, h, d);

            // *10f amplificira male CT razlike (~0.01–0.05) na koristan raspon
            return Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy + dz * dz) * 10f);
        }

        static float Sample(float[][] density, int x, int y, int z, int w, int h, int d)
        {
            x = Mathf.Clamp(x, 0, w - 1);
            y = Mathf.Clamp(y, 0, h - 1);
            z = Mathf.Clamp(z, 0, d - 1);
            return density[z][y * w + x];
        }
    }
}
