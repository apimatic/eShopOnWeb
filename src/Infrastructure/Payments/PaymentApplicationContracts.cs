using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed record CreateOrderLine(int CatalogItemId, int Quantity);
public sealed record ShippingAddressData(string Street, string City, string State, string Country, string ZipCode);
public sealed record OrderLineView(int CatalogItemId, string ProductName, decimal UnitPrice, int Quantity);
public sealed record RefundView(string RefundId, string Status, decimal Amount, DateTimeOffset? CompletedAt);
public sealed record PaymentView(
    string Status,
    string Currency,
    decimal AuthorizedAmount,
    string? PayPalOrderId,
    string? AuthorizationId,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal CapturedAmount,
    decimal PayPalFee,
    decimal NetAmount,
    decimal RefundedAmount,
    IReadOnlyList<RefundView> Refunds);
public sealed record OrderView(
    int OrderId,
    DateTimeOffset OrderDate,
    string Status,
    decimal Total,
    string Currency,
    IReadOnlyList<OrderLineView> Items,
    PaymentView? Payment);
public sealed record PaymentMethodView(int PaymentMethodId, string Brand, string Last4, string Expiry);
public sealed record RefundResultView(string RefundId, string Status, decimal Amount, decimal RemainingRefundable);
public sealed record ReconciliationPayPalRow(
    string TransactionId, string? ReferenceId, string EventCode, string Status,
    DateTimeOffset InitiatedAt, decimal Amount, string Currency, decimal? Fee,
    string? InvoiceId, int? OrderId, string MatchStatus);
public sealed record ReconciliationLocalRow(
    int OrderId, string Kind, string PayPalId, DateTimeOffset OccurredAt,
    decimal Amount, string Currency, string MatchStatus);
public sealed record ReconciliationView(
    DateTimeOffset From, DateTimeOffset To,
    IReadOnlyList<ReconciliationPayPalRow> PayPalTransactions,
    IReadOnlyList<ReconciliationLocalRow> LocalTransactions);

public interface IPaymentApplicationService
{
    Task<OrderView> CreateOrderAsync(string buyerId, IReadOnlyCollection<CreateOrderLine> items,
        ShippingAddressData shippingAddress, CancellationToken cancellationToken);
    Task<OrderView> PayAsync(string buyerId, int orderId, PaymentCardData? card,
        int? paymentMethodId, CancellationToken cancellationToken);
    Task<OrderView> FulfilAsync(int orderId, CancellationToken cancellationToken);
    Task<OrderView> CancelAsync(int orderId, CancellationToken cancellationToken);
    Task<RefundResultView> RefundAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderView>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken);
    Task<PaymentMethodView> SavePaymentMethodAsync(string buyerId, PaymentCardData card,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<PaymentMethodView>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken);
    Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken);
    Task<ReconciliationView> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken);
}

public sealed class PaymentWorkflowException : Exception
{
    public PaymentWorkflowException(int statusCode, string code, string message, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public string Code { get; }
}
