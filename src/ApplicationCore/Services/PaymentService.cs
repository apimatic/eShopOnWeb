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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IAppLogger<PaymentService> _logger;
    private readonly string _currency;

    // Serializes payment state transitions per order, so a double-click or concurrent retry
    // cannot pass the state guard twice and hold the shopper twice.
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPaymentGateway paymentGateway,
        PayPalSettings payPalSettings,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _paymentGateway = paymentGateway;
        _logger = logger;
        _currency = string.IsNullOrWhiteSpace(payPalSettings.Currency) ? "USD" : payPalSettings.Currency;
    }

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items, Address shipToAddress,
        CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items.Count == 0)
        {
            throw new PaymentStateException("An order must contain at least one item.");
        }
        if (items.Any(i => i.Units <= 0))
        {
            throw new PaymentStateException("Item quantities must be positive.");
        }

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(items.Select(i => i.CatalogItemId).ToArray()), ct);

        var orderItems = new List<OrderItem>();
        foreach (var requested in items)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == requested.CatalogItemId)
                ?? throw new PaymentStateException($"Catalog item {requested.CatalogItemId} does not exist.");
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, requested.Units));
        }

        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orderRepository.AddAsync(order, ct);
        return order;
    }

    public async Task<Order?> PayOrderAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId,
        CancellationToken ct = default)
    {
        var orderLock = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await orderLock.WaitAsync(ct);
        try
        {
            var order = await GetOwnedOrderAsync(buyerId, orderId, ct);
            if (order is null)
            {
                return null;
            }

            // Idempotent in effect: a repeated pay on an authorized order is a no-op returning current state.
            if (order.Status == OrderStatus.PaymentAuthorized)
            {
                return order;
            }
            if (order.Status != OrderStatus.PendingPayment)
            {
                throw new PaymentStateException($"Order {order.Id} cannot be paid (current state: {order.Status}).");
            }
            if (card is null && savedPaymentMethodId is null)
            {
                throw new PaymentStateException("Payment requires either card details or a saved payment method id.");
            }

            string? vaultTokenId = null;
            if (savedPaymentMethodId is not null)
            {
                var savedCards = await _paymentMethodRepository.ListAsync(
                    new SavedPaymentMethodsByBuyerSpecification(buyerId), ct);
                var savedCard = savedCards.FirstOrDefault(c => c.Id == savedPaymentMethodId.Value)
                    ?? throw new PaymentStateException($"Saved payment method {savedPaymentMethodId} was not found for this shopper.");
                vaultTokenId = savedCard.PayPalPaymentTokenId;
            }

            var total = order.Total();
            var referenceId = order.Id.ToString();
            // Invoice ids are unique per merchant at PayPal; a fresh one per payment attempt keeps a
            // retried pay from colliding with an earlier attempt (or an unrelated order) there.
            var invoiceId = $"eshop-order-{order.Id}-{Guid.NewGuid():N}";
            order.AssignPaymentInvoiceId(invoiceId);

            // Attempt-scoped idempotency keys: a transport retry within this attempt replays at the
            // provider, while a brand-new pay attempt never replays a previous attempt's response.
            var created = await _paymentGateway.CreateOrderAsync(total, _currency, referenceId, invoiceId,
                referenceId, $"{invoiceId}-create", ct);
            if (created.PayerActionRequired)
            {
                throw new PaymentDeclinedException(
                    "The card requires buyer authentication (3DS) in a browser, which this integration does not support.");
            }

            GatewayAuthorization authorization;
            if (created.Authorization is not null)
            {
                // The provider authorized the order at create time; no separate authorize call.
                authorization = created.Authorization;
            }
            else
            {
                try
                {
                    authorization = await _paymentGateway.AuthorizeOrderAsync(created.PayPalOrderId,
                        card, vaultTokenId, $"{invoiceId}-authorize", ct);
                }
                catch
                {
                    _logger.LogWarning($"Authorization failed for order {order.Id} (PayPal order {created.PayPalOrderId}).");
                    throw;
                }
            }

            if (authorization.PayerActionRequired)
            {
                throw new PaymentDeclinedException(
                    "The card requires buyer authentication (3DS) in a browser, which this integration does not support.");
            }

            order.RecordAuthorization(created.PayPalOrderId, authorization.AuthorizationId,
                authorization.Status, authorization.ExpiresAt, _currency);
            await _orderRepository.UpdateAsync(order, ct);
            return order;
        }
        finally
        {
            orderLock.Release();
        }
    }

    public async Task<Order?> FulfilOrderAsync(int orderId, CancellationToken ct = default)
    {
        var orderLock = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await orderLock.WaitAsync(ct);
        try
        {
            var order = await _orderRepository.FirstOrDefaultAsync(new OrderByIdWithDetailsSpecification(orderId), ct);
            if (order is null)
            {
                return null;
            }

            // Idempotent: fulfilling an already-fulfilled order returns the captured state.
            if (order.Status == OrderStatus.Fulfilled)
            {
                return order;
            }
            if (order.Status != OrderStatus.PaymentAuthorized || order.AuthorizationId is null)
            {
                throw new PaymentStateException($"Order {order.Id} cannot be fulfilled (current state: {order.Status}).");
            }

            var authorization = await _paymentGateway.GetAuthorizationAsync(order.AuthorizationId, ct);
            if (IsStale(authorization))
            {
                GatewayAuthorizationState renewed;
                try
                {
                    renewed = await _paymentGateway.ReauthorizeAsync(order.AuthorizationId, order.Total(),
                        order.Currency ?? _currency, $"{order.PaymentInvoiceId}-reauthorize", ct);
                }
                catch (PaymentGatewayException ex) when (ex.ProviderStatusCode is >= 400 and < 500)
                {
                    throw new PaymentStateException(
                        $"The payment hold on order {order.Id} has expired and PayPal can no longer renew it. " +
                        "Void this order and ask the shopper to pay again so a fresh hold can be placed.");
                }

                order.RecordRenewedAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            }
            else
            {
                order.RecordRenewedAuthorization(authorization.AuthorizationId, authorization.Status, authorization.ExpiresAt);
            }

            var capture = await _paymentGateway.CaptureAuthorizationAsync(order.AuthorizationId!,
                order.PaymentInvoiceId ?? $"eshop-order-{order.Id}", $"{order.PaymentInvoiceId}-capture", ct);

            order.RecordCapture(capture.CaptureId, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
            await _orderRepository.UpdateAsync(order, ct);
            return order;
        }
        finally
        {
            orderLock.Release();
        }
    }

    public async Task<Order?> CancelOrderAsync(int orderId, CancellationToken ct = default)
    {
        var orderLock = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await orderLock.WaitAsync(ct);
        try
        {
            var order = await _orderRepository.FirstOrDefaultAsync(new OrderByIdWithDetailsSpecification(orderId), ct);
            if (order is null)
            {
                return null;
            }

            if (order.Status == OrderStatus.Cancelled)
            {
                return order;
            }
            if (order.Status != OrderStatus.PendingPayment && order.Status != OrderStatus.PaymentAuthorized)
            {
                throw new PaymentStateException(
                    $"Order {order.Id} has already been fulfilled; issue a refund instead of cancelling.");
            }

            // Releasing the hold means no money ever moves for this order.
            if (order.AuthorizationId is not null)
            {
                await _paymentGateway.VoidAuthorizationAsync(order.AuthorizationId, $"{order.PaymentInvoiceId}-void", ct);
            }

            order.MarkCancelled();
            await _orderRepository.UpdateAsync(order, ct);
            return order;
        }
        finally
        {
            orderLock.Release();
        }
    }

    public async Task<OrderRefund?> RefundOrderAsync(int orderId, decimal? amount, string idempotencyKey,
        CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var orderLock = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await orderLock.WaitAsync(ct);
        try
        {
            var order = await _orderRepository.FirstOrDefaultAsync(new OrderByIdWithDetailsSpecification(orderId), ct);
            if (order is null)
            {
                return null;
            }

            // Caller-supplied idempotency key: a repeat under the same key returns the original refund.
            var existing = order.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
            if (existing is not null)
            {
                return existing;
            }

            if (order.Status != OrderStatus.Fulfilled && order.Status != OrderStatus.PartiallyRefunded)
            {
                throw new PaymentStateException($"Order {order.Id} has no captured payment to refund (current state: {order.Status}).");
            }

            var isFullRefundOfUntouchedCapture = amount is null && order.RefundedAmount == 0m;
            var refundAmount = amount ?? order.RefundableAmount;
            if (refundAmount <= 0m || refundAmount > order.RefundableAmount)
            {
                throw new PaymentStateException(
                    $"Refund of {refundAmount:0.00} exceeds the remaining refundable amount {order.RefundableAmount:0.00} on order {order.Id}.");
            }

            // The caller's key dedupes locally (above); the provider key is namespaced so it cannot
            // collide with unrelated integrations sharing the same merchant account.
            var refund = await _paymentGateway.RefundCaptureAsync(order.CaptureId!,
                isFullRefundOfUntouchedCapture ? null : refundAmount,
                order.Currency ?? _currency, $"eshop-refund-{idempotencyKey}", ct);

            var recorded = order.RegisterRefund(refund.RefundId, refundAmount, refund.Status, idempotencyKey);
            await _orderRepository.UpdateAsync(order, ct);
            return recorded;
        }
        finally
        {
            orderLock.Release();
        }
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithDetailsSpecification(buyerId), ct);
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var existingCards = await _paymentMethodRepository.ListAsync(
            new SavedPaymentMethodsByBuyerSpecification(buyerId), ct);
        var payPalCustomerId = existingCards.FirstOrDefault(c => c.PayPalCustomerId is not null)?.PayPalCustomerId;

        var vaulted = await _paymentGateway.VaultCardAsync(card, payPalCustomerId, buyerId,
            $"eshop-vault-{Guid.NewGuid():N}", ct);

        var saved = new SavedPaymentMethod(buyerId, vaulted.PaymentTokenId,
            vaulted.PayPalCustomerId ?? payPalCustomerId, vaulted.Brand, vaulted.LastDigits,
            vaulted.Expiry, vaulted.CardholderName);
        await _paymentMethodRepository.AddAsync(saved, ct);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> GetSavedCardsAsync(string buyerId, CancellationToken ct = default)
    {
        return await _paymentMethodRepository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), ct);
    }

    public async Task<bool> DeleteSavedCardAsync(string buyerId, int savedPaymentMethodId, CancellationToken ct = default)
    {
        var savedCards = await _paymentMethodRepository.ListAsync(
            new SavedPaymentMethodsByBuyerSpecification(buyerId), ct);
        var savedCard = savedCards.FirstOrDefault(c => c.Id == savedPaymentMethodId);
        if (savedCard is null)
        {
            return false;
        }

        await _paymentGateway.DeleteVaultedCardAsync(savedCard.PayPalPaymentTokenId, ct);
        await _paymentMethodRepository.DeleteAsync(savedCard, ct);
        return true;
    }

    public async Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        if (to <= from)
        {
            throw new PaymentStateException("The reconciliation range is empty: 'to' must be after 'from'.");
        }

        var transactions = await _paymentGateway.SearchTransactionsAsync(from, to, ct);
        var paidOrders = await _orderRepository.ListAsync(new PaidOrdersSpecification(), ct);

        var entries = new List<ReconciliationEntry>();
        var matchedOrderIds = new HashSet<int>();
        foreach (var transaction in transactions)
        {
            var match = paidOrders.FirstOrDefault(o => Matches(o, transaction));
            if (match is not null)
            {
                matchedOrderIds.Add(match.Id);
            }
            entries.Add(new ReconciliationEntry(transaction, match?.Id));
        }

        var missing = paidOrders
            .Where(o => !matchedOrderIds.Contains(o.Id))
            .Select(o => o.Id)
            .ToList();

        return new ReconciliationReport(from, to, entries, missing, transactions.Count);
    }

    private static bool Matches(Order order, GatewayTransaction transaction)
    {
        if (!string.IsNullOrEmpty(transaction.ReferenceId) &&
            (transaction.ReferenceId == order.PayPalOrderId ||
             transaction.ReferenceId == order.AuthorizationId ||
             transaction.ReferenceId == order.CaptureId))
        {
            return true;
        }
        if (!string.IsNullOrEmpty(transaction.TransactionId) &&
            (transaction.TransactionId == order.CaptureId ||
             transaction.TransactionId == order.AuthorizationId ||
             order.Refunds.Any(r => r.PayPalRefundId == transaction.TransactionId)))
        {
            return true;
        }
        if (!string.IsNullOrEmpty(order.PaymentInvoiceId) && transaction.InvoiceId == order.PaymentInvoiceId)
        {
            return true;
        }
        return false;
    }

    private static bool IsStale(GatewayAuthorizationState authorization)
    {
        if (authorization.ExpiresAt is not null && authorization.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return true;
        }
        // Only a live hold can be captured; anything else needs renewal first.
        return !string.Equals(authorization.Status, "CREATED", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(authorization.Status, "PENDING", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Order?> GetOwnedOrderAsync(string buyerId, int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderByIdWithDetailsSpecification(orderId), ct);
        // Ownership scoping: another shopper's order is indistinguishable from a missing one.
        return order is null || order.BuyerId != buyerId ? null : order;
    }
}
