// Assets/Sauti/Runtime/Scripts/LlmUnityRagBackend.cs
//
// MEM-002 default backend — delegates to LLMUnity's RAG façade (v3.0.3).
// Spec: memory/voice_ai_architecture.md § 4.3.
// API surface verified: memory/api_surfaces.md "LLMUnity.RAG : Searchable".
//
// LLMUnity.RAG is a MonoBehaviour — the caller AddComponents it onto a host
// GameObject, calls Init(...), then hands the instance to this backend's
// constructor. That keeps this file dependency-injectable for tests
// (FakeRagBackend in SautiRagTests does not need the LLMUnity package).
//
// This file is Unity-only: `using LLMUnity` resolves only when the LLMUnity
// UPM package is installed. dotnet build smoke check does not apply here.

using System;
using System.IO;
using System.Threading.Tasks;

#if SAUTI_LLMUNITY_AVAILABLE
using LLMUnity;
#endif

namespace Sauti.Memory
{
    /// <summary>
    /// Default <see cref="ISautiRagBackend"/> implementation backed by
    /// LLMUnity's <c>RAG</c> MonoBehaviour. Constructed with a pre-initialised
    /// <c>RAG</c> component (caller runs <c>AddComponent&lt;RAG&gt;()</c> +
    /// <c>rag.Init(SearchMethods.DBSearch, ChunkingMethods.NoChunking, llm)</c>
    /// before passing it here).
    /// </summary>
    public sealed class LlmUnityRagBackend : ISautiRagBackend
    {
#if SAUTI_LLMUNITY_AVAILABLE
        private readonly RAG _rag;
        private bool _loaded;

        public LlmUnityRagBackend(RAG rag)
        {
            _rag = rag ?? throw new ArgumentNullException(nameof(rag));
        }

        public bool IsLoaded => _loaded;

        public async Task LoadAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("path must not be empty", nameof(path));
            if (!File.Exists(path))
                throw new FileNotFoundException("RAG database not found", path);

            // LLMUnity Searchable.Load(string) is async + returns bool.
            // Per memory/api_surfaces.md, false here means "loaded nothing" — treat as load failure.
            bool ok = await _rag.Load(path).ConfigureAwait(false);
            _loaded = ok;
            if (!ok) throw new InvalidOperationException($"LLMUnity RAG.Load returned false for {path}");
        }

        public async Task<(string[] chunks, float[] scores)> SearchAsync(string query, int numResults)
        {
            if (!_loaded)
                return (Array.Empty<string>(), Array.Empty<float>());

            // LLMUnity Searchable.Search is async and returns (string[], float[]).
            (string[] phrases, float[] distances) = await _rag.Search(query, numResults).ConfigureAwait(false);
            return (phrases ?? Array.Empty<string>(), distances ?? Array.Empty<float>());
        }
#else
        // SAUTI_LLMUNITY_AVAILABLE not defined: this file lives in the Sauti.Runtime
        // asmdef but the LLMUnity package isn't visible to the compiler. Stub
        // so the assembly compiles; runtime exercise throws.
        //
        // To enable: add LLMUnity's asmdef name to Sauti.Runtime.asmdef references[]
        // AND define the symbol SAUTI_LLMUNITY_AVAILABLE in Project Settings
        // → Player → Scripting Define Symbols (or via the asmdef's `defineConstraints`).

        public bool IsLoaded => false;

        public Task LoadAsync(string path) =>
            throw new InvalidOperationException(
                "LlmUnityRagBackend requires SAUTI_LLMUNITY_AVAILABLE to be defined. " +
                "Add LLMUnity to Sauti.Runtime.asmdef references and define the symbol.");

        public Task<(string[] chunks, float[] scores)> SearchAsync(string query, int numResults) =>
            throw new InvalidOperationException(
                "LlmUnityRagBackend requires SAUTI_LLMUNITY_AVAILABLE to be defined.");
#endif
    }
}
