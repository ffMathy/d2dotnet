using System.Runtime.CompilerServices;

namespace D2Dotnet.Map.Generator.Interop;

/// <summary>Direct access to this (32-bit) process's memory, i.e. <c>pLevel-&gt;dwPosX</c> in C.</summary>
internal sealed unsafe class NativeGameMemory : IGameMemory
{
    public T Read<T>(uint address) where T : unmanaged => Unsafe.ReadUnaligned<T>((void*)(nuint)address);

    public void Write<T>(uint address, T value) where T : unmanaged => Unsafe.WriteUnaligned((void*)(nuint)address, value);
}
