using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The port through which eShopOnWeb talks to the Visa invoicing provider. The
/// concrete adapter lives in Infrastructure and is the only place that knows about
/// the CyberSource SDK; the application core depends only on this abstraction.
///
/// Every method here maps to a single provider capability. A transition the
/// provider legitimately refuses (for example correcting a paid bill) surfaces as
/// a <see cref="Exceptions.VisaInvoiceProviderException"/> carrying the provider's
/// reason — it is an outcome of the bill's state, not a gap in the integration.
/// </summary>
public interface IVisaInvoiceGateway
{
    /// <summary>
    /// The currency the provider account bills in. It is a property of the account,
    /// not chosen per call, so the application stamps it onto every bill it raises.
    /// </summary>
    string AccountCurrency { get; }

    /// <summary>Raises a bill with the provider in a not-yet-issued (draft) state.</summary>
    Task<ProviderInvoiceState> CreateDraftAsync(ProviderInvoiceDraft draft, CancellationToken cancellationToken = default);

    /// <summary>Reads the provider's current record of a bill, including how it can be paid.</summary>
    Task<ProviderInvoiceState> GetAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Corrects a bill at the provider. The provider requires the full billed detail
    /// on every update, so the caller re-sends the amount unchanged from the order.
    /// </summary>
    Task<ProviderInvoiceState> UpdateAsync(string providerInvoiceId, ProviderInvoiceDraft draft, CancellationToken cancellationToken = default);

    /// <summary>Puts the bill to the shopper (sends it), after which a payment link is available.</summary>
    Task<ProviderInvoiceState> SendAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>Withdraws (cancels) the bill so it is no longer payable.</summary>
    Task<ProviderInvoiceState> CancelAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the provider's own record of every bill it raised whose creation time
    /// falls within the range, across the whole account (including bills that are not
    /// this application's). Used to build the operator reconciliation report.
    /// </summary>
    Task<IReadOnlyList<ProviderInvoiceState>> ListRaisedBetweenAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
