using System;
using System.Collections.Generic;
using System.Globalization;
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
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the paid-order lifecycle: place, authorize (hold), fulfil (capture),
/// cancel (void), refund, and reconcile. Domain invariants live on the entities; this
/// service coordinates them with PayPal via <see cref="IPayPalGateway"/>.
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPayPalGateway _payPalGateway;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalSettings _settings;
    private readonly KeyedAsyncLock _orderLock;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Buyer> buyerRepository,
        IPayPalGateway payPalGateway,
        IUriComposer uriComposer,
        PayPalSettings settings,
        KeyedAsyncLock orderLock)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _itemRepository = itemRepository;
        _buyerRepository = buyerRepository;
        _payPalGateway = payPalGateway;
        _uriComposer = uriComposer;
        _settings = settings;
        _orderLock = orderLock;
    }

    private string Currency => _settings.Currency;

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<PlaceOrderItem> items, Address? shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items is null || items.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one item.", nameof(items));
        }
        if (items.Any(i => i.Quantity <= 0))
        {
            throw new ArgumentException("Every order item must have a quantity greater than zero.", nameof(items));
        }

        var catalogIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogIds), cancellationToken);

        var orderItems = new List<OrderItem>();
        foreach (var line in items)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new ArgumentException($"Catalog item {line.CatalogItemId} does not exist.", nameof(items));

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress ?? DefaultAddress(), orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order.Id;
    }

    public async Task<Payment> AuthorizeAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId, CancellationToken cancellationToken = default)
    {
        // Serialize money-moving operations for this order so a double-click cannot authorize twice.
        using var gate = await _orderLock.LockAsync($"order-{orderId}", cancellationToken);
        var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);

        // Idempotency: never authorize twice for a double-click.
        if (payment is not null)
        {
            if (payment.IsAuthorized)
            {
                return payment;
            }
            if (payment.State is PaymentState.Captured or PaymentState.PartiallyRefunded or PaymentState.Refunded)
            {
                throw new InvalidOrderStateException("This order has already been paid.");
            }
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidOrderStateException($"Order {orderId} cannot be paid from state {order.Status}.");
        }

        var instrument = await ResolveInstrumentAsync(buyerId, card, savedPaymentMethodId, cancellationToken);

        var amount = order.Total();
        if (amount <= 0)
        {
            throw new InvalidOrderStateException("The order total must be greater than zero to authorize a payment.");
        }

        var idempotencyKey = $"{RuntimeContext.InstanceId}-{orderId}";
        var authorization = await _payPalGateway.AuthorizeAsync(
            new AuthorizeOrderCommand(orderId, amount, Currency, instrument, idempotencyKey), cancellationToken);

        if (payment is null)
        {
            payment = new Payment(orderId, buyerId, amount, Currency);
            payment.SetAuthorized(authorization.PayPalOrderId, authorization.AuthorizationId, authorization.Status, authorization.ExpiresAt);
            await _paymentRepository.AddAsync(payment, cancellationToken);
        }
        else
        {
            payment.SetAuthorized(authorization.PayPalOrderId, authorization.AuthorizationId, authorization.Status, authorization.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        order.MarkAuthorized();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return payment;
    }

    public async Task<Payment> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        using var gate = await _orderLock.LockAsync($"order-{orderId}", cancellationToken);
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken)
            ?? throw new OrderNotFoundException(orderId);
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken)
            ?? throw new InvalidOrderStateException($"Order {orderId} has no payment to fulfil; it must be authorized first.");

        if (payment.State == PaymentState.Captured || order.Status == OrderStatus.Fulfilled)
        {
            // Idempotent: already fulfilled/captured.
            return payment;
        }
        if (!payment.IsAuthorized)
        {
            throw new InvalidOrderStateException($"Order {orderId} cannot be fulfilled from payment state {payment.State}.");
        }

        var amount = payment.Amount;
        var capture = await CaptureWithRenewalAsync(payment, amount, cancellationToken);

        payment.SetCaptured(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return payment;
    }

    public async Task<OrderWithPayment> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        using var gate = await _orderLock.LockAsync($"order-{orderId}", cancellationToken);
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken)
            ?? throw new OrderNotFoundException(orderId);

        if (order.Status == OrderStatus.Fulfilled)
        {
            throw new InvalidOrderStateException("This order has already been fulfilled; issue a refund instead of cancelling.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            return new OrderWithPayment(order, payment); // Idempotent.
        }

        // Release the hold if one exists, so no money ever moved.
        if (payment is not null && payment.IsAuthorized && payment.AuthorizationId is not null)
        {
            await _payPalGateway.VoidAsync(payment.AuthorizationId, $"void-{RuntimeContext.InstanceId}-{orderId}", cancellationToken);
            payment.Void();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return new OrderWithPayment(order, payment);
    }

    public async Task<Refund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        // Serialize refunds for this order so concurrent same-key requests cannot both call PayPal.
        using var gate = await _orderLock.LockAsync($"order-{orderId}", cancellationToken);

        var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken)
            ?? throw new InvalidOrderStateException($"Order {orderId} has no captured payment to refund.");

        if (payment.State is not (PaymentState.Captured or PaymentState.PartiallyRefunded))
        {
            throw new InvalidOrderStateException("Only a fulfilled (captured) order can be refunded.");
        }

        // Idempotency: repeating the same key must not refund twice.
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        var refundable = payment.RefundableAmount();
        if (refundable <= 0)
        {
            throw new InvalidOrderStateException("This order has already been fully refunded.");
        }

        // A null amount means "refund what remains"; a partial amount must not exceed the remainder.
        var effectiveAmount = amount ?? refundable;
        if (effectiveAmount <= 0)
        {
            throw new ArgumentException("The refund amount must be greater than zero.", nameof(amount));
        }
        if (effectiveAmount > refundable)
        {
            throw new InvalidOrderStateException(
                $"Refund of {effectiveAmount.ToString("0.00", CultureInfo.InvariantCulture)} {payment.Currency} exceeds the " +
                $"{refundable.ToString("0.00", CultureInfo.InvariantCulture)} {payment.Currency} still refundable on this capture.");
        }

        var captureId = payment.CaptureId
            ?? throw new InvalidOrderStateException("The payment has no capture id to refund against.");

        RefundResult result;
        try
        {
            result = await _payPalGateway.RefundAsync(captureId, effectiveAmount, payment.Currency, idempotencyKey, cancellationToken);
        }
        catch (PaymentException ex) when (ex.IsDuplicateRequest)
        {
            // A refund under this idempotency key already reached PayPal, and PayPal rejected the
            // duplicate — so no second refund was made. Return the refund that the first request
            // recorded (re-reading briefly in case it is still being committed).
            var alreadyRecorded = await FindRefundWithRetryAsync(orderId, idempotencyKey, cancellationToken);
            return alreadyRecorded
                ?? throw new PaymentException(
                    "A refund with this idempotency key was already submitted to PayPal; no second refund was made.");
        }

        var refund = payment.AddRefund(idempotencyKey, result.RefundId, result.Amount, result.Currency, result.Status);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        return refund;
    }

    public async Task<IReadOnlyList<OrderWithPayment>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        if (orders.Count == 0)
        {
            return new List<OrderWithPayment>();
        }

        var orderIds = orders.Select(o => o.Id).ToArray();
        var payments = await _paymentRepository.ListAsync(new PaymentsByOrderIdsSpecification(orderIds), cancellationToken);
        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new OrderWithPayment(o, paymentsByOrder.GetValueOrDefault(o.Id)))
            .ToList();
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // eShop's own record: every payment that ever reached PayPal.
        var payments = (await _paymentRepository.ListAsync(cancellationToken))
            .Where(p => p.PayPalOrderId is not null)
            .ToList();

        var transactions = await _payPalGateway.SearchTransactionsAsync(from, to, cancellationToken);

        // Index eShop payments by the identifiers PayPal echoes back. Matching is intentionally
        // precise: the invoice id is globally unique (it carries this run's instance id), and the
        // capture/authorization ids are exact — so historical sandbox transactions are not matched
        // by a coincidental custom_id like "1".
        var paymentByInvoiceId = payments.ToDictionary(p => ExpectedInvoiceId(p.OrderId), StringComparer.Ordinal);
        var paymentByPayPalId = new Dictionary<string, Payment>(StringComparer.Ordinal);
        foreach (var p in payments)
        {
            var ids = new List<string?> { p.AuthorizationId, p.CaptureId };
            ids.AddRange(p.Refunds.Select(r => r.PayPalRefundId));
            foreach (var id in ids)
            {
                if (!string.IsNullOrEmpty(id))
                {
                    paymentByPayPalId[id!] = p;
                }
            }
        }

        var lines = new List<ReconciliationLine>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var tx in transactions)
        {
            Payment? match = null;
            if (!string.IsNullOrEmpty(tx.InvoiceId) && paymentByInvoiceId.TryGetValue(tx.InvoiceId!, out var byInvoice))
            {
                match = byInvoice;
            }
            else if (paymentByPayPalId.TryGetValue(tx.TransactionId, out var byId))
            {
                match = byId;
            }

            if (match is not null)
            {
                matchedOrderIds.Add(match.OrderId);
                lines.Add(new ReconciliationLine(
                    ReconciliationMatch.Matched, match.OrderId, tx.TransactionId, tx.InvoiceId,
                    match.CapturedAmount ?? match.Amount, tx.Amount, tx.Currency ?? match.Currency, tx.Status));
            }
            else
            {
                lines.Add(new ReconciliationLine(
                    ReconciliationMatch.PayPalOnly, null, tx.TransactionId, tx.InvoiceId,
                    null, tx.Amount, tx.Currency, tx.Status));
            }
        }

        // eShop payments PayPal's report did not surface (e.g. reporting lag) — only the captured/paid ones,
        // since an authorization-only hold is not a settled transaction.
        foreach (var p in payments.Where(p => p.CaptureId is not null && !matchedOrderIds.Contains(p.OrderId)))
        {
            lines.Add(new ReconciliationLine(
                ReconciliationMatch.EShopOnly, p.OrderId, p.CaptureId, null,
                p.CapturedAmount ?? p.Amount, null, p.Currency, p.CaptureStatus));
        }

        var matchedCount = lines.Count(l => l.Match == ReconciliationMatch.Matched);
        var payPalOnly = lines.Count(l => l.Match == ReconciliationMatch.PayPalOnly);
        var eShopOnly = lines.Count(l => l.Match == ReconciliationMatch.EShopOnly);

        return new ReconciliationReport(
            from, to, Currency,
            PayPalTransactionCount: transactions.Count,
            EShopPaidOrderCount: payments.Count(p => p.CaptureId is not null),
            MatchedCount: matchedCount,
            PayPalOnlyCount: payPalOnly,
            EShopOnlyCount: eShopOnly,
            Lines: lines);
    }

    // ---------------------------------------------------------------------
    private async Task<Refund?> FindRefundWithRetryAsync(int orderId, string idempotencyKey, CancellationToken cancellationToken)
    {
        // The first request's write may still be settling; re-read a few times before giving up.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);
            var refund = payment?.FindRefundByKey(idempotencyKey);
            if (refund is not null)
            {
                return refund;
            }
            await Task.Delay(100, cancellationToken);
        }
        return null;
    }

    private async Task<CaptureResult> CaptureWithRenewalAsync(Payment payment, decimal amount, CancellationToken cancellationToken)
    {
        var authorizationId = payment.AuthorizationId!;

        // Proactively renew a hold that has already gone stale before attempting the capture.
        if (payment.AuthorizationExpiresAt.HasValue && payment.AuthorizationExpiresAt.Value <= DateTimeOffset.UtcNow)
        {
            authorizationId = await RenewAuthorizationAsync(payment, amount, cancellationToken);
        }

        try
        {
            return await _payPalGateway.CaptureAsync(authorizationId, amount, payment.Currency, CaptureKey(payment), cancellationToken);
        }
        catch (PayPalAuthorizationExpiredException)
        {
            // The hold expired between authorization and fulfilment: renew and capture once more.
            authorizationId = await RenewAuthorizationAsync(payment, amount, cancellationToken);
            return await _payPalGateway.CaptureAsync(authorizationId, amount, payment.Currency, CaptureKey(payment) + "-r", cancellationToken);
        }
    }

    private async Task<string> RenewAuthorizationAsync(Payment payment, decimal amount, CancellationToken cancellationToken)
    {
        try
        {
            var renewed = await _payPalGateway.ReauthorizeAsync(
                payment.AuthorizationId!, amount, payment.Currency, $"reauth-{RuntimeContext.InstanceId}-{payment.OrderId}", cancellationToken);
            payment.RenewAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            return renewed.AuthorizationId;
        }
        catch (PaymentException ex)
        {
            throw new PaymentException(
                $"The authorization hold for order {payment.OrderId} has expired and could not be renewed " +
                $"({ex.Message}). Ask the shopper to pay the order again before fulfilling it.", ex);
        }
    }

    private static string CaptureKey(Payment payment) => $"capture-{RuntimeContext.InstanceId}-{payment.OrderId}";

    // Must match the invoice_id the gateway sets when creating the PayPal order:
    // eshop-{IdempotencyKey} where IdempotencyKey = {InstanceId}-{orderId}. The instance id makes
    // it globally unique so reconciliation never mis-matches a previous run's transactions.
    private static string ExpectedInvoiceId(int orderId) => $"eshop-{RuntimeContext.InstanceId}-{orderId}";

    private async Task<Order> GetOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        // Load with items so Order.Total() is correct (GetByIdAsync would not include them).
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            // Cross-owner access is indistinguishable from "not found".
            throw new OrderNotFoundException(orderId);
        }
        return order;
    }

    private async Task<PaymentInstrument> ResolveInstrumentAsync(string buyerId, CardDetails? card, int? savedPaymentMethodId, CancellationToken cancellationToken)
    {
        if (savedPaymentMethodId.HasValue)
        {
            var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);
            var method = buyer?.FindPaymentMethod(savedPaymentMethodId.Value);
            if (method is null || string.IsNullOrEmpty(method.CardId))
            {
                // Either it isn't the caller's saved card, or it no longer exists / is unusable.
                throw new ArgumentException("The specified saved card was not found among your saved cards.");
            }
            return PaymentInstrument.FromVault(method.CardId);
        }

        if (card is not null)
        {
            return PaymentInstrument.FromCard(card);
        }

        throw new ArgumentException("Provide either card details or the id of one of your saved cards.");
    }

    private static Address DefaultAddress() =>
        new Address("N/A", "N/A", "N/A", "N/A", "00000");
}
