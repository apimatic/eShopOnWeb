using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed record OrderLineInput(int CatalogItemId, int Quantity);
public sealed record ShippingAddressInput(string Street, string City, string State, string Country, string ZipCode);
public sealed record OrderView(int OrderId, DateTimeOffset OrderDate, string Status, string PaymentStatus,
    decimal Total, string? Currency, string? PayPalOrderId, string? AuthorizationId,
    string? AuthorizationStatus, DateTimeOffset? AuthorizationExpiresAt, string? CaptureId,
    string? CaptureStatus, decimal? CapturedAmount, decimal? PayPalFee, decimal? NetAmount,
    decimal RefundedAmount, IReadOnlyList<RefundView> Refunds);
public sealed record RefundView(int RefundId, string PayPalRefundId, string Status, decimal Amount,
    DateTimeOffset CreatedAt, string IdempotencyKey);
public sealed record PaymentMethodView(int PaymentMethodId, string Brand, string Last4, string Expiry,
    string? CardholderName);
public sealed record ReconciliationView(DateTimeOffset From, DateTimeOffset To,
    IReadOnlyList<ReconciliationLine> Lines);
public sealed record ReconciliationLine(string MatchStatus, int? OrderId, string Source,
    string TransactionId, string? ReferenceId, string? EventCode, string Status, decimal Amount,
    decimal Fee, string Currency, DateTimeOffset Timestamp);

public interface ICommercePaymentService
{
    Task<OrderView> CreateOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines,
        ShippingAddressInput address, CancellationToken cancellationToken);
    Task<OrderView> PayAsync(int orderId, string buyerId, CardDetails? card, int? paymentMethodId,
        CancellationToken cancellationToken);
    Task<OrderView> FulfilAsync(int orderId, CancellationToken cancellationToken);
    Task<OrderView> CancelAsync(int orderId, CancellationToken cancellationToken);
    Task<RefundView> RefundAsync(int orderId, string buyerId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderView>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken);
    Task<PaymentMethodView> SavePaymentMethodAsync(string buyerId, CardDetails card,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<PaymentMethodView>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken);
    Task DeletePaymentMethodAsync(int paymentMethodId, string buyerId, CancellationToken cancellationToken);
    Task<ReconciliationView> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken);
}

public enum CommerceErrorKind { Validation, NotFound, Forbidden, Conflict, Upstream }

public sealed class CommerceException : Exception
{
    public CommerceException(CommerceErrorKind kind, string code, string message) : base(message)
    {
        Kind = kind;
        Code = code;
    }
    public CommerceErrorKind Kind { get; }
    public string Code { get; }
}
