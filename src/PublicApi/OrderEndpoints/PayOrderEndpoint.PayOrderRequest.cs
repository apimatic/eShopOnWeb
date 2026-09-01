using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>Id of a saved card (from POST api/payment-methods). Alternative to Card.</summary>
    public int? PaymentMethodId { get; set; }

    /// <summary>One-off card details. Alternative to PaymentMethodId.</summary>
    public CardDetailsDto? Card { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) {}
    public PayOrderResponse() {}

    public int OrderId { get; set; }
    public int PaymentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public decimal AuthorizedAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; set; }
}
