using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using DynamicData;

namespace WhisperVoiceInput.Services.LogViewer;

// Minimal ring-buffer-backed implementation of ISourceList<T> / IObservableList<T>
public sealed class RingSourceList<T> : ISourceList<T> where T : notnull
{
    private sealed class RingBuffer
    {
        private T[] _buffer;
        private int _head;
        private int _count;

        public RingBuffer(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _buffer = new T[capacity];
            _head = 0;
            _count = 0;
        }

        public int Capacity => _buffer.Length;
        public int Count => _count;

        public IEnumerable<T> Items()
        {
            for (int i = 0; i < _count; i++)
                yield return _buffer[( _head + i ) % Capacity];
        }

        public bool TryAdd(T item, out T? evicted)
        {
            if (_count < Capacity)
            {
                _buffer[( _head + _count ) % Capacity] = item;
                _count++;
                evicted = default;
                return false;
            }
            evicted = _buffer[_head];
            _buffer[_head] = item;
            _head = ( _head + 1 ) % Capacity;
            return true;
        }

        public IReadOnlyList<T> RemoveFromStart(int removeCount)
        {
            if (removeCount <= 0) return [];
            if (removeCount > _count) removeCount = _count;
            var removed = new List<T>(removeCount);
            for (int i = 0; i < removeCount; i++)
                removed.Add(_buffer[( _head + i ) % Capacity]);
            _head = ( _head + removeCount ) % Capacity;
            _count -= removeCount;
            return removed;
        }

        public void Rebuild(int capacity)
        {
            var items = Items().ToArray();
            _buffer = new T[capacity];
            _head = 0;
            _count = 0;
            foreach (var it in items.Take(capacity))
                TryAdd(it, out _);
        }
    }

    private readonly Lock _sync = new();
    private readonly Subject<IChangeSet<T>> _changes = new();
    private readonly BehaviorSubject<int> _countChanged;
    private readonly BehaviorSubject<bool> _isEmptyChanged;
    private readonly RingBuffer _ring;

    public RingSourceList(int capacity)
    {
        _ring = new RingBuffer(capacity);
        _countChanged = new BehaviorSubject<int>(0);
        _isEmptyChanged = new BehaviorSubject<bool>(true);
    }

    public int Capacity => _ring.Capacity;

    public void UpdateCapacity(int newCapacity)
    {
        if (newCapacity <= 0 || newCapacity == _ring.Capacity) return;
        List<Change<T>>? changes = null;
        lock (_sync)
        {
            if (newCapacity < _ring.Count)
            {
                var remove = _ring.Count - newCapacity;
                var removed = _ring.RemoveFromStart(remove);
                if (removed.Count > 0)
                {
                    changes = new List<Change<T>>
                    {
                        new Change<T>(ListChangeReason.RemoveRange, removed, 0)
                    };
                }
            }
            _ring.Rebuild(newCapacity);
            PublishState_NoLock(changes);
        }
    }

    public int Count
    {
        get
        {
            lock (_sync) return _ring.Count;
        }
    }

    public IReadOnlyList<T> Items
    {
        get
        {
            lock (_sync) return _ring.Items().ToArray();
        }
    }

    public IObservable<int> CountChanged => _countChanged.AsObservable();
    public IObservable<bool> IsEmptyChanged => _isEmptyChanged.AsObservable();

    public IObservable<IChangeSet<T>> Connect(Func<T, bool>? predicate = null)
    {
        return Observable.Create<IChangeSet<T>>(obs =>
        {
            lock (_sync)
            {
                var snapshot = (predicate == null ? _ring.Items() : _ring.Items().Where(predicate)).ToList();
                if (snapshot.Count > 0)
                {
                    obs.OnNext(new ChangeSet<T>(new[]
                    {
                        new Change<T>(ListChangeReason.AddRange, snapshot, 0)
                    }));
                }
            }
            return _changes.Subscribe(obs);
        });
    }

    public IObservable<IChangeSet<T>> Preview(Func<T, bool>? predicate = null)
    {
        // For simplicity, mirror Connect for now
        return _changes.AsObservable();
    }

    public void Edit(Action<IExtendedList<T>>? updateAction)
    {
        if (updateAction == null) return;
        List<Change<T>> changes = new();
        lock (_sync)
        {
            var editor = new Editor(this, _ring, changes);
            updateAction(editor);
            PublishState_NoLock(changes);
        }
    }

    private void PublishState_NoLock(List<Change<T>>? changes)
    {
        _countChanged.OnNext(_ring.Count);
        _isEmptyChanged.OnNext(_ring.Count == 0);
        if (changes != null && changes.Count > 0)
            _changes.OnNext(new ChangeSet<T>(changes));
    }

    public void Dispose()
    {
        _changes.OnCompleted();
        _changes.Dispose();
        _countChanged.Dispose();
        _isEmptyChanged.Dispose();
    }

    private sealed class Editor : IExtendedList<T>
    {
        private readonly RingSourceList<T> _owner;
        private readonly RingBuffer _ring;
        private readonly List<Change<T>> _changes;

        public Editor(RingSourceList<T> owner, RingBuffer ring, List<Change<T>> changes)
        {
            _owner = owner;
            _ring = ring;
            _changes = changes;
        }

        public void Add(T item)
        {
            var evicted = _ring.TryAdd(item, out var removed);
            if (evicted)
            {
                _changes.Add(new Change<T>(ListChangeReason.Remove, removed!, 0));
                _changes.Add(new Change<T>(ListChangeReason.Add, item, _ring.Count - 1));
            }
            else
            {
                _changes.Add(new Change<T>(ListChangeReason.Add, item, _ring.Count - 1));
            }
        }

        public void AddRange(IEnumerable<T> items)
        {
            if (items == null) return;
            var toAdd = items as IList<T> ?? items.ToList();
            if (toAdd.Count == 0) return;

            var removed = new List<T>();
            var added = new List<T>();
            foreach (var it in toAdd)
            {
                var evicted = _ring.TryAdd(it, out var rem);
                if (evicted && rem != null) removed.Add(rem);
                added.Add(it);
            }
            if (removed.Count > 0)
                _changes.Add(new Change<T>(ListChangeReason.RemoveRange, removed, 0));
            _changes.Add(new Change<T>(ListChangeReason.AddRange, added, Math.Max(0, _ring.Count - added.Count)));
        }

        public void RemoveAt(int index) => throw new NotSupportedException();
        public void RemoveRange(int index, int count) => throw new NotSupportedException();
        public void Insert(int index, T item) => throw new NotSupportedException();
        public void InsertRange(IEnumerable<T> items, int index) => throw new NotSupportedException();
        public void Move(int oldIndex, int newIndex) => throw new NotSupportedException();
        public bool Remove(T item) => throw new NotSupportedException();
        public void RemoveMany(IEnumerable<T> items) => throw new NotSupportedException();
        public void Replace(T item, int index) => throw new NotSupportedException();
        public void ReplaceRange(IEnumerable<T> items, int index) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public int Count => _ring.Count;
        public bool IsReadOnly => false;
        public T this[int index]
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public bool Contains(T item)
        {
            var comparer = EqualityComparer<T>.Default;
            foreach (var it in _ring.Items())
            {
                if (comparer.Equals(it, item)) return true;
            }
            return false;
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (arrayIndex < 0) throw new ArgumentOutOfRangeException(nameof(arrayIndex));
            foreach (var it in _ring.Items())
            {
                if (arrayIndex >= array.Length) throw new ArgumentException("Array too small");
                array[arrayIndex++] = it;
            }
        }

        public int IndexOf(T item)
        {
            var comparer = EqualityComparer<T>.Default;
            int i = 0;
            foreach (var it in _ring.Items())
            {
                if (comparer.Equals(it, item)) return i;
                i++;
            }
            return -1;
        }

        public IEnumerator<T> GetEnumerator() => _ring.Items().GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}


