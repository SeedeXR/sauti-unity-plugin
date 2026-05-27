// Assets/Sauti/Editor/Components/SautiKnowledgeBaseEditor.cs
//
// v1.3 — custom inspector for SautiKnowledgeBase. Adds:
//   • "Build Knowledge Base" button — invokes RagDatabaseBuilder.BuildFromMenu
//     so designers don't need to hunt for the Sauti → Build Knowledge Base
//     menu item.
//   • A status row showing the knowledge.db file size + last write time
//     (resolved from the assigned SautiKnowledgeConfig).
//   • A "Reveal in Finder/Explorer" button for the resolved knowledge.db.

using System.IO;
using Sauti.Components;
using Sauti.Editor.Rag;
using UnityEditor;
using UnityEngine;

namespace Sauti.Editor.Components
{
    [CustomEditor(typeof(SautiKnowledgeBase))]
    public sealed class SautiKnowledgeBaseEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var kb = (SautiKnowledgeBase)target;
            var config = kb.Config;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Knowledge database", EditorStyles.boldLabel);

            if (config == null)
            {
                EditorGUILayout.HelpBox(
                    "No SautiKnowledgeConfig assigned. Create one via " +
                    "Assets → Create → Sauti → Knowledge Config.",
                    MessageType.Warning);
                return;
            }

            string dbPath = config.ResolveKnowledgeDbPath();
            bool exists = File.Exists(dbPath);

            EditorGUILayout.LabelField("Path", dbPath);
            if (exists)
            {
                var info = new FileInfo(dbPath);
                EditorGUILayout.LabelField("Size", $"{info.Length:N0} bytes");
                EditorGUILayout.LabelField("Last built", info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "knowledge.db not found at that path. Click Build Knowledge Base " +
                    "to create it from your knowledge-base/ directory.",
                    MessageType.Warning);
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Build Knowledge Base"))
                {
                    RagDatabaseBuilder.BuildFromMenu();
                }

                using (new EditorGUI.DisabledScope(!exists))
                {
                    if (GUILayout.Button("Reveal"))
                    {
                        EditorUtility.RevealInFinder(dbPath);
                    }
                }
            }

            if (Application.isPlaying)
            {
                EditorGUILayout.LabelField("Loaded at runtime", kb.IsLoaded ? "Yes" : "No (lazy-loads on first Retrieve)");
            }
        }
    }
}
