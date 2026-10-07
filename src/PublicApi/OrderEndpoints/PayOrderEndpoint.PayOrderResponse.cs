using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public PayOrderResponse()
    {
    }

    public int OrderId { get; set; }

    /// <summary>Paid, AlreadyPaid, Declined, InvalidCard, Pending, InProgress, Unknown, ProviderUnavailable, NotFound, Invalid.</summary>
    public string Outcome { get; set; } = string.Empty;

    /// <summary>The order's payment state after this request, when known.</summary>
    public string? PaymentStatus { get; set; }

    /// <summary>What happened, in terms the shopper can act on.</summary>
    public string Message { get; set; } = string.Empty;

    public PaymentAttemptDto? Payment { get; set; }
}
