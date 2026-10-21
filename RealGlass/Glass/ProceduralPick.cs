using UnityEngine;

namespace RealGlass.Glass
{
    /// <summary>
    /// The geometry of picking a building of Procedural Objects with the mouse (ProceduralBuildings.
    /// Raycast, for PerAsset.AssetPicker): the ray against the building's box first -- the slab test
    /// (Kay and Kajiya, "Ray Tracing Complex Scenes", SIGGRAPH 1986) -- then against the triangles of
    /// the boxes it meets, nearest first (Moller and Trumbore, "Fast, Minimum Storage Ray/Triangle
    /// Intersection", Journal of Graphics Tools 2(1), 1997). Plain arithmetic on the vectors' fields,
    /// no engine call: tools/test-settings.ps1 runs it offline.
    /// </summary>
    public static class ProceduralPick
    {
        /// <summary>
        /// Where the ray o + t d (t &gt;= 0) enters the box min..max: 0 when it starts inside, -1 when it
        /// misses the box.
        /// </summary>
        public static float RayBox(Vector3 o, Vector3 d, Vector3 min, Vector3 max)
        {
            float near = 0f, far = float.MaxValue;
            if (!Slab(o.x, d.x, min.x, max.x, ref near, ref far) || !Slab(o.y, d.y, min.y, max.y, ref near, ref far) ||
                !Slab(o.z, d.z, min.z, max.z, ref near, ref far))
            {
                return -1f;
            }

            return near;
        }

        private static bool Slab(float o, float d, float lo, float hi, ref float near, ref float far)
        {
            if (d > -1e-9f && d < 1e-9f)
                return o >= lo && o <= hi;

            float t0 = (lo - o) / d, t1 = (hi - o) / d;
            if (t0 > t1)
            {
                float swap = t0;
                t0 = t1;
                t1 = swap;
            }

            if (t0 > near)
                near = t0;
            if (t1 < far)
                far = t1;
            return near <= far;
        }

        /// <summary>
        /// The nearest triangle the ray o + t d meets with 0 &lt;= t &lt; maxT, whichever way it faces (a
        /// building's faces can be turned in Procedural Objects); -1 when none. tri: three indices into
        /// v a triangle, as Mesh.triangles.
        /// </summary>
        public static float RayTriangles(Vector3 o, Vector3 d, Vector3[] v, int[] tri, float maxT)
        {
            float best = maxT;
            bool hit = false;
            int n = tri.Length - 2;
            for (int k = 0; k < n; k += 3)
            {
                Vector3 a = v[tri[k]], b = v[tri[k + 1]], c = v[tri[k + 2]];
                float e1x = b.x - a.x, e1y = b.y - a.y, e1z = b.z - a.z;
                float e2x = c.x - a.x, e2y = c.y - a.y, e2z = c.z - a.z;

                // p = d x e2; det = e1 . p
                float px = d.y * e2z - d.z * e2y, py = d.z * e2x - d.x * e2z, pz = d.x * e2y - d.y * e2x;
                float det = e1x * px + e1y * py + e1z * pz;
                if (det > -1e-12f && det < 1e-12f)
                    continue;   // the ray runs along the triangle's plane

                float inv = 1f / det;
                float sx = o.x - a.x, sy = o.y - a.y, sz = o.z - a.z;
                float u = (sx * px + sy * py + sz * pz) * inv;
                if (u < 0f || u > 1f)
                    continue;

                // q = s x e1
                float qx = sy * e1z - sz * e1y, qy = sz * e1x - sx * e1z, qz = sx * e1y - sy * e1x;
                float w = (d.x * qx + d.y * qy + d.z * qz) * inv;
                if (w < 0f || u + w > 1f)
                    continue;

                float t = (e2x * qx + e2y * qy + e2z * qz) * inv;
                if (t >= 0f && t < best)
                {
                    best = t;
                    hit = true;
                }
            }

            return hit ? best : -1f;
        }
    }
}
