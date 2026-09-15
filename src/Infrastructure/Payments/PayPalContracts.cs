using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed record PayPalAuthorization(
    string PayPalOrderId,
    string OrderStatus,
    string AuthorizationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ExpiresAt);

public sealed record PayPalCapture(
    string CaptureId,
    string Status,
    decimal Amount,
    string Currency,
    decimal? Fee,
    decimal? Net,
    DateTimeOffset? CreatedAt);

public sealed record PayPalRefund(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? CreatedAt);

public sealed record PayPalSavedCard(
    string VaultId,
    string? CustomerId,
    string Brand,
    string LastDigits,
    string Expiry);

public sealed record PayPalTransaction(
    string TransactionId,
    string? EventCode,
    string? Status,
    DateTimeOffset? TransactionTime,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    string? InvoiceId);

public sealed record PayPalTransactionPage(
    IReadOnlyList<PayPalTransaction> Transactions,
    int Page,
    int TotalPages);

public interface IPayPalClient
{
    string Currency { get; }
    Task<string> CreateOrderAsync(decimal amount, string paymentReference, string requestId,
        CancellationToken cancellationToken);
    Task<PayPalAuthorization> AuthorizeOrderAsync(string payPalOrderId, CardInput? card,
        string? vaultId, string requestId, CancellationToken cancellationToken);
    Task<PayPalAuthorization> GetAuthorizationAsync(string authorizationId,
        string payPalOrderId, CancellationToken cancellationToken);
    Task<PayPalAuthorization> ReauthorizeAsync(string authorizationId, string payPalOrderId,
        decimal amount, string requestId, CancellationToken cancellationToken);
    Task<PayPalCapture> CaptureAsync(string authorizationId, decimal amount,
        string paymentReference, string requestId, CancellationToken cancellationToken);
    Task<PayPalCapture> GetCaptureAsync(string captureId, CancellationToken cancellationToken);
    Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken);
    Task<PayPalRefund> RefundAsync(string captureId, decimal amount, string paymentReference,
        string requestId, CancellationToken cancellationToken);
    Task<PayPalRefund> GetRefundAsync(string refundId, CancellationToken cancellationToken);
    Task<PayPalSavedCard> SaveCardAsync(string merchantCustomerId, CardInput card,
        string requestId, CancellationToken cancellationToken);
    Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken);
    Task<PayPalTransactionPage> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        int page, CancellationToken cancellationToken);
}
