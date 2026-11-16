// Port of libd2 packages/drlg/src/drlg/pool.zig (MIT, (c) 2026 jaenster).
//
// DRLG memory pool — mechanical transform of recon/closure/Memory.cpp.
//
// The reconstructed 1.14d closure ALREADY bypasses the real pool allocator (VirtualAlloc +
// CRITICAL_SECTION-guarded free lists) and routes every allocation through a plain
// calloc/free-style allocator, because the pool internals differ under Wine and — crucially —
// the pool does NOT consume the DRLG seed, so it has no effect on generated geometry:
//   AllocClientMemory  (1.14d 0040b3a0, Memory.cpp:1094) -> calloc(1, n)
//   FreeClientMemory   (1.14d 0040b3c0, Memory.cpp:1101) -> free
//   ReAllocClientMemory(1.14d 0040b3f0, Memory.cpp:1107) -> realloc
//   AllocServerMemory  (1.14d 0040b430, Memory.cpp:1119) -> calloc(1, n)
//   FreeServerMemory   (1.14d 0040b480, Memory.cpp:1126) -> free
//   ReAllocServerMemory(1.14d 0040b4b0, Memory.cpp:1137) -> realloc
//
// This port keeps the same calloc/free behaviour over native memory owned by one
// generation (a NativePool), so the whole act is freed at once when the generation ends.

using System.Runtime.InteropServices;

namespace D2Dotnet.Maps.Engine;

/// <summary>A generation-scoped native heap: zeroed allocations, tracked so a free of a
/// foreign pointer is a no-op (as the engine's Fog::Memory pool) and so everything left is
/// released on <see cref="Dispose"/>.</summary>
internal sealed unsafe class NativePool : IDisposable
{
    // Live-allocation registry. The engine's Fog::Memory pool only frees blocks it actually
    // handed out and silently ignores foreign pointers; the DRLG maze relies on this —
    // DRLGGRID_FreeGrid frees pCellsRowOffsets, which for a maze room is an INT payload
    // overlaid on the grid-pointer field (def/variant), not a real block.
    private readonly Dictionary<nint, nuint> _live = new();
    private bool _disposed;

    public void* Alloc(nuint n)
    {
        if (n == 0) return null;
        void* p = NativeMemory.AllocZeroed(n);
        _live[(nint)p] = n;
        return p;
    }

    public void Free(void* p)
    {
        if (p == null) return;
        // Pool semantics: only free blocks we allocated; ignore foreign pointers.
        if (!_live.Remove((nint)p)) return;
        NativeMemory.Free(p);
    }

    public void* Realloc(void* old, nuint n)
    {
        if (old == null) return Alloc(n);
        if (n == 0)
        {
            Free(old);
            return null;
        }
        void* nw = Alloc(n);
        if (_live.TryGetValue((nint)old, out var oldLen))
        {
            nuint copy = oldLen < n ? oldLen : n;
            Buffer.MemoryCopy(old, nw, (long)n, (long)copy);
        }
        Free(old);
        return nw;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var p in _live.Keys) NativeMemory.Free((void*)p);
        _live.Clear();
        GC.SuppressFinalize(this);
    }

    ~NativePool()
    {
        if (_disposed) return;
        foreach (var p in _live.Keys) NativeMemory.Free((void*)p);
    }
}

/// <summary>The recon allocator entry points over the current thread's generation pool.</summary>
internal static unsafe class Pool
{
    /// <summary>Backing pool for all DRLG allocations of the generation running on this thread.</summary>
    [ThreadStatic] internal static NativePool? Current;

    private static NativePool P => Current ?? throw new InvalidOperationException("No DRLG generation pool is active on this thread.");

    /// <summary>AllocClientMemory (Memory.cpp:1094): calloc(1, nAllocSize).</summary>
    public static void* AllocClientMemory(nuint nAllocSize) => P.Alloc(nAllocSize);

    /// <summary>FreeClientMemory (Memory.cpp:1101): free.</summary>
    public static void FreeClientMemory(void* pMemoryToFree) => P.Free(pMemoryToFree);

    /// <summary>ReAllocClientMemory (Memory.cpp:1107): realloc.</summary>
    public static void* ReAllocClientMemory(void* pMemory, nuint nSize) => P.Realloc(pMemory, nSize);

    /// <summary>AllocServerMemory (Memory.cpp:1119): calloc(1, nSize). Pool manager ignored.</summary>
    public static void* AllocServerMemory(void* pPool, nuint nSize) => P.Alloc(nSize);

    /// <summary>FreeServerMemory (Memory.cpp:1126): free. Pool manager ignored.</summary>
    public static void FreeServerMemory(void* pPool, void* pMemoryToFree) => P.Free(pMemoryToFree);

    /// <summary>ReAllocServerMemory (Memory.cpp:1137): realloc. Pool manager ignored.</summary>
    public static void* ReAllocServerMemory(void* pPool, void* pMemory, nuint nNewSize) => P.Realloc(pMemory, nNewSize);

    /// <summary>Typed calloc of <paramref name="count"/> elements.</summary>
    public static T* Alloc<T>(int count = 1) where T : unmanaged => (T*)P.Alloc((nuint)(sizeof(T) * count));
}
