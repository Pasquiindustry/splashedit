using UnityEngine;
using UnityEditor;
using SplashEdit.RuntimeCode;

namespace SplashEdit.EditorCode
{
    /// <summary>
    /// Legacy editor window kept for menu-bar convenience.
    /// Navigation can now live on a dedicated PSXNavigationSettings component.
    /// This window simply selects the most relevant scene object so users who
    /// click the old menu item land on the right place.
    /// </summary>
    public class PSXNavRegionEditor : EditorWindow
    {
        [MenuItem("PlayStation 1/Nav Region Builder")]
        public static void ShowWindow()
        {
            var navSettings = FindObjectsByType<PSXNavigationSettings>(FindObjectsSortMode.None);
            if (navSettings.Length > 0)
            {
                Selection.activeGameObject = navSettings[0].gameObject;
                EditorGUIUtility.PingObject(navSettings[0]);
                Debug.Log("[Nav] Selecting PSXNavigationSettings.");
                return;
            }

            // Fallback to the legacy PSXPlayer-hosted nav settings.
            var players = FindObjectsByType<PSXPlayer>(FindObjectsSortMode.None);
            if (players.Length > 0)
            {
                Selection.activeGameObject = players[0].gameObject;
                EditorGUIUtility.PingObject(players[0]);
                Debug.Log("[Nav] Selecting legacy PSXPlayer navigation settings.");
            }
            else
            {
                EditorUtility.DisplayDialog("Nav Region Builder",
                    "No PSXNavigationSettings or PSXPlayer found in the scene.\n\nAdd a PSXNavigationSettings component for scene-level navigation, or use the legacy PSXPlayer workflow.",
                    "OK");
            }
        }
    }
}
