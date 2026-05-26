# Extending Sauti

Sauti exposes **three extension points** for the most common customisation needs. Each one is one small interface; each one ships with a default implementation and a test pattern.

| Extension point | Interface | Default | Test fixture |
|---|---|---|---|
| RAG backend | `ISautiRagBackend` | `LlmUnityRagBackend` | `FakeRagBackend` |
| RAG embedder | `IRagEmbedder` | `MiniLmRagEmbedder` | Write a fake per-test |
| Prompt assembler | (no interface — convention) | `FullVoiceLoop.BuildPrompt` | Compose strings yourself |

---

## Extension point 1 — `ISautiRagBackend`

Swap LLMUnity's `DBSearch` for any other vector backend (in-memory, on-disk flat index, hosted service, fake-for-tests).

### The interface

Source: [`Assets/Sauti/Runtime/Scripts/ISautiRagBackend.cs`](https://github.com/your-org/sauti-unity-plugin/blob/main/Assets/Sauti/Runtime/Scripts/ISautiRagBackend.cs).

```csharp
namespace Sauti.Memory
{
    public interface ISautiRagBackend
    {
        bool IsLoaded { get; }

        Task LoadAsync(string path);

        Task<(string[] chunks, float[] scores)> SearchAsync(string query, int numResults);
    }
}
```

Three members. Two methods.

### When to write your own

- **You want a smaller binary format.** LLMUnity's RAG persists via `ZipArchive` — heavier than necessary for small corpora.
- **You want to skip ANN.** If your knowledge base is small (< 1000 chunks), a brute-force cosine search loaded entirely into RAM is simpler, faster cold-start, and easier to debug than an ANN index.
- **You want to host the vector DB externally** (for offline-build-only scenarios where the runtime ships only the embedder, not the full corpus). Risky for privacy-first apps; explicit choice.
- **You want a test double.** See `FakeRagBackend` below — already in the test suite, copy-paste as your starting point.

### Minimal stub — a hard-coded fake

```csharp
using System.Threading.Tasks;
using Sauti.Memory;

public sealed class HardCodedFakeRagBackend : ISautiRagBackend
{
    private readonly (string chunk, float score)[] _fixed;

    public HardCodedFakeRagBackend(params (string chunk, float score)[] fixedResults)
    {
        _fixed = fixedResults;
    }

    public bool IsLoaded { get; private set; }

    public Task LoadAsync(string path)
    {
        IsLoaded = true;
        return Task.CompletedTask;
    }

    public Task<(string[] chunks, float[] scores)> SearchAsync(string query, int numResults)
    {
        int n = System.Math.Min(numResults, _fixed.Length);
        var chunks = new string[n];
        var scores = new float[n];
        for (int i = 0; i < n; i++) { chunks[i] = _fixed[i].chunk; scores[i] = _fixed[i].score; }
        return Task.FromResult((chunks, scores));
    }
}

// Usage in a scripted-encounter scene:
var fake = new HardCodedFakeRagBackend(
    ("Elder Maren only speaks after sundown.", 0.92f),
    ("The Crystal Caverns lie north of Stormwall.", 0.85f));
var rag = new SautiRag(fake);
await rag.LoadAsync("ignored-by-fake");
```

### Worked stub — a custom on-disk flat index

A non-trivial real backend. Loads every chunk + embedding into RAM, does brute-force cosine. Good for corpora up to ~10 K chunks; cold-start is fast (a few hundred ms for ~1 K × 384-dim float vectors).

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sauti.Memory;

public sealed class FlatCosineRagBackend : ISautiRagBackend
{
    // (1)
    private readonly int _dimensions;
    private readonly IRagEmbedder _queryEmbedder;

    private string[] _chunkTexts;
    private float[][] _embeddings;
    private bool _loaded;

    public FlatCosineRagBackend(IRagEmbedder queryEmbedder, int dimensions = 384)
    {
        _queryEmbedder = queryEmbedder
            ?? throw new ArgumentNullException(nameof(queryEmbedder));
        _dimensions = dimensions;
    }

    public bool IsLoaded => _loaded;

    public async Task LoadAsync(string path)
    {
        // (2) Use the same binary format RagDatabaseBuilder writes —
        // see Assets/Sauti/Editor/RagDatabaseBuilder.cs for the layout.
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);

        uint magic = br.ReadUInt32();
        if (magic != 0x01474152u) throw new InvalidDataException("not a Sauti knowledge.db");
        uint dims = br.ReadUInt32();
        if (dims != _dimensions) throw new InvalidDataException($"dim mismatch: file={dims} expected={_dimensions}");
        uint count = br.ReadUInt32();

        var texts = new List<string>((int)count);
        var embeds = new List<float[]>((int)count);
        for (int i = 0; i < count; i++)
        {
            ushort docIdLen = br.ReadUInt16(); br.ReadBytes(docIdLen);    // skip docId
            ushort titleLen = br.ReadUInt16(); br.ReadBytes(titleLen);    // skip title
            uint textLen   = br.ReadUInt32();
            string text = System.Text.Encoding.UTF8.GetString(br.ReadBytes((int)textLen));
            float[] emb = new float[dims];
            for (int d = 0; d < dims; d++) emb[d] = br.ReadSingle();
            texts.Add(text);
            embeds.Add(emb);
        }
        _chunkTexts = texts.ToArray();
        _embeddings = embeds.ToArray();
        _loaded = true;
        await Task.CompletedTask;
    }

    public async Task<(string[] chunks, float[] scores)> SearchAsync(string query, int numResults)
    {
        if (!_loaded) return (Array.Empty<string>(), Array.Empty<float>());
        float[] q = await _queryEmbedder.EmbedAsync(query);     // (3)

        // (4) Brute-force cosine. Embeddings are already L2-normalised
        // (MiniLmRagEmbedder normalises at write time), so dot product = cosine.
        var scored = _embeddings
            .Select((e, i) => (i, score: DotProduct(q, e)))
            .OrderByDescending(t => t.score)
            .Take(numResults)
            .ToArray();

        return (
            scored.Select(t => _chunkTexts[t.i]).ToArray(),
            scored.Select(t => t.score).ToArray());
    }

    private static float DotProduct(float[] a, float[] b)
    {
        double s = 0;
        for (int i = 0; i < a.Length; i++) s += (double)a[i] * b[i];
        return (float)s;
    }
}
```

1. The backend needs a query-time embedder. Same `IRagEmbedder` you used for the offline build — that's the **same encoder** rule from `voice_ai_architecture.md § 4.3`.
2. Reuse the binary format from `RagDatabaseBuilder.WriteDatabase` so the existing build menu still produces files this backend can read.
3. Encode the query into the same vector space as the chunks.
4. Brute-force cosine. For ~10 K chunks of 384-dim vectors this is <10 ms on a desktop CPU.

### Unit-test pattern

Use the `FakeRagBackend` from [`SautiRagTests.cs`](https://github.com/your-org/sauti-unity-plugin/blob/main/Assets/Sauti/Tests/Editor/SautiRagTests.cs) as the template. The same shape applies to any custom backend that holds state:

```csharp
[Test]
public async Task BackendForwardsNumResultsClamped()
{
    var fake = new FakeRagBackend();
    var rag = new SautiRag(fake);
    await rag.LoadAsync(SomeExistingTempPath());

    await rag.SearchAsync("query", numResults: 9999);   // ridiculous

    Assert.AreEqual(50, fake.LastNumResults);            // clamped to MaxNumResults
}

[Test]
public async Task UnloadedBackendReturnsEmpty()
{
    var fake = new FakeRagBackend();
    var rag = new SautiRag(fake);
    // Note: NOT calling LoadAsync.

    (string[] chunks, float[] scores) = await rag.SearchAsync("anything", 3);

    Assert.IsEmpty(chunks);
    Assert.IsEmpty(scores);
}
```

The test pattern works for **any** `ISautiRagBackend` implementation: construct the SUT (system under test), exercise the public surface, assert on either the fake's recorded inputs (for unit-level verification) or on the returned results (for integration-level verification).

---

## Extension point 2 — `IRagEmbedder`

Swap `MiniLmRagEmbedder` for a smaller, faster, or differently-trained sentence encoder.

### The interface

Source: [`Assets/Sauti/Editor/IRagEmbedder.cs`](https://github.com/your-org/sauti-unity-plugin/blob/main/Assets/Sauti/Editor/IRagEmbedder.cs).

```csharp
namespace Sauti.Editor.Rag
{
    public interface IRagEmbedder
    {
        int Dimensions { get; }

        Task<float[]> EmbedAsync(string text);

        Task<float[][]> EmbedBatchAsync(string[] texts);
    }
}
```

Three members. Two methods (one with a batch optimisation seam).

### When to write your own

- **Faster, smaller encoder.** If your corpus is small and well-organised, a tiny encoder (e.g. 64-dim instead of 384-dim) may be enough and uses 6× less RAM in the index.
- **Cross-encoder for re-ranking.** Two-stage retrieval: first pass via MiniLM, second pass via a cross-encoder for the top-20. Implement the cross-encoder as an `IRagEmbedder` that scores pairs (cheap hack: concatenate query + chunk and encode the pair).
- **Hosted embedding API for offline-build-only scenarios.** Build the index from a server-side embedding endpoint; ship only the resulting `knowledge.db`. The runtime still embeds queries locally (it has to — runtime is offline), so you also need a query-time encoder that lives in the same vector space.

!!! danger "The same-encoder rule"
    The encoder that built the index **must** be the same model as the encoder that runs at runtime to embed queries. Different encoders produce different vector spaces; cosine similarity between them is meaningless.

    If you swap `MiniLmRagEmbedder` for a custom encoder, swap it on **both** the build side (Editor) and the runtime query side. The custom `ISautiRagBackend` in the previous section takes an `IRagEmbedder` in its constructor for exactly this reason.

### Minimal stub — a deterministic fake

For tests, you don't even need a real model. A deterministic hash-based fake gives consistent vectors:

```csharp
using System.Threading.Tasks;
using Sauti.Editor.Rag;

public sealed class DeterministicHashEmbedder : IRagEmbedder
{
    public int Dimensions => 8;

    public Task<float[]> EmbedAsync(string text)
    {
        // Map each character to a slot. Deterministic.
        float[] v = new float[8];
        if (text != null)
        {
            foreach (char c in text) v[c % 8] += 1f;
        }
        // L2-normalise so cosine has meaning.
        float norm = 0f;
        for (int i = 0; i < 8; i++) norm += v[i] * v[i];
        norm = (float)System.Math.Sqrt(norm);
        if (norm < 1e-6f) norm = 1e-6f;
        for (int i = 0; i < 8; i++) v[i] /= norm;
        return Task.FromResult(v);
    }

    public async Task<float[][]> EmbedBatchAsync(string[] texts)
    {
        var output = new float[texts.Length][];
        for (int i = 0; i < texts.Length; i++) output[i] = await EmbedAsync(texts[i]);
        return output;
    }
}
```

This isn't useful for retrieval quality, but it's useful for testing the chunker + writer + reader round-trip without dragging the 22 MB MiniLM ONNX into the test suite. See [`RagDatabaseBuilderTests.cs`](https://github.com/your-org/sauti-unity-plugin/blob/main/Assets/Sauti/Tests/Editor/RagDatabaseBuilderTests.cs) for a worked test that uses a fake embedder.

### Worked stub — a smaller real model

If you've trained or quantised a smaller sentence encoder (e.g. a distilled MiniLM at 6-layer / 128-dim), implementing `IRagEmbedder` against raw `Microsoft.ML.OnnxRuntime.InferenceSession` follows the same shape as [`MiniLmRagEmbedder`](api-reference.md#minilmragembedder). The key adaptations:

1. **Adjust `Dimensions`** to match your model's output.
2. **Match the tokeniser** to the model's training. If it's BERT-style, reuse `WordPieceTokenizer` with the model's vocab. If it's BPE / SentencePiece, you need a separate tokeniser.
3. **Decide on the pooling strategy.** MiniLM uses mean-pool + L2 norm (Reimers & Gurevych 2019). Other models bake the pooling into the ONNX graph, in which case you just read the output directly.

The 250-line `MiniLmRagEmbedder.cs` is a good template — copy it, rename, adjust constants. The `Dispose`, `EnsureInitialised`, and dynamic input-name discovery patterns are reusable as-is.

### Unit-test pattern

```csharp
[Test]
public async Task EmbedderProducesUnitVector()
{
    var embedder = new DeterministicHashEmbedder();
    float[] v = await embedder.EmbedAsync("hello world");

    float norm = v.Select(x => x * x).Sum();
    Assert.That(norm, Is.EqualTo(1f).Within(1e-5));
}

[Test]
public async Task BatchEqualsPerItem()
{
    var embedder = new DeterministicHashEmbedder();
    string[] texts = { "alpha", "beta", "gamma" };

    float[][] batch = await embedder.EmbedBatchAsync(texts);
    float[] single = await embedder.EmbedAsync("beta");

    CollectionAssert.AreEqual(single, batch[1]);
}
```

These tests don't validate the *quality* of the embedder (that requires a labelled dataset and a recall-at-k benchmark) — they validate the *contract*: vectors are unit-length, the batch path matches the single path.

---

## Extension point 3 — Custom prompt assembler

Sauti doesn't ship a prompt-assembler **interface** — the `BuildPrompt` method in `FullVoiceLoop.cs` is a **convention**, not a requirement. You're free to assemble prompts however your game needs.

### When to write your own

- **You want per-character system prompts.** The reference scaffold uses one global system prompt. If you have ten NPCs with distinct personas, you'll want to compose a system prompt from the NPC's [template](../designer-guide/templates.md) `persona` block and assign it to a per-NPC `LLMAgent`.
- **You're using structured-output templates.** The `structured-output.json` template encodes action schemas; you splice them into the prompt as additional rules.
- **You need conditional rules.** Different rules for different game states (e.g. "in combat, keep responses under 15 words"; "in dialogue, allow up to 40").

### The reference shape (per-turn)

```csharp
public string BuildPrompt(string userMessage, string[] ragChunks)
{
    var sb = new StringBuilder();
    sb.Append(TemporaryMemory.BuildPromptBlock());  // Layer 2

    if (ragChunks != null && ragChunks.Length > 0)
    {
        sb.AppendLine("Relevant context:");
        foreach (var chunk in ragChunks) sb.AppendLine($"- {chunk}");
        sb.AppendLine();
    }

    sb.Append("User: ").AppendLine(userMessage);
    sb.Append("Assistant: ");
    return sb.ToString();
}
```

### The reference shape (system prompt, once at setup)

```csharp
private static string AssembleSystemPrompt()
{
    return
        "Respond only in plain spoken English sentences. " +
        "No markdown, asterisks, bullet points, headers, or lists. " +
        "Keep every response under 40 words. " +
        "Speak as if in a live conversation. " +
        "/no_think";
}

// In Awake:
_llmAgent.systemPrompt = AssembleSystemPrompt();
```

### Worked variant — per-NPC persona injection

```csharp
public sealed class PersonaPromptAssembler
{
    private readonly string _npcPersona;
    private readonly int _maxWords;
    private readonly bool _appendNoThink;

    public PersonaPromptAssembler(NpcDialogueTemplate template)
    {
        _npcPersona = template.persona.summary;
        _maxWords = template.promptRules.maxWordsPerResponse;
        _appendNoThink = template.promptRules.noThink;
    }

    public string AssembleSystemPrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are: " + _npcPersona);
        sb.AppendLine("Respond only in plain spoken English sentences.");
        sb.AppendLine("No markdown, asterisks, bullet points, headers, or lists.");
        sb.AppendLine($"Keep every response under {_maxWords} words.");
        sb.AppendLine("Speak as if in a live conversation.");
        if (_appendNoThink) sb.Append("/no_think");
        return sb.ToString();
    }

    public string AssembleTurnPrompt(string userMessage, string[] ragChunks)
    {
        var sb = new StringBuilder();
        sb.Append(TemporaryMemory.BuildPromptBlock());
        if (ragChunks?.Length > 0)
        {
            sb.AppendLine("Relevant context:");
            foreach (var c in ragChunks) sb.AppendLine($"- {c}");
            sb.AppendLine();
        }
        sb.Append("User: ").AppendLine(userMessage);
        sb.Append(_npcPersona.Split(' ')[0] /* NPC's first word as label */).Append(": ");
        return sb.ToString();
    }
}
```

### `/no_think` is per-model

When you replace `Gemma3-1B` (deferred) for Quest in a future version, that model **does not honour** `/no_think`. The directive becomes harmless-but-pointless on Gemma. The clean fix is to key the directive off the resolved model filename / manifest:

```csharp
// Sketch — query the manifest at runtime:
bool supportsNoThink = ModelManifest.Lookup(modelFileName).supportsNoThinkDirective;
if (supportsNoThink) sb.AppendLine("/no_think");
```

The `supportsNoThinkDirective` field is part of the per-model [manifest schema](../reference/manifests.md).

### Unit-test pattern

Prompt assemblers are pure string functions — test them like any pure function:

```csharp
[Test]
public void SystemPromptIncludesAllFourRules()
{
    var assembler = new PersonaPromptAssembler(SomeFakeTemplate());
    string prompt = assembler.AssembleSystemPrompt();

    StringAssert.Contains("plain spoken English", prompt);
    StringAssert.Contains("No markdown", prompt);
    StringAssert.Contains("under 40 words", prompt);
    StringAssert.Contains("live conversation", prompt);
}

[Test]
public void TurnPromptIncludesRagChunksWhenProvided()
{
    var assembler = new PersonaPromptAssembler(SomeFakeTemplate());
    string prompt = assembler.AssembleTurnPrompt(
        userMessage: "Where is the artifact?",
        ragChunks: new[] { "Elder Maren knows where the artifact is." });

    StringAssert.Contains("Elder Maren", prompt);
    StringAssert.Contains("Where is the artifact?", prompt);
}
```

---

## Putting it all together — a fully-custom pipeline

```csharp
// 1. Custom embedder.
var embedder = new MyDistilledMiniLm(modelPath: "...", dimensions: 128);

// 2. Custom backend that uses the embedder for queries.
var backend = new FlatCosineRagBackend(embedder, dimensions: 128);
await backend.LoadAsync(Path.Combine(Application.streamingAssetsPath, "VoiceAI/rag/knowledge.db"));

// 3. Wrap in the Sauti façade for the consistent surface.
var rag = new SautiRag(backend);

// 4. Custom prompt assembler.
var template = LoadNpcTemplate("elder-maren.json");
var assembler = new PersonaPromptAssembler(template);

// 5. LLMUnity LLM + Agent as usual.
var llm = gameObject.AddComponent<LLM>();
llm.SetModel(qwen3Path);
await llm.WaitUntilReady();

var llmAgent = gameObject.AddComponent<LLMAgent>();
llmAgent.llm = llm;
llmAgent.systemPrompt = assembler.AssembleSystemPrompt();

// 6. Per turn — Sauti's three layers, your custom backend, your custom prompt.
(string[] chunks, _) = await rag.SearchAsync(userMessage, numResults: 3);
string turnPrompt = assembler.AssembleTurnPrompt(userMessage, chunks);
string reply = await llmAgent.Chat(turnPrompt, OnCumulative, OnComplete, addToHistory: true);
```

Five Sauti types touched (`IRagEmbedder`, `ISautiRagBackend`, `SautiRag`, `TemporaryMemory`, the prompt-assembler convention) — every one swappable.

---

## What you can't easily extend (and why)

- **Whisper / Kokoro / LLMUnity themselves.** These are upstream packages. Replacing them means writing your own STT / TTS / LLM runner — significantly more work than swapping a Sauti interface.
- **The three-layer memory model.** The fact that there are three layers (history, temp, RAG) and how they combine in `BuildPrompt` is the **spec**, not a Sauti convention. A two-layer or four-layer model is fine as a design choice but would diverge from the v1.2 architecture and would need a new spec doc.

---

## Cross-references

- The interfaces: [`ISautiRagBackend`](api-reference.md#isautiragbackend), [`IRagEmbedder`](api-reference.md#iragembedder).
- The defaults: [`LlmUnityRagBackend`](api-reference.md#llmunityragbackend), [`MiniLmRagEmbedder`](api-reference.md#minilmragembedder).
- The reference orchestrator: [`experiments/05-full-voice-loop/FullVoiceLoop.cs`](https://github.com/your-org/sauti-unity-plugin/blob/main/experiments/05-full-voice-loop/FullVoiceLoop.cs).
- The tests to read for the test-pattern shape: [`SautiRagTests.cs`](https://github.com/your-org/sauti-unity-plugin/blob/main/Assets/Sauti/Tests/Editor/SautiRagTests.cs), [`RagDatabaseBuilderTests.cs`](https://github.com/your-org/sauti-unity-plugin/blob/main/Assets/Sauti/Tests/Editor/RagDatabaseBuilderTests.cs).
