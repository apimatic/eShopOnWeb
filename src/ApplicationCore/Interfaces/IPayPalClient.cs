using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Card details used for a one-off payment or for vaulting. Full card data
/// passes through to PayPal only; it is never persisted or logged.
/// </summary>
public record PayPalCardDetails(
    string Number,
    int ExpiryMonth,
    int ExpiryYear,
    string? SecurityCode,
    string? CardholderName,
    PayPalBillingAddress? BillingAddress);

public record PayPalBillingAddress(
    string? AddressLine1,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode,
    string CountryCode);

public record PayPalAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset AuthorizedAt);

public record PayPalAuthorizationInfo(
    string AuthorizationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? CreateTime);

public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal FeeAmount,
    decimal NetAmount,
    string Currency);

public record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

public record PayPalVaultedCardResult(
    string VaultTokenId,
    string CustomerId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? CardholderName);

public record PayPalTransaction(
    string TransactionId,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? InitiationDate);

/// <summary>
/// Thin client over the PayPal REST APIs this integration uses
/// (Orders v2, Payments v2, Payment Method Tokens v3, Transaction Search v1).
/// </summary>
public interface IPayPalClient
{
    /// <summary>
    /// Creates a PayPal order with intent AUTHORIZE and authorizes it with the
    /// given card or vaulted card token.
    /// </summary>
    Task<PayPalAuthorizationResult> AuthorizeOrderAsync(
        string referenceId,
        string invoiceId,
        decimal amount,
        string currency,
        PayPalCardDetails? card,
        string? vaultTokenId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationInfo> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationInfo> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PayPalCaptureResult> GetCaptureAsync(
        string captureId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the capture id PayPal recorded for an order, used to recover local
    /// state when a capture succeeded but its response was lost.
    /// </summary>
    Task<string?> GetCapturedIdForOrderAsync(
        string payPalOrderId,
        CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refunds a capture. A null amount refunds the remaining captured amount in full.
    /// </summary>
    Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        decimal? amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PayPalVaultedCardResult> VaultCardAsync(
        PayPalCardDetails card,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task DeleteVaultedCardAsync(
        string vaultTokenId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists PayPal's own record of transactions over the whole range
    /// (all pages, chunked to PayPal's maximum range window).
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
