using System;
using System.Collections.Generic;
using System.Globalization;
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

public class CheckoutService : ICheckoutService
{
    private static readonly Address DefaultShipTo = new("123 Main St.", "Kent", "OH", "US", "44240");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IPaymentSettings _paymentSettings;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<CheckoutService> _logger;

    public CheckoutService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPaymentGateway paymentGateway,
        IPaymentSettings paymentSettings,
        IUriComposer uriComposer,
        IAppLogger<CheckoutService> logger)
    {
        _orderRepository = orderRepository;
        _catalogItemRepository = catalogItemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _paymentGateway = paymentGateway;
        _paymentSettings = paymentSettings;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineRequest> items,
        Address? shipToAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException("A signed-in shopper is required.", 401, "UNAUTHENTICATED");
        }

        if (items is null || items.Count == 0)
        {
            throw new PaymentException("At least one catalog item is required.", 400, "EMPTY_ORDER");
        }

        var catalogItemIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var orderItems = new List<OrderItem>();
        foreach (var line in items)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentException("Quantity must be greater than zero.", 400, "INVALID_QUANTITY");
            }

            if (!catalogById.TryGetValue(line.CatalogItemId, out var catalogItem))
            {
                throw new PaymentException($"Catalog item {line.CatalogItemId} was not found.", 400, "CATALOG_ITEM_NOT_FOUND");
            }

            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress ?? DefaultShipTo, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> PayAsync(
        string buyerId,
        int orderId,
        CardDetails? card,
        int? paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        order.EnsureOwnedBy(buyerId);

        if (order.Status is OrderStatus.Authorized or OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            _logger.LogInformation("Pay skipped for order {OrderId}; already in state {Status}.", order.Id, order.Status);
            return order;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentException("A cancelled order cannot be paid.", 409, "INVALID_ORDER_STATE");
        }

        if ((card is null) == !paymentMethodId.HasValue)
        {
            throw new PaymentException("Provide either card details or a saved paymentMethodId, not both.", 400, "INVALID_PAYMENT_SOURCE");
        }

        string? vaultId = null;
        if (paymentMethodId.HasValue)
        {
            var saved = await _paymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId.Value, buyerId), cancellationToken);
            if (saved is null)
            {
                throw new PaymentException("Saved payment method was not found.", 404, "PAYMENT_METHOD_NOT_FOUND");
            }

            vaultId = saved.PayPalPaymentTokenId;
        }

        var amount = order.Total();
        var currency = _paymentSettings.Currency;
        if (string.IsNullOrEmpty(order.Payment.InvoiceId))
        {
            order.AssignInvoiceId($"ESHOP-{order.Id}-{Guid.NewGuid():N}");
            await _orderRepository.UpdateAsync(order, cancellationToken);
        }

        var result = await _paymentGateway.AuthorizeAsync(
            new AuthorizePaymentCommand(
                order.Id,
                amount,
                currency,
                card,
                vaultId,
                idempotencyKey: order.Payment.InvoiceId!,
                invoiceId: order.Payment.InvoiceId!),
            cancellationToken);

        if (result.AuthorizedAmount != amount)
        {
            throw new PaymentException(
                $"PayPal authorized {result.AuthorizedAmount} {result.Currency} but the order total is {amount} {currency}.",
                502,
                "AMOUNT_MISMATCH");
        }

        order.RecordPayPalOrder(result.PayPalOrderId, result.PayPalOrderStatus, result.Currency, order.Payment.InvoiceId);
        order.MarkAuthorized(
            result.AuthorizationId,
            result.AuthorizationStatus,
            result.Expiration,
            result.AuthorizedAmount,
            result.Currency,
            paymentMethodId);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Order {OrderId} authorized with PayPal authorization {AuthorizationId}.", order.Id, result.AuthorizationId);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            return order;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentException("A cancelled order cannot be fulfilled.", 409, "INVALID_ORDER_STATE");
        }

        if (order.Status != OrderStatus.Authorized || string.IsNullOrEmpty(order.Payment.AuthorizationId))
        {
            throw new PaymentException("The order must be authorized before it can be fulfilled.", 409, "INVALID_ORDER_STATE");
        }

        var authorizationId = order.Payment.AuthorizationId;
        var amount = order.Total();
        var currency = order.Payment.Currency ?? _paymentSettings.Currency;

        var snapshot = await _paymentGateway.GetAuthorizationAsync(authorizationId, cancellationToken);
        if (snapshot.IsExpired(DateTimeOffset.UtcNow) || HonorPeriodElapsed(snapshot))
        {
            snapshot = await TryRenewAuthorizationAsync(order, snapshot, amount, currency, cancellationToken);
            authorizationId = snapshot.AuthorizationId;
        }

        PaymentCaptureResult capture;
        try
        {
            capture = await _paymentGateway.CaptureAsync(
                authorizationId,
                amount,
                currency,
                invoiceId: order.Payment.InvoiceId ?? InvoiceIdFor(order.Id),
                idempotencyKey: $"eshop-capture-{order.Payment.InvoiceId ?? order.Id.ToString(CultureInfo.InvariantCulture)}",
                cancellationToken);
        }
        catch (PaymentException ex) when (IsExpiredAuthorization(ex))
        {
            _logger.LogWarning("Capture failed because authorization {AuthorizationId} is stale; renewing.", authorizationId);
            snapshot = await TryRenewAuthorizationAsync(order, snapshot, amount, currency, cancellationToken);
            authorizationId = snapshot.AuthorizationId;
            capture = await _paymentGateway.CaptureAsync(
                authorizationId,
                amount,
                currency,
                invoiceId: order.Payment.InvoiceId ?? InvoiceIdFor(order.Id),
                idempotencyKey: $"eshop-capture-{order.Payment.InvoiceId ?? order.Id.ToString(CultureInfo.InvariantCulture)}-renewed",
                cancellationToken);
        }

        order.MarkFulfilled(
            capture.CaptureId,
            capture.CaptureStatus,
            capture.CapturedAmount,
            capture.PaypalFee,
            capture.NetAmount,
            capture.Currency);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Order {OrderId} fulfilled; capture {CaptureId}.", order.Id, capture.CaptureId);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            throw new PaymentException("A fulfilled order cannot be cancelled; issue a refund instead.", 409, "INVALID_ORDER_STATE");
        }

        if (!string.IsNullOrEmpty(order.Payment.AuthorizationId))
        {
            await _paymentGateway.VoidAsync(order.Payment.AuthorizationId, $"eshop-void-{order.Id}", cancellationToken);
        }

        order.MarkCancelled("VOIDED");
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Order {OrderId} cancelled and authorization released.", order.Id);
        return order;
    }

    public async Task<(Order Order, PaymentRefund Refund)> RefundAsync(
        string buyerId,
        int orderId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentException("An idempotency key is required for refunds.", 400, "IDEMPOTENCY_KEY_REQUIRED");
        }

        var order = await GetOrderAsync(orderId, cancellationToken);
        order.EnsureOwnedBy(buyerId);

        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return (order, existing);
        }

        if (string.IsNullOrEmpty(order.Payment.CaptureId) || order.Payment.CapturedAmount is null)
        {
            throw new PaymentException("The order has no captured payment to refund.", 409, "INVALID_ORDER_STATE");
        }

        var refundAmount = amount ?? order.RefundableRemaining();
        var currency = order.Payment.Currency ?? _paymentSettings.Currency;

        // Validate locally first so a too-large refund never reaches PayPal.
        if (refundAmount > order.RefundableRemaining())
        {
            throw new PaymentException(
                $"Refund amount {refundAmount} exceeds the remaining refundable amount {order.RefundableRemaining()}.",
                409,
                "REFUND_EXCEEDS_CAPTURE");
        }

        var result = await _paymentGateway.RefundAsync(
            order.Payment.CaptureId,
            refundAmount,
            currency,
            idempotencyKey,
            cancellationToken);

        var refund = order.AddRefund(result.RefundId, result.Status ?? "COMPLETED", result.Amount, result.Currency, idempotencyKey);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Order {OrderId} refunded {Amount} with PayPal refund {RefundId}.", order.Id, result.Amount, result.RefundId);
        return (order, refund);
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new PaymentException("`to` must be on or after `from`.", 400, "INVALID_DATE_RANGE");
        }

        var paypalTransactions = await _paymentGateway.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersWithAnyPaymentSpec(), cancellationToken);

        var paypalById = new Dictionary<string, GatewayTransaction>(StringComparer.OrdinalIgnoreCase);
        foreach (var tx in paypalTransactions)
        {
            AddIfMissing(paypalById, tx.TransactionId, tx);
            AddIfMissing(paypalById, tx.ReferenceId, tx);
        }

        var matchedPaypalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matches = new List<ReconciliationMatch>();
        var eShopOnly = new List<ReconciliationMatch>();

        foreach (var order in orders)
        {
            var identifiers = CollectPayPalIdentifiers(order);
            var matchedTx = identifiers
                .Select(id => paypalById.TryGetValue(id, out var tx) ? tx : null)
                .FirstOrDefault(tx => tx is not null);

            if (matchedTx is not null)
            {
                matches.Add(new ReconciliationMatch(
                    "matched",
                    order.Id,
                    matchedTx.TransactionId,
                    matchedTx.ReferenceId,
                    matchedTx.InvoiceId ?? InvoiceIdFor(order.Id),
                    matchedTx.Amount ?? order.Payment.CapturedAmount ?? order.Payment.AuthorizedAmount,
                    matchedTx.Currency ?? order.Payment.Currency));

                foreach (var id in identifiers)
                {
                    matchedPaypalIds.Add(id);
                }

                matchedPaypalIds.Add(matchedTx.TransactionId);
                if (!string.IsNullOrEmpty(matchedTx.ReferenceId))
                {
                    matchedPaypalIds.Add(matchedTx.ReferenceId);
                }
            }
            else if (OrderTouchesRange(order, from, to))
            {
                eShopOnly.Add(new ReconciliationMatch(
                    "eshop_only",
                    order.Id,
                    order.Payment.CaptureId ?? order.Payment.AuthorizationId,
                    order.Payment.PayPalOrderId,
                    InvoiceIdFor(order.Id),
                    order.Payment.CapturedAmount ?? order.Payment.AuthorizedAmount,
                    order.Payment.Currency));
            }
        }

        var paypalOnly = paypalTransactions
            .Where(tx => !matchedPaypalIds.Contains(tx.TransactionId)
                         && (string.IsNullOrEmpty(tx.ReferenceId) || !matchedPaypalIds.Contains(tx.ReferenceId)))
            .GroupBy(tx => tx.TransactionId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Select(tx => new ReconciliationMatch(
                "paypal_only",
                TryParseOrderId(tx.InvoiceId, tx.CustomField),
                tx.TransactionId,
                tx.ReferenceId,
                tx.InvoiceId,
                tx.Amount,
                tx.Currency))
            .ToList();

        return new ReconciliationReport(from, to, matches, paypalOnly, eShopOnly);
    }

    private async Task<AuthorizationSnapshot> TryRenewAuthorizationAsync(
        Order order,
        AuthorizationSnapshot current,
        decimal amount,
        string currency,
        CancellationToken cancellationToken)
    {
        try
        {
            var renewed = await _paymentGateway.ReauthorizeAsync(
                current.AuthorizationId,
                amount,
                currency,
                $"eshop-reauth-{order.Id}",
                cancellationToken);

            order.Payment.RecordAuthorization(
                renewed.AuthorizationId,
                renewed.Status,
                renewed.Expiration,
                renewed.Amount ?? amount,
                renewed.Currency ?? currency);

            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation(
                "Renewed authorization for order {OrderId}: {OldId} -> {NewId}.",
                order.Id,
                current.AuthorizationId,
                renewed.AuthorizationId);
            return renewed;
        }
        catch (PaymentException ex)
        {
            throw new AuthorizationUnrenewableException(
                "The payment authorization has expired and cannot be renewed. Ask the shopper to pay for this order again before fulfilling it. " +
                $"PayPal reported: {ex.Message}");
        }
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new PaymentException("Order was not found.", 404, "ORDER_NOT_FOUND");
        }

        return order;
    }

    private static bool HonorPeriodElapsed(AuthorizationSnapshot snapshot)
    {
        if (!snapshot.Expiration.HasValue)
        {
            return false;
        }

        return snapshot.Expiration.Value <= DateTimeOffset.UtcNow.AddHours(1);
    }

    private static bool IsExpiredAuthorization(PaymentException ex) =>
        string.Equals(ex.ErrorCode, "AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase)
        || (ex.Message?.Contains("AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase) ?? false)
        || (ex.Message?.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase) ?? false);

    private static string InvoiceIdFor(int orderId) => $"ESHOP-{orderId}";

    private static void AddIfMissing(Dictionary<string, GatewayTransaction> map, string? key, GatewayTransaction tx)
    {
        if (!string.IsNullOrWhiteSpace(key) && !map.ContainsKey(key))
        {
            map[key] = tx;
        }
    }

    private static IEnumerable<string> CollectPayPalIdentifiers(Order order)
    {
        if (!string.IsNullOrEmpty(order.Payment.PayPalOrderId)) yield return order.Payment.PayPalOrderId;
        if (!string.IsNullOrEmpty(order.Payment.AuthorizationId)) yield return order.Payment.AuthorizationId;
        if (!string.IsNullOrEmpty(order.Payment.CaptureId)) yield return order.Payment.CaptureId;
        yield return order.Payment.InvoiceId ?? InvoiceIdFor(order.Id);
        yield return order.Id.ToString(CultureInfo.InvariantCulture);
        foreach (var refund in order.Refunds)
        {
            if (!string.IsNullOrEmpty(refund.PayPalRefundId)) yield return refund.PayPalRefundId;
        }
    }

    private static bool OrderTouchesRange(Order order, DateTimeOffset from, DateTimeOffset to) =>
        order.OrderDate >= from && order.OrderDate <= to;

    private static int? TryParseOrderId(string? invoiceId, string? customField)
    {
        foreach (var candidate in new[] { customField, invoiceId })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var value = candidate;
            const string prefix = "ESHOP-";
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                value = value[prefix.Length..];
            }

            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                return id;
            }
        }

        return null;
    }
}
