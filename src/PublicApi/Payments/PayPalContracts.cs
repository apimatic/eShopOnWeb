using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record CardDetails(
    string Name,
    string Number,
    string Expiry,
    string SecurityCode,
    BillingAddress BillingAddress);

public sealed record BillingAddress(
    string AddressLine1,
    string? AddressLine2,
    string City,
    string State,
    string PostalCode,
    string CountryCode);

public sealed record PayPalAuthorization(
    string OrderId,
    string AuthorizationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset CreateTime,
    DateTimeOffset? ExpirationTime);

public sealed record PayPalCapture(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    decimal Fee,
    decimal NetAmount);

public sealed record PayPalRefund(string Id, string Status, decimal Amount, string Currency);

public sealed record PayPalPaymentToken(
    string Id,
    string Brand,
    string Last4,
    string? Expiry);

public sealed record PayPalTransaction(
    string Id,
    string? ReferenceId,
    string? ReferenceIdType,
    string? InvoiceId,
    string? CustomId,
    string? EventCode,
    string? Status,
    decimal? Amount,
    decimal? Fee,
    string? Currency,
    DateTimeOffset? InitiatedAt,
    DateTimeOffset? UpdatedAt);

public interface IPayPalClient
{
    Task<PayPalAuthorization> AuthorizeAsync(int orderId, string paymentRequestId, decimal amount,
        string currency, CardDetails? card, string? vaultId, CancellationToken cancellationToken);
    Task<PayPalAuthorization> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken);
    Task<PayPalCapture> CaptureAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken);
    Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken);
    Task<PayPalRefund> RefundAsync(string captureId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken);
    Task<PayPalPaymentToken> CreatePaymentTokenAsync(string buyerId, CardDetails card,
        string requestId, CancellationToken cancellationToken);
    Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed class PayPalApiException : Exception
{
    public PayPalApiException(int statusCode, string name, string message, string? debugId,
        IReadOnlyList<string> details) : base(message)
    {
        StatusCode = statusCode;
        Name = name;
        DebugId = debugId;
        Details = details;
    }

    public int StatusCode { get; }
    public string Name { get; }
    public string? DebugId { get; }
    public IReadOnlyList<string> Details { get; }
}

public sealed class PayPalPayerActionRequiredException : Exception
{
    public PayPalPayerActionRequiredException(string message) : base(message) { }
}
