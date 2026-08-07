using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Safe, display-only description of a saved card. Never contains full card details.</summary>
public class SavedCardDto
{
    public int PaymentMethodId { get; set; }
    public string Alias { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;

    public static SavedCardDto From(SavedCardInfo info) => new()
    {
        PaymentMethodId = info.PaymentMethodId,
        Alias = info.Alias,
        Brand = info.Brand,
        Last4 = info.Last4,
        Expiry = info.Expiry
    };
}
