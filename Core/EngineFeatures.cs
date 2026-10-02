using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SplashEdit.Core
{
    /// <summary>
    /// How one engine feature is decided. Auto follows the project scan.
    /// </summary>
    public enum FeatureMode
    {
        Auto,
        Always,
        Never
    }

    /// <summary>
    /// What one exported scene carries, as the engine will read it.
    /// </summary>
    public sealed class SceneFacts
    {
        public string Name = "";
        public int UICanvases;
        public bool LoadingScreen;
        public int SpriteSheets;
        public bool Tilemap;
        public int SkinnedMeshes;
        public int Cutscenes;
        public int Animations;
        public int PointLights;
        public bool StreamedWorld;
        public int NavRegions;
        public int Agents;
        public int Colliders;
        public int TriggerBoxes;
        public bool NetworkSceneId;

        /// <summary>
        /// Reads the counts from a written splashpack, with the same version
        /// checks the engine's loader (splashpack.cpp) applies.
        /// </summary>
        public static SceneFacts FromSplashpack(string name, byte[] pack)
        {
            if (pack == null || pack.Length < 120 || pack[0] != 'S' || pack[1] != 'P')
                throw new ArgumentException($"'{name}' is not a splashpack");

            int version = U16(pack, 2);
            var facts = new SceneFacts { Name = name ?? "" };
            facts.Colliders = U16(pack, 12);
            facts.TriggerBoxes = U16(pack, 38);
            facts.NavRegions = U16(pack, 44);
            if (U32(pack, 88) != 0) facts.Cutscenes = U16(pack, 84);
            if (version >= 13 && U32(pack, 96) != 0) facts.UICanvases = U16(pack, 92);
            if (U32(pack, 108) != 0) facts.Animations = U16(pack, 104);
            if (version >= 18 && U32(pack, 116) != 0) facts.SkinnedMeshes = U16(pack, 112);
            if (version >= 22) facts.Agents = U16(pack, 114);
            if (version >= 21) facts.StreamedWorld = U32(pack, 124) != 0;
            if (version >= 22)
            {
                if (U32(pack, 128) != 0) facts.SpriteSheets = U16(pack, 132);
                facts.NetworkSceneId = U32(pack, 136) != 0;
            }
            if (version >= 23) facts.Tilemap = U32(pack, 140) != 0;
            if (version >= 24 && pack.Length >= 148)
            {
                uint lights = U32(pack, 144);
                if (lights != 0 && lights + 2 <= pack.Length) facts.PointLights = U16(pack, (int)lights);
            }
            return facts;
        }

        static int U16(byte[] b, int o) => b[o] | (b[o + 1] << 8);
        static uint U32(byte[] b, int o) => (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
    }

    public sealed class FeatureDecision
    {
        public string Id;
        public string Label;
        public bool Included;
        public FeatureMode Mode;
        /// <summary>Why the project needs it. Empty when nothing uses it.</summary>
        public List<string> Reasons = new List<string>();
        /// <summary>Set when the mode contradicts the scan.</summary>
        public string Warning;
    }

    public sealed class FeatureSet
    {
        public List<FeatureDecision> Features = new List<FeatureDecision>();

        /// <summary>The value for the engine Makefile's FEATURES variable.</summary>
        public string MakeValue
        {
            get
            {
                var ids = Features.Where(f => f.Included).Select(f => f.Id).ToArray();
                return ids.Length == 0 ? "none" : string.Join(" ", ids);
            }
        }

        /// <summary>One line per feature, for a build log.</summary>
        public IEnumerable<string> Describe()
        {
            foreach (var f in Features)
            {
                string state = f.Included ? "in " : "out";
                string why = f.Reasons.Count > 0 ? EngineFeatures.JoinReasons(f.Reasons) : "nothing uses it";
                string mode = f.Mode == FeatureMode.Auto ? "" : $" [{f.Mode}]";
                yield return $"{state} {f.Label}{mode}: {why}";
                if (f.Warning != null) yield return $"    warning: {f.Warning}";
            }
        }
    }

    /// <summary>
    /// Decides which optional engine subsystems a build needs, from the exported
    /// scenes and the Lua scripts they run. The ids are the engine Makefile's
    /// FEATURES names (psxsplash src/features.hh).
    /// </summary>
    public static class EngineFeatures
    {
        public sealed class Info
        {
            public string Id;
            public string Label;
            /// <summary>Lua globals the engine replaces with an error stub when the feature is out.</summary>
            public string[] LuaNamespaces;
        }

        public static readonly Info[] All =
        {
            new Info { Id = "net", Label = "Networking", LuaNamespaces = new[] { "Net" } },
            new Info { Id = "ui", Label = "UI", LuaNamespaces = new[] { "UI" } },
            new Info { Id = "sprites", Label = "Sprites and tilemaps", LuaNamespaces = new[] { "Sprite", "Tile" } },
            new Info { Id = "skin", Label = "Skinned meshes", LuaNamespaces = new[] { "SkinnedAnim" } },
            new Info { Id = "cutscene", Label = "Cutscenes and animations", LuaNamespaces = new[] { "Cutscene", "Animation" } },
            new Info { Id = "lights", Label = "Point lights", LuaNamespaces = new[] { "Light" } },
            new Info { Id = "streaming", Label = "World streaming", LuaNamespaces = new string[0] },
            new Info { Id = "memcard", Label = "Memory card", LuaNamespaces = new[] { "MemCard" } },
            new Info { Id = "nav", Label = "Navigation", LuaNamespaces = new string[0] },
            new Info { Id = "agents", Label = "AI agents", LuaNamespaces = new[] { "Agent" } },
            new Info { Id = "collision", Label = "Collision and triggers", LuaNamespaces = new string[0] },
        };

        public static FeatureSet Compute(IEnumerable<SceneFacts> scenes,
                                         IEnumerable<KeyValuePair<string, string>> luaSources,
                                         IDictionary<string, FeatureMode> overrides = null)
        {
            // Per feature, in order: a kind of evidence and where it was found,
            // e.g. "UI canvases" -> "Menu (2)", "Lobby (1)".
            var evidence = All.ToDictionary(f => f.Id, f => new List<KeyValuePair<string, List<string>>>());
            void Add(string id, string kind, string where)
            {
                var groups = evidence[id];
                var g = groups.FirstOrDefault(x => x.Key == kind);
                if (g.Value == null)
                {
                    g = new KeyValuePair<string, List<string>>(kind, new List<string>());
                    groups.Add(g);
                }
                g.Value.Add(where);
            }
            void AddCount(string id, string kind, string scene, int count)
            {
                if (count > 0) Add(id, kind, $"{scene} ({count})");
            }

            foreach (var s in scenes ?? Enumerable.Empty<SceneFacts>())
            {
                string scene = s.Name;
                AddCount("ui", "UI canvases", scene, s.UICanvases);
                if (s.LoadingScreen) Add("ui", "loading screens", scene);
                AddCount("sprites", "sprite sheets", scene, s.SpriteSheets);
                if (s.Tilemap) Add("sprites", "tilemaps", scene);
                AddCount("skin", "skinned meshes", scene, s.SkinnedMeshes);
                AddCount("cutscene", "cutscenes", scene, s.Cutscenes);
                AddCount("cutscene", "animations", scene, s.Animations);
                AddCount("lights", "runtime point lights", scene, s.PointLights);
                if (s.StreamedWorld) Add("streaming", "streamed worlds", scene);
                AddCount("nav", "nav regions", scene, s.NavRegions);
                AddCount("agents", "agents", scene, s.Agents);
                AddCount("collision", "colliders", scene, s.Colliders);
                AddCount("collision", "trigger boxes", scene, s.TriggerBoxes);
                if (s.NetworkSceneId) Add("net", "Network Scene Id", scene);
            }

            foreach (var lua in luaSources ?? Enumerable.Empty<KeyValuePair<string, string>>())
            {
                var used = LuaGlobalsUsed(lua.Value);
                foreach (var f in All)
                    foreach (var ns in f.LuaNamespaces)
                        if (used.Contains(ns)) Add(f.Id, $"scripts using {ns}", lua.Key);
            }

            var reasons = All.ToDictionary(f => f.Id,
                f => evidence[f.Id].Select(g => $"{g.Key}: {JoinNames(g.Value)}").ToList());

            var set = new FeatureSet();
            var byId = new Dictionary<string, FeatureDecision>();
            foreach (var f in All)
            {
                FeatureMode mode = FeatureMode.Auto;
                if (overrides != null) overrides.TryGetValue(f.Id, out mode);
                var d = new FeatureDecision { Id = f.Id, Label = f.Label, Mode = mode, Reasons = reasons[f.Id] };
                d.Included = mode == FeatureMode.Always || (mode == FeatureMode.Auto && d.Reasons.Count > 0);
                if (mode == FeatureMode.Never && d.Reasons.Count > 0)
                    d.Warning = $"set to Never but the project uses it ({JoinReasons(d.Reasons)}). " +
                                "The engine will skip that scene data, and scripts calling it will stop with an error.";
                byId[f.Id] = d;
                set.Features.Add(d);
            }

            // Agents walk nav regions; the Makefile adds nav for them too.
            var agents = byId["agents"];
            var nav = byId["nav"];
            if (agents.Included && !nav.Included)
            {
                nav.Included = true;
                nav.Reasons.Add("needed by AI agents");
                if (nav.Mode == FeatureMode.Never)
                    nav.Warning = "set to Never, but AI agents are in the build and need it, so it stays in.";
            }
            return set;
        }

        internal static string JoinReasons(List<string> reasons) => string.Join("; ", reasons);

        static string JoinNames(List<string> names)
        {
            const int shown = 3;
            if (names.Count <= shown) return string.Join(", ", names);
            return string.Join(", ", names.Take(shown)) + $" and {names.Count - shown} more";
        }

        /// <summary>
        /// The free global names a Lua script refers to: identifiers that are not
        /// a field access (after '.' or ':'), plus string literals, so _G["Net"]
        /// counts too. Comments are ignored. A local shadowing a global name also
        /// counts, which errs toward building the feature in.
        /// </summary>
        public static HashSet<string> LuaGlobalsUsed(string src)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(src)) return names;
            int i = 0, n = src.Length;
            // The last two significant characters outside comments and strings.
            char prev = '\0', prevprev = '\0';
            while (i < n)
            {
                char c = src[i];
                if (c == '-' && i + 1 < n && src[i + 1] == '-')
                {
                    i += 2;
                    int level = LongBracketLevel(src, i);
                    if (level >= 0) i = SkipLongBracket(src, i, level, null);
                    else while (i < n && src[i] != '\n') i++;
                    continue;
                }
                if (c == '[')
                {
                    int level = LongBracketLevel(src, i);
                    if (level >= 0)
                    {
                        var sb = new StringBuilder();
                        i = SkipLongBracket(src, i, level, sb);
                        names.Add(sb.ToString());
                        prevprev = prev;
                        prev = ']';
                        continue;
                    }
                }
                if (c == '"' || c == '\'')
                {
                    var sb = new StringBuilder();
                    i++;
                    while (i < n && src[i] != c && src[i] != '\n')
                    {
                        if (src[i] == '\\' && i + 1 < n) { i++; sb.Append(src[i]); i++; continue; }
                        sb.Append(src[i]);
                        i++;
                    }
                    i++;
                    names.Add(sb.ToString());
                    prevprev = prev;
                    prev = c;
                    continue;
                }
                if (char.IsLetter(c) || c == '_')
                {
                    int start = i;
                    while (i < n && (char.IsLetterOrDigit(src[i]) || src[i] == '_')) i++;
                    // After '.' or ':' it is a field name, unless the '.' is the
                    // second half of the '..' concatenation operator.
                    bool isField = prev == ':' || (prev == '.' && prevprev != '.');
                    if (!isField) names.Add(src.Substring(start, i - start));
                    prevprev = prev;
                    prev = 'a';
                    continue;
                }
                if (!char.IsWhiteSpace(c)) { prevprev = prev; prev = c; }
                i++;
            }
            return names;
        }

        // "[[" -> 0, "[==[" -> 2, anything else -> -1. Starts at the first '['.
        static int LongBracketLevel(string s, int i)
        {
            if (i >= s.Length || s[i] != '[') return -1;
            int j = i + 1, level = 0;
            while (j < s.Length && s[j] == '=') { level++; j++; }
            return j < s.Length && s[j] == '[' ? level : -1;
        }

        static int SkipLongBracket(string s, int i, int level, StringBuilder content)
        {
            int start = i + level + 2;
            string close = "]" + new string('=', level) + "]";
            int end = s.IndexOf(close, start, StringComparison.Ordinal);
            if (end < 0) end = s.Length;
            content?.Append(s, start, end - start);
            return Math.Min(s.Length, end + close.Length);
        }
    }
}
