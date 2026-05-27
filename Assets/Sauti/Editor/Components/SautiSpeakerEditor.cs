// Assets/Sauti/Editor/Components/SautiSpeakerEditor.cs
//
// v1.3 — custom inspector for SautiSpeaker. Adds:
//   • A test-text field + "Test Speak" button that runs the configured voice
//     in Play mode without needing a scripted trigger.
//   • A read-only status row showing whether the underlying runner is ready.

using Sauti.Components;
using UnityEditor;
using UnityEngine;

namespace Sauti.Editor.Components
{
    [CustomEditor(typeof(SautiSpeaker))]
    public sealed class SautiSpeakerEditor : UnityEditor.Editor
    {
        private string _testText = "Hello from Sauti.";

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var speaker = (SautiSpeaker)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Quick test", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                _testText = EditorGUILayout.TextField("Test text", _testText);
                if (GUILayout.Button("Test Speak"))
                {
                    speaker.Speak(_testText);
                }
            }

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Enter Play mode to test speech. The Kokoro runner loads on the " +
                    "first Speak call — first invocation may take 1–2 s while the ONNX " +
                    "session initialises.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.LabelField("Runner ready", speaker.IsReady ? "Yes" : "No (lazy-loads on first Speak)");
            }
        }
    }
}
