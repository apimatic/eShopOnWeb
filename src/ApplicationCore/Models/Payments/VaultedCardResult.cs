namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

/// <summary>Result of vaulting a card: gateway identifiers plus safe display data only.</summary>
public class VaultedCardResult
{
    public string PaymentTokenId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string CardBrand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}
