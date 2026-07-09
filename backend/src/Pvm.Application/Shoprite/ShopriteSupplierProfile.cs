namespace Pvm.Application.Shoprite;

public static class ShopriteSupplierProfile
{
    public const string PvmSellerVatRegistrationNumber = "4010137059";

    public static string EffectiveSellerVatRegistrationNumber(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? PvmSellerVatRegistrationNumber
            : value.Trim();
}
