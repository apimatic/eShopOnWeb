using System;
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
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogRepository;
    private readonly IRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IPayPalGateway _gateway;
    private readonly string _currency;

    public OrderPaymentService(IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogRepository,
        IRepository<PaymentMethod> paymentMethodRepository,
        IPayPalGateway gateway,
        string currency)
    {
        _orderRepository = orderRepository;
        _catalogRepository = catalogRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _gateway = gateway;
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        _currency = currency;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, Address shipToAddress, IReadOnlyList<OrderItemRequest> items, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (items == null || items.Count == 0)
        {
            throw new ArgumentException("An order requires at least one catalog item.");
        }

        var requestedIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        if (items.Any(i => i.Quantity <= 0))
        {
            throw new ArgumentException("Item quantities must be greater than zero.");
        }

        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(requestedIds), ct);
        var orderItems = new List<OrderItem>();
        foreach (var item in items)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == item.CatalogItemId);
            if (catalogItem is null)
            {
                throw new ArgumentException($"Catalog item {item.CatalogItemId} does not exist.");
            }
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri ?? string.Empty);
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, item.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, ct);
        return order;
    }

    public async Task<Order> PayOrderAsync(string buyerId, int orderId, GatewayCard? card, int? paymentMethodId, CancellationToken ct)
    {
        var order = await GetOrderAsync(orderId, ct);
        if (order is null || order.BuyerId != buyerId)
        {
            throw new OrderNotFoundException(orderId);
        }

        if (card != null && paymentMethodId != null)
        {
            throw new ArgumentException("Provide either card details or a saved payment method id, not both.");
        }

        // Idempotency: a repeat pay on an order with a live authorization returns the existing payment.
        if (order.Status == OrderStatus.PaymentAuthorized &&
            order.Payment != null &&
            IsAuthorizationActive(order.Payment))
        {
            return order;
        }

        if (order.Status == OrderStatus.Fulfilled || order.Status == OrderStatus.Cancelled)
        {
            throw new OrderStateException($"Order {orderId} is {order.Status} and can no longer be paid.");
        }

        var amount = decimal.Round(order.Total(), 2, MidpointRounding.ToEven);
        if (amount <= 0)
        {
            throw new OrderStateException($"Order {orderId} has a zero total; there is nothing to authorize.");
        }

        var vaultTokenId = (string?)null;
        if (paymentMethodId != null)
        {
            var savedCard = await _paymentMethodRepository.GetByIdAsync(paymentMethodId.Value, ct);
            if (savedCard is null || savedCard.BuyerId != buyerId)
            {
                throw new PaymentMethodNotFoundException(paymentMethodId.Value);
            }
            vaultTokenId = savedCard.PayPalVaultId;
        }
        else if (card is null)
        {
            throw new ArgumentException("Provide either card details or a saved payment method id.");
        }

        // A fresh authorization attempt needs a fresh PayPal request id; the first attempt
        // uses a deterministic key (stable per order, unique across app runs, since it includes
        // the order's creation timestamp) so a transport-level resend replays the same result.
        var idempotencyKey = order.Payment is null
            ? $"eshop-auth-{orderId}-{order.OrderDate:yyyyMMddHHmmss}"
            : $"eshop-auth-{orderId}-{Guid.NewGuid():N}";

        var result = await _gateway.AuthorizeAsync(
            new GatewayAuthorizeRequest(new GatewayMoney(amount, _currency), card, vaultTokenId),
            idempotencyKey, ct);

        if (result.RequiresPayerAction)
        {
            throw new PaymentDeclinedException(
                "PayPal requires the cardholder to approve this payment in a browser (3-D Secure challenge); " +
                "it cannot be completed server-side.");
        }
        if (!result.Success || string.IsNullOrEmpty(result.AuthorizationId))
        {
            throw new PaymentDeclinedException(result.DeclineReason ?? "The card issuer declined the payment.");
        }

        if (order.Payment is null)
        {
            order.AttachPayment(new OrderPayment(order.Id, _currency, amount));
        }
        order.Payment!.RecordAuthorization(result.PayPalOrderId, result.AuthorizationId,
            result.Status ?? "CREATED", result.ExpiresAt, amount, paymentMethodId);

        await _orderRepository.UpdateAsync(order, ct);
        return order;
    }

    public async Task<Order> FulfilOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await GetOrderAsync(orderId, ct);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }
        if (order.Status == OrderStatus.Fulfilled)
        {
            return order; // already fulfilled and captured - idempotent
        }
        if (order.Status == OrderStatus.Cancelled)
        {
            throw new OrderStateException($"Order {orderId} is cancelled and cannot be fulfilled.");
        }
        var payment = order.Payment;
        if (payment?.PayPalAuthorizationId is null)
        {
            throw new OrderStateException($"Order {orderId} has no payment authorization; it must be paid before fulfilment.");
        }

        var amount = decimal.Round(order.Total(), 2, MidpointRounding.ToEven);
        var authorizationId = payment.PayPalAuthorizationId;

        // A stale authorization is renewed rather than failing the fulfilment outright.
        var current = await _gateway.GetAuthorizationAsync(authorizationId, ct);
        var isLive = current.Success &&
                     (current.Status == "CREATED" || current.Status == "PENDING") &&
                     (current.ExpiresAt is null || current.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1));

        if (!isLive)
        {
            var reauthorization = await _gateway.ReauthorizeAsync(
                authorizationId,
                new GatewayMoney(amount, _currency),
                $"eshop-reauth-{orderId}-{Guid.NewGuid():N}", ct);

            if (!reauthorization.Success || string.IsNullOrEmpty(reauthorization.AuthorizationId))
            {
                throw new OrderStateException(
                    $"The payment authorization for order {orderId} is no longer renewable " +
                    $"(status: {current.Status ?? "unknown"}, reason: {reauthorization.DeclineReason ?? current.DeclineReason ?? "none reported"}). " +
                    "No capture was attempted. Create a new payment for this order or handle the refund manually.");
            }
            authorizationId = reauthorization.AuthorizationId!;
            payment.RecordRenewal(authorizationId, reauthorization.Status ?? "CREATED", reauthorization.ExpiresAt);
        }

        var capture = await _gateway.CaptureAsync(
            authorizationId, new GatewayMoney(amount, _currency), $"eshop-capture-{orderId}-{order.OrderDate:yyyyMMddHHmmss}", ct);

        if (!capture.Success || string.IsNullOrEmpty(capture.CaptureId))
        {
            throw new PaymentDeclinedException(
                $"PayPal could not capture the payment for order {orderId}: {capture.DeclineReason ?? "unknown reason"}.");
        }

        payment.RecordCapture(capture.CaptureId!,
            capture.Status ?? "COMPLETED",
            capture.Amount?.Amount ?? amount,
            capture.Fee?.Amount,
            capture.NetAmount?.Amount);
        order.MarkFulfilled();

        await _orderRepository.UpdateAsync(order, ct);
        return order;
    }

    public async Task<Order> CancelOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await GetOrderAsync(orderId, ct);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }
        if (order.Status == OrderStatus.Cancelled)
        {
            return order; // idempotent
        }
        if (order.Status == OrderStatus.Fulfilled)
        {
            throw new OrderStateException($"Order {orderId} is already fulfilled; refund it instead of cancelling.");
        }

        var payment = order.Payment;
        if (payment?.PayPalAuthorizationId != null && payment.AuthorizationStatus != "VOIDED")
        {
            var current = await _gateway.GetAuthorizationAsync(payment.PayPalAuthorizationId, ct);
            if (current.Success && (current.Status == "CAPTURED" || current.Status == "PARTIALLY_CAPTURED"))
            {
                throw new OrderStateException($"Order {orderId}'s payment has already been captured; refund it instead.");
            }
            if (current.Success && current.Status != "VOIDED")
            {
                var voided = await _gateway.VoidAsync(payment.PayPalAuthorizationId, $"eshop-void-{orderId}-{Guid.NewGuid():N}", ct);
                if (!voided.Success)
                {
                    throw new PayPalGatewayException(
                        $"PayPal could not release the held funds for order {orderId}: {voided.DeclineReason ?? "unknown reason"}");
                }
            }
            payment.RecordVoid();
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, ct);
        return order;
    }

    public async Task<RefundOutcome> RefundOrderAsync(int orderId, decimal? amount, string idempotencyKey, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await GetOrderAsync(orderId, ct);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }
        var payment = order.Payment;
        if (payment?.CaptureStatus != "COMPLETED" || payment.CapturedAmount is null)
        {
            throw new OrderStateException($"Order {orderId} has no captured payment to refund.");
        }

        // Repeating a request under the same key must not refund twice.
        var existing = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing != null)
        {
            return new RefundOutcome(existing, payment.RefundedAmount, payment.RefundableAmount);
        }

        var refundable = payment.RefundableAmount;
        var refundAmount = decimal.Round(amount ?? refundable, 2, MidpointRounding.ToEven);
        if (refundAmount <= 0)
        {
            throw new OrderStateException("A refund amount must be greater than zero.");
        }
        if (refundAmount > refundable)
        {
            throw new OrderStateException(
                $"Refund of {refundAmount:0.00} {_currency} exceeds the refundable balance of {refundable:0.00} {_currency} " +
                $"for order {orderId} ({payment.CapturedAmount:0.00} captured, {payment.RefundedAmount:0.00} already refunded).");
        }

        // The caller's key is unique per capture; PayPal's request id must be unique per
        // client, so namespace the caller's key with the capture id. Repeating the same
        // request under the same key reaches PayPal with the same header -> replayed, not
        // executed twice.
        var result = await _gateway.RefundAsync(
            payment.PayPalCaptureId!,
            new GatewayMoney(refundAmount, _currency),
            $"eshop-refund-{payment.PayPalCaptureId}-{idempotencyKey}", ct);

        if (!result.Success || string.IsNullOrEmpty(result.RefundId))
        {
            throw new PayPalGatewayException(
                $"PayPal could not refund order {orderId}: {result.DeclineReason ?? "unknown reason"}");
        }

        var refund = new PaymentRefund(payment.Id, idempotencyKey, refundAmount, _currency);
        refund.RecordResult(result.RefundId, result.Status ?? PaymentRefund.CompletedStatus);
        payment.AddRefund(refund);

        await _orderRepository.UpdateAsync(order, ct);
        return new RefundOutcome(refund, payment.RefundedAmount, payment.RefundableAmount);
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _orderRepository.ListAsync(new BuyerOrdersWithPaymentsSpecification(buyerId), ct);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var transactions = await _gateway.SearchTransactionsAsync(from, to, ct);

        var orders = await _orderRepository.ListAsync(new AllOrdersWithPaymentsSpecification(), ct);
        var ordersWithActivity = orders
            .Where(o => o.Payment != null && HasPaymentActivityInRange(o.Payment, from, to))
            .ToList();

        var knownIds = new Dictionary<string, (int OrderId, string OrderStatus)>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in orders)
        {
            var payment = order.Payment;
            if (payment is null) continue;
            if (payment.PayPalOrderId != null) knownIds[payment.PayPalOrderId] = (order.Id, order.Status.ToString());
            if (payment.PayPalAuthorizationId != null) knownIds[payment.PayPalAuthorizationId] = (order.Id, order.Status.ToString());
            if (payment.PayPalCaptureId != null) knownIds[payment.PayPalCaptureId] = (order.Id, order.Status.ToString());
            foreach (var refund in payment.Refunds)
            {
                if (refund.PayPalRefundId != null) knownIds[refund.PayPalRefundId] = (order.Id, order.Status.ToString());
            }
        }

        var matchedOrderIds = new HashSet<int>();
        var entries = new List<ReconciliationEntry>();
        foreach (var tx in transactions)
        {
            int? orderId = knownIds.TryGetValue(tx.TransactionId, out var known) ? known.OrderId : null;
            if (orderId.HasValue) matchedOrderIds.Add(orderId.Value);
            entries.Add(new ReconciliationEntry(
                tx.TransactionId, tx.Status, tx.Amount, tx.InitiationDate, orderId,
                orderId.HasValue ? knownIds[tx.TransactionId].OrderStatus : null));
        }

        // Orders whose payment activity falls in the range but that PayPal reports no transaction for.
        var unmatched = ordersWithActivity
            .Where(o => !matchedOrderIds.Contains(o.Id))
            .Select(o => new ReconciliationEntry(
                $"order-{o.Id}", o.Status.ToString(),
                new GatewayMoney(decimal.Round(o.Total(), 2, MidpointRounding.ToEven), _currency),
                o.Payment!.CapturedAt ?? o.Payment.AuthorizedAt, o.Id, o.Status.ToString()))
            .ToList();

        return new ReconciliationReport(from, to, transactions.Count, entries, unmatched);
    }

    private static bool HasPaymentActivityInRange(OrderPayment payment, DateTimeOffset from, DateTimeOffset to)
    {
        return InRange(payment.AuthorizedAt, from, to)
            || (payment.CapturedAt.HasValue && InRange(payment.CapturedAt.Value, from, to))
            || payment.Refunds.Any(r => InRange(r.CreatedAt, from, to));
    }

    private static bool InRange(DateTimeOffset value, DateTimeOffset from, DateTimeOffset to) =>
        value >= from && value <= to;

    private static bool IsAuthorizationActive(OrderPayment payment) =>
        (payment.AuthorizationStatus == "CREATED" || payment.AuthorizationStatus == "PENDING") &&
        (payment.AuthorizationExpiresAt is null || payment.AuthorizationExpiresAt > DateTimeOffset.UtcNow);

    private async Task<Order?> GetOrderAsync(int orderId, CancellationToken ct) =>
        await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentSpecification(orderId), ct);
}
