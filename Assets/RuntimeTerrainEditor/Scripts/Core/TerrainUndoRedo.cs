using System;
using System.Collections.Generic;

namespace RuntimeTerrainEditor
{
    /// <summary>One step in the undo history.</summary>
    public interface ITerrainUndoAction
    {
        /// <summary>Approximate memory the step holds, counted against the history's memory budget.</summary>
        long SizeInBytes { get; }

        /// <summary>Puts back how things were before this step.</summary>
        void Undo();

        /// <summary>Applies this step again after it was undone.</summary>
        void Redo();
    }

    /// <summary>
    /// Bounded undo/redo history for terrain edits. The oldest steps are dropped once the history has too many steps or
    /// holds too much memory; the newest step is always kept.
    /// </summary>
    public class TerrainUndoRedo
    {
        private readonly LinkedList<ITerrainUndoAction> _undo = new LinkedList<ITerrainUndoAction>();
        private readonly Stack<ITerrainUndoAction> _redo = new Stack<ITerrainUndoAction>();
        private readonly int _maxSteps;
        private readonly long _maxBytes;
        private long _undoBytes;
        private long _redoBytes;

        /// <summary>Number of steps that can be undone.</summary>
        public int UndoCount => _undo.Count;

        /// <summary>Number of steps that can be redone.</summary>
        public int RedoCount => _redo.Count;

        /// <summary>Approximate memory all undo and redo steps hold.</summary>
        public long SizeInBytes => _undoBytes + _redoBytes;

        /// <summary>
        /// Creates an empty history that keeps at most <paramref name="maxSteps"/> undo steps and about
        /// <paramref name="maxBytes"/> of memory.
        /// </summary>
        public TerrainUndoRedo(int maxSteps, long maxBytes = long.MaxValue)
        {
            _maxSteps = maxSteps < 1 ? 1 : maxSteps;
            _maxBytes = maxBytes < 1 ? 1 : maxBytes;
        }

        /// <summary>Adds a finished step. Clears the redo steps, since they no longer follow from the current state.</summary>
        public void Record(ITerrainUndoAction action)
        {
            _redo.Clear();
            _redoBytes = 0;
            _undo.AddLast(action);
            _undoBytes += action.SizeInBytes;
            while (_undo.Count > 1 && (_undo.Count > _maxSteps || _undoBytes > _maxBytes))
            {
                _undoBytes -= _undo.First.Value.SizeInBytes;
                _undo.RemoveFirst();
            }
        }

        /// <summary>Undoes the latest step and returns it, or null when there is nothing to undo.</summary>
        public ITerrainUndoAction Undo()
        {
            if (_undo.Count == 0)
                return null;
            ITerrainUndoAction action = _undo.Last.Value;
            _undo.RemoveLast();
            _undoBytes -= action.SizeInBytes;
            action.Undo();
            _redo.Push(action);
            _redoBytes += action.SizeInBytes;
            return action;
        }

        /// <summary>Redoes the latest undone step and returns it, or null when there is nothing to redo.</summary>
        public ITerrainUndoAction Redo()
        {
            if (_redo.Count == 0)
                return null;
            ITerrainUndoAction action = _redo.Pop();
            _redoBytes -= action.SizeInBytes;
            action.Redo();
            _undo.AddLast(action);
            _undoBytes += action.SizeInBytes;
            return action;
        }

        /// <summary>Forgets all undo and redo steps.</summary>
        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
            _undoBytes = 0;
            _redoBytes = 0;
        }

        /// <summary>True when any undo or redo step matches.</summary>
        public bool Contains(Predicate<ITerrainUndoAction> match)
        {
            foreach (ITerrainUndoAction action in _undo)
            {
                if (match(action))
                    return true;
            }
            foreach (ITerrainUndoAction action in _redo)
            {
                if (match(action))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Drops the undo and redo steps that match, keeping the order of the rest.
        /// Only use it for steps that don't depend on each other (e.g. paint steps, which never touch heights).
        /// </summary>
        public void RemoveAll(Predicate<ITerrainUndoAction> match)
        {
            LinkedListNode<ITerrainUndoAction> node = _undo.First;
            while (node != null)
            {
                LinkedListNode<ITerrainUndoAction> next = node.Next;
                if (match(node.Value))
                {
                    _undoBytes -= node.Value.SizeInBytes;
                    _undo.Remove(node);
                }
                node = next;
            }

            ITerrainUndoAction[] redo = _redo.ToArray(); // top of the stack first
            _redo.Clear();
            _redoBytes = 0;
            for (int i = redo.Length - 1; i >= 0; i--)
            {
                if (match(redo[i]))
                    continue;
                _redo.Push(redo[i]);
                _redoBytes += redo[i].SizeInBytes;
            }
        }
    }
}
