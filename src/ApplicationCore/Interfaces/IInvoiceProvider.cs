using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the external invoicing provider (Visa, via its CyberSource platform). The
/// application core depends only on this contract; the concrete implementation lives in the
/// Infrastructure project and is the only place that talks to the provider's SDK.
/// </summary>
public interface IInvoiceProvider
{
    /// <summary>Raise a new bill held by the provider in a not-yet-issued (draft) state.</summary>
    Task<ProviderInvoice> CreateDraftInvoiceAsync(NewInvoice invoice, CancellationToken cancellationToken = default);

    /// <summary>Read the provider's current view of a bill, including how it can be paid.</summary>
    Task<ProviderInvoice> GetInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>Correct the due date / customer details of a bill that has not yet been issued.</summary>
    Task<ProviderInvoice> UpdateInvoiceAsync(string providerInvoiceId, InvoiceAmendment amendment, CancellationToken cancellationToken = default);

    /// <summary>Put the bill to the shopper. Afterwards a payment link can be handed out.</summary>
    Task<ProviderInvoice> PublishInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>Withdraw the bill so that it can no longer be paid.</summary>
    Task<ProviderInvoice> CancelInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// List every bill the provider records as created within the given UTC window. Covers the whole
    /// range (paging through the provider as needed). Includes bills that are not eShop's, so callers
    /// must reconcile against eShop's own records to tell them apart.
    /// </summary>
    Task<IReadOnlyList<ProviderInvoiceSummary>> ListInvoicesCreatedBetweenAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);
}
