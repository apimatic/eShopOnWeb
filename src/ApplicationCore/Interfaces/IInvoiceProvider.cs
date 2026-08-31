using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The port to the external payment/invoicing provider. eShop's application layer talks only to this
/// abstraction; the concrete adapter (in Infrastructure) is the sole place the provider SDK is used.
/// Every method is a single provider round-trip and translates provider/transport failures into the
/// application's own <see cref="Exceptions.InvoiceProviderException"/>.
/// </summary>
public interface IInvoiceProvider
{
    /// <summary>Raise a new bill, held in draft (not yet put to the shopper). Returns the provider's record.</summary>
    Task<ProviderInvoice> RaiseAsync(RaiseInvoiceCommand command, CancellationToken cancellationToken = default);

    /// <summary>Read a single bill's current provider state, including its status history.</summary>
    Task<ProviderInvoice> GetAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>Correct a draft bill's due date/customer details. The provider refuses this once the bill is non-draft.</summary>
    Task<ProviderInvoice> CorrectAsync(string providerInvoiceId, CorrectInvoiceCommand command, CancellationToken cancellationToken = default);

    /// <summary>Put the bill to the shopper. Afterwards a payment link is available.</summary>
    Task<ProviderInvoice> IssueAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>Withdraw the bill so it is no longer payable.</summary>
    Task<ProviderInvoice> WithdrawAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read one page of the provider's own list of bills. There is no server-side date filter, so the
    /// caller pages the whole set and filters by created-date itself.
    /// </summary>
    Task<ProviderInvoicePage> ListAsync(int offset, int limit, CancellationToken cancellationToken = default);
}
