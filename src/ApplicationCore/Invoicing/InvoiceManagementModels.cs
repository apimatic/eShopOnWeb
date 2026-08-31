using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>
/// A bill as the application knows it: eShop's local record paired with the live state read back from
/// the provider. The provider is authoritative for status and payment link; the local record ties the
/// bill to its order and owner.
/// </summary>
public record InvoiceSnapshot(Invoice Local, VisaInvoiceState Provider);

/// <summary>Which ledger a reconciliation entry appears in.</summary>
public enum ReconciliationSource
{
    /// <summary>Known to both eShop and the provider — a bill eShop raised and the provider recorded.</summary>
    Matched,

    /// <summary>Recorded by the provider but not by eShop — a bill that is not this application's.</summary>
    ProviderOnly,

    /// <summary>Recorded by eShop but not returned by the provider — eShop believes it raised it, the provider does not show it.</summary>
    EShopOnly
}

/// <summary>One line of the reconciliation report, keyed by the provider invoice id.</summary>
public record ReconciliationEntry(
    string InvoiceId,
    ReconciliationSource Source,
    string? ProviderStatus,
    DateTimeOffset? ProviderCreatedUtc,
    decimal? Amount,
    string? Currency,
    int? OrderId,
    string? BuyerId,
    string? CustomerName,
    string? EShopStatus);

/// <summary>
/// The reconciliation report over a date range: the provider's own record of bills raised, lined up
/// against what eShop believes it raised, making plain which bills are this application's and which are not.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderCount,
    int EShopCount,
    int MatchedCount,
    int ProviderOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<ReconciliationEntry> Entries);
