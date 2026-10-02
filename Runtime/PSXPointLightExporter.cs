using System.Collections.Generic;
using System.IO;
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

        // ------------------------------------------------------------------
        // Light tracks (cutscenes and animations)
        // ------------------------------------------------------------------

        /// <summary>Light index written for a light track whose light is not exported. The engine ignores it.</summary>
        public const byte NoTrackLight = 0xFF;

        /// <summary>The scene Light a light track names, active or not, or null.</summary>
        public static Light FindTrackLight(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Light found = null;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (l.gameObject.name != name) continue;
                if (l.type == LightType.Point) return l;
                if (found == null) found = l;
            }
            return found;
        }

        /// <summary>
        /// Why a light track cannot drive the light it names, as an instruction
        /// to fix it, or null when the light is exported as a runtime light.
        /// </summary>
        public static string TrackLightProblem(string name, Light[] exported)
        {
            if (string.IsNullOrEmpty(name))
                return "No light is picked. Choose a Realtime or Mixed Point Light in the track's properties.";
            if (exported != null && exported.Any(l => l != null && l.gameObject.name == name))
                return null;
            Light light = FindTrackLight(name);
            if (light == null)
                return $"There is no Light named '{name}' in the scene. Pick an existing Point Light, or rename the light back.";
            if (light.type != LightType.Point)
                return $"'{name}' is a {light.type} light and only Point Lights run on the PS1. Set its Type to Point.";
            if (!IsRuntime(light))
                return $"'{name}' is a Baked light, so it is baked into vertex colours and cannot change at runtime. Set its Mode to Realtime or Mixed.";
            if (!light.gameObject.activeInHierarchy)
                return $"'{name}' is on an inactive GameObject, so it is not exported. Keep the GameObject active and switch the light off with its Light component (or a Light Enabled keyframe) instead.";
            return $"'{name}' is not exported because the scene has more than {MaxSceneLights} Realtime/Mixed Point Lights. Set the lights that never change to Baked.";
        }

        /// <summary>
        /// Index in <paramref name="exported"/> of the light a light track
        /// drives, or <see cref="NoTrackLight"/> after a warning saying what to fix.
        /// </summary>
        public static byte ResolveTrackLight(PSXCutsceneTrack track, Light[] exported, string clipLabel,
                                             float gteScaling, System.Action<string, LogType> log)
        {
            string where = $"{clipLabel}, {track.TrackType} track";
            string problem = TrackLightProblem(track.ObjectName, exported);
            if (problem != null)
            {
                log?.Invoke($"{where}: {problem} The track is skipped.", LogType.Warning);
                return NoTrackLight;
            }
            int index = System.Array.FindIndex(exported, l => l != null && l.gameObject.name == track.ObjectName);
            if (exported.Count(l => l != null && l.gameObject.name == track.ObjectName) > 1)
                log?.Invoke($"{where}: more than one runtime light is named '{track.ObjectName}', and the track drives " +
                            $"the first. Give the lights unique names.", LogType.Warning);

            // Positions and range travel as 4.12 int16, so they stop at 8 GTE units.
            if ((track.TrackType == PSXTrackType.LightPosition || track.TrackType == PSXTrackType.LightRadius) &&
                track.Keyframes != null)
            {
                float limit = 32767f / 4096f * gteScaling;
                foreach (var kf in track.Keyframes)
                {
                    bool over = track.TrackType == PSXTrackType.LightRadius
                        ? kf.Value.x > limit
                        : Mathf.Abs(kf.Value.x) > limit || Mathf.Abs(kf.Value.y) > limit || Mathf.Abs(kf.Value.z) > limit;
                    if (!over) continue;
                    log?.Invoke($"{where}: the keyframe at frame {kf.Frame} is beyond {limit:0.##} units, the most a " +
                                $"track can hold at GTE scaling {gteScaling}, and is clamped. Raise the GTE scaling or " +
                                "keep the light closer to the origin.", LogType.Warning);
                    break;
                }
            }
            return (byte)index;
        }

        /// <summary>A light track keyframe's three int16 values, in the units the engine applies.</summary>
        public static void WriteTrackKeyframe(BinaryWriter writer, PSXTrackType type, Vector3 v, float gteScaling)
        {
            short a = 0, b = 0, c = 0;
            switch (type)
            {
                case PSXTrackType.LightPosition:
                    a = PSXTrig.ConvertCoordinateToPSX(v.x, gteScaling);
                    b = PSXTrig.ConvertCoordinateToPSX(-v.y, gteScaling);
                    c = PSXTrig.ConvertCoordinateToPSX(v.z, gteScaling);
                    break;
                case PSXTrackType.LightColor:
                    a = Utils.ColorUnityToPSX(v.x);
                    b = Utils.ColorUnityToPSX(v.y);
                    c = Utils.ColorUnityToPSX(v.z);
                    break;
                case PSXTrackType.LightIntensity:
                    a = (short)Mathf.Clamp(Mathf.RoundToInt(v.x * 4096f), 0, 32767);
                    break;
                case PSXTrackType.LightRadius:
                    a = (short)Mathf.Max(0, (int)PSXTrig.ConvertCoordinateToPSX(v.x, gteScaling));
                    break;
                case PSXTrackType.LightEnabled:
                    a = (short)(v.x > 0.5f ? 1 : 0);
                    break;
            }
            writer.Write(a);
            writer.Write(b);
            writer.Write(c);
        }

        /// <summary>The light's current value for a light track, as a keyframe holds it.</summary>
        public static Vector3 CaptureTrackValue(Light light, PSXTrackType type)
        {
            switch (type)
            {
                case PSXTrackType.LightPosition: return light.transform.position;
                case PSXTrackType.LightColor: return new Vector3(light.color.r, light.color.g, light.color.b);
                case PSXTrackType.LightIntensity: return new Vector3(light.intensity, 0, 0);
                case PSXTrackType.LightRadius: return new Vector3(light.range, 0, 0);
                case PSXTrackType.LightEnabled: return new Vector3(light.enabled ? 1f : 0f, 0, 0);
                default: return Vector3.zero;
            }
        }

        /// <summary>Set a keyframe value on the light, for the timeline preview.</summary>
        public static void ApplyTrackValue(Light light, PSXTrackType type, Vector3 v)
        {
            switch (type)
            {
                case PSXTrackType.LightPosition: light.transform.position = v; break;
                case PSXTrackType.LightColor: light.color = new Color(v.x, v.y, v.z); break;
                case PSXTrackType.LightIntensity: light.intensity = Mathf.Max(0f, v.x); break;
                case PSXTrackType.LightRadius: light.range = Mathf.Max(0f, v.x); break;
                case PSXTrackType.LightEnabled: light.enabled = v.x > 0.5f; break;
            }
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
