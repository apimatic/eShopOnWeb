using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The port through which this application talks to the invoicing provider. The only implementation is the
/// CyberSource adapter in the infrastructure layer; it is the sole place provider SDK types are used, and
/// every failure it surfaces is an <see cref="Exceptions.InvoicingProviderException"/>.
/// </summary>
public interface IInvoicingProvider
{
    /// <summary>Raise a new bill. It starts out not yet put to the shopper.</summary>
    Task<ProviderInvoice> RaiseAsync(RaiseInvoiceCommand command, CancellationToken cancellationToken = default);

    /// <summary>Read the provider's current view of a bill, including its status history and payment link.</summary>
    Task<ProviderInvoice> GetAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>Correct a bill's due date and customer details with the provider.</summary>
    Task<ProviderInvoice> UpdateAsync(string providerInvoiceId, UpdateInvoiceCommand command, CancellationToken cancellationToken = default);

    /// <summary>Put the bill to the shopper (deliver it). Afterwards a payment link can be handed out.</summary>
    Task<ProviderInvoice> SendAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>Withdraw (cancel) a bill so it is no longer payable.</summary>
    Task<ProviderInvoice> CancelAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The provider's own record of invoices created within the range. The provider offers no server-side
    /// date filter, so the adapter pages the account and filters on each record's created date.
    /// </summary>
    Task<IReadOnlyList<ProviderInvoiceSummary>> ListCreatedBetweenAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
