using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Settings;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new ConcurrentDictionary<int, SemaphoreSlim>();

    // Scopes PayPal request ids to this app instance so they are stable for dedup within a
    // run but never collide with a previous deployment or a restarted in-memory store.
    private static readonly string InstanceId = Guid.NewGuid().ToString("N");

    private readonly IPayPalGateway _payPalGateway;
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderRefund> _refundRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<PaymentMethod> _paymentMethodRepository;
    private readonly PayPalOptions _options;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IPayPalGateway payPalGateway,
        IRepository<Order> orderRepository,
        IRepository<OrderRefund> refundRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<PaymentMethod> paymentMethodRepository,
        PayPalOptions options,
        IAppLogger<OrderPaymentService> logger)
    {
        _payPalGateway = payPalGateway;
        _orderRepository = orderRepository;
        _refundRepository = refundRepository;
        _catalogItemRepository = catalogItemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _options = options;
        _logger = logger;
    }

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderLineItem> lines, Address shipToAddress, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(_options.Currency, nameof(_options.Currency));

        if (lines is null || lines.Count == 0)
        {
            throw new InvalidOrderStateException("An order must contain at least one item.", 400);
        }

        var distinctIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItemRepository.ListAsync(new CatalogItemsSpecification(distinctIds), ct);

        if (catalogItems.Count != distinctIds.Length)
        {
            throw new InvalidOrderStateException("One or more catalog items could not be found.", 400);
        }

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new InvalidOrderStateException("Quantities must be greater than zero.", 400);
            }

            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, items, _options.Currency);
        await _orderRepository.AddAsync(order, ct);
        return order;
    }

    public Task<Order> PayAsync(string buyerId, int orderId, OrderPaymentMethod payment, CancellationToken ct)
    {
        return WithOrderLockAsync(orderId, async () =>
        {
            var order = await LoadOrderAsync(orderId, ct);
            if (order.BuyerId != buyerId)
            {
                throw new OrderNotFoundException(orderId);
            }

            if (order.Status == OrderStatus.Cancelled)
            {
                throw new InvalidOrderStateException("This order is cancelled and can no longer be paid.", 409);
            }

            // Idempotent in effect: a double-click never authorizes the shopper twice.
            if (order.Status == OrderStatus.Authorized &&
                !string.IsNullOrWhiteSpace(order.AuthorizationId) &&
                !string.IsNullOrWhiteSpace(order.PayPalOrderId))
            {
                return order;
            }

            var (card, vaultId) = await ResolvePaymentSourceAsync(buyerId, payment, ct);

            var createResult = await _payPalGateway.CreateOrderAsync(
                orderId, order.Total(), _options.Currency, card, vaultId, RequestId("pay", orderId), ct);

            string authorizationId;
            string authorizationStatus;
            DateTimeOffset? expirationTime;

            if (!string.IsNullOrWhiteSpace(createResult.AuthorizationId))
            {
                // Card payments are authorized when the order is created.
                authorizationId = createResult.AuthorizationId;
                authorizationStatus = createResult.AuthorizationStatus ?? "AUTHORIZED";
                expirationTime = createResult.ExpirationTime;
            }
            else
            {
                // The order was not authorized at creation; authorize it explicitly.
                var authorizeResult = await _payPalGateway.AuthorizeOrderAsync(
                    createResult.OrderId, card, vaultId, RequestId("auth", orderId), ct);

                if (string.IsNullOrWhiteSpace(authorizeResult.AuthorizationId))
                {
                    throw new PayPalApiException("PayPal did not return an authorization for this order.", 422);
                }

                authorizationId = authorizeResult.AuthorizationId;
                authorizationStatus = authorizeResult.AuthorizationStatus ?? string.Empty;
                expirationTime = authorizeResult.ExpirationTime;
            }

            order.MarkAuthorized(
                createResult.OrderId,
                authorizationId,
                authorizationStatus,
                expirationTime);

            await _orderRepository.UpdateAsync(order, ct);
            _logger.LogInformation("Order {OrderId} authorized with PayPal authorization {AuthorizationId}.", order.Id, authorizationId);
            return order;
        });
    }

    public Task<Order> FulfilAsync(int orderId, CancellationToken ct)
    {
        return WithOrderLockAsync(orderId, async () =>
        {
            var order = await LoadOrderAsync(orderId, ct);

            if (order.Status == OrderStatus.Cancelled)
            {
                throw new InvalidOrderStateException("A cancelled order cannot be fulfilled.", 409);
            }

            if (order.Status == OrderStatus.PartiallyRefunded || order.Status == OrderStatus.Refunded)
            {
                throw new InvalidOrderStateException("A refunded order cannot be fulfilled.", 409);
            }

            // Idempotent in effect: an already-completed capture is not taken twice.
            if (order.Status == OrderStatus.Fulfilled &&
                !string.IsNullOrWhiteSpace(order.CaptureId) &&
                string.Equals(order.CaptureStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase))
            {
                return order;
            }

            if (string.IsNullOrWhiteSpace(order.AuthorizationId))
            {
                throw new InvalidOrderStateException("This order has no authorized payment to capture.", 409);
            }

            var authorizationId = order.AuthorizationId;

            if (order.IsAuthorizationStale())
            {
                await RenewAuthorizationAsync(order, authorizationId, ct);
            }

            var capture = await _payPalGateway.CaptureAsync(authorizationId, RequestId("capture", orderId), ct);

            order.MarkCaptured(capture.CaptureId, capture.Status, capture.GrossAmount, capture.FeeAmount, capture.NetAmount);
            await _orderRepository.UpdateAsync(order, ct);

            _logger.LogInformation("Order {OrderId} fulfilled; PayPal capture {CaptureId} status {CaptureStatus}.", order.Id, capture.CaptureId, capture.Status);
            return order;
        });
    }

    public Task<Order> CancelAsync(int orderId, CancellationToken ct)
    {
        return WithOrderLockAsync(orderId, async () =>
        {
            var order = await LoadOrderAsync(orderId, ct);

            if (order.Status == OrderStatus.Cancelled)
            {
                return order;
            }

            if (order.Status == OrderStatus.Fulfilled ||
                order.Status == OrderStatus.PartiallyRefunded ||
                order.Status == OrderStatus.Refunded)
            {
                throw new InvalidOrderStateException("A fulfilled order cannot be cancelled; refund it instead.", 409);
            }

            if (!string.IsNullOrWhiteSpace(order.AuthorizationId) && !order.IsAuthorizationStale())
            {
                try
                {
                    await _payPalGateway.VoidAsync(order.AuthorizationId, RequestId("void", orderId), ct);
                }
                catch (PayPalApiException ex) when (ex.StatusCode == 409)
                {
                    // The authorization is already in a terminal state (for example already
                    // voided); no money moves, so cancellation still succeeds.
                    _logger.LogWarning("Order {OrderId} void returned 409: {Message}", order.Id, ex.Message);
                }
            }

            order.Cancel();
            await _orderRepository.UpdateAsync(order, ct);
            _logger.LogInformation("Order {OrderId} cancelled; any held funds were released.", order.Id);
            return order;
        });
    }

    public Task<(Order Order, OrderRefund Refund)> RefundAsync(int orderId, decimal amount, string idempotencyKey, CancellationToken ct)
    {
        return WithOrderLockAsync(orderId, async () =>
        {
            var order = await LoadOrderAsync(orderId, ct);

            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                throw new InvalidOrderStateException("A refund idempotency key is required.", 400);
            }

            if (order.Status == OrderStatus.AwaitingPayment || order.Status == OrderStatus.Authorized || order.Status == OrderStatus.Cancelled)
            {
                throw new InvalidOrderStateException("Only a fulfilled order can be refunded.", 409);
            }

            if (string.IsNullOrWhiteSpace(order.CaptureId))
            {
                throw new InvalidOrderStateException("This order has no captured payment to refund.", 409);
            }

            var existing = await _refundRepository.FirstOrDefaultAsync(new OrderRefundByIdempotencyKeySpec(order.Id, idempotencyKey), ct);
            if (existing is not null)
            {
                return (order, existing);
            }

            if (amount <= 0)
            {
                throw new InvalidOrderStateException("Refund amount must be greater than zero.", 400);
            }

            var refundable = order.RefundableAmount();
            if (amount > refundable)
            {
                throw new InvalidOrderStateException($"Refund amount exceeds the refundable balance ({refundable:0.00} {order.Currency}).", 422);
            }

            var refund = await _payPalGateway.RefundAsync(order.CaptureId, amount, _options.Currency, idempotencyKey, ct);

            var refundEntity = new OrderRefund(order.Id, refund.RefundId, refund.Status, amount, order.Currency, idempotencyKey);
            await _refundRepository.AddAsync(refundEntity, ct);
            order.ApplyRefund(refundEntity);
            await _orderRepository.UpdateAsync(order, ct);

            _logger.LogInformation("Order {OrderId} refunded {Amount} {Currency}; PayPal refund {RefundId} status {RefundStatus}.", order.Id, amount, order.Currency, refund.RefundId, refund.Status);
            return (order, refundEntity);
        });
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
    }

    public async Task<PaymentMethod> SavePaymentMethodAsync(string buyerId, PayPalCardDetails card, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var result = await _payPalGateway.CreatePaymentTokenAsync(card, Guid.NewGuid().ToString("N"), MerchantCustomerId(buyerId), ct);

        var paymentMethod = new PaymentMethod(buyerId, result.TokenId, result.Brand ?? string.Empty, result.Last4 ?? string.Empty, result.Expiry ?? string.Empty);
        await _paymentMethodRepository.AddAsync(paymentMethod, ct);
        return paymentMethod;
    }

    public async Task<IReadOnlyList<PaymentMethod>> GetPaymentMethodsAsync(string buyerId, CancellationToken ct)
    {
        return await _paymentMethodRepository.ListAsync(new PaymentMethodsByBuyerSpecification(buyerId), ct);
    }

    public async Task DeletePaymentMethodAsync(string buyerId, Guid paymentMethodId, CancellationToken ct)
    {
        var paymentMethod = await _paymentMethodRepository.FirstOrDefaultAsync(new PaymentMethodByIdSpec(paymentMethodId), ct);
        if (paymentMethod is null || paymentMethod.BuyerId != buyerId)
        {
            throw new PaymentMethodNotFoundException();
        }

        await _payPalGateway.DeletePaymentTokenAsync(paymentMethod.PayPalPaymentTokenId, ct);
        await _paymentMethodRepository.DeleteAsync(paymentMethod, ct);
    }

    public async Task<IReadOnlyList<ReconciliationRow>> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var transactions = await _payPalGateway.SearchTransactionsAsync(from, to, ct);
        var orders = await _orderRepository.ListAsync(new OrdersInDateRangeSpec(from, to), ct);

        var ordersById = orders.ToDictionary(o => o.Id);
        var matchedOrderIds = new HashSet<int>();

        var rows = new List<ReconciliationRow>();

        foreach (var transaction in transactions)
        {
            var orderId = TryMatchOrderId(transaction, ordersById);
            if (orderId.HasValue)
            {
                matchedOrderIds.Add(orderId.Value);
            }

            var fee = transaction.Fee ?? 0m;
            rows.Add(new ReconciliationRow(
                transaction.TransactionId,
                transaction.ReferenceId,
                transaction.EventCode,
                transaction.Status,
                transaction.InitiationDate,
                transaction.Amount,
                transaction.Fee,
                transaction.Amount.HasValue ? transaction.Amount - fee : null,
                transaction.Currency,
                transaction.PayerEmail,
                orderId,
                orderId.HasValue ? ordersById[orderId.Value].Status.ToString() : null,
                orderId.HasValue ? ordersById[orderId.Value].Total() : null,
                orderId.HasValue ? "matched" : "paypal-only"));
        }

        foreach (var order in orders)
        {
            if (matchedOrderIds.Contains(order.Id))
            {
                continue;
            }

            rows.Add(new ReconciliationRow(
                null, null, null, order.Status.ToString(), order.OrderDate, null, null, null, order.Currency, null,
                order.Id, order.Status.ToString(), order.Total(), "eshop-only"));
        }

        return rows
            .OrderByDescending(r => r.Date ?? DateTimeOffset.MinValue)
            .ThenBy(r => r.OrderId ?? int.MaxValue)
            .ToList();
    }

    private static int? TryMatchOrderId(PayPalTransactionRecord transaction, IReadOnlyDictionary<int, Order> ordersById)
    {
        // The purchase unit's custom_id carries "eshop-order-{orderId}" and surfaces in the
        // reporting feed as custom_field — the reliable key back to an eShop order.
        if (!string.IsNullOrWhiteSpace(transaction.CustomField) &&
            transaction.CustomField.StartsWith("eshop-order-", StringComparison.OrdinalIgnoreCase))
        {
            var idPart = transaction.CustomField.Substring("eshop-order-".Length);
            if (int.TryParse(idPart, out var customOrderId) && ordersById.ContainsKey(customOrderId))
            {
                return customOrderId;
            }
        }

        if (int.TryParse(transaction.ReferenceId, out var referenceOrderId) && ordersById.ContainsKey(referenceOrderId))
        {
            return referenceOrderId;
        }

        return null;
    }

    private async Task RenewAuthorizationAsync(Order order, string authorizationId, CancellationToken ct)
    {
        try
        {
            var renewed = await _payPalGateway.ReauthorizeAsync(authorizationId, RequestId("reauth", order.Id), ct);
            order.UpdateAuthorization(renewed.Status, renewed.ExpirationTime);
            await _orderRepository.UpdateAsync(order, ct);
        }
        catch (PayPalApiException ex)
        {
            throw new AuthorizationCannotBeRenewedException(
                $"The authorization for order {order.Id} is stale and PayPal can no longer renew it (HTTP {ex.StatusCode}). The shopper must pay again.");
        }
    }

    private async Task<(PayPalCardDetails? Card, string? VaultId)> ResolvePaymentSourceAsync(string buyerId, OrderPaymentMethod payment, CancellationToken ct)
    {
        if (payment.Card is not null)
        {
            return (payment.Card, null);
        }

        if (payment.PaymentMethodId.HasValue)
        {
            var paymentMethod = await _paymentMethodRepository.FirstOrDefaultAsync(new PaymentMethodByIdSpec(payment.PaymentMethodId.Value), ct);
            if (paymentMethod is null || paymentMethod.BuyerId != buyerId)
            {
                throw new PaymentMethodNotFoundException();
            }

            return (null, paymentMethod.PayPalPaymentTokenId);
        }

        throw new InvalidOrderStateException("A card or a saved payment method is required to pay.", 400);
    }

    private static string RequestId(string purpose, int orderId)
    {
        return $"eshop-{InstanceId}-{purpose}-{orderId}";
    }

    private static string MerchantCustomerId(string buyerId)
    {
        // PayPal's vault endpoint rejects merchant_customer_id values that contain
        // characters such as '@'; derive a stable, safe identifier from the buyer id.
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(buyerId));
        return "eshop-" + Convert.ToHexString(hash).ToLowerInvariant()[..24];
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }

        return order;
    }

    private static async Task<T> WithOrderLockAsync<T>(int orderId, Func<Task<T>> action)
    {
        var semaphore = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync();
        try
        {
            return await action();
        }
        finally
        {
            semaphore.Release();
        }
    }
}