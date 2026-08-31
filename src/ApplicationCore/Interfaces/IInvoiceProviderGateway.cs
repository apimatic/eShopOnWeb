using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A thin gateway over the Visa/CyberSource invoicing API. Implementations translate the SDK's wire
/// model to and from the domain and surface a single failure type
/// (<see cref="Exceptions.InvoiceProviderException"/>). This is the only seam through which eShop talks
/// to the provider.
/// </summary>
public interface IInvoiceProviderGateway
{
    /// <summary>Raise a bill that is NOT yet put to the shopper (a draft).</summary>
    Task<InvoiceReceipt> RaiseAsync(NewInvoiceRequest request, CancellationToken cancellationToken);

    /// <summary>Read the provider's live view of a bill — status, how it got there, and any pay link.</summary>
    Task<InvoiceState> GetAsync(string providerInvoiceId, CancellationToken cancellationToken);

    /// <summary>Correct a still-draft bill (full replace at the provider).</summary>
    Task CorrectAsync(InvoiceCorrection correction, CancellationToken cancellationToken);

    /// <summary>Put the bill to the shopper (deliver). Returns the resulting state, including any pay link.</summary>
    Task<InvoiceState> IssueAsync(string providerInvoiceId, CancellationToken cancellationToken);

    /// <summary>Take the bill back (cancel) so it is no longer payable.</summary>
    Task WithdrawAsync(string providerInvoiceId, CancellationToken cancellationToken);

    /// <summary>
    /// List the provider's record of bills on the account. The provider's list projection carries no
    /// creation date and offers no date filter, so this returns the whole account (paged); the caller
    /// lines it up against eShop's own date-bounded records to reconcile.
    /// </summary>
    Task<IReadOnlyList<ProviderInvoiceRecord>> ListAllAsync(CancellationToken cancellationToken);
}
