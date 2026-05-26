// Assets/Sauti/Tests/Editor/TemporaryMemoryTests.cs
//
// MEM-001 test coverage. Four cases per Session 4 brief:
//   1. Empty store produces empty block.
//   2. Single fact produces the canonical prompt block.
//   3. Clear() empties the store.
//   4. Multiple facts join correctly into a single line.
//
// Tests run in EditMode (no Unity scene needed). Each test calls Clear() in
// SetUp so the static store does not leak between cases.

using NUnit.Framework;
using Sauti.Memory;

namespace Sauti.Tests.Memory
{
    [TestFixture]
    public class TemporaryMemoryTests
    {
        [SetUp]
        public void ResetStoreBeforeEachTest()
        {
            TemporaryMemory.Clear();
        }

        [Test]
        public void BuildPromptBlock_EmptyStore_ReturnsEmptyString()
        {
            string block = TemporaryMemory.BuildPromptBlock();
            Assert.AreEqual(string.Empty, block);
        }

        [Test]
        public void BuildPromptBlock_SingleFact_ReturnsCanonicalLine()
        {
            TemporaryMemory.Set("player_name", "Marcus");

            string block = TemporaryMemory.BuildPromptBlock();

            Assert.AreEqual("Known facts about this session: player_name=Marcus.\n", block);
        }

        [Test]
        public void Clear_AfterSet_EmptiesStore()
        {
            TemporaryMemory.Set("player_name", "Marcus");
            TemporaryMemory.Set("current_quest", "Find the lost artifact");

            TemporaryMemory.Clear();

            Assert.AreEqual(string.Empty, TemporaryMemory.BuildPromptBlock());
        }

        [Test]
        public void BuildPromptBlock_MultipleFacts_JoinsWithCommaSeparator()
        {
            TemporaryMemory.Set("player_name", "Marcus");
            TemporaryMemory.Set("current_quest", "Find the lost artifact");

            string block = TemporaryMemory.BuildPromptBlock();

            // Ordering of Dictionary enumeration is insertion-ordered in modern .NET
            // (Mono / IL2CPP / .NET Core+), but to keep this test robust we assert
            // containment rather than exact string equality.
            StringAssert.StartsWith("Known facts about this session: ", block);
            StringAssert.EndsWith(".\n", block);
            StringAssert.Contains("player_name=Marcus", block);
            StringAssert.Contains("current_quest=Find the lost artifact", block);
            StringAssert.Contains(", ", block);
        }

        [Test]
        public void Set_TwiceOnSameKey_OverwritesValue()
        {
            // Bonus assertion derived from spec: Dictionary[key] = value semantics.
            TemporaryMemory.Set("player_name", "Marcus");
            TemporaryMemory.Set("player_name", "Aria");

            string block = TemporaryMemory.BuildPromptBlock();

            Assert.AreEqual("Known facts about this session: player_name=Aria.\n", block);
        }
    }
}
