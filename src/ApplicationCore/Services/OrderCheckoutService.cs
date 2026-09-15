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

public class OrderCheckoutService : IOrderCheckoutService
{
    public const string DefaultShipStreet = "123 Main St.";
    public const string DefaultShipCity = "Anytown";
    public const string DefaultShipState = "CA";
    public const string DefaultShipCountry = "US";
    public const string DefaultShipZip = "12345";

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalGateway _payPal;

    public OrderCheckoutService(
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
        PlaceOrderAddress? shipTo,
        CancellationToken cancellationToken = default)
    {
        if (items == null || items.Count == 0)
        {
            throw new PaymentException(400, "At least one catalog item is required.", "EMPTY_ORDER");
        }

        var catalogItemIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var orderItems = new List<OrderItem>();
        foreach (var line in items)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentException(400, $"Quantity for catalog item {line.CatalogItemId} must be greater than zero.", "INVALID_QUANTITY");
            }

            if (!catalogById.TryGetValue(line.CatalogItemId, out var catalogItem))
            {
                throw new PaymentException(400, $"Catalog item {line.CatalogItemId} was not found.", "CATALOG_ITEM_NOT_FOUND");
            }

            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var address = shipTo is null
            ? new Address(DefaultShipStreet, DefaultShipCity, DefaultShipState, DefaultShipCountry, DefaultShipZip)
            : new Address(shipTo.Street, shipTo.City, shipTo.State, shipTo.Country, shipTo.ZipCode);

        var order = new Order(buyerId, address, orderItems);
        order.AssignCurrency(_payPal.Currency);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> PayAsync(
        string buyerId,
        int orderId,
        CardPaymentDetails? card,
        int? paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var order = await GetOwnedOrder(buyerId, orderId, cancellationToken);

        if (order.Status == OrderStatus.Authorized || order.Status == OrderStatus.Fulfilled
            || order.Status == OrderStatus.PartiallyRefunded || order.Status == OrderStatus.Refunded)
        {
            return order;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentException(409, "A cancelled order cannot be paid.", "ORDER_CANCELLED");
        }

        var hasCard = card != null && !string.IsNullOrWhiteSpace(card.Number);
        if (hasCard == paymentMethodId.HasValue)
        {
            throw new PaymentException(400, "Provide either card details or a saved paymentMethodId, not both.", "INVALID_PAYMENT_SOURCE");
        }

        var amount = order.Total();
        if (amount <= 0)
        {
            throw new PaymentException(400, "Order total must be greater than zero.", "INVALID_AMOUNT");
        }

        var idempotencyKey = $"eshop-pay-{order.PaymentAttemptKey}";
        AuthorizationResult authorization;
        if (hasCard)
        {
            authorization = await _payPal.AuthorizeCardAsync(order.Id, amount, card!, idempotencyKey, cancellationToken);
        }
        else
        {
            var saved = await _paymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId!.Value, buyerId), cancellationToken);
            if (saved == null)
            {
                throw new PaymentException(404, "Saved payment method was not found.", "PAYMENT_METHOD_NOT_FOUND");
            }

            authorization = await _payPal.AuthorizeVaultedCardAsync(
                order.Id, amount, saved.PayPalVaultId, idempotencyKey, cancellationToken);
        }

        AssertAuthorizedAmountMatches(amount, authorization.AuthorizedAmount);

        order.RecordAuthorization(authorization, _payPal.Currency);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrder(orderId, cancellationToken);

        if (order.Status == OrderStatus.Fulfilled || order.Status == OrderStatus.PartiallyRefunded || order.Status == OrderStatus.Refunded)
        {
            return order;
        }

        if (order.Status != OrderStatus.Authorized)
        {
            throw new PaymentException(409, "Only an authorized order can be fulfilled.", "ORDER_NOT_AUTHORIZED");
        }

        if (string.IsNullOrEmpty(order.PayPalAuthorizationId))
        {
            throw new PaymentException(409, "This order has no PayPal authorization to capture.", "MISSING_AUTHORIZATION");
        }

        var authorizationId = await EnsureFreshAuthorizationAsync(order, cancellationToken);
        var capture = await CaptureWithRenewalAsync(order, authorizationId, cancellationToken);

        order.RecordCapture(capture);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrder(orderId, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }

        var voided = false;
        if (order.Status == OrderStatus.Authorized && !string.IsNullOrEmpty(order.PayPalAuthorizationId))
        {
            await _payPal.VoidAuthorizationAsync(
                order.PayPalAuthorizationId,
                $"eshop-void-{order.PaymentAttemptKey}",
                cancellationToken);
            voided = true;
        }

        order.RecordCancellation(voided);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<(Order Order, PaymentRefund Refund)> RefundAsync(
        string buyerId,
        int orderId,
        string idempotencyKey,
        decimal? amount,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentException(400, "An idempotency key is required for refunds.", "MISSING_IDEMPOTENCY_KEY");
        }

        var order = await GetOwnedOrder(buyerId, orderId, cancellationToken);
        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return (order, existing);
        }

        if (string.IsNullOrEmpty(order.PayPalCaptureId))
        {
            throw new PaymentException(409, "This order has no captured payment to refund.", "MISSING_CAPTURE");
        }

        var remaining = order.RefundableRemaining();
        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0)
        {
            throw new PaymentException(409, "There is no remaining captured amount to refund.", "NOTHING_TO_REFUND");
        }

        if (refundAmount - remaining > 0.001m)
        {
            throw new PaymentException(409,
                $"Requested refund {refundAmount.ToString("0.00", CultureInfo.InvariantCulture)} exceeds the remaining captured amount of {remaining.ToString("0.00", CultureInfo.InvariantCulture)}.",
                "REFUND_EXCEEDS_CAPTURE");
        }

        var result = await _payPal.RefundCaptureAsync(
            order.PayPalCaptureId,
            amount,
            $"eshop-rf-{order.PaymentAttemptKey}-{idempotencyKey}",
            cancellationToken);

        var refund = order.RecordRefund(result, idempotencyKey);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return (order, refund);
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        return orders;
    }

    public async Task<SavedPaymentMethod> SavePaymentMethodAsync(
        string buyerId,
        CardPaymentDetails card,
        CancellationToken cancellationToken = default)
    {
        if (card == null || string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry))
        {
            throw new PaymentException(400, "Card number and expiry are required.", "INVALID_CARD");
        }

        var existing = await _paymentMethodRepository.FirstOrDefaultAsync(
            new LatestSavedPaymentMethodByBuyerSpec(buyerId), cancellationToken);

        var vaulted = await _payPal.VaultCardAsync(
            card,
            existing?.PayPalCustomerId,
            $"eshop-vault-{buyerId}-{Guid.NewGuid():N}",
            cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.VaultId,
            vaulted.CustomerId ?? existing?.PayPalCustomerId,
            vaulted.LastDigits,
            vaulted.Brand,
            vaulted.Expiry,
            vaulted.CardholderName ?? card.Name);

        await _paymentMethodRepository.AddAsync(saved, cancellationToken);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListPaymentMethodsAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        return await _paymentMethodRepository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var saved = await _paymentMethodRepository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId, buyerId), cancellationToken);
        if (saved == null)
        {
            throw new PaymentException(404, "Saved payment method was not found.", "PAYMENT_METHOD_NOT_FOUND");
        }

        await _payPal.DeleteVaultedCardAsync(saved.PayPalVaultId, cancellationToken);
        await _paymentMethodRepository.DeleteAsync(saved, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new PaymentException(400, "`to` must be on or after `from`.", "INVALID_DATE_RANGE");
        }

        var paypalTransactions = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        var eshopOrders = await _orderRepository.ListAsync(new PaidOrdersSpecification(), cancellationToken);

        var matched = new List<ReconciliationMatch>();
        var matchedOrderIds = new HashSet<int>();
        var matchedTxnIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var txn in paypalTransactions)
        {
            var order = eshopOrders.FirstOrDefault(o => Matches(o, txn));
            if (order != null)
            {
                matched.Add(new ReconciliationMatch(order, txn));
                matchedOrderIds.Add(order.Id);
                if (!string.IsNullOrEmpty(txn.TransactionId))
                {
                    matchedTxnIds.Add(txn.TransactionId);
                }
            }
        }

        var paypalOnly = paypalTransactions
            .Where(t => !matchedTxnIds.Contains(t.TransactionId))
            .ToList();

        var eshopOnly = eshopOrders
            .Where(o => !matchedOrderIds.Contains(o.Id) && InRange(o, from, to))
            .ToList();

        return new ReconciliationReport(from, to, matched, paypalOnly, eshopOnly);
    }

    private async Task<string> EnsureFreshAuthorizationAsync(Order order, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (order.AuthorizationIsPastRenewalWindow(now))
        {
            throw new PaymentException(409, order.UnrenewableAuthorizationMessage(), "AUTHORIZATION_UNRENEWABLE");
        }

        if (!order.HonorPeriodHasElapsed(now))
        {
            return order.PayPalAuthorizationId!;
        }

        return await ReauthorizeOrderAsync(order, cancellationToken);
    }

    private async Task<string> ReauthorizeOrderAsync(Order order, CancellationToken cancellationToken)
    {
        try
        {
            var renewed = await _payPal.ReauthorizeAsync(
                order.PayPalAuthorizationId!,
                order.Total(),
                $"eshop-reauth-{order.Id}-{Guid.NewGuid():N}",
                cancellationToken);
            order.RecordReauthorization(renewed);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order.PayPalAuthorizationId!;
        }
        catch (PaymentException ex) when (IsUnrenewable(ex))
        {
            throw new PaymentException(409, order.UnrenewableAuthorizationMessage(), "AUTHORIZATION_UNRENEWABLE");
        }
    }

    private async Task<CaptureResult> CaptureWithRenewalAsync(Order order, string authorizationId, CancellationToken cancellationToken)
    {
        try
        {
            return await _payPal.CaptureAuthorizationAsync(
                authorizationId,
                order.Total(),
                InvoiceId(order.Id),
                $"eshop-capture-{order.PaymentAttemptKey}",
                cancellationToken);
        }
        catch (PaymentException ex) when (IsExpiredAuthorization(ex))
        {
            if (order.AuthorizationIsPastRenewalWindow(DateTimeOffset.UtcNow))
            {
                throw new PaymentException(409, order.UnrenewableAuthorizationMessage(), "AUTHORIZATION_UNRENEWABLE");
            }

            var renewedId = await ReauthorizeOrderAsync(order, cancellationToken);
            try
            {
                return await _payPal.CaptureAuthorizationAsync(
                    renewedId,
                    order.Total(),
                    InvoiceId(order.Id),
                    $"eshop-capture-{order.PaymentAttemptKey}",
                    cancellationToken);
            }
            catch (PaymentException inner) when (IsUnrenewable(inner) || IsExpiredAuthorization(inner))
            {
                throw new PaymentException(409, order.UnrenewableAuthorizationMessage(), "AUTHORIZATION_UNRENEWABLE");
            }
        }
    }

    private async Task<Order> GetOwnedOrder(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrder(orderId, cancellationToken);
        if (!order.BelongsTo(buyerId))
        {
            throw new PaymentException(404, $"Order {orderId} was not found.", "ORDER_NOT_FOUND");
        }

        return order;
    }

    private async Task<Order> GetOrder(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            throw new PaymentException(404, $"Order {orderId} was not found.", "ORDER_NOT_FOUND");
        }

        return order;
    }

    private static void AssertAuthorizedAmountMatches(decimal orderTotal, decimal authorized)
    {
        if (Math.Abs(orderTotal - authorized) > 0.001m)
        {
            throw new PaymentException(502,
                "PayPal authorized an amount that does not match the order total.",
                "AMOUNT_MISMATCH");
        }
    }

    private static bool IsExpiredAuthorization(PaymentException ex) =>
        string.Equals(ex.ErrorCode, "AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ex.ErrorCode, "AUTHORIZATION_DENIED", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnrenewable(PaymentException ex) =>
        string.Equals(ex.ErrorCode, "AUTHORIZATION_UNRENEWABLE", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ex.ErrorCode, "MAX_NUMBER_OF_REAUTHORIZATION_EXCEEDED", StringComparison.OrdinalIgnoreCase)
        || (ex.Message?.IndexOf("29-day", StringComparison.OrdinalIgnoreCase) >= 0);

    public static string InvoiceId(int orderId) => $"ESHOP-{orderId}";

    private static bool InRange(Order order, DateTimeOffset from, DateTimeOffset to)
    {
        if (order.OrderDate >= from && order.OrderDate <= to)
        {
            return true;
        }

        if (order.PayPalAuthorizationCreated is DateTimeOffset authorized
            && authorized >= from && authorized <= to)
        {
            return true;
        }

        return false;
    }

    private static bool Matches(Order order, ReportedTransaction txn)
    {
        var invoice = InvoiceId(order.Id);
        if (!string.IsNullOrEmpty(txn.InvoiceId)
            && (txn.InvoiceId.StartsWith(invoice, StringComparison.OrdinalIgnoreCase)
                || string.Equals(txn.InvoiceId, order.Id.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(txn.CustomField)
            && string.Equals(txn.CustomField, invoice, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return SameId(txn.TransactionId, order)
            || SameId(txn.ReferenceId, order);
    }

    private static bool SameId(string? paypalId, Order order) =>
        !string.IsNullOrEmpty(paypalId) &&
        (string.Equals(paypalId, order.PayPalOrderId, StringComparison.OrdinalIgnoreCase)
         || string.Equals(paypalId, order.PayPalAuthorizationId, StringComparison.OrdinalIgnoreCase)
         || string.Equals(paypalId, order.PayPalCaptureId, StringComparison.OrdinalIgnoreCase)
         || order.Refunds.Any(r => string.Equals(paypalId, r.PayPalRefundId, StringComparison.OrdinalIgnoreCase)));
}
