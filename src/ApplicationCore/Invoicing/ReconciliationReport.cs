using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>Which side(s) know about a given bill within the reconciled range.</summary>
public enum ReconciliationSource
{
    /// <summary>Both the provider and eShop have a record of the bill.</summary>
    Both = 0,

    /// <summary>The provider knows about the bill but eShop does not (e.g. raised by other activity).</summary>
    ProviderOnly = 1,

    /// <summary>eShop believes it raised the bill but the provider's record does not show it in range.</summary>
    EShopOnly = 2
}

/// <summary>A single bill lined up across the provider's record and eShop's record.</summary>
public record ReconciliationEntry
{
    public string InvoiceId { get; init; } = string.Empty;

    public ReconciliationSource Source { get; init; }

    /// <summary>The provider-owned status, when the provider knows about the bill.</summary>
    public string? ProviderStatus { get; init; }

    /// <summary>eShop's lifecycle state, when eShop has a record.</summary>
    public string? EShopStatus { get; init; }

    public DateTimeOffset? CreatedDate { get; init; }

    public decimal? Amount { get; init; }

    public string? Currency { get; init; }

    public string? CustomerName { get; init; }

    /// <summary>The eShop order the bill was raised against, when eShop has a record.</summary>
    public int? OrderId { get; init; }
}

/// <summary>
/// The operator's view of what has actually been billed over a range: the provider's own record of
/// bills raised, lined up against what eShop believes it raised, making plain which is which.
/// </summary>
public record ReconciliationReport
{
    public DateTimeOffset From { get; init; }

    public DateTimeOffset To { get; init; }

    public IReadOnlyList<ReconciliationEntry> Entries { get; init; } = Array.Empty<ReconciliationEntry>();

    public int TotalCount => Entries.Count;

    public int MatchedCount => Entries.Count(e => e.Source == ReconciliationSource.Both);

    public int ProviderOnlyCount => Entries.Count(e => e.Source == ReconciliationSource.ProviderOnly);

    public int EShopOnlyCount => Entries.Count(e => e.Source == ReconciliationSource.EShopOnly);
}
