using System;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>
/// A row from the provider's own record of bills, as returned by its list operation. Used to reconcile
/// the provider's record against what eShopOnWeb believes it raised.
/// </summary>
public record ProviderInvoiceSummary
{
    public required string ProviderInvoiceId { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? CreatedDate { get; init; }
}
