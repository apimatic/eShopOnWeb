using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the order + payment lifecycle on top of the existing Order/OrderItem model and
/// the <see cref="IPaymentGateway"/> port. Enforces the state machine (awaiting → authorized →
/// fulfilled / cancelled / refunded), idempotency, ownership, and the "never refund past what was
/// captured" invariant. All actual money movement is delegated to the gateway.
/// </summary>
public class PaymentService : IPaymentService
{
    private const string DefaultAddressValue = "N/A";
    private const string DefaultPictureUri = "eCatalog-item-default.png";

    private readonly IRepository<Order> _orders;
    private readonly IRepository<OrderPayment> _payments;
    private readonly IReadRepository<CatalogItem> _catalogItems;
    private readonly IReadRepository<SavedCard> _savedCards;
    private readonly IPaymentGateway _gateway;
    private readonly IAppLogger<PaymentService> _logger;
    private readonly string _currency;

    public PaymentService(
        IRepository<Order> orders,
        IRepository<OrderPayment> payments,
        IReadRepository<CatalogItem> catalogItems,
        IReadRepository<SavedCard> savedCards,
        IPaymentGateway gateway,
        PayPalSettings settings,
        IAppLogger<PaymentService> logger)
    {
        _orders = orders;
        _payments = payments;
        _catalogItems = catalogItems;
        _savedCards = savedCards;
        _gateway = gateway;
        _logger = logger;
        _currency = string.IsNullOrWhiteSpace(settings.Currency) ? "USD" : settings.Currency;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<PlaceOrderItem> items,
        ShippingAddressInput? address, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items is null || items.Count == 0)
        {
            throw new InvalidPaymentOperationException("An order must contain at least one item.");
        }

        // Aggregate duplicate catalog ids and validate quantities.
        var quantities = new Dictionary<int, int>();
        foreach (var item in items)
        {
            if (item.Quantity <= 0)
            {
                throw new InvalidPaymentOperationException($"Quantity for catalog item {item.CatalogItemId} must be greater than zero.");
            }
            quantities[item.CatalogItemId] = quantities.GetValueOrDefault(item.CatalogItemId) + item.Quantity;
        }

        var catalogItems = await _catalogItems.ListAsync(
            new CatalogItemsSpecification(quantities.Keys.ToArray()), cancellationToken);

        var missing = quantities.Keys.Except(catalogItems.Select(c => c.Id)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidPaymentOperationException($"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var orderItems = quantities.Select(kvp =>
        {
            var catalogItem = catalogItems.First(c => c.Id == kvp.Key);
            var pictureUri = string.IsNullOrEmpty(catalogItem.PictureUri) ? DefaultPictureUri : catalogItem.PictureUri;
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, kvp.Value);
        }).ToList();

        var shipToAddress = new Address(
            street: Coalesce(address?.Street),
            city: Coalesce(address?.City),
            state: address?.State ?? DefaultAddressValue,
            country: Coalesce(address?.Country),
            zipcode: Coalesce(address?.ZipCode));

        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orders.AddAsync(order, cancellationToken);

        _logger.LogInformation($"Order {order.Id} placed by {buyerId} with {orderItems.Count} line(s), total {Format(order.Total())} {_currency}.");
        return order.Id;
    }

    public async Task<OrderPaymentView> AuthorizeAsync(string buyerId, int orderId, CardDetails? card,
        int? savedCardId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);
        var existingPayment = await _payments.FirstOrDefaultAsync(
            new OrderPaymentByOrderIdSpecification(orderId), cancellationToken);

        // Idempotent: a repeated pay never authorizes twice.
        if (existingPayment?.AuthorizationId != null)
        {
            return BuildView(order, existingPayment);
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidPaymentOperationException(
                $"Order {orderId} cannot be paid because it is {order.Status}.");
        }

        // Resolve the funding source: exactly one of inline card / saved card.
        string? vaultId = null;
        if (savedCardId.HasValue)
        {
            if (card != null)
            {
                throw new InvalidPaymentOperationException("Provide either card details or a saved card, not both.");
            }
            var saved = await _savedCards.GetByIdAsync(savedCardId.Value, cancellationToken);
            if (saved is null || saved.BuyerId != buyerId)
            {
                throw new InvalidPaymentOperationException($"Saved card {savedCardId.Value} was not found for this shopper.");
            }
            vaultId = saved.VaultId;
        }
        else if (card is null)
        {
            throw new InvalidPaymentOperationException("Provide card details or a saved card to pay with.");
        }

        var total = Round(order.Total());
        if (total <= 0)
        {
            throw new InvalidPaymentOperationException($"Order {orderId} has a non-positive total and cannot be paid.");
        }

        var requestId = Guid.NewGuid().ToString();
        var invoiceId = $"eshop-{orderId}-{Guid.NewGuid():N}";
        var authRequest = new AuthorizeRequest(total, _currency, orderId.ToString(CultureInfo.InvariantCulture),
            invoiceId, requestId, card, vaultId);

        var result = await _gateway.AuthorizeAsync(authRequest, cancellationToken);

        var payment = new OrderPayment(orderId, total, _currency, result.PayPalOrderId, invoiceId, requestId);
        payment.SetAuthorization(result.AuthorizationId, result.Status);
        payment.SetCardMetadata(result.CardBrand, result.CardLast4);
        await _payments.AddAsync(payment, cancellationToken);

        order.MarkAuthorized();
        await _orders.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Order {orderId} authorized: hold {result.AuthorizationId} for {Format(total)} {_currency} (PayPal order {result.PayPalOrderId}).");
        return BuildView(order, payment);
    }

    public async Task<OrderPaymentView> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }

        var payment = await _payments.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(orderId), cancellationToken);
        if (payment is null || payment.AuthorizationId is null)
        {
            throw new InvalidPaymentOperationException($"Order {orderId} has no authorized payment to fulfil; it must be paid first.");
        }

        // Idempotent: already captured.
        if (order.Status == OrderStatus.Fulfilled && payment.CaptureId != null)
        {
            return BuildView(order, payment);
        }

        if (order.Status != OrderStatus.Authorized)
        {
            throw new InvalidPaymentOperationException($"Order {orderId} cannot be fulfilled because it is {order.Status}.");
        }

        var captureRequestId = Guid.NewGuid().ToString();
        CaptureResult capture = await CaptureRenewingIfStaleAsync(order, payment, captureRequestId, cancellationToken);

        // The capture response may not include the fee/net breakdown immediately; read it back.
        var settled = capture;
        if (settled.PayPalFee is null || settled.NetAmount is null)
        {
            try
            {
                settled = await _gateway.GetCaptureAsync(capture.CaptureId, cancellationToken);
            }
            catch (PaymentGatewayException ex)
            {
                _logger.LogWarning($"Could not read capture {capture.CaptureId} breakdown back from PayPal: {ex.Message}");
            }
        }

        payment.SetCapture(settled.CaptureId, settled.Status, settled.Amount, settled.PayPalFee, settled.NetAmount);
        await _payments.UpdateAsync(payment, cancellationToken);

        order.MarkFulfilled();
        await _orders.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Order {orderId} fulfilled: captured {Format(settled.Amount)} {_currency} (capture {settled.CaptureId}, fee {Format(settled.PayPalFee ?? 0)}, net {Format(settled.NetAmount ?? 0)}).");
        return BuildView(order, payment);
    }

    /// <summary>
    /// Capture the authorization, renewing it first if PayPal reports it as stale/expired.
    /// If the authorization can no longer be renewed, throws an operator-actionable error.
    /// </summary>
    private async Task<CaptureResult> CaptureRenewingIfStaleAsync(Order order, OrderPayment payment,
        string captureRequestId, CancellationToken cancellationToken)
    {
        try
        {
            return await _gateway.CaptureAsync(payment.AuthorizationId!, payment.Amount, payment.CurrencyCode,
                captureRequestId, cancellationToken);
        }
        catch (PaymentGatewayException ex) when (IsAuthorizationStale(ex))
        {
            _logger.LogWarning($"Authorization {payment.AuthorizationId} for order {order.Id} is stale ({ex.PayPalName}); attempting to renew.");
            AuthorizationResult renewed;
            try
            {
                renewed = await _gateway.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount,
                    payment.CurrencyCode, cancellationToken);
            }
            catch (PaymentGatewayException reauthEx)
            {
                throw new InvalidPaymentOperationException(
                    $"The authorization for order {order.Id} has gone stale and can no longer be renewed " +
                    $"(PayPal: {reauthEx.PayPalName ?? "reauthorization failed"} - {reauthEx.Message}). " +
                    "Ask the shopper to pay for the order again before it can be fulfilled.");
            }

            payment.SetAuthorization(renewed.AuthorizationId, renewed.Status);
            await _payments.UpdateAsync(payment, cancellationToken);

            return await _gateway.CaptureAsync(renewed.AuthorizationId, payment.Amount, payment.CurrencyCode,
                Guid.NewGuid().ToString(), cancellationToken);
        }
    }

    public async Task<OrderPaymentView> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }

        var payment = await _payments.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(orderId), cancellationToken);

        // Idempotent.
        if (order.Status == OrderStatus.Cancelled)
        {
            return BuildView(order, payment);
        }

        switch (order.Status)
        {
            case OrderStatus.AwaitingPayment:
                // Nothing was ever held; just cancel.
                order.MarkCancelled();
                await _orders.UpdateAsync(order, cancellationToken);
                break;

            case OrderStatus.Authorized:
                if (payment?.AuthorizationId != null)
                {
                    await _gateway.VoidAsync(payment.AuthorizationId, Guid.NewGuid().ToString(), cancellationToken);
                    payment.SetVoided();
                    await _payments.UpdateAsync(payment, cancellationToken);
                }
                order.MarkCancelled();
                await _orders.UpdateAsync(order, cancellationToken);
                _logger.LogInformation($"Order {orderId} cancelled; held funds released (authorization {payment?.AuthorizationId} voided).");
                break;

            default:
                throw new InvalidPaymentOperationException(
                    $"Order {orderId} is {order.Status} and cannot be cancelled; issue a refund instead.");
        }

        return BuildView(order, payment);
    }

    public async Task<RefundOutcome> RefundAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new InvalidPaymentOperationException("A refund requires a non-empty idempotency key.");
        }

        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);
        var payment = await _payments.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(orderId), cancellationToken);
        if (payment is null || payment.CaptureId is null)
        {
            throw new InvalidPaymentOperationException($"Order {orderId} has not been captured; there is nothing to refund.");
        }

        if (order.Status != OrderStatus.Fulfilled && order.Status != OrderStatus.PartiallyRefunded)
        {
            throw new InvalidPaymentOperationException($"Order {orderId} cannot be refunded because it is {order.Status}.");
        }

        // Idempotent: same key returns the same refund and never refunds twice.
        var duplicate = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (duplicate != null)
        {
            return new RefundOutcome(duplicate.PayPalRefundId, BuildView(order, payment));
        }

        var refundable = payment.RefundableRemaining;
        if (refundable <= 0)
        {
            throw new InvalidPaymentOperationException($"Order {orderId} has already been fully refunded.");
        }

        var requested = Round(amount ?? refundable);
        if (requested <= 0)
        {
            throw new InvalidPaymentOperationException("Refund amount must be greater than zero.");
        }
        if (requested > refundable)
        {
            throw new InvalidPaymentOperationException(
                $"Refund of {Format(requested)} {_currency} exceeds the refundable remaining of {Format(refundable)} {_currency} for order {orderId}.");
        }

        // Derive the PayPal-Request-Id deterministically from the capture + caller key: same key ->
        // same id (so a genuine retry is deduplicated by PayPal too) while staying globally unique,
        // so an unrelated caller reusing a short key like "k1" cannot collide on PayPal's side.
        var refundRequestId = BuildDeterministicRequestId($"refund:{payment.CaptureId}:{idempotencyKey}");

        // No invoice_id on refunds: two distinct partial refunds of one capture would otherwise
        // collide on PayPal's per-merchant invoice uniqueness. Refunds reconcile by refund id.
        var result = await _gateway.RefundAsync(payment.CaptureId, requested, _currency, refundRequestId,
            invoiceId: null, cancellationToken);

        payment.AddRefund(result.RefundId, result.Status, requested, idempotencyKey);
        await _payments.UpdateAsync(payment, cancellationToken);

        var fullyRefunded = payment.RefundableRemaining <= 0;
        order.MarkRefunded(fullyRefunded);
        await _orders.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Order {orderId} refunded {Format(requested)} {_currency} (refund {result.RefundId}); order is now {order.Status}.");
        return new RefundOutcome(result.RefundId, BuildView(order, payment));
    }

    public async Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var payments = await _payments.ListAsync(new OrderPaymentsWithRefundsSpecification(), cancellationToken);
        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

        return orders
            .OrderByDescending(o => o.Id)
            .Select(o => BuildView(o, paymentsByOrder.GetValueOrDefault(o.Id)))
            .ToList();
    }

    public async Task<OrderPaymentView?> GetMyOrderAsync(string buyerId, int orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            return null;
        }
        var payment = await _payments.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(orderId), cancellationToken);
        return BuildView(order, payment);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new InvalidPaymentOperationException("Reconciliation 'to' must not be earlier than 'from'.");
        }

        var transactions = await _gateway.ListTransactionsAsync(from, to, cancellationToken);
        var payments = await _payments.ListAsync(new OrderPaymentsWithRefundsSpecification(), cancellationToken);
        var orders = await _orders.ListAsync(cancellationToken);
        var ordersById = orders.ToDictionary(o => o.Id);

        // Build lookups from eShop's side to match PayPal transaction ids.
        var byCapture = new Dictionary<string, OrderPayment>();
        var byAuthorization = new Dictionary<string, OrderPayment>();
        var byRefund = new Dictionary<string, OrderPayment>();
        foreach (var p in payments)
        {
            if (p.CaptureId != null) byCapture[p.CaptureId] = p;
            if (p.AuthorizationId != null) byAuthorization[p.AuthorizationId] = p;
            foreach (var r in p.Refunds)
            {
                byRefund[r.PayPalRefundId] = p;
            }
        }

        var report = new ReconciliationReport
        {
            From = from,
            To = to,
            CurrencyCode = _currency,
            PayPalTransactionCount = transactions.Count,
            PayPalGrossTotal = Round(transactions.Sum(t => t.Amount))
        };

        var matchedTransactionIds = new HashSet<string>();
        foreach (var txn in transactions)
        {
            OrderPayment? match = null;
            string matchedBy = string.Empty;

            if (byCapture.TryGetValue(txn.TransactionId, out var byCap)) { match = byCap; matchedBy = "capture"; }
            else if (byRefund.TryGetValue(txn.TransactionId, out var byRef)) { match = byRef; matchedBy = "refund"; }
            else if (byAuthorization.TryGetValue(txn.TransactionId, out var byAuth)) { match = byAuth; matchedBy = "authorization"; }
            else if (int.TryParse(txn.CustomField, NumberStyles.Integer, CultureInfo.InvariantCulture, out var customOrderId)
                     && ordersById.ContainsKey(customOrderId)) { match = payments.FirstOrDefault(p => p.OrderId == customOrderId); matchedBy = "custom_id"; }

            if (match != null)
            {
                matchedTransactionIds.Add(txn.TransactionId);
                report.Matched.Add(new ReconciliationMatch
                {
                    TransactionId = txn.TransactionId,
                    EventCode = txn.EventCode,
                    OrderId = match.OrderId,
                    PayPalAmount = txn.Amount,
                    Status = txn.Status,
                    MatchedBy = matchedBy
                });
            }
            else
            {
                report.PayPalOnly.Add(txn);
            }
        }

        // eShop payments captured within the range that PayPal reporting has not (yet) returned.
        var reportedIds = transactions.Select(t => t.TransactionId).ToHashSet();
        foreach (var p in payments.Where(p => p.CaptureId != null))
        {
            if (!ordersById.TryGetValue(p.OrderId, out var order)) continue;
            if (order.OrderDate < from || order.OrderDate > to) continue;
            if (reportedIds.Contains(p.CaptureId!)) continue;

            report.EShopOnly.Add(new EShopUnmatched
            {
                OrderId = p.OrderId,
                StatusName = order.Status.ToString(),
                CaptureId = p.CaptureId,
                Amount = p.CapturedAmount ?? p.Amount,
                Reason = "Captured in eShop but no matching PayPal transaction found in range (PayPal reporting can lag recent activity)."
            });
        }

        return report;
    }

    private async Task<Order> LoadOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            // Same error whether missing or not owned, so ownership cannot be probed.
            throw new OrderNotFoundException(orderId);
        }
        return order;
    }

    /// <summary>
    /// True only for PayPal signals that the authorization is stale/expired and could be renewed,
    /// e.g. AUTHORIZATION_EXPIRED / PAYMENT_AUTHORIZATION_EXPIRED. Kept conservative so a plain
    /// decline is never mistaken for staleness.
    /// </summary>
    private static bool IsAuthorizationStale(PaymentGatewayException ex)
    {
        var name = ex.PayPalName ?? string.Empty;
        return name.IndexOf("EXPIRED", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string Coalesce(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DefaultAddressValue : value;

    /// <summary>
    /// A stable GUID-shaped id derived from a seed, so retries of the same logical operation reuse
    /// one PayPal-Request-Id while distinct operations never collide.
    /// </summary>
    private static string BuildDeterministicRequestId(string seed)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed));
        var guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, 16);
        return new Guid(guidBytes).ToString();
    }

    private decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string Format(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private OrderPaymentView BuildView(Order order, OrderPayment? payment)
    {
        var view = new OrderPaymentView
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            OrderDate = order.OrderDate,
            Status = order.Status,
            Total = Round(order.Total()),
            CurrencyCode = _currency,
            Items = order.OrderItems.Select(i => new OrderLineView
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList()
        };

        if (payment != null)
        {
            view.PayPalOrderId = payment.PayPalOrderId;
            view.AuthorizationId = payment.AuthorizationId;
            view.AuthorizationStatus = payment.AuthorizationStatus;
            view.CaptureId = payment.CaptureId;
            view.CaptureStatus = payment.CaptureStatus;
            view.CapturedAmount = payment.CapturedAmount;
            view.PayPalFee = payment.PayPalFee;
            view.NetAmount = payment.NetAmount;
            view.TotalRefunded = payment.TotalRefunded;
            view.RefundableRemaining = payment.RefundableRemaining;
            view.CardBrand = payment.CardBrand;
            view.CardLast4 = payment.CardLast4;
            view.Refunds = payment.Refunds
                .OrderBy(r => r.CreatedAt)
                .Select(r => new RefundView
                {
                    RefundId = r.PayPalRefundId,
                    Status = r.Status,
                    Amount = r.Amount,
                    CreatedAt = r.CreatedAt
                }).ToList();
        }

        return view;
    }
}
