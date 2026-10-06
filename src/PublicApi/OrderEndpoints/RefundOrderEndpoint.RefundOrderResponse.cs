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
    public string? RefundId { get; set; }

    /// <summary>Received, Pending, Rejected, ProviderUnavailable, ExceedsRefundable, InvalidAmount, NotPaid, InProgress.</summary>
    public string Outcome { get; set; } = string.Empty;
    public string? RefundStatus { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public string? PspReference { get; set; }

    /// <summary>What can still be refunded on the order after this request.</summary>
    public decimal RefundableAmount { get; set; }
    public string Message { get; set; } = string.Empty;
}
