using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

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

    public ReconciliationSummaryDto Summary { get; set; } = new();

    public List<ReconciliationEntryDto> Entries { get; set; } = new();

    /// <summary>Explains how the date range is applied given the provider's list has no per-invoice creation date.</summary>
    public string Note { get; set; } = string.Empty;
}
