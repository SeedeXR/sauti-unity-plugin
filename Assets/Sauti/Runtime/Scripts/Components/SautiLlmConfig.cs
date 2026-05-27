// Assets/Sauti/Runtime/Scripts/Components/SautiLlmConfig.cs
//
// v1.3 — ScriptableObject preset for LLM prompting parameters.
// SautiAgent reads this when assembling its prompt; the actual LLM call goes
// through LLMUnity (or any ILlmCompleter the developer wires in) so this SO
// holds only the prompt-shape settings, not the model itself.

using UnityEngine;

namespace Sauti.Components
{
    [CreateAssetMenu(
        fileName = "LlmConfig",
        menuName = "Sauti/LLM Config",
        order = 102)]
    public sealed class SautiLlmConfig : ScriptableObject
    {
        [Header("Prompt shape")]
        [Tooltip("System / persona prompt — prepended to every conversation turn.")]
        [TextArea(3, 10)]
        public string systemPrompt = "You are a helpful AI assistant. Be concise.";

        [Tooltip("If true, appends '/no_think' to the assembled prompt. " +
                 "Qwen3-specific directive that skips the model's internal " +
                 "<think> reasoning trace — fastest reply path for game NPCs. " +
                 "Has no effect on Gemma / Llama / other model families.")]
        public bool useNoThinkDirective = true;

        [Header("Retrieval injection")]
        [Tooltip("If true and the agent has a SautiKnowledgeBase wired, the " +
                 "top retrieved chunks are prepended to the user message as " +
                 "'Context:\\n<chunks>\\n\\nQuestion:\\n<question>'.")]
        public bool injectRagContext = true;

        [Tooltip("Override the SautiKnowledgeConfig.defaultTopK for this agent. " +
                 "0 = use the config's default.")]
        [Range(0, 20)]
        public int topKOverride = 0;

        [Header("Memory")]
        [Tooltip("If true, prepends the global TemporaryMemory.BuildPromptBlock() " +
                 "(session facts set via TemporaryMemory.Set) to the system prompt.")]
        public bool injectTemporaryMemory = true;
    }
}
