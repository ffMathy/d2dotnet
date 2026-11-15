namespace D2Dotnet.Map.Data;

/// <summary>Game difficulty (upstream <c>@diablo2/data</c> <c>difficulty.ts</c>).</summary>
public enum Difficulty
{
    Normal = 0,
    Nightmare = 1,
    Hell = 2,
}

/// <summary>Difficulty helpers (upstream <c>DifficultyUtil</c>).</summary>
public static class DifficultyUtil
{
    /// <summary>
    /// Parse a difficulty from a number (JavaScript <c>Number()</c> rules) or a name (<c>"Normal"</c>, case insensitive).
    /// </summary>
    public static Difficulty? FromString(string? f)
    {
        if (f == null) return null;
        var num = JsNumber.Parse(f);
        if (!double.IsNaN(num))
        {
            if (JsNumber.IsEnumKey<Difficulty>(num)) return (Difficulty)(int)num;
            return null;
        }
        return f.ToLowerInvariant() switch
        {
            "normal" => Difficulty.Normal,
            "nightmare" => Difficulty.Nightmare,
            "hell" => Difficulty.Hell,
            _ => null,
        };
    }
}
