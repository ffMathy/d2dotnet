namespace D2Dotnet.MapServer.Data;

/// <summary>Game acts (upstream <c>@diablo2/data</c> <c>act.ts</c>).</summary>
public enum Act
{
    ActI = 0,
    ActII = 1,
    ActIII = 2,
    ActIV = 3,
    ActV = 4,
}

/// <summary>LevelCode =&gt; Act, the first level code of the next act (upstream <c>ActLevels</c>).</summary>
public static class ActLevels
{
    public const int ActI = 40;
    public const int ActII = 75;
    public const int ActIII = 103;
    public const int ActIV = 109;
    public const int ActV = 142;
}

/// <summary>Act helpers (upstream <c>ActUtil</c>).</summary>
public static class ActUtil
{
    /// <summary>Act a level code belongs to. Non numbers (NaN) fall through to <see cref="Act.ActV"/> as upstream.</summary>
    public static Act FromLevel(double code)
    {
        if (code < ActLevels.ActI) return Act.ActI;
        if (code < ActLevels.ActII) return Act.ActII;
        if (code < ActLevels.ActIII) return Act.ActIII;
        if (code < ActLevels.ActIV) return Act.ActIV;
        if (code < ActLevels.ActV) return Act.ActV;
        return Act.ActV;
    }

    /// <summary>
    /// Parse an act from a number (<c>"0"</c>, <c>"0x01"</c>, ... using JavaScript <c>Number()</c> rules) or a name (<c>"ActI"</c>, case insensitive).
    /// </summary>
    public static Act? FromString(string? f)
    {
        if (f == null) return null;
        var num = JsNumber.Parse(f);
        if (!double.IsNaN(num))
        {
            if (JsNumber.IsEnumKey<Act>(num)) return (Act)(int)num;
            return null;
        }
        return f.ToLowerInvariant() switch
        {
            "acti" => Act.ActI,
            "actii" => Act.ActII,
            "actiii" => Act.ActIII,
            "activ" => Act.ActIV,
            "actv" => Act.ActV,
            _ => null,
        };
    }
}
