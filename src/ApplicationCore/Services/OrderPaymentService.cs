using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    // Serializes payment mutations per order inside this process, so a double-click can never
    // run two authorizations/captures side by side. The provider idempotency keys below cover
    // the cross-attempt and transport-retry cases.
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();

    // PayPal stores PayPal-Request-Id keys for weeks and replays the stored response — even an
    // error — when a key repeats. The in-memory store reuses order ids across app runs, so keys
    // carry a per-process component to keep each run's operations distinct.
    private static readonly string RunId = Guid.NewGuid().ToString("N")[..8];

    private static readonly Address DefaultShipToAddress =
        new("Not provided", "Not provided", "Not provided", "Not provided", "00000");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedCard> savedCardRepository,
        IPaymentGateway paymentGateway,
        IUriComposer uriComposer,
        PayPalSettings settings,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _savedCardRepository = savedCardRepository;
        _paymentGateway = paymentGateway;
        _uriComposer = uriComposer;
        _settings = settings;
        _logger = logger;
    }

    private string Currency => _settings.Currency
        ?? throw new InvalidOperationException("PayPal:Currency is not configured.");

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> items,
        Address? shipToAddress, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items is null || items.Count == 0)
        {
            throw new BadRequestException("At least one item is required to place an order.");
        }
        if (items.Any(i => i.Quantity < 1))
        {
            throw new BadRequestException("Every item quantity must be at least 1.");
        }

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);

        var missing = ids.Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
        {
            throw new BadRequestException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = items.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress ?? DefaultShipToAddress, orderItems);
        order.SetCurrency(Currency);

        return await _orderRepository.AddAsync(order, ct);
    }

    public async Task<Order> PayOrderAsync(string buyerId, int orderId, CardDetails? card, int? savedCardId,
        CancellationToken ct = default)
    {
        if (card is null == savedCardId is null)
        {
            throw new BadRequestException("Provide exactly one of card details or savedCardId.");
        }

        var orderLock = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await orderLock.WaitAsync(ct);
        try
        {
            var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithDetailsByIdSpec(orderId), ct);
            if (order is null || order.BuyerId != buyerId)
            {
                throw new OrderNotFoundException(orderId);
            }

            if (order.PaymentStatus == OrderPaymentStatus.Authorized)
            {
                return order; // idempotent replay: the hold already exists
            }
            if (order.PaymentStatus != OrderPaymentStatus.AwaitingPayment)
            {
                throw new PaymentStateException(
                    $"Order {orderId} is {order.PaymentStatus} and cannot be paid. Only an order awaiting payment can be paid.");
            }

            string? vaultTokenId = null;
            if (savedCardId is not null)
            {
                var savedCard = await _savedCardRepository.GetByIdAsync(savedCardId.Value, ct);
                if (savedCard is null || savedCard.BuyerId != buyerId)
                {
                    throw new SavedCardNotFoundException(savedCardId.Value);
                }
                vaultTokenId = savedCard.PayPalPaymentTokenId;
            }

            var attempt = order.NextPaymentAttempt();
            await _orderRepository.UpdateAsync(order, ct);

            var result = await _paymentGateway.AuthorizePaymentAsync(
                new AuthorizationRequest(order.Id, order.Total(), Currency,
                    $"order-{order.Id}-authorize-{attempt}-{RunId}", card, vaultTokenId), ct);

            order.RegisterPayPalOrder(result.PayPalOrderId);
            order.MarkAuthorized(result.AuthorizationId, result.Status, result.ExpiresAt);
            await _orderRepository.UpdateAsync(order, ct);

            _logger.LogInformation("Order {OrderId} authorized (authorization id persisted).", order.Id);
            return order;
        }
        finally
        {
            orderLock.Release();
        }
    }

    public async Task<Order> FulfilOrderAsync(int orderId, CancellationToken ct = default)
    {
        var orderLock = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await orderLock.WaitAsync(ct);
        try
        {
            var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithDetailsByIdSpec(orderId), ct)
                ?? throw new OrderNotFoundException(orderId);

            if (order.PaymentStatus is OrderPaymentStatus.Captured or OrderPaymentStatus.CapturePending)
            {
                return order; // idempotent replay: the money was already taken
            }
            if (order.PaymentStatus != OrderPaymentStatus.Authorized)
            {
                throw new PaymentStateException(
                    $"Order {orderId} is {order.PaymentStatus}; only an authorized order can be fulfilled.");
            }

            var stale = order.AuthorizationExpiresAt.HasValue
                && order.AuthorizationExpiresAt.Value <= DateTimeOffset.UtcNow;
            if (stale)
            {
                _logger.LogInformation("Order {OrderId} authorization expired; renewing before capture.", order.Id);
                await RenewAuthorizationAsync(order, ct);
            }

            CaptureResult capture;
            try
            {
                capture = await CaptureAsync(order, ct);
            }
            catch (PaymentGatewayException ex) when (ex.IsClientError && !stale)
            {
                // The hold may have gone stale since our check. Refresh from PayPal, renew once if still open.
                var info = await _paymentGateway.GetAuthorizationAsync(order.AuthorizationId!, ct);
                order.SyncAuthorizationStatus(info.Status, info.ExpiresAt);
                if (!info.IsOpen)
                {
                    await _orderRepository.UpdateAsync(order, ct);
                    throw new PaymentStateException(
                        $"PayPal reports the authorization for order {orderId} is {info.Status} and can no longer be captured. " +
                        "Cancel the order so the shopper can place and pay for a new one.");
                }

                await RenewAuthorizationAsync(order, ct);
                capture = await CaptureAsync(order, ct);
            }

            if (capture.Status == "COMPLETED")
            {
                order.MarkCaptured(capture.CaptureId, capture.Gross ?? order.Total(), capture.Fee, capture.Net);
            }
            else if (capture.Status == "PENDING")
            {
                order.MarkCapturePending(capture.CaptureId, capture.Gross, capture.Fee, capture.Net);
            }
            else
            {
                await _orderRepository.UpdateAsync(order, ct);
                throw new PaymentStateException(
                    $"PayPal reported capture status {capture.Status} for order {orderId}. The authorization remains open; " +
                    "retry fulfilment later or cancel the order.");
            }

            await _orderRepository.UpdateAsync(order, ct);
            _logger.LogInformation("Order {OrderId} captured (capture id persisted).", order.Id);
            return order;
        }
        finally
        {
            orderLock.Release();
        }
    }

    public async Task<Order> CancelOrderAsync(int orderId, CancellationToken ct = default)
    {
        var orderLock = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await orderLock.WaitAsync(ct);
        try
        {
            var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithDetailsByIdSpec(orderId), ct)
                ?? throw new OrderNotFoundException(orderId);

            if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
            {
                return order; // idempotent replay
            }
            if (order.PaymentStatus is OrderPaymentStatus.Captured or OrderPaymentStatus.CapturePending
                or OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded)
            {
                throw new PaymentStateException(
                    $"Order {orderId} has captured funds and cannot be cancelled; issue a refund instead.");
            }

            if (order.PaymentStatus == OrderPaymentStatus.Authorized)
            {
                try
                {
                    var voidedStatus = await _paymentGateway.VoidAuthorizationAsync(order.AuthorizationId!,
                        $"order-{order.Id}-void-{order.NextVoidAttempt()}-{RunId}", ct);
                    order.SyncAuthorizationStatus(voidedStatus, null);
                }
                catch (PaymentGatewayException ex) when (ex.IsClientError)
                {
                    // PayPal voids conflict when the hold was already captured or voided; find out which.
                    var info = await _paymentGateway.GetAuthorizationAsync(order.AuthorizationId!, ct);
                    order.SyncAuthorizationStatus(info.Status, info.ExpiresAt);
                    if (info.Status is "CAPTURED" or "PARTIALLY_CAPTURED")
                    {
                        await _orderRepository.UpdateAsync(order, ct);
                        throw new PaymentStateException(
                            $"PayPal reports the authorization for order {orderId} was already captured; issue a refund instead of cancelling.");
                    }
                    // VOIDED (or otherwise closed): fall through and converge to Cancelled.
                }
            }

            order.MarkCancelled();
            await _orderRepository.UpdateAsync(order, ct);
            _logger.LogInformation("Order {OrderId} cancelled; any held funds released.", order.Id);
            return order;
        }
        finally
        {
            orderLock.Release();
        }
    }

    public async Task<RefundOutcome> RefundOrderAsync(int orderId, decimal? amount, string idempotencyKey,
        CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var orderLock = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await orderLock.WaitAsync(ct);
        try
        {
            var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithDetailsByIdSpec(orderId), ct)
                ?? throw new OrderNotFoundException(orderId);

            var existing = order.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
            if (existing is not null)
            {
                return new RefundOutcome(order, existing, Replayed: true);
            }

            if (order.PaymentStatus == OrderPaymentStatus.CapturePending)
            {
                throw new PaymentStateException(
                    $"Order {orderId} has a capture still pending at PayPal; retry the refund once it completes.");
            }
            if (order.PaymentStatus is not (OrderPaymentStatus.Captured or OrderPaymentStatus.PartiallyRefunded))
            {
                throw new PaymentStateException(
                    $"Order {orderId} is {order.PaymentStatus} and has no captured payment to refund.");
            }

            var refundAmount = amount ?? order.RemainingRefundable;
            if (refundAmount <= 0m || refundAmount > order.RemainingRefundable)
            {
                throw new PaymentStateException(
                    $"Refund amount {refundAmount:0.00} exceeds the remaining refundable amount " +
                    $"{order.RemainingRefundable:0.00} on order {orderId}.");
            }

            var result = await _paymentGateway.RefundCaptureAsync(order.CaptureId!, refundAmount, Currency,
                idempotencyKey, ct);

            var refund = order.AddRefund(result.RefundId, result.Amount, result.Currency, result.Status, idempotencyKey);
            await _orderRepository.UpdateAsync(order, ct);

            _logger.LogInformation("Order {OrderId} refunded {Amount} {Currency}.", order.Id, result.Amount, result.Currency);
            return new RefundOutcome(order, refund, Replayed: false);
        }
        finally
        {
            orderLock.Release();
        }
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _orderRepository.ListAsync(new CustomerOrdersWithDetailsSpecification(buyerId), ct);
    }

    private Task<CaptureResult> CaptureAsync(Order order, CancellationToken ct)
    {
        // No invoice id on the capture: the authorization already carries this order's invoice
        // id, and PayPal rejects a second transaction reusing it.
        return _paymentGateway.CaptureAuthorizationAsync(order.AuthorizationId!, order.Total(), Currency,
            $"order-{order.Id}-capture-{order.NextCaptureAttempt()}-{RunId}", null, ct);
    }

    private async Task RenewAuthorizationAsync(Order order, CancellationToken ct)
    {
        if (order.AuthorizedAt.HasValue && order.AuthorizedAt.Value <= DateTimeOffset.UtcNow.AddDays(-30))
        {
            throw new PaymentStateException(
                $"The PayPal authorization for order {order.Id} is more than 30 days old and can no longer be renewed. " +
                "Cancel this order and ask the shopper to place and pay for a new one.");
        }

        AuthorizationInfo renewed;
        try
        {
            renewed = await _paymentGateway.ReauthorizePaymentAsync(order.AuthorizationId!, order.Total(), Currency,
                $"order-{order.Id}-reauthorize-{order.NextPaymentAttempt()}-{RunId}", ct);
        }
        catch (PaymentGatewayException ex) when (ex.IsClientError)
        {
            throw new PaymentStateException(
                $"PayPal could not renew the authorization for order {order.Id} " +
                $"({ex.ErrorName ?? "error"}: {string.Join("; ", ex.Issues)}). " +
                "Cancel this order and ask the shopper to pay again.");
        }

        if (!renewed.IsOpen)
        {
            throw new PaymentStateException(
                $"PayPal renewed the authorization for order {order.Id} but reports status {renewed.Status}. " +
                "Cancel this order and ask the shopper to pay again.");
        }

        order.MarkReauthorized(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
        await _orderRepository.UpdateAsync(order, ct);
        _logger.LogInformation("Order {OrderId} authorization renewed.", order.Id);
    }
}
