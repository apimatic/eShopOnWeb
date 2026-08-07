using Microsoft.eShopWeb.PublicApi.PaymentShared;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Request to save a card for the signed-in shopper.</summary>
public class CreatePaymentMethodRequest
{
    public CardDto? Card { get; set; }

    /// <summary>Optional shopper-supplied nickname, e.g. "personal visa".</summary>
    public string? Alias { get; set; }
}
