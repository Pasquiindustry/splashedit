using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// Unity Point Lights exported as runtime point lights (splashpack v24).
    ///
    /// Unity's own Light Mode decides which: Baked point lights go into vertex
    /// colours exactly as before and never run on the console; Realtime and
    /// Mixed ones are runtime lights. A mesh set to Auto is lit at runtime when
    /// a runtime light's range reaches its bounds, and runtime lights are then
    /// left out of that mesh's bake so they are not counted twice.
    /// </summary>
    public static class PSXPointLightExporter
    {
        public const int MaxSceneLights = 16;   // MAX_SCENE_LIGHTS in lightmath.hh
        public const int MaxLightsPerMesh = 4;  // MAX_LIGHTS_PER_MESH in lightmath.hh

        /// <summary>What export will do with one mesh. Shared by the exporter, the inspector and the gizmo.</summary>
        public struct MeshLighting
        {
            public bool HasMesh;
            public bool RuntimeLit;
            /// Runtime lights reaching the mesh, in the order the PS1 picks them.
            public List<Light> Reaching;
            /// Baked-mode Point Lights reaching the mesh (always baked).
            public List<Light> BakedReaching;

            public bool OverCap => RuntimeLit && Reaching.Count > MaxLightsPerMesh;
            public IEnumerable<Light> Kept => Reaching.Take(MaxLightsPerMesh);
            public IEnumerable<Light> Ignored => Reaching.Skip(MaxLightsPerMesh);
        }

        /// <summary>A Point Light whose Light Mode is Realtime or Mixed.</summary>
        public static bool IsRuntime(Light light)
        {
            if (light.type != LightType.Point) return false;
#if UNITY_EDITOR
            return light.lightmapBakeType != LightmapBakeType.Baked;
#else
            return true;
#endif
        }

        static bool IsBakedPoint(Light light) => light.type == LightType.Point && !IsRuntime(light);

        /// <summary>
        /// Every runtime Point Light on an active GameObject, disabled ones
        /// included so Lua can switch them on. Sorted by hierarchy path, which
        /// keeps the table order (and so which four win on a crowded mesh)
        /// stable between exports.
        /// </summary>
        public static Light[] Collect(bool logWarnings = true)
        {
            var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None)
                .Where(IsRuntime)
                .OrderBy(l => HierarchyPath(l.transform), System.StringComparer.Ordinal)
                .ToList();
            if (lights.Count > MaxSceneLights)
            {
                if (logWarnings)
                    Debug.LogWarning(SceneCapMessage(lights));
                lights.RemoveRange(MaxSceneLights, lights.Count - MaxSceneLights);
            }
            return lights.ToArray();
        }

        public static string SceneCapMessage(IList<Light> lights) =>
            $"[PSX lights] The scene has {lights.Count} Realtime/Mixed Point Lights and the PS1 holds " +
            $"{MaxSceneLights}, so these are left out: " +
            string.Join(", ", lights.Skip(MaxSceneLights).Select(l => l.name)) +
            ". To fix it, set the Mode of the lights that never change to Baked, or delete some.";

        /// <param name="includeBaked">Also list the Baked-mode Point Lights reaching it (a scene search).</param>
        public static MeshLighting Analyze(PSXObjectExporter exp, Light[] runtimeLights, bool includeBaked = false)
        {
            var result = new MeshLighting { Reaching = new List<Light>(), BakedReaching = new List<Light>() };
            if (!TryGetWorldBounds(exp, out Bounds bounds)) return result;
            result.HasMesh = true;
            result.Reaching.AddRange(runtimeLights.Where(l => l != null && Reaches(l, bounds)));
            if (includeBaked)
                result.BakedReaching.AddRange(Object.FindObjectsByType<Light>(FindObjectsSortMode.None)
                    .Where(l => l.enabled && IsBakedPoint(l) && Reaches(l, bounds)));
            if (runtimeLights.Any(l => l != null))
            {
                switch (exp.DynamicLighting)
                {
                    case PSXDynamicLighting.On:
                    case PSXDynamicLighting.OnSmooth: result.RuntimeLit = true; break;
                    case PSXDynamicLighting.Off: result.RuntimeLit = false; break;
                    default: result.RuntimeLit = result.Reaching.Count > 0; break;
                }
            }
            return result;
        }

        public static string MeshCapMessage(PSXObjectExporter exp, MeshLighting ml) =>
            $"[PSX lights] {ml.Reaching.Count} Realtime/Mixed Point Lights reach '{exp.name}', and the PS1 lights " +
            $"a mesh with {MaxLightsPerMesh}. It uses {string.Join(", ", ml.Kept.Select(l => l.name))} and ignores " +
            $"{string.Join(", ", ml.Ignored.Select(l => l.name))}. To fix it, lower a light's Range, set a light " +
            "that never moves to Mode Baked, or split the mesh so each piece is reached by four or fewer.";

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
                var ml = Analyze(exp, lights);
                exp.IsDynamicLit = ml.RuntimeLit;
                if (ml.OverCap)
                    Debug.LogWarning(MeshCapMessage(exp, ml), exp);
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

#if UNITY_EDITOR
        // The inspector and every exporter's gizmo ask for the scene's lights on
        // each repaint; one lookup per editor tick is plenty.
        static Light[] s_cached;
        static double s_cachedAt = -1;

        public static Light[] CollectCached()
        {
            double now = UnityEditor.EditorApplication.timeSinceStartup;
            if (s_cached == null || now - s_cachedAt > 0.25)
            {
                s_cached = Collect(logWarnings: false);
                s_cachedAt = now;
            }
            return s_cached;
        }
#endif

        private static string HierarchyPath(Transform t)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null; p = p.parent)
                path = p.name + "/" + path;
            return path + "#" + t.GetSiblingIndex();
        }
    }
}
