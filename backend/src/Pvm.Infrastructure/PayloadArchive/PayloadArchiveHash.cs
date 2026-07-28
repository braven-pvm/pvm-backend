using System.Security.Cryptography;
using System.Text;

namespace Pvm.Infrastructure.PayloadArchive;

internal static class PayloadArchiveHash
{
    public static byte[] Bytes(string content) => Encoding.UTF8.GetBytes(content);

    public static string Sha256(ReadOnlySpan<byte> content)
        => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
}
