using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the PayPal REST APIs used by this integration:
/// Orders v2 (authorize), Payments v2 (capture/reauthorize/void/refund),
/// Vault v3 (payment method tokens) and Transaction Search v1 (reporting).
/// </summary>
public interface IPayPalClient
{
    Task<PayPalAuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency, PayPalCardDetails card, string referenceId, string requestId, CancellationToken cancellationToken = default);
    Task<PayPalAuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string currency, string vaultTokenId, string referenceId, string requestId, CancellationToken cancellationToken = default);
    Task<PayPalAuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);
    Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency, string requestId, CancellationToken cancellationToken = default);
    Task<PayPalAuthorizationState> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string requestId, CancellationToken cancellationToken = default);
    Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default);
    Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal amount, string currency, string requestId, CancellationToken cancellationToken = default);
    Task<PayPalSetupTokenResult> CreateSetupTokenAsync(PayPalCardDetails card, string requestId, CancellationToken cancellationToken = default);
    Task<PayPalPaymentTokenResult> CreatePaymentTokenAsync(string setupTokenId, string requestId, CancellationToken cancellationToken = default);
    Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PayPalTransactionInfo>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public record PayPalAddress(string? AddressLine1, string? City, string? State, string? PostalCode, string CountryCode);

/// <summary>
/// Full card details used only in transit to PayPal. Never persisted, never logged.
/// </summary>
public record PayPalCardDetails(string Number, string Expiry, string? SecurityCode, string? Name, PayPalAddress? BillingAddress);

public record PayPalAuthorizationResult(string PayPalOrderId, string AuthorizationId, string Status, decimal Amount, string Currency, DateTimeOffset? ExpiresAt);

public record PayPalAuthorizationState(string AuthorizationId, string Status, decimal? Amount, string? Currency, DateTimeOffset? ExpiresAt);

public record PayPalCaptureResult(string CaptureId, string Status, decimal GrossAmount, string Currency, decimal? PayPalFee, decimal? NetAmount);

public record PayPalRefundResult(string RefundId, string Status, decimal Amount, string Currency);

public record PayPalSetupTokenResult(string SetupTokenId, string Status, string? CustomerId);

public record PayPalPaymentTokenResult(string PaymentTokenId, string? CustomerId, string? Brand, string? LastDigits, string? Expiry, string? CardholderName);

public record PayPalTransactionInfo(
    string TransactionId,
    string? ReferenceId,
    string? ReferenceIdType,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? Currency,
    decimal? FeeAmount,
    DateTimeOffset? InitiationDate,
    DateTimeOffset? UpdatedDate);
