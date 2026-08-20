using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private static readonly Address DefaultShippingAddress =
        new("123 Main Street", "Seattle", "WA", "US", "98101");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalGateway _payPal;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IUriComposer uriComposer,
        IPayPalGateway payPal,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _catalogItemRepository = catalogItemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _uriComposer = uriComposer;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<ShopperOrder> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineRequest> items,
        Address? shippingAddress,
        CancellationToken cancellationToken = default)
    {
        if (items is null || items.Count == 0)
        {
            throw new PaymentException(400, "An order must contain at least one catalog item.");
        }

        if (items.Any(item => item.Quantity <= 0))
        {
            throw new PaymentException(400, "Each order line must have a quantity greater than zero.");
        }

        var catalogIds = items.Select(item => item.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItemRepository.ListAsync(new CatalogItemsSpecification(catalogIds), cancellationToken);
        var catalogById = catalogItems.ToDictionary(item => item.Id);

        var missing = catalogIds.Where(id => !catalogById.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
        {
            throw new PaymentException(400, $"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = items.Select(line =>
        {
            var catalogItem = catalogById[line.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shippingAddress ?? DefaultShippingAddress, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);

        var payment = new OrderPayment(order.Id, buyerId, order.Total(), _payPal.Currency);
        await _paymentRepository.AddAsync(payment, cancellationToken);

        _logger.LogInformation("Created order {0} awaiting payment.", order.Id);
        return new ShopperOrder(order, payment);
    }

    public async Task<OrderPayment> PayAsync(
        int orderId,
        string buyerId,
        CardPaymentSource? card,
        int? paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var order = await GetOwnedOrderAsync(orderId, buyerId, cancellationToken);
        var payment = await GetPaymentAsync(orderId, cancellationToken);

        if (payment.Status is OrderPaymentStatus.Authorized or OrderPaymentStatus.Captured
            or OrderPaymentStatus.Refunded or OrderPaymentStatus.PartiallyRefunded)
        {
            _logger.LogInformation("Pay for order {0} was already completed with status {1}.", orderId, payment.Status);
            return payment;
        }

        if (payment.Status == OrderPaymentStatus.Cancelled)
        {
            throw new PaymentException(409, "This order was cancelled and can no longer be paid.");
        }

        var usingCard = card is not null;
        var usingSaved = paymentMethodId.HasValue;
        if (usingCard == usingSaved)
        {
            throw new PaymentException(400, "Provide either card details or a saved paymentMethodId, not both.");
        }

        SavedPaymentMethod? saved = null;
        if (usingSaved)
        {
            saved = await _paymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdSpecification(paymentMethodId!.Value), cancellationToken);
            if (saved is null || saved.BuyerId != buyerId)
            {
                throw new PaymentException(404, "The saved payment method was not found.");
            }
        }

        var amount = decimal.Round(order.Total(), 2, MidpointRounding.AwayFromZero);
        if (amount != payment.Amount)
        {
            throw new PaymentException(409, "The order total no longer matches the payment amount.");
        }

        var invoiceId = UniqueInvoiceId(payment);
        var requestId = $"eshop-pay-{payment.ReferenceKey}";

        PayPalAuthorizationResult authorization;
        if (saved is not null)
        {
            authorization = await _payPal.AuthorizeVaultedCardPaymentAsync(
                amount, invoiceId, order.Id.ToString(), saved.PayPalPaymentTokenId, requestId, cancellationToken);
        }
        else
        {
            authorization = await _payPal.AuthorizeCardPaymentAsync(
                amount, invoiceId, order.Id.ToString(), card!, requestId, cancellationToken);
        }

        if (authorization.Amount != amount)
        {
            throw new PaymentException(502,
                $"PayPal authorized {authorization.Amount} {authorization.Currency} but the order total is {amount} {_payPal.Currency}.");
        }

        payment.RecordAuthorization(
            authorization.PayPalOrderId,
            authorization.AuthorizationId,
            authorization.Status,
            authorization.ExpirationTime,
            saved?.Id);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        _logger.LogInformation("Authorized PayPal payment for order {0}.", orderId);
        return payment;
    }

    public async Task<OrderPayment> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await GetPaymentAsync(orderId, cancellationToken);

        if (payment.Status is OrderPaymentStatus.Captured or OrderPaymentStatus.Refunded or OrderPaymentStatus.PartiallyRefunded)
        {
            _logger.LogInformation("Fulfil for order {0} was already completed with status {1}.", orderId, payment.Status);
            return payment;
        }

        if (payment.Status == OrderPaymentStatus.Cancelled)
        {
            throw new PaymentException(409, "A cancelled order cannot be fulfilled.");
        }

        if (payment.Status != OrderPaymentStatus.Authorized || string.IsNullOrWhiteSpace(payment.AuthorizationId))
        {
            throw new PaymentException(409, "The order must be paid (authorized) before it can be fulfilled.");
        }

        var authorizationId = payment.AuthorizationId;
        PayPalAuthorizationDetails details;
        try
        {
            details = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (PaymentException ex) when (ex.StatusCode == 404)
        {
            throw new PaymentException(409,
                "PayPal no longer has this authorization. Ask the shopper to pay again, then fulfil the new hold.");
        }

        if (string.Equals(details.Status, "CAPTURED", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(payment.CaptureId))
        {
            return payment;
        }

        if (IsAuthorizationUnusable(details.Status) && !string.Equals(details.Status, "CREATED", StringComparison.OrdinalIgnoreCase))
        {
            authorizationId = await RenewAuthorizationAsync(payment, details, cancellationToken);
        }
        else if (IsAuthorizationStale(details))
        {
            authorizationId = await RenewAuthorizationAsync(payment, details, cancellationToken);
        }

        PayPalCaptureResult capture;
        try
        {
            capture = await _payPal.CaptureAuthorizationAsync(
                authorizationId,
                payment.Amount,
                UniqueInvoiceId(payment),
                $"eshop-capture-{payment.ReferenceKey}",
                cancellationToken);
        }
        catch (PaymentException ex) when (IsExpiredAuthorizationError(ex))
        {
            authorizationId = await RenewAuthorizationAsync(payment, details, cancellationToken);
            capture = await _payPal.CaptureAuthorizationAsync(
                authorizationId,
                payment.Amount,
                UniqueInvoiceId(payment),
                $"eshop-capture-{payment.ReferenceKey}-renewed",
                cancellationToken);
        }

        payment.RecordCapture(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PaypalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        _logger.LogInformation("Captured PayPal payment for order {0}.", orderId);
        return payment;
    }

    public async Task<OrderPayment> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await GetPaymentAsync(orderId, cancellationToken);

        if (payment.Status == OrderPaymentStatus.Cancelled)
        {
            return payment;
        }

        if (payment.Status is OrderPaymentStatus.Captured or OrderPaymentStatus.Refunded or OrderPaymentStatus.PartiallyRefunded)
        {
            throw new PaymentException(409, "A fulfilled order cannot be cancelled. Issue a refund instead.");
        }

        if (payment.Status == OrderPaymentStatus.Authorized && !string.IsNullOrWhiteSpace(payment.AuthorizationId))
        {
            try
            {
                await _payPal.VoidAuthorizationAsync(payment.AuthorizationId, $"eshop-void-{payment.ReferenceKey}", cancellationToken);
            }
            catch (PaymentException ex) when (ex.StatusCode == 404 || ex.Message.Contains("VOIDED", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("PayPal authorization for order {0} was already released.", orderId);
            }
        }

        payment.MarkCancelled();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        _logger.LogInformation("Cancelled order {0} and released any PayPal hold.", orderId);
        return payment;
    }

    public async Task<OrderRefund> RefundAsync(
        int orderId,
        string buyerId,
        bool isAdministrator,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentException(400, "A refund idempotencyKey is required.");
        }

        if (!isAdministrator)
        {
            await GetOwnedOrderAsync(orderId, buyerId, cancellationToken);
        }
        else
        {
            await GetOrderAsync(orderId, cancellationToken);
        }

        var payment = await GetPaymentAsync(orderId, cancellationToken);
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        if (payment.Status == OrderPaymentStatus.Refunded)
        {
            throw new PaymentException(409, "There is no remaining captured amount to refund.");
        }

        if (payment.Status is not (OrderPaymentStatus.Captured or OrderPaymentStatus.PartiallyRefunded))
        {
            throw new PaymentException(409, "Only a fulfilled (captured) order can be refunded.");
        }

        if (string.IsNullOrWhiteSpace(payment.CaptureId))
        {
            throw new PaymentException(409, "This order has no captured PayPal payment to refund.");
        }

        var refundAmount = amount.HasValue
            ? decimal.Round(amount.Value, 2, MidpointRounding.AwayFromZero)
            : payment.RemainingRefundable;

        if (refundAmount <= 0)
        {
            throw new PaymentException(409, "There is no remaining captured amount to refund.");
        }

        if (refundAmount > payment.RemainingRefundable)
        {
            throw new PaymentException(400,
                $"Refund amount {refundAmount} exceeds the remaining captured amount {payment.RemainingRefundable}.");
        }

        var result = await _payPal.RefundCaptureAsync(
            payment.CaptureId,
            amount.HasValue ? refundAmount : null,
            $"{UniqueInvoiceId(payment)}-R-{SanitizeIdempotencyKey(idempotencyKey)}",
            orderId.ToString(),
            idempotencyKey,
            cancellationToken);

        var refund = payment.AddRefund(result.RefundId, result.Status, result.Amount == 0 ? refundAmount : result.Amount, result.Currency, idempotencyKey);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        _logger.LogInformation("Refunded {0} on order {1}.", refundAmount, orderId);
        return refund;
    }

    public async Task<IReadOnlyList<ShopperOrder>> ListMyOrdersAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var payments = await _paymentRepository.ListAsync(
            new OrderPaymentsByOrderIdsSpecification(orders.Select(o => o.Id)), cancellationToken);
        var paymentByOrderId = payments.ToDictionary(p => p.OrderId);
        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(order => new ShopperOrder(order, paymentByOrderId.GetValueOrDefault(order.Id)))
            .ToList();
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new PaymentException(400, "Query parameter 'to' must be on or after 'from'.");
        }

        var paypalTransactions = await ListAllPayPalTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersInDateRangeSpecification(from, to), cancellationToken);
        var payments = await _paymentRepository.ListAsync(new OrderPaymentsWithRefundsSpecification(), cancellationToken);
        var paymentByOrderId = payments.ToDictionary(p => p.OrderId);

        var ordersById = orders.ToDictionary(o => o.Id);
        foreach (var payment in payments)
        {
            if (ordersById.ContainsKey(payment.OrderId))
            {
                continue;
            }

            if (payment.UpdatedAt >= from && payment.UpdatedAt <= to)
            {
                var extra = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpecification(payment.OrderId), cancellationToken);
                if (extra is not null)
                {
                    ordersById[extra.Id] = extra;
                }
            }
        }

        var shopperOrders = ordersById.Values
            .Select(order => new ShopperOrder(order, paymentByOrderId.GetValueOrDefault(order.Id)))
            .ToList();

        var unmatchedOrders = shopperOrders
            .Where(item => item.Payment is not null && HasPayPalIdentifiers(item.Payment))
            .ToList();
        var unmatchedPayPal = paypalTransactions.ToList();
        var matches = new List<ReconciliationMatch>();

        foreach (var transaction in paypalTransactions)
        {
            var order = shopperOrders.FirstOrDefault(item => Matches(item.Payment, transaction));
            if (order is null)
            {
                continue;
            }

            matches.Add(new ReconciliationMatch(order, transaction));
            unmatchedPayPal.Remove(transaction);
            unmatchedOrders.RemoveAll(item => item.Order.Id == order.Order.Id);
        }

        return new ReconciliationReport(from, to, matches, unmatchedPayPal, unmatchedOrders);
    }

    private async Task<string> RenewAuthorizationAsync(
        OrderPayment payment,
        PayPalAuthorizationDetails current,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payment.AuthorizationId))
        {
            throw CannotRenew();
        }

        try
        {
            var renewed = await _payPal.ReauthorizeAsync(
                payment.AuthorizationId,
                payment.Amount,
                $"eshop-reauth-{payment.OrderId}-{payment.AuthorizationId}",
                cancellationToken);

            payment.RefreshAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpirationTime);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            _logger.LogInformation("Renewed PayPal authorization for order {0}.", payment.OrderId);
            return renewed.AuthorizationId;
        }
        catch (PaymentException ex)
        {
            _logger.LogWarning("PayPal reauthorization failed for order {0}: {1}", payment.OrderId, ex.Message);
            throw CannotRenew(ex.Message);
        }
    }

    private static PaymentException CannotRenew(string? paypalMessage = null)
    {
        var suffix = string.IsNullOrWhiteSpace(paypalMessage) ? string.Empty : $" PayPal said: {paypalMessage}";
        return new PaymentException(409,
            "The PayPal authorization can no longer be renewed (the 29-day window has closed or PayPal rejected the reauthorization). " +
            "Do not fulfil this order against the old hold. Ask the shopper to pay again, then fulfil the new authorization." +
            suffix);
    }

    private static bool IsAuthorizationStale(PayPalAuthorizationDetails details)
    {
        if (details.ExpirationTime is { } expiration && expiration <= DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return true;
        }

        return string.Equals(details.Status, "EXPIRED", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAuthorizationUnusable(string status)
    {
        return status.ToUpperInvariant() is "VOIDED" or "DENIED" or "EXPIRED";
    }

    private static bool IsExpiredAuthorizationError(PaymentException exception)
    {
        var message = exception.Message.ToUpperInvariant();
        return message.Contains("AUTHORIZATION_EXPIRED") ||
               message.Contains("AUTH_EXPIRED") ||
               message.Contains("EXPIRED") && message.Contains("AUTHORIZ");
    }

    private async Task<Order> GetOwnedOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentException(403, "This order does not belong to the signed-in shopper.");
        }

        return order;
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpecification(orderId), cancellationToken);
        if (order is null)
        {
            throw new PaymentException(404, $"Order {orderId} was not found.");
        }

        return order;
    }

    private async Task<OrderPayment> GetPaymentAsync(int orderId, CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(orderId), cancellationToken);
        if (payment is null)
        {
            throw new PaymentException(404, $"No payment record exists for order {orderId}.");
        }

        return payment;
    }

    private async Task<IReadOnlyList<PayPalReportedTransaction>> ListAllPayPalTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var results = new List<PayPalReportedTransaction>();
        const int maxWindowDays = 31;
        var windowStart = from;

        while (windowStart <= to)
        {
            var windowEnd = windowStart.AddDays(maxWindowDays);
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            var page = 1;
            int? totalPages = null;
            while (true)
            {
                var pageResult = await _payPal.ListTransactionsAsync(windowStart, windowEnd, page, 100, cancellationToken);
                results.AddRange(pageResult.Transactions);
                totalPages ??= pageResult.TotalPages;

                if (pageResult.Transactions.Count == 0 || pageResult.Transactions.Count < 100)
                {
                    break;
                }

                if (totalPages.HasValue && page >= totalPages.Value)
                {
                    break;
                }

                page++;
                if (page > 1000)
                {
                    throw new PaymentException(502, "Aborted PayPal transaction search after 1000 pages.");
                }
            }

            if (windowEnd == to)
            {
                break;
            }

            windowStart = windowEnd.AddSeconds(1);
        }

        return results;
    }

    private static bool HasPayPalIdentifiers(OrderPayment payment)
    {
        return !string.IsNullOrWhiteSpace(payment.PayPalOrderId) ||
               !string.IsNullOrWhiteSpace(payment.AuthorizationId) ||
               !string.IsNullOrWhiteSpace(payment.CaptureId) ||
               payment.Refunds.Any();
    }

    private static bool Matches(OrderPayment? payment, PayPalReportedTransaction transaction)
    {
        if (payment is null)
        {
            return false;
        }

        var invoicePrefix = InvoicePrefix(payment.OrderId);
        if (!string.IsNullOrWhiteSpace(transaction.InvoiceId) &&
            (string.Equals(transaction.InvoiceId, invoicePrefix, StringComparison.OrdinalIgnoreCase) ||
             transaction.InvoiceId.StartsWith(invoicePrefix + "-", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(transaction.InvoiceId, UniqueInvoiceId(payment), StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(transaction.CustomField) &&
            string.Equals(transaction.CustomField, payment.OrderId.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var identifiers = new[]
            {
                payment.PayPalOrderId,
                payment.AuthorizationId,
                payment.CaptureId
            }
            .Concat(payment.Refunds.Select(r => r.PayPalRefundId))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToArray();

        return identifiers.Any(id =>
            string.Equals(id, transaction.TransactionId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(id, transaction.PaypalReferenceId, StringComparison.OrdinalIgnoreCase));
    }

    internal static string InvoicePrefix(int orderId) => $"ESHOP-{orderId}";

    internal static string UniqueInvoiceId(OrderPayment payment) =>
        $"{InvoicePrefix(payment.OrderId)}-{payment.ReferenceKey}";

    private static string SanitizeIdempotencyKey(string key)
    {
        var sanitized = Regex.Replace(key, @"[^A-Za-z0-9_-]", "-");
        return sanitized.Length <= 40 ? sanitized : sanitized[..40];
    }
}
