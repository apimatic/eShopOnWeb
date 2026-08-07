using System;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.PublicApi.PaymentShared;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves a card for the signed-in shopper. The card is vaulted with PayPal; this app keeps only
/// the token reference and a safe descriptor. Full card details are never stored.
/// </summary>
public class SavePaymentMethodRequest : CardRequest
{
    /// <summary>Set server-side from the JWT; never bound from the request body.</summary>
    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class SavePaymentMethodResponse : BaseResponse
{
    public SavePaymentMethodResponse(Guid correlationId) : base(correlationId) { }

    public SavePaymentMethodResponse() { }

    /// <summary>The saved card's identifier, exposed as a top-level field so it can be reused to pay.</summary>
    public int PaymentMethodId { get; set; }

    public PaymentMethodDto? PaymentMethod { get; set; }
}
