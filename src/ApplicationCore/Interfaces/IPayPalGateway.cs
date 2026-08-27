using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over PayPal's REST APIs (Orders v2, Payments v2, Payment Method Tokens v3,
/// Transaction Search v1). Implementations must never log full card details.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>Creates a PayPal order with intent AUTHORIZE and the given payment source.</summary>
    Task<PayPalOrderResult> CreateOrderAsync(decimal amount, string currency, string referenceId,
        PayPalPaymentSource paymentSource, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Authorizes (holds) the funds for a previously created PayPal order.</summary>
    Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string payPalOrderId,
        PayPalPaymentSource paymentSource, string requestId, CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Captures (takes) previously authorized funds.</summary>
    Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken = default);

    /// <summary>Renews a stale authorization, returning the replacement authorization.</summary>
    Task<PayPalAuthorizationDetails> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken = default);

    /// <summary>Voids an authorization, releasing the shopper's held funds.</summary>
    Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a capture, in full or in part. idempotencyKey prevents duplicate refunds.</summary>
    Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Creates a vault setup token carrying the full card details (never persisted locally).</summary>
    Task<PayPalSetupTokenResult> CreateSetupTokenAsync(PayPalCardDetails card, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Exchanges a setup token for a permanent vaulted payment token.</summary>
    Task<PayPalPaymentTokenResult> CreatePaymentTokenAsync(string setupTokenId, string requestId, CancellationToken cancellationToken = default);

    Task DeletePaymentTokenAsync(string vaultTokenId, CancellationToken cancellationToken = default);

    /// <summary>Lists PayPal's own record of transactions over the whole range (all pages).</summary>
    Task<IReadOnlyList<PayPalTransactionRecord>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public sealed record PayPalCardDetails(
    string Number,
    string Expiry, // YYYY-MM
    string? SecurityCode,
    string? CardholderName,
    string? BillingAddressLine1,
    string? BillingCity,
    string? BillingState,
    string? BillingPostalCode,
    string? BillingCountryCode);

/// <summary>A payment source for a PayPal order: either one-off card details or a vaulted card.</summary>
public sealed record PayPalPaymentSource(PayPalCardDetails? Card, string? VaultTokenId)
{
    public static PayPalPaymentSource ForCard(PayPalCardDetails card) => new(card, null);
    public static PayPalPaymentSource ForVaultedCard(string vaultTokenId) => new(null, vaultTokenId);
}

/// <summary>
/// Result of creating a PayPal order. When a card payment source is supplied at creation,
/// PayPal authorizes immediately and <see cref="Authorization"/> is populated.
/// </summary>
public sealed record PayPalOrderResult(string Id, string Status, string? PayerActionUrl,
    PayPalAuthorizationResult? Authorization);

public sealed record PayPalAuthorizationResult(
    string OrderId,
    string OrderStatus,
    string AuthorizationId,
    string AuthorizationStatus,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpirationTime,
    string? PayerActionUrl);

public sealed record PayPalAuthorizationDetails(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpirationTime);

public sealed record PayPalCaptureResult(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    decimal? PayPalFee,
    decimal? NetAmount);

public sealed record PayPalRefundResult(string Id, string Status, decimal Amount, string Currency);

public sealed record PayPalSetupTokenResult(string Id, string Status, string? CustomerId, string? ApproveUrl);

public sealed record PayPalPaymentTokenResult(
    string Id,
    string? CustomerId,
    string? Brand,
    string? LastDigits,
    string? ExpiryMonth,
    string? ExpiryYear,
    string? CardholderName);

public sealed record PayPalTransactionRecord(
    string TransactionId,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? Currency,
    decimal? FeeAmount,
    DateTimeOffset? InitiationDate,
    DateTimeOffset? UpdatedDate);
