using System.Buffers.Binary;

namespace D2Dotnet.Map.Generator.Native;

/// <summary>
/// Hand-assembled 32-bit x86 machine code for the two things .NET cannot express:
/// <list type="bullet">
/// <item>calling a <c>__fastcall</c> function (the runtime has no fastcall support on x86), and</item>
/// <item>the naked "magic jump" stubs from d2_client.c (inline asm that pushes registers and
/// jumps into the middle of a D2Client function).</item>
/// </list>
/// The bytes are written into PAGE_EXECUTE_READWRITE memory by the win-x86 executable and called
/// through <c>delegate* unmanaged[Stdcall]</c>. Every encoding below was cross-checked against
/// nasm 2.16.01 (<c>bits 32</c>); the unit tests pin the exact byte sequences.
/// </summary>
public static class X86Thunks
{
    /// <summary>Maximum DWORD arguments a fastcall bridge supports (disp8 addressing limit).</summary>
    public const int MaxArgs = 30;

    /// <summary>
    /// A stdcall-callable bridge to a fastcall function taking <paramref name="argCount"/> DWORD
    /// arguments. The caller pushes all arguments (stdcall, right to left); the bridge moves
    /// arg1/arg2 into ECX/EDX, re-pushes the rest, calls the target, and returns popping
    /// <c>4 * argCount</c> bytes. It keeps a frame (EBP) and restores ESP itself, so — like the
    /// gcc -O0 caller in the original program — it tolerates a callee that pops a different
    /// number of bytes than declared. EAX (the return value) is passed through untouched.
    /// <code>
    /// 55                push ebp
    /// 89 E5             mov  ebp, esp
    /// FF 75 xx          push dword [ebp+8+4*(k-1)]   ; for k = argCount .. 3 (stack args, right to left)
    /// 8B 55 0C          mov  edx, [ebp+12]            ; arg2, if argCount &gt;= 2
    /// 8B 4D 08          mov  ecx, [ebp+8]             ; arg1, if argCount &gt;= 1
    /// B8 imm32          mov  eax, target
    /// FF D0             call eax
    /// 89 EC             mov  esp, ebp
    /// 5D                pop  ebp
    /// C2 imm16 | C3     ret  4*argCount | ret
    /// </code>
    /// </summary>
    public static byte[] FastcallBridge(uint target, int argCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(argCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(argCount, MaxArgs);

        var code = new List<byte>(32 + 3 * argCount)
        {
            0x55,       // push ebp
            0x89, 0xE5, // mov ebp, esp
        };
        for (var k = argCount; k >= 3; k--)
        {
            // push dword [ebp + disp8]: FF /6, modrm 01 110 101 = 0x75
            code.Add(0xFF);
            code.Add(0x75);
            code.Add((byte)(8 + 4 * (k - 1)));
        }
        if (argCount >= 2) code.AddRange([0x8B, 0x55, 0x0C]); // mov edx, [ebp+12]  (8B /r, modrm 01 010 101)
        if (argCount >= 1) code.AddRange([0x8B, 0x4D, 0x08]); // mov ecx, [ebp+8]   (8B /r, modrm 01 001 101)
        code.Add(0xB8);                                       // mov eax, imm32
        AddUInt32(code, target);
        code.AddRange([0xFF, 0xD0]);                          // call eax  (FF /2, modrm 11 010 000)
        code.AddRange([0x89, 0xEC]);                          // mov esp, ebp
        code.Add(0x5D);                                       // pop ebp
        if (argCount == 0)
        {
            code.Add(0xC3);                                   // ret
        }
        else
        {
            code.Add(0xC2);                                   // ret imm16
            var pop = (ushort)(4 * argCount);
            code.Add((byte)(pop & 0xFF));
            code.Add((byte)(pop >> 8));
        }
        return [.. code];
    }

    /// <summary>Byte offset of the inner stub (the part equivalent to the C inline asm) in <see cref="MagicJump"/>.</summary>
    public const int MagicJumpInnerOffset = 12;

    /// <summary>Total size of <see cref="MagicJump"/>.</summary>
    public const int MagicJumpSize = 21;

    /// <summary>
    /// The <c>D2CLIENT_Pod_InitGameMisc</c> / <c>D2CLIENT_Pd2_InitGameMisc</c> stub. The C version
    /// is a gcc function whose inline asm first undoes gcc's own prologue (<c>movl %ebp,%esp;
    /// popl %ebp</c>) and then does <c>push ecx; push ebp; push esi; push edi; jmp 0x6faf....</c>
    /// — it enters D2Client right after that function's prologue, and the D2 epilogue pops the
    /// four registers and returns to our caller. The trailing <c>pushl %ebp</c> is unreachable.
    /// <para>
    /// Here the inner part is reproduced exactly (rel32 jump, no register clobbered). It is
    /// wrapped in a frame that saves ESP in EBP and restores it after the D2 code returns, because
    /// the D2 function is declared <c>__stdcall</c> with three dummy parameters and may pop more
    /// stack than our zero-argument call pushed; gcc -O0 callers tolerate that via their frame
    /// pointer, the .NET JIT is not guaranteed to.
    /// </para>
    /// <code>
    /// +0   55                push ebp
    /// +1   89 E5             mov  ebp, esp
    /// +3   E8 04 00 00 00    call +12              ; pushes the return address (+8)
    /// +8   89 EC             mov  esp, ebp
    /// +10  5D                pop  ebp
    /// +11  C3                ret
    /// +12  51                push ecx              ; --- inner: the original inline asm ---
    /// +13  55                push ebp
    /// +14  56                push esi
    /// +15  57                push edi
    /// +16  E9 rel32          jmp  target           ; rel32 = target - (thunkAddress + 21)
    /// </code>
    /// </summary>
    /// <param name="thunkAddress">Address the bytes will be copied to (needed for the relative jump).</param>
    /// <param name="target">Absolute jump target, e.g. <see cref="D2Pointers.D2CLIENT_Pod_InitGameMisc_I_P"/>.</param>
    public static byte[] MagicJump(uint thunkAddress, uint target)
    {
        var code = new List<byte>(MagicJumpSize)
        {
            0x55,                         // push ebp
            0x89, 0xE5,                   // mov ebp, esp
            0xE8, 0x04, 0x00, 0x00, 0x00, // call inner (rel32 = 12 - 8)
            0x89, 0xEC,                   // mov esp, ebp
            0x5D,                         // pop ebp
            0xC3,                         // ret
            0x51,                         // inner: push ecx
            0x55,                         // push ebp
            0x56,                         // push esi
            0x57,                         // push edi
            0xE9,                         // jmp rel32
        };
        AddUInt32(code, RelativeJump(thunkAddress + MagicJumpSize, target));
        return [.. code];
    }

    /// <summary>rel32 operand of a jump/call whose next instruction is at <paramref name="nextInstruction"/>.</summary>
    public static uint RelativeJump(uint nextInstruction, uint target) => unchecked(target - nextInstruction);

    private static void AddUInt32(List<byte> code, uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, value);
        foreach (var x in b) code.Add(x);
    }
}
