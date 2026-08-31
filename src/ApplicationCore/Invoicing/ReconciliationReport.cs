using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>
/// Where an invoice in the reconciliation report was found. The provider account carries bills
/// that are not this application's, so the report must make plain which is which.
/// </summary>
public enum ReconciliationSource
{
    /// <summary>Known to both the provider and eShop — they line up.</summary>
    Matched,

    /// <summary>The provider knows about this bill but eShop has no record of raising it (e.g. another application's bill).</summary>
    ProviderOnly,

    /// <summary>eShop believes it raised this bill but the provider's record does not show it.</summary>
    EShopOnly
}

/// <summary>
/// A report lining the provider's own record of bills raised in a date range up against what
/// eShop believes it raised, over the whole range.
/// </summary>
public class ReconciliationReport
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public int ProviderInvoiceCount { get; init; }
    public int EShopInvoiceCount { get; init; }
    public int MatchedCount { get; init; }
    public int ProviderOnlyCount { get; init; }
    public int EShopOnlyCount { get; init; }
    public IReadOnlyList<ReconciliationEntry> Entries { get; init; } = Array.Empty<ReconciliationEntry>();
}

public class ReconciliationEntry
{
    public required string InvoiceId { get; init; }
    public required ReconciliationSource Source { get; init; }

    /// <summary>True when eShop recognises this bill as one of its own.</summary>
    public bool RecognizedByEShop { get; init; }

    /// <summary>The provider's reported status, when the provider knows the bill.</summary>
    public string? ProviderStatus { get; init; }

    /// <summary>eShop's linkage, when eShop knows the bill.</summary>
    public int? OrderId { get; init; }
    public string? BuyerId { get; init; }

    public decimal? Amount { get; init; }
    public string? CurrencyCode { get; init; }
    public string? CustomerName { get; init; }
    public DateTimeOffset? ProviderCreatedDate { get; init; }
    public DateTimeOffset? EShopCreatedAt { get; init; }
}
