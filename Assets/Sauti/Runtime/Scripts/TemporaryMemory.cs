// Assets/Sauti/Runtime/Scripts/TemporaryMemory.cs
//
// MEM-001 — Layer 2 of the three-layer memory architecture.
// Spec: memory/voice_ai_architecture.md § 4.2.
//
// Session-scoped key/value store for named facts the AI learns mid-session
// (player name, current quest, stated preferences). Survives turn rollovers;
// cleared by the host on app exit / scene unload.
//
// Pure C# — no UnityEngine API dependency — so it is unit-testable headlessly
// (see Assets/Sauti/Tests/Editor/TemporaryMemoryTests.cs).

using System.Collections.Generic;
using System.Linq;

namespace Sauti.Memory
{
    public static class TemporaryMemory
    {
        private static readonly Dictionary<string, string> _store = new Dictionary<string, string>();

        public static void Set(string key, string value) => _store[key] = value;

        public static void Clear() => _store.Clear();

        public static string BuildPromptBlock()
        {
            if (_store.Count == 0) return string.Empty;
            var facts = string.Join(", ", _store.Select(kv => $"{kv.Key}={kv.Value}"));
            return $"Known facts about this session: {facts}.\n";
        }
    }
}
