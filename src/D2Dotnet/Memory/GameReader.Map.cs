using D2Dotnet.Game;
using D2Dotnet.Maps;

namespace D2Dotnet.Memory;

public sealed partial class GameReader
{
    // Player unit -> act -> act misc. The layout comes from MapAssist (Structs/UnitAny.cs pAct at +0x20, Structs/Act.cs
    // pActMisc, Structs/ActMisc.cs GameDifficulty at +0x830) and koolo (internal/reader/reader.go getMapSeed: the init
    // seed hash at act misc +0x840 and the end seed hash at +0x868, 32 bits each). They describe the game; no code is
    // taken from either. Reign of the Warlock moved pActMisc from MapAssist's +0x78 to +0x70 (+0x78 now points into the
    // module) and the act misc fields below 8 bytes down: observed in a running game, where the seed decoded from
    // +0x838/+0x860 is the map id the character's save file holds.
    internal const nuint UnitActOffset = 0x20;
    internal const nuint ActMiscOffset = 0x70;
    internal const nuint DifficultyOffset = 0x828;
    internal const nuint InitSeedHashOffset = 0x838;
    internal const nuint EndSeedHashOffset = 0x860;

    /// <summary>
    /// The map seed, difficulty and area of the main player's game, ready for
    /// <see cref="MapGenerator.Generate(MapContext)"/>; null when not in a game, or when the seed cannot be recovered or
    /// the difficulty is not one the game has.
    /// </summary>
    public MapContext? ReadMapContext() =>
        ReadPlayers(null).FirstOrDefault(p => p.IsMainPlayer) is { } unit ? ReadMapContext(unit) : null;

    internal MapContext? ReadMapContext(PlayerUnit player)
    {
        if (player.Area == 0)
            return null;

        var act = Process.ReadPointer(unchecked(player.Address + UnitActOffset));
        if (act == 0)
            return null;

        var actMisc = Process.ReadPointer(unchecked(act + ActMiscOffset));
        if (actMisc == 0)
            return null;

        var difficulty = Process.ReadUInt16(unchecked(actMisc + DifficultyOffset));
        if (difficulty > (ushort)Difficulty.Hell)
            return null;

        var initHash = Process.ReadUInt32(unchecked(actMisc + InitSeedHashOffset));
        var endHash = Process.ReadUInt32(unchecked(actMisc + EndSeedHashOffset));
        if (!MapSeed.TryGetMapSeed(initHash, endHash, out var seed))
            return null;

        return new MapContext(seed, (Difficulty)difficulty, player.Area);
    }
}
