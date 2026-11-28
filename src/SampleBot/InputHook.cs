using System.Runtime.InteropServices;

/// <summary>
/// Hooks in the game process that let the bot aim without the real cursor: <c>GetCursorPos</c> answers the bot's aim
/// while the bot acts (and the real cursor otherwise), and <c>GetKeyState</c> and <c>GetAsyncKeyState</c> answer that
/// Shift is down while the bot holds it. The clicks and keys themselves stay window messages, so the player keeps the
/// mouse and keyboard.
/// </summary>
/// <remarks>
/// user32 is mapped at the same address in every process of a boot, so the functions are found in this process and
/// patched at the same addresses in the game's. <c>GetCursorPos</c> is <c>mov edx, 1; jmp [slot]</c>: the slot (a
/// pointer) is pointed at a stub. The key state functions start with position independent instructions, copied into
/// a trampoline before a 14 byte jump to the stub overwrites them. One page in the game holds the stubs and their data;
/// a later run finds it again (by its magic) and reuses it. Nothing is unhooked: with the flags off the stubs pass
/// every call on.
/// </remarks>
internal sealed class InputHook : IDisposable
{
    // The data at the start of the page.
    private const int CursorOn = 0x00, CursorX = 0x04, CursorY = 0x08, ShiftOn = 0x0C;
    private const int CursorOriginal = 0x10, KeyStateBack = 0x18, AsyncKeyStateBack = 0x20, Magic = 0x28;
    // How often the game called each hooked function (for checking that the game uses them).
    private const int CursorCalls = 0x30, KeyStateCalls = 0x34, AsyncKeyStateCalls = 0x38, CursorInfoCalls = 0x3C;
    private const int CursorInfoOriginal = 0x40, MessagePosOriginal = 0x48, MessagePosCalls = 0x50;
    private const int KeyboardStateCalls = 0x54, KeyboardStateOriginal = 0x58, RawInputOriginal = 0x60, RawInputCalls = 0x68;
    private const int CursorStub = 0x100, KeyStateStub = 0x200, AsyncKeyStateStub = 0x300, CursorInfoStub = 0x400, MessagePosStub = 0x500;
    private const int KeyboardStateStub = 0x600, RawInputStub = 0x700;
    private static readonly byte[] MagicBytes = "D2DHOOK4"u8.ToArray();

    // Each slot stub and where its original pointer is kept.
    private static readonly Dictionary<int, int> OriginalOf = new()
    {
        [CursorStub] = CursorOriginal, [CursorInfoStub] = CursorInfoOriginal, [MessagePosStub] = MessagePosOriginal,
        [KeyboardStateStub] = KeyboardStateOriginal, [RawInputStub] = RawInputOriginal,
    };
    private static readonly byte[] MagicPrefix = "D2DHOOK"u8.ToArray();

    // What the key state functions start with on this Windows (checked before patching): position independent.
    private static readonly byte[] KeyStatePrologue = [0x40, 0x53, 0x48, 0x83, 0xEC, 0x20, 0x65, 0x48, 0x8B, 0x04, 0x25, 0x30, 0x00, 0x00, 0x00];
    private static readonly byte[] AsyncKeyStatePrologue =
        [0x48, 0x89, 0x5C, 0x24, 0x08, 0x57, 0x48, 0x83, 0xEC, 0x20, 0x65, 0x48, 0x8B, 0x04, 0x25, 0x30, 0x00, 0x00, 0x00];

    private readonly nint _process;
    private readonly nint _page;

    private InputHook(nint process, nint page)
    {
        _process = process;
        _page = page;
    }

    /// <summary>Installs the hooks in the game (or finds them from an earlier run); null when that fails (logged).</summary>
    public static InputHook? Install(int processId)
    {
        var process = OpenProcess(ProcessVmOperation | ProcessVmRead | ProcessVmWrite | ProcessQueryInformation, false, processId);
        if (process == 0)
        {
            Bot.Log($"Input hook: cannot open the game for writing (error {Marshal.GetLastWin32Error()}).");
            return null;
        }

        try
        {
            var user32 = GetModuleHandle("user32.dll");
            var cursor = GetProcAddress(user32, "GetCursorPos");
            var keyState = GetProcAddress(user32, "GetKeyState");
            var cursorInfoSlot = SlotOf(process, GetProcAddress(user32, "GetCursorInfo"));
            var messagePosSlot = SlotOf(process, GetProcAddress(user32, "GetMessagePos"));
            var keyboardStateSlot = SlotOf(process, GetProcAddress(user32, "GetKeyboardState"));
            var rawInputSlot = SlotOf(process, GetProcAddress(user32, "GetRawInputData"));
            var asyncKeyState = GetProcAddress(user32, "GetAsyncKeyState");

            // GetCursorPos: mov edx, 1 (ba 01 00 00 00); jmp [rip + disp] (48 ff 25 disp32).
            var head = Read(process, cursor, 12);
            if (head is not [0xBA, 0x01, 0x00, 0x00, 0x00, 0x48, 0xFF, 0x25, ..])
            {
                Bot.Log($"Input hook: GetCursorPos looks different on this Windows ({Convert.ToHexString(head)}).");
                CloseHandle(process);
                return null;
            }

            var slot = cursor + 12 + BitConverter.ToInt32(head, 8);
            var pointed = (nint)BitConverter.ToInt64(Read(process, slot, 8));
            var page = FindPage(process, pointed - CursorStub);
            if (page == 0 && IsOlderPage(process, pointed - CursorStub))
            {
                // An older version's hooks: put the functions back as they were, and install this version.
                var old = pointed - CursorStub;
                foreach (var each in new[] { slot, cursorInfoSlot, messagePosSlot, keyboardStateSlot, rawInputSlot })
                    Unhook(process, each, old);
                pointed = (nint)BitConverter.ToInt64(Read(process, slot, 8));
                Restore(process, keyState, KeyStatePrologue);
                Restore(process, asyncKeyState, AsyncKeyStatePrologue);
                Bot.Log("Input hook: replaced an older version's hooks.");
            }

            if (page == 0)
            {
                page = VirtualAllocEx(process, 0, 0x1000, MemCommit | MemReserve, PageExecuteReadWrite);
                if (page == 0)
                    throw new InvalidOperationException($"no memory in the game (error {Marshal.GetLastWin32Error()})");
                Write(process, page + CursorOriginal, BitConverter.GetBytes((long)pointed));
                Write(process, page + CursorStub, CursorCode(page + CursorStub));
                PatchInline(process, page, keyState, KeyStatePrologue, KeyStateStub, KeyStateBack, KeyStateCalls);
                PatchInline(process, page, asyncKeyState, AsyncKeyStatePrologue, AsyncKeyStateStub, AsyncKeyStateBack, AsyncKeyStateCalls);
                Write(process, page + Magic, MagicBytes);
                HookSlot(process, page, cursorInfoSlot, CursorInfoStub, CursorInfoCode(page + CursorInfoStub));
                HookSlot(process, page, messagePosSlot, MessagePosStub, MessagePosCode(page + MessagePosStub));
                HookSlot(process, page, keyboardStateSlot, KeyboardStateStub, KeyboardStateCode(page + KeyboardStateStub));
                HookSlot(process, page, rawInputSlot, RawInputStub, CountingCode(page + RawInputStub, RawInputCalls, RawInputOriginal));
                // Last: the cursor slot, one aligned eight byte write.
                WriteProtected(process, slot, BitConverter.GetBytes((long)(page + CursorStub)));
                Bot.Log($"Input hook: installed in the game at 0x{page:X}.");
            }
            else
            {
                Bot.Log($"Input hook: found from an earlier run at 0x{page:X}.");
            }

            var hook = new InputHook(process, page);
            hook.Release();
            return hook;
        }
        catch (Exception e)
        {
            Bot.Log($"Input hook: {e.Message}.");
            CloseHandle(process);
            return null;
        }
    }

    /// <summary>The game sees the cursor at <paramref name="screen"/> (screen pixels) until <see cref="Release"/>.</summary>
    public void Aim((int X, int Y) screen)
    {
        var data = new byte[12];
        BitConverter.TryWriteBytes(data.AsSpan(0), 1);
        BitConverter.TryWriteBytes(data.AsSpan(4), screen.X);
        BitConverter.TryWriteBytes(data.AsSpan(8), screen.Y);
        // The position first, then the flag: the stub never answers with half a position.
        Write(_process, _page + CursorX, data[4..]);
        Write(_process, _page + CursorOn, data[..4]);
    }

    /// <summary>The game sees Shift down (or not).</summary>
    public void Shift(bool down) => Write(_process, _page + ShiftOn, BitConverter.GetBytes(down ? 1 : 0));

    /// <summary>The game sees the real cursor and keyboard again.</summary>
    public void Release()
    {
        Write(_process, _page + CursorOn, BitConverter.GetBytes(0));
        Write(_process, _page + ShiftOn, BitConverter.GetBytes(0));
    }

    public void Dispose()
    {
        Release();
        CloseHandle(_process);
    }

    /// <summary>How often the game called GetCursorPos, GetKeyState and GetAsyncKeyState through the hooks.</summary>
    public (int Cursor, int KeyState, int AsyncKeyState, int CursorInfo, int MessagePos, int KeyboardState, int RawInput) Calls()
    {
        var data = Read(_process, _page + CursorCalls, 0x3C);
        int At(int offset) => BitConverter.ToInt32(data, offset - CursorCalls);
        return (At(CursorCalls), At(KeyStateCalls), At(AsyncKeyStateCalls), At(CursorInfoCalls), At(MessagePosCalls), At(KeyboardStateCalls), At(RawInputCalls));
    }

    // GetKeyboardState(BYTE* rcx): the original, then Shift and left Shift down while the flag is on.
    private static byte[] KeyboardStateCode(nint at)
    {
        var a = new Assembler(at);
        a.RipRelative([0xFF, 0x05], Data(at, KeyboardStateCalls));    // inc dword [calls]
        a.RipRelative([0x83, 0x3D], Data(at, ShiftOn), [0x00]);       // cmp dword [shift], 0
        a.JumpShort(0x74, "original");
        a.Bytes(0x53);                                                // push rbx
        a.Bytes(0x48, 0x89, 0xCB);                                    // mov rbx, rcx
        a.Bytes(0x48, 0x83, 0xEC, 0x20);                              // sub rsp, 32
        a.RipRelative([0xFF, 0x15], Data(at, KeyboardStateOriginal)); // call [original]
        a.Bytes(0x48, 0x83, 0xC4, 0x20);                              // add rsp, 32
        a.Bytes(0x80, 0x4B, 0x10, 0x80);                              // or byte [rbx+VK_SHIFT], 0x80
        a.Bytes(0x80, 0x8B, 0xA0, 0x00, 0x00, 0x00, 0x80);            // or byte [rbx+VK_LSHIFT], 0x80
        a.Bytes(0x5B);                                                // pop rbx
        a.Bytes(0xC3);
        a.Label("original");
        a.RipRelative([0xFF, 0x25], Data(at, KeyboardStateOriginal)); // jmp [original]
        return a.Build();
    }

    // Counts the calls and passes them on.
    private static byte[] CountingCode(nint at, int calls, int original)
    {
        var a = new Assembler(at);
        a.RipRelative([0xFF, 0x05], Data(at, calls));                 // inc dword [calls]
        a.RipRelative([0xFF, 0x25], Data(at, original));              // jmp [original]
        return a.Build();
    }

    // The pointer slot a user32 function jumps through: jmp [rip+d] (ff 25), with a rex prefix (48 ff 25), or after
    // mov edx, imm (ba imm32).
    private static nint SlotOf(nint process, nint function)
    {
        var head = Read(process, function, 12);
        var at = head[0] == 0xBA ? 5 : 0;
        if (head[at] == 0x48)
            at++;
        if (head[at] != 0xFF || head[at + 1] != 0x25)
            throw new InvalidOperationException($"a cursor function looks different on this Windows ({Convert.ToHexString(head)})");
        return function + at + 6 + BitConverter.ToInt32(head, at + 2);
    }

    // The slot pointed at the stub, the original pointer kept for it.
    private static void HookSlot(nint process, nint page, nint slot, int stub, byte[] code)
    {
        Write(process, page + OriginalOf[stub], Read(process, slot, 8));
        Write(process, page + stub, code);
        WriteProtected(process, slot, BitConverter.GetBytes((long)(page + stub)));
    }

    // A slot pointing into an older page gets its original pointer back.
    private static void Unhook(nint process, nint slot, nint old)
    {
        var value = (nint)BitConverter.ToInt64(Read(process, slot, 8));
        if (value >= old && value < old + 0x1000 && OriginalOf.TryGetValue((int)(value - old), out var original))
            WriteProtected(process, slot, Read(process, old + original, 8));
    }

    // GetCursorInfo(CURSORINFO* rcx): the original, then the aim over ptScreenPos (+16) while the flag is on.
    private static byte[] CursorInfoCode(nint at)
    {
        var a = new Assembler(at);
        a.RipRelative([0xFF, 0x05], Data(at, CursorInfoCalls));     // inc dword [calls]
        a.RipRelative([0x83, 0x3D], Data(at, CursorOn), [0x00]);    // cmp dword [on], 0
        a.JumpShort(0x74, "original");
        a.Bytes(0x53);                                              // push rbx
        a.Bytes(0x48, 0x89, 0xCB);                                  // mov rbx, rcx
        a.Bytes(0x48, 0x83, 0xEC, 0x20);                            // sub rsp, 32
        a.RipRelative([0xFF, 0x15], Data(at, CursorInfoOriginal));  // call [original]
        a.Bytes(0x48, 0x83, 0xC4, 0x20);                            // add rsp, 32
        a.RipRelative([0x8B, 0x15], Data(at, CursorX));             // mov edx, [x]
        a.Bytes(0x89, 0x53, 0x10);                                  // mov [rbx+16], edx
        a.RipRelative([0x8B, 0x15], Data(at, CursorY));             // mov edx, [y]
        a.Bytes(0x89, 0x53, 0x14);                                  // mov [rbx+20], edx
        a.Bytes(0x5B);                                              // pop rbx
        a.Bytes(0xC3);
        a.Label("original");
        a.RipRelative([0xFF, 0x25], Data(at, CursorInfoOriginal));  // jmp [original]
        return a.Build();
    }

    // GetMessagePos(): the aim packed (y high, x low) while the flag is on.
    private static byte[] MessagePosCode(nint at)
    {
        var a = new Assembler(at);
        a.RipRelative([0xFF, 0x05], Data(at, MessagePosCalls));     // inc dword [calls]
        a.RipRelative([0x83, 0x3D], Data(at, CursorOn), [0x00]);    // cmp dword [on], 0
        a.JumpShort(0x74, "original");
        a.RipRelative([0x8B, 0x05], Data(at, CursorY));             // mov eax, [y]
        a.Bytes(0xC1, 0xE0, 0x10);                                  // shl eax, 16
        a.RipRelative([0x0F, 0xB7, 0x15], Data(at, CursorX));       // movzx edx, word [x]
        a.Bytes(0x09, 0xD0);                                        // or eax, edx
        a.Bytes(0xC3);
        a.Label("original");
        a.RipRelative([0xFF, 0x25], Data(at, MessagePosOriginal));  // jmp [original]
        return a.Build();
    }

    private static bool IsOlderPage(nint process, nint candidate)
    {
        try
        {
            return Read(process, candidate + Magic, MagicPrefix.Length).AsSpan().SequenceEqual(MagicPrefix);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    // A function an older version patched (it starts with the 14 byte jump) gets its prologue back.
    private static void Restore(nint process, nint function, byte[] prologue)
    {
        if (Read(process, function, 2) is [0xFF, 0x25])
            WriteProtected(process, function, prologue);
    }

    // The page an earlier run left, when the slot points into one (its magic is there).
    private static nint FindPage(nint process, nint candidate)
    {
        try
        {
            return Read(process, candidate + Magic, MagicBytes.Length).AsSpan().SequenceEqual(MagicBytes) ? candidate : 0;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    // The key state function's prologue goes into the stub's trampoline, and a jump to the stub over the prologue.
    private static void PatchInline(nint process, nint page, nint function, byte[] prologue, int stub, int back, int calls)
    {
        var now = Read(process, function, prologue.Length);
        if (!now.AsSpan().SequenceEqual(prologue))
            throw new InvalidOperationException($"the key state function looks different on this Windows ({Convert.ToHexString(now)})");
        Write(process, page + back, BitConverter.GetBytes((long)(function + prologue.Length)));
        Write(process, page + stub, KeyStateCode(page + stub, prologue, page + back, calls));
        var jump = new byte[prologue.Length];
        Array.Fill(jump, (byte)0x90);
        new byte[] { 0xFF, 0x25, 0, 0, 0, 0 }.CopyTo(jump, 0);
        BitConverter.GetBytes((long)(page + stub)).CopyTo(jump, 6);
        WriteProtected(process, function, jump);
    }

    // GetCursorPos(POINT* rcx): the aim while the flag is on, else the original.
    private static byte[] CursorCode(nint at)
    {
        var a = new Assembler(at);
        a.RipRelative([0xFF, 0x05], Data(at, CursorCalls));      // inc dword [calls]
        a.RipRelative([0x83, 0x3D], Data(at, CursorOn), [0x00]); // cmp dword [on], 0
        a.JumpShort(0x74, "original");                           // je original
        a.RipRelative([0x8B, 0x05], Data(at, CursorX));          // mov eax, [x]
        a.Bytes(0x89, 0x01);                                     // mov [rcx], eax
        a.RipRelative([0x8B, 0x05], Data(at, CursorY));          // mov eax, [y]
        a.Bytes(0x89, 0x41, 0x04);                               // mov [rcx+4], eax
        a.Bytes(0xB8, 0x01, 0x00, 0x00, 0x00);                   // mov eax, 1
        a.Bytes(0xC3);                                           // ret
        a.Label("original");
        a.RipRelative([0xFF, 0x25], Data(at, CursorOriginal));   // jmp [original]
        return a.Build();
    }

    // GetKeyState / GetAsyncKeyState(int ecx): down for Shift (and left Shift) while the flag is on, else the original.
    private static byte[] KeyStateCode(nint at, byte[] prologue, nint back, int calls)
    {
        var a = new Assembler(at);
        a.RipRelative([0xFF, 0x05], Data(at, calls));          // inc dword [calls]
        a.RipRelative([0x83, 0x3D], Data(at, ShiftOn), [0x00]); // cmp dword [shift], 0
        a.JumpShort(0x74, "original");                          // je original
        a.Bytes(0x83, 0xF9, 0x10);                              // cmp ecx, VK_SHIFT
        a.JumpShort(0x74, "down");
        a.Bytes(0x81, 0xF9, 0xA0, 0x00, 0x00, 0x00);            // cmp ecx, VK_LSHIFT
        a.JumpShort(0x74, "down");
        a.JumpShort(0xEB, "original");
        a.Label("down");
        a.Bytes(0xB8, 0x00, 0x80, 0xFF, 0xFF);                  // mov eax, 0xFFFF8000 (high bit: down)
        a.Bytes(0xC3);
        a.Label("original");
        a.Bytes(prologue);
        a.RipRelative([0xFF, 0x25], back);                      // jmp [back]
        return a.Build();
    }

    // The page's data at an offset, seen from code at `at` (in the same page).
    private static nint Data(nint at, int offset) => at - (at & 0xFFF) + offset;

    /// <summary>A few x64 instructions with rip relative operands and short jumps to labels.</summary>
    private sealed class Assembler(nint origin)
    {
        private readonly List<byte> _code = [];
        private readonly Dictionary<string, int> _labels = [];
        private readonly List<(int At, string Label)> _jumps = [];

        public void Bytes(params byte[] bytes) => _code.AddRange(bytes);

        public void Label(string name) => _labels[name] = _code.Count;

        // opcode, a 32 bit displacement to `target` from the end of the instruction, then any immediate.
        public void RipRelative(byte[] opcode, nint target, byte[]? immediate = null)
        {
            immediate ??= [];
            var end = origin + _code.Count + opcode.Length + 4 + immediate.Length;
            _code.AddRange(opcode);
            _code.AddRange(BitConverter.GetBytes(checked((int)(target - end))));
            _code.AddRange(immediate);
        }

        public void JumpShort(byte opcode, string label)
        {
            _code.Add(opcode);
            _jumps.Add((_code.Count, label));
            _code.Add(0);
        }

        public byte[] Build()
        {
            foreach (var (at, label) in _jumps)
                _code[at] = (byte)checked((sbyte)(_labels[label] - (at + 1)));
            return [.. _code];
        }
    }

    private static byte[] Read(nint process, nint address, int length)
    {
        var buffer = new byte[length];
        if (!ReadProcessMemory(process, address, buffer, length, out _))
            throw new InvalidOperationException($"cannot read the game at 0x{address:X} (error {Marshal.GetLastWin32Error()})");
        return buffer;
    }

    private static void Write(nint process, nint address, byte[] data)
    {
        if (!WriteProcessMemory(process, address, data, data.Length, out _))
            throw new InvalidOperationException($"cannot write the game at 0x{address:X} (error {Marshal.GetLastWin32Error()})");
    }

    // A write to code or read-only data: made writable for it, then protected as before.
    private static void WriteProtected(nint process, nint address, byte[] data)
    {
        if (!VirtualProtectEx(process, address, data.Length, PageExecuteReadWrite, out var old))
            throw new InvalidOperationException($"cannot unprotect the game at 0x{address:X} (error {Marshal.GetLastWin32Error()})");
        try
        {
            Write(process, address, data);
            FlushInstructionCache(process, address, data.Length);
        }
        finally
        {
            VirtualProtectEx(process, address, data.Length, old, out _);
        }
    }

    private const uint ProcessVmOperation = 0x0008, ProcessVmRead = 0x0010, ProcessVmWrite = 0x0020, ProcessQueryInformation = 0x0400;
    private const uint MemCommit = 0x1000, MemReserve = 0x2000, PageExecuteReadWrite = 0x40;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, bool inherit, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern nint GetProcAddress(nint module, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint VirtualAllocEx(nint process, nint address, nint size, uint type, uint protect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtectEx(nint process, nint address, nint size, uint protect, out uint old);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(nint process, nint address, byte[] buffer, nint size, out nint read);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(nint process, nint address, byte[] buffer, nint size, out nint written);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FlushInstructionCache(nint process, nint address, nint size);
}
