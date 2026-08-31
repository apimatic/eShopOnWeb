using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public interface IPayPalClient
{
    string Currency { get; }

    Task<PayPalSavedCard> SaveCardAsync(CardDetails card, string? customerId, string requestId, CancellationToken cancellationToken);
    Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken);
    Task<PayPalAuthorization> AuthorizeAsync(int orderId, Guid paymentReference, decimal amount, CardDetails? card, string? vaultId, string requestId, CancellationToken cancellationToken);
    Task<PayPalAuthorization> ReauthorizeAsync(string authorizationId, string requestId, CancellationToken cancellationToken);
    Task<PayPalCapture> CaptureAsync(string authorizationId, decimal amount, string requestId, CancellationToken cancellationToken);
    Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken);
    Task<PayPalRefund> RefundAsync(string captureId, decimal amount, string requestId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record CardDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string Name,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string State,
    string PostalCode,
    string CountryCode);

public sealed record PayPalSavedCard(
    string PaymentTokenId,
    string? CustomerId,
    string Brand,
    string Last4,
    string Expiry,
    string? CardholderName);

public sealed record PayPalAuthorization(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt);

public sealed record PayPalCapture(
    string CaptureId,
    string Status,
    decimal Amount,
    string Currency,
    decimal PayPalFee,
    decimal NetProceeds,
    DateTimeOffset CreatedAt);

public sealed record PayPalRefund(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt);

public sealed record PayPalTransaction(
    string TransactionId,
    string? ReferenceId,
    string? ReferenceIdType,
    string EventCode,
    string Status,
    DateTimeOffset? InitiatedAt,
    DateTimeOffset? UpdatedAt,
    decimal Amount,
    string Currency,
    decimal? Fee,
    string? InvoiceId,
    string? CustomField);
