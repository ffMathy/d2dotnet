namespace D2Dotnet.Maps;

/// <summary>
/// What the map generator needs to rebuild the map a game is played on: the game's map seed and difficulty, and the
/// area of interest (usually where the player is). Read one from a running game with
/// <see cref="Memory.GameReader.ReadMapContext()"/> (or <see cref="Game.GameSnapshot.Map"/>), then pass it to
/// <see cref="MapGenerator.Generate(MapContext)"/>.
/// </summary>
/// <param name="Seed">The game's map seed.</param>
/// <param name="Difficulty">The game's difficulty.</param>
/// <param name="Area">The area of interest; its act is the act to generate.</param>
public readonly record struct MapContext(uint Seed, Difficulty Difficulty, AreaId Area)
{
    /// <summary>The act <see cref="Area"/> is in.</summary>
    public ActId Act => Game.Areas.GetAct(Area);
}
