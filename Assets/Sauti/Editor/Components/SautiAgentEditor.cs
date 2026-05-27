// Assets/Sauti/Editor/Components/SautiAgentEditor.cs
//
// v1.3 — custom inspector for SautiAgent. Adds:
//   • A "Verify Wiring" button that reports which slots are unset.
//   • A test-question field + "Preview Prompt" button that calls
//     BuildPromptAsync (Play mode, no LLM round-trip) and prints the assembled
//     prompt to the console so designers can sanity-check the system prompt
//     + RAG injection before plugging in an LLM.

using System.Text;
using Sauti.Components;
using UnityEditor;
using UnityEngine;

namespace Sauti.Editor.Components
{
    [CustomEditor(typeof(SautiAgent))]
    public sealed class SautiAgentEditor : UnityEditor.Editor
    {
        private string _testQuestion = "Who is Elder Maren?";

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var agent = (SautiAgent)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Tools", EditorStyles.boldLabel);

            if (GUILayout.Button("Verify Wiring"))
            {
                var sb = new StringBuilder("Sauti Agent wiring:\n");
                sb.Append("  Speaker:        ").AppendLine(agent.Speaker == null ? "MISSING (optional — replies won't be spoken)" : agent.Speaker.name);
                sb.Append("  KnowledgeBase:  ").AppendLine(agent.Knowledge == null ? "MISSING (optional — RAG context won't be injected)" : agent.Knowledge.name);
                sb.Append("  LlmConfig:      ").AppendLine(agent.LlmConfig == null ? "MISSING (REQUIRED — using defaults)" : agent.LlmConfig.name);
                Debug.Log(sb.ToString(), agent);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Preview prompt", EditorStyles.boldLabel);
            _testQuestion = EditorGUILayout.TextField("Test question", _testQuestion);

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("Preview Prompt (no LLM call)"))
                {
                    PreviewPromptAsync(agent, _testQuestion);
                }
            }

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Enter Play mode to preview the assembled prompt. " +
                    "Preview runs the same retrieval pipeline AskAsync would, " +
                    "but stops before any LLM call.",
                    MessageType.Info);
            }
        }

        private static async void PreviewPromptAsync(SautiAgent agent, string question)
        {
            try
            {
                string prompt = await agent.BuildPromptAsync(question);
                Debug.Log($"[Sauti][Agent] Preview prompt for \"{question}\":\n----\n{prompt}\n----", agent);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Sauti][Agent] Preview failed: {ex}", agent);
            }
        }
    }
}
