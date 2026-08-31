using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the payment provider (PayPal). Implementations must never
/// persist or log full card details.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>
    /// Creates a provider order with intent=AUTHORIZE and authorizes it with the given
    /// payment source (one-off card or vaulted card). Places a hold; no money moves.
    /// </summary>
    Task<AuthorizationResult> AuthorizeOrderAsync(int orderId, decimal amount, string currency,
        PaymentSourceDto paymentSource, string requestId, CancellationToken cancellationToken = default);

    Task<AuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Captures (settles) an authorization. This is when money actually moves.</summary>
    Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken = default);

    /// <summary>Renews a stale authorization hold.</summary>
    Task<AuthorizationDetails> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken = default);

    /// <summary>Releases a hold without charging.</summary>
    Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a capture, fully (amount == null) or partially.</summary>
    Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string requestId, string? noteToPayer, CancellationToken cancellationToken = default);

    /// <summary>Vaults a card and returns the durable token plus safe display data.</summary>
    Task<VaultedCardResult> VaultCardAsync(CardDetails card, string customerId, string requestId,
        CancellationToken cancellationToken = default);

    Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default);

    /// <summary>Lists the provider's own record of transactions for a range, paging through the whole range.</summary>
    Task<IReadOnlyList<ProviderTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
