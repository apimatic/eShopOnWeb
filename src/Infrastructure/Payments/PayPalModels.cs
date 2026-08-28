using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed record PayPalAddress(string AddressLine1, string? AddressLine2, string City,
    string State, string PostalCode, string CountryCode);

public sealed record PayPalCard(string Name, string Number, string Expiry, string SecurityCode,
    PayPalAddress BillingAddress);

public sealed record PayPalLineItem(int CatalogItemId, string Name, decimal UnitPrice, int Quantity);

public sealed record PayPalAuthorizationResult(string OrderId, string OrderStatus, string AuthorizationId,
    string AuthorizationStatus, decimal Amount, string Currency, DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt);

public sealed record PayPalCaptureResult(string Id, string Status, decimal Amount, string Currency,
    decimal? Fee, decimal? NetAmount, DateTimeOffset CreatedAt);

public sealed record PayPalRefundResult(string Id, string Status, decimal Amount, string Currency,
    DateTimeOffset CreatedAt);

public sealed record PayPalVaultResult(string PaymentTokenId, string CustomerId, string Brand,
    string Last4, string Expiry);

public sealed record PayPalTransaction(string TransactionId, string? ReferenceId, string? ReferenceIdType,
    string? EventCode, DateTimeOffset? InitiatedAt, DateTimeOffset? UpdatedAt, decimal? Amount,
    string? Currency, decimal? Fee, string? Status, string? InvoiceId, string? CustomField);

public interface IPayPalClient
{
    Task<string> CreateOrderAsync(Guid externalId, decimal amount, string currency,
        IReadOnlyCollection<PayPalLineItem> items, string requestId, CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string paypalOrderId, PayPalCard? card,
        string? vaultId, string requestId, CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId, string paypalOrderId,
        CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, string paypalOrderId,
        decimal amount, string currency, string requestId, CancellationToken cancellationToken);
    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
        string invoiceId, string requestId, CancellationToken cancellationToken);
    Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken);
    Task<string> VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken);
    Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string currency,
        string requestId, string customId, CancellationToken cancellationToken);
    Task<PayPalVaultResult> SaveCardAsync(string ownerId, PayPalCard card, string requestId,
        CancellationToken cancellationToken);
    Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed class PayPalApiException : Exception
{
    public PayPalApiException(int statusCode, string name, string message, string? debugId,
        IReadOnlyCollection<string> issues)
        : base($"PayPal {name}: {message}" + (issues.Count > 0 ? $" ({string.Join(", ", issues)})" : string.Empty))
    {
        StatusCode = statusCode;
        ErrorName = name;
        DebugId = debugId;
        Issues = issues;
    }

    public int StatusCode { get; }
    public string ErrorName { get; }
    public string? DebugId { get; }
    public IReadOnlyCollection<string> Issues { get; }
}

public sealed class PayPalPayerActionRequiredException : Exception
{
    public PayPalPayerActionRequiredException(string operation)
        : base($"PayPal requires an interactive payer challenge during {operation}; this headless API does not support browser approval.") { }
}
