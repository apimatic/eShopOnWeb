using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the external invoicing provider (Visa, through its CyberSource platform).
/// Each method maps to one provider capability so that every action eShop can take stays
/// separately invocable. Implementations translate provider-specific errors into the
/// application's <see cref="Microsoft.eShopWeb.ApplicationCore.Exceptions"/> vocabulary.
/// </summary>
public interface IInvoiceProvider
{
    /// <summary>Raises a bill with the provider that is not yet put to the shopper (a draft).</summary>
    Task<ProviderInvoiceResult> CreateDraftAsync(ProviderInvoiceDraft draft, CancellationToken cancellationToken = default);

    /// <summary>Reads the provider's current view of a bill, including its payment link once payable.</summary>
    Task<ProviderInvoiceResult> GetAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>Corrects the details a draft bill carries at the provider.</summary>
    Task<ProviderInvoiceResult> UpdateAsync(string providerInvoiceId, ProviderInvoiceDraft draft, CancellationToken cancellationToken = default);

    /// <summary>Puts the bill to the shopper (publishes it), making it payable.</summary>
    Task<ProviderInvoiceResult> PublishAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>Withdraws the bill so that it is no longer payable.</summary>
    Task<ProviderInvoiceResult> CancelAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider account's own record of invoices whose creation date falls within
    /// the given range, inclusive. Covers the whole range regardless of provider paging.
    /// </summary>
    Task<IReadOnlyList<ProviderInvoiceSummary>> ListCreatedBetweenAsync(
        DateTimeOffset fromInclusive,
        DateTimeOffset toInclusive,
        CancellationToken cancellationToken = default);
}
