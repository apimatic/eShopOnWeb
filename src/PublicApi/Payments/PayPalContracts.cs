using System;
using System.Collections.Generic;
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

public sealed record PaymentCard(
    string Number,
    string Expiry,
    string SecurityCode,
    string Name,
    CardBillingAddress BillingAddress);

public sealed record CardBillingAddress(
    string AddressLine1,
    string? AddressLine2,
    string City,
    string State,
    string PostalCode,
    string CountryCode);

public sealed record PayPalOrderResult(string Id, string Status);

public sealed record PayPalAuthorizationResult(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record PayPalCaptureResult(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    decimal Fee,
    decimal Net,
    DateTimeOffset CreatedAt);

public sealed record PayPalRefundResult(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt);

public sealed record PayPalVaultResult(
    string PaymentTokenId,
    string? CustomerId,
    string Brand,
    string LastDigits,
    string Expiry,
    string? CardholderName);

public sealed record PayPalTransaction(
    string TransactionId,
    string? ReferenceId,
    string? ReferenceIdType,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    DateTimeOffset? InitiatedAt,
    DateTimeOffset? UpdatedAt,
    string? InvoiceId);

public interface IPayPalGateway
{
    Task<PayPalOrderResult> CreateOrderAsync(int orderId, string paymentReference, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string payPalOrderId, PaymentCard? card,
        string? vaultId, string requestId, CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken);
    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, string paymentReference, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken);
    Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken);
    Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken);
    Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken);
    Task<PayPalVaultResult> SaveCardAsync(string buyerId, string? payPalCustomerId, PaymentCard card,
        string requestId, CancellationToken cancellationToken);
    Task DeletePaymentTokenAsync(string tokenId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed class PayPalApiException : Exception
{
    public PayPalApiException(int statusCode, string code, string message, string? debugId = null,
        bool payerActionRequired = false)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        DebugId = debugId;
        PayerActionRequired = payerActionRequired;
    }

    public int StatusCode { get; }
    public string Code { get; }
    public string? DebugId { get; }
    public bool PayerActionRequired { get; }
}
