using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The abstraction over the payment processor (PayPal). It hides every SDK type from the rest of
/// the application: the domain and the application service talk only in the plain records above.
/// Implementations translate processor failures into <c>PaymentGatewayException</c> /
/// <c>PaymentChallengeRequiredException</c>.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>The three-letter ISO-4217 currency the processor is configured to charge in (from configuration).</summary>
    string CurrencyCode { get; }

    /// <summary>Place a hold for the order total, funded by a raw card or a saved card. Money is not taken.</summary>
    Task<GatewayAuthorization> AuthorizeAsync(GatewayAuthorizeRequest request, CancellationToken ct);

    /// <summary>Read the current state of an authorization (used to detect a stale hold before capture).</summary>
    Task<GatewayAuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken ct);

    /// <summary>Renew a stale authorization, returning the (possibly new) authorization to capture against.</summary>
    Task<GatewayAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode, string idempotencyKey, CancellationToken ct);

    /// <summary>Capture (take) the held funds. Returns the captured amount, PayPal's fee, and the net proceeds.</summary>
    Task<GatewayCapture> CaptureAsync(string authorizationId, decimal amount, string currencyCode, string idempotencyKey, CancellationToken ct);

    /// <summary>Void (release) an uncaptured authorization so no money ever moves.</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Refund a captured payment, in full (null amount) or in part.</summary>
    Task<GatewayRefund> RefundAsync(string captureId, decimal? amount, string currencyCode, string idempotencyKey, CancellationToken ct);

    /// <summary>Vault (save) a card, returning its token id and a safe descriptor.</summary>
    Task<GatewayVaultedCard> VaultCardAsync(GatewayVaultCardRequest request, CancellationToken ct);

    /// <summary>Remove a vaulted card so it can no longer be charged.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct);

    /// <summary>List PayPal's own record of transactions across the whole date range (all pages).</summary>
    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
