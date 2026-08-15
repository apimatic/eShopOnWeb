using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPayPalGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly IPaymentOperationLock _lock;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> catalogItemRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IPayPalGateway gateway,
        IUriComposer uriComposer,
        IPaymentOperationLock paymentOperationLock,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _catalogItemRepository = catalogItemRepository;
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _lock = paymentOperationLock;
        _logger = logger;
    }

    private static string OrderKey(int orderId) => $"order:{orderId}";

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines,
        ShippingAddressInput? shipTo, CancellationToken cancellationToken = default)
    {
        if (lines == null || lines.Count == 0)
            throw new PaymentValidationException("An order must contain at least one line.");
        if (lines.Any(l => l.Quantity <= 0))
            throw new PaymentValidationException("Every order line must have a quantity of at least 1.");

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        if (catalogItems.Count != ids.Length)
        {
            var missing = ids.Except(catalogItems.Select(c => c.Id));
            throw new EntityNotFoundException($"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var items = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            // Price comes from the catalog, never from the caller.
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = shipTo != null
            ? new Address(shipTo.Street, shipTo.City, shipTo.State, shipTo.Country, shipTo.ZipCode)
            : new Address("N/A", "N/A", "N/A", "N/A", "00000");

        var order = new Order(buyerId, address, items);
        await _orderRepository.AddAsync(order, cancellationToken);
        _logger.LogInformation("Order {0} placed for {1} ({2} line(s), total {3}).",
            order.Id, buyerId, items.Count, order.Total());
        return order.Id;
    }

    public async Task<Order> AuthorizeAsync(string buyerId, int orderId, PayOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        using var _ = await _lock.AcquireAsync(OrderKey(orderId), cancellationToken);

        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderByIdForBuyerSpecification(orderId, buyerId), cancellationToken)
            ?? throw new EntityNotFoundException($"Order {orderId} was not found.");

        // Idempotent: a hold is already in place, don't authorize again.
        if (order.PaymentStatus == OrderPaymentStatus.Authorized)
            return order;

        if (order.PaymentStatus != OrderPaymentStatus.AwaitingPayment)
            throw new OrderPaymentException(
                $"Order {orderId} cannot be paid because it is '{order.PaymentStatus}'.");

        // Resolve the payment source: exactly one of a one-off card or a saved card.
        PayPalCardInput? card = command.Card;
        string? vaultId = null;
        int? savedId = null;

        if (command.SavedPaymentMethodId.HasValue)
        {
            if (card != null)
                throw new PaymentValidationException("Provide either card details or a saved card id, not both.");

            var saved = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdForBuyerSpecification(command.SavedPaymentMethodId.Value, buyerId),
                cancellationToken)
                ?? throw new EntityNotFoundException(
                    $"Saved card {command.SavedPaymentMethodId.Value} was not found.");

            vaultId = saved.PayPalVaultId;
            savedId = saved.Id;
        }
        else if (card == null)
        {
            throw new PaymentValidationException("Provide either card details or a saved card id.");
        }

        var amount = order.Total();
        if (amount <= 0m)
            throw new PaymentValidationException(
                $"Order {orderId} has a non-positive total and cannot be paid.");

        // Fresh request id per invocation: idempotent against a transport-level resend of THIS call,
        // while a genuine retry after a decline gets a new id. Concurrent double-clicks are held off
        // by the per-order lock and the persisted state check above.
        var authCommand = new PayPalAuthorizeCommand(
            amount, order.Id.ToString(), Guid.NewGuid().ToString(), card, vaultId);
        var auth = await _gateway.AuthorizeAsync(authCommand, cancellationToken);

        order.SetAuthorized(auth.PayPalOrderId, auth.AuthorizationId, auth.Currency, auth.ExpiresAt, savedId);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Order {0} authorized for {1} {2}.", order.Id, amount, auth.Currency);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        using var _ = await _lock.AcquireAsync(OrderKey(orderId), cancellationToken);

        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderWithPaymentByIdSpecification(orderId), cancellationToken)
            ?? throw new EntityNotFoundException($"Order {orderId} was not found.");

        // Idempotent: already captured.
        if (order.PaymentStatus == OrderPaymentStatus.Fulfilled)
            return order;

        if (order.PaymentStatus != OrderPaymentStatus.Authorized)
            throw new OrderPaymentException(
                $"Order {orderId} cannot be fulfilled because it is '{order.PaymentStatus}'.");

        var amount = order.Total();
        var authId = order.PayPalAuthorizationId!;

        // Renew a stale hold before capturing, rather than letting the capture fail outright.
        if (order.AuthorizationExpiresAt.HasValue &&
            DateTimeOffset.UtcNow >= order.AuthorizationExpiresAt.Value)
        {
            authId = await RenewAuthorizationAsync(order, amount, cancellationToken);
        }

        PayPalCaptureResult capture;
        try
        {
            capture = await _gateway.CaptureAsync(authId, Guid.NewGuid().ToString(), cancellationToken);
        }
        catch (AuthorizationExpiredException)
        {
            // The hold expired between our check and the capture — renew once and retry.
            authId = await RenewAuthorizationAsync(order, amount, cancellationToken);
            capture = await _gateway.CaptureAsync(authId, Guid.NewGuid().ToString(), cancellationToken);
        }

        order.SetFulfilled(capture.CaptureId, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Order {0} fulfilled; captured {1} {2} (fee {3}, net {4}).",
            order.Id, capture.GrossAmount, capture.Currency, capture.PayPalFee, capture.NetAmount);
        return order;
    }

    private async Task<string> RenewAuthorizationAsync(Order order, decimal amount, CancellationToken cancellationToken)
    {
        try
        {
            var renewed = await _gateway.ReauthorizeAsync(order.PayPalAuthorizationId!, amount, cancellationToken);
            order.SetReauthorized(renewed.AuthorizationId, renewed.ExpiresAt);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation("Order {0} authorization renewed ({1}).", order.Id, renewed.AuthorizationId);
            return renewed.AuthorizationId;
        }
        catch (PayPalGatewayException ex)
        {
            throw new AuthorizationNotRenewableException(
                $"The authorization for order {order.Id} has expired and can no longer be renewed. " +
                "Ask the shopper to place and pay for the order again.", ex);
        }
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        using var _ = await _lock.AcquireAsync(OrderKey(orderId), cancellationToken);

        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderWithPaymentByIdSpecification(orderId), cancellationToken)
            ?? throw new EntityNotFoundException($"Order {orderId} was not found.");

        // Idempotent: already cancelled.
        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
            return order;

        if (order.PaymentStatus == OrderPaymentStatus.Authorized)
        {
            // Release the hold so the shopper's money is freed.
            await _gateway.VoidAsync(order.PayPalAuthorizationId!, Guid.NewGuid().ToString(), cancellationToken);
        }
        else if (order.PaymentStatus != OrderPaymentStatus.AwaitingPayment)
        {
            throw new OrderPaymentException(
                $"Order {orderId} cannot be cancelled because it is '{order.PaymentStatus}'. " +
                "Use a refund for a fulfilled order.");
        }

        order.SetCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Order {0} cancelled.", order.Id);
        return order;
    }

    public async Task<(Order Order, OrderRefund Refund)> RefundAsync(string buyerId, int orderId,
        decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new PaymentValidationException("A refund idempotency key is required.");

        using var _ = await _lock.AcquireAsync(OrderKey(orderId), cancellationToken);

        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderByIdForBuyerSpecification(orderId, buyerId), cancellationToken)
            ?? throw new EntityNotFoundException($"Order {orderId} was not found.");

        // Idempotent replay: the same key returns the same refund without refunding again.
        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
            return (order, existing);

        if (order.PaymentStatus != OrderPaymentStatus.Fulfilled &&
            order.PaymentStatus != OrderPaymentStatus.PartiallyRefunded)
            throw new OrderPaymentException(
                $"Order {orderId} cannot be refunded because it is '{order.PaymentStatus}'.");

        var remaining = order.RefundableRemaining();
        if (remaining <= 0m)
            throw new OrderPaymentException($"Order {orderId} has nothing left to refund.");

        var effective = amount ?? remaining;
        if (effective <= 0m)
            throw new PaymentValidationException("Refund amount must be positive.");
        if (effective > remaining)
            throw new OrderPaymentException(
                $"Refund of {effective} exceeds the remaining refundable amount {remaining} for order {orderId}.");

        var result = await _gateway.RefundAsync(order.PayPalCaptureId!, effective, idempotencyKey, cancellationToken);
        var refund = order.AddRefund(idempotencyKey, result.RefundId, result.Amount, result.Status);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Order {0} refunded {1} {2} (refund {3}).",
            order.Id, result.Amount, result.Currency, result.RefundId);
        return (order, refund);
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(
            new CustomerOrdersWithPaymentSpecification(buyerId), cancellationToken);
        return orders;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        // PayPal's own ledger for the range, paged through in full by the gateway.
        var entries = await _gateway.SearchTransactionsAsync(from, to, cancellationToken);
        var localOrders = await _orderRepository.ListAsync(
            new OrdersWithCaptureSpecification(from, to), cancellationToken);

        // Index eShop's money-moving references (capture ids and refund ids) back to their order.
        var eshopByTxn = new Dictionary<string, Order>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in localOrders)
        {
            if (o.PayPalCaptureId != null) eshopByTxn[o.PayPalCaptureId] = o;
            foreach (var r in o.Refunds) eshopByTxn[r.PayPalRefundId] = o;
        }

        var paypalIds = new HashSet<string>(
            entries.Select(e => e.TransactionId), StringComparer.OrdinalIgnoreCase);

        var matched = new List<ReconciliationLine>();
        var inPayPalOnly = new List<ReconciliationLine>();
        foreach (var e in entries)
        {
            if (eshopByTxn.TryGetValue(e.TransactionId, out var o))
                matched.Add(new ReconciliationLine(
                    e.TransactionId, e.Status, e.Amount, e.Currency, o.Id, o.CapturedAmount, "Matched"));
            else
                inPayPalOnly.Add(new ReconciliationLine(
                    e.TransactionId, e.Status, e.Amount, e.Currency, null, null, "InPayPalNotInEShop"));
        }

        var inEShopOnly = new List<ReconciliationLine>();
        foreach (var o in localOrders)
        {
            if (o.PayPalCaptureId != null && !paypalIds.Contains(o.PayPalCaptureId))
                inEShopOnly.Add(new ReconciliationLine(
                    o.PayPalCaptureId, o.PaymentStatus.ToString(), o.CapturedAmount, o.PaymentCurrency,
                    o.Id, o.CapturedAmount, "InEShopNotInPayPal"));
        }

        return new ReconciliationReport(
            from, to, entries.Count, localOrders.Count, matched, inPayPalOnly, inEShopOnly);
    }
}
