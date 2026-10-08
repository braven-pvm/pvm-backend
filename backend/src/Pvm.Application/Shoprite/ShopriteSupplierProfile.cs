namespace Pvm.Application.Shoprite;

public static class ShopriteSupplierProfile
{
    public const string PvmSellerVatRegistrationNumber = "4010137059";

    /// <summary>PVM's GLN as a Shoprite supplier. It also prefixes every PVM GTIN (6001197).</summary>
    public const string PvmSupplierGln = "6001197000006";

    public static string EffectiveSellerVatRegistrationNumber(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? PvmSellerVatRegistrationNumber
            : value.Trim();
}
