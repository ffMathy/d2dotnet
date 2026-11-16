namespace D2Dotnet.MapServer.Data;

/// <summary>Difficulty helpers (upstream <c>@diablo2/data</c> <c>difficulty.ts</c> <c>DifficultyUtil</c>) over the library's <see cref="Difficulty"/>.</summary>
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
