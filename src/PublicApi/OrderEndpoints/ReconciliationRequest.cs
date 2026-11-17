using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationItemResponse> Matched { get; set; } = new();
    public List<ReconciliationItemResponse> FinixOnly { get; set; } = new();
    public List<ReconciliationItemResponse> EshopOnly { get; set; } = new();
    public int FinixTransactionCount { get; set; }
    public int EshopTransactionCount { get; set; }
}

public class ReconciliationItemResponse
{
    public string? FinixTransactionId { get; set; }
    public int? OrderId { get; set; }
    public int? RefundId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public long AmountMinor { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? CreatedAt { get; set; }
    public string? OrderReference { get; set; }
    public string? Note { get; set; }
}
