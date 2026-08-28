using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed record PaymentCard(
    string Name,
    string Number,
    string Expiry,
    string SecurityCode,
    CardBillingAddress? BillingAddress);

public sealed record CardBillingAddress(
    string AddressLine1,
    string? AddressLine2,
    string City,
    string State,
    string PostalCode,
    string CountryCode);

public abstract record PayPalPaymentSource
{
    private PayPalPaymentSource() { }
    public sealed record OneOffCard(PaymentCard Card) : PayPalPaymentSource;
    public sealed record VaultedCard(string VaultId) : PayPalPaymentSource;
}

public sealed record PayPalAuthorization(
    string PayPalOrderId,
    string OrderStatus,
    string AuthorizationId,
    string Status,
    decimal Amount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt);

public sealed record PayPalCapture(
    string Id,
    string Status,
    decimal Amount,
    decimal? PayPalFee,
    decimal? NetAmount,
    DateTimeOffset CreatedAt);

public sealed record PayPalOrderState(PayPalAuthorization? Authorization, PayPalCapture? Capture);

public sealed record PayPalRefund(
    string Id,
    string Status,
    decimal Amount,
    DateTimeOffset CreatedAt);

public sealed record PayPalVaultedCard(
    string VaultId,
    string? CustomerId,
    string Brand,
    string LastDigits,
    string Expiry);

public sealed record PayPalTransaction(
    string TransactionId,
    string? ReferenceId,
    string? ReferenceIdType,
    string? InvoiceId,
    string? CustomField,
    string? EventCode,
    string? Status,
    decimal? Amount,
    decimal? Fee,
    string? Currency,
    DateTimeOffset? InitiatedAt,
    DateTimeOffset? UpdatedAt);

public interface IPayPalClient
{
    Task<PayPalAuthorization> AuthorizeAsync(decimal amount, string currency, Guid paymentReference,
        PayPalPaymentSource source, CancellationToken cancellationToken);
    Task<PayPalOrderState> GetOrderStateAsync(string payPalOrderId, CancellationToken cancellationToken);
    Task<PayPalAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        Guid paymentReference, CancellationToken cancellationToken);
    Task<PayPalCapture> CaptureAsync(string authorizationId, decimal amount, string currency,
        Guid paymentReference, CancellationToken cancellationToken);
    Task<string> VoidAsync(string authorizationId, Guid paymentReference, CancellationToken cancellationToken);
    Task<PayPalRefund> RefundAsync(string captureId, decimal amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken);
    Task<PayPalRefund> GetRefundAsync(string refundId, CancellationToken cancellationToken);
    Task<PayPalVaultedCard> SaveCardAsync(string buyerId, PaymentCard card, CancellationToken cancellationToken);
    Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed class PayPalApiException : Exception
{
    public PayPalApiException(HttpStatusCode statusCode, string errorName, string message,
        string? issue, string? debugId)
        : base(BuildMessage(statusCode, errorName, message, issue, debugId))
    {
        StatusCode = statusCode;
        ErrorName = errorName;
        Issue = issue;
        DebugId = debugId;
    }

    public HttpStatusCode StatusCode { get; }
    public string ErrorName { get; }
    public string? Issue { get; }
    public string? DebugId { get; }
    public bool RequiresPayerAction => ErrorName == "PAYER_ACTION_REQUIRED" || Issue == "PAYER_ACTION_REQUIRED";

    private static string BuildMessage(HttpStatusCode statusCode, string errorName, string message,
        string? issue, string? debugId)
    {
        var detail = string.IsNullOrWhiteSpace(issue) ? message : $"{issue}: {message}";
        var correlation = string.IsNullOrWhiteSpace(debugId) ? string.Empty : $" PayPal debug id: {debugId}.";
        return $"PayPal request failed ({(int)statusCode}, {errorName}): {detail}.{correlation}";
    }
}

public sealed class PayPalPayerActionRequiredException : Exception
{
    public PayPalPayerActionRequiredException(string message) : base(message) { }
}
