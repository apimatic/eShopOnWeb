using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
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

    /// <summary>Total bills the provider reported in the range (eShop's and everyone else's).</summary>
    public int ProviderInvoiceCount { get; set; }

    /// <summary>Total bills eShop believes it raised in the range.</summary>
    public int EShopInvoiceCount { get; set; }

    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }

    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}

public class ReconciliationEntryDto
{
    public string InvoiceId { get; set; } = string.Empty;

    /// <summary>Which side(s) of the ledger carry this bill: Matched, ProviderOnly or EShopOnly.</summary>
    public string Presence { get; set; } = string.Empty;

    /// <summary>True when this bill is eShop's; false for another activity's bill on the shared account.</summary>
    public bool IsEShopInvoice { get; set; }

    public string? ProviderStatus { get; set; }
    public DateTimeOffset? ProviderCreatedDate { get; set; }

    public int? OrderId { get; set; }
    public string? LocalStatus { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
}
