using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationEntryDto
{
    /// <summary>Matched, MissingInEShop or MissingInPayPal.</summary>
    public string Status { get; set; } = default!;
    public string? PayPalTransactionId { get; set; }
    public string? PayPalEventCode { get; set; }
    public string? PayPalStatus { get; set; }
    public decimal? PayPalAmount { get; set; }
    public int? OrderId { get; set; }
    public string? EShopReference { get; set; }
    public decimal? EShopAmount { get; set; }
    public string? CurrencyCode { get; set; }
}

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
