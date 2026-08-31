using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the PayPal REST APIs (Orders v2, Payments v2, Vault v3,
/// Transaction Search v1). Implementations must never log full card details.
/// </summary>
public interface IPayPalClient
{
    /// <summary>Creates a PayPal order with intent AUTHORIZE, paid either with
    /// raw card details or a vaulted card token.</summary>
    Task<PayPalOrderCreated> CreateOrderAsync(decimal amount, string currency, string referenceId,
        PayPalCardDetails? card, string? vaultTokenId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Authorizes (holds) the funds for a previously created PayPal order.</summary>
    Task<PayPalAuthorizationInfo> AuthorizeOrderAsync(string payPalOrderId, string requestId, CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationInfo> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Captures (takes) previously authorized funds.</summary>
    Task<PayPalCaptureInfo> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken = default);

    /// <summary>Shows captured payment details, including the seller receivable
    /// breakdown (gross, PayPal fee, net) which the capture response itself omits.</summary>
    Task<PayPalCaptureInfo> GetCaptureAsync(string captureId, CancellationToken cancellationToken = default);

    /// <summary>Renews an authorization whose honor period has lapsed.</summary>
    Task<PayPalAuthorizationInfo> ReauthorizeAuthorizationAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken = default);

    /// <summary>Voids an authorization, releasing the shopper's held funds.</summary>
    Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a captured payment, in part (amount set) or in full (amount null).</summary>
    Task<PayPalRefundInfo> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string requestId, CancellationToken cancellationToken = default);

    /// <summary>Creates a vault setup token for a card (first step of saving a card).</summary>
    Task<PayPalSetupTokenInfo> CreateSetupTokenAsync(PayPalCardDetails card, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Converts an approved setup token into a reusable payment token.</summary>
    Task<PayPalPaymentTokenInfo> CreatePaymentTokenAsync(string setupTokenId, string requestId, CancellationToken cancellationToken = default);

    Task DeletePaymentTokenAsync(string vaultTokenId, CancellationToken cancellationToken = default);

    /// <summary>Lists PayPal's own record of transactions over a date range (all pages).</summary>
    Task<IReadOnlyList<PayPalTransactionInfo>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
