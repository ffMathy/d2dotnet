using System.Diagnostics;
using D2Dotnet.Map.Generator.Native;
using IOPath = System.IO.Path;

namespace D2Dotnet.Map.Generator.Tests;

/// <summary>
/// Expected byte sequences were produced with nasm 2.16.01 (<c>bits 32</c>) from the assembly in
/// the comments; <see cref="CrossCheckWithNasmWhenInstalled"/> re-assembles them live if nasm is
/// on PATH.
/// </summary>
public class X86ThunkTests
{
    private static byte[] Hex(string s) => Convert.FromHexString(s.Replace(" ", "", StringComparison.Ordinal));

    [Fact]
    public void FastcallBridgeZeroArgs()
    {
        // push ebp / mov ebp, esp / mov eax, 0x6FF51234 / call eax / mov esp, ebp / pop ebp / ret
        Assert.Equal(Hex("55 89E5 B83412F56F FFD0 89EC 5D C3"), X86Thunks.FastcallBridge(0x6FF51234, 0));
    }

    [Fact]
    public void FastcallBridgeOneArg()
    {
        // ... mov ecx, [ebp+8] ... ret 4
        Assert.Equal(Hex("55 89E5 8B4D08 B83412F56F FFD0 89EC 5D C20400"), X86Thunks.FastcallBridge(0x6FF51234, 1));
    }

    [Fact]
    public void FastcallBridgeTwoArgs()
    {
        // ... mov edx, [ebp+12] / mov ecx, [ebp+8] ... ret 8
        Assert.Equal(Hex("55 89E5 8B550C 8B4D08 B83412F56F FFD0 89EC 5D C20800"), X86Thunks.FastcallBridge(0x6FF51234, 2));
    }

    [Fact]
    public void FastcallBridgeFourArgs()
    {
        // push dword [ebp+20] / push dword [ebp+16] / mov edx, [ebp+12] / mov ecx, [ebp+8] ... ret 16
        Assert.Equal(Hex("55 89E5 FF7514 FF7510 8B550C 8B4D08 B83412F56F FFD0 89EC 5D C21000"), X86Thunks.FastcallBridge(0x6FF51234, 4));
    }

    [Fact]
    public void FastcallBridgeThreeArgsPushesOnlyTheThird()
    {
        Assert.Equal(Hex("55 89E5 FF7510 8B550C 8B4D08 B878563412 FFD0 89EC 5D C20C00"), X86Thunks.FastcallBridge(0x12345678, 3));
    }

    [Fact]
    public void FastcallBridgeRejectsBadArgCounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => X86Thunks.FastcallBridge(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => X86Thunks.FastcallBridge(0, X86Thunks.MaxArgs + 1));
    }

    [Fact]
    public void MagicJumpPodAt0x400000()
    {
        // org 0x00400000: push ebp / mov ebp, esp / call inner / mov esp, ebp / pop ebp / ret
        // inner: push ecx / push ebp / push esi / push edi / jmp 0x6faf559b
        var code = X86Thunks.MagicJump(0x00400000, D2Pointers.D2CLIENT_Pod_InitGameMisc_I_P);
        Assert.Equal(Hex("55 89E5 E804000000 89EC 5D C3 51 55 56 57 E986556F6F"), code);
        Assert.Equal(X86Thunks.MagicJumpSize, code.Length);
    }

    [Fact]
    public void MagicJumpInnerIsTheOriginalInlineAsm()
    {
        var code = X86Thunks.MagicJump(0x00400000, D2Pointers.D2CLIENT_Pd2_InitGameMisc_I_P);
        // nasm: org 0x0040000C (inner offset) / push ecx/ebp/esi/edi / jmp 0x6faf454b
        // rel32 = 0x6faf454b - 0x00400015 = 0x6F6F4536
        Assert.Equal(Hex("51 55 56 57 E9 36456F6F"), code[X86Thunks.MagicJumpInnerOffset..]);
        // The call at +3 lands on the inner stub: next ip (+8) + rel32 (4) = +12
        Assert.Equal(X86Thunks.MagicJumpInnerOffset, 8 + BitConverter.ToInt32(code, 4));
    }

    [Fact]
    public void MagicJumpTargetsAreTheHardCodedCAddresses()
    {
        Assert.Equal(0x6faf559bu, D2Pointers.D2CLIENT_Pod_InitGameMisc_I_P);
        Assert.Equal(0x6faf454bu, D2Pointers.D2CLIENT_Pd2_InitGameMisc_I_P);
        // The jump target decodes back from the relative operand at any thunk address.
        foreach (var at in new uint[] { 0x00400000, 0x7FFE0000, 0x90000010 })
        {
            var code = X86Thunks.MagicJump(at, 0x6faf559b);
            var rel = BitConverter.ToUInt32(code, 17);
            Assert.Equal(0x6faf559bu, unchecked(at + X86Thunks.MagicJumpSize + rel));
        }
    }

    [Fact]
    public void CrossCheckWithNasmWhenInstalled()
    {
        string? nasm = Environment.GetEnvironmentVariable("PATH")?.Split(IOPath.PathSeparator)
            .Select(p => IOPath.Combine(p, "nasm")).FirstOrDefault(File.Exists);
        if (nasm == null) return; // nasm not installed: the pinned bytes above still apply.

        var dir = Directory.CreateTempSubdirectory("d2map-nasm");
        try
        {
            byte[] Assemble(string src)
            {
                var asm = IOPath.Combine(dir.FullName, "t.asm");
                var bin = IOPath.Combine(dir.FullName, "t.bin");
                File.WriteAllText(asm, "bits 32\n" + src);
                var psi = new ProcessStartInfo(nasm) { RedirectStandardError = true };
                foreach (var a in new[] { "-f", "bin", asm, "-o", bin }) psi.ArgumentList.Add(a);
                using var p = Process.Start(psi)!;
                p.WaitForExit();
                Assert.Equal(0, p.ExitCode);
                return File.ReadAllBytes(bin);
            }

            for (var n = 0; n <= 6; n++)
            {
                var src = "push ebp\nmov ebp, esp\n";
                for (var k = n; k >= 3; k--) src += $"push dword [ebp+{8 + 4 * (k - 1)}]\n";
                if (n >= 2) src += "mov edx, [ebp+12]\n";
                if (n >= 1) src += "mov ecx, [ebp+8]\n";
                src += "mov eax, 0x6FD12345\ncall eax\nmov esp, ebp\npop ebp\n" + (n == 0 ? "ret\n" : $"ret {4 * n}\n");
                Assert.Equal(Assemble(src), X86Thunks.FastcallBridge(0x6FD12345, n));
            }

            var magic = "org 0x00500000\npush ebp\nmov ebp, esp\ncall inner\nmov esp, ebp\npop ebp\nret\ninner:\npush ecx\npush ebp\npush esi\npush edi\njmp 0x6faf454b\n";
            Assert.Equal(Assemble(magic), X86Thunks.MagicJump(0x00500000, 0x6faf454b));
        }
        finally
        {
            dir.Delete(true);
        }
    }
}
