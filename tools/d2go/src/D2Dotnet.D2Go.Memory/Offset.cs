// Port of d2go pkg/memory/offset.go and addresses.go (github.com/hectorgimenez/d2go, MIT, (c) 2023 Héctor Giménez).

using System.Buffers.Binary;

namespace D2Dotnet.D2Go.Memory;

/// <summary>
/// Offsets from the module base of the game structures, found by scanning the game code for byte patterns (Go
/// <c>memory.Offset</c>). <see cref="Hover"/> is relative to the module base like the others.
/// </summary>
public readonly record struct Offset(
    nuint GameData,
    nuint UnitTable,
    nuint UI,
    nuint Hover,
    nuint Expansion,
    nuint RosterOffset,
    nuint PanelManagerContainerOffset);

/// <summary>A code pattern of offset.go: bytes, and a mask where <c>?</c> matches any byte.</summary>
internal sealed record OffsetPattern(string Name, byte[] Bytes, string Mask, bool ByOperand = false);

/// <summary>The pattern scan of offset.go (Go <c>calculateOffsets</c>) and the fixed addresses of addresses.go.</summary>
internal static class Offsets
{
    // addresses.go: terror zone areas, relative to the module base.
    internal const nuint TzOffline = 0x22D6D44;
    internal const nuint TzOnline = 0x29BD4D8;

    // offset.go, in the order calculateOffsets scans them.
    internal static readonly OffsetPattern GameData = new("GameData", [0x44, 0x88, 0x25, 0x00, 0x00, 0x00, 0x00, 0x66, 0x44, 0x89, 0x25, 0x00, 0x00, 0x00, 0x00], "xxx????xxxx????");
    internal static readonly OffsetPattern UnitTable = new("UnitTable", [0x48, 0x03, 0xC7, 0x49, 0x8B, 0x8C, 0xC6], "xxxxxxx");
    internal static readonly OffsetPattern UI = new("UI", [0x40, 0x84, 0xed, 0x0f, 0x94, 0x05], "xxxxxx");
    internal static readonly OffsetPattern Hover = new("Hover", [0xc6, 0x84, 0xc2, 0x00, 0x00, 0x00, 0x00, 0x00, 0x48, 0x8b, 0x74], "xxx?????xxx");
    internal static readonly OffsetPattern Expansion = new("Expansion", [0x48, 0x8B, 0x05, 0x00, 0x00, 0x00, 0x00, 0x48, 0x8B, 0xD9, 0xF3, 0x0F, 0x10, 0x50, 0x00], "xxx????xxxxxxx?");
    internal static readonly OffsetPattern Roster = new("Roster", [0x02, 0x45, 0x33, 0xD2, 0x4D, 0x8B], "xxxxxx");
    internal static readonly OffsetPattern PanelManagerContainer = new("PanelManagerContainer", [0x48, 0x89, 0x05, 0x00, 0x00, 0x00, 0x00, 0x48, 0x85, 0xDB, 0x74, 0x1E], "xxx????xxxxx", ByOperand: true);

    internal static IReadOnlyList<OffsetPattern> All { get; } = [GameData, UnitTable, UI, Hover, Expansion, Roster, PanelManagerContainer];

    /// <summary>
    /// Scans the module image for the patterns and decodes the offsets (Go <c>calculateOffsets</c>). All arithmetic
    /// wraps like Go's <c>uintptr</c>, so a pattern that is not found (address 0) gives a meaningless offset rather
    /// than an error, as in Go.
    /// </summary>
    /// <exception cref="IndexOutOfRangeException">The module image could not be read (Go ignores the error, then panics indexing the nil image).</exception>
    internal static Offset Calculate(GameProcess process)
    {
        // ignoring errors, always best practices
        ReadOnlySpan<byte> memory = process.GetProcessMemory() ?? [];
        var moduleBase = process.ModuleBaseAddress;

        unchecked
        {
            // GameReader
            var pattern = process.FindPattern(memory, GameData.Bytes, GameData.Mask);
            var bytes = process.ReadBytesFromMemory(pattern + 0x3, 4);
            var offsetInt = (nuint)BinaryPrimitives.ReadUInt32LittleEndian(bytes);
            var gameDataOffset = pattern - moduleBase - 0x121 + offsetInt;

            // UnitTable
            pattern = process.FindPattern(memory, UnitTable.Bytes, UnitTable.Mask);
            bytes = process.ReadBytesFromMemory(pattern + 7, 4);
            var unitTableOffset = (nuint)BinaryPrimitives.ReadUInt32LittleEndian(bytes);

            // UI
            pattern = process.FindPattern(memory, UI.Bytes, UI.Mask);
            var uiOffset = process.ReadUInt(pattern + 6, IntType.Uint32);
            var uiOffsetPtr = pattern - moduleBase + 10 + (nuint)uiOffset;

            // Hover
            pattern = process.FindPattern(memory, Hover.Bytes, Hover.Mask);
            var hoverOffset = process.ReadUInt(pattern + 3, IntType.Uint32) - 1;

            // Expansion
            pattern = process.FindPattern(memory, Expansion.Bytes, Expansion.Mask);
            var offsetPtr = (nuint)process.ReadUInt(pattern + 3, IntType.Uint32);
            var expOffset = pattern - moduleBase + 7 + offsetPtr;

            // Party members offset
            pattern = process.FindPattern(memory, Roster.Bytes, Roster.Mask);
            offsetPtr = (nuint)process.ReadUInt(pattern - 3, IntType.Uint32);
            var rosterOffset = pattern - moduleBase + 1 + offsetPtr;

            // PanelManagerContainer
            pattern = process.FindPatternByOperand(memory, PanelManagerContainer.Bytes, PanelManagerContainer.Mask);
            _ = process.ReadBytesFromMemory(pattern, 8); // read and unused in Go too
            var panelManagerContainerOffset = pattern - moduleBase;

            return new Offset(
                GameData: gameDataOffset,
                UnitTable: unitTableOffset,
                UI: uiOffsetPtr,
                Hover: (nuint)hoverOffset,
                Expansion: expOffset,
                RosterOffset: rosterOffset,
                PanelManagerContainerOffset: panelManagerContainerOffset);
        }
    }
}
