using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>Where a reconciled bill was found — making plain which record is eShop's and which is not.</summary>
public enum ReconciliationSource
{
    /// <summary>Present both at the provider and in eShop's own records.</summary>
    MatchedBoth = 0,

    /// <summary>The provider knows about this bill but eShop does not (e.g. raised by other activity on the shared account).</summary>
    ProviderOnly = 1,

    /// <summary>eShop believes it raised this bill but the provider's list for the range did not return it.</summary>
    EShopOnly = 2
}

/// <summary>One line of the reconciliation report.</summary>
public record ReconciliationEntry(
    string InvoiceId,
    ReconciliationSource Source,
    bool BelongsToEShop,
    string? ProviderStatus,
    string? EShopState,
    string? Amount,
    string? Currency,
    DateTimeOffset? CreatedDate,
    string? DueDate,
    string? CustomerName,
    string? MerchantCustomerId);

/// <summary>
/// A report over a date range lining up the provider's own record of bills raised against what eShop
/// believes it raised, so a bill the provider knows about and eShop doesn't — or the reverse — is visible.
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
