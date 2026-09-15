using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaidOrderService : IPaidOrderService
{
    private static readonly Address DefaultShipTo =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPayPalGateway _payPal;
    private readonly IUriComposer _uriComposer;

    public PaidOrderService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPayPalGateway payPal,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
    }

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> items, Address? shipToAddress)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new OrderPaymentException(401, "The caller is not authenticated.");
        }

        if (items == null || items.Count == 0)
        {
            throw new OrderPaymentException(400, "The order must contain at least one catalog item.");
        }

        foreach (var line in items)
        {
            if (line.CatalogItemId <= 0 || line.Quantity <= 0)
            {
                throw new OrderPaymentException(400, "Each order line must include a catalog item id and a quantity greater than zero.");
            }
        }

        var catalogIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogIds));
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var missing = catalogIds.Where(id => !catalogById.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            throw new OrderPaymentException(400, $"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = new List<OrderItem>();
        foreach (var line in items)
        {
            var catalogItem = catalogById[line.CatalogItemId];
            var pictureUri = string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                ? "none"
                : _uriComposer.ComposePicUri(catalogItem.PictureUri);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress ?? DefaultShipTo, orderItems);
        order.MarkAwaitingPayment(RequireCurrency());
        await _orderRepository.AddAsync(order);
        return order;
    }

    public async Task<Order> PayAsync(int orderId, string buyerId, CardPaymentSource? card, int? paymentMethodId)
    {
        var order = await GetRequiredOrderAsync(orderId);
        EnsureOwner(order, buyerId);

        if (order.Status == OrderStatus.Authorized ||
            order.Status == OrderStatus.Fulfilled ||
            order.Status == OrderStatus.PartiallyRefunded ||
            order.Status == OrderStatus.Refunded)
        {
            return order;
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new OrderPaymentException(409, $"Order {orderId} cannot be paid while it is {order.Status}.");
        }

        if (card != null && paymentMethodId.HasValue)
        {
            throw new OrderPaymentException(400, "Provide either card details or a saved payment method, not both.");
        }

        string? vaultId = null;
        if (paymentMethodId.HasValue)
        {
            var saved = await _paymentMethodRepository.GetByIdAsync(paymentMethodId.Value);
            if (saved == null || !saved.BelongsTo(buyerId))
            {
                throw new OrderPaymentException(404, "The saved payment method was not found.");
            }

            vaultId = saved.PayPalVaultId;
        }
        else if (card == null)
        {
            throw new OrderPaymentException(400, "Provide card details or a saved payment method id.");
        }

        var currency = order.Currency ?? RequireCurrency();
        var amount = MoneyFormatter.Round(order.Total(), currency);
        if (amount <= 0)
        {
            throw new OrderPaymentException(400, "The order total must be greater than zero.");
        }

        order.EnsureAuthorizeRequestId();
        await _orderRepository.UpdateAsync(order);

        var authorization = await _payPal.AuthorizeAsync(
            amount,
            currency,
            invoiceId: order.AuthorizeRequestId!,
            customId: order.Id.ToString(),
            idempotencyKey: order.AuthorizeRequestId!,
            card,
            vaultId);

        if (!MoneyFormatter.AmountsEqual(authorization.Amount, amount, currency))
        {
            throw new OrderPaymentException(502,
                $"PayPal authorized {authorization.Amount} {authorization.Currency}, which does not match the order total {amount} {currency}.");
        }

        order.RecordAuthorization(
            authorization.PayPalOrderId ?? string.Empty,
            authorization.AuthorizationId,
            authorization.Status,
            authorization.ExpirationTime);
        await _orderRepository.UpdateAsync(order);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId)
    {
        var order = await GetRequiredOrderAsync(orderId);

        if (order.Status == OrderStatus.Fulfilled ||
            order.Status == OrderStatus.PartiallyRefunded ||
            order.Status == OrderStatus.Refunded)
        {
            return order;
        }

        if (order.Status != OrderStatus.Authorized)
        {
            throw new OrderPaymentException(409, $"Order {orderId} cannot be fulfilled while it is {order.Status}.");
        }

        if (string.IsNullOrEmpty(order.PayPalAuthorizationId))
        {
            throw new OrderPaymentException(409, "Order has no PayPal authorization to capture.");
        }

        var currency = order.Currency ?? RequireCurrency();
        var authorizationId = await EnsureFreshAuthorizationAsync(order, currency);

        PayPalCaptureResult capture;
        try
        {
            capture = await CaptureAuthorizedPaymentAsync(order, authorizationId, currency);
        }
        catch (OrderPaymentException ex) when (IsStaleAuthorization(ex.Message))
        {
            authorizationId = await RenewAuthorizationAsync(order, currency);
            order.RotateCaptureRequestId();
            await _orderRepository.UpdateAsync(order);
            capture = await CaptureAuthorizedPaymentAsync(order, authorizationId, currency);
        }

        order.RecordCapture(
            capture.CaptureId,
            capture.Status,
            capture.CapturedAmount,
            capture.PayPalFee,
            capture.NetAmount);
        await _orderRepository.UpdateAsync(order);
        return order;
    }

    private async Task<PayPalCaptureResult> CaptureAuthorizedPaymentAsync(Order order, string authorizationId, string currency)
    {
        order.EnsureCaptureRequestId();
        await _orderRepository.UpdateAsync(order);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var captured = await _payPal.CaptureAsync(
                authorizationId,
                invoiceId: order.CaptureRequestId!,
                idempotencyKey: order.CaptureRequestId!);

            PayPalCaptureResult detailed;
            try
            {
                detailed = await _payPal.GetCaptureAsync(captured.CaptureId);
            }
            catch (OrderPaymentException)
            {
                detailed = captured;
            }

            var relatedAuth = detailed.AuthorizationId ?? captured.AuthorizationId;
            var capturedAmount = detailed.CapturedAmount > 0m ? detailed.CapturedAmount : captured.CapturedAmount;
            var amountMatches = capturedAmount > 0m
                && MoneyFormatter.AmountsEqual(capturedAmount, order.Total(), currency);
            var authMatches = string.IsNullOrEmpty(relatedAuth)
                || string.Equals(relatedAuth, authorizationId, StringComparison.OrdinalIgnoreCase);

            if (authMatches && amountMatches)
            {
                var fee = detailed.PayPalFee > 0m ? detailed.PayPalFee : captured.PayPalFee;
                var net = detailed.NetAmount > 0m ? detailed.NetAmount : captured.NetAmount;
                if (net <= 0m || net > capturedAmount)
                {
                    net = capturedAmount - fee;
                    if (net < 0m)
                    {
                        net = capturedAmount;
                    }
                }

                return detailed with
                {
                    CapturedAmount = capturedAmount,
                    PayPalFee = fee,
                    NetAmount = net,
                    AuthorizationId = relatedAuth ?? authorizationId
                };
            }

            order.RotateCaptureRequestId();
            await _orderRepository.UpdateAsync(order);
        }

        throw new OrderPaymentException(502,
            "PayPal returned a capture that does not belong to this order's authorization. Fulfilment was not recorded; retry the request.");
    }

    public async Task<Order> CancelAsync(int orderId)
    {
        var order = await GetRequiredOrderAsync(orderId);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            throw new OrderPaymentException(409, "A fulfilled order cannot be cancelled. Issue a refund instead.");
        }

        if (order.Status is not OrderStatus.AwaitingPayment and not OrderStatus.Authorized)
        {
            throw new OrderPaymentException(409, $"Order {orderId} cannot be cancelled while it is {order.Status}.");
        }

        if (!string.IsNullOrEmpty(order.PayPalAuthorizationId) && order.Status == OrderStatus.Authorized)
        {
            await _payPal.VoidAuthorizationAsync(order.PayPalAuthorizationId);
        }

        order.RecordCancellation();
        await _orderRepository.UpdateAsync(order);
        return order;
    }

    public async Task<Order> RefundAsync(int orderId, string buyerId, string idempotencyKey, decimal? amount)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new OrderPaymentException(400, "A refund idempotency key is required.");
        }

        var order = await GetRequiredOrderAsync(orderId);
        EnsureOwner(order, buyerId);

        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return order;
        }

        if (order.Status is not OrderStatus.Fulfilled and not OrderStatus.PartiallyRefunded)
        {
            throw new OrderPaymentException(409, $"Order {orderId} cannot be refunded while it is {order.Status}.");
        }

        if (string.IsNullOrEmpty(order.PayPalCaptureId))
        {
            throw new OrderPaymentException(409, "Order has no captured PayPal payment to refund.");
        }

        var currency = order.Currency ?? RequireCurrency();
        var remaining = MoneyFormatter.Round(order.RemainingRefundable(), currency);
        if (remaining <= 0)
        {
            throw new OrderPaymentException(409, "This order has already been refunded in full.");
        }

        decimal refundAmount;
        if (amount.HasValue)
        {
            refundAmount = MoneyFormatter.Round(amount.Value, currency);
            if (refundAmount <= 0)
            {
                throw new OrderPaymentException(400, "Refund amount must be greater than zero.");
            }

            if (refundAmount > remaining)
            {
                throw new OrderPaymentException(409,
                    $"Refund of {refundAmount} exceeds the remaining refundable amount of {remaining} {currency}.");
            }
        }
        else
        {
            refundAmount = remaining;
        }

        var result = await _payPal.RefundAsync(
            order.PayPalCaptureId,
            amount.HasValue ? refundAmount : null,
            currency,
            idempotencyKey);

        var recordedAmount = result.Amount > 0 ? result.Amount : refundAmount;
        order.AddRefund(result.RefundId, result.Status, recordedAmount, idempotencyKey);
        await _orderRepository.UpdateAsync(order);
        return order;
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
        return orders;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to)
    {
        if (to < from)
        {
            throw new OrderPaymentException(400, "The reconciliation 'to' timestamp must be on or after 'from'.");
        }

        var paypalTransactions = await _payPal.ListTransactionsAsync(from, to);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentSpecification());

        var matched = new List<ReconciliationMatch>();
        var matchedTransactionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedOrderIds = new HashSet<int>();

        foreach (var transaction in paypalTransactions)
        {
            var order = orders.FirstOrDefault(o => Matches(o, transaction));
            if (order == null)
            {
                continue;
            }

            matched.Add(new ReconciliationMatch { Order = order, Transaction = transaction });
            matchedTransactionIds.Add(transaction.TransactionId);
            matchedOrderIds.Add(order.Id);
        }

        var paypalOnly = paypalTransactions
            .Where(t => !matchedTransactionIds.Contains(t.TransactionId))
            .ToList();

        var eshopOnly = orders
            .Where(o => !matchedOrderIds.Contains(o.Id) && HasPaymentActivityInRange(o, from, to))
            .ToList();

        return new ReconciliationReport
        {
            From = from,
            To = to,
            Matched = matched,
            PayPalOnly = paypalOnly,
            EshopOnly = eshopOnly
        };
    }

    private async Task<string> EnsureFreshAuthorizationAsync(Order order, string currency)
    {
        var authorizationId = order.PayPalAuthorizationId!;
        try
        {
            var current = await _payPal.GetAuthorizationAsync(authorizationId);
            order.ReplaceAuthorization(current.AuthorizationId, current.Status, current.ExpirationTime);
            await _orderRepository.UpdateAsync(order);

            if (string.Equals(current.Status, "EXPIRED", StringComparison.OrdinalIgnoreCase) ||
                (current.ExpirationTime.HasValue && current.ExpirationTime.Value <= DateTimeOffset.UtcNow))
            {
                return await RenewAuthorizationAsync(order, currency);
            }

            return current.AuthorizationId;
        }
        catch (OrderPaymentException)
        {
            return authorizationId;
        }
    }

    private async Task<string> RenewAuthorizationAsync(Order order, string currency)
    {
        try
        {
            var renewed = await _payPal.ReauthorizeAsync(
                order.PayPalAuthorizationId!,
                MoneyFormatter.Round(order.Total(), currency),
                currency);

            order.ReplaceAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpirationTime);
            await _orderRepository.UpdateAsync(order);
            return renewed.AuthorizationId;
        }
        catch (Exception ex) when (ex is not PayerActionRequiredException)
        {
            throw new OrderPaymentException(409,
                "This order's PayPal authorization has expired and cannot be renewed. " +
                "Ask the shopper to place a new order and pay again, then fulfil that order. " +
                "Do not retry capture on this authorization. " +
                $"PayPal said: {ex.Message}");
        }
    }

    private static bool IsStaleAuthorization(string message) =>
        message.Contains("AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("AUTHORIZATION_VOIDED", StringComparison.OrdinalIgnoreCase);

    private async Task<Order> GetRequiredOrderAsync(int orderId)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId));
        if (order == null)
        {
            throw new OrderPaymentException(404, $"Order {orderId} was not found.");
        }

        return order;
    }

    private static void EnsureOwner(Order order, string buyerId)
    {
        if (!order.BelongsTo(buyerId))
        {
            throw new OrderPaymentException(404, $"Order {order.Id} was not found.");
        }
    }

    private static bool Matches(Order order, PayPalReportedTransaction transaction)
    {
        var identifiers = new HashSet<string>(order.PayPalIdentifiers(), StringComparer.OrdinalIgnoreCase);
        identifiers.Add($"ORDER-{order.Id}");
        identifiers.Add(order.Id.ToString());
        if (!string.IsNullOrEmpty(order.AuthorizeRequestId)) identifiers.Add(order.AuthorizeRequestId);
        if (!string.IsNullOrEmpty(order.CaptureRequestId)) identifiers.Add(order.CaptureRequestId);

        return Contains(identifiers, transaction.TransactionId)
            || Contains(identifiers, transaction.ReferenceId)
            || Contains(identifiers, transaction.InvoiceId)
            || Contains(identifiers, transaction.CustomField);
    }

    private static bool Contains(HashSet<string> identifiers, string? value) =>
        !string.IsNullOrWhiteSpace(value) && identifiers.Contains(value.Trim());

    private static bool HasPaymentActivityInRange(Order order, DateTimeOffset from, DateTimeOffset to)
    {
        return InRange(order.AuthorizedAt, from, to)
            || InRange(order.CapturedAt, from, to)
            || InRange(order.CancelledAt, from, to)
            || order.Refunds.Any(r => InRange(r.CreatedAt, from, to))
            || (order.Status != OrderStatus.Placed && InRange(order.OrderDate, from, to));
    }

    private static bool InRange(DateTimeOffset? value, DateTimeOffset from, DateTimeOffset to) =>
        value.HasValue && value.Value >= from && value.Value <= to;

    private string RequireCurrency()
    {
        if (string.IsNullOrWhiteSpace(_payPal.Currency))
        {
            throw new OrderPaymentException(500, "PayPal:Currency is not configured.");
        }

        return _payPal.Currency;
    }
}
