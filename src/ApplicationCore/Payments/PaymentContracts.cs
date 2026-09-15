using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed record OrderLineInput(int CatalogItemId, int Quantity);

public sealed record ShippingAddressInput(
    string Street,
    string City,
    string State,
    string Country,
    string ZipCode);

public sealed record BillingAddressInput(
    string AddressLine1,
    string? AddressLine2,
    string City,
    string State,
    string PostalCode,
    string CountryCode);

public sealed record CardInput(
    string Name,
    string Number,
    string Expiry,
    string SecurityCode,
    BillingAddressInput BillingAddress);

public sealed record PayOrderInput(CardInput? Card, int? PaymentMethodId);

public sealed record OrderItemView(
    int CatalogItemId,
    string ProductName,
    decimal UnitPrice,
    int Quantity);

public sealed record RefundView(
    string RefundId,
    string Status,
    decimal Amount,
    DateTimeOffset CreatedAt,
    string IdempotencyKey);

public sealed record PaymentView(
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

public sealed record OrderView(
    int OrderId,
    DateTimeOffset OrderDate,
    string Status,
    decimal Total,
    string Currency,
    IReadOnlyList<OrderItemView> Items,
    PaymentView? Payment);

public sealed record PaymentMethodView(
    int PaymentMethodId,
    string Brand,
    string LastDigits,
    string Expiry,
    DateTimeOffset CreatedAt);

public sealed record RefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    decimal RefundedAmount,
    decimal RefundableAmount);

public sealed record PayPalTransactionView(
    string TransactionId,
    string? EventCode,
    string? Status,
    DateTimeOffset? TransactionTime,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    string? InvoiceId,
    int? OrderId,
    string MatchStatus);

public sealed record LocalTransactionView(
    int OrderId,
    string Kind,
    string PayPalId,
    string? Status,
    DateTimeOffset? TransactionTime,
    decimal? Amount,
    string Currency);

public sealed record ReconciliationView(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<PayPalTransactionView> PayPalTransactions,
    IReadOnlyList<LocalTransactionView> LocalOnlyTransactions);

public interface IPaymentService
{
    Task<OrderView> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> items,
        ShippingAddressInput shippingAddress, CancellationToken cancellationToken);
    Task<OrderView> PayAsync(string buyerId, int orderId, PayOrderInput input,
        CancellationToken cancellationToken);
    Task<OrderView> FulfilAsync(int orderId, CancellationToken cancellationToken);
    Task<OrderView> CancelAsync(int orderId, CancellationToken cancellationToken);
    Task<RefundResult> RefundAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderView>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken);
    Task<PaymentMethodView> SavePaymentMethodAsync(string buyerId, CardInput card,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<PaymentMethodView>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken);
    Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken);
    Task<ReconciliationView> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken);
}
