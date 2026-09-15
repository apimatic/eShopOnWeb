using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the payment processor (PayPal). Lives in the core so application services
/// depend on the capability, not on the HTTP details, which are implemented in Infrastructure.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Places a hold on the money for the given amount. Does not take it.</summary>
    Task<AuthorizationResult> AuthorizeAsync(PaymentAuthorizationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reads the current state of an authorization (to decide if it is still usable).</summary>
    Task<AuthorizationSnapshot> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Renews a stale authorization, yielding a fresh authorization to capture against.</summary>
    Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, CancellationToken cancellationToken = default);

    /// <summary>Captures (takes) the held money.</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency, string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Releases a hold before capture, so no money moves.</summary>
    Task VoidAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a capture, in full (null amount) or in part.</summary>
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Vaults a card for later reuse, returning its token and a safe descriptor.</summary>
    Task<VaultCardResult> VaultCardAsync(CardDetails card, string? customerId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Removes a vaulted card so it can no longer be charged.</summary>
    Task DeleteVaultTokenAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>Lists PayPal's own transaction records across a date range (whole range, all pages).</summary>
    Task<IReadOnlyCollection<GatewayTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
