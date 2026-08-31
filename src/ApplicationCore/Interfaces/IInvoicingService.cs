using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the external invoicing provider (Visa, through its CyberSource platform).
/// Every Visa interaction goes through this seam; the concrete implementation lives in
/// Infrastructure and is the only code that references the CyberSource SDK.
/// </summary>
public interface IInvoicingService
{
    /// <summary>Raise a new bill with the provider. The bill starts out in a draft state.</summary>
    Task<ProviderInvoiceSnapshot> CreateInvoiceAsync(NewInvoiceRequest request, CancellationToken cancellationToken = default);

    /// <summary>Read the current state of a bill, as the provider reports it.</summary>
    Task<ProviderInvoiceSnapshot> GetInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>Correct a bill that has not yet been put to the shopper.</summary>
    Task<ProviderInvoiceSnapshot> UpdateInvoiceAsync(string providerInvoiceId, InvoiceCorrection correction, CancellationToken cancellationToken = default);

    /// <summary>Put the bill to the shopper (send it), enabling a way to pay it.</summary>
    Task<ProviderInvoiceSnapshot> SendInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>Withdraw (cancel) a bill so it can no longer be paid.</summary>
    Task<ProviderInvoiceSnapshot> CancelInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// List every bill the provider itself recorded as created within the range. Covers the whole
    /// range (pages through the provider's feed). This includes bills that are not this
    /// application's, since the provider account is shared.
    /// </summary>
    Task<IReadOnlyList<ProviderInvoiceListItem>> ListInvoicesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
