using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
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
    public const int HonorPeriodDays = 3;
    public const int AuthorizationPeriodDays = 29;

    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPayPalGateway _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly IPaymentSettings _paymentSettings;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPayPalGateway payPal,
        IUriComposer uriComposer,
        IPaymentSettings paymentSettings,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _paymentSettings = paymentSettings;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineRequest> items,
        Address shippingAddress,
        CancellationToken cancellationToken = default)
    {
        if (items is null || items.Count == 0)
        {
            throw new PaymentException("An order must contain at least one catalog item.");
        }

        foreach (var line in items)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentException("Item quantity must be greater than zero.");
            }
        }

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        if (catalogItems.Count != ids.Length)
        {
            var found = catalogItems.Select(c => c.Id).ToHashSet();
            var missing = ids.Where(id => !found.Contains(id));
            throw new PaymentException($"Catalog item(s) not found: {string.Join(", ", missing)}.", 404);
        }

        var orderItems = items.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shippingAddress, orderItems);
        order.SetCurrency(_paymentSettings.Currency);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> PayAsync(
        string buyerId,
        int orderId,
        CardPaymentSource? card,
        int? paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        if (card is null && paymentMethodId is null)
        {
            throw new PaymentException("Provide card details or a saved paymentMethodId.");
        }

        if (card is not null && paymentMethodId is not null)
        {
            throw new PaymentException("Provide either card details or a saved paymentMethodId, not both.");
        }

        var gate = await LockAsync(orderId, cancellationToken);
        try
        {
            var order = await LoadOwnedOrderAsync(orderId, buyerId, cancellationToken);

            if (order.Status is OrderStatus.Authorized or OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
            {
                return order;
            }

            if (order.Status == OrderStatus.Cancelled)
            {
                throw new PaymentException("This order was cancelled and cannot be paid.", 409);
            }

            var currency = order.Currency ?? _paymentSettings.Currency;
            var amount = order.Total();
            if (amount <= 0)
            {
                throw new PaymentException("Order total must be greater than zero.");
            }

            var lines = order.OrderItems.Select(i => new PayPalPurchaseLine
            {
                Name = i.ItemOrdered.ProductName,
                UnitAmount = i.UnitPrice,
                Quantity = i.Units
            }).ToList();

            var invoiceId = $"{order.InvoiceId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            var customId = order.Id.ToString(CultureInfo.InvariantCulture);
            var idempotencyKey = $"eshop-pay-{order.Id}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

            PayPalAuthorizationResult result;
            if (paymentMethodId is int savedId)
            {
                var method = await _paymentMethodRepository.FirstOrDefaultAsync(
                    new SavedPaymentMethodByIdAndBuyerSpec(savedId, buyerId), cancellationToken);
                if (method is null)
                {
                    throw new PaymentException("Saved payment method not found.", 404);
                }

                result = await _payPal.AuthorizeVaultedCardAsync(
                    amount, currency, customId, invoiceId, lines, method.PayPalPaymentTokenId, idempotencyKey, cancellationToken);
            }
            else
            {
                ValidateCard(card!);
                result = await _payPal.AuthorizeCardAsync(
                    amount, currency, customId, invoiceId, lines, card!, idempotencyKey, cancellationToken);
            }

            if (result.Amount != amount)
            {
                _logger.LogWarning(
                    "PayPal authorized {Authorized} for order {OrderId} whose total is {Total}.",
                    result.Amount, order.Id, amount);
            }

            order.MarkAuthorized(
                result.PayPalOrderId,
                result.AuthorizationId,
                result.AuthorizationStatus,
                result.Amount,
                result.Currency,
                result.Expiration);

            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var gate = await LockAsync(orderId, cancellationToken);
        try
        {
            var order = await LoadOrderAsync(orderId, cancellationToken);

            if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
            {
                return order;
            }

            if (order.Status == OrderStatus.Cancelled)
            {
                throw new PaymentException("Cannot fulfil a cancelled order.", 409);
            }

            if (order.Status != OrderStatus.Authorized || string.IsNullOrWhiteSpace(order.PayPalAuthorizationId))
            {
                throw new PaymentException("Order must be authorized before it can be fulfilled.", 409);
            }

            var currency = order.Currency ?? _paymentSettings.Currency;
            var amount = order.AuthorizedAmount ?? order.Total();
            var authorizationId = await EnsureAuthorizationReadyToCaptureAsync(order, amount, currency, cancellationToken);

            var capture = await CaptureWithRenewalFallbackAsync(order, authorizationId, amount, currency, cancellationToken);

            order.MarkFulfilled(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PaypalFee, capture.NetProceeds);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var gate = await LockAsync(orderId, cancellationToken);
        try
        {
            var order = await LoadOrderAsync(orderId, cancellationToken);

            if (order.Status == OrderStatus.Cancelled)
            {
                return order;
            }

            if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
            {
                throw new PaymentException("Cannot cancel after fulfilment. Issue a refund instead.", 409);
            }

            if (!string.IsNullOrWhiteSpace(order.PayPalAuthorizationId))
            {
                try
                {
                    await _payPal.VoidAuthorizationAsync(
                        order.PayPalAuthorizationId,
                        $"eshop-void-{order.PayPalAuthorizationId}",
                        cancellationToken);
                }
                catch (PaymentException ex) when (ex.StatusCode is 404 or 409)
                {
                    _logger.LogWarning("PayPal void for order {OrderId} returned {Status}: {Message}", order.Id, ex.StatusCode, ex.Message);
                }
            }

            order.MarkCancelled();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<OrderRefund> RefundAsync(
        string buyerId,
        int orderId,
        string idempotencyKey,
        decimal? amount,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentException("Refunds require an idempotencyKey so a retry cannot refund twice.");
        }

        var gate = await LockAsync(orderId, cancellationToken);
        try
        {
            var order = await LoadOwnedOrderAsync(orderId, buyerId, cancellationToken);

            var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
            if (existing is not null)
            {
                return existing;
            }

            if (order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded))
            {
                throw new PaymentException("Refunds are only allowed after fulfilment.", 409);
            }

            if (string.IsNullOrWhiteSpace(order.PayPalCaptureId))
            {
                throw new PaymentException("Order has no captured PayPal payment to refund.", 409);
            }

            var refundAmount = amount ?? order.RemainingRefundable;
            if (refundAmount <= 0)
            {
                throw new PaymentException("There is no remaining captured amount to refund.", 409);
            }

            if (refundAmount > order.RemainingRefundable)
            {
                throw new PaymentException(
                    $"Refund of {refundAmount} exceeds the remaining captured amount of {order.RemainingRefundable}.",
                    409);
            }

            var currency = order.Currency ?? _paymentSettings.Currency;
            var result = await _payPal.RefundCaptureAsync(
                order.PayPalCaptureId,
                refundAmount,
                currency,
                idempotencyKey,
                cancellationToken);

            var refund = order.RecordRefund(result.RefundId, result.Status, result.Amount == 0 ? refundAmount : result.Amount, result.Currency, idempotencyKey);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return refund;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        return orders;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new PaymentException("`to` must be greater than or equal to `from`.");
        }

        var paypalTransactions = await _payPal.ListAllTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersInDateRangeSpec(from, to), cancellationToken);

        var matches = new List<ReconciledRow>();
        var unmatchedPayPal = new List<PayPalReportedTransaction>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in paypalTransactions)
        {
            var order = FindMatchingOrder(orders, txn);
            if (order is null)
            {
                unmatchedPayPal.Add(txn);
                continue;
            }

            matchedOrderIds.Add(order.Id);
            matches.Add(new ReconciledRow
            {
                OrderId = order.Id,
                MatchReason = DescribeMatch(order, txn),
                PayPalTransaction = txn
            });
        }

        var eshopOnly = orders
            .Where(o => !matchedOrderIds.Contains(o.Id) && HasPayPalFootprint(o))
            .Select(o => new EshopUnmatchedOrder
            {
                OrderId = o.Id,
                Status = o.Status.ToString(),
                PayPalOrderId = o.PayPalOrderId,
                PayPalAuthorizationId = o.PayPalAuthorizationId,
                PayPalCaptureId = o.PayPalCaptureId,
                Total = o.Total(),
                OrderDate = o.OrderDate
            })
            .ToList();

        return new ReconciliationReport
        {
            From = from,
            To = to,
            Matches = matches,
            PayPalOnly = unmatchedPayPal,
            EshopOnly = eshopOnly
        };
    }

    private async Task<string> EnsureAuthorizationReadyToCaptureAsync(
        Order order,
        decimal amount,
        string currency,
        CancellationToken cancellationToken)
    {
        var authorizationId = order.PayPalAuthorizationId!;
        PayPalAuthorizationDetails details;
        try
        {
            details = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (PaymentException ex) when (ex.StatusCode == 404)
        {
            throw new PaymentException(
                "PayPal no longer has this authorization. Ask the shopper to pay again; the original hold cannot be recovered.",
                409);
        }

        order.RefreshAuthorization(details.AuthorizationId, details.Status, details.Expiration);

        if (details.Status is "CAPTURED" or "PARTIALLY_CAPTURED")
        {
            return details.AuthorizationId;
        }

        if (details.Status is "VOIDED" or "DENIED")
        {
            throw new PaymentException(
                $"PayPal authorization is {details.Status} and cannot be captured. Ask the shopper to pay again.",
                409);
        }

        if (IsPastRenewalWindow(order, details))
        {
            throw CannotRenew();
        }

        if (NeedsRenewal(order, details))
        {
            details = await RenewAuthorizationAsync(order, details.AuthorizationId, amount, currency, cancellationToken);
        }

        return details.AuthorizationId;
    }

    private async Task<PayPalCaptureResult> CaptureWithRenewalFallbackAsync(
        Order order,
        string authorizationId,
        decimal amount,
        string currency,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _payPal.CaptureAuthorizationAsync(
                authorizationId,
                amount,
                currency,
                CaptureInvoiceId(order, authorizationId),
                CaptureIdempotencyKey(authorizationId),
                cancellationToken);
        }
        catch (PaymentException ex) when (IsStaleAuthorizationError(ex))
        {
            if (IsPastRenewalWindow(order))
            {
                throw CannotRenew();
            }

            var renewed = await RenewAuthorizationAsync(order, authorizationId, amount, currency, cancellationToken);
            return await _payPal.CaptureAuthorizationAsync(
                renewed.AuthorizationId,
                amount,
                currency,
                CaptureInvoiceId(order, renewed.AuthorizationId),
                CaptureIdempotencyKey(renewed.AuthorizationId),
                cancellationToken);
        }
    }

    private async Task<PayPalAuthorizationDetails> RenewAuthorizationAsync(
        Order order,
        string authorizationId,
        decimal amount,
        string currency,
        CancellationToken cancellationToken)
    {
        try
        {
            var renewed = await _payPal.ReauthorizeAsync(
                authorizationId,
                amount,
                currency,
                $"eshop-reauth-{authorizationId}",
                cancellationToken);

            order.RefreshAuthorization(renewed.AuthorizationId, renewed.Status, renewed.Expiration);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation("Renewed PayPal authorization for order {OrderId} to {AuthorizationId}.", order.Id, renewed.AuthorizationId);
            return renewed;
        }
        catch (PaymentException ex)
        {
            throw new PaymentException(
                "PayPal would not renew this authorization. The hold is past the window in which it can be reauthorized " +
                "(PayPal allows reauthorization from day 4 through day 29; after 30 days you must take a new payment). " +
                "Ask the shopper to pay again, then fulfil the new authorization. " +
                $"PayPal said: {ex.Message}",
                409);
        }
    }

    private static bool NeedsRenewal(Order order, PayPalAuthorizationDetails details)
    {
        var now = DateTimeOffset.UtcNow;
        if (details.Expiration is DateTimeOffset expiration && expiration <= now.AddMinutes(5))
        {
            return true;
        }

        var lastAuthorized = details.CreateTime ?? order.AuthorizedAt;
        return lastAuthorized is DateTimeOffset stamp && now - stamp >= TimeSpan.FromDays(HonorPeriodDays);
    }

    private static bool IsPastRenewalWindow(Order order, PayPalAuthorizationDetails? details = null)
    {
        var origin = order.OriginalAuthorizedAt ?? details?.CreateTime ?? order.AuthorizedAt;
        if (origin is DateTimeOffset stamp)
        {
            return DateTimeOffset.UtcNow - stamp >= TimeSpan.FromDays(AuthorizationPeriodDays);
        }

        return false;
    }

    private static bool IsStaleAuthorizationError(PaymentException ex) =>
        ex.Message.Contains("AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("AUTHORIZATION_VOIDED", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase) ||
        ex.StatusCode is 409 or 422;

    private static PaymentException CannotRenew() =>
        new(
            "This PayPal authorization can no longer be renewed. PayPal holds last a 3-day honor period and can be reauthorized only within 29 days of the original authorization. " +
            "Ask the shopper to pay again, then fulfil the new authorization.",
            409);

    private static string CaptureIdempotencyKey(string authorizationId) =>
        $"eshop-capture-{authorizationId}";

    private static string CaptureInvoiceId(Order order, string authorizationId) =>
        $"{order.InvoiceId}-{authorizationId}";

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new PaymentException($"Order {orderId} was not found.", 404);
        }

        return order;
    }

    private async Task<Order> LoadOwnedOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (!order.OwnedBy(buyerId))
        {
            throw new PaymentException("Order not found.", 404);
        }

        return order;
    }

    private static async Task<SemaphoreSlim> LockAsync(int orderId, CancellationToken cancellationToken)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return gate;
    }

    private static void ValidateCard(CardPaymentSource card)
    {
        if (string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry) || string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            throw new PaymentException("Card number, expiry (YYYY-MM), and security code are required.");
        }
    }

    private static Order? FindMatchingOrder(IReadOnlyList<Order> orders, PayPalReportedTransaction txn)
    {
        foreach (var order in orders)
        {
            if (!string.IsNullOrWhiteSpace(txn.CustomField) &&
                string.Equals(txn.CustomField, order.Id.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                return order;
            }

            if (!string.IsNullOrWhiteSpace(txn.InvoiceId) &&
                txn.InvoiceId.StartsWith(order.InvoiceId, StringComparison.OrdinalIgnoreCase))
            {
                return order;
            }

            if (IdsEqual(txn.TransactionId, order.PayPalCaptureId) ||
                IdsEqual(txn.TransactionId, order.PayPalAuthorizationId) ||
                IdsEqual(txn.TransactionId, order.PayPalOrderId) ||
                IdsEqual(txn.ReferenceId, order.PayPalCaptureId) ||
                IdsEqual(txn.ReferenceId, order.PayPalAuthorizationId) ||
                IdsEqual(txn.ReferenceId, order.PayPalOrderId) ||
                order.Refunds.Any(r => IdsEqual(txn.TransactionId, r.PayPalRefundId) || IdsEqual(txn.ReferenceId, r.PayPalRefundId)))
            {
                return order;
            }
        }

        return null;
    }

    private static string DescribeMatch(Order order, PayPalReportedTransaction txn)
    {
        if (IdsEqual(txn.TransactionId, order.PayPalCaptureId) || IdsEqual(txn.ReferenceId, order.PayPalCaptureId))
        {
            return "capture_id";
        }

        if (IdsEqual(txn.TransactionId, order.PayPalAuthorizationId) || IdsEqual(txn.ReferenceId, order.PayPalAuthorizationId))
        {
            return "authorization_id";
        }

        if (order.Refunds.Any(r => IdsEqual(txn.TransactionId, r.PayPalRefundId) || IdsEqual(txn.ReferenceId, r.PayPalRefundId)))
        {
            return "refund_id";
        }

        if (!string.IsNullOrWhiteSpace(txn.CustomField))
        {
            return "custom_id";
        }

        return "invoice_id";
    }

    private static bool HasPayPalFootprint(Order order) =>
        !string.IsNullOrWhiteSpace(order.PayPalOrderId) ||
        !string.IsNullOrWhiteSpace(order.PayPalAuthorizationId) ||
        !string.IsNullOrWhiteSpace(order.PayPalCaptureId);

    private static bool IdsEqual(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    internal static string ToMerchantCustomerId(string buyerId)
    {
        var builder = new StringBuilder("eshop_");
        foreach (var ch in buyerId)
        {
            builder.Append(char.IsLetterOrDigit(ch) || ch is '_' or '-' ? ch : '_');
        }

        var value = builder.ToString();
        return value.Length <= 64 ? value : value[..64];
    }
}
