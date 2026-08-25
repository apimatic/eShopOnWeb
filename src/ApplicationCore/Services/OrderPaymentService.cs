using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPaymentGateway _gateway;
    private readonly string _currency;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<Buyer> buyerRepository,
        IPaymentGateway gateway,
        string currency)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _catalogItemRepository = catalogItemRepository;
        _buyerRepository = buyerRepository;
        _gateway = gateway;
        _currency = currency;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items, Address shippingAddress, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items is null || items.Count == 0)
        {
            throw new InvalidOrderStateException("An order must contain at least one item.");
        }

        var catalogItemIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), ct);

        var orderItems = new List<OrderItem>();
        foreach (var line in items)
        {
            if (line.Quantity <= 0)
            {
                throw new InvalidOrderStateException($"Quantity for catalog item {line.CatalogItemId} must be positive.");
            }

            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
            if (catalogItem is null)
            {
                throw new ResourceNotFoundException($"Catalog item {line.CatalogItemId} was not found.");
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shippingAddress, orderItems);
        await _orderRepository.AddAsync(order, ct);

        var payment = new Payment(order.Id, order.Total(), _currency);
        await _paymentRepository.AddAsync(payment, ct);

        return order;
    }

    public async Task<Payment> AuthorizePaymentAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId, CancellationToken ct)
    {
        var order = await GetOwnedOrderAsync(orderId, buyerId, ct);
        var payment = await GetPaymentForOrderAsync(orderId, ct);

        if (payment.Status != PaymentStatus.AwaitingAuthorization)
        {
            // Idempotent in effect: a repeated pay request after authorization already happened
            // returns the existing state instead of authorizing (holding funds) a second time.
            return payment;
        }

        if (card is null && savedPaymentMethodId is null)
        {
            throw new InvalidOrderStateException("Provide either card details or a saved payment method id.");
        }

        if (card is not null && savedPaymentMethodId is not null)
        {
            throw new InvalidOrderStateException("Provide either card details or a saved payment method id, not both.");
        }

        var requestId = $"authorize-order-{orderId}";
        CardAuthorizationResult result;
        int? paymentMethodId = null;

        if (savedPaymentMethodId is not null)
        {
            var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), ct);
            var method = buyer?.PaymentMethods.FirstOrDefault(m => m.Id == savedPaymentMethodId && m.IsActive);
            if (method is null)
            {
                throw new ResourceNotFoundException($"Saved card {savedPaymentMethodId} was not found.");
            }

            result = await _gateway.AuthorizeWithVaultedCardAsync(method.CardId!, payment.Amount, payment.Currency, requestId, ct);
            paymentMethodId = method.Id;
        }
        else
        {
            result = await _gateway.AuthorizeWithCardAsync(card!, payment.Amount, payment.Currency, requestId, ct);
        }

        payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId, result.Status, result.ExpiresAt, paymentMethodId);
        await _paymentRepository.UpdateAsync(payment, ct);

        order.MarkPaymentAuthorized();
        await _orderRepository.UpdateAsync(order, ct);

        return payment;
    }

    public async Task<Payment> FulfilOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            throw new ResourceNotFoundException($"Order {orderId} was not found.");
        }

        var payment = await GetPaymentForOrderAsync(orderId, ct);

        if (payment.Status == PaymentStatus.AwaitingAuthorization)
        {
            throw new InvalidOrderStateException($"Order {orderId} has not been authorized for payment yet.");
        }

        if (payment.Status != PaymentStatus.Authorized)
        {
            // Idempotent in effect: already captured (or voided/refunded) -- return current state.
            return payment;
        }

        var captureResult = await CaptureWithRenewalAsync(payment, ct);

        payment.MarkCaptured(captureResult.CaptureId, captureResult.Status, captureResult.CapturedAmount, captureResult.FeeAmount, captureResult.NetAmount);
        await _paymentRepository.UpdateAsync(payment, ct);

        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, ct);

        return payment;
    }

    public async Task<Order> CancelOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            throw new ResourceNotFoundException($"Order {orderId} was not found.");
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            return order; // idempotent
        }

        if (order.Status == OrderStatus.Fulfilled)
        {
            throw new InvalidOrderStateException($"Order {orderId} has already been fulfilled and cannot be cancelled; issue a refund instead.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), ct);
        if (payment is not null && payment.Status == PaymentStatus.Authorized)
        {
            await _gateway.VoidAsync(payment.PayPalAuthorizationId!, $"void-order-{orderId}", ct);
            payment.MarkVoided();
            await _paymentRepository.UpdateAsync(payment, ct);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, ct);

        return order;
    }

    public async Task<Refund> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        await GetOwnedOrderAsync(orderId, buyerId, ct);
        var payment = await GetPaymentForOrderAsync(orderId, ct);

        // Idempotency short-circuit: a retried request under the same caller-supplied key must
        // not call PayPal again, and must not count as a second (legitimate) partial refund.
        var existing = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        if (payment.Status != PaymentStatus.Captured && payment.Status != PaymentStatus.PartiallyRefunded)
        {
            throw new InvalidOrderStateException($"Order {orderId} has not been fulfilled, so it cannot be refunded.");
        }

        var refundAmount = amount ?? payment.RemainingRefundable;
        if (refundAmount <= 0 || refundAmount > payment.RemainingRefundable)
        {
            throw new InvalidOrderStateException(
                $"Refund amount must be greater than 0 and no more than the remaining refundable amount of {payment.RemainingRefundable} {payment.Currency}.");
        }

        var result = await _gateway.RefundAsync(payment.PayPalCaptureId!, amount, payment.Currency, idempotencyKey, ct);

        var refund = payment.AddRefund(result.RefundId, result.Amount, result.Status, idempotencyKey);
        await _paymentRepository.UpdateAsync(payment, ct);

        return refund;
    }

    public async Task<IReadOnlyList<OrderWithPayment>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var orderIds = orders.Select(o => o.Id).ToArray();
        var payments = await _paymentRepository.ListAsync(new PaymentsByOrderIdsSpecification(orderIds), ct);

        return orders
            .Select(o => new OrderWithPayment(o, payments.FirstOrDefault(p => p.OrderId == o.Id)))
            .ToList();
    }

    private async Task<Order> GetOwnedOrderAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null || order.BuyerId != buyerId)
        {
            throw new ResourceNotFoundException($"Order {orderId} was not found.");
        }

        return order;
    }

    private async Task<Payment> GetPaymentForOrderAsync(int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), ct);
        if (payment is null)
        {
            throw new ResourceNotFoundException($"Order {orderId} was not found.");
        }

        return payment;
    }

    /// <summary>
    /// Captures the authorization, renewing it first if it has already gone stale (or if PayPal
    /// rejects the capture specifically because it has expired), and retries the capture once
    /// after a successful renewal. A renewal PayPal itself refuses bubbles up as
    /// <see cref="AuthorizationNotRenewableException"/> for an operator to act on.
    /// </summary>
    private async Task<CaptureResult> CaptureWithRenewalAsync(Payment payment, CancellationToken ct)
    {
        var requestId = $"capture-order-{payment.OrderId}";
        var authorizationStale = payment.AuthorizationExpiresAt is not null && payment.AuthorizationExpiresAt.Value <= DateTimeOffset.UtcNow;

        if (!authorizationStale)
        {
            try
            {
                return await _gateway.CaptureAsync(payment.PayPalAuthorizationId!, requestId, ct);
            }
            catch (AuthorizationExpiredException)
            {
                // fall through to renewal below
            }
        }

        var reauth = await _gateway.ReauthorizeAsync(payment.PayPalAuthorizationId!, payment.Amount, payment.Currency, $"reauthorize-order-{payment.OrderId}", ct);
        payment.MarkReauthorized(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
        await _paymentRepository.UpdateAsync(payment, ct);

        return await _gateway.CaptureAsync(payment.PayPalAuthorizationId!, $"{requestId}-renewed", ct);
    }
}
