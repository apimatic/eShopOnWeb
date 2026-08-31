using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>
/// A bill together with the provider's account of how it reached its current state. Returned by the
/// read/act operations so an endpoint can shape the response.
/// </summary>
public record InvoiceDetails(Invoice Invoice, IReadOnlyList<ProviderInvoiceEvent> ProviderHistory);

/// <summary>Which side(s) of the reconciliation know about a bill.</summary>
public enum ReconciliationSource
{
    /// <summary>Both eShop and the provider have a record of this bill.</summary>
    EShopAndProvider = 0,

    /// <summary>The provider has this bill but eShop does not — i.e. it was not raised by this application.</summary>
    ProviderOnly = 1,

    /// <summary>eShop raised this bill but the provider's range record does not include it.</summary>
    EShopOnly = 2
}

/// <summary>One line of the reconciliation report — a single bill and which side(s) know about it.</summary>
public record ReconciliationEntry(
    int? InvoiceId,
    int? OrderId,
    string ProviderInvoiceId,
    ReconciliationSource Source,
    InvoiceStatus? EShopStatus,
    string? ProviderStatus,
    string? Amount,
    string? CurrencyCode,
    string? CreatedDate,
    string? BuyerId);

/// <summary>
/// The provider's record of bills raised in a range, lined up against what eShop believes it raised, so a
/// bill known to only one side is visible as such.
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
