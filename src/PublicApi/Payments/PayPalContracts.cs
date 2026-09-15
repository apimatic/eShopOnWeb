using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record CardInput(
    string Name,
    string Number,
    string Expiry,
    string SecurityCode,
    CardBillingAddress BillingAddress);

public sealed record CardBillingAddress(
    string CountryCode,
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea1,
    string? AdminArea2,
    string? PostalCode);

public sealed record ProviderAuthorization(
    string PayPalOrderId,
    string PayPalOrderStatus,
    string AuthorizationId,
    string AuthorizationStatus,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt);

public sealed record ProviderCapture(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    decimal? Fee,
    decimal? Net,
    DateTimeOffset? UpdatedAt);

public sealed record ProviderRefund(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? UpdatedAt);

public sealed record ProviderSavedCard(
    string TokenId,
    string CustomerId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? CardType);

public sealed record ProviderTransaction(
    string TransactionId,
    string? PayPalReferenceId,
    string? InvoiceId,
    string? CustomField,
    string? Status,
    string? EventCode,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    DateTimeOffset? InitiatedAt,
    DateTimeOffset? UpdatedAt);

public sealed class PayPalProviderException : Exception
{
    public PayPalProviderException(string message, int? statusCode = null, string? debugId = null,
        bool outcomeUnknown = false, Exception? innerException = null) : base(message, innerException)
    {
        StatusCode = statusCode;
        DebugId = debugId;
        OutcomeUnknown = outcomeUnknown;
    }

    public int? StatusCode { get; }
    public string? DebugId { get; }
    public bool OutcomeUnknown { get; }
}

public interface IPayPalGateway
{
    Task<ProviderAuthorization> AuthorizeAsync(int orderId, string operationId, decimal amount,
        string currency, CardInput? card, string? vaultId, CancellationToken cancellationToken);
    Task<ProviderAuthorization> GetAuthorizationAsync(string authorizationId, string paypalOrderId,
        CancellationToken cancellationToken);
    Task<ProviderAuthorization> ReauthorizeAsync(string authorizationId, string paypalOrderId,
        string requestId, decimal amount, string currency, CancellationToken cancellationToken);
    Task<ProviderCapture> CaptureAsync(string authorizationId, string requestId, int orderId,
        decimal amount, string currency, CancellationToken cancellationToken);
    Task<ProviderCapture> GetCaptureAsync(string captureId, CancellationToken cancellationToken);
    Task<string> VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken);
    Task<ProviderRefund> RefundAsync(string captureId, string idempotencyKey, decimal? amount,
        string currency, CancellationToken cancellationToken);
    Task<ProviderRefund> GetRefundAsync(string refundId, CancellationToken cancellationToken);
    Task<ProviderSavedCard> SaveCardAsync(string ownerId, string requestId, CardInput card,
        string? existingCustomerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderSavedCard>> ListCardsAsync(string customerId, CancellationToken cancellationToken);
    Task DeleteCardAsync(string tokenId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken);
}
