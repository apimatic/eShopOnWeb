using System;
using System.Collections.Generic;
using System.Globalization;
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
    private static readonly Address DefaultShipTo =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalGateway _payPal;
    private readonly IPaymentConfiguration _paymentConfiguration;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IUriComposer uriComposer,
        IPayPalGateway payPal,
        IPaymentConfiguration paymentConfiguration)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _uriComposer = uriComposer;
        _payPal = payPal;
        _paymentConfiguration = paymentConfiguration;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineRequest> items,
        Address? shipTo,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items == null || items.Count == 0)
        {
            throw new PaymentException(400, "An order must contain at least one catalog item.");
        }

        var quantities = new Dictionary<int, int>();
        foreach (var line in items)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentException(400, "Quantity must be greater than zero.");
            }

            quantities[line.CatalogItemId] = quantities.TryGetValue(line.CatalogItemId, out var existing)
                ? existing + line.Quantity
                : line.Quantity;
        }

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(quantities.Keys.ToArray()), cancellationToken);

        var missing = quantities.Keys.Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
        {
            throw new PaymentException(400, $"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var orderItems = catalogItems.Select(catalogItem =>
        {
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, quantities[catalogItem.Id]);
        }).ToList();

        var order = new Order(buyerId, shipTo ?? DefaultShipTo, orderItems);
        return await _orderRepository.AddAsync(order, cancellationToken);
    }

    public async Task<Order> PayAsync(
        string buyerId,
        int orderId,
        PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        EnsureBuyer(order, buyerId);

        if (order.Status is OrderStatus.Authorized or OrderStatus.Fulfilled
            or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            return order;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentException(409, "A cancelled order cannot be paid.");
        }

        var currency = RequireCurrency();
        var amount = order.Total();
        var idempotencyKey = order.Payment.EnsureAuthorizeRequestId();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        PayPalAuthorizationResult authorization;
        if (request.PaymentMethodId.HasValue)
        {
            if (request.Card != null)
            {
                throw new PaymentException(400, "Provide either a saved payment method or card details, not both.");
            }

            var saved = await GetUsableSavedCardAsync(buyerId, request.PaymentMethodId.Value, cancellationToken);
            authorization = await _payPal.AuthorizeSavedCardAsync(
                order.Id, amount, currency, saved.PayPalTokenId, idempotencyKey, cancellationToken);
        }
        else if (request.Card != null)
        {
            authorization = await _payPal.AuthorizeCardAsync(
                order.Id, amount, currency, CardInputNormalizer.Normalize(request.Card), idempotencyKey, cancellationToken);
        }
        else
        {
            throw new PaymentException(400, "Provide card details or a saved paymentMethodId.");
        }

        if (authorization.PayerActionRequired)
        {
            throw new PayerActionRequiredException(authorization.PayPalOrderId);
        }

        order.RecordAuthorization(
            authorization.PayPalOrderId,
            authorization.AuthorizationId,
            authorization.AuthorizationStatus,
            authorization.Expiration,
            currency);

        if (authorization.ExistingCapture != null)
        {
            order.Payment.NoteCaptureId(authorization.ExistingCapture.CaptureId);
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded
            && !string.IsNullOrEmpty(order.Payment.CaptureId))
        {
            return order;
        }

        if (order.Status != OrderStatus.Authorized || string.IsNullOrEmpty(order.Payment.AuthorizationId))
        {
            throw new PaymentException(409, "An order must be authorized before it can be fulfilled.");
        }

        var currency = order.Payment.Currency ?? RequireCurrency();
        var amount = order.Total();
        var authorizationId = order.Payment.AuthorizationId;

        if (!string.IsNullOrEmpty(order.Payment.CaptureId))
        {
            var existing = await _payPal.GetCaptureAsync(order.Payment.CaptureId, cancellationToken);
            order.RecordCapture(
                existing.CaptureId,
                existing.Status,
                existing.CapturedAmount,
                existing.PaypalFee,
                existing.NetAmount);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        }

        if (order.Payment.AuthorizationLooksStale(DateTimeOffset.UtcNow))
        {
            authorizationId = await RenewAuthorizationAsync(order, authorizationId, amount, currency, cancellationToken);
        }

        PayPalCaptureResult capture;
        var captureKey = order.Payment.EnsureCaptureRequestId();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        try
        {
            capture = await _payPal.CaptureAsync(
                authorizationId,
                amount,
                currency,
                captureKey,
                cancellationToken);
        }
        catch (PaymentException ex) when (IsAlreadyCaptured(ex) && !string.IsNullOrEmpty(order.Payment.PayPalOrderId))
        {
            var recovered = await _payPal.FindCaptureForPayPalOrderAsync(order.Payment.PayPalOrderId, cancellationToken);
            if (recovered == null)
            {
                throw new PaymentException(409, "PayPal reports this authorization was already captured, but no capture id is available to record.");
            }

            capture = recovered;
        }
        catch (PaymentException ex) when (IsStaleAuthorizationFailure(ex))
        {
            authorizationId = await RenewAuthorizationAsync(order, authorizationId, amount, currency, cancellationToken);
            capture = await _payPal.CaptureAsync(
                authorizationId,
                amount,
                currency,
                captureKey,
                cancellationToken);
        }

        order.RecordCapture(
            capture.CaptureId,
            capture.Status,
            capture.CapturedAmount,
            capture.PaypalFee,
            capture.NetAmount);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }

        if (!string.IsNullOrEmpty(order.Payment.AuthorizationId)
            && order.Status == OrderStatus.Authorized)
        {
            try
            {
                var voidKey = order.Payment.EnsureVoidRequestId();
                await _orderRepository.UpdateAsync(order, cancellationToken);
                await _payPal.VoidAsync(
                    order.Payment.AuthorizationId,
                    voidKey,
                    cancellationToken);
            }
            catch (PaymentException ex) when (ex.StatusCode is 404 or 409 or 422)
            {
                // Already voided or no longer voidable; still record the local cancel when PayPal has released the hold.
            }
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<OrderRefund> RefundAsync(
        string buyerId,
        int orderId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await GetOrderAsync(orderId, cancellationToken);
        EnsureBuyer(order, buyerId);

        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        if (string.IsNullOrEmpty(order.Payment.CaptureId))
        {
            throw new PaymentException(409, "A captured payment is required before a refund can be issued.");
        }

        var remaining = order.RefundableRemaining();
        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0m)
        {
            throw new PaymentException(409, "There is no remaining captured amount to refund.");
        }

        if (refundAmount > remaining)
        {
            throw new PaymentException(409, $"Refund of {refundAmount} exceeds the remaining captured amount of {remaining}.");
        }

        var currency = order.Payment.Currency ?? RequireCurrency();
        var isFull = refundAmount == remaining && remaining == (order.Payment.CapturedAmount ?? remaining);

        var result = await _payPal.RefundAsync(
            order.Payment.CaptureId,
            isFull && amount == null ? null : refundAmount,
            currency,
            idempotencyKey,
            cancellationToken);

        var refund = order.RecordRefund(result.RefundId, idempotencyKey, result.Amount, result.Status);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return refund;
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to < from)
        {
            throw new PaymentException(400, "`to` must be greater than or equal to `from`.");
        }

        var paypalTransactions = await _payPal.SearchTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersWithPayPalActivitySpec(), cancellationToken);

        var matches = new List<ReconciliationMatch>();
        var paypalOnly = new List<PayPalReportedTransaction>();
        var matchedOrderIds = new HashSet<int>();
        var matchedTxnIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var txn in paypalTransactions)
        {
            var match = FindMatchingOrder(orders, txn);
            if (match == null)
            {
                paypalOnly.Add(txn);
                continue;
            }

            matchedOrderIds.Add(match.Value.Order.Id);
            if (!string.IsNullOrEmpty(txn.TransactionId))
            {
                matchedTxnIds.Add(txn.TransactionId);
            }

            matches.Add(new ReconciliationMatch(match.Value.Order.Id, txn.TransactionId, match.Value.Reason));
        }

        var eshopOnly = orders
            .Where(o => !matchedOrderIds.Contains(o.Id))
            .Where(o => IsInRange(o, from, to))
            .Select(o => new ReconciliationOrderRow(
                o.Id,
                o.Status,
                o.Payment.PayPalOrderId,
                o.Payment.AuthorizationId,
                o.Payment.CaptureId,
                o.Total()))
            .ToList();

        return new ReconciliationReport(from, to, matches, paypalOnly, eshopOnly);
    }

    private async Task<string> RenewAuthorizationAsync(
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
                order.Payment.EnsureReauthorizeRequestId(),
                cancellationToken);

            order.RecordReauthorization(renewed.AuthorizationId, renewed.AuthorizationStatus, renewed.Expiration);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return renewed.AuthorizationId;
        }
        catch (PaymentException ex) when (ex.StatusCode is 400 or 422 or 409)
        {
            throw new AuthorizationNotRenewableException(ex.Issue);
        }
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            throw new OrderNotFoundException(orderId);
        }

        return order;
    }

    private async Task<SavedPaymentMethod> GetUsableSavedCardAsync(
        string buyerId,
        int paymentMethodId,
        CancellationToken cancellationToken)
    {
        var saved = await _paymentMethodRepository.GetByIdAsync(paymentMethodId, cancellationToken);
        if (saved == null || saved.IsDeleted)
        {
            throw new PaymentMethodNotFoundException(paymentMethodId);
        }

        if (!string.Equals(saved.BuyerId, buyerId, StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentMethodAccessDeniedException();
        }

        return saved;
    }

    private static void EnsureBuyer(Order order, string buyerId)
    {
        if (!order.BelongsTo(buyerId))
        {
            throw new OrderAccessDeniedException();
        }
    }

    private string RequireCurrency()
    {
        if (string.IsNullOrWhiteSpace(_paymentConfiguration.Currency))
        {
            throw new PaymentException(500, "PayPal:Currency is not configured.");
        }

        return _paymentConfiguration.Currency;
    }

    private static bool IsStaleAuthorizationFailure(PaymentException ex)
    {
        var issue = ex.Issue ?? string.Empty;
        var message = ex.Message ?? string.Empty;
        return ex.StatusCode is 400 or 422
            && (issue.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase)
                || issue.Contains("HONOR", StringComparison.OrdinalIgnoreCase)
                || message.Contains("expired", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAlreadyCaptured(PaymentException ex)
    {
        var issue = ex.Issue ?? string.Empty;
        var message = ex.Message ?? string.Empty;
        return issue.Contains("CAPTURED", StringComparison.OrdinalIgnoreCase)
            || issue.Contains("DUPLICATE_INVOICE", StringComparison.OrdinalIgnoreCase)
            || message.Contains("already captured", StringComparison.OrdinalIgnoreCase);
    }

    private static (Order Order, string Reason)? FindMatchingOrder(
        IReadOnlyList<Order> orders,
        PayPalReportedTransaction txn)
    {
        foreach (var order in orders)
        {
            if (MatchesId(txn.InvoiceId, order.Payment.AuthorizeRequestId)
                || MatchesId(txn.CustomField, order.Payment.AuthorizeRequestId)
                || MatchesId(txn.InvoiceId, order.Payment.CaptureRequestId)
                || MatchesId(txn.CustomField, order.Id.ToString(CultureInfo.InvariantCulture)))
            {
                return (order, "invoice_or_custom_id");
            }

            if (MatchesId(txn.TransactionId, order.Payment.CaptureId)
                || MatchesId(txn.PaypalReferenceId, order.Payment.CaptureId))
            {
                return (order, "capture_id");
            }

            if (MatchesId(txn.TransactionId, order.Payment.AuthorizationId)
                || MatchesId(txn.PaypalReferenceId, order.Payment.AuthorizationId))
            {
                return (order, "authorization_id");
            }

            if (MatchesId(txn.TransactionId, order.Payment.PayPalOrderId)
                || MatchesId(txn.PaypalReferenceId, order.Payment.PayPalOrderId))
            {
                return (order, "paypal_order_id");
            }

            foreach (var refund in order.Refunds)
            {
                if (MatchesId(txn.TransactionId, refund.PayPalRefundId)
                    || MatchesId(txn.PaypalReferenceId, refund.PayPalRefundId))
                {
                    return (order, "refund_id");
                }
            }
        }

        return null;
    }

    private static bool MatchesId(string? left, string? right) =>
        !string.IsNullOrEmpty(left)
        && !string.IsNullOrEmpty(right)
        && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool IsInRange(Order order, DateTimeOffset from, DateTimeOffset to) =>
        order.OrderDate >= from && order.OrderDate <= to;
}
