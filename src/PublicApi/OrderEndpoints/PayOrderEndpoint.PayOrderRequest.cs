using System;
using Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    /// <summary>Raw card details for a one-off payment. Mutually exclusive with PaymentMethodId.</summary>
    public CardDetailsDto? Card { get; set; }

    /// <summary>Id of one of the caller's saved cards (POST api/payment-methods) to pay with.</summary>
    public int? PaymentMethodId { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public int OrderId { get; set; }
    public int PaymentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public decimal AuthorizedAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
}
