using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private static readonly TimeSpan StaleHoldBuffer = TimeSpan.FromMinutes(5);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPaymentProcessor _payments;
    private readonly IPaymentSettings _paymentsSettings;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<Buyer> buyerRepository,
        IPaymentProcessor payments,
        IPaymentSettings paymentsSettings)
    {
        _orderRepository = orderRepository;
        _buyerRepository = buyerRepository;
        _payments = payments;
        _paymentsSettings = paymentsSettings;
    }

    public async Task<Order> PayWithCardAsync(string buyerId, int orderId, CardPaymentInput card, CancellationToken ct)
    {
        var order = await GetOwnedOrder(buyerId, orderId, ct);
        return await AuthorizeAsync(order, (requestId, token) =>
            _payments.AuthorizeCardAsync(order.Id, order.Total(), Currency(), card, requestId, token), ct);
    }

    public async Task<Order> PayWithSavedCardAsync(string buyerId, int orderId, int paymentMethodId, CancellationToken ct)
    {
        var order = await GetOwnedOrder(buyerId, orderId, ct);
        var buyer = await GetBuyer(buyerId, ct);
        var method = buyer.GetPaymentMethod(paymentMethodId);
        if (method == null || string.IsNullOrEmpty(method.CardId))
            throw new EntityNotFoundException($"Payment method {paymentMethodId} was not found.");

        return await AuthorizeAsync(order, (requestId, token) =>
            _payments.AuthorizeVaultedCardAsync(order.Id, order.Total(), Currency(), method.CardId, requestId, token), ct);
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken ct)
    {
        var order = await GetOrder(orderId, ct);
        if (order.Status == OrderStatus.Fulfilled && order.Payment.HasCapture)
        {
            var existing = await _payments.GetCaptureAsync(order.Payment.CaptureId!, ct);
            order.MarkFulfilled(existing.CaptureId, existing.Status, existing.CapturedAmount, existing.PaypalFee, existing.NetAmount,
                order.Payment.CaptureRequestId ?? $"eshop-order-{order.Id}-capture", existing.Status);
            await _orderRepository.UpdateAsync(order, ct);
            return order;
        }

        if (order.Status == OrderStatus.Cancelled)
            throw new ConflictException($"Order {order.Id} is cancelled and cannot be fulfilled.");
        if (order.Status is OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
            throw new ConflictException($"Order {order.Id} has already been fulfilled.");
        if (!order.Payment.HasHold)
            throw new ConflictException($"Order {order.Id} has no authorized payment to capture.");

        var authorizationId = order.Payment.AuthorizationId!;
        var hold = await _payments.GetAuthorizationAsync(authorizationId, ct);
        if (IsStale(hold))
        {
            try
            {
                var renewed = await _payments.ReauthorizeAsync(
                    authorizationId,
                    order.Total(),
                    Currency(),
                    $"eshop-order-{order.Id}-reauth-{order.Payment.ReauthorizeCount + 1}",
                    ct);
                order.RecordReauthorization(renewed.AuthorizationId, renewed.AuthorizationStatus, renewed.ExpirationTime);
                authorizationId = renewed.AuthorizationId;
                await _orderRepository.UpdateAsync(order, ct);
            }
            catch (PaymentProcessingException ex)
            {
                throw new PaymentProcessingException(
                    "The payment hold could not be renewed. Ask the shopper to pay again, then retry fulfilment. " + ex.Message,
                    ex,
                    ex.StatusCode >= 400 && ex.StatusCode < 500 ? ex.StatusCode : 409,
                    operatorActionable: true);
            }
        }

        var captureRequestId = $"eshop-order-{order.Id}-capture";
        CaptureResult captured;
        try
        {
            captured = await _payments.CaptureAsync(authorizationId, captureRequestId, ct);
        }
        catch (PaymentProcessingException ex) when (ex.StatusCode == 409 && order.Payment.HasCapture)
        {
            var existing = await _payments.GetCaptureAsync(order.Payment.CaptureId!, ct);
            captured = new CaptureResult
            {
                CaptureId = existing.CaptureId,
                CaptureStatus = existing.Status,
                CapturedAmount = existing.CapturedAmount,
                PaypalFee = existing.PaypalFee,
                NetAmount = existing.NetAmount,
                AuthorizationStatus = hold.Status
            };
        }

        order.MarkFulfilled(captured.CaptureId, captured.CaptureStatus, captured.CapturedAmount, captured.PaypalFee, captured.NetAmount, captureRequestId, captured.AuthorizationStatus);
        await _orderRepository.UpdateAsync(order, ct);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken ct)
    {
        var order = await GetOrder(orderId, ct);
        if (order.Status == OrderStatus.Cancelled)
            return order;
        if (order.Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
            throw new ConflictException($"Order {order.Id} is already fulfilled and cannot be cancelled. Issue a refund instead.");

        var voidRequestId = $"eshop-order-{order.Id}-void";
        if (order.Payment.HasHold)
            await _payments.VoidAuthorizationAsync(order.Payment.AuthorizationId!, voidRequestId, ct);

        order.MarkCancelled(order.Payment.HasHold ? "VOIDED" : order.Payment.AuthorizationStatus, voidRequestId);
        await _orderRepository.UpdateAsync(order, ct);
        return order;
    }

    public async Task<OrderRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("A refund idempotency key is required.", nameof(idempotencyKey));

        var order = await GetOwnedOrder(buyerId, orderId, ct);
        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
            return existing;

        if (order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded))
            throw new ConflictException($"Order {order.Id} cannot be refunded from status {order.Status}.");
        if (!order.Payment.HasCapture)
            throw new ConflictException($"Order {order.Id} has no captured payment to refund.");

        var capture = await _payments.GetCaptureAsync(order.Payment.CaptureId!, ct);
        if (string.Equals(capture.Status, "REFUNDED", StringComparison.OrdinalIgnoreCase))
            throw new ConflictException($"Order {order.Id} is already fully refunded.");

        var remaining = order.Payment.RemainingRefundable;
        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0)
            throw new ArgumentException("Refund amount must be greater than zero.");
        if (refundAmount > remaining)
            throw new ConflictException($"Refund of {refundAmount} exceeds remaining refundable amount {remaining}.");

        var result = await _payments.RefundAsync(
            order.Payment.CaptureId!,
            order.Payment.PayPalOrderId,
            amount.HasValue ? refundAmount : null,
            Currency(),
            idempotencyKey,
            ct);

        var refund = order.AddRefund(result.RefundId, idempotencyKey, result.Amount, result.Status ?? "COMPLETED", result.CaptureStatus);
        await _orderRepository.UpdateAsync(order, ct);
        return refund;
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
    }

    public Task<Order> GetMyOrderAsync(string buyerId, int orderId, CancellationToken ct) =>
        GetOwnedOrder(buyerId, orderId, ct);

    private async Task<Order> AuthorizeAsync(Order order, Func<string, CancellationToken, Task<AuthorizationResult>> authorize, CancellationToken ct)
    {
        if (order.Status == OrderStatus.Authorized && order.Payment.HasHold)
            return order;
        if (order.Status is not OrderStatus.AwaitingPayment)
            throw new ConflictException($"Order {order.Id} cannot be paid from status {order.Status}.");

        var requestId = $"eshop-order-{order.Id}-authorize";
        AuthorizationResult result;
        if (!string.IsNullOrEmpty(order.Payment.PayPalOrderId) && !order.Payment.HasHold)
            result = await _payments.AuthorizeExistingPayPalOrderAsync(order.Payment.PayPalOrderId, requestId, ct);
        else
            result = await authorize(requestId, ct);

        order.RecordPayPalOrder(result.PayPalOrderId, result.PayPalOrderStatus, Currency(), requestId);
        order.MarkAuthorized(result.AuthorizationId, result.AuthorizationStatus, result.ExpirationTime, result.PayPalOrderStatus);
        await _orderRepository.UpdateAsync(order, ct);
        return order;
    }

    private async Task<Order> GetOwnedOrder(string buyerId, int orderId, CancellationToken ct)
    {
        var order = await GetOrder(orderId, ct);
        if (!order.BelongsTo(buyerId))
            throw new ForbiddenException("The order does not belong to the caller.");
        return order;
    }

    private async Task<Order> GetOrder(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        if (order == null)
            throw new EntityNotFoundException($"Order {orderId} was not found.");
        return order;
    }

    private async Task<Buyer> GetBuyer(string buyerId, CancellationToken ct)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerByIdentitySpecification(buyerId), ct);
        if (buyer == null)
            throw new EntityNotFoundException("No saved cards exist for the caller.");
        return buyer;
    }

    private string Currency()
    {
        if (string.IsNullOrWhiteSpace(_paymentsSettings.Currency))
            throw new PaymentProcessingException("PayPal:Currency is not configured.", 500);
        return _paymentsSettings.Currency;
    }

    private static bool IsStale(AuthorizationDetails hold)
    {
        if (hold.ExpirationTime.HasValue && hold.ExpirationTime.Value <= DateTimeOffset.UtcNow.Add(StaleHoldBuffer))
            return true;
        if (!string.IsNullOrEmpty(hold.Status) &&
            !string.Equals(hold.Status, "CREATED", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(hold.Status, "PENDING", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(hold.Status, "EXPIRED", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
