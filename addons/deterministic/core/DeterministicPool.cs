// Engine-free (no Godot dependency). Part of the deterministic addon.
using System;

namespace ReusableComponents.Deterministic;

/// <summary>Fixed-capacity pool of structs with a LIFO free-index stack. No allocation after construction.
/// A spawn into a full pool is refused (returns -1) and counted in <see cref="Misses"/>, which is
/// deterministic. Slot reuse order is part of the simulation state: save it with <see cref="Save"/>
/// (it includes the free stack), or a resumed run will hand out different indices and diverge.</summary>
/// <typeparam name="T">Entity struct. Iterate <see cref="Items"/> by index and keep an alive flag in T.</typeparam>
public sealed class DeterministicPool<T> where T : struct
{
    /// <summary>Entity storage, indexed by slot. Write through <c>ref pool.Items[i]</c>.</summary>
    public T[] Items { get; }
    readonly int[] _free;
    int _top;

    /// <summary>Slots currently allocated.</summary>
    public int Active { get; private set; }
    /// <summary>Highest <see cref="Active"/> seen.</summary>
    public int Peak { get; private set; }
    /// <summary>Allocations refused because the pool was full.</summary>
    public int Misses { get; private set; }
    /// <summary>Successful allocations since construction.</summary>
    public int Spawned { get; private set; }
    /// <summary>Total slots.</summary>
    public int Capacity => Items.Length;

    /// <summary>Creates the pool. The first allocations return 0, 1, 2, ...</summary>
    /// <param name="capacity">Slot count, &gt;= 0.</param>
    /// <exception cref="ArgumentOutOfRangeException">Negative capacity.</exception>
    public DeterministicPool(int capacity)
    {
        if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        Items = new T[capacity];
        _free = new int[capacity];
        for (int i = 0; i < capacity; i++) _free[i] = capacity - 1 - i;
        _top = capacity;
    }

    /// <summary>Takes a free slot (most recently freed first).</summary>
    /// <returns>Slot index, or -1 when full (counted in <see cref="Misses"/>).</returns>
    public int Alloc()
    {
        if (_top == 0) { Misses++; return -1; }
        int i = _free[--_top];
        Active++; Spawned++;
        if (Active > Peak) Peak = Active;
        return i;
    }

    /// <summary>Returns a slot. The caller must clear its alive flag; freeing a slot twice corrupts the pool.</summary>
    /// <param name="index">Slot from <see cref="Alloc"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">Index outside the pool.</exception>
    /// <exception cref="InvalidOperationException">More frees than allocations.</exception>
    public void Free(int index)
    {
        if ((uint)index >= (uint)Items.Length) throw new ArgumentOutOfRangeException(nameof(index));
        if (_top == _free.Length) throw new InvalidOperationException("pool over-freed");
        _free[_top++] = index; Active--;
    }

    /// <summary>Copies the full pool state (items, free stack, counters) for a save.</summary>
    /// <returns>A detached snapshot.</returns>
    public PoolState Save() => new() { Items = (T[])Items.Clone(), Free = _free[.._top], Active = Active, Peak = Peak, Misses = Misses, Spawned = Spawned };

    /// <summary>Restores a <see cref="Save"/> snapshot into this pool.</summary>
    /// <param name="state">Snapshot from a pool of the same capacity.</param>
    /// <exception cref="ArgumentException">Capacity mismatch or inconsistent free stack.</exception>
    public void Load(PoolState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Items.Length != Items.Length || state.Free.Length > _free.Length || state.Free.Length + state.Active != Items.Length)
            throw new ArgumentException("pool state does not match this pool's capacity", nameof(state));
        Array.Copy(state.Items, Items, Items.Length);
        Array.Copy(state.Free, _free, state.Free.Length); _top = state.Free.Length;
        Active = state.Active; Peak = state.Peak; Misses = state.Misses; Spawned = state.Spawned;
    }

    /// <summary>Serializable pool snapshot (public fields so System.Text.Json with IncludeFields round-trips it).</summary>
    public sealed class PoolState
    {
        /// <summary>Copy of every slot.</summary>
        public T[] Items = [];
        /// <summary>Free stack, bottom first.</summary>
        public int[] Free = [];
        /// <summary>Counters.</summary>
        public int Active, Peak, Misses, Spawned;
    }
}
