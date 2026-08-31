using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the invoicing provider (Visa, through its CyberSource platform). The concrete
/// implementation lives in the Infrastructure layer; nothing above it depends on the provider SDK.
/// Every method talks to the provider — this application never receives call-backs from it, so all
/// current state has to be obtained by asking.
/// </summary>
public interface IInvoiceProvider
{
    /// <summary>Raise a bill with the provider. The bill starts out not yet put to the shopper.</summary>
    Task<ProviderInvoice> CreateInvoiceAsync(CreateProviderInvoiceRequest request, CancellationToken cancellationToken = default);

    /// <summary>Ask the provider for a bill's current state, how it got there, and how it can be paid.</summary>
    Task<ProviderInvoice> GetInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>Correct a bill that has not yet been put to the shopper.</summary>
    Task<ProviderInvoice> UpdateInvoiceAsync(string providerInvoiceId, UpdateProviderInvoiceRequest request, CancellationToken cancellationToken = default);

    /// <summary>Put the bill to the shopper so that a way to pay it can be handed out.</summary>
    Task<ProviderInvoice> IssueInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>Withdraw a bill that should not be paid.</summary>
    Task<ProviderInvoice> WithdrawInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The provider's own record of bills raised within the given range, each with its creation date
    /// resolved. The provider account carries bills that are not this application's; they are returned
    /// here too so the caller can tell which is which.
    /// </summary>
    Task<IReadOnlyList<ProviderInvoiceSummary>> ListInvoicesCreatedBetweenAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
