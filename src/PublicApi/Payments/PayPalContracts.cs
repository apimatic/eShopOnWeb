using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalOptions
{
    public const string SectionName = "PayPal";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
}

public sealed class PaymentApiException : Exception
{
    public PaymentApiException(HttpStatusCode statusCode, string safeMessage) : base(safeMessage)
        => StatusCode = statusCode;

    public HttpStatusCode StatusCode { get; }
}

public sealed class PayPalProviderException : Exception
{
    public PayPalProviderException(string safeMessage, Exception innerException,
        HttpStatusCode? providerStatus = null, string? debugId = null) : base(safeMessage, innerException)
    {
        ProviderStatus = providerStatus;
        DebugId = debugId;
    }

    public HttpStatusCode? ProviderStatus { get; }
    public string? DebugId { get; }
}

public sealed class PayPalChallengeRequiredException : Exception
{
    public PayPalChallengeRequiredException()
        : base("PayPal requires browser approval for this card. The headless payment flow has stopped.") { }
}

public sealed record CardInput(
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

public sealed record PayPalAuthorizationResult(
    string PayPalOrderId,
    string? OrderStatus,
    string AuthorizationId,
    string? AuthorizationStatus,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpirationTime,
    DateTimeOffset? CreateTime);

public sealed record PayPalCaptureResult(
    string CaptureId,
    string? Status,
    decimal Amount,
    string Currency,
    decimal? GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount);

public sealed record PayPalRefundResult(string RefundId, string? Status, decimal Amount, string Currency);

public sealed record PayPalSavedCardResult(
    string PaymentTokenId,
    string CustomerId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? Name);

public sealed record PayPalAuthorizationSnapshot(
    string AuthorizationId,
    string? Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpirationTime,
    DateTimeOffset? CreateTime);

public sealed record PayPalTransactionRecord(
    string? TransactionId,
    string? ReferenceId,
    string? ReferenceType,
    string? EventCode,
    DateTimeOffset? InitiationDate,
    DateTimeOffset? UpdatedDate,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    string? Status,
    string? InvoiceId,
    string? CustomField);

public sealed record PayPalTransactionReport(
    IReadOnlyList<PayPalTransactionRecord> Transactions,
    DateTimeOffset? LastRefreshedAt,
    int PagesRead);

public interface IPayPalGateway
{
    Task<PayPalAuthorizationResult> AuthorizeAsync(int orderId, decimal amount, string currency,
        string createRequestId, string authorizeRequestId, CardInput? card, string? vaultId,
        string? existingPayPalOrderId, CancellationToken ct);
    Task<PayPalAuthorizationSnapshot> GetAuthorizationAsync(string authorizationId, CancellationToken ct);
    Task<PayPalAuthorizationSnapshot> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken ct);
    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, int orderId, decimal amount,
        string currency, string requestId, CancellationToken ct);
    Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken ct);
    Task<string?> VoidAsync(string authorizationId, string requestId, CancellationToken ct);
    Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string currency,
        bool refundRemainingBalance, string idempotencyKey, int orderId, CancellationToken ct);
    Task<PayPalSavedCardResult> SaveCardAsync(string ownerCorrelation, string? customerId,
        CardInput card, string setupRequestId, string tokenRequestId, CancellationToken ct);
    Task<IReadOnlySet<string>> ListPaymentTokenIdsAsync(string customerId, CancellationToken ct);
    Task DeletePaymentTokenAsync(string tokenId, CancellationToken ct);
    Task<PayPalTransactionReport> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct);
}
