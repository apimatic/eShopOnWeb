using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationEntryDto
{
    public string? PayPalTransactionId { get; set; }
    public int? OrderId { get; set; }
    public decimal? PayPalAmount { get; set; }
    public decimal? EShopAmount { get; set; }
    public string? PayPalStatus { get; set; }

    /// <summary>"Matched", "AmountMismatch", "PayPalOnly", or "EShopOnly".</summary>
    public string MatchStatus { get; set; } = string.Empty;
}

public class GetReconciliationResponse : BaseResponse
{
    public GetReconciliationResponse(Guid correlationId) : base(correlationId)
    {
    }

    public GetReconciliationResponse()
    {
    }

    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}
