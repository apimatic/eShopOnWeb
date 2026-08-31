using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The boundary between eShop and the invoicing provider (Visa/CyberSource). Every provider interaction goes
/// through here; failures surface as <see cref="Exceptions.InvoiceProviderException"/>. Since the provider
/// cannot call back into this application, current provider state is always obtained by asking it (a read),
/// never received as a notification.
/// </summary>
public interface IInvoicingProvider
{
    /// <summary>Raise a bill with the provider, held as a draft (not yet put to the shopper).</summary>
    Task<ProviderInvoiceState> CreateDraftAsync(ProviderInvoiceDraft draft, CancellationToken cancellationToken);

    /// <summary>Read the provider's current record of a bill.</summary>
    Task<ProviderInvoiceState> GetAsync(string providerInvoiceId, CancellationToken cancellationToken);

    /// <summary>Correct a draft bill's due date and customer details with the provider.</summary>
    Task<ProviderInvoiceState> UpdateAsync(string providerInvoiceId, ProviderInvoiceUpdate update, CancellationToken cancellationToken);

    /// <summary>Put the bill to the shopper. Yields the payment link.</summary>
    Task<ProviderInvoiceState> SendAsync(string providerInvoiceId, CancellationToken cancellationToken);

    /// <summary>Withdraw the bill with the provider.</summary>
    Task<ProviderInvoiceState> CancelAsync(string providerInvoiceId, CancellationToken cancellationToken);

    /// <summary>
    /// The provider's own record of every bill it created in the given range — including bills raised by
    /// activity other than this application. The range is applied to the provider's creation date.
    /// </summary>
    Task<IReadOnlyList<ProviderInvoiceSummary>> ListCreatedBetweenAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
