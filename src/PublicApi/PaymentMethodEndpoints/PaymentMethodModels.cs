using Microsoft.eShopWeb.PublicApi.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Request to save (vault) a card for the signed-in shopper.</summary>
public class CreatePaymentMethodRequest
{
    public CardInputModel Card { get; set; } = new();

    /// <summary>Optional shopper-supplied label to recognise the card.</summary>
    public string? Alias { get; set; }
}

/// <summary>
/// A saved card as it can be shown back to the shopper. Carries only a display-safe descriptor —
/// never full card details.
/// </summary>
public class SavedCardDto
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? Alias { get; set; }
}
