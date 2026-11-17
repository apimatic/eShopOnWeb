using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationRequest
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
    public int FinixTransactionCount { get; set; }
    public int MatchedCount { get; set; }
    public int FinixOnlyCount { get; set; }
    public int EshopOnlyCount { get; set; }
    public List<ReconciliationTransactionDto> FinixTransactions { get; set; } = new();
    public List<ReconciliationDiscrepancyDto> Discrepancies { get; set; } = new();
}

public class ReconciliationTransactionDto
{
    public string FinixId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? OperationKey { get; set; }
    public string? State { get; set; }
    public long? AmountCents { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public int? MatchedOrderId { get; set; }
    public string? MatchedLocalType { get; set; }
    public string MatchStatus { get; set; } = string.Empty;
}

public class ReconciliationDiscrepancyDto
{
    public string Side { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? FinixId { get; set; }
    public int? OrderId { get; set; }
    public int? LocalId { get; set; }
    public long? AmountCents { get; set; }
    public string? State { get; set; }
    public string Message { get; set; } = string.Empty;
}
