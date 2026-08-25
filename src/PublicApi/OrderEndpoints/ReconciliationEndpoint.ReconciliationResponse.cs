using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public record ReconciliationEntryDto(
    int? OrderId,
    string? PayPalTransactionId,
    decimal? EShopAmount,
    decimal? PayPalAmount,
    string? EShopStatus,
    string? PayPalStatus,
    string MatchStatus);

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ReconciliationResponse()
    {
    }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationEntryDto> Entries { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
