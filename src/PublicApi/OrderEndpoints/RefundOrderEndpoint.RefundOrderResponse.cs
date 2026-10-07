using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public RefundOrderResponse()
    {
    }

    public int OrderId { get; set; }
    public Guid? RefundId { get; set; }

    /// <summary>Accepted, Replayed, NotFound, NotRefundable, ExceedsRefundable, Invalid, InProgress, Rejected, ProviderUnavailable, ProviderDidNotRespond.</summary>
    public string Status { get; set; } = "";

    public string Message { get; set; } = "";

    /// <summary>The refund's own state: InFlight, Received, Rejected or Unknown.</summary>
    public string? RefundStatus { get; set; }

    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? RemainingRefundable { get; set; }
    public string? PspReference { get; set; }
}
