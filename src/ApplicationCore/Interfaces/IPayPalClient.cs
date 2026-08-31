using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Low-level client for the PayPal REST APIs (Orders v2, Payments v2,
/// Vault v3, Transaction Search v1). Implementations must never persist or
/// log full card details.
/// </summary>
public interface IPayPalClient
{
    /// <summary>Create an order with intent=AUTHORIZE. Returns the PayPal order id.</summary>
    Task<string> CreateOrderAsync(decimal amount, string currency, string referenceId, string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Authorize an order paying with raw card details.</summary>
    Task<PayPalAuthorizationInfo> AuthorizeOrderWithCardAsync(string payPalOrderId, CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Authorize an order paying with a vaulted card (payment token id).</summary>
    Task<PayPalAuthorizationInfo> AuthorizeOrderWithVaultedCardAsync(string payPalOrderId, string vaultTokenId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationInfo> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationInfo> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<PayPalCaptureInfo> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<PayPalRefundInfo> RefundCaptureAsync(string captureId, decimal? amount, string currency, string? noteToPayer, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Vault a card and return its payment token plus safe display metadata.</summary>
    Task<PayPalCardTokenInfo> CreatePaymentTokenAsync(CardDetails card, string merchantCustomerId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task DeletePaymentTokenAsync(string vaultTokenId, CancellationToken cancellationToken = default);

    /// <summary>List all transactions in the range, paging/chunking as needed.</summary>
    Task<IReadOnlyList<PayPalTransactionInfo>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
