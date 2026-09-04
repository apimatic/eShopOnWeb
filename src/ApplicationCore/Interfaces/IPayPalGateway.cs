using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record PayPalCardAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode,
    string CountryCode);

public sealed record PayPalCardDetails(
    string Name,
    string Number,
    string Expiry,
    string SecurityCode,
    PayPalCardAddress? BillingAddress);

public sealed record PayPalCreateOrderResult(
    string OrderId,
    string Status,
    string? AuthorizationId = null,
    string? AuthorizationStatus = null,
    DateTimeOffset? ExpirationTime = null);

public sealed record PayPalAuthorizeResult(
    string OrderId,
    string OrderStatus,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? ExpirationTime);

public sealed record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal? GrossAmount,
    decimal? FeeAmount,
    decimal? NetAmount,
    string? Currency);

public sealed record PayPalAuthorizationActionResult(string AuthorizationId, string Status, DateTimeOffset? ExpirationTime);

public sealed record PayPalRefundResult(string RefundId, string Status, decimal? Amount, string? Currency);

public sealed record PayPalPaymentTokenResult(string TokenId, string? Brand, string? Last4, string? Expiry);

public sealed record PayPalTransactionRecord(
    string? TransactionId,
    string? ReferenceId,
    string? EventCode,
    string? Status,
    DateTimeOffset? InitiationDate,
    decimal? Amount,
    decimal? Fee,
    string? Currency,
    string? PayerEmail,
    string? CustomField = null);

public interface IPayPalGateway
{
    Task<PayPalCreateOrderResult> CreateOrderAsync(int orderId, decimal amount, string currency, PayPalCardDetails? card, string? vaultId, string requestId, CancellationToken ct);

    Task<PayPalAuthorizeResult> AuthorizeOrderAsync(string payPalOrderId, PayPalCardDetails? card, string? vaultId, string requestId, CancellationToken ct);

    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, string requestId, CancellationToken ct);

    Task<PayPalAuthorizationActionResult> VoidAsync(string authorizationId, string requestId, CancellationToken ct);

    Task<PayPalAuthorizationActionResult> ReauthorizeAsync(string authorizationId, string requestId, CancellationToken ct);

    Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string currency, string requestId, CancellationToken ct);

    Task<PayPalPaymentTokenResult> CreatePaymentTokenAsync(PayPalCardDetails card, string requestId, string merchantCustomerId, CancellationToken ct);

    Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken ct);

    Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}