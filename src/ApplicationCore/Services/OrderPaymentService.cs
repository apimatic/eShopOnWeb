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
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orders;
    private readonly IReadRepository<CatalogItem> _catalogItems;
    private readonly IReadRepository<Entities.PaymentMethodAggregate.SavedPaymentMethod> _savedCards;
    private readonly IPayPalGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly PerOrderLock _orderLock;
    private readonly PaymentRunContext _run;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orders,
        IReadRepository<CatalogItem> catalogItems,
        IReadRepository<Entities.PaymentMethodAggregate.SavedPaymentMethod> savedCards,
        IPayPalGateway gateway,
        IUriComposer uriComposer,
        PerOrderLock orderLock,
        PaymentRunContext run,
        IAppLogger<OrderPaymentService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _savedCards = savedCards;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _orderLock = orderLock;
        _run = run;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines, ShippingAddressInput? shipTo, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one line item.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new ArgumentException("Every order line must have a quantity greater than zero.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), ct);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var missing = ids.Where(id => !byId.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
        {
            throw new ArgumentException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var items = lines.Select(line =>
        {
            var catalogItem = byId[line.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = shipTo is null
            ? new Address("N/A", "N/A", "N/A", "N/A", "00000")
            : new Address(shipTo.Street, shipTo.City, shipTo.State, shipTo.Country, shipTo.ZipCode);

        var order = new Order(buyerId, address, items);
        await _orders.AddAsync(order, ct);

        _logger.LogInformation("Placed order {0} for buyer {1}; total {2}.", order.Id, buyerId, order.Total());
        return order;
    }

    public async Task<Order> PayAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId, CancellationToken ct)
    {
        using var _ = await _orderLock.AcquireAsync(orderId, ct);

        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdAndBuyerSpec(orderId, buyerId), ct)
            ?? throw new ResourceNotFoundException($"Order {orderId} was not found.");

        // Idempotent in effect: a double-click that arrives after the first authorization committed sees the
        // order already Authorized and returns it unchanged — no second hold is placed.
        if (order.Status == OrderStatus.Authorized)
        {
            return order;
        }
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentOperationException($"Order {orderId} cannot be paid because it is {order.Status}.");
        }

        string? vaultId = null;
        if (savedPaymentMethodId.HasValue)
        {
            var saved = await _savedCards.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdAndBuyerSpecification(savedPaymentMethodId.Value, buyerId), ct)
                ?? throw new ResourceNotFoundException($"Saved card {savedPaymentMethodId.Value} was not found.");
            vaultId = saved.VaultId;
        }
        else if (card is null)
        {
            throw new ArgumentException("Provide either card details or a saved card id to pay.");
        }

        var request = new AuthorizeRequest
        {
            Amount = order.Total(),
            Currency = _gateway.Currency,
            CustomId = _run.CustomId(orderId),
            InvoiceId = _run.InvoiceId(orderId),
            RequestIdSeed = _run.AuthorizeSeed(orderId),
            Card = vaultId is null ? card : null,
            VaultId = vaultId,
            Description = $"eShopOnWeb order {orderId}",
        };

        var auth = await _gateway.AuthorizeAsync(request, ct);

        var payment = new OrderPayment(
            auth.PayPalOrderId, auth.AuthorizationId, auth.Status, auth.AuthorizedAmount, auth.Currency, auth.ExpiresAt);
        order.MarkAuthorized(payment);
        await _orders.UpdateAsync(order, ct);

        _logger.LogInformation("Authorized order {0}: hold {1} for {2} {3}.", orderId, auth.AuthorizationId, auth.AuthorizedAmount, auth.Currency);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken ct)
    {
        using var _ = await _orderLock.AcquireAsync(orderId, ct);

        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct)
            ?? throw new ResourceNotFoundException($"Order {orderId} was not found.");

        if (order.Status == OrderStatus.Fulfilled)
        {
            return order; // idempotent
        }
        if (order.Status != OrderStatus.Authorized || order.Payment is null)
        {
            throw new PaymentOperationException($"Order {orderId} cannot be fulfilled because it is {order.Status}.");
        }

        var payment = order.Payment;
        var amount = order.Total();

        CaptureResult capture;
        try
        {
            capture = await _gateway.CaptureAsync(payment.AuthorizationId, amount, _run.CaptureKey(orderId), ct);
        }
        catch (PaymentGatewayException ex) when (ex.AuthorizationExpired)
        {
            // The hold went stale before fulfilment — renew it rather than failing the fulfilment outright.
            _logger.LogWarning("Authorization {0} for order {1} is stale; re-authorizing before capture.", payment.AuthorizationId, orderId);
            var reauth = await _gateway.ReauthorizeAsync(payment.AuthorizationId, amount, _run.ReauthorizeKey(orderId), ct);
            payment.RenewAuthorization(reauth.AuthorizationId, reauth.Status, reauth.AuthorizedAmount, reauth.ExpiresAt);
            await _orders.UpdateAsync(order, ct); // persist the renewed hold id before re-capturing

            capture = await _gateway.CaptureAsync(reauth.AuthorizationId, amount, _run.CaptureKey(orderId) + "-r", ct);
        }

        payment.RecordCapture(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PayPalFee, capture.NetAmount);
        order.MarkFulfilled();
        await _orders.UpdateAsync(order, ct);

        _logger.LogInformation("Fulfilled order {0}: captured {1} {2} (fee {3}, net {4}) via capture {5}.",
            orderId, capture.CapturedAmount, capture.Currency, capture.PayPalFee, capture.NetAmount, capture.CaptureId);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken ct)
    {
        using var _ = await _orderLock.AcquireAsync(orderId, ct);

        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct)
            ?? throw new ResourceNotFoundException($"Order {orderId} was not found.");

        if (order.Status == OrderStatus.Cancelled)
        {
            return order; // idempotent
        }
        if (order.Status != OrderStatus.Authorized || order.Payment is null)
        {
            throw new PaymentOperationException(
                $"Order {orderId} cannot be cancelled because it is {order.Status}. Cancellation releases a hold before fulfilment.");
        }

        await _gateway.VoidAsync(order.Payment.AuthorizationId, _run.VoidKey(orderId), ct);
        order.MarkCancelled();
        await _orders.UpdateAsync(order, ct);

        _logger.LogInformation("Cancelled order {0}: released hold {1}.", orderId, order.Payment.AuthorizationId);
        return order;
    }

    public async Task<RefundOutcome> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        using var _ = await _orderLock.AcquireAsync(orderId, ct);

        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdAndBuyerSpec(orderId, buyerId), ct)
            ?? throw new ResourceNotFoundException($"Order {orderId} was not found.");

        if (order.Payment?.CaptureId is null)
        {
            throw new PaymentOperationException($"Order {orderId} cannot be refunded because it has not been captured (it is {order.Status}).");
        }

        var payment = order.Payment;

        // Idempotent: a repeat under the same key returns the same refund without refunding again.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return new RefundOutcome(order, existing);
        }

        var remaining = payment.RefundableRemaining();
        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0m)
        {
            throw new PaymentOperationException($"Order {orderId} has no remaining refundable amount.");
        }
        if (refundAmount > remaining)
        {
            throw new PaymentOperationException(
                $"Refund of {refundAmount:0.00} exceeds the remaining refundable amount of {remaining:0.00}.");
        }

        // Dedup on the caller's raw key (above); send PayPal a run-unique request id so it is never a
        // merchant-wide duplicate across runs.
        var result = await _gateway.RefundAsync(payment.CaptureId!, refundAmount, _run.RefundRequestId(orderId, idempotencyKey), ct);

        var refund = payment.AddRefund(result.RefundId, refundAmount, result.Status, idempotencyKey);
        order.ReflectRefundState();
        await _orders.UpdateAsync(order, ct);

        _logger.LogInformation("Refunded order {0}: {1} {2} via refund {3}; order now {4}.",
            orderId, refundAmount, result.Currency, result.RefundId, order.Status);
        return new RefundOutcome(order, refund);
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        return await _orders.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), ct);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (to <= from)
        {
            throw new ArgumentException("'to' must be after 'from'.");
        }

        var transactions = await _gateway.SearchTransactionsAsync(from, to, ct);

        var allOrders = await _orders.ListAsync(ct);
        var capturedOrders = allOrders
            .Where(o => o.Payment?.CaptureId is not null)
            .ToDictionary(o => o.Id);

        var matched = new List<MatchedTransaction>();
        var inPayPalNotInEShop = new List<PayPalTransaction>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in transactions)
        {
            var orderId = PaymentRunContext.TryParseOrderIdFromCustomId(txn.CustomId);
            if (orderId.HasValue && capturedOrders.TryGetValue(orderId.Value, out var order))
            {
                matchedOrderIds.Add(orderId.Value);
                matched.Add(new MatchedTransaction(
                    orderId.Value, txn.TransactionId, txn.Status, txn.Amount, txn.Currency,
                    order.Payment!.CapturedAmount ?? 0m, order.Status));
            }
            else
            {
                inPayPalNotInEShop.Add(txn);
            }
        }

        var inEShopNotInPayPal = capturedOrders.Values
            .Where(o => !matchedOrderIds.Contains(o.Id))
            .Select(o => new UnmatchedOrder(
                o.Id, o.Payment!.CapturedAmount ?? 0m, o.Payment!.Currency, o.Status, o.Payment!.CaptureId))
            .ToList();

        return new ReconciliationReport(
            from, to, transactions.Count, capturedOrders.Count, transactions.Count == 0,
            matched, inPayPalNotInEShop, inEShopNotInPayPal);
    }
}
