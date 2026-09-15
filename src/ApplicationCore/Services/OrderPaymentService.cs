using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private static readonly TimeSpan HonorPeriod = TimeSpan.FromDays(3);
    private static readonly TimeSpan ReauthorizeWindow = TimeSpan.FromDays(30);

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalog;
    private readonly IRepository<Buyer> _buyers;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalGateway _payPal;
    private readonly PayPalOptions _payPalOptions;

    public OrderPaymentService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalog,
        IRepository<Buyer> buyers,
        IUriComposer uriComposer,
        IPayPalGateway payPal,
        PayPalOptions payPalOptions)
    {
        _orders = orders;
        _catalog = catalog;
        _buyers = buyers;
        _uriComposer = uriComposer;
        _payPal = payPal;
        _payPalOptions = payPalOptions;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> items, Address shipTo, CancellationToken ct)
    {
        if (items == null || items.Count == 0)
        {
            throw new CheckoutException(400, "An order must contain at least one item.");
        }

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalog.ListAsync(new CatalogItemsSpecification(ids), ct);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var orderItems = new List<OrderItem>();
        foreach (var line in items)
        {
            if (line.Quantity < 1)
            {
                throw new CheckoutException(400, $"Quantity for catalog item {line.CatalogItemId} must be at least 1.");
            }

            if (!catalogById.TryGetValue(line.CatalogItemId, out var catalogItem))
            {
                throw new CheckoutException(400, $"Catalog item {line.CatalogItemId} was not found.");
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipTo, orderItems);
        await _orders.AddAsync(order, ct);
        return order;
    }

    public async Task<Order> PayAsync(string buyerId, int orderId, int? paymentMethodId, CardPaymentInput? card, CancellationToken ct)
    {
        var order = await LoadOwnedOrder(buyerId, orderId, ct);

        if (order.Status == OrderState.Authorized && !string.IsNullOrEmpty(order.AuthorizationId))
        {
            return order;
        }

        if (order.Status != OrderState.AwaitingPayment)
        {
            throw new CheckoutException(409, $"Order {orderId} cannot be paid in state {order.Status}.");
        }

        if (order.Total() <= 0)
        {
            throw new CheckoutException(400, "Order total must be greater than zero.");
        }

        var currency = RequireCurrency();
        var payPalRequestId = $"order-pay-{order.PaymentIdempotencyKey}";
        AuthorizationResult authorization;

        if (paymentMethodId.HasValue)
        {
            var buyer = await GetBuyer(buyerId, ct) ?? throw new CheckoutException(404, "Saved card was not found.");
            var method = buyer.FindPaymentMethod(paymentMethodId.Value)
                ?? throw new CheckoutException(404, "Saved card was not found.");
            if (string.IsNullOrEmpty(method.CardId))
            {
                throw new CheckoutException(409, "Saved card is no longer usable.");
            }

            authorization = await _payPal.AuthorizeSavedCardAsync(order.Id, order.Total(), currency, payPalRequestId, method.CardId, ct);
        }
        else if (card != null)
        {
            authorization = await _payPal.AuthorizeCardAsync(order.Id, order.Total(), currency, payPalRequestId, card, ct);
        }
        else
        {
            throw new CheckoutException(400, "Provide card details or a saved paymentMethodId.");
        }

        if (!AmountsEqual(authorization.Amount, order.Total()))
        {
            try
            {
                await _payPal.VoidAsync(authorization.AuthorizationId, $"void-mismatch-{order.PaymentIdempotencyKey}", ct);
            }
            catch (CheckoutException)
            {
                // Best-effort release; still refuse the mismatched hold.
            }

            throw new CheckoutException(409,
                $"PayPal held {authorization.Amount.ToString("0.00", CultureInfo.InvariantCulture)} but the order total is {order.Total().ToString("0.00", CultureInfo.InvariantCulture)}.");
        }

        order.RecordAuthorization(
            authorization.PayPalOrderId,
            authorization.AuthorizationId,
            authorization.Status,
            authorization.Amount,
            authorization.CreateTime,
            authorization.ExpirationTime);

        await _orders.UpdateAsync(order, ct);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken ct)
    {
        var order = await LoadOrder(orderId, ct);

        if (order.Status is OrderState.Fulfilled or OrderState.PartiallyRefunded or OrderState.Refunded
            && !string.IsNullOrEmpty(order.CaptureId))
        {
            return order;
        }

        if (order.Status != OrderState.Authorized || string.IsNullOrEmpty(order.AuthorizationId))
        {
            throw new CheckoutException(409, $"Order {orderId} cannot be fulfilled in state {order.Status}.");
        }

        var currency = RequireCurrency();
        var authorizationId = order.AuthorizationId;
        var original = order.OriginalAuthorizationTime ?? order.AuthorizationTime ?? order.OrderDate;
        var age = DateTimeOffset.UtcNow - original.ToUniversalTime();

        if (age >= ReauthorizeWindow)
        {
            throw new CheckoutException(409,
                "The PayPal authorization is older than 29 days and can no longer be renewed. Ask the shopper to pay again so a new authorization can be created, then fulfil the new payment.");
        }

        var current = await _payPal.GetAuthorizationAsync(authorizationId, ct);
        var expired = current.ExpirationTime.HasValue && current.ExpirationTime.Value <= DateTimeOffset.UtcNow;
        if (age >= HonorPeriod || expired)
        {
            try
            {
                var reauthorized = await _payPal.ReauthorizeAsync(
                    authorizationId,
                    order.AuthorizedAmount ?? order.Total(),
                    currency,
                    $"order-reauth-{order.PaymentIdempotencyKey}",
                    ct);
                order.ReplaceAuthorization(reauthorized.AuthorizationId, reauthorized.Status, reauthorized.CreateTime, reauthorized.ExpirationTime);
                await _orders.UpdateAsync(order, ct);
                authorizationId = reauthorized.AuthorizationId;
            }
            catch (CheckoutException ex) when (ex.StatusCode is >= 400 and < 500)
            {
                throw new CheckoutException(409,
                    "The PayPal authorization is no longer in the honor period and could not be renewed. Ask the shopper to pay again so a new authorization can be created. " + ex.Message,
                    ex);
            }
        }

        CaptureResult capture;
        try
        {
            capture = await _payPal.CaptureAsync(authorizationId, $"order-fulfil-{order.PaymentIdempotencyKey}", ct);
        }
        catch (CheckoutException ex) when (ex.StatusCode == 409 && !string.IsNullOrEmpty(order.CaptureId))
        {
            capture = await _payPal.GetCaptureAsync(order.CaptureId, ct);
        }

        order.RecordCapture(capture.CaptureId, capture.Status, capture.Amount, capture.PaypalFee, capture.NetAmount);
        await _orders.UpdateAsync(order, ct);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken ct)
    {
        var order = await LoadOrder(orderId, ct);

        if (order.Status == OrderState.Cancelled)
        {
            return order;
        }

        if (order.Status is OrderState.Fulfilled or OrderState.PartiallyRefunded or OrderState.Refunded)
        {
            throw new CheckoutException(409, "A fulfilled order cannot be cancelled. Issue a refund instead.");
        }

        if (!string.IsNullOrEmpty(order.AuthorizationId))
        {
            try
            {
                await _payPal.VoidAsync(order.AuthorizationId, $"order-void-{order.PaymentIdempotencyKey}", ct);
            }
            catch (CheckoutException ex) when (ex.StatusCode == 409)
            {
                // Already voided or captured — local state below is still applied for void-before-fulfil.
            }
        }

        order.RecordVoid();
        await _orders.UpdateAsync(order, ct);
        return order;
    }

    public async Task<OrderRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new CheckoutException(400, "An idempotency key is required for refunds.");
        }

        var order = await LoadOwnedOrder(buyerId, orderId, ct);
        var existing = order.Refunds.FirstOrDefault(r =>
            string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
        if (existing != null)
        {
            return existing;
        }

        if (string.IsNullOrEmpty(order.CaptureId) || order.Status is OrderState.AwaitingPayment or OrderState.Authorized or OrderState.Cancelled)
        {
            throw new CheckoutException(409, $"Order {orderId} has not been captured and cannot be refunded.");
        }

        var remaining = order.RemainingRefundable();
        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0)
        {
            throw new CheckoutException(409, "There is no remaining captured amount to refund.");
        }

        if (refundAmount - remaining > 0.0000001m)
        {
            throw new CheckoutException(409,
                $"Refund of {refundAmount.ToString("0.00", CultureInfo.InvariantCulture)} exceeds remaining refundable amount {remaining.ToString("0.00", CultureInfo.InvariantCulture)}.");
        }

        var currency = RequireCurrency();
        var isFull = AmountsEqual(refundAmount, remaining) && AmountsEqual(refundAmount, order.CapturedAmount ?? 0m) && order.Refunds.Count == 0;
        var result = await _payPal.RefundAsync(
            order.CaptureId,
            isFull ? null : refundAmount,
            currency,
            $"{order.PaymentIdempotencyKey}:{idempotencyKey}",
            ct);

        var refund = order.RecordRefund(result.RefundId, result.Status, result.Amount, idempotencyKey);
        await _orders.UpdateAsync(order, ct);
        return refund;
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        return await _orders.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), ct);
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, CardPaymentInput card, CancellationToken ct)
    {
        var vaulted = await _payPal.SaveCardAsync(buyerId, card, $"vault-{buyerId}-{Guid.NewGuid():N}", ct);
        var buyer = await GetBuyer(buyerId, ct);
        if (buyer == null)
        {
            buyer = new Buyer(buyerId);
            buyer.SetPayPalCustomerId(vaulted.PayPalCustomerId);
            var method = buyer.AddPaymentMethod(vaulted.PaymentTokenId, vaulted.LastDigits, vaulted.Brand, vaulted.Expiry, vaulted.Name);
            await _buyers.AddAsync(buyer, ct);
            return method;
        }

        buyer.SetPayPalCustomerId(vaulted.PayPalCustomerId);
        var added = buyer.AddPaymentMethod(vaulted.PaymentTokenId, vaulted.LastDigits, vaulted.Brand, vaulted.Expiry, vaulted.Name);
        await _buyers.UpdateAsync(buyer, ct);
        return added;
    }

    public async Task<IReadOnlyList<PaymentMethod>> ListCardsAsync(string buyerId, CancellationToken ct)
    {
        var buyer = await GetBuyer(buyerId, ct);
        if (buyer == null)
        {
            return Array.Empty<PaymentMethod>();
        }

        return buyer.PaymentMethods.ToList();
    }

    public async Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var buyer = await GetBuyer(buyerId, ct) ?? throw new CheckoutException(404, "Saved card was not found.");
        var method = buyer.RemovePaymentMethod(paymentMethodId) ?? throw new CheckoutException(404, "Saved card was not found.");

        if (!string.IsNullOrEmpty(method.CardId))
        {
            try
            {
                await _payPal.DeletePaymentTokenAsync(method.CardId, ct);
            }
            catch (CheckoutException ex) when (ex.StatusCode == 404)
            {
                // Already gone at PayPal — still drop the local mapping.
            }
        }

        await _buyers.UpdateAsync(buyer, ct);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (to < from)
        {
            throw new CheckoutException(400, "`to` must be on or after `from`.");
        }

        var paypal = await _payPal.SearchTransactionsAsync(from, to, ct);
        var orders = await _orders.ListAsync(new OrdersInRangeWithPaymentSpecification(from, to), ct);

        var matches = new List<ReconciliationMatch>();
        var paypalOnly = new List<PayPalTransactionRecord>();
        var matchedOrderIds = new HashSet<int>();
        var matchedTxn = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var txn in paypal)
        {
            var order = FindMatchingOrder(orders, txn);
            if (order != null)
            {
                matches.Add(new ReconciliationMatch(order.Id, txn));
                matchedOrderIds.Add(order.Id);
                matchedTxn.Add(txn.TransactionId);
            }
            else
            {
                paypalOnly.Add(txn);
            }
        }

        var eShopOnly = orders
            .Where(o => !matchedOrderIds.Contains(o.Id) && HasPaymentState(o))
            .Select(o => new ReconciliationOrderSummary(
                o.Id,
                o.Status.ToString(),
                o.PayPalOrderId,
                o.AuthorizationId,
                o.CaptureId,
                o.Total()))
            .ToList();

        return new ReconciliationReport(from, to, matches, paypalOnly, eShopOnly);
    }

    private static bool HasPaymentState(Order order) =>
        !string.IsNullOrEmpty(order.PayPalOrderId)
        || !string.IsNullOrEmpty(order.AuthorizationId)
        || !string.IsNullOrEmpty(order.CaptureId);

    private static Order? FindMatchingOrder(IReadOnlyList<Order> orders, PayPalTransactionRecord txn)
    {
        foreach (var order in orders)
        {
            if (Matches(order.PayPalOrderId, txn.TransactionId, txn.ReferenceId, txn.CustomField, txn.InvoiceId))
            {
                return order;
            }

            if (Matches(order.AuthorizationId, txn.TransactionId, txn.ReferenceId, txn.CustomField, txn.InvoiceId))
            {
                return order;
            }

            if (Matches(order.CaptureId, txn.TransactionId, txn.ReferenceId, txn.CustomField, txn.InvoiceId))
            {
                return order;
            }

            if (order.Refunds.Any(r => Matches(r.PayPalRefundId, txn.TransactionId, txn.ReferenceId, txn.CustomField, txn.InvoiceId)))
            {
                return order;
            }

            if (int.TryParse(txn.CustomField, out var customOrderId) && customOrderId == order.Id)
            {
                return order;
            }

            if (!string.IsNullOrEmpty(txn.InvoiceId) &&
                !string.IsNullOrEmpty(order.PaymentIdempotencyKey) &&
                txn.InvoiceId.Contains(order.PaymentIdempotencyKey, StringComparison.OrdinalIgnoreCase))
            {
                return order;
            }
        }

        return null;
    }

    private static bool Matches(string? localId, params string?[] candidates)
    {
        if (string.IsNullOrEmpty(localId))
        {
            return false;
        }

        return candidates.Any(c => !string.IsNullOrEmpty(c) && string.Equals(c, localId, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<Order> LoadOrder(int orderId, CancellationToken ct)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderByIdWithDetailsSpec(orderId), ct);
        return order ?? throw new CheckoutException(404, $"Order {orderId} was not found.");
    }

    private async Task<Order> LoadOwnedOrder(string buyerId, int orderId, CancellationToken ct)
    {
        var order = await LoadOrder(orderId, ct);
        if (!order.BelongsTo(buyerId))
        {
            throw new CheckoutException(403, "You cannot act on another shopper's order.");
        }

        return order;
    }

    private async Task<Buyer?> GetBuyer(string buyerId, CancellationToken ct)
    {
        return await _buyers.FirstOrDefaultAsync(new BuyerByIdentitySpec(buyerId), ct);
    }

    private string RequireCurrency()
    {
        if (string.IsNullOrWhiteSpace(_payPalOptions.Currency))
        {
            throw new CheckoutException(500, "PayPal:Currency is not configured.");
        }

        return _payPalOptions.Currency;
    }

    private static bool AmountsEqual(decimal left, decimal right) =>
        Math.Abs(left - right) < 0.005m;
}
