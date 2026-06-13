using System;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class CodexPlayModeAutomation
{
    private const bool EnableCommandFileControl = false;
    private const string CommandPath = "Temp/CodexPlayModeCommand.txt";
    private const string StatePath = "Temp/CodexPlayModeState.txt";
    private static double nextPollTime;

    static CodexPlayModeAutomation()
    {
        if (EnableCommandFileControl)
        {
            EditorApplication.update += PollCommandFile;
        }

        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        WriteState("loaded");
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        WriteState(state.ToString());
        Debug.Log("[CodexPlayModeAutomation] " + state);
    }

    private static void PollCommandFile()
    {
        if (EditorApplication.timeSinceStartup < nextPollTime)
        {
            return;
        }

        nextPollTime = EditorApplication.timeSinceStartup + 0.25;

        if (!File.Exists(CommandPath))
        {
            return;
        }

        string command;
        try
        {
            command = File.ReadAllText(CommandPath).Trim().ToLowerInvariant();
            File.Delete(CommandPath);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[CodexPlayModeAutomation] Failed to read command: " + ex.Message);
            return;
        }

        if (command == "enter")
        {
            EditorApplication.isPlaying = true;
        }
        else if (command == "exit")
        {
            EditorApplication.isPlaying = false;
        }
        else if (command == "toggle")
        {
            EditorApplication.isPlaying = !EditorApplication.isPlaying;
        }

        WriteState("command=" + command);
    }

    private static void WriteState(string reason)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath));
            File.WriteAllText(
                StatePath,
                "reason=" + reason + Environment.NewLine +
                "isPlaying=" + EditorApplication.isPlaying + Environment.NewLine +
                "isPlayingOrWillChangePlaymode=" + EditorApplication.isPlayingOrWillChangePlaymode + Environment.NewLine +
                "time=" + DateTime.Now.ToString("O") + Environment.NewLine);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[CodexPlayModeAutomation] Failed to write state: " + ex.Message);
        }
    }
}
