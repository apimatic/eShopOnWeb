using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PayOrderRequest
{
    [FromRoute(Name = "orderId")] public int OrderId { get; set; }

    [FromBody] public PayOrderBody Body { get; set; } = new();
}

/// <summary>
/// Pay with either one-off card details OR a saved card id - exactly one must be supplied.
/// </summary>
public class PayOrderBody
{
    /// <summary>Raw card details for a one-off payment.</summary>
    public CardDto? Card { get; set; }

    /// <summary>Id of one of the shopper's saved cards to pay with instead.</summary>
    public int? PaymentMethodId { get; set; }
}
