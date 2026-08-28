using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed record CardBillingAddress(
    string AddressLine1,
    string? AddressLine2,
    string City,
    string? State,
    string PostalCode,
    string CountryCode);

public sealed record CardInput(
    string Name,
    string Number,
    string Expiry,
    string SecurityCode,
    CardBillingAddress BillingAddress);

public sealed record PlaceOrderItem(int CatalogItemId, int Quantity);

public sealed record ShippingAddressInput(
    string Street,
    string City,
    string State,
    string Country,
    string ZipCode);

public sealed record OrderView(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    string Currency,
    OrderPaymentStatus PaymentStatus,
    OrderFulfilmentStatus FulfilmentStatus,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    decimal? AuthorizedAmount,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    decimal RefundedAmount,
    IReadOnlyList<RefundView> Refunds);

public sealed record RefundView(
    int RefundId,
    string IdempotencyKey,
    string? PayPalRefundId,
    string Status,
    decimal Amount,
    string Currency,
    string? StatusReason);

public sealed record SavedPaymentMethodView(
    int PaymentMethodId,
    string Brand,
    string LastDigits,
    string Expiry,
    string? CardholderName,
    string? CardType);

public sealed record AuthorizeOrderInput(CardInput? Card, int? PaymentMethodId);

public sealed record ReconciliationTransaction(
    string? TransactionId,
    string? PayPalReferenceId,
    string? EventCode,
    DateTimeOffset? InitiationDate,
    DateTimeOffset? UpdatedDate,
    decimal? Amount,
    decimal? Fee,
    string? Currency,
    string? Status,
    string? InvoiceId,
    string? CustomField);

public sealed record ReconciliationEntry(
    string MatchStatus,
    int? OrderId,
    string? LocalType,
    string? LocalPayPalId,
    decimal? LocalAmount,
    ReconciliationTransaction? PayPalTransaction);

public interface IPaymentService
{
    Task<OrderView> PlaceOrderAsync(string buyerId, IReadOnlyList<PlaceOrderItem> items,
        ShippingAddressInput? shippingAddress, CancellationToken cancellationToken);
    Task<OrderView> AuthorizeAsync(int orderId, string buyerId, AuthorizeOrderInput input,
        CancellationToken cancellationToken);
    Task<OrderView> FulfilAsync(int orderId, CancellationToken cancellationToken);
    Task<OrderView> CancelAsync(int orderId, CancellationToken cancellationToken);
    Task<RefundView> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderView>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken);
    Task<SavedPaymentMethodView> SavePaymentMethodAsync(string buyerId, CardInput card,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<SavedPaymentMethodView>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken);
    Task DeletePaymentMethodAsync(int paymentMethodId, string buyerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReconciliationEntry>> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken);
}

public sealed record GatewayCreateOrderRequest(
    int OrderId,
    decimal Amount,
    string Currency,
    string IdempotencyKey);

public sealed record GatewayOrder(string PayPalOrderId, string Status);

public sealed record GatewayAuthorizeRequest(
    int OrderId,
    string PayPalOrderId,
    decimal Amount,
    string Currency,
    CardInput? Card,
    string? VaultPaymentTokenId,
    string IdempotencyKey);

public sealed record GatewayAuthorization(
    string PayPalOrderId,
    string PayPalOrderStatus,
    string AuthorizationId,
    string AuthorizationStatus,
    string? StatusReason,
    decimal Amount,
    string Currency,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ExpiresAt);

public sealed record GatewayCapture(
    string CaptureId,
    string Status,
    string? StatusReason,
    decimal Amount,
    string Currency,
    decimal? PayPalFee,
    decimal? NetAmount,
    DateTimeOffset? CapturedAt);

public sealed record GatewayRefund(
    string RefundId,
    string Status,
    string? StatusReason,
    decimal Amount,
    string Currency);

public sealed record GatewaySavedCard(
    string PaymentTokenId,
    string CustomerId,
    string Brand,
    string LastDigits,
    string Expiry,
    string? CardholderName,
    string? CardType);

public interface IPayPalGateway
{
    string Currency { get; }
    Task<GatewayOrder> CreateOrderAsync(GatewayCreateOrderRequest request, CancellationToken cancellationToken);
    Task<GatewayAuthorization> AuthorizeAsync(GatewayAuthorizeRequest request, CancellationToken cancellationToken);
    Task<GatewayAuthorization> GetAuthorizationAsync(string payPalOrderId, string authorizationId,
        CancellationToken cancellationToken);
    Task<GatewayAuthorization> ReauthorizeAsync(string payPalOrderId, string authorizationId,
        decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken);
    Task<GatewayCapture> CaptureAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey,
        CancellationToken cancellationToken);
    Task<GatewayCapture> GetCaptureAsync(string captureId, CancellationToken cancellationToken);
    Task<string> VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken);
    Task<GatewayRefund> RefundAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken);
    Task<GatewaySavedCard> SaveCardAsync(string buyerId, CardInput card, string operationId,
        CancellationToken cancellationToken);
    Task DeleteCardAsync(string paymentTokenId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReconciliationTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed class PaymentOperationException : Exception
{
    public PaymentOperationException(string code, string message, int statusCode = 409,
        string? providerDebugId = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        StatusCode = statusCode;
        ProviderDebugId = providerDebugId;
    }

    public string Code { get; }
    public int StatusCode { get; }
    public string? ProviderDebugId { get; }
}
