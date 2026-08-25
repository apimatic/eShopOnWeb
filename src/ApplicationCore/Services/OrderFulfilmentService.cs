using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderFulfilmentService : IOrderFulfilmentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IAppLogger<OrderFulfilmentService> _logger;

    public OrderFulfilmentService(IRepository<Order> orderRepository, IPaymentGateway paymentGateway,
        IAppLogger<OrderFulfilmentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentGateway = paymentGateway;
        _logger = logger;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken ct = default)
    {
        var order = await LoadAsync(orderId, ct);

        if (order.Status == OrderStatus.Fulfilled)
            return order; // idempotent replay - money was already taken.

        if (order.Status != OrderStatus.PaymentAuthorized)
            throw new InvalidOrderStateException(orderId, order.Status, "fulfil");

        var payment = order.Payment!;
        var captureIdempotencyKey = $"fulfil-order-{order.PaymentIdempotencySeed:N}";

        GatewayCaptureResult capture;
        try
        {
            capture = await _paymentGateway.CaptureAsync(payment.AuthorizationId, payment.Amount, payment.Currency,
                finalCapture: true, captureIdempotencyKey, ct);
        }
        catch (PaymentGatewayException ex) when (LooksStale(ex))
        {
            _logger.LogWarning(
                "Authorization {0} for order {1} is stale ({2}); attempting a renewal before giving up.",
                payment.AuthorizationId, orderId, ex.Message);

            GatewayReauthorizationResult reauthorization;
            try
            {
                reauthorization = await _paymentGateway.ReauthorizeAsync(payment.AuthorizationId, payment.Amount,
                    payment.Currency, $"reauthorize-order-{order.PaymentIdempotencySeed:N}", ct);
            }
            catch (PaymentGatewayException reauthorizeEx)
            {
                throw new PaymentAuthorizationNotRenewableException(
                    $"The payment hold for order {orderId} has expired and PayPal could not renew it " +
                    $"({reauthorizeEx.Message}). Cancel this order and collect a new payment from the shopper.");
            }

            payment.RecordReauthorization(reauthorization.AuthorizationId, reauthorization.Status, reauthorization.ExpiresAt);
            await _orderRepository.UpdateAsync(order, ct);

            capture = await _paymentGateway.CaptureAsync(payment.AuthorizationId, payment.Amount, payment.Currency,
                finalCapture: true, captureIdempotencyKey, ct);
        }

        payment.RecordCapture(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PayPalFeeAmount,
            capture.NetAmount, capture.CapturedAt);
        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, ct);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken ct = default)
    {
        var order = await LoadAsync(orderId, ct);

        if (order.Status == OrderStatus.Cancelled)
            return order; // idempotent replay

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
            throw new InvalidOrderStateException(orderId, order.Status, "cancel");

        if (order.Payment is not null && order.Payment.AuthorizationStatus != "VOIDED")
        {
            await _paymentGateway.VoidAsync(order.Payment.AuthorizationId, $"cancel-order-{order.PaymentIdempotencySeed:N}", ct);
            order.Payment.RecordVoided(DateTimeOffset.UtcNow);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, ct);
        return order;
    }

    public async Task<(Order Order, OrderRefund Refund)> RefundAsync(int orderId, decimal? amount, string idempotencyKey,
        string? note, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await LoadAsync(orderId, ct);

        if (order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded))
            throw new InvalidOrderStateException(orderId, order.Status, "refund");

        var payment = order.Payment!;

        var existingRefund = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existingRefund is not null)
            return (order, existingRefund); // idempotent replay - do not refund twice for the same key.

        var remaining = payment.CapturedAmount!.Value - payment.ConsumedForRefund;
        var requestedAmount = amount ?? remaining;

        if (requestedAmount <= 0 || requestedAmount > remaining)
            throw new RefundAmountExceedsRemainingException(requestedAmount, remaining);

        // Namespace the caller's key with this order's random seed so it can never collide, at PayPal's
        // idempotency layer, with the same literal key reused by a different order or capture.
        var payPalIdempotencyKey = $"refund-{order.PaymentIdempotencySeed:N}-{idempotencyKey}";
        var gatewayResult = await _paymentGateway.RefundAsync(payment.CaptureId!, amount, payment.Currency, note,
            payPalIdempotencyKey, ct);

        var refund = new OrderRefund(payment.Id, idempotencyKey, gatewayResult.RefundId, gatewayResult.Status,
            gatewayResult.Amount, gatewayResult.Currency, note, DateTimeOffset.UtcNow);

        payment.AddRefund(refund);
        order.ReflectRefundState();
        await _orderRepository.UpdateAsync(order, ct);
        return (order, refund);
    }

    private async Task<Order> LoadAsync(int orderId, CancellationToken ct)
    {
        return await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), ct)
            ?? throw new OrderNotFoundException(orderId);
    }

    private static bool LooksStale(PaymentGatewayException ex) =>
        ex.Issues.Any(i => i.Contains("EXPIR", StringComparison.OrdinalIgnoreCase))
        || (ex.ErrorName?.Contains("EXPIR", StringComparison.OrdinalIgnoreCase) ?? false)
        || ex.Message.Contains("EXPIR", StringComparison.OrdinalIgnoreCase);
}
