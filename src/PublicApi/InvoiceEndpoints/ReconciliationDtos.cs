using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>One line of the reconciliation report.</summary>
public class ReconciliationEntryResponse
{
    public string InvoiceId { get; set; } = string.Empty;

    /// <summary>
    /// Which ledger the bill appears in: <c>matched</c> (eShop and provider), <c>providerOnly</c>
    /// (the provider knows of it but eShop does not — not this application's bill), or <c>eShopOnly</c>
    /// (eShop believes it raised it but the provider does not show it).
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Whether this bill is one of this application's (as opposed to other activity on the account).</summary>
    public bool IsEShopInvoice { get; set; }

    public string? ProviderStatus { get; set; }
    public string? EShopStatus { get; set; }
    public DateTimeOffset? ProviderCreatedUtc { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public int? OrderId { get; set; }
    public string? BuyerId { get; set; }
    public string? CustomerName { get; set; }
}

/// <summary>
/// The reconciliation report: the provider's own record of bills raised in a date range lined up
/// against what eShop believes it raised, making plain which bills are this application's.
/// </summary>
public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    /// <summary>Total bills the provider recorded in the range (this application's and others').</summary>
    public int ProviderCount { get; set; }

    /// <summary>Bills eShop believes it raised in the range.</summary>
    public int EShopCount { get; set; }

    /// <summary>Bills known to both.</summary>
    public int MatchedCount { get; set; }

    /// <summary>Bills the provider recorded that are not this application's.</summary>
    public int ProviderOnlyCount { get; set; }

    /// <summary>Bills eShop believes it raised that the provider does not show.</summary>
    public int EShopOnlyCount { get; set; }

    public IReadOnlyList<ReconciliationEntryResponse> Entries { get; set; } = new List<ReconciliationEntryResponse>();

    public static ReconciliationResponse FromReport(ReconciliationReport report) => new()
    {
        From = report.From,
        To = report.To,
        ProviderCount = report.ProviderCount,
        EShopCount = report.EShopCount,
        MatchedCount = report.MatchedCount,
        ProviderOnlyCount = report.ProviderOnlyCount,
        EShopOnlyCount = report.EShopOnlyCount,
        Entries = report.Entries.Select(e => new ReconciliationEntryResponse
        {
            InvoiceId = e.InvoiceId,
            Source = SourceLabel(e.Source),
            IsEShopInvoice = e.Source != ReconciliationSource.ProviderOnly,
            ProviderStatus = e.ProviderStatus,
            EShopStatus = e.EShopStatus,
            ProviderCreatedUtc = e.ProviderCreatedUtc,
            Amount = e.Amount,
            Currency = e.Currency,
            OrderId = e.OrderId,
            BuyerId = e.BuyerId,
            CustomerName = e.CustomerName
        }).ToList()
    };

    private static string SourceLabel(ReconciliationSource source) => source switch
    {
        ReconciliationSource.Matched => "matched",
        ReconciliationSource.ProviderOnly => "providerOnly",
        ReconciliationSource.EShopOnly => "eShopOnly",
        _ => source.ToString()
    };
}
