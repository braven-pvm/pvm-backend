using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Primitives;

namespace Pvm.Api.Features.AcumaticaPushNotifications;

public static class AcumaticaPushNotificationSecretValidator
{
    public static bool IsValid(StringValues suppliedValues, string expectedSecret)
    {
        if (suppliedValues.Count != 1 || string.IsNullOrEmpty(suppliedValues[0]))
        {
            return false;
        }

        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(suppliedValues[0]!));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expectedSecret));
        return CryptographicOperations.FixedTimeEquals(suppliedHash, expectedHash);
    }
}
