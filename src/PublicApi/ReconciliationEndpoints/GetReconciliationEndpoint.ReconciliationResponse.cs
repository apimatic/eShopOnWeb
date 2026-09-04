using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse() { }

    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    public int MatchedCount { get; set; }
    public int PayPalOnlyCount { get; set; }
    public int EshopOnlyCount { get; set; }
    public string CoverageNote { get; set; } = string.Empty;
    public List<ReconciliationRowDto> Rows { get; set; } = new();
}

public class ReconciliationRowDto
{
    /// <summary>Matched | PayPalOnly | EshopOnly.</summary>
    public string MatchState { get; set; } = string.Empty;

    public string? TransactionId { get; set; }
    public string? TransactionStatus { get; set; }
    public string? TransactionEventCode { get; set; }
    public decimal? Amount { get; set; }
    public decimal? FeeAmount { get; set; }
    public decimal? NetAmount { get; set; }
    public string? Currency { get; set; }
    public string? ProviderInvoiceId { get; set; }
    public string? ProviderReferenceId { get; set; }
    public int? OrderId { get; set; }
    public string? OrderStatus { get; set; }
    public decimal? OrderTotal { get; set; }
    public string? OrderBuyerId { get; set; }
    public string? OrderPaymentSummary { get; set; }
    public DateTimeOffset? TransactionDate { get; set; }
}
