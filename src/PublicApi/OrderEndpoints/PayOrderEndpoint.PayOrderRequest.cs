using System;
using Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }

    /// <summary>Card details for a one-off payment. Mutually exclusive with SavedPaymentMethodId.</summary>
    public CardDetailsDto? Card { get; set; }

    /// <summary>Id of one of the caller's saved cards (POST /api/payment-methods) to pay with instead.</summary>
    public int? SavedPaymentMethodId { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiryTime { get; set; }
    public decimal Amount { get; set; }
    public string? Currency { get; set; }
}
