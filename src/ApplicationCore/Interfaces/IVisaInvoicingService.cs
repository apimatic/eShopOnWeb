using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Gateway to the invoicing provider (Visa, through its CyberSource platform). Every interaction with
/// the provider goes through this abstraction; the implementation lives in the Infrastructure layer so
/// the domain never depends on the provider SDK.
/// </summary>
public interface IVisaInvoicingService
{
    /// <summary>Raise a bill with the provider. The bill starts out as a draft, not yet put to the shopper.</summary>
    Task<VisaInvoiceState> RaiseInvoiceAsync(VisaInvoiceDraft draft, CancellationToken cancellationToken = default);

    /// <summary>Read a bill's current state from the provider, including its payment link once issued.</summary>
    Task<VisaInvoiceState> GetInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Correct the mutable details (due date, customer) of a draft bill. The billed amount and lines are
    /// re-supplied from the order so the amount cannot drift from what the order says.
    /// </summary>
    Task<VisaInvoiceState> UpdateInvoiceAsync(string providerInvoiceId, VisaInvoiceDraft draft, CancellationToken cancellationToken = default);

    /// <summary>Put the bill to the shopper (publish it). Afterwards it is payable and reports a payment link.</summary>
    Task<VisaInvoiceState> IssueInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>Withdraw a bill that should not be paid (cancel it). Afterwards it is no longer payable.</summary>
    Task<VisaInvoiceState> WithdrawInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The provider's own record of every bill it created in the given date range (across the whole
    /// range, paging as needed). Used to reconcile the provider ledger against eShop's records.
    /// </summary>
    Task<IReadOnlyList<VisaProviderInvoice>> ListInvoicesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
