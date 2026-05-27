// Assets/Sauti/Runtime/Scripts/Components/SautiKnowledgeBase.cs
//
// v1.3 — drag-and-drop RAG component. Wraps Sauti.Memory.SautiRag.
//
// Designer usage:
//   1. Add Component → Sauti → Sauti Knowledge Base
//   2. Drag a SautiKnowledgeConfig asset into the Config slot
//   3. (If using LLMUnity backend) drag an LLMUnity RAG component into the
//      slot; otherwise call Initialise(yourBackend) from code
//   4. Use the inspector's "Build Knowledge Base" button to (re)build
//      knowledge.db from the project's knowledge-base/ folder
//
// Programmer usage is unchanged — instantiate Sauti.Memory.SautiRag directly
// with any ISautiRagBackend. This component is purely additive.

using System;
using System.Threading;
using System.Threading.Tasks;
using Sauti.Memory;
using UnityEngine;
using UnityEngine.Events;

namespace Sauti.Components
{
    [AddComponentMenu("Sauti/Sauti Knowledge Base")]
    public sealed class SautiKnowledgeBase : MonoBehaviour
    {
        [Tooltip("RAG paths + retrieval settings. Create via Assets → Create → Sauti → Knowledge Config.")]
        [SerializeField] private SautiKnowledgeConfig config;

#if SAUTI_LLMUNITY_AVAILABLE
        [Tooltip("Drag in your LLMUnity RAG component. Leave null if you set a " +
                 "backend programmatically via Initialise(ISautiRagBackend).")]
        [SerializeField] private LLMUnity.RAG llmUnityRag;
#endif

        [Header("Events")]
        [Tooltip("Fired with each retrieved chunk array after a successful search.")]
        public UnityEvent<string[]> OnRetrieved = new UnityEvent<string[]>();

        [Tooltip("Fired if loading or retrieval throws. Argument is the message.")]
        public UnityEvent<string> OnRetrieveError = new UnityEvent<string>();

        private SautiRag _rag;

        /// <summary>The wrapped SautiRag instance. Null until Initialise / first retrieve.</summary>
        public SautiRag Rag => _rag;

        public SautiKnowledgeConfig Config { get => config; set => config = value; }

        /// <summary>True if a backend has been wired AND its knowledge.db is loaded.</summary>
        public bool IsLoaded => _rag != null && _rag.IsLoaded;

        /// <summary>
        /// Code path — supply any ISautiRagBackend. Overrides the inspector's
        /// LLMUnity reference if both are provided. Safe to call repeatedly.
        /// </summary>
        public void Initialise(ISautiRagBackend backend)
        {
            if (backend == null) throw new ArgumentNullException(nameof(backend));
            _rag = new SautiRag(backend);
        }

        /// <summary>
        /// Ensure the underlying SautiRag is constructed and its knowledge.db is
        /// loaded. Auto-constructs from the inspector's LLMUnity RAG reference
        /// if Initialise hasn't been called.
        /// </summary>
        public async Task EnsureLoadedAsync()
        {
            if (config == null)
                throw new InvalidOperationException(
                    "SautiKnowledgeBase has no Config assigned.");

            if (_rag == null) AutoInitialise();
            if (_rag.IsLoaded) return;

            await _rag.LoadAsync(config.ResolveKnowledgeDbPath()).ConfigureAwait(true);
        }

        /// <summary>
        /// Retrieve the top-K knowledge chunks for <paramref name="query"/>.
        /// Uses <see cref="SautiKnowledgeConfig.defaultTopK"/> unless overridden.
        /// </summary>
        public async Task<(string[] chunks, float[] scores)> RetrieveAsync(
            string query,
            int? topK = null,
            CancellationToken ct = default)
        {
            try
            {
                await EnsureLoadedAsync().ConfigureAwait(true);
                if (ct.IsCancellationRequested) return (Array.Empty<string>(), Array.Empty<float>());

                int k = topK ?? (config != null ? config.defaultTopK : SautiRag.DefaultNumResults);
                var result = await _rag.SearchAsync(query, k).ConfigureAwait(true);
                OnRetrieved?.Invoke(result.chunks);
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Sauti][KB] retrieve failed: {ex}", this);
                OnRetrieveError?.Invoke(ex.Message);
                return (Array.Empty<string>(), Array.Empty<float>());
            }
        }

        private void AutoInitialise()
        {
#if SAUTI_LLMUNITY_AVAILABLE
            if (llmUnityRag != null)
            {
                _rag = new SautiRag(new LlmUnityRagBackend(llmUnityRag));
                return;
            }
#endif
            throw new InvalidOperationException(
                "SautiKnowledgeBase needs a backend. Either:\n" +
                "  • Call Initialise(yourBackend) from code, or\n" +
                "  • Assign an LLMUnity RAG component and define SAUTI_LLMUNITY_AVAILABLE.");
        }
    }
}
