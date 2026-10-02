using System;
using System.Collections.Generic;
using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// The result of splitting a scene's static geometry into streamed regions.
    /// Built by <see cref="PSXWorldStreamPlanner"/>; the writer turns it into the
    /// stream table and the .geo file, and the scene view preview draws it.
    /// </summary>
    public class PSXWorldStreamPlan
    {
        public class Region
        {
            public int MinX, MinY, MinZ, MaxX, MaxY, MaxZ;  // fp12, PS1 space like the GameObject AABB
            public readonly List<int> Objects = new List<int>();  // indices into the planner input
            public int TriBytes;                 // before sector padding
            public uint FirstSector;
            public uint ByteSize;                // multiple of 2048
        }

        public readonly List<Region> Regions = new List<Region>();
        public readonly HashSet<int> Streamed = new HashSet<int>();
        public int[] TriCounts;                  // per input object
        public uint SlotBytes;
        public int SlotCount;
        public int LoadRadius;    // fp12
        public int UnloadRadius;  // fp12

        /// <summary>Plain-language problems, each saying what to do about it.</summary>
        public readonly List<string> Warnings = new List<string>();
        public PSXWorldStreamStats Stats = new PSXWorldStreamStats();
    }

    /// <summary>Plain numbers for the inspector and the control panel.</summary>
    public class PSXWorldStreamStats
    {
        public int RegionCount;
        public int StreamedObjects;
        public int ResidentObjects;
        public long StreamedTriangleBytes;   // what would sit in RAM without streaming
        public int SlotCount;
        public long SlotBytes;
        public long PoolBytes => SlotCount * SlotBytes;
        public long TableBytes;
        public float RegionSize;             // Unity units
        public float LoadDistance;           // Unity units
        public long RamSaved => StreamedTriangleBytes - PoolBytes - TableBytes;
    }

    /// <summary>
    /// Everything the planner looks at. Filled from the export's SceneData for
    /// the real export, and from the open scene for the editor preview, so both
    /// run the same partition.
    /// </summary>
    public class PSXWorldStreamInputs
    {
        public PSXObjectExporter[] Exporters;
        public int[] TriCounts;          // per exporter
        public int[][] Aabbs;            // per exporter, fp12 (minX, minY, minZ, maxX, maxY, maxZ), null if none
        public HashSet<PSXObjectExporter> Skinned = new HashSet<PSXObjectExporter>();
        public PSXInteractable[] Interactables;
        public PSXAgent[] Agents;
        public PSXTriggerBox[] TriggerBoxes;
        public PSXCutsceneClip[] Cutscenes;
        public PSXAnimationClip[] Animations;
        public LuaFile SceneLuaFile;
        public float GteScaling = 100f;
        public bool FogEnabled;
        public int FogDensity = 5;
        public float RegionSize;         // Unity units, 0 = automatic
        public float LoadDistance;       // Unity units, 0 = automatic
    }

    /// <summary>
    /// Decides which objects stream and how. Everything is automatic: the
    /// designer only ticks "Stream World Geometry" on the scene exporter.
    /// </summary>
    public static class PSXWorldStreamPlanner
    {
        public const int BytesPerTri = 52;
        public const int SectorSize = 2048;
        public const int TargetRegionBytes = 64 * 1024;
        public const int LargeRegionBytes = 256 * 1024;
        public const int MaxSlots = 32;  // StreamPlanner::kMaxResident

        public const int TableHeaderBytes = 20;
        public const int RegionRecordBytes = 32;
        public const int ObjectRefBytes = 8;

        /// <summary>
        /// Ordering table size the engine is built with (SplashSettings.OtSize).
        /// Nothing at a depth past it is drawn, so with fog off it is the draw
        /// distance. The editor assembly points this at the real setting.
        /// </summary>
        public static Func<int> OrderingTableSize = () => 2048 * 4;

        public static PSXWorldStreamPlan Build(PSXWorldStreamInputs inp)
        {
            float gte = inp.GteScaling;
            PSXObjectExporter[] exporters = inp.Exporters;
            bool[] eligible = FindEligible(inp);

            var plan = new PSXWorldStreamPlan { TriCounts = inp.TriCounts };
            int eligibleCount = 0;
            long eligibleBytes = 0;
            int bMinX = int.MaxValue, bMinZ = int.MaxValue, bMaxX = int.MinValue, bMaxZ = int.MinValue;
            for (int i = 0; i < exporters.Length; i++)
            {
                if (!eligible[i]) continue;
                eligibleCount++;
                eligibleBytes += (long)inp.TriCounts[i] * BytesPerTri;
                int[] a = inp.Aabbs[i];
                bMinX = Math.Min(bMinX, a[0]); bMinZ = Math.Min(bMinZ, a[2]);
                bMaxX = Math.Max(bMaxX, a[3]); bMaxZ = Math.Max(bMaxZ, a[5]);
            }
            if (eligibleCount == 0)
            {
                plan.Warnings.Add("Nothing in this scene can stream: every object has a script, an animation or " +
                                  "cutscene track, an interaction or skinning, so all of it stays loaded. Untick " +
                                  "\"Stream World Geometry\", or take the script off scenery that does not need one.");
                return plan;
            }

            // Radii. Nothing farther than the fog wall, or past the ordering table
            // with fog off, is drawn, so a region has to be in memory by then. A
            // quarter more covers the camera moving while the disc read runs.
            long draw = OrderingTableSize();
            if (inp.FogEnabled) draw = Math.Min(draw, 20000 / Mathf.Clamp(inp.FogDensity, 1, 10));  // renderer fogFarSZ
            long load = inp.LoadDistance > 0f ? (long)Mathf.RoundToInt(inp.LoadDistance / gte * 4096f) : draw * 5 / 4;
            load = Math.Max(1L, Math.Min(load, int.MaxValue / 2));
            long unload = load * 3 / 2;
            plan.LoadRadius = (int)load;
            plan.UnloadRadius = (int)unload;

            long spanX = Math.Max(1L, (long)bMaxX - bMinX);
            long spanZ = Math.Max(1L, (long)bMaxZ - bMinZ);
            long span = Math.Max(spanX, spanZ);

            // Region size. Slots cost slotCount * the largest region, so the
            // automatic pick is the grid with the least streaming memory that
            // keeps regions under TargetRegionBytes and slots under the engine cap.
            long cell;
            Grid grid;
            if (inp.RegionSize > 0f)
            {
                cell = Math.Max(1L, (long)Mathf.RoundToInt(inp.RegionSize / gte * 4096f));
                grid = Partition(inp, eligible, bMinX, bMinZ, cell);
                Size(grid, unload, bMinX, bMinZ, bMaxX, bMaxZ);
            }
            else
            {
                grid = null;
                int bestRank = int.MaxValue;
                long bestCost = long.MaxValue;
                long lastCell = -1;
                for (int k = 1; k <= 96; k++)
                {
                    long c = Math.Max(1L, (span + k - 1) / k);
                    if (c == lastCell) continue;
                    lastCell = c;
                    var g = Partition(inp, eligible, bMinX, bMinZ, c);
                    Size(g, unload, bMinX, bMinZ, bMaxX, bMaxZ);
                    int rank = (g.Slots <= MaxSlots ? 0 : 2) + (g.MaxBytes <= TargetRegionBytes ? 0 : 1);
                    long cost = (long)Math.Min(g.Slots, MaxSlots) * g.MaxBytes;
                    if (rank < bestRank || (rank == bestRank && cost < bestCost))
                    {
                        bestRank = rank;
                        bestCost = cost;
                        grid = g;
                    }
                    if (g.Regions.Count > 4096) break;
                }
                cell = grid.Cell;
            }

            uint sector = 0;
            int refCount = 0;
            foreach (var r in grid.Regions)
            {
                r.FirstSector = sector;
                sector += r.ByteSize / SectorSize;
                refCount += r.Objects.Count;
                plan.Regions.Add(r);
                foreach (int i in r.Objects) plan.Streamed.Add(i);
            }
            plan.SlotBytes = (uint)grid.MaxBytes;
            plan.SlotCount = Math.Min(grid.Slots, MaxSlots);

            var s = plan.Stats;
            s.RegionCount = plan.Regions.Count;
            s.StreamedObjects = plan.Streamed.Count;
            s.ResidentObjects = exporters.Length - plan.Streamed.Count;
            s.StreamedTriangleBytes = eligibleBytes;
            s.SlotCount = plan.SlotCount;
            s.SlotBytes = plan.SlotBytes;
            s.TableBytes = TableHeaderBytes + RegionRecordBytes * plan.Regions.Count + ObjectRefBytes * refCount;
            s.RegionSize = cell * gte / 4096f;
            s.LoadDistance = load * gte / 4096f;

            if (plan.Regions.Count > 65535 || refCount > 65535)
                plan.Warnings.Add($"The world splits into {plan.Regions.Count} regions holding {refCount} objects, more " +
                                  "than a scene can hold (65535). Raise the region size under Advanced, or merge small " +
                                  "props into bigger meshes.");
            Warn(plan, inp, grid.Slots);
            return plan;
        }

        /// <summary>Whether a plan can actually be written.</summary>
        public static bool IsWritable(PSXWorldStreamPlan plan)
        {
            if (plan == null || plan.Regions.Count == 0 || plan.Regions.Count > 65535) return false;
            int refs = 0;
            foreach (var r in plan.Regions) refs += r.Objects.Count;
            return refs <= 65535;
        }

        private class Grid
        {
            public long Cell;
            public List<PSXWorldStreamPlan.Region> Regions;
            public Dictionary<long, PSXWorldStreamPlan.Region> ByCell;
            public const long Cols = 1L << 31;
            public long MaxBytes;
            public int Slots;
        }

        // Uniform XZ grid; each object goes to the cell holding its AABB centre.
        private static Grid Partition(PSXWorldStreamInputs inp, bool[] eligible, int minX, int minZ, long cell)
        {
            var g = new Grid { Cell = cell, ByCell = new Dictionary<long, PSXWorldStreamPlan.Region>() };
            var sorted = new SortedDictionary<long, PSXWorldStreamPlan.Region>();
            for (int i = 0; i < inp.Exporters.Length; i++)
            {
                if (!eligible[i]) continue;
                int[] a = inp.Aabbs[i];
                long cx = (((long)a[0] + a[3]) / 2 - minX) / cell;
                long cz = (((long)a[2] + a[5]) / 2 - minZ) / cell;
                long key = cz * Grid.Cols + cx;
                if (!sorted.TryGetValue(key, out var r))
                {
                    r = new PSXWorldStreamPlan.Region
                    {
                        MinX = int.MaxValue, MinY = int.MaxValue, MinZ = int.MaxValue,
                        MaxX = int.MinValue, MaxY = int.MinValue, MaxZ = int.MinValue
                    };
                    sorted[key] = r;
                }
                r.Objects.Add(i);
                r.TriBytes += inp.TriCounts[i] * BytesPerTri;
                r.MinX = Math.Min(r.MinX, a[0]); r.MinY = Math.Min(r.MinY, a[1]); r.MinZ = Math.Min(r.MinZ, a[2]);
                r.MaxX = Math.Max(r.MaxX, a[3]); r.MaxY = Math.Max(r.MaxY, a[4]); r.MaxZ = Math.Max(r.MaxZ, a[5]);
            }
            g.Regions = new List<PSXWorldStreamPlan.Region>(sorted.Values);
            foreach (var kv in sorted)
            {
                kv.Value.ByteSize = (uint)((kv.Value.TriBytes + SectorSize - 1) / SectorSize * SectorSize);
                g.MaxBytes = Math.Max(g.MaxBytes, kv.Value.ByteSize);
                g.ByCell[kv.Key] = kv.Value;
            }
            return g;
        }

        // Slots: the most regions within unloadRadius of any sampled camera
        // position, plus one for the read in flight.
        private static void Size(Grid g, long unload, int minX, int minZ, int maxX, int maxZ)
        {
            long cell = g.Cell;
            long step = Math.Max(Math.Max(1L, cell / 2), Math.Max((long)maxX - minX, (long)maxZ - minZ) / 64);
            long reach = unload / cell + 2;  // a region's bounds stay near its own cell
            long unloadSq = unload * unload;
            int worst = 0;
            for (long z = minZ; z <= maxZ + step - 1; z += step)
            {
                for (long x = minX; x <= maxX + step - 1; x += step)
                {
                    long cx = (Math.Min(x, maxX) - minX) / cell, cz = (Math.Min(z, maxZ) - minZ) / cell;
                    int count = 0;
                    for (long dz = -reach; dz <= reach; dz++)
                        for (long dx = -reach; dx <= reach; dx++)
                        {
                            if (cx + dx < 0 || cz + dz < 0) continue;
                            if (!g.ByCell.TryGetValue((cz + dz) * Grid.Cols + cx + dx, out var r)) continue;
                            if (DistSq(r, x, z) <= unloadSq) count++;
                        }
                    if (count > worst) worst = count;
                }
            }
            g.Slots = Math.Max(1, worst) + 1;
        }

        private static void Warn(PSXWorldStreamPlan plan, PSXWorldStreamInputs inp, int slotsWanted)
        {
            var exporters = inp.Exporters;
            var sizes = new List<uint>();
            foreach (var r in plan.Regions) sizes.Add(r.ByteSize);
            sizes.Sort();
            uint median = sizes.Count > 0 ? sizes[sizes.Count / 2] : 0;

            for (int ri = 0; ri < plan.Regions.Count; ri++)
            {
                var r = plan.Regions[ri];
                int biggest = r.Objects[0];
                foreach (int i in r.Objects)
                    if (inp.TriCounts[i] > inp.TriCounts[biggest] ||
                        (inp.TriCounts[i] == inp.TriCounts[biggest] &&
                         string.CompareOrdinal(exporters[i].name, exporters[biggest].name) < 0))
                        biggest = i;
                string big = exporters[biggest].name;

                if (r.ByteSize > LargeRegionBytes)
                    plan.Warnings.Add($"Region {ri} holds {r.ByteSize / 1024} KB of triangles, which is slow to read from " +
                                      $"disc while the player walks. Split its biggest object '{big}' into smaller " +
                                      "meshes, or lower the region size under Advanced.");
                else if (median > 0 && r.ByteSize > 2 * median && plan.Regions.Count > 1)
                    plan.Warnings.Add($"Region {ri} is {r.ByteSize / 1024} KB while most are {median / 1024} KB, so every " +
                                      $"streaming slot costs {r.ByteSize / 1024} KB: split the big object '{big}' or lower " +
                                      "the region size under Advanced.");
            }
            if (slotsWanted > MaxSlots)
                plan.Warnings.Add($"Up to {slotsWanted - 1} regions are near the player at once, but the engine keeps at " +
                                  $"most {MaxSlots}, so some far scenery will pop in late. Raise the region size or " +
                                  "lower the load distance under Advanced.");
            if (plan.Stats.RamSaved <= 0)
                plan.Warnings.Add($"Streaming does not save RAM here: the scenery is only " +
                                  $"{plan.Stats.StreamedTriangleBytes / 1024} KB and streaming needs " +
                                  $"{plan.Stats.PoolBytes / 1024} KB to hold the regions near the player. Streaming " +
                                  "is for large worlds; untick \"Stream World Geometry\" for this scene.");
            foreach (string src in LuaSources(inp))
            {
                if (src != null && src.Contains("PlayCDDA"))
                {
                    plan.Warnings.Add("A script in this scene plays CD music (Audio.PlayCDDA), but a streamed scene uses " +
                                      "the disc drive for the world, so that music will not play. Use sequenced or SPU " +
                                      "audio for this scene's music, or untick \"Stream World Geometry\".");
                    break;
                }
            }
        }

        public static long DistSq(PSXWorldStreamPlan.Region r, long x, long z)
        {
            long dx = x < r.MinX ? r.MinX - x : (x > r.MaxX ? x - r.MaxX : 0);
            long dz = z < r.MinZ ? r.MinZ - z : (z > r.MaxZ ? z - r.MaxZ : 0);
            return dx * dx + dz * dz;
        }

        /// <summary>
        /// An object streams when nothing in the scene can move it or touch its
        /// triangles: no Lua script, not skinned, no interactable or agent, not a
        /// dynamic collider, not the target of an animation or cutscene track, and
        /// not named in any Lua source (Entity.Find). Scripts that reach objects
        /// only by index (Entity.FindByIndex) cannot be seen from here.
        /// </summary>
        public static bool[] FindEligible(PSXWorldStreamInputs inp)
        {
            PSXObjectExporter[] exporters = inp.Exporters;
            var touched = new HashSet<PSXObjectExporter>();
            if (inp.Interactables != null)
                foreach (var it in inp.Interactables)
                    if (it != null && it.GetComponent<PSXObjectExporter>() is PSXObjectExporter e) touched.Add(e);
            if (inp.Agents != null)
                foreach (var ag in inp.Agents)
                    if (ag != null && ag.GetComponent<PSXObjectExporter>() is PSXObjectExporter e) touched.Add(e);

            var trackTargets = new HashSet<string>();
            if (inp.Cutscenes != null)
                foreach (var c in inp.Cutscenes)
                    if (c != null) AddTargets(c.Tracks, trackTargets);
            if (inp.Animations != null)
                foreach (var a in inp.Animations)
                    if (a != null) AddTargets(a.Tracks, trackTargets);

            var sources = LuaSources(inp);
            var result = new bool[exporters.Length];
            for (int i = 0; i < exporters.Length; i++)
            {
                var e = exporters[i];
                if (e == null || inp.TriCounts[i] == 0 || inp.Aabbs[i] == null) continue;
                if (e.LuaFile != null || inp.Skinned.Contains(e) || touched.Contains(e)) continue;
                if (e.CollisionType == PSXCollisionType.Dynamic) continue;
                if (trackTargets.Contains(e.name)) continue;
                if (NamedInLua(e.name, sources)) continue;
                result[i] = true;
            }
            return result;
        }

        private static List<string> LuaSources(PSXWorldStreamInputs inp)
        {
            var sources = new List<string>();
            if (inp.SceneLuaFile != null) sources.Add(inp.SceneLuaFile.LuaScript);
            foreach (var e in inp.Exporters)
                if (e != null && e.LuaFile != null) sources.Add(e.LuaFile.LuaScript);
            if (inp.TriggerBoxes != null)
                foreach (var tb in inp.TriggerBoxes)
                    if (tb != null && tb.LuaFile != null) sources.Add(tb.LuaFile.LuaScript);
            return sources;
        }

        private static void AddTargets(List<PSXCutsceneTrack> tracks, HashSet<string> names)
        {
            if (tracks == null) return;
            foreach (var t in tracks)
                if (t != null && !string.IsNullOrEmpty(t.ObjectName) && !t.IsCameraTrack && !t.IsUITrack)
                    names.Add(t.ObjectName);
        }

        private static bool NamedInLua(string name, List<string> sources)
        {
            foreach (var src in sources)
            {
                if (string.IsNullOrEmpty(src)) continue;
                if (src.Contains("\"" + name + "\"") || src.Contains("'" + name + "'")) return true;
            }
            return false;
        }

        /// <summary>
        /// Inputs from the open scene, for the editor preview. Triangle counts come
        /// from the Unity mesh, which the exporter converts one triangle for one.
        /// </summary>
        public static PSXWorldStreamInputs FromScene(PSXSceneExporter sceneExporter)
        {
            var skinnedRoots = new HashSet<GameObject>();
            foreach (var s in UnityEngine.Object.FindObjectsByType<PSXSkinnedObjectExporter>(FindObjectsSortMode.None))
                skinnedRoots.Add(s.gameObject);

            var list = new List<PSXObjectExporter>();
            foreach (var e in UnityEngine.Object.FindObjectsByType<PSXObjectExporter>(FindObjectsSortMode.None))
            {
                if (e.gameObject.hideFlags != HideFlags.None) continue;
                bool underSkinned = false;
                for (Transform t = e.transform; t != null && !underSkinned; t = t.parent)
                    underSkinned = skinnedRoots.Contains(t.gameObject);
                if (!underSkinned) list.Add(e);
            }

            float gte = sceneExporter.GTEScaling;
            var inp = new PSXWorldStreamInputs
            {
                Exporters = list.ToArray(),
                TriCounts = new int[list.Count],
                Aabbs = new int[list.Count][],
                Interactables = UnityEngine.Object.FindObjectsByType<PSXInteractable>(FindObjectsSortMode.None),
                Agents = UnityEngine.Object.FindObjectsByType<PSXAgent>(FindObjectsSortMode.None),
                TriggerBoxes = UnityEngine.Object.FindObjectsByType<PSXTriggerBox>(FindObjectsSortMode.None),
                Cutscenes = sceneExporter.Cutscenes,
                Animations = sceneExporter.Animations,
                SceneLuaFile = sceneExporter.SceneLuaFile,
                GteScaling = gte,
                FogEnabled = sceneExporter.FogEnabled,
                FogDensity = sceneExporter.FogDensity,
                RegionSize = sceneExporter.StreamRegionSize,
                LoadDistance = sceneExporter.StreamLoadDistance,
            };
            for (int i = 0; i < list.Count; i++)
            {
                var mf = list[i].GetComponent<MeshFilter>();
                Mesh mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null || list[i].GetComponent<Renderer>() == null) continue;
                int tris = 0;
                for (int sm = 0; sm < mesh.subMeshCount; sm++)
                    tris += (int)(mesh.GetIndexCount(sm) / 3);
                inp.TriCounts[i] = tris;
                inp.Aabbs[i] = PSXSceneWriter.ComputeObjectAABB(list[i], gte);
            }
            return inp;
        }
    }
}
