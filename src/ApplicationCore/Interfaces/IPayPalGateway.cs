using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the PayPal REST APIs, built strictly against the OpenAPI specs in api-specs/.
/// The concrete implementation lives in Infrastructure; the domain never sees HTTP or JSON.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>
    /// Places a hold on the money: creates a PayPal order with intent AUTHORIZE using the supplied
    /// card or vaulted card and authorizes it. Throws <see cref="PayPalChallengeRequiredException"/>
    /// if the payment needs shopper approval in a browser.
    /// </summary>
    Task<GatewayAuthorizationResult> AuthorizeAsync(GatewayAuthorizeRequest request, CancellationToken ct = default);

    /// <summary>Renews a stale authorization before capture.</summary>
    Task<GatewayAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currencyCode, string requestId, CancellationToken ct = default);

    /// <summary>Captures (takes) the held funds. Reports captured amount, fee and net proceeds.</summary>
    Task<GatewayCaptureResult> CaptureAsync(string authorizationId, decimal amount,
        string currencyCode, string requestId, CancellationToken ct = default);

    /// <summary>Releases the hold before capture, so no money moves.</summary>
    Task VoidAsync(string authorizationId, CancellationToken ct = default);

    /// <summary>Refunds a capture, in full (null amount) or in part.</summary>
    Task<GatewayRefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode,
        string customId, string requestId, CancellationToken ct = default);

    /// <summary>Vaults a card for a customer, returning the token and a safe card description.</summary>
    Task<GatewayVaultCardResult> VaultCardAsync(string customerId, GatewayCardDetails card,
        string requestId, CancellationToken ct = default);

    /// <summary>Removes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string tokenId, CancellationToken ct = default);

    /// <summary>
    /// Returns PayPal's own record of transactions across the whole date range (chunked to the
    /// spec's max window and paginated through every page).
    /// </summary>
    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct = default);
}
