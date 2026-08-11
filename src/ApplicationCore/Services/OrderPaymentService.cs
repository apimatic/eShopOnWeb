using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Coordinates the payment lifecycle over the existing Order aggregate: place → authorize (hold) →
/// fulfil (capture) / cancel (void) / refund. PayPal is reached only through
/// <see cref="IPayPalPaymentGateway"/>. Payment operations serialize per-order (via
/// <see cref="KeyedAsyncLock"/>) and are idempotent in effect.
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _savedPaymentMethodRepository;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IPaymentConfiguration _paymentConfiguration;
    private readonly KeyedAsyncLock _orderLock;

    private static readonly Address DefaultShippingAddress =
        new("123 Main St", "Kent", "OH", "US", "44240");

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> savedPaymentMethodRepository,
        IPayPalPaymentGateway gateway,
        IPaymentConfiguration paymentConfiguration,
        KeyedAsyncLock orderLock)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _savedPaymentMethodRepository = savedPaymentMethodRepository;
        _gateway = gateway;
        _paymentConfiguration = paymentConfiguration;
        _orderLock = orderLock;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines,
        Address? shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(lines, nameof(lines));
        if (lines.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one line.", nameof(lines));
        }
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new ArgumentException($"Quantity for catalog item {line.CatalogItemId} must be positive.");
            }
        }

        var itemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(itemIds), cancellationToken);

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new ArgumentException($"Catalog item {line.CatalogItemId} does not exist.");
            var pictureUri = string.IsNullOrEmpty(catalogItem.PictureUri) ? "eCatalog-item-default.png" : catalogItem.PictureUri;
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            // Price comes from the catalog, never from the request.
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress ?? DefaultShippingAddress, orderItems);
        return await _orderRepository.AddAsync(order, cancellationToken);
    }

    public async Task<Order> AuthorizeOrderAsync(string buyerId, int orderId, CardDetails? card,
        int? savedPaymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (card is null && savedPaymentMethodId is null)
        {
            throw new ArgumentException("Provide card details or a saved payment method to pay with.");
        }

        using (await _orderLock.LockAsync(OrderKey(orderId), cancellationToken))
        {
            var order = await GetOrderForBuyerAsync(orderId, buyerId, cancellationToken);

            // Idempotent: a repeated pay for an already-authorized order returns the existing hold.
            if (order.Payment is { IsAuthorized: true })
            {
                return order;
            }
            if (order.Status == OrderStatus.Cancelled)
            {
                throw new InvalidOperationException($"Order {orderId} is cancelled and cannot be paid.");
            }
            if (order.Status == OrderStatus.Fulfilled)
            {
                throw new InvalidOperationException($"Order {orderId} is already fulfilled.");
            }

            var amount = order.Total();
            // Fresh request id per attempt so a declined card can be retried with another card,
            // while the per-order lock + state check above prevent a double-click double-authorizing.
            var requestId = $"authorize-{orderId}-{Guid.NewGuid():N}";

            CardAuthorizationResult result;
            if (savedPaymentMethodId is int savedId)
            {
                var saved = await _savedPaymentMethodRepository.FirstOrDefaultAsync(
                    new SavedPaymentMethodByIdForBuyerSpecification(savedId, buyerId), cancellationToken)
                    ?? throw new PaymentGatewayException($"Saved payment method {savedId} was not found for this shopper.");
                result = await _gateway.AuthorizeWithVaultedCardAsync(amount, saved.VaultId, requestId, cancellationToken);
            }
            else
            {
                result = await _gateway.AuthorizeWithCardAsync(amount, card!, storeInVault: false, requestId, cancellationToken);
            }

            order.RecordAuthorization(result.PayPalOrderId, result.AuthorizationId, result.Status,
                _paymentConfiguration.Currency, result.ExpiresAt);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        }
    }

    public async Task<Order> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        using (await _orderLock.LockAsync(OrderKey(orderId), cancellationToken))
        {
            var order = await GetOrderAsync(orderId, cancellationToken);

            if (order.Status == OrderStatus.Fulfilled)
            {
                return order; // already captured — idempotent
            }
            if (order.Status == OrderStatus.Cancelled)
            {
                throw new InvalidOperationException($"Order {orderId} is cancelled and cannot be fulfilled.");
            }
            var payment = order.RequirePayment();
            if (!payment.IsAuthorized)
            {
                throw new InvalidOperationException($"Order {orderId} has no authorization to capture.");
            }

            // Renew a stale authorization rather than failing the fulfilment outright.
            if (payment.AuthorizationExpiresAt is { } expires && expires <= DateTimeOffset.UtcNow)
            {
                await RenewAuthorizationAsync(order, cancellationToken);
            }

            CaptureResult capture;
            try
            {
                capture = await _gateway.CaptureAuthorizationAsync(payment.AuthorizationId!,
                    $"capture-{orderId}-{Guid.NewGuid():N}", cancellationToken);
            }
            catch (AuthorizationExpiredException)
            {
                // Stale despite our check: renew then capture the fresh authorization once more.
                await RenewAuthorizationAsync(order, cancellationToken);
                capture = await _gateway.CaptureAuthorizationAsync(payment.AuthorizationId!,
                    $"capture-{orderId}-{Guid.NewGuid():N}", cancellationToken);
            }

            order.RecordFulfilment(capture.CaptureId, capture.Status, capture.Amount, capture.PayPalFee, capture.NetAmount);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        }
    }

    private async Task RenewAuthorizationAsync(Order order, CancellationToken cancellationToken)
    {
        var payment = order.RequirePayment();
        try
        {
            var renewed = await _gateway.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount, cancellationToken);
            order.RecordReauthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            await _orderRepository.UpdateAsync(order, cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            throw new AuthorizationNotRenewableException(
                $"Order {order.Id}'s authorization has expired and can no longer be renewed ({ex.Message}). " +
                "Ask the shopper to place and pay for the order again.", ex);
        }
    }

    public async Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        using (await _orderLock.LockAsync(OrderKey(orderId), cancellationToken))
        {
            var order = await GetOrderAsync(orderId, cancellationToken);

            if (order.Status == OrderStatus.Cancelled)
            {
                return order; // already released — idempotent
            }
            if (order.Status == OrderStatus.Fulfilled)
            {
                throw new InvalidOperationException(
                    $"Order {orderId} is already fulfilled; refund it instead of cancelling.");
            }

            if (order.Payment is { IsAuthorized: true } payment)
            {
                await _gateway.VoidAuthorizationAsync(payment.AuthorizationId!, cancellationToken);
            }

            order.RecordCancellation();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        }
    }

    public async Task<(Order Order, PaymentRefund Refund)> RefundOrderAsync(string buyerId, int orderId,
        decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        using (await _orderLock.LockAsync(OrderKey(orderId), cancellationToken))
        {
            var order = await GetOrderForBuyerAsync(orderId, buyerId, cancellationToken);
            var payment = order.RequirePayment();

            if (!payment.IsCaptured)
            {
                throw new InvalidOperationException($"Order {orderId} has not been captured and cannot be refunded.");
            }

            // Idempotent: the same key never refunds twice.
            var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
            if (existing is not null)
            {
                return (order, existing);
            }

            // Full refund defaults to what is still refundable; a partly-refunded order can never
            // become refundable beyond what was captured.
            var refundAmount = amount ?? payment.RefundableRemaining;
            payment.GuardRefundWithinCaptured(refundAmount);

            var result = await _gateway.RefundCaptureAsync(payment.CaptureId!, refundAmount, idempotencyKey, cancellationToken);
            var refund = payment.AddRefund(result.RefundId, result.Amount, result.Status, idempotencyKey, DateTimeOffset.UtcNow);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return (order, refund);
        }
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken)
            ?? throw new OrderNotFoundException(orderId);
    }

    private async Task<Order> GetOrderForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        return await _orderRepository.FirstOrDefaultAsync(
            new OrderByIdForBuyerSpecification(orderId, buyerId), cancellationToken)
            ?? throw new OrderNotFoundException(orderId);
    }

    private static string OrderKey(int orderId) => $"order-{orderId}";
}
