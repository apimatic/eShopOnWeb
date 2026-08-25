using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalGateway _payPalGateway;
    private readonly string _currency;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IUriComposer uriComposer,
        IPayPalGateway payPalGateway,
        IOptions<PayPalCurrencyOptions> currencyOptions)
    {
        _orderRepository = orderRepository;
        _catalogItemRepository = catalogItemRepository;
        _uriComposer = uriComposer;
        _payPalGateway = payPalGateway;
        _currency = currencyOptions.Value.Currency;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, Address shipToAddress, IReadOnlyList<OrderLineItemRequest> items, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (items is null || items.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one item.", nameof(items));
        }

        var catalogItemIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), ct);

        var orderItems = new List<OrderItem>();
        foreach (var line in items)
        {
            if (line.Quantity <= 0)
            {
                throw new ArgumentException($"Quantity for catalog item {line.CatalogItemId} must be positive.", nameof(items));
            }

            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
            if (catalogItem is null)
            {
                throw new ArgumentException($"Catalog item {line.CatalogItemId} does not exist.", nameof(items));
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orderRepository.AddAsync(order, ct);
        return order;
    }

    public async Task<(Order Order, AuthorizePaymentOutcome Outcome)?> PayAsync(int orderId, string buyerId, PaymentSourceRequest paymentSource, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), ct);
        if (order is null || order.BuyerId != buyerId)
        {
            return null;
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            if (order.Status == OrderStatus.PaymentAuthorized && order.Payment is not null)
            {
                // Idempotent replay: a double-click after the first authorize already succeeded.
                var payment = order.Payment;
                return (order, new AuthorizePaymentAuthorized(payment.PayPalOrderId, payment.AuthorizationId,
                    payment.AuthorizationStatus, payment.AuthorizedAmount, payment.Currency, payment.AuthorizationExpiresAt));
            }

            throw new InvalidOrderStateException($"Order {orderId} cannot be paid: current status is {order.Status}.");
        }

        var amount = order.Total();
        var idempotencyKey = $"authorize:{orderId}";
        var outcome = await _payPalGateway.AuthorizeAsync(amount, _currency, paymentSource, idempotencyKey, ct);

        if (outcome is AuthorizePaymentAuthorized authorized)
        {
            var payment = new Payment(authorized.PayPalOrderId, authorized.AuthorizationId, authorized.AuthorizationStatus,
                authorized.AuthorizedAmount, authorized.Currency, authorized.ExpiresAt);
            order.AuthorizePayment(payment);
            await _orderRepository.UpdateAsync(order, ct);
        }

        return (order, outcome);
    }

    public async Task<Order?> FulfilAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), ct);
        if (order is null)
        {
            return null;
        }

        if (order.Status == OrderStatus.Fulfilled)
        {
            return order; // idempotent replay
        }

        if (order.Status != OrderStatus.PaymentAuthorized || order.Payment is null)
        {
            throw new InvalidOrderStateException($"Order {orderId} cannot be fulfilled: current status is {order.Status}.");
        }

        var payment = order.Payment;
        var captureResult = await CaptureWithRenewalAsync(order, payment, ct);

        payment.Captured(captureResult.CaptureId, captureResult.Status, captureResult.CapturedAmount,
            captureResult.FeeAmount, captureResult.NetAmount);
        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, ct);
        return order;
    }

    private async Task<CaptureResult> CaptureWithRenewalAsync(Order order, Payment payment, CancellationToken ct)
    {
        var snapshot = await _payPalGateway.GetAuthorizationAsync(payment.AuthorizationId, ct);
        var idempotencyKey = $"capture:{order.Id}";

        var proactivelyStale = snapshot.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow;
        if (proactivelyStale)
        {
            await RenewAuthorizationAsync(order, payment, ct);
            return await _payPalGateway.CaptureAsync(payment.AuthorizationId, payment.AuthorizedAmount, payment.Currency, idempotencyKey, ct);
        }

        try
        {
            return await _payPalGateway.CaptureAsync(payment.AuthorizationId, payment.AuthorizedAmount, payment.Currency, idempotencyKey, ct);
        }
        catch (PaymentGatewayException) when (payment.CaptureId is null)
        {
            // PayPal disagreed with our proactive staleness check (e.g. clock skew) — renew once and retry.
            await RenewAuthorizationAsync(order, payment, ct);
            return await _payPalGateway.CaptureAsync(payment.AuthorizationId, payment.AuthorizedAmount, payment.Currency, idempotencyKey, ct);
        }
    }

    private async Task RenewAuthorizationAsync(Order order, Payment payment, CancellationToken ct)
    {
        var renewed = await _payPalGateway.ReauthorizeAsync(payment.AuthorizationId, ct);
        payment.Reauthorized(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
        await _orderRepository.UpdateAsync(order, ct);
    }

    public async Task<Order?> CancelAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), ct);
        if (order is null)
        {
            return null;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            return order; // idempotent replay
        }

        if (order.Status == OrderStatus.PaymentAuthorized && order.Payment is not null)
        {
            await _payPalGateway.VoidAsync(order.Payment.AuthorizationId, ct);
            order.Payment.Voided("VOIDED");
        }
        else if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidOrderStateException($"Order {orderId} cannot be cancelled: current status is {order.Status}.");
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, ct);
        return order;
    }

    public async Task<(Order Order, Refund Refund)?> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), ct);
        if (order is null || order.BuyerId != buyerId || order.Payment is null)
        {
            return null;
        }

        var payment = order.Payment;

        var existingRefund = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existingRefund is not null)
        {
            // Idempotent replay: same key seen before — never refund twice for it.
            return (order, existingRefund);
        }

        if (order.Status != OrderStatus.Fulfilled && order.Status != OrderStatus.PartiallyRefunded)
        {
            throw new InvalidOrderStateException($"Order {orderId} cannot be refunded: current status is {order.Status}.");
        }

        if (payment.CaptureId is null)
        {
            throw new InvalidOrderStateException($"Order {orderId} has no captured payment to refund.");
        }

        var refundableAmount = payment.RefundableAmount;
        if (refundableAmount <= 0m)
        {
            throw new InvalidOrderStateException($"Order {orderId} has already been refunded in full.");
        }

        if (amount is { } requested && (requested <= 0m || requested > refundableAmount))
        {
            throw new InvalidOrderStateException(
                $"Refund amount {requested} {payment.Currency} exceeds the {refundableAmount} {payment.Currency} still refundable on order {orderId}.");
        }

        var refundResult = await _payPalGateway.RefundAsync(payment.CaptureId, amount, payment.Currency, idempotencyKey, ct);
        var refund = payment.AddRefund(refundResult.RefundId, refundResult.Amount, refundResult.Status, idempotencyKey);

        var isFullRefund = payment.RefundableAmount <= 0m;
        order.MarkRefunded(isFullRefund);

        await _orderRepository.UpdateAsync(order, ct);
        return (order, refund);
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), ct);
    }
}
