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

    public int TotalCount { get; set; }

    /// <summary>Bills both the provider and eShop have a record of.</summary>
    public int MatchedCount { get; set; }

    /// <summary>Bills the provider knows about but eShop does not (not this application's).</summary>
    public int ProviderOnlyCount { get; set; }

    /// <summary>Bills eShop believes it raised but the provider's record does not show in range.</summary>
    public int EShopOnlyCount { get; set; }

    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}
