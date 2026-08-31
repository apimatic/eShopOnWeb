using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's port to the Visa/CyberSource invoicing provider. Implementations translate provider
/// failures into <see cref="Microsoft.eShopWeb.ApplicationCore.Exceptions.InvoicingProviderException"/>
/// and never leak SDK types or secrets across this boundary.
/// </summary>
public interface IInvoicingService
{
    /// <summary>Raise a new bill with the provider, left as a draft (not yet put to the shopper).</summary>
    Task<ProviderInvoice> RaiseInvoiceAsync(RaiseInvoiceCommand command, CancellationToken ct = default);

    /// <summary>Read a bill's current provider-reported state, history and payment link.</summary>
    Task<ProviderInvoice> GetInvoiceAsync(string providerInvoiceId, CancellationToken ct = default);

    /// <summary>Correct the due date and customer details on a draft bill.</summary>
    Task<ProviderInvoice> AmendInvoiceAsync(string providerInvoiceId, AmendInvoiceCommand command, CancellationToken ct = default);

    /// <summary>Put the bill to the shopper (deliver it); afterwards a payment link is available.</summary>
    Task<ProviderInvoice> IssueInvoiceAsync(string providerInvoiceId, CancellationToken ct = default);

    /// <summary>Withdraw a bill so it is no longer payable.</summary>
    Task<ProviderInvoice> WithdrawInvoiceAsync(string providerInvoiceId, CancellationToken ct = default);

    /// <summary>
    /// List the provider's own record of bills created within the given range, for reconciliation. The
    /// provider has no server-side date filter, so the implementation pages the account and filters by
    /// each bill's created date. The account carries bills raised by other activity too — every bill the
    /// provider returns in range is included, whether or not it is this application's.
    /// </summary>
    Task<IReadOnlyList<ProviderInvoiceSummary>> ListInvoicesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
