using System;
using CodeShot.ToolWindows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeShot.Test
{
    [TestClass]
    public class BoundedEditHistoryTests
    {
        [TestMethod]
        public void Constructor_RejectsNonPositiveCapacity()
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new BoundedEditHistory<string>(0));
        }

        [TestMethod]
        public void Record_EnablesUndoAndClearsRedo()
        {
            var history = new BoundedEditHistory<string>(3);
            history.Record("first");
            history.TryUndo("second", out _);

            history.Record("branch");

            Assert.IsTrue(history.CanUndo);
            Assert.IsFalse(history.CanRedo);
        }

        [TestMethod]
        public void UndoAndRedo_MoveCurrentStateBetweenStacks()
        {
            var history = new BoundedEditHistory<string>(3);
            history.Record("first");
            history.Record("second");

            Assert.IsTrue(history.TryUndo("third", out var second));
            Assert.AreEqual("second", second);
            Assert.IsTrue(history.TryUndo(second, out var first));
            Assert.AreEqual("first", first);
            Assert.IsFalse(history.CanUndo);

            Assert.IsTrue(history.TryRedo(first, out second));
            Assert.AreEqual("second", second);
            Assert.IsTrue(history.TryRedo(second, out var third));
            Assert.AreEqual("third", third);
            Assert.IsFalse(history.CanRedo);
        }

        [TestMethod]
        public void Record_DropsOldestStateAtCapacity()
        {
            var history = new BoundedEditHistory<int>(2);
            history.Record(1);
            history.Record(2);
            history.Record(3);

            Assert.IsTrue(history.TryUndo(4, out var state));
            Assert.AreEqual(3, state);
            Assert.IsTrue(history.TryUndo(state, out state));
            Assert.AreEqual(2, state);
            Assert.IsFalse(history.TryUndo(state, out _));
        }

        [TestMethod]
        public void Clear_RemovesUndoAndRedoStates()
        {
            var history = new BoundedEditHistory<string>(3);
            history.Record("first");
            history.TryUndo("second", out _);

            history.Clear();

            Assert.IsFalse(history.CanUndo);
            Assert.IsFalse(history.CanRedo);
        }
    }
}
