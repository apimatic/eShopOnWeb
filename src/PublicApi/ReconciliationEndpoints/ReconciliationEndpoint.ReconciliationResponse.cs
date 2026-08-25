using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

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
    /// <summary>"Matched", "PayPalOnly" (PayPal knows about it, eShop doesn't), or "EShopOnly" (the reverse).</summary>
    public string MatchStatus { get; set; } = string.Empty;

    public string? PayPalTransactionId { get; set; }
    public string? PayPalStatus { get; set; }
    public decimal? PayPalAmount { get; set; }
    public string? PayPalCurrency { get; set; }

    public int? OrderId { get; set; }
    public string? OrderStatus { get; set; }
    public decimal? OrderCapturedAmount { get; set; }
}
