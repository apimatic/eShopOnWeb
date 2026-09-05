using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Abstraction over the payment provider. All methods return classified results instead of
/// throwing; transport and provider failures are translated into <see cref="GatewayResult{T}"/>.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>The currency the gateway takes from configuration.</summary>
    string Currency { get; }

    /// <summary>Authorizes (holds) funds with a raw card. Does not capture.</summary>
    Task<GatewayResult<AuthorizeOutcome>> AuthorizeAsync(string requestId, decimal amount, string currency,
        CardInput card, string invoiceId, CancellationToken ct = default);

    /// <summary>Authorizes (holds) funds with a vaulted card token. Does not capture.</summary>
    Task<GatewayResult<AuthorizeOutcome>> AuthorizeWithVaultTokenAsync(string requestId, decimal amount,
        string currency, string vaultTokenId, string invoiceId, CancellationToken ct = default);

    /// <summary>Reauthorizes an existing (possibly stale) authorization for the same amount.</summary>
    Task<GatewayResult<ReauthorizeOutcome>> ReauthorizeAsync(string requestId, string authorizationId,
        decimal amount, string currency, CancellationToken ct = default);

    /// <summary>Fetches the current state of an authorization.</summary>
    Task<GatewayResult<AuthorizationInfo>> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default);

    /// <summary>Captures the funds held by an authorization (final capture).</summary>
    Task<GatewayResult<CaptureOutcome>> CaptureAsync(string requestId, string authorizationId, decimal amount,
        string currency, CancellationToken ct = default);

    /// <summary>Voids an authorization, releasing the hold without moving money.</summary>
    Task<GatewayResult<string>> VoidAsync(string requestId, string authorizationId, CancellationToken ct = default);

    /// <summary>
    /// Refunds a captured payment in full (when <paramref name="amount"/> is null) or in part.
    /// <paramref name="requestId"/> must be the caller-supplied idempotency key.
    /// </summary>
    Task<GatewayResult<RefundOutcome>> RefundAsync(string requestId, string captureId, decimal? amount,
        string currency, CancellationToken ct = default);

    /// <summary>Vaults a card and returns the token plus display data (never full card details).</summary>
    Task<GatewayResult<VaultOutcome>> VaultCardAsync(string buyerId, CardInput card, CancellationToken ct = default);

    /// <summary>Removes a vaulted card token so it can no longer be used to pay.</summary>
    Task<GatewayResult<bool>> DeleteVaultTokenAsync(string vaultTokenId, CancellationToken ct = default);

    /// <summary>Lists the provider's own transactions for a date range, across all pages.</summary>
    Task<GatewayResult<ReconciliationResult>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default);
}
