using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Ietp.Telemetry.Domain;

public static partial class SeriesIdentity
{
    private static readonly Guid NamespaceId = Guid.Parse("a107c86a-a438-5db7-8d7c-766a1a6d7f31");

    public static string CreateKey(string country, string environment, string site, string asset, string measurement)
    {
        var key = string.Join('.', country, environment, site, asset, measurement).ToLowerInvariant();
        return KeyPattern().IsMatch(key)
            ? key
            : throw new ArgumentException("Series keys require five dot-separated lowercase alphanumeric/underscore segments.");
    }

    public static Guid CreateId(string seriesKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seriesKey);
        if (!KeyPattern().IsMatch(seriesKey))
        {
            throw new ArgumentException("Series key is not canonical.", nameof(seriesKey));
        }

        Span<byte> namespaceBytes = stackalloc byte[16];
        NamespaceId.TryWriteBytes(namespaceBytes, bigEndian: true, out _);
        var nameBytes = Encoding.UTF8.GetBytes(seriesKey);
        var input = new byte[namespaceBytes.Length + nameBytes.Length];
        namespaceBytes.CopyTo(input);
        nameBytes.CopyTo(input.AsSpan(namespaceBytes.Length));
        Span<byte> hash = stackalloc byte[20];
        // UUID v5 requires SHA-1 for interoperability; this is identity derivation, not security.
#pragma warning disable CA5350
        SHA1.HashData(input, hash);
#pragma warning restore CA5350
        hash[6] = (byte)((hash[6] & 0x0f) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
        return new Guid(hash[..16], bigEndian: true);
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9_]*\\.[a-z0-9][a-z0-9_]*\\.[a-z0-9][a-z0-9_]*\\.[a-z0-9][a-z0-9_]*\\.[a-z0-9][a-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
