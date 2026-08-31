using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>
/// The port through which the application talks to the invoicing provider (Visa, via CyberSource). The
/// application layer depends only on this abstraction; the concrete SDK integration lives in Infrastructure.
/// Every method translates a provider failure into an <see cref="InvoicingProviderException"/> so the
/// application has a single failure type to reason about.
/// </summary>
public interface IInvoicingProvider
{
    /// <summary>Raise a new bill. The bill starts out not yet put to the shopper.</summary>
    Task<ProviderInvoice> RaiseAsync(RaiseInvoiceCommand command, CancellationToken cancellationToken = default);

    /// <summary>Read a bill's current state, the provider's record of how it got there, and its pay link.</summary>
    Task<ProviderInvoice> GetAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>Replace a draft bill's due date and customer details (amount re-hydrated from the order).</summary>
    Task<ProviderInvoice> UpdateAsync(string providerInvoiceId, UpdateInvoiceCommand command, CancellationToken cancellationToken = default);

    /// <summary>Deliver the bill to the customer so it becomes payable.</summary>
    Task<ProviderInvoice> IssueAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>Cancel the bill so it is no longer payable.</summary>
    Task<ProviderInvoice> WithdrawAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The provider's own record of every bill it reports as raised within the given range, filtered on the
    /// provider's created-date. Covers the whole range by paging the account-wide list.
    /// </summary>
    Task<IReadOnlyList<ProviderInvoiceSummary>> ListRaisedBetweenAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
