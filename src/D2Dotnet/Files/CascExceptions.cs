namespace D2Dotnet.Files;

/// <summary>The CASC storage is malformed, truncated, fails a checksum, or uses a feature this reader does not support.</summary>
public class CascFormatException : IOException
{
    public CascFormatException(string message) : base(message) { }

    public CascFormatException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>A BLTE frame is encrypted (<c>'E'</c> mode) and no key for it is known.</summary>
public sealed class CascEncryptedException : CascFormatException
{
    public CascEncryptedException(ulong keyName, char cipher)
        : base($"BLTE frame is encrypted with key {keyName:X16} (cipher '{cipher}'); no decryption key is known. " +
               "This reader does not decrypt CASC content.")
    {
        KeyName = keyName;
    }

    /// <summary>The 8-byte TACT key name, read little-endian (the convention used by CascLib's key tables).</summary>
    public ulong KeyName { get; }
}
