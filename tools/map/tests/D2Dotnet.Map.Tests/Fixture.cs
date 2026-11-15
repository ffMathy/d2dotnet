namespace D2Dotnet.Map.Tests;

public static class Fixture
{
    public static string Path(params string[] parts) =>
        System.IO.Path.Join([AppContext.BaseDirectory, "Fixtures", .. parts]);

    public static byte[] Bytes(params string[] parts) => File.ReadAllBytes(Path(parts));
}
