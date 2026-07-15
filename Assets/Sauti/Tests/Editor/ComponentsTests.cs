// Assets/Sauti/Tests/Editor/ComponentsTests.cs
//
// v1.3 — sanity tests for the new SO + MonoBehaviour layer. EditMode-only;
// no Play mode, no LLM, no Kokoro runner. These verify:
//   • The three ScriptableObjects construct with sane defaults.
//   • SautiAgent.BuildPromptAsync produces the expected shape under various
//     opt-in flag combinations — system prompt, /no_think, TemporaryMemory,
//     and RAG context. RAG is fed via a FakeRagBackend (same pattern as
//     SautiRagTests) so we don't depend on real ONNX / LLMUnity.
//   • The agent's reply-event path fires OnReplyReady when AcceptReply is called.

using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Sauti.Components;
using Sauti.Memory;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sauti.Tests.Components
{
    public class ComponentsTests
    {
        // --- ScriptableObject defaults -------------------------------------

        [Test]
        public void SautiVoiceProfile_HasSaneDefaults()
        {
            var p = ScriptableObject.CreateInstance<SautiVoiceProfile>();
            Assert.AreEqual("af_bella", p.voiceId);
            Assert.AreEqual(1.0f, p.speed, 1e-6);
            StringAssert.Contains("VoiceAI/tts/", p.modelPathRelative);
            StringAssert.Contains("VoiceAI/tts/voices", p.voicesDirectoryPathRelative);
            Assert.IsTrue(string.IsNullOrEmpty(p.tokenizerPathRelative),
                "Default tokenizer path should be blank so the embedded vocab is used.");
        }

        [Test]
        public void SautiKnowledgeConfig_HasSaneDefaults()
        {
            var c = ScriptableObject.CreateInstance<SautiKnowledgeConfig>();
            Assert.AreEqual(3, c.defaultTopK);
            StringAssert.Contains("VoiceAI/rag/", c.knowledgeDbPathRelative);
            StringAssert.Contains("VoiceAI/embeddings/", c.embeddingModelPathRelative);
            StringAssert.AreEqualIgnoringCase("knowledge-base", c.knowledgeBaseSourceDir);
        }

        [Test]
        public void SautiLlmConfig_HasSaneDefaults()
        {
            var c = ScriptableObject.CreateInstance<SautiLlmConfig>();
            Assert.IsNotEmpty(c.systemPrompt);
            Assert.IsTrue(c.useNoThinkDirective, "Qwen3 path: /no_think on by default.");
            Assert.IsTrue(c.injectRagContext);
            Assert.IsTrue(c.injectTemporaryMemory);
            Assert.AreEqual(0, c.topKOverride, "0 means 'use config default'.");
        }

        // --- SautiAgent.BuildPromptAsync -----------------------------------

        [Test]
        public async Task BuildPrompt_WithSystemAndQuestion_Only()
        {
            var agent = NewAgent(out _, withSpeaker: false, withKnowledge: false);
            string prompt = await agent.BuildPromptAsync("Who is Elder Maren?");
            StringAssert.Contains("You are a helpful AI assistant", prompt);
            StringAssert.Contains("Question: Who is Elder Maren?", prompt);
            StringAssert.Contains("/no_think", prompt);
            StringAssert.DoesNotContain("Context:", prompt);
            Object.DestroyImmediate(agent.gameObject);
        }

        [Test]
        public async Task BuildPrompt_WithRagContext_PrependsRetrievedChunks()
        {
            var agent = NewAgent(out var kb, withSpeaker: false, withKnowledge: true);
            kb.Initialise(new FakeRagBackend(
                new[] { "Elder Maren is the village storyteller.", "She lives in Frostmere." },
                new[] { 0.92f, 0.81f }));

            string prompt = await agent.BuildPromptAsync("Who is Elder Maren?");
            StringAssert.Contains("Context:", prompt);
            StringAssert.Contains("Elder Maren is the village storyteller.", prompt);
            StringAssert.Contains("She lives in Frostmere.", prompt);
            StringAssert.Contains("Question: Who is Elder Maren?", prompt);
            Object.DestroyImmediate(agent.gameObject);
        }

        [Test]
        public async Task BuildPrompt_TempMemory_InjectsKnownFacts()
        {
            TemporaryMemory.Clear();
            TemporaryMemory.Set("player_name", "Aria");
            try
            {
                var agent = NewAgent(out _, withSpeaker: false, withKnowledge: false);
                string prompt = await agent.BuildPromptAsync("hello");
                StringAssert.Contains("player_name=Aria", prompt);
                Object.DestroyImmediate(agent.gameObject);
            }
            finally { TemporaryMemory.Clear(); }
        }

        [Test]
        public async Task BuildPrompt_RespectsConfigToggles()
        {
            var agent = NewAgent(out _, withSpeaker: false, withKnowledge: false);
            agent.LlmConfig.useNoThinkDirective = false;
            agent.LlmConfig.injectTemporaryMemory = false;

            TemporaryMemory.Clear();
            TemporaryMemory.Set("ignored", "true");
            try
            {
                string prompt = await agent.BuildPromptAsync("hello");
                StringAssert.DoesNotContain("/no_think", prompt);
                StringAssert.DoesNotContain("ignored=true", prompt);
                Object.DestroyImmediate(agent.gameObject);
            }
            finally { TemporaryMemory.Clear(); }
        }

        // --- AcceptReply event path ----------------------------------------

        [Test]
        public void AcceptReply_FiresOnReplyReady()
        {
            var agent = NewAgent(out _, withSpeaker: false, withKnowledge: false);
            string received = null;
            agent.OnReplyReady.AddListener(r => received = r);
            agent.AcceptReply("Hello, traveller.");
            Assert.AreEqual("Hello, traveller.", received);
            Object.DestroyImmediate(agent.gameObject);
        }

        // --- SautiSpeaker CTS lifecycle (B6) ---------------------------------

        [Test]
        public async Task SpeakAsync_ReEntry_DisposesSupersededCts()
        {
            var go = new GameObject("test-speaker");
            go.AddComponent<AudioSource>();
            var sp = go.AddComponent<SautiSpeaker>();
            var profile = ScriptableObject.CreateInstance<SautiVoiceProfile>();
            profile.modelPathRelative = "VoiceAI/tts/does-not-exist.onnx";
            sp.Profile = profile;

            try
            {
                // Each call fails at EnsureRunner (missing model) AFTER creating
                // its linked CTS — exactly the leak path: the superseded CTS was
                // cancelled but never disposed, so its registration on the
                // external token kept it alive. The speaker logs each failure
                // as an error by design; don't let those fail the test.
                LogAssert.ignoreFailingMessages = true;
                await sp.SpeakAsync("one");
                var ctsField = typeof(SautiSpeaker).GetField(
                    "_activeCts", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(ctsField, "SautiSpeaker._activeCts field renamed? Update this test.");
                var first = (CancellationTokenSource)ctsField.GetValue(sp);
                Assert.IsNotNull(first, "SpeakAsync should have created a linked CTS.");

                await sp.SpeakAsync("two");
                // CancellationTokenSource.Token throws once the source is disposed.
                Assert.Throws<System.ObjectDisposedException>(
                    () => _ = first.Token,
                    "Re-entrant SpeakAsync must dispose the superseded CTS.");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(profile);
            }
        }

        // --- Helpers --------------------------------------------------------

        private static SautiAgent NewAgent(out SautiKnowledgeBase kb, bool withSpeaker, bool withKnowledge)
        {
            var go = new GameObject("test-agent");
            var agent = go.AddComponent<SautiAgent>();
            agent.LlmConfig = ScriptableObject.CreateInstance<SautiLlmConfig>();

            kb = null;
            if (withKnowledge)
            {
                kb = go.AddComponent<SautiKnowledgeBase>();
                kb.Config = ScriptableObject.CreateInstance<SautiKnowledgeConfig>();
                agent.Knowledge = kb;
            }
            if (withSpeaker)
            {
                go.AddComponent<AudioSource>();
                var sp = go.AddComponent<SautiSpeaker>();
                sp.Profile = ScriptableObject.CreateInstance<SautiVoiceProfile>();
                agent.Speaker = sp;
            }
            return agent;
        }

        private sealed class FakeRagBackend : ISautiRagBackend
        {
            private readonly string[] _chunks;
            private readonly float[] _scores;
            public FakeRagBackend(string[] chunks, float[] scores) { _chunks = chunks; _scores = scores; }
            public bool IsLoaded => true;
            public Task LoadAsync(string path) => Task.CompletedTask;
            public Task<(string[] chunks, float[] scores)> SearchAsync(string query, int numResults)
                => Task.FromResult((_chunks, _scores));
        }
    }
}
