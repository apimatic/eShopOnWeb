using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the payment processor (PayPal). Keeps ApplicationCore free of any PayPal SDK
/// dependency - the concrete implementation lives in Infrastructure.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Authorizes (holds) funds against a one-off card. Does not capture.</summary>
    Task<PaymentAuthorizationResult> AuthorizeWithCardAsync(string requestId, decimal amount, string currencyCode, CardDetails card, CancellationToken ct = default);

    /// <summary>Authorizes (holds) funds against a previously vaulted card. Does not capture.</summary>
    Task<PaymentAuthorizationResult> AuthorizeWithVaultedCardAsync(string requestId, decimal amount, string currencyCode, string vaultId, CancellationToken ct = default);

    /// <summary>Captures (actually takes) a previously authorized payment, in full.</summary>
    Task<PaymentCaptureResult> CapturePaymentAsync(string requestId, string authorizationId, CancellationToken ct = default);

    /// <summary>Renews an authorization that has expired or is about to, so it can still be captured.</summary>
    Task<PaymentAuthorizationResult> ReauthorizePaymentAsync(string requestId, string authorizationId, decimal amount, string currencyCode, CancellationToken ct = default);

    /// <summary>Releases a hold without ever capturing it.</summary>
    Task VoidPaymentAsync(string requestId, string authorizationId, CancellationToken ct = default);

    /// <summary>Refunds a previously captured payment, in full (amount = null) or in part.</summary>
    Task<PaymentRefundResult> RefundCaptureAsync(string requestId, string captureId, decimal? amount, string? currencyCode, CancellationToken ct = default);

    /// <summary>Saves a card in PayPal's vault for later reuse. Full card details are never returned/stored by the caller.</summary>
    Task<VaultedCardResult> VaultCardAsync(string requestId, string merchantCustomerId, CardDetails card, CancellationToken ct = default);

    /// <summary>Deletes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default);

    /// <summary>Lists PayPal's own transaction records for a date range, walking every page.</summary>
    Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

public record CardDetails(
    string Number,
    /// <summary>"YYYY-MM"</summary>
    string ExpiryYearMonth,
    string? SecurityCode,
    string? CardholderName,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    string CountryCode);

public record PaymentAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    bool RequiresBuyerAction);

public record PaymentCaptureResult(
    string CaptureId,
    string Status,
    decimal CapturedAmount,
    decimal? PayPalFeeAmount,
    decimal? NetAmount,
    string CurrencyCode);

public record PaymentRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string CurrencyCode);

public record VaultedCardResult(
    string VaultId,
    string CardBrand,
    string Last4,
    string Expiry);

public record PayPalTransactionRecord(
    string TransactionId,
    decimal? Amount,
    string? CurrencyCode,
    string? Status,
    DateTimeOffset? InitiatedDate,
    DateTimeOffset? UpdatedDate);
