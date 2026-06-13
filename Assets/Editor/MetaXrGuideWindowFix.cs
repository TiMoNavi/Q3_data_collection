using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Q3DataCollection.Editor
{
    [InitializeOnLoad]
    internal static class MetaXrGuideWindowFix
    {
        private const string GuideWindowTypeName = "Meta.XR.Guides.Editor.GuideWindow";
        private const string SessionShowAboutKey = "Meta.XR.SDK.Welcome to Meta XR SDK.ShowAbout";

        static MetaXrGuideWindowFix()
        {
            EditorApplication.delayCall += Apply;
        }

        [MenuItem("Tools/Q3 Data Collection/Fix Meta XR Guide Window Error")]
        private static void Apply()
        {
            DisableAutomaticWelcomeGuide();
            RemoveInvalidGuideWindows();
        }

        private static void DisableAutomaticWelcomeGuide()
        {
            SessionState.SetBool(SessionShowAboutKey, false);

            SetDontShowAgain("Meta.XR.Guides.Editor.About.WelcomePage");
            SetDontShowAgain("Meta.XR.Guides.Editor.About.Onboarding");
        }

        private static void SetDontShowAgain(string ownerId)
        {
            EditorPrefs.SetBool($"Meta.XR.SDK.{ownerId}.DontShowAgain", true);
        }

        private static void RemoveInvalidGuideWindows()
        {
            var guideWindowType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(GuideWindowTypeName))
                .FirstOrDefault(type => type != null);

            if (guideWindowType == null)
            {
                return;
            }

            foreach (var window in Resources.FindObjectsOfTypeAll(guideWindowType).Cast<UnityEngine.Object>())
            {
                if (window is EditorWindow editorWindow)
                {
                    try
                    {
                        editorWindow.Close();
                        continue;
                    }
                    catch (NullReferenceException)
                    {
                    }
                }

                UnityEngine.Object.DestroyImmediate(window, true);
            }
        }
    }
}
