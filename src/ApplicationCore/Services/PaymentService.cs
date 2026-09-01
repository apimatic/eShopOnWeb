using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the order/payment state machine against the payment gateway. All money
/// movement is idempotent in effect: state guards make repeats no-ops, and every provider
/// write goes out under a deterministic PayPal-Request-Id (or the caller's refund key).
/// </summary>
public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Entities.BuyerAggregate.SavedCard> _savedCardRepository;
    private readonly IPaymentGateway _gateway;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Entities.BuyerAggregate.SavedCard> savedCardRepository,
        IPaymentGateway gateway,
        PayPalSettings settings,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _itemRepository = itemRepository;
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _settings = settings;
        _logger = logger;
    }

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items, Address shipToAddress, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(items, nameof(items));
        if (items.Count == 0)
        {
            throw new OrderStateException("An order must contain at least one item.");
        }

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(items.Select(i => i.CatalogItemId).ToArray()), ct);

        var orderItems = new List<OrderItem>();
        foreach (var item in items)
        {
            Guard.Against.NegativeOrZero(item.Quantity, nameof(item.Quantity));
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == item.CatalogItemId)
                ?? throw new OrderStateException($"Catalog item {item.CatalogItemId} does not exist.");
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, item.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, orderItems);
        return await _orderRepository.AddAsync(order, ct);
    }

    public Task<Payment> PayWithCardAsync(string buyerId, int orderId, CardPaymentDetails card, CancellationToken ct)
    {
        Guard.Against.Null(card, nameof(card));
        return PayAsync(buyerId, orderId,
            (id, amount, currency, key, token) => _gateway.AuthorizeCardPaymentAsync(id, amount, currency, card, key, token), ct);
    }

    public async Task<Payment> PayWithSavedCardAsync(string buyerId, int orderId, int savedCardId, CancellationToken ct)
    {
        var savedCard = await _savedCardRepository.GetByIdAsync(savedCardId, ct);
        if (savedCard is null || savedCard.BuyerId != buyerId)
        {
            throw new OrderStateException($"Saved card {savedCardId} was not found.");
        }

        return await PayAsync(buyerId, orderId,
            (id, amount, currency, key, token) => _gateway.AuthorizeSavedCardPaymentAsync(id, amount, currency, savedCard.PayPalPaymentTokenId, key, token), ct);
    }

    private async Task<Payment> PayAsync(
        string buyerId,
        int orderId,
        Func<int, decimal, string, string, CancellationToken, Task<GatewayAuthorizationResult>> authorize,
        CancellationToken ct)
    {
        var order = await GetBuyerOrderAsync(buyerId, orderId, ct);

        if (order.Status == OrderStatus.PaymentAuthorized)
        {
            // Idempotent replay of a double-click: the hold already exists, return it.
            return (await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct))!;
        }
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new OrderStateException($"Order {orderId} is {order.Status} and cannot be paid.");
        }

        var amount = order.Total();
        var currency = _settings.Currency;

        var result = await authorize(orderId, amount, currency, $"eshop-authorize-order-{orderId}", ct);

        var payment = new Payment(orderId, buyerId, amount, currency);
        payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId, result.Status, result.ExpiresAt);
        await _paymentRepository.AddAsync(payment, ct);

        order.MarkPaymentAuthorized();
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation($"Order {orderId} authorized: hold {result.AuthorizationId} for {amount} {currency}.");
        return payment;
    }

    public async Task<Payment> FulfilOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct)
            ?? throw new OrderNotFoundException(orderId);

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct);

        if (order.Status == OrderStatus.Fulfilled)
        {
            // Idempotent replay: money was already taken; return what PayPal reported.
            return payment!;
        }
        if (order.Status != OrderStatus.PaymentAuthorized || payment is null || payment.AuthorizationId is null)
        {
            throw new OrderStateException($"Order {orderId} is {order.Status} and cannot be fulfilled.");
        }

        await EnsureAuthorizationUsableAsync(order, payment, ct);

        var capture = await _gateway.CaptureAsync(payment.AuthorizationId!, orderId, $"eshop-capture-order-{orderId}", ct);

        payment.MarkCaptured(capture.CaptureId, capture.GrossAmount, capture.Fee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, ct);

        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation($"Order {orderId} fulfilled: captured {capture.GrossAmount} (fee {capture.Fee}, net {capture.NetAmount}).");
        return payment;
    }

    private async Task EnsureAuthorizationUsableAsync(Order order, Payment payment, CancellationToken ct)
    {
        GatewayAuthorizationStatus? authorization = null;
        var unknown = false;
        try
        {
            authorization = await _gateway.GetAuthorizationAsync(payment.AuthorizationId!, ct);
        }
        catch (PaymentGatewayException)
        {
            // The authorization can no longer be read (gone, or the provider rejected the
            // lookup); treat it as stale and try to renew before giving up on the fulfilment.
            unknown = true;
        }

        var stale = unknown
            || authorization!.Status is "DENIED" or "VOIDED"
            || (authorization!.ExpiresAt is not null && authorization.ExpiresAt <= DateTimeOffset.UtcNow);

        if (!stale)
        {
            return;
        }

        _logger.LogInformation($"Authorization {payment.AuthorizationId} for order {order.Id} is stale; attempting reauthorization.");
        try
        {
            var renewed = await _gateway.ReauthorizeAsync(
                payment.AuthorizationId!, payment.Amount, payment.Currency, $"eshop-reauthorize-order-{order.Id}", ct);
            payment.MarkAuthorizationRenewed(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, ct);
        }
        catch (PaymentGatewayException ex)
        {
            payment.MarkRequiresNewAuthorization();
            await _paymentRepository.UpdateAsync(payment, ct);

            order.MarkAwaitingPayment();
            await _orderRepository.UpdateAsync(order, ct);

            throw new AuthorizationNotRenewableException(
                $"The PayPal authorization for order {order.Id} expired and can no longer be renewed " +
                "(PayPal allows reauthorization only within a limited window). The order was moved back to " +
                "awaiting payment — ask the shopper to pay again, then fulfil. " +
                $"Provider detail: {ex.Message}");
        }
    }

    public async Task<Order> CancelOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct)
            ?? throw new OrderNotFoundException(orderId);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }
        if (order.Status == OrderStatus.Fulfilled)
        {
            throw new OrderStateException($"Order {orderId} is fulfilled; issue a refund instead of cancelling.");
        }

        if (order.Status == OrderStatus.PaymentAuthorized)
        {
            var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct);
            if (payment?.AuthorizationId is not null && payment.Status == PaymentStatus.Authorized)
            {
                await _gateway.VoidAsync(payment.AuthorizationId, $"eshop-void-order-{orderId}", ct);
                payment.MarkVoided();
                await _paymentRepository.UpdateAsync(payment, ct);
            }
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation($"Order {orderId} cancelled; any held funds released.");
        return order;
    }

    public async Task<PaymentRefund> RefundOrderAsync(int orderId, decimal? amount, string idempotencyKey, string? note, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct)
            ?? throw new OrderNotFoundException(orderId);
        if (order.Status != OrderStatus.Fulfilled)
        {
            throw new OrderStateException($"Order {orderId} is {order.Status}; only fulfilled orders can be refunded.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct);
        if (payment?.CaptureId is null)
        {
            throw new OrderStateException($"Order {orderId} has no captured payment to refund.");
        }

        var existing = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing is not null)
        {
            // Same key replayed: return the original refund, never refund twice.
            return existing;
        }

        var refundAmount = amount ?? payment.RefundableRemaining;
        if (refundAmount <= 0m || refundAmount > payment.RefundableRemaining)
        {
            throw new OrderStateException(
                $"Refund of {refundAmount} {payment.Currency} exceeds the remaining refundable amount " +
                $"of {payment.RefundableRemaining} {payment.Currency} for order {orderId}.");
        }

        var result = await _gateway.RefundAsync(
            payment.CaptureId, orderId, refundAmount, payment.Currency, idempotencyKey, note, ct);

        var refund = payment.AddRefund(result.RefundId, idempotencyKey, result.Amount, result.Status);
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation($"Order {orderId} refunded {result.Amount} {payment.Currency} (refund {result.RefundId}).");
        return refund;
    }

    public async Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken ct)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        return orders.OrderByDescending(o => o.OrderDate).ToList();
    }

    public async Task<IReadOnlyList<Payment>> ListBuyerPaymentsAsync(string buyerId, CancellationToken ct)
    {
        return await _paymentRepository.ListAsync(new PaymentsByBuyerIdSpec(buyerId), ct);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (to < from)
        {
            throw new OrderStateException("'to' must not be earlier than 'from'.");
        }

        var transactions = await _gateway.SearchTransactionsAsync(from, to, ct);

        var payments = await _paymentRepository.ListAsync(ct);
        var inRange = payments
            .Where(p => (p.CreatedAt >= from && p.CreatedAt <= to) || (p.CapturedAt >= from && p.CapturedAt <= to))
            .ToList();

        var rows = new List<ReconciliationTransaction>();
        foreach (var t in transactions)
        {
            var match = MatchPayment(t, inRange);
            rows.Add(new ReconciliationTransaction(
                t.TransactionId, t.Status, t.Amount, t.Currency, t.Fee, t.InitiatedAt, t.UpdatedAt,
                t.InvoiceId, t.CustomField, t.PayPalReferenceId, match?.OrderId));
        }

        var seenIds = transactions.Select(t => t.TransactionId).Where(id => id is not null).ToHashSet();
        var missing = inRange
            .Where(p => p.CaptureId is not null && !seenIds.Contains(p.CaptureId)
                     && (p.AuthorizationId is null || !seenIds.Contains(p.AuthorizationId)))
            .Select(p => new ReconciliationLocalPayment(
                p.OrderId, p.PayPalOrderId, p.AuthorizationId, p.CaptureId, p.CapturedAmount, p.Currency,
                "Captured locally but not present in PayPal's transaction report for this range " +
                "(sandbox reporting lags live activity by up to a few hours)."))
            .ToList();

        return new ReconciliationReport(from, to, _settings.Currency, rows, missing);
    }

    private static Payment? MatchPayment(GatewayTransaction t, IReadOnlyCollection<Payment> candidates)
    {
        // Strongest first: the transaction IS a known capture/authorization, or references a known PayPal order.
        var byId = candidates.FirstOrDefault(p =>
            (t.TransactionId is not null && (p.CaptureId == t.TransactionId || p.AuthorizationId == t.TransactionId))
            || (t.PayPalReferenceId is not null && p.PayPalOrderId == t.PayPalReferenceId));
        if (byId is not null)
        {
            return byId;
        }

        // Then the order reference we stamp on every purchase unit / refund ("order-{id}",
        // with invoice ids carrying a uniqueness suffix after the id).
        var reference = t.CustomField ?? t.InvoiceId;
        if (reference is not null && reference.StartsWith("order-", StringComparison.Ordinal))
        {
            var digits = new string(reference.Substring("order-".Length).TakeWhile(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var referencedOrderId))
            {
                return candidates.FirstOrDefault(p => p.OrderId == referencedOrderId);
            }
        }

        return null;
    }

    private async Task<Order> GetBuyerOrderAsync(string buyerId, int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        // Existence is not leaked across shoppers: another buyer's order is simply "not found".
        if (order is null || order.BuyerId != buyerId)
        {
            throw new OrderNotFoundException(orderId);
        }
        return order;
    }
}
