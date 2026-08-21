using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's view of PayPal. The concrete implementation lives in Infrastructure and is the
/// only code that talks to the PayPal SDK; everything above this interface is SDK-agnostic and works
/// with the plain result records in <see cref="PayPal"/>. Implementations translate every provider
/// failure into a <see cref="Exceptions.PaymentException"/> subtype.
/// </summary>
public interface IPayPalPaymentGateway
{
    /// <summary>
    /// Places a hold on the money (create order with intent AUTHORIZE, then authorize it) for the
    /// given amount. Pays with raw card details or a saved (vaulted) card. Does not take the money.
    /// </summary>
    Task<PayPalAuthorizationResult> AuthorizeAsync(PayPalAuthorizationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Takes the held money (captures the authorization) at fulfilment.</summary>
    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Renews a stale authorization so a later capture can still take the money.</summary>
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Releases a hold before fulfilment (voids the authorization); no money moves.</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Returns money after fulfilment (refunds the capture), in full or in part.</summary>
    Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Vaults a card so it can be reused for later orders; returns a safe descriptor and the vault id.</summary>
    Task<PayPalVaultResult> VaultCardAsync(PayPalCardDetails card, string customerId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Removes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists PayPal's own record of transactions over a date range, paging across the whole range,
    /// for reconciliation against eShop orders.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
