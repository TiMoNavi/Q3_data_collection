using System;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class PlayModeViewCrashWorkaround
{
    static PlayModeViewCrashWorkaround()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
        {
            SceneView.FocusWindowIfItsOpen<SceneView>();
        }
    }

    private static void CloseGameViewsAndFocusScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        try
        {
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.Focus();
            }
            else
            {
                SceneView.FocusWindowIfItsOpen<SceneView>();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PlayModeViewCrashWorkaround] Failed to close GameView: {ex.Message}");
        }
    }
}
