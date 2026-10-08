using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPaymentService
{
    Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyList<PlaceOrderLine> lines, Address? shipToAddress, CancellationToken cancellationToken = default);
    Task<PayOrderResult> PayOrderAsync(PayOrderCommand command, CancellationToken cancellationToken = default);
    Task<RefundOrderResult> RefundOrderAsync(RefundOrderCommand command, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderPaymentView>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
}

public sealed record PlaceOrderLine(int CatalogItemId, int Quantity);

public enum PlaceOrderOutcome { Created, Invalid }

public sealed record PlaceOrderResult(PlaceOrderOutcome Outcome, string Message, Order? Order = null);

public sealed record PayOrderCommand(int OrderId, string BuyerId, EncryptedCard Card, string ReturnUrl);

public enum PayOrderOutcome
{
    Paid,
    AlreadyPaid,
    OrderNotFound,
    PaymentInProgress,
    CannotCharge,
    Declined,
    InvalidCard,
    ProviderUnavailable,
    ProcessorTimeout,
    OutcomeUnknown
}

public sealed record PayOrderResult(PayOrderOutcome Outcome, string Message, Order? Order = null, PaymentAttempt? Attempt = null);

public sealed record RefundOrderCommand(int OrderId, decimal? Amount, string? Reason, string? IdempotencyKey, string RequestedBy);

public enum RefundOrderOutcome
{
    Submitted,
    AlreadySubmitted,
    OrderNotFound,
    OrderNotPaid,
    InvalidAmount,
    ExceedsRefundable,
    IdempotencyKeyReused,
    RefundInProgress,
    Rejected,
    ProviderUnavailable,
    ProcessorTimeout,
    OutcomeUnknown
}

public sealed record RefundOrderResult(RefundOrderOutcome Outcome, string Message, Order? Order = null, OrderRefund? Refund = null);

public sealed record OrderPaymentView(Order Order, IReadOnlyList<PaymentAttempt> Attempts, IReadOnlyList<OrderRefund> Refunds);
