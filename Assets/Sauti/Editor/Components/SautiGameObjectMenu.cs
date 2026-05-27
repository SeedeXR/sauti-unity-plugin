// Assets/Sauti/Editor/Components/SautiGameObjectMenu.cs
//
// v1.3 — top-of-hierarchy "GameObject → Sauti → …" menu entries.
// Creates pre-wired GameObjects so a designer dropping Sauti into a scene
// doesn't have to manually add four components + one AudioSource.

using Sauti.Components;
using UnityEditor;
using UnityEngine;

namespace Sauti.Editor.Components
{
    public static class SautiGameObjectMenu
    {
        [MenuItem("GameObject/Sauti/Sauti Agent", false, 10)]
        public static void CreateSautiAgent(MenuCommand command)
        {
            var go = new GameObject("Sauti Agent");
            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);

            // Attach an AudioSource first so SautiSpeaker's [RequireComponent] is satisfied.
            go.AddComponent<AudioSource>();
            var speaker = go.AddComponent<SautiSpeaker>();
            var knowledge = go.AddComponent<SautiKnowledgeBase>();
            var agent = go.AddComponent<SautiAgent>();

            agent.Speaker = speaker;
            agent.Knowledge = knowledge;

            Undo.RegisterCreatedObjectUndo(go, "Create Sauti Agent");
            Selection.activeObject = go;
        }

        [MenuItem("GameObject/Sauti/Sauti Speaker (TTS only)", false, 11)]
        public static void CreateSautiSpeaker(MenuCommand command)
        {
            var go = new GameObject("Sauti Speaker");
            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);

            go.AddComponent<AudioSource>();
            go.AddComponent<SautiSpeaker>();

            Undo.RegisterCreatedObjectUndo(go, "Create Sauti Speaker");
            Selection.activeObject = go;
        }
    }
}
