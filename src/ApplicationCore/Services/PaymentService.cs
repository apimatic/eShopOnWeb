using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private const string CurrencyCode = "USD";

    private readonly IRepository<Order> _orderRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IBuyerService _buyerService;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IPaymentGateway paymentGateway,
        IBuyerService buyerService,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentGateway = paymentGateway;
        _buyerService = buyerService;
        _logger = logger;
    }

    public async Task<PaymentResult> PayOrderAsync(
        string buyerId, int orderId, PaymentCard? card, int? savedPaymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        if ((card is null) == (savedPaymentMethodId is null))
        {
            throw new ArgumentException("Provide either one-off card details or a saved payment method id, but not both.");
        }

        var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);

        // Idempotent short-circuit: an already-paid order is never charged again.
        if (order.PaymentStatus == PaymentStatus.Paid)
        {
            _logger.LogInformation("Order {0} is already paid; returning existing payment.", orderId);
            return ToResult(order);
        }

        if (order.PaymentStatus != PaymentStatus.AwaitingPayment)
        {
            throw new PaymentOperationException($"Order {orderId} cannot be paid because it is {order.PaymentStatus}.");
        }

        var amount = order.Total();
        // Stable, order-unique key so a duplicate/retried pay is de-duplicated at PayPal, while
        // distinct orders never collide (even across restarts where numeric ids can repeat).
        var idempotencyKey = $"pay-{order.IdempotencyToken}";

        GatewayChargeResult charge;
        if (card is not null)
        {
            charge = await _paymentGateway.ChargeCardAsync(amount, CurrencyCode, card, idempotencyKey, cancellationToken);
        }
        else
        {
            var buyer = await _buyerService.GetOrCreateBuyerAsync(buyerId, cancellationToken);
            var paymentMethod = FindOwnedPaymentMethod(buyer, savedPaymentMethodId!.Value);
            charge = await _paymentGateway.ChargeVaultedCardAsync(amount, CurrencyCode, paymentMethod.CardId, idempotencyKey, cancellationToken);
        }

        order.MarkPaid(charge.GatewayOrderId, charge.CaptureId);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Order {0} paid via PayPal (capture {1}).", orderId, charge.CaptureId);
        return ToResult(order);
    }

    public async Task<PaymentResult> RefundOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);

        // Idempotent short-circuit: an already-refunded order is never refunded again.
        if (order.PaymentStatus == PaymentStatus.Refunded)
        {
            _logger.LogInformation("Order {0} is already refunded; returning existing refund.", orderId);
            return ToResult(order);
        }

        if (order.PaymentStatus != PaymentStatus.Paid || string.IsNullOrEmpty(order.PaymentCaptureId))
        {
            throw new PaymentOperationException($"Order {orderId} cannot be refunded because it is {order.PaymentStatus}.");
        }

        var idempotencyKey = $"refund-{order.IdempotencyToken}";
        var refund = await _paymentGateway.RefundAsync(order.PaymentCaptureId!, idempotencyKey, cancellationToken);

        order.MarkRefunded(refund.RefundId);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Order {0} refunded via PayPal (refund {1}).", orderId, refund.RefundId);
        return ToResult(order);
    }

    private async Task<Order> GetOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);

        // Return the same "not found" signal whether the order does not exist or belongs to someone
        // else, so a shopper cannot probe for other shoppers' orders.
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new OrderNotFoundException(orderId);
        }

        return order;
    }

    private static Entities.BuyerAggregate.PaymentMethod FindOwnedPaymentMethod(Entities.BuyerAggregate.Buyer buyer, int paymentMethodId)
    {
        foreach (var pm in buyer.PaymentMethods)
        {
            if (pm.Id == paymentMethodId)
            {
                return pm;
            }
        }

        // Not found under this buyer => treat as not found (never reveal another buyer's card).
        throw new PaymentMethodNotFoundException(paymentMethodId);
    }

    private PaymentResult ToResult(Order order) => new PaymentResult(
        order.Id, order.PaymentStatus, order.Total(), CurrencyCode,
        order.PayPalOrderId, order.PaymentCaptureId, order.PaymentRefundId);
}
