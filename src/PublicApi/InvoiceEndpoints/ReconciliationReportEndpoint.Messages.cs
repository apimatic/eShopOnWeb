using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class ReconciliationRequest
{
    public ReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
}

/// <summary>One reconciled bill, labelled with where it was found so eShop's records and the provider's
/// other activity are told apart.</summary>
public class ReconciliationEntryDto
{
    public string InvoiceId { get; set; } = string.Empty;

    /// <summary>MatchedBoth, ProviderOnly, or EShopOnly.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>True when this bill is one of eShop's (matched, or carrying eShop's marker).</summary>
    public bool BelongsToEShop { get; set; }

    public string? ProviderStatus { get; set; }
    public string? EShopState { get; set; }
    public string? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? CreatedDate { get; set; }
    public string? DueDate { get; set; }
    public string? CustomerName { get; set; }
    public string? MerchantCustomerId { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    /// <summary>How many bills the provider reported as raised in the range.</summary>
    public int ProviderCount { get; set; }

    /// <summary>How many bills eShop believes it raised in the range.</summary>
    public int EShopCount { get; set; }

    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }

    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}
