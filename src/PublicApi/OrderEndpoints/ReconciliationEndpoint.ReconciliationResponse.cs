using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

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
}

public class ReconciliationEntryDto
{
    public string MatchStatus { get; set; } = string.Empty;
    public string? PayPalTransactionId { get; set; }
    public string? PayPalOrderId { get; set; }
    public int? OrderId { get; set; }
    public decimal? PayPalAmount { get; set; }
    public decimal? EShopAmount { get; set; }
    public string? PayPalStatus { get; set; }
    public string? EShopStatus { get; set; }
}
