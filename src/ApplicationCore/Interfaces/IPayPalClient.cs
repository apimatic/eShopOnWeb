using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalClient
{
    Task<PayPalOrderResult> CreateOrderAsync(decimal amount, string currency, string invoiceId,
        string customId, string requestId, CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string orderId, PayPalCard? card,
        string? vaultId, string requestId, CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken);
    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken);
    Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken);
    Task<string> VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken);
    Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string currency,
        string requestId, string customId, string? note, CancellationToken cancellationToken);
    Task<PayPalRefundResult> GetRefundAsync(string refundId, CancellationToken cancellationToken);
    Task<PayPalSavedCardResult> SaveCardAsync(PayPalCard card, string requestId,
        CancellationToken cancellationToken);
    Task DeleteSavedCardAsync(string vaultId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record PayPalAddress(string AddressLine1, string? AddressLine2, string City,
    string State, string PostalCode, string CountryCode);

public sealed record PayPalCard(string Name, string Number, string Expiry, string SecurityCode,
    PayPalAddress BillingAddress);

public sealed record PayPalOrderResult(string Id, string Status);

public sealed record PayPalAuthorizationResult(string Id, string Status, decimal Amount,
    string Currency, DateTimeOffset? CreateTime, DateTimeOffset? ExpirationTime,
    bool RequiresPayerAction, string? PayPalOrderStatus);

public sealed record PayPalCaptureResult(string Id, string Status, decimal Amount,
    string Currency, decimal? PayPalFee, decimal? NetAmount, DateTimeOffset? CreateTime);

public sealed record PayPalRefundResult(string Id, string Status, decimal Amount,
    string Currency, DateTimeOffset? CreateTime, DateTimeOffset? UpdateTime);

public sealed record PayPalSavedCardResult(string Id, string Brand, string Last4,
    string Expiry, string? CardholderName);

public sealed record PayPalTransaction(string TransactionId, string? ReferenceId,
    string? ReferenceIdType, string? EventCode, DateTimeOffset? InitiatedAt,
    DateTimeOffset? UpdatedAt, decimal? Amount, string? Currency, decimal? Fee,
    string? Status, string? InvoiceId);

public sealed class PayPalApiException : Exception
{
    public PayPalApiException(HttpStatusCode statusCode, string name, string message,
        string? debugId, IReadOnlyList<string> issues)
        : base($"PayPal rejected the operation ({name}): {message}")
    {
        StatusCode = statusCode;
        ErrorName = name;
        DebugId = debugId;
        Issues = issues;
    }

    public HttpStatusCode StatusCode { get; }
    public string ErrorName { get; }
    public string? DebugId { get; }
    public IReadOnlyList<string> Issues { get; }
}
