using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// Unity Point Lights exported as runtime point lights (splashpack v24).
    ///
    /// A mesh set to Auto is lit at runtime when any exported light's range
    /// reaches its bounds. Point lights are then left out of that mesh's baked
    /// vertex colours, so they are not counted twice. Meshes that are not lit at
    /// runtime bake every light exactly as before.
    /// </summary>
    public static class PSXPointLightExporter
    {
        public const int MaxSceneLights = 16;   // MAX_SCENE_LIGHTS in lightmath.hh
        public const int MaxLightsPerMesh = 4;  // MAX_LIGHTS_PER_MESH in lightmath.hh

        /// <summary>
        /// Every Point Light on an active GameObject, disabled ones included so
        /// Lua can switch them on. Sorted by hierarchy path, which keeps the
        /// table order (and so which four win on a crowded mesh) stable between
        /// exports.
        /// </summary>
        public static Light[] Collect()
        {
            var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None)
                .Where(l => l.type == LightType.Point)
                .OrderBy(l => HierarchyPath(l.transform), System.StringComparer.Ordinal)
                .ToList();
            if (lights.Count > MaxSceneLights)
            {
                Debug.LogWarning($"[PSX lights] The scene has {lights.Count} Point Lights. The PS1 keeps " +
                                 $"the first {MaxSceneLights}; these are left out: " +
                                 string.Join(", ", lights.Skip(MaxSceneLights).Select(l => l.name)));
                lights.RemoveRange(MaxSceneLights, lights.Count - MaxSceneLights);
            }
            return lights.ToArray();
        }

        /// <summary>
        /// Decide which exporters are lit at runtime and set
        /// <see cref="PSXObjectExporter.IsDynamicLit"/>. Skinned-mesh proxies are
        /// never lit: the engine does not light skinned meshes.
        /// </summary>
        public static void Resolve(PSXObjectExporter[] exporters, Light[] lights, ICollection<PSXObjectExporter> skinnedProxies)
        {
            foreach (var exp in exporters)
            {
                exp.IsDynamicLit = false;
                if (lights.Length == 0) continue;
                if (skinnedProxies != null && skinnedProxies.Contains(exp)) continue;
                if (!TryGetWorldBounds(exp, out Bounds bounds)) continue;

                var touching = lights.Where(l => Reaches(l, bounds)).ToList();
                switch (exp.DynamicLighting)
                {
                    case PSXDynamicLighting.On: exp.IsDynamicLit = true; break;
                    case PSXDynamicLighting.Off: exp.IsDynamicLit = false; break;
                    default: exp.IsDynamicLit = touching.Count > 0; break;
                }

                if (exp.IsDynamicLit && touching.Count > MaxLightsPerMesh)
                {
                    Debug.LogWarning($"[PSX lights] {touching.Count} Point Lights reach '{exp.name}', but the PS1 " +
                                     $"lights a mesh with at most {MaxLightsPerMesh}. It will use " +
                                     string.Join(", ", touching.Take(MaxLightsPerMesh).Select(l => l.name)) +
                                     " and ignore " + string.Join(", ", touching.Skip(MaxLightsPerMesh).Select(l => l.name)) +
                                     ". Shrink a light's Range or split the mesh.", exp);
                }
            }
        }

        /// <summary>Does the light's range reach the box? Same test the engine runs.</summary>
        public static bool Reaches(Light light, Bounds bounds)
        {
            if (light.range <= 0f) return false;
            Vector3 closest = bounds.ClosestPoint(light.transform.position);
            return (closest - light.transform.position).sqrMagnitude < light.range * light.range;
        }

        /// <summary>
        /// World AABB of the exporter's mesh: its local bounds' eight corners
        /// through the transform, which is what the writer exports as the
        /// object's AABB.
        /// </summary>
        public static bool TryGetWorldBounds(PSXObjectExporter exp, out Bounds bounds)
        {
            bounds = default;
            Mesh mesh = exp.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null) return false;
            Vector3 c = mesh.bounds.center, e = mesh.bounds.extents;
            bool first = true;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c + new Vector3((i & 1) != 0 ? e.x : -e.x,
                                                 (i & 2) != 0 ? e.y : -e.y,
                                                 (i & 4) != 0 ? e.z : -e.z);
                Vector3 w = exp.transform.TransformPoint(corner);
                if (first) { bounds = new Bounds(w, Vector3.zero); first = false; }
                else bounds.Encapsulate(w);
            }
            return true;
        }

        private static string HierarchyPath(Transform t)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null; p = p.parent)
                path = p.name + "/" + path;
            return path + "#" + t.GetSiblingIndex();
        }
    }
}
