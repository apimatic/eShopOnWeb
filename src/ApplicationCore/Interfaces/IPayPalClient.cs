using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Low-level client for the PayPal REST APIs used by this integration:
/// Orders v2 (authorize), Payments v2 (capture/reauthorize/void/refund),
/// Payment Method Tokens v3 (vault) and Transaction Search v1 (reporting).
/// </summary>
public interface IPayPalClient
{
    /// <summary>Creates a PayPal order with intent AUTHORIZE. Returns the PayPal order id.</summary>
    Task<string> CreateOrderAsync(decimal amount, string currency, string customId, string requestId, CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> AuthorizeOrderWithCardAsync(string payPalOrderId, CardDetails card, string requestId, CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> AuthorizeOrderWithVaultAsync(string payPalOrderId, string vaultTokenId, string requestId, CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string requestId, CancellationToken cancellationToken = default);

    Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default);

    Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a capture. A null amount refunds the remaining captured amount in full.</summary>
    Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency, string requestId, CancellationToken cancellationToken = default);

    Task<PayPalSetupTokenResult> CreateSetupTokenAsync(CardDetails card, string requestId, CancellationToken cancellationToken = default);

    Task<PayPalVaultedCardResult> CreatePaymentTokenAsync(string setupTokenId, string requestId, CancellationToken cancellationToken = default);

    Task DeletePaymentTokenAsync(string vaultTokenId, CancellationToken cancellationToken = default);

    /// <summary>Lists all PayPal transactions in the range, following pagination to the last page.</summary>
    Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
