using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over PayPal's REST APIs (Orders v2, Payments v2, Vault v3, Transaction
/// Search v1). Implemented in the Infrastructure layer against the live REST endpoints.
/// The application layer never sees PayPal's JSON shapes — only the neutral results.
/// </summary>
public interface IPayPalPaymentGateway
{
    /// <summary>The ISO-4217 currency all amounts are processed in (from configuration).</summary>
    string ConfiguredCurrency { get; }

    /// <summary>Authorize (place a hold for) the amount using raw card details.</summary>
    Task<AuthorizationResult> AuthorizeWithCardAsync(decimal amount, CardPaymentDetails card,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Authorize (place a hold for) the amount using a previously vaulted card.</summary>
    Task<AuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string vaultId,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Capture a previously created authorization (takes the money).</summary>
    Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Reauthorize a stale hold, producing a fresh authorization that can be captured.</summary>
    Task<ReauthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Void an authorization, releasing the held funds (no money moves).</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Refund a capture, in full or in part.</summary>
    Task<RefundResult> RefundCaptureAsync(string captureId, decimal amount,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Vault (save) a card for reuse, returning the vault token and safe descriptor.</summary>
    Task<VaultedCardResult> VaultCardAsync(CardPaymentDetails card, string? customerId,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Remove a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// List all PayPal transactions across the whole [from,to] range. The implementation
    /// windows the range into PayPal's 31-day maximum and follows every page.
    /// </summary>
    Task<IReadOnlyList<GatewayTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
