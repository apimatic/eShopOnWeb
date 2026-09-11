using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>
/// A thin, typed wrapper over the PayPal REST APIs used by this integration. The configured
/// currency lives inside the implementation (from PayPal:Currency), so callers pass amounts only.
/// Implementations must never log full card details or credentials.
/// </summary>
public interface IPayPalClient
{
    /// <summary>Creates a PayPal order with intent AUTHORIZE and a card payment source, placing a
    /// hold on the money in a single step (no buyer browser approval).</summary>
    Task<AuthorizeOrderResult> CreateAuthorizedOrderAsync(AuthorizeOrderRequest request, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Captures (settles) an authorized payment, taking the money.</summary>
    Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Renews a hold that is nearing or past expiry so it can still be captured.</summary>
    Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, CancellationToken cancellationToken = default);

    /// <summary>Reads the current state of a hold (status and expiry).</summary>
    Task<AuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Releases a hold without taking any money.</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a capture, fully (amount null) or partially.</summary>
    Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Vaults a raw card (no approval step) and returns a durable token to charge later.</summary>
    Task<VaultCardResult> VaultCardAsync(PayPalCard card, string? customerId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Removes a vaulted card so it can no longer be charged.</summary>
    Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default);

    /// <summary>Returns every PayPal transaction in the date range, chunking the window and
    /// paging through all results so the whole range is covered.</summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
