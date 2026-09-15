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
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    public const string InvoicePrefix = "ESHOP-";
    private static readonly Address DefaultShipTo = new("123 Main Street", "Seattle", "WA", "US", "98101");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalGateway _payPal;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IUriComposer uriComposer,
        IPayPalGateway payPal)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _uriComposer = uriComposer;
        _payPal = payPal;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<PlaceOrderItem> items,
        Address? shipToAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException(401, "A signed-in shopper is required.");
        }

        if (items == null || items.Count == 0)
        {
            throw new PaymentException(400, "The order must contain at least one catalog item.");
        }

        foreach (var item in items)
        {
            if (item.Quantity <= 0)
            {
                throw new PaymentException(400, $"Quantity for catalog item {item.CatalogItemId} must be greater than zero.");
            }
        }

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        foreach (var id in ids)
        {
            if (!catalogById.ContainsKey(id))
            {
                throw new PaymentException(400, $"Catalog item {id} was not found.");
            }
        }

        var orderItems = items.Select(item =>
        {
            var catalogItem = catalogById[item.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, item.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress ?? DefaultShipTo, orderItems);
        return await _orderRepository.AddAsync(order, cancellationToken);
    }

    public async Task<Order> PayAsync(
        int orderId,
        string buyerId,
        CardPaymentSource? card,
        int? paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        EnsureBuyer(order, buyerId);

        if (order.AlreadyAuthorized())
        {
            return order;
        }

        if (order.AlreadyCancelled())
        {
            throw new PaymentException(409, $"Order {orderId} was cancelled and cannot be paid.");
        }

        if (card == null && paymentMethodId == null)
        {
            throw new PaymentException(400, "Provide card details or a saved paymentMethodId.");
        }

        if (card != null && paymentMethodId != null)
        {
            throw new PaymentException(400, "Provide either card details or a saved paymentMethodId, not both.");
        }

        var amount = Money.ToCents(order.Total());
        if (amount <= 0)
        {
            throw new PaymentException(400, "The order total must be greater than zero.");
        }

        var invoiceId = UniqueInvoiceId(order, "A");
        var customId = CustomIdFor(order);
        var idempotencyKey = $"eshop-pay-{order.Id}-{order.OrderDate.UtcTicks}";
        PayPalAuthorizationResult authorization;

        if (paymentMethodId.HasValue)
        {
            var saved = await _paymentMethodRepository.GetByIdAsync(paymentMethodId.Value, cancellationToken);
            if (saved == null || !saved.BelongsTo(buyerId))
            {
                throw new PaymentException(404, "Saved payment method was not found.");
            }

            authorization = await _payPal.AuthorizeVaultedCardPaymentAsync(
                invoiceId, customId, amount, saved.PayPalPaymentTokenId, idempotencyKey, cancellationToken);
        }
        else
        {
            authorization = await _payPal.AuthorizeCardPaymentAsync(
                invoiceId, customId, amount, card!, idempotencyKey, cancellationToken);
        }

        if (Money.ToCents(authorization.Amount) != amount)
        {
            throw new PaymentException(502,
                $"PayPal authorized {authorization.Amount:0.00} but the order total is {amount:0.00}.");
        }

        order.MarkAuthorized(
            authorization.PayPalOrderId,
            authorization.AuthorizationId,
            authorization.Status,
            authorization.AuthorizedAt,
            authorization.ExpiresAt,
            authorization.Currency);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.AlreadyFulfilled())
        {
            return order;
        }

        if (order.Status != OrderStatus.Authorized || string.IsNullOrWhiteSpace(order.Payment.AuthorizationId))
        {
            throw new PaymentException(409, $"Order {orderId} cannot be fulfilled because it is {order.Status}.");
        }

        var amount = Money.ToCents(order.Total());
        var now = DateTimeOffset.UtcNow;
        var originalAuthAt = order.Payment.OriginalAuthorizedAt ?? order.Payment.AuthorizedAt ?? now;
        var authorizationPeriodEnd = originalAuthAt.AddDays(29);

        if (now >= authorizationPeriodEnd)
        {
            throw new PaymentException(422,
                $"The PayPal authorization for order {orderId} expired on {authorizationPeriodEnd:u} and can no longer be renewed. Ask the shopper to pay again.");
        }

        var authorizationId = await EnsureFreshAuthorizationAsync(order, amount, now, cancellationToken);
        PayPalCaptureResult capture;
        try
        {
            capture = await CaptureOrderAsync(order, authorizationId, amount, cancellationToken);
        }
        catch (PaymentException ex) when (IsStaleAuthorization(ex))
        {
            if (now >= authorizationPeriodEnd)
            {
                throw new PaymentException(422,
                    $"The PayPal authorization for order {orderId} can no longer be renewed. Ask the shopper to pay again. {ex.Message}");
            }

            authorizationId = await ReauthorizeOrderAsync(order, amount, cancellationToken);
            capture = await CaptureOrderAsync(order, authorizationId, amount, cancellationToken, retry: true);
        }

        order.MarkFulfilled(
            capture.CaptureId,
            capture.Status,
            capture.CapturedAmount,
            capture.PayPalFee,
            capture.NetAmount);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.AlreadyCancelled())
        {
            return order;
        }

        if (order.AlreadyFulfilled())
        {
            throw new PaymentException(409,
                $"Order {orderId} has already been fulfilled and cannot be cancelled. Issue a refund instead.");
        }

        if (!string.IsNullOrWhiteSpace(order.Payment.OriginalAuthorizationId))
        {
            await _payPal.VoidAuthorizationAsync(order.Payment.OriginalAuthorizationId, cancellationToken);
            order.MarkCancelled("VOIDED");
        }
        else
        {
            order.MarkCancelled();
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<OrderRefund> RefundAsync(
        int orderId,
        string idempotencyKey,
        decimal? amount,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentException(400, "An idempotency key is required for refunds.");
        }

        var order = await GetOrderAsync(orderId, cancellationToken);
        var existing = order.Payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        if (string.IsNullOrWhiteSpace(order.Payment.CaptureId) || order.Payment.CapturedAmount is null)
        {
            throw new PaymentException(409, $"Order {orderId} has no captured payment to refund.");
        }

        var remaining = order.Payment.RemainingRefundableAmount();
        var refundAmount = amount.HasValue ? Money.ToCents(amount.Value) : remaining;
        if (refundAmount <= 0)
        {
            throw new PaymentException(409, $"Order {orderId} has no remaining captured amount to refund.");
        }

        if (refundAmount > remaining)
        {
            throw new PaymentException(409,
                $"Refund of {refundAmount:0.00} exceeds the remaining captured amount of {remaining:0.00}.");
        }

        var isFullRefund = refundAmount == remaining;
        var result = await _payPal.RefundCaptureAsync(
            order.Payment.CaptureId,
            isFullRefund ? null : refundAmount,
            order.Payment.Currency ?? _payPal.Currency,
            $"eshop-refund-{order.Id}-{idempotencyKey}",
            cancellationToken);

        var refund = order.MarkRefunded(idempotencyKey, result.RefundId, result.Status, refundAmount);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return refund;
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        return orders;
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new PaymentException(400, "The reconciliation 'to' timestamp must be on or after 'from'.");
        }

        var paypalTransactions = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentSpecification(), cancellationToken);

        var eshopPayments = orders
            .Where(o => HasPaymentActivity(o, from, to))
            .Select(ToEshopRecord)
            .ToList();

        var matches = new List<ReconciledMatch>();
        var matchedPaypalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in paypalTransactions)
        {
            var order = FindMatchingOrder(orders, txn);
            if (order == null)
            {
                continue;
            }

            var reason = DescribeMatch(order, txn);
            matches.Add(new ReconciledMatch(order.Id, txn.TransactionId, reason));
            if (!string.IsNullOrWhiteSpace(txn.TransactionId))
            {
                matchedPaypalIds.Add(txn.TransactionId);
            }

            matchedOrderIds.Add(order.Id);
        }

        var paypalOnly = paypalTransactions
            .Where(t => !string.IsNullOrWhiteSpace(t.TransactionId) && !matchedPaypalIds.Contains(t.TransactionId))
            .ToList();

        var eshopOnly = eshopPayments
            .Where(p => !matchedOrderIds.Contains(p.OrderId))
            .ToList();

        return new ReconciliationReport(from, to, matches, paypalOnly, eshopOnly, paypalTransactions, eshopPayments);
    }

    private async Task<string> EnsureFreshAuthorizationAsync(
        Order order,
        decimal amount,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var authorizationId = order.Payment.AuthorizationId!;
        var honorEnds = order.Payment.HonorPeriodEndsAt ?? order.Payment.AuthorizedAt?.AddDays(3);

        try
        {
            var details = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
            if (string.Equals(details.Status, "VOIDED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(details.Status, "EXPIRED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(details.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentException(422,
                    $"The PayPal authorization for order {order.Id} is {details.Status} and cannot be captured. Ask the shopper to pay again.");
            }

            if (details.ExpirationTime.HasValue && details.ExpirationTime.Value <= now)
            {
                return await ReauthorizeOrderAsync(order, amount, cancellationToken);
            }
        }
        catch (PaymentException ex) when (ex.StatusCode == 404)
        {
            throw new PaymentException(422,
                $"The PayPal authorization for order {order.Id} is no longer available. Ask the shopper to pay again.");
        }

        if (honorEnds.HasValue && now >= honorEnds.Value)
        {
            return await ReauthorizeOrderAsync(order, amount, cancellationToken);
        }

        return authorizationId;
    }

    private async Task<string> ReauthorizeOrderAsync(Order order, decimal amount, CancellationToken cancellationToken)
    {
        var originalId = order.Payment.OriginalAuthorizationId ?? order.Payment.AuthorizationId;
        if (string.IsNullOrWhiteSpace(originalId))
        {
            throw new PaymentException(422,
                $"Order {order.Id} has no PayPal authorization that can be renewed. Ask the shopper to pay again.");
        }

        try
        {
            var reauth = await _payPal.ReauthorizeAsync(
                originalId,
                amount,
                $"eshop-reauth-{order.Id}-{order.OrderDate.UtcTicks}",
                cancellationToken);

            order.MarkReauthorized(reauth.AuthorizationId, reauth.Status, reauth.AuthorizedAt, reauth.ExpiresAt);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return reauth.AuthorizationId;
        }
        catch (PaymentException ex)
        {
            throw new PaymentException(422,
                $"The PayPal authorization for order {order.Id} could not be renewed. Ask the shopper to pay again. {ex.Message}");
        }
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            throw new PaymentException(404, $"Order {orderId} was not found.");
        }

        return order;
    }

    private static void EnsureBuyer(Order order, string buyerId)
    {
        if (!order.BelongsTo(buyerId))
        {
            throw new PaymentException(404, $"Order {order.Id} was not found.");
        }
    }

    private static string CustomIdFor(Order order) => $"{InvoicePrefix}{order.Id}";

    private static string UniqueInvoiceId(Order order, string stage) =>
        $"{InvoicePrefix}{order.Id}-{stage}-{Guid.NewGuid():N}";

    private async Task<PayPalCaptureResult> CaptureOrderAsync(
        Order order,
        string authorizationId,
        decimal amount,
        CancellationToken cancellationToken,
        bool retry = false)
    {
        var requestId = retry
            ? $"eshop-capture-{order.Id}-{order.OrderDate.UtcTicks}-retry"
            : $"eshop-capture-{order.Id}-{order.OrderDate.UtcTicks}";

        var capture = await _payPal.CaptureAuthorizationAsync(
            authorizationId,
            amount,
            UniqueInvoiceId(order, "C"),
            requestId,
            cancellationToken);

        if (Money.ToCents(capture.CapturedAmount) != amount)
        {
            capture = await _payPal.CaptureAuthorizationAsync(
                authorizationId,
                amount,
                UniqueInvoiceId(order, "C"),
                $"{requestId}-{Guid.NewGuid():N}",
                cancellationToken);
        }

        if (Money.ToCents(capture.CapturedAmount) != amount)
        {
            throw new PaymentException(502,
                $"PayPal captured {capture.CapturedAmount:0.00} but the order total is {amount:0.00}.");
        }

        return capture;
    }

    private static bool InvoiceBelongsToOrder(string? invoiceId, Order order)
    {
        if (string.IsNullOrWhiteSpace(invoiceId))
        {
            return false;
        }

        var prefix = CustomIdFor(order);
        if (string.Equals(invoiceId, prefix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return System.Text.RegularExpressions.Regex.IsMatch(
            invoiceId,
            $"^{System.Text.RegularExpressions.Regex.Escape(prefix)}-[AC]-[0-9a-fA-F]{{32}}$");
    }

    private static bool IsStaleAuthorization(PaymentException exception) =>
        exception.Message.Contains("AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase)
        || exception.Message.Contains("AUTHORIZATION_DENIED", StringComparison.OrdinalIgnoreCase)
        || exception.Message.Contains("expired", StringComparison.OrdinalIgnoreCase);

    private static bool HasPaymentActivity(Order order, DateTimeOffset from, DateTimeOffset to)
    {
        if (string.IsNullOrWhiteSpace(order.Payment.PayPalOrderId) &&
            string.IsNullOrWhiteSpace(order.Payment.AuthorizationId) &&
            string.IsNullOrWhiteSpace(order.Payment.CaptureId))
        {
            return false;
        }

        if (order.Payment.AuthorizedAt is { } authorized && authorized >= from && authorized <= to)
        {
            return true;
        }

        if (order.OrderDate >= from && order.OrderDate <= to &&
            (!string.IsNullOrWhiteSpace(order.Payment.PayPalOrderId) ||
             !string.IsNullOrWhiteSpace(order.Payment.AuthorizationId) ||
             !string.IsNullOrWhiteSpace(order.Payment.CaptureId)))
        {
            return true;
        }

        foreach (var refund in order.Payment.Refunds)
        {
            if (refund.CreatedAt >= from && refund.CreatedAt <= to)
            {
                return true;
            }
        }

        return false;
    }

    private static EshopPaymentRecord ToEshopRecord(Order order) =>
        new(
            order.Id,
            order.Status,
            order.Payment.PayPalOrderId,
            order.Payment.AuthorizationId,
            order.Payment.CaptureId,
            order.Payment.CapturedAmount,
            order.Payment.Refunds.Select(r => r.PayPalRefundId).ToList());

    private static Order? FindMatchingOrder(IReadOnlyList<Order> orders, PayPalReportedTransaction txn)
    {
        foreach (var order in orders)
        {
            if (Matches(order, txn))
            {
                return order;
            }
        }

        return null;
    }

    private static bool Matches(Order order, PayPalReportedTransaction txn)
    {
        if (!string.IsNullOrWhiteSpace(txn.InvoiceId) && InvoiceBelongsToOrder(txn.InvoiceId, order))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(txn.CustomField) && InvoiceBelongsToOrder(txn.CustomField, order))
        {
            return true;
        }

        return SameId(txn.TransactionId, order.Payment.PayPalOrderId)
               || SameId(txn.TransactionId, order.Payment.AuthorizationId)
               || SameId(txn.TransactionId, order.Payment.OriginalAuthorizationId)
               || SameId(txn.TransactionId, order.Payment.CaptureId)
               || order.Payment.Refunds.Any(r => SameId(txn.TransactionId, r.PayPalRefundId))
               || SameId(txn.PayPalReferenceId, order.Payment.PayPalOrderId)
               || SameId(txn.PayPalReferenceId, order.Payment.CaptureId)
               || SameId(txn.PayPalReferenceId, order.Payment.AuthorizationId)
               || SameId(txn.PayPalReferenceId, order.Payment.OriginalAuthorizationId)
               || order.Payment.Refunds.Any(r => SameId(txn.PayPalReferenceId, r.PayPalRefundId));
    }

    private static string DescribeMatch(Order order, PayPalReportedTransaction txn)
    {
        if (InvoiceBelongsToOrder(txn.InvoiceId, order))
        {
            return "invoice_id";
        }

        if (SameId(txn.TransactionId, order.Payment.CaptureId))
        {
            return "capture_id";
        }

        if (SameId(txn.TransactionId, order.Payment.AuthorizationId) ||
            SameId(txn.TransactionId, order.Payment.OriginalAuthorizationId))
        {
            return "authorization_id";
        }

        if (order.Payment.Refunds.Any(r => SameId(txn.TransactionId, r.PayPalRefundId)))
        {
            return "refund_id";
        }

        return "paypal_reference";
    }

    private static bool SameId(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
