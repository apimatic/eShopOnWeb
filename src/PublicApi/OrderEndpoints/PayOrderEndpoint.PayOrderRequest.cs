using System;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }

    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;

    /// <summary>
    /// One-off card details. Mutually exclusive with <see cref="PaymentMethodId"/>.
    /// </summary>
    public CardRequest? Card { get; set; }

    /// <summary>
    /// Id of one of the caller's saved cards (see POST /api/payment-methods).
    /// </summary>
    public int? PaymentMethodId { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public PaymentDto? Payment { get; set; }
}
