using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    /// <summary>One-off card details for this payment.</summary>
    public CardRequest? Card { get; set; }

    /// <summary>Id of a saved card (from POST api/payment-methods) to pay with instead.</summary>
    public int? PaymentMethodId { get; set; }

    [JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>Populated from the JWT, never from the request body.</summary>
    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public PaymentDto Payment { get; set; } = new();
}
