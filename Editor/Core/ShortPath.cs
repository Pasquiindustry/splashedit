using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace SplashEdit.EditorCode
{
    /// <summary>
    /// Resolves filesystem paths for safe use as a make working directory.
    /// GNU make and the psyqo makefiles derive their own root via
    /// $(abspath $(lastword $(MAKEFILE_LIST))) resolved against make's CWD, and neither
    /// handles a space in that path correctly. On Windows we can usually dodge the
    /// problem with the legacy 8.3 short path; everywhere else a space is fatal.
    /// </summary>
    public static class ShortPath
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetShortPathNameW(string lpszLongPath, StringBuilder lpszShortPath, uint cchBuffer);

        /// <summary>
        /// Resolves <paramref name="dir"/> into a path make can safely use as its working
        /// directory. On Windows, a path containing spaces is converted to its 8.3 short
        /// path. Returns false with a descriptive <paramref name="error"/> if the path
        /// contains a space and cannot be made safe (non-Windows, or 8.3 names disabled).
        /// </summary>
        public static bool TryResolveForMake(string dir, out string resolvedDir, out string error)
        {
            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                if (dir.IndexOf(' ') >= 0)
                {
                    resolvedDir = null;
                    error = SpacesInPathError(dir);
                    return false;
                }

                resolvedDir = dir;
                error = null;
                return true;
            }

            if (dir.IndexOf(' ') < 0)
            {
                resolvedDir = dir;
                error = null;
                return true;
            }

            string shortPath = GetShortPath(dir);
            if (string.IsNullOrEmpty(shortPath) || shortPath.IndexOf(' ') >= 0)
            {
                resolvedDir = null;
                error = SpacesInPathError(dir) +
                    " On Windows this can usually be fixed by enabling 8.3 short names on the " +
                    "drive (run `fsutil 8dot3name set <drive>: 0` as Administrator - this only " +
                    "affects files created afterwards, so the project may need to be recreated " +
                    "at its current path once enabled).";
                return false;
            }

            resolvedDir = shortPath;
            error = null;
            return true;
        }

        private static string SpacesInPathError(string dir) =>
            $"The project path contains spaces (\"{dir}\"), which GNU make and the psyqo " +
            "makefiles cannot handle. Move the project to a path without spaces.";

        private static string GetShortPath(string longPath)
        {
            var buffer = new StringBuilder(260);
            uint length = GetShortPathNameW(longPath, buffer, (uint)buffer.Capacity);
            if (length == 0)
                return null;

            if (length > buffer.Capacity)
            {
                buffer = new StringBuilder((int)length);
                length = GetShortPathNameW(longPath, buffer, (uint)buffer.Capacity);
                if (length == 0)
                    return null;
            }

            return buffer.ToString();
        }
    }
}
