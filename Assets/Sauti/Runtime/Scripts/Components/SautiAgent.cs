// Assets/Sauti/Runtime/Scripts/Components/SautiAgent.cs
//
// v1.3 — top-level orchestrator. Composes:
//   {SautiLlmConfig.systemPrompt} +
//   {TemporaryMemory.BuildPromptBlock()}    (opt-in)
//   {top-K RAG chunks from SautiKnowledgeBase}  (opt-in)
//   {user question}
// into a single prompt, then either:
//   • forwards it to a developer-supplied ILlmCompleter (code path), or
//   • emits OnPromptReady so the designer wires it into LLMUnity / their own
//     LLM in the Inspector (no-code path).
//
// Bringing back the reply uses AcceptReply(string) — the designer wires
// LLM.OnReply → SautiAgent.AcceptReply.  The agent then fires OnReplyReady
// (and pipes into the Speaker if one is attached).
//
// Programmer usage: nothing here is required. You can keep assembling prompts
// in your own code with SautiRag + TemporaryMemory + raw LLMUnity calls.
// This component is purely additive.

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sauti.Memory;
using UnityEngine;
using UnityEngine.Events;

namespace Sauti.Components
{
    /// <summary>
    /// Code-side hook: implement this on any object (MonoBehaviour or POCO)
    /// to plug a custom LLM completer into SautiAgent.AskAsync.
    /// </summary>
    public interface ILlmCompleter
    {
        Task<string> CompleteAsync(string prompt, CancellationToken ct = default);
    }

    [AddComponentMenu("Sauti/Sauti Agent")]
    public sealed class SautiAgent : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("Optional — drag in a SautiSpeaker to auto-speak each reply.")]
        [SerializeField] private SautiSpeaker speaker;

        [Tooltip("Optional — drag in a SautiKnowledgeBase to inject RAG context.")]
        [SerializeField] private SautiKnowledgeBase knowledge;

        [Tooltip("Prompt shape + RAG injection flags. Create via " +
                 "Assets → Create → Sauti → LLM Config.")]
        [SerializeField] private SautiLlmConfig llmConfig;

        [Header("Events")]
        [Tooltip("Fired with the fully-assembled prompt. Wire this to your LLM's " +
                 "Chat / Complete entry point. The reply must come back via " +
                 "AcceptReply(string).")]
        public UnityEvent<string> OnPromptReady = new UnityEvent<string>();

        [Tooltip("Fired once the assistant reply is delivered via AcceptReply " +
                 "(or via AskAsync's ILlmCompleter return).")]
        public UnityEvent<string> OnReplyReady = new UnityEvent<string>();

        [Tooltip("Fired if prompt assembly or the code-path completer throws.")]
        public UnityEvent<string> OnAskError = new UnityEvent<string>();

        public SautiSpeaker Speaker { get => speaker; set => speaker = value; }
        public SautiKnowledgeBase Knowledge { get => knowledge; set => knowledge = value; }
        public SautiLlmConfig LlmConfig { get => llmConfig; set => llmConfig = value; }

        /// <summary>
        /// Code path — build the prompt, hand it to <paramref name="completer"/>,
        /// and pipe the reply through OnReplyReady + the attached Speaker.
        /// </summary>
        public async Task<string> AskAsync(
            string question,
            ILlmCompleter completer,
            CancellationToken ct = default)
        {
            if (completer == null) throw new ArgumentNullException(nameof(completer));
            string prompt = await BuildPromptAsync(question, ct).ConfigureAwait(true);
            OnPromptReady?.Invoke(prompt);

            string reply;
            try
            {
                reply = await completer.CompleteAsync(prompt, ct).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Sauti][Agent] completer threw: {ex}", this);
                OnAskError?.Invoke(ex.Message);
                throw;
            }

            await DeliverReplyAsync(reply, ct).ConfigureAwait(true);
            return reply;
        }

        /// <summary>
        /// Designer path — build the prompt and emit OnPromptReady. The reply
        /// must come back through <see cref="AcceptReply"/>.
        /// </summary>
        public async Task EmitPromptAsync(string question, CancellationToken ct = default)
        {
            try
            {
                string prompt = await BuildPromptAsync(question, ct).ConfigureAwait(true);
                OnPromptReady?.Invoke(prompt);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Sauti][Agent] EmitPrompt failed: {ex}", this);
                OnAskError?.Invoke(ex.Message);
            }
        }

        /// <summary>
        /// Designer path — receive the LLM's reply. Fires OnReplyReady and
        /// (if a Speaker is wired) auto-speaks the reply. Safe to wire to a
        /// UnityEvent\&lt;string\&gt;.
        /// </summary>
        public void AcceptReply(string reply) { _ = DeliverReplyAsync(reply, default); }

        /// <summary>
        /// Assemble the system prompt + memory + RAG context + user question
        /// into a single string. Public so tests and custom callers can inspect
        /// the exact shape.
        /// </summary>
        public async Task<string> BuildPromptAsync(string question, CancellationToken ct = default)
        {
            var sb = new StringBuilder();

            if (llmConfig != null)
            {
                if (!string.IsNullOrWhiteSpace(llmConfig.systemPrompt))
                    sb.AppendLine(llmConfig.systemPrompt);

                if (llmConfig.injectTemporaryMemory)
                {
                    string mem = TemporaryMemory.BuildPromptBlock();
                    if (!string.IsNullOrEmpty(mem)) sb.AppendLine(mem);
                }
            }

            bool injectRag = llmConfig != null && llmConfig.injectRagContext && knowledge != null;
            if (injectRag)
            {
                int? topKOverride = (llmConfig.topKOverride > 0) ? llmConfig.topKOverride : (int?)null;
                var (chunks, _) = await knowledge.RetrieveAsync(question, topKOverride, ct)
                                                  .ConfigureAwait(true);
                if (chunks != null && chunks.Length > 0)
                {
                    sb.AppendLine("Context:");
                    for (int i = 0; i < chunks.Length; i++)
                    {
                        sb.Append("- ");
                        sb.AppendLine(chunks[i]);
                    }
                    sb.AppendLine();
                }
            }

            sb.Append("Question: ");
            sb.Append(question);

            if (llmConfig != null && llmConfig.useNoThinkDirective)
            {
                sb.AppendLine();
                sb.Append("/no_think");
            }

            return sb.ToString();
        }

        private async Task DeliverReplyAsync(string reply, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(reply)) return;
            OnReplyReady?.Invoke(reply);

            if (speaker != null)
            {
                try { await speaker.SpeakAsync(reply, ct).ConfigureAwait(true); }
                catch (Exception ex) { Debug.LogError($"[Sauti][Agent] Speaker threw: {ex}", this); }
            }
        }
    }
}
