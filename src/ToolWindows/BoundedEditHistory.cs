using System;
using System.Collections.Generic;

namespace CodeShot.ToolWindows
{
    internal sealed class BoundedEditHistory<T>
    {
        private readonly int _capacity;
        private readonly List<T> _undo = new List<T>();
        private readonly List<T> _redo = new List<T>();

        internal BoundedEditHistory(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _capacity = capacity;
        }

        internal bool CanUndo => _undo.Count > 0;
        internal bool CanRedo => _redo.Count > 0;

        internal void Record(T current)
        {
            Push(_undo, current);
            _redo.Clear();
        }

        internal bool TryUndo(T current, out T state)
            => TryMove(_undo, _redo, current, out state);

        internal bool TryRedo(T current, out T state)
            => TryMove(_redo, _undo, current, out state);

        internal void Clear()
        {
            _undo.Clear();
            _redo.Clear();
        }

        private bool TryMove(List<T> source, List<T> destination, T current, out T state)
        {
            if (source.Count == 0)
            {
                state = default!;
                return false;
            }

            Push(destination, current);
            var index = source.Count - 1;
            state = source[index];
            source.RemoveAt(index);
            return true;
        }

        private void Push(List<T> history, T state)
        {
            if (history.Count >= _capacity)
            {
                history.RemoveAt(0);
            }

            history.Add(state);
        }
    }
}
