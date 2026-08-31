using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed class SensitiveCardDetails
{
    public string? Name { get; set; }
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }

    public void Clear()
    {
        Name = Number = Expiry = SecurityCode = AddressLine1 = AddressLine2 = null;
        City = State = PostalCode = CountryCode = null;
    }
}

public sealed record SavedCardResult(string TokenId, string? CustomerId, string? Name,
    string? Brand, string? LastDigits, string? Expiry, string? CardType,
    string? VerificationStatus);

public sealed record AuthorizationResult(string PayPalOrderId, string OrderStatus,
    string AuthorizationId, string AuthorizationStatus, decimal Amount, string Currency,
    DateTimeOffset? CreatedAt, DateTimeOffset? ExpiresAt, bool PayerActionRequired);

public sealed record AuthorizationSnapshot(string Id, string Status, decimal Amount,
    string Currency, DateTimeOffset? CreatedAt, DateTimeOffset? ExpiresAt,
    string? StatusReason);

public sealed record CaptureResult(string Id, string Status, decimal GrossAmount,
    string Currency, decimal? Fee, decimal? Net);

public sealed record RefundResult(string Id, string Status, decimal Amount, string Currency);

public sealed record ProviderTransaction(string? TransactionId, string? PayPalReferenceId,
    string? PayPalReferenceIdType, string? EventCode, DateTimeOffset? InitiatedAt,
    DateTimeOffset? UpdatedAt, decimal? Amount, string? Currency, decimal? Fee,
    string? Status, string? InvoiceId, string? CustomField);

public sealed record ProviderTransactionReport(IReadOnlyList<ProviderTransaction> Transactions,
    DateTimeOffset? LastRefreshedAt);

public interface IPaymentGateway
{
    Task<SavedCardResult> SaveCardAsync(string merchantCustomerId, SensitiveCardDetails card,
        string requestId, CancellationToken cancellationToken);
    Task<SavedCardResult> GetSavedCardAsync(string tokenId, CancellationToken cancellationToken);
    Task DeleteSavedCardAsync(string tokenId, CancellationToken cancellationToken);
    Task<AuthorizationResult> AuthorizeAsync(int orderId, decimal amount, string currency,
        SensitiveCardDetails? card, string? savedCardTokenId, string requestId,
        CancellationToken cancellationToken);
    Task<AuthorizationSnapshot> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken);
    Task<AuthorizationSnapshot> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken);
    Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken);
    Task<AuthorizationSnapshot> VoidAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken);
    Task<RefundResult> RefundAsync(string captureId, decimal amount, string currency,
        bool fullRemaining, string requestId, CancellationToken cancellationToken);
    Task<RefundResult> GetRefundAsync(string refundId, CancellationToken cancellationToken);
    Task<ProviderTransactionReport> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed class PaymentProviderException : Exception
{
    public PaymentProviderException(string operation, string safeMessage, HttpStatusCode? statusCode,
        Exception? innerException = null) : base(safeMessage, innerException)
    {
        Operation = operation;
        StatusCode = statusCode;
    }

    public string Operation { get; }
    public HttpStatusCode? StatusCode { get; }
}

public sealed class PaymentOperationException : Exception
{
    public PaymentOperationException(int statusCode, string code, string safeMessage)
        : base(safeMessage)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public string Code { get; }
}
