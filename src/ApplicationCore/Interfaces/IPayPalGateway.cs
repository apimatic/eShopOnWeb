using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalGateway
{
    Task<PayPalOrderResult> CreateOrderAsync(string orderReference, decimal amount, string currency,
        CancellationToken cancellationToken);
    Task<PayPalOrderResult> GetOrderAsync(string payPalOrderId, CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string orderReference, string payPalOrderId,
        CardPaymentSource? card, string? vaultId, CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string orderReference, string authorizationId,
        decimal amount, string currency, CancellationToken cancellationToken);
    Task<PayPalCaptureResult> CaptureAsync(string orderReference, string authorizationId, decimal amount,
        string currency, CancellationToken cancellationToken);
    Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken);
    Task VoidAsync(string orderReference, string authorizationId, CancellationToken cancellationToken);
    Task<PayPalRefundResult> RefundAsync(string orderReference, string captureId, string idempotencyKey,
        decimal amount, string currency, string? note, CancellationToken cancellationToken);
    Task<SavedCardResult> SaveCardAsync(string merchantCustomerId, CardPaymentSource card,
        CancellationToken cancellationToken);
    Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PayPalTransactionResult>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record CardPaymentSource(
    string Name,
    string Number,
    string Expiry,
    string SecurityCode,
    CardBillingAddress BillingAddress);

public sealed record CardBillingAddress(
    string AddressLine1,
    string? AddressLine2,
    string City,
    string State,
    string PostalCode,
    string CountryCode);

public sealed record PayPalOrderResult(
    string Id,
    string Status,
    PayPalAuthorizationResult? Authorization,
    PayPalCaptureResult? Capture);

public sealed record PayPalAuthorizationResult(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ExpiresAt,
    string OrderStatus);

public sealed record PayPalCaptureResult(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    decimal? PayPalFee,
    decimal? NetAmount,
    DateTimeOffset? CreatedAt);

public sealed record PayPalRefundResult(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    decimal? PayPalFee,
    decimal? NetAmount,
    DateTimeOffset? CreatedAt);

public sealed record SavedCardResult(
    string VaultId,
    string Brand,
    string LastDigits,
    string? Expiry);

public sealed record PayPalTransactionResult(
    string TransactionId,
    string? ReferenceId,
    string? ReferenceIdType,
    string? InvoiceId,
    string? CustomId,
    string? EventCode,
    string? Status,
    decimal? GrossAmount,
    decimal? FeeAmount,
    string? Currency,
    DateTimeOffset? InitiatedAt,
    DateTimeOffset? UpdatedAt);
