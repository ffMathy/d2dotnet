namespace D2Dotnet.Map.Data;

/// <summary>Item record (upstream <c>Diablo2Item</c>).</summary>
public sealed record Diablo2Item(string Code, int NameId);

/// <summary>Item table (upstream <c>Diablo2MpqItem</c>).</summary>
public sealed class Diablo2MpqItem(Diablo2MpqData mpq)
{
    public Dictionary<string, Diablo2Item> ByCode { get; } = [];
    public List<Diablo2Item> ByIndex { get; } = [];
    public Diablo2MpqData Mpq { get; } = mpq;

    public void Add(Diablo2Item item)
    {
        ByCode[item.Code] = item;
        ByIndex.Add(item);
    }
}
