using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPayPalGateway _payPalGateway;
    private readonly PayPalOptions _payPalOptions;

    public OrderPaymentService(IRepository<Order> orderRepository, IRepository<Buyer> buyerRepository,
        IPayPalGateway payPalGateway, PayPalOptions payPalOptions)
    {
        _orderRepository = orderRepository;
        _buyerRepository = buyerRepository;
        _payPalGateway = payPalGateway;
        _payPalOptions = payPalOptions;
    }

    public async Task<Order?> PayAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId,
        CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), ct);
        if (order is null || order.BuyerId != buyerId) return null;

        // Double-click / retry safety: an order that already has a payment is never re-authorized.
        if (order.Payment is not null)
        {
            return order;
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentOperationNotAllowedException($"Order {orderId} is not awaiting payment (status: {order.Status}).");
        }

        var exactlyOnePaymentSourceProvided = (card is not null) ^ (savedPaymentMethodId is not null);
        if (!exactlyOnePaymentSourceProvided)
        {
            throw new PaymentOperationNotAllowedException(
                "Supply exactly one of card details or a saved payment method id to pay for this order.");
        }

        string? vaultId = null;
        if (savedPaymentMethodId is not null)
        {
            var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), ct);
            var method = buyer?.PaymentMethods.FirstOrDefault(m => m.Id == savedPaymentMethodId.Value);
            if (method is null)
            {
                throw new ResourceNotFoundException($"Saved payment method {savedPaymentMethodId} was not found.");
            }

            vaultId = method.VaultId;
        }

        var amount = order.Total();
        var currency = _payPalOptions.Currency;
        var requestId = $"paypal-authorize-order-{order.Id}";

        var result = await _payPalGateway.AuthorizeAsync(requestId, amount, currency, card, vaultId, ct);

        var payment = new Payment(order.Id, currency, amount, result.PayPalOrderId, result.AuthorizationId,
            result.Status, requestId, result.ExpiresOn);
        order.AttachPayment(payment);
        await _orderRepository.UpdateAsync(order, ct);
        return order;
    }

    public async Task<Order?> FulfilAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), ct);
        if (order is null) return null;

        if (order.Status == OrderStatus.Fulfilled)
        {
            return order; // idempotent no-op
        }

        if (order.Status != OrderStatus.PaymentAuthorized || order.Payment is null)
        {
            throw new PaymentOperationNotAllowedException(
                $"Order {orderId} has no active payment authorization to fulfil (status: {order.Status}).");
        }

        var payment = order.Payment;

        if (payment.AuthorizationExpiresOn.HasValue && DateTimeOffset.UtcNow >= payment.AuthorizationExpiresOn.Value)
        {
            var reauthRequestId = payment.NextReauthorizationRequestId(order.Id);
            try
            {
                var reauth = await _payPalGateway.ReauthorizeAsync(reauthRequestId, payment.AuthorizationId,
                    payment.Amount, payment.Currency, ct);
                payment.Reauthorize(reauth.Status, reauth.ExpiresOn);
                await _orderRepository.UpdateAsync(order, ct);
            }
            catch (PaymentOperationNotAllowedException ex)
            {
                throw new PaymentOperationNotAllowedException(
                    $"Order {orderId}'s payment authorization has expired and PayPal will not renew it, so it " +
                    $"cannot be fulfilled. A new payment must be taken for this order. PayPal detail: {ex.Message}");
            }
        }

        var captureRequestId = $"paypal-capture-order-{order.Id}";
        var capture = await _payPalGateway.CaptureAsync(captureRequestId, payment.AuthorizationId, ct);
        payment.MarkCaptured(capture.CaptureId, capture.Status, captureRequestId, capture.Amount, capture.PayPalFee,
            capture.NetAmount);
        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, ct);
        return order;
    }

    public async Task<Order?> CancelAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), ct);
        if (order is null) return null;

        if (order.Status == OrderStatus.Cancelled)
        {
            return order; // idempotent no-op
        }

        if (order.Status != OrderStatus.PaymentAuthorized || order.Payment is null)
        {
            throw new PaymentOperationNotAllowedException(
                $"Order {orderId} cannot be cancelled from status {order.Status}; once fulfilled, use a refund instead.");
        }

        var requestId = $"paypal-void-order-{order.Id}";
        await _payPalGateway.VoidAsync(requestId, order.Payment.AuthorizationId, ct);
        order.Payment.MarkVoided();
        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, ct);
        return order;
    }

    public async Task<Refund?> RequestRefundAsync(string buyerId, int orderId, decimal amount, string idempotencyKey,
        CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), ct);
        if (order is null || order.BuyerId != buyerId) return null;

        if (order.Payment is null || !order.Payment.IsCaptured)
        {
            throw new PaymentOperationNotAllowedException($"Order {orderId} has not been fulfilled; there is nothing to refund.");
        }

        var payment = order.Payment;

        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return existing; // idempotent replay: same key, no new refund issued
        }

        if (amount > payment.RemainingRefundable)
        {
            throw new PaymentOperationNotAllowedException(
                $"Refund amount {amount:F2} exceeds the remaining refundable amount {payment.RemainingRefundable:F2} " +
                $"for order {orderId}.");
        }

        var result = await _payPalGateway.RefundAsync(idempotencyKey, payment.CaptureId!, amount, payment.Currency, ct);
        var refund = new Refund(result.RefundId, result.Amount, result.Status, idempotencyKey);
        payment.AddRefund(refund);
        payment.UpdateCaptureStatus(payment.RemainingRefundable <= 0m ? "REFUNDED" : "PARTIALLY_REFUNDED", null, null);
        await _orderRepository.UpdateAsync(order, ct);
        return refund;
    }
}
