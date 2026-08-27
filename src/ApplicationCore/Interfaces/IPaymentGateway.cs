using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Server-to-server payment provider operations. Implemented by the PayPal gateway in
/// Infrastructure; all PayPal SDK types stay behind this boundary.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Create a PayPal order (intent AUTHORIZE) and authorize it with a raw or vaulted card.</summary>
    Task<AuthorizationResult> AuthorizeOrderAsync(AuthorizePaymentCommand command, CancellationToken ct = default);

    Task<AuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default);

    /// <summary>Renew a stale authorization. Throws PaymentGatewayException when PayPal refuses.</summary>
    Task<AuthorizationState> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken ct = default);

    Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);

    Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Refund a capture. amount == null requests a full refund of the captured amount.</summary>
    Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct = default);

    Task<SavedCardResult> SaveCardAsync(SaveCardCommand command, CancellationToken ct = default);

    Task DeleteSavedCardAsync(string vaultTokenId, CancellationToken ct = default);

    /// <summary>All PayPal transactions in [from, to]; iterates every page and chunks ranges over 31 days.</summary>
    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
