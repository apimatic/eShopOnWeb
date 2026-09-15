using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// Abstraction over the PayPal REST APIs this integration uses (Checkout Orders v2, Payments v2,
/// Payment Method Tokens v3, Transaction Search v1). The concrete implementation lives in Infrastructure
/// and is built directly against the PayPal OpenAPI specs. All methods target the configured environment.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>
    /// Creates a PayPal order with intent=AUTHORIZE and a card (raw or vaulted) as the payment source, placing a
    /// hold for the exact amount. Returns the authorization the hold produced. If PayPal responds with a payer
    /// approval challenge, <see cref="AuthorizeResult.RequiresPayerAction"/> is true and no authorization is created.
    /// </summary>
    Task<AuthorizeResult> CreateAndAuthorizeOrderAsync(AuthorizeRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reads the current state of an authorization (status and expiry) so a stale hold can be detected.</summary>
    Task<AuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Renews a stale authorization, producing a fresh hold that can then be captured.</summary>
    Task<ReauthorizeResult> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Captures (takes the money for) an authorization. A null amount captures the full authorized amount.</summary>
    Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal? amount, string currencyCode, string requestId, string? invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Releases an authorization's held funds so no money moves.</summary>
    Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a capture, in full (null amount) or in part.</summary>
    Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currencyCode, string requestId, string? invoiceId, string? customId, CancellationToken cancellationToken = default);

    /// <summary>Vaults a card, returning the persistent vault token id plus safe display fields.</summary>
    Task<VaultCardResult> VaultCardAsync(CardDetails card, string? existingCustomerId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Deletes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns PayPal's own record of transactions across the whole [from,to] range (chunked into the API's
    /// supported windows and fully paged), for reconciliation against eShop orders.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
