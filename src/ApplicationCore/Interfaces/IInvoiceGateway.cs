using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The boundary to the external invoicing provider (Visa, via its CyberSource platform). Every call
/// eShop makes to the provider goes through this gateway; the implementation lives in the
/// Infrastructure layer so the provider SDK never leaks into the application or API.
/// </summary>
public interface IInvoiceGateway
{
    /// <summary>Raise a brand-new bill. It starts out not yet put to the shopper (DRAFT).</summary>
    Task<ProviderInvoice> CreateInvoiceAsync(InvoiceDraft draft, CancellationToken cancellationToken = default);

    /// <summary>Ask the provider for a bill's current state, history and payment link.</summary>
    Task<ProviderInvoice> GetInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>Correct the due date / customer details of a bill still in DRAFT.</summary>
    Task<ProviderInvoice> UpdateInvoiceAsync(string providerInvoiceId, InvoiceAmendment amendment, CancellationToken cancellationToken = default);

    /// <summary>Put the bill to the shopper so it becomes payable.</summary>
    Task<ProviderInvoice> IssueInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>Withdraw the bill so it is no longer payable.</summary>
    Task<ProviderInvoice> WithdrawInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// List the provider's own record of bills created within the given range. Covers the whole
    /// range (the gateway pages through the provider on the caller's behalf).
    /// </summary>
    Task<IReadOnlyList<ProviderInvoiceSummary>> ListInvoicesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
