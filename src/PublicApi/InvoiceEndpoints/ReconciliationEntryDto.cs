using System;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// One reconciliation row lining up a bill between the provider and eShop. <see cref="BelongsToEShop"/>
/// makes plain which provider bills are this application's and which belong to other activity on the account.
/// </summary>
public class ReconciliationEntryDto
{
    /// <summary>eShop's identifier (the operator endpoints act on it), when eShop has a record of the bill.</summary>
    public int? InvoiceId { get; set; }

    public string? ProviderInvoiceId { get; set; }

    /// <summary>One of: Reconciled, MissingFromEShop, MissingFromProvider, ForeignProviderInvoice.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>False for a bill on the provider account that is not this application's.</summary>
    public bool BelongsToEShop { get; set; }

    public bool PresentAtProvider { get; set; }
    public bool PresentInEShop { get; set; }

    public string? MerchantCustomerId { get; set; }
    public string? ProviderStatus { get; set; }

    /// <summary>eShop's lifecycle status, when eShop has a record of the bill.</summary>
    public string? EShopStatus { get; set; }

    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? CreatedDate { get; set; }
}
