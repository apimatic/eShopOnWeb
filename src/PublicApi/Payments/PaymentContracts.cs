using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record BillingAddressRequest(
    string AddressLine1,
    string? AddressLine2,
    string City,
    string State,
    string PostalCode,
    string CountryCode);

public sealed record CardRequest(
    string Name,
    string Number,
    string Expiry,
    string SecurityCode,
    BillingAddressRequest BillingAddress);

public sealed record OrderLineRequest(int CatalogItemId, int Quantity);
public sealed record PlaceOrderRequest(IReadOnlyList<OrderLineRequest> Items,
    BillingAddressRequest? ShippingAddress = null);
public sealed record PayOrderRequest(CardRequest? Card, int? PaymentMethodId);
public sealed record SavePaymentMethodRequest(CardRequest Card);
public sealed record CreateRefundRequest(decimal? Amount, string IdempotencyKey);

public sealed record ProviderAuthorization(string PayPalOrderId, string AuthorizationId,
    string Status, decimal Amount, DateTimeOffset? Expiration);
public sealed record ProviderCapture(string CaptureId, string Status, decimal Amount,
    decimal? Fee, decimal? Net);
public sealed record ProviderRefund(string RefundId, string Status, decimal? Amount);
public sealed record ProviderAuthorizationState(string AuthorizationId, string Status,
    decimal Amount, DateTimeOffset? Expiration);
public sealed record ProviderVaultedCard(string SetupTokenId, string PaymentTokenId,
    string? CustomerId, string Status, string? Brand, string? LastDigits,
    string? Expiry, string? CardholderName);
public sealed record ProviderTransaction(string TransactionId, string? ReferenceId,
    string? ReferenceIdType, string? InvoiceId, string? CustomField, string? Status,
    string? EventCode, DateTimeOffset? InitiatedAt, decimal? Amount, decimal? Fee,
    string? Currency);

public sealed record OrderPaymentDto(int OrderId, DateTimeOffset OrderDate, string PaymentState,
    string Currency, decimal Total, string? PayPalOrderId, string? AuthorizationId,
    string? AuthorizationStatus, decimal? AuthorizedAmount, DateTimeOffset? AuthorizationExpiration,
    string? CaptureId, string? CaptureStatus, decimal? CapturedAmount, decimal? PayPalFee,
    decimal? NetProceeds, decimal RefundedAmount, DateTimeOffset? FulfilledAt,
    IReadOnlyList<OrderItemDto> Items);
public sealed record OrderItemDto(int CatalogItemId, string ProductName, decimal UnitPrice, int Quantity);
public sealed record PaymentMethodDto(int PaymentMethodId, string? Brand, string? LastDigits,
    string? Expiry, string? CardholderName);
public sealed record RefundDto(string RefundId, int OrderId, string Status, decimal RequestedAmount,
    decimal? RefundedAmount, string Currency);
public sealed record ReconciliationRecord(string Source, string RecordId, int? OrderId,
    string MatchStatus, string? Status, decimal? Amount, string? Currency, DateTimeOffset? Timestamp);
public sealed record ReconciliationResponse(DateTimeOffset From, DateTimeOffset To,
    bool ProviderReportEmpty, IReadOnlyList<ReconciliationRecord> Records);

public interface IPayPalGateway
{
    Task<string> CreateOrderAsync(int orderId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken);
    Task<ProviderAuthorization> AuthorizeOrderAsync(string payPalOrderId, decimal amount,
        CardRequest? card, string? vaultId, string requestId, CancellationToken cancellationToken);
    Task<ProviderAuthorization?> GetOrderAuthorizationAsync(string payPalOrderId,
        CancellationToken cancellationToken);
    Task<ProviderAuthorizationState> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken);
    Task<ProviderAuthorizationState> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken);
    Task<ProviderCapture> CaptureAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken);
    Task<ProviderCapture> GetCaptureAsync(string captureId, CancellationToken cancellationToken);
    Task<ProviderAuthorizationState> VoidAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken);
    Task<ProviderRefund> RefundAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken);
    Task<ProviderRefund> GetRefundAsync(string refundId, CancellationToken cancellationToken);
    Task<ProviderVaultedCard> SaveCardAsync(CardRequest card, string setupRequestId,
        string tokenRequestId, CancellationToken cancellationToken);
    Task DeletePaymentTokenAsync(string tokenId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed class PaymentApiException : Exception
{
    public PaymentApiException(int statusCode, string message, Exception? inner = null)
        : base(message, inner) => StatusCode = statusCode;
    public int StatusCode { get; }
}

public sealed class PayPalProviderException : Exception
{
    public PayPalProviderException(string message, Exception inner) : base(message, inner) { }
}
