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
using Microsoft.eShopWeb.ApplicationCore.Payment;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class CheckoutPaymentService : ICheckoutPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPayPalPaymentGateway _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalConfiguration _paypalConfig;

    public CheckoutPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPayPalPaymentGateway payPal,
        IUriComposer uriComposer,
        IPayPalConfiguration paypalConfig)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _paypalConfig = paypalConfig;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineRequest> items,
        Address shippingAddress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
            throw new PaymentException("A signed-in shopper is required.", 401);
        if (items is null || items.Count == 0)
            throw new PaymentException("At least one catalog item is required.");

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var orderItems = new List<OrderItem>();
        foreach (var line in items)
        {
            if (line.Quantity <= 0)
                throw new PaymentException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            if (!byId.TryGetValue(line.CatalogItemId, out var catalogItem))
                throw new PaymentException($"Catalog item {line.CatalogItemId} was not found.", 404);

            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shippingAddress, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> PayAsync(
        int orderId,
        string buyerId,
        CardPaymentInput? card,
        int? paymentMethodId,
        CancellationToken cancellationToken)
    {
        var order = await GetOwnedOrder(orderId, buyerId, cancellationToken);

        if (order.PaymentStatus is OrderPaymentStatus.Authorized or OrderPaymentStatus.Fulfilled
            or OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded)
        {
            return order;
        }

        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
            throw new PaymentException("A cancelled order cannot be paid.");

        var hasCard = card is not null;
        var hasSaved = paymentMethodId.HasValue;
        if (hasCard == hasSaved)
            throw new PaymentException("Provide either card details or a saved payment method, not both.");

        var currency = RequireCurrency();
        var amount = order.Total();
        var requestId = $"{order.PaymentReference}-authorize";

        AuthorizationHold hold;
        if (hasSaved)
        {
            var saved = await _paymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId!.Value, buyerId), cancellationToken);
            if (saved is null)
                throw new PaymentException("Saved payment method was not found.", 404);

            hold = await _payPal.AuthorizeVaultedCardAsync(
                order.Id, order.PaymentReference, amount, currency, saved.PayPalPaymentTokenId, requestId, cancellationToken);
        }
        else
        {
            hold = await _payPal.AuthorizeCardAsync(
                order.Id, order.PaymentReference, amount, currency, card!, requestId, cancellationToken);
        }

        order.RecordAuthorization(
            hold.PayPalOrderId,
            hold.AuthorizationId,
            hold.Status,
            ParseExpiration(hold.ExpirationTime),
            hold.Currency);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrder(orderId, cancellationToken);

        if (order.PaymentStatus is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded)
            return order;

        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
            throw new PaymentException("A cancelled order cannot be fulfilled.");
        if (order.PaymentStatus != OrderPaymentStatus.Authorized || string.IsNullOrEmpty(order.AuthorizationId))
            throw new PaymentException("The order has no authorization to capture. The shopper must pay first.");

        var now = DateTimeOffset.UtcNow;
        if (order.AuthorizationCanNoLongerBeRenewed(now) && order.AuthorizationIsStale(now))
        {
            throw new PaymentException(
                "The PayPal authorization is older than 29 days and can no longer be renewed. Ask the shopper to pay again so a new hold can be created.",
                409);
        }

        if (order.AuthorizationIsStale(now))
        {
            await RenewAuthorization(order, cancellationToken);
        }

        try
        {
            return await CaptureOrder(order, cancellationToken);
        }
        catch (PaymentException ex) when (!ex.ChallengeRequired && ex.StatusCode is >= 400 and < 500)
        {
            if (order.AuthorizationCanNoLongerBeRenewed(DateTimeOffset.UtcNow))
            {
                throw new PaymentException(
                    "PayPal rejected the capture and the authorization can no longer be renewed. Ask the shopper to pay again.",
                    409);
            }

            await RenewAuthorization(order, cancellationToken);
            return await CaptureOrder(order, cancellationToken);
        }
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrder(orderId, cancellationToken);

        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
            return order;
        if (order.PaymentStatus is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded)
            throw new PaymentException("A fulfilled order cannot be cancelled. Issue a refund instead.");

        if (!string.IsNullOrEmpty(order.AuthorizationId))
        {
            var status = await _payPal.VoidAsync(
                order.AuthorizationId,
                $"{order.PaymentReference}-void",
                cancellationToken);
            order.RecordVoid(status);
        }
        else
        {
            order.CancelUnpaid();
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<(Order Order, OrderRefund Refund)> RefundAsync(
        int orderId,
        string buyerId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new PaymentException("An idempotency key is required for refunds.");

        var order = await GetOwnedOrder(orderId, buyerId, cancellationToken);

        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
            return (order, existing);

        if (order.PaymentStatus is not (OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded))
            throw new PaymentException("Only a fulfilled order can be refunded.");
        if (string.IsNullOrEmpty(order.CaptureId))
            throw new PaymentException("The order has no captured payment to refund.");

        var remaining = order.RemainingRefundable();
        if (remaining <= 0m)
            throw new PaymentException("This order has already been refunded in full.");

        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0m)
            throw new PaymentException("Refund amount must be greater than zero.");
        if (refundAmount > remaining)
            throw new PaymentException($"Refund amount {refundAmount} exceeds the remaining refundable amount {remaining}.");

        var currency = order.Currency ?? RequireCurrency();
        var result = await _payPal.RefundAsync(
            order.CaptureId,
            amount.HasValue ? refundAmount : null,
            currency,
            $"{order.PaymentReference}:{idempotencyKey}",
            cancellationToken);

        var already = order.FindRefundByPayPalId(result.RefundId);
        if (already is not null)
            return (order, already);

        var refund = order.AddRefund(
            result.RefundId,
            PayPalMoney.Parse(result.AmountValue),
            result.Status,
            idempotencyKey);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return (order, refund);
    }

    public async Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to < from)
            throw new PaymentException("The reconciliation 'to' instant must be on or after 'from'.");

        var currency = RequireCurrency();
        var paypalTxns = await _payPal.SearchTransactionsAsync(from, to, currency, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersForReconciliationSpec(from, to), cancellationToken);

        var unmatchedPaypal = new List<PayPalReportedTransaction>();
        foreach (var txn in paypalTxns)
        {
            if (!MatchesOrder(txn, orders))
                unmatchedPaypal.Add(txn);
        }

        var unmatchedOrders = orders
            .Where(o => o.PaymentStatus != OrderPaymentStatus.AwaitingPayment && o.PaymentStatus != OrderPaymentStatus.Cancelled)
            .Where(o => !paypalTxns.Any(t => MatchesOrder(t, new[] { o })))
            .ToList();

        return new ReconciliationReport(from, to, null, paypalTxns, orders, unmatchedPaypal, unmatchedOrders);
    }

    private async Task RenewAuthorization(Order order, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(order.AuthorizationId))
            throw new PaymentException("The order has no authorization to renew.");
        if (order.AuthorizationCanNoLongerBeRenewed(DateTimeOffset.UtcNow))
        {
            throw new PaymentException(
                "The PayPal authorization can no longer be renewed (older than 29 days). Ask the shopper to pay again so a new hold can be created.",
                409);
        }

        try
        {
            var renewed = await _payPal.ReauthorizeAsync(
                order.AuthorizationId,
                order.Total(),
                order.Currency ?? RequireCurrency(),
                $"{order.PaymentReference}-reauthorize",
                cancellationToken);
            order.ReplaceAuthorization(renewed.AuthorizationId, renewed.Status, ParseExpiration(renewed.ExpirationTime));
            await _orderRepository.UpdateAsync(order, cancellationToken);
        }
        catch (PaymentException ex)
        {
            throw new PaymentException(
                "PayPal could not renew the authorization. Ask the shopper to pay again so a new hold can be created. " + ex.Message,
                409);
        }
    }

    private async Task<Order> CaptureOrder(Order order, CancellationToken cancellationToken)
    {
        var capture = await _payPal.CaptureAsync(
            order.AuthorizationId!,
            order.PaymentReference,
            $"{order.PaymentReference}-capture",
            cancellationToken);

        order.RecordCapture(
            capture.CaptureId,
            capture.Status,
            PayPalMoney.Parse(capture.AmountValue),
            PayPalMoney.ParseNullable(capture.PaypalFeeValue),
            PayPalMoney.ParseNullable(capture.NetAmountValue));
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    private async Task<Order> GetOwnedOrder(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await GetOrder(orderId, cancellationToken);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            throw new PaymentException("Order was not found.", 404);
        return order;
    }

    private async Task<Order> GetOrder(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order is null)
            throw new PaymentException("Order was not found.", 404);
        return order;
    }

    private string RequireCurrency()
    {
        if (string.IsNullOrWhiteSpace(_paypalConfig.Currency) || _paypalConfig.Currency.Length != 3)
            throw new PaymentException("PayPal:Currency is not configured as a 3-letter ISO-4217 code.", 500);
        return _paypalConfig.Currency.ToUpperInvariant();
    }

    private static DateTimeOffset? ParseExpiration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (DateTimeOffset.TryParse(value, out var parsed))
            return parsed;
        return null;
    }

    private static bool MatchesOrder(PayPalReportedTransaction txn, IEnumerable<Order> orders)
    {
        foreach (var order in orders)
        {
            var id = order.Id.ToString();
            if (!string.IsNullOrEmpty(txn.InvoiceId) &&
                (string.Equals(txn.InvoiceId, id, StringComparison.Ordinal)
                 || string.Equals(txn.InvoiceId, order.PaymentReference, StringComparison.Ordinal)))
                return true;
            if (!string.IsNullOrEmpty(txn.CustomField) &&
                (string.Equals(txn.CustomField, id, StringComparison.Ordinal)
                 || string.Equals(txn.CustomField, order.PaymentReference, StringComparison.Ordinal)))
                return true;
            if (!string.IsNullOrEmpty(txn.TransactionId) &&
                (string.Equals(txn.TransactionId, order.CaptureId, StringComparison.OrdinalIgnoreCase)
                 || string.Equals(txn.TransactionId, order.AuthorizationId, StringComparison.OrdinalIgnoreCase)
                 || string.Equals(txn.TransactionId, order.PayPalOrderId, StringComparison.OrdinalIgnoreCase)))
                return true;
            if (!string.IsNullOrEmpty(txn.PaypalReferenceId) &&
                (string.Equals(txn.PaypalReferenceId, order.CaptureId, StringComparison.OrdinalIgnoreCase)
                 || string.Equals(txn.PaypalReferenceId, order.AuthorizationId, StringComparison.OrdinalIgnoreCase)
                 || string.Equals(txn.PaypalReferenceId, order.PayPalOrderId, StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }
}
