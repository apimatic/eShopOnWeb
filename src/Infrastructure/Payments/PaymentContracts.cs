using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed record BillingAddressData(
    string AddressLine1,
    string? AddressLine2,
    string City,
    string State,
    string PostalCode,
    string CountryCode);

public sealed record PaymentCardData(
    string Number,
    string Expiry,
    string SecurityCode,
    string Name,
    BillingAddressData BillingAddress);

public sealed record PayPalAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt);

public sealed record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal Amount,
    string Currency,
    decimal PayPalFee,
    decimal NetAmount,
    DateTimeOffset CreatedAt);

public sealed record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt);

public sealed record PayPalVaultResult(
    string VaultId,
    string? CustomerId,
    string Brand,
    string Last4,
    string Expiry);

public sealed record PayPalTransaction(
    string TransactionId,
    string? ReferenceId,
    string? ReferenceIdType,
    string EventCode,
    string Status,
    DateTimeOffset InitiatedAt,
    decimal Amount,
    string Currency,
    decimal? Fee,
    string? InvoiceId,
    string? CustomField);

public interface IPayPalGateway
{
    Task<PayPalAuthorizationResult> AuthorizeAsync(int orderId, decimal amount, string currency,
        PaymentCardData? card, string? vaultId, string requestId, CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken);
    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, int orderId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken);
    Task VoidAsync(string authorizationId, CancellationToken cancellationToken);
    Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken);
    Task<PayPalVaultResult> SaveCardAsync(PaymentCardData card, string merchantCustomerId,
        string? paypalCustomerId, string requestId, CancellationToken cancellationToken);
    Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken);
}

public class PayPalException : Exception
{
    public PayPalException(string message, int statusCode = 0, string? debugId = null)
        : base(message)
    {
        StatusCode = statusCode;
        DebugId = debugId;
    }

    public int StatusCode { get; }
    public string? DebugId { get; }
}

public sealed class PayPalPayerActionRequiredException : PayPalException
{
    public PayPalPayerActionRequiredException(string operation)
        : base($"PayPal requires browser approval for {operation}; this API supports headless direct-card flows only.", 422) { }
}
