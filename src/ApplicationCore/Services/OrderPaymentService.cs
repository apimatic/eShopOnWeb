using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
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
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalSettings _payPalSettings;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Buyer> buyerRepository,
        IPaymentGateway paymentGateway,
        IUriComposer uriComposer,
        PayPalSettings payPalSettings,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _buyerRepository = buyerRepository;
        _paymentGateway = paymentGateway;
        _uriComposer = uriComposer;
        _payPalSettings = payPalSettings;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderItem> items,
        Address? shipToAddress,
        CancellationToken cancellationToken = default)
    {
        if (items is null || items.Count == 0)
        {
            throw new PaymentException("An order requires at least one catalog item.", 400);
        }

        foreach (var item in items)
        {
            if (item.Quantity <= 0)
            {
                throw new PaymentException("Item quantities must be greater than zero.", 400);
            }
        }

        var catalogIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogIds), cancellationToken);
        if (catalogItems.Count != catalogIds.Length)
        {
            var found = catalogItems.Select(c => c.Id).ToHashSet();
            var missing = catalogIds.Where(id => !found.Contains(id));
            throw new PaymentException($"Unknown catalog item(s): {string.Join(", ", missing)}.", 400);
        }

        var orderItems = items.Select(requested =>
        {
            var catalogItem = catalogItems.First(c => c.Id == requested.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, requested.Quantity);
        }).ToList();

        var address = shipToAddress ?? new Address("N/A", "N/A", "N/A", "US", "00000");
        var order = new Order(buyerId, address, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> AuthorizePaymentAsync(
        string buyerId,
        int orderId,
        CardPaymentSource? card,
        int? paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderForBuyer(orderId, buyerId, cancellationToken);

        if (order.Status == OrderStatus.Authorized && !string.IsNullOrEmpty(order.PayPalAuthorizationId))
        {
            return order;
        }

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            throw new PaymentException("This order has already been paid and captured.", 409);
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentException("A cancelled order cannot be paid.", 409);
        }

        if (card is null && paymentMethodId is null)
        {
            throw new PaymentException("Provide card details or a saved paymentMethodId.", 400);
        }

        if (card is not null && paymentMethodId is not null)
        {
            throw new PaymentException("Provide either card details or a saved paymentMethodId, not both.", 400);
        }

        var amount = OrderMoney(order);
        var invoiceId = $"ESHOP-{order.Id}-{order.PaymentIdempotencyKey[..8]}";
        var customId = order.Id.ToString(CultureInfo.InvariantCulture);
        var idempotencyKey = $"eshop-pay-{order.PaymentIdempotencyKey}";

        AuthorizationResult authorization;
        if (paymentMethodId is not null)
        {
            var vaultId = await ResolveVaultId(buyerId, paymentMethodId.Value, cancellationToken);
            authorization = await _paymentGateway.AuthorizeVaultedCardAsync(
                invoiceId, customId, amount, vaultId, idempotencyKey, cancellationToken);
        }
        else
        {
            authorization = await _paymentGateway.AuthorizeCardAsync(
                invoiceId, customId, amount, card!, idempotencyKey, cancellationToken);
        }

        EnsureAuthorizedAmountMatches(order, authorization.Amount);

        order.RecordAuthorization(
            authorization.PayPalOrderId,
            authorization.PayPalOrderStatus,
            authorization.AuthorizationId,
            authorization.AuthorizationStatus,
            authorization.ExpirationTime,
            amount.Currency);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Authorized order {OrderId} PayPal authorization {AuthorizationId}", order.Id, order.PayPalAuthorizationId!);
        return order;
    }

    public async Task<Order> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrder(orderId, cancellationToken);

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded
            && !string.IsNullOrEmpty(order.PayPalCaptureId))
        {
            return order;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentException("A cancelled order cannot be fulfilled.", 409);
        }

        if (order.Status != OrderStatus.Authorized || string.IsNullOrEmpty(order.PayPalAuthorizationId))
        {
            throw new PaymentException("The order must be authorized before it can be fulfilled.", 409);
        }

        var amount = OrderMoney(order);
        var authorizationId = await EnsureFreshAuthorization(order, amount, cancellationToken);
        var capture = await CaptureWithRenewal(order, authorizationId, amount, cancellationToken);

        order.RecordCapture(
            capture.CaptureId,
            capture.Status,
            capture.CapturedAmount.Value,
            capture.PaypalFee?.Value,
            capture.NetAmount?.Value,
            capture.CapturedAmount.Currency);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Fulfilled order {OrderId} PayPal capture {CaptureId}", order.Id, order.PayPalCaptureId!);
        return order;
    }

    public async Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrder(orderId, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            throw new PaymentException("A fulfilled order cannot be cancelled; issue a refund instead.", 409);
        }

        if (!string.IsNullOrEmpty(order.PayPalAuthorizationId))
        {
            await _paymentGateway.VoidAuthorizationAsync(
                order.PayPalAuthorizationId,
                $"eshop-cancel-{order.PaymentIdempotencyKey}",
                cancellationToken);
        }

        order.RecordCancellation();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Cancelled order {OrderId}", order.Id);
        return order;
    }

    public async Task<RefundOrderResult> RefundOrderAsync(
        string buyerId,
        int orderId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentException("A refund requires an idempotencyKey.", 400);
        }

        var order = await LoadOrderForBuyer(orderId, buyerId, cancellationToken);

        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return new RefundOrderResult(order, existing);
        }

        if (order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded))
        {
            throw new PaymentException("Refunds are only allowed after the order has been fulfilled.", 409);
        }

        if (string.IsNullOrEmpty(order.PayPalCaptureId) || order.CapturedAmount is null)
        {
            throw new PaymentException("This order has no captured payment to refund.", 409);
        }

        var remaining = order.RefundableRemaining();
        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0)
        {
            throw new PaymentException("Refund amount must be greater than zero.", 400);
        }

        if (refundAmount > remaining)
        {
            throw new PaymentException(
                $"Refund of {refundAmount.ToString("0.00", CultureInfo.InvariantCulture)} exceeds the remaining captured amount of {remaining.ToString("0.00", CultureInfo.InvariantCulture)}.",
                409);
        }

        var money = new MoneyAmount(decimal.Round(refundAmount, 2, MidpointRounding.AwayFromZero), Currency());
        var gatewayRefund = await _paymentGateway.RefundCaptureAsync(
            order.PayPalCaptureId,
            money,
            idempotencyKey,
            cancellationToken);

        var refund = order.RecordRefund(
            gatewayRefund.PayPalRefundId,
            gatewayRefund.Status,
            gatewayRefund.Amount.Value,
            gatewayRefund.Amount.Currency,
            idempotencyKey);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Refunded {Amount} on order {OrderId} PayPal refund {RefundId}", money.Value, order.Id, refund.PayPalRefundId);
        return new RefundOrderResult(order, refund);
    }

    public async Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new PaymentException("`to` must be on or after `from`.", 400);
        }

        var paypal = await _paymentGateway.ListTransactionsAsync(from, to, cancellationToken);
        var eshopOrders = await _orderRepository.ListAsync(new OrdersWithPayPalIdsSpecification(), cancellationToken);

        var matchedPaypalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedOrderIds = new HashSet<int>();
        var rows = new List<ReconciliationRow>();

        foreach (var txn in paypal.Transactions)
        {
            var order = MatchOrder(eshopOrders, txn);
            if (order is null)
            {
                rows.Add(new ReconciliationRow(
                    "paypal_only",
                    null,
                    txn.TransactionId,
                    txn.EventCode,
                    txn.Status,
                    null,
                    txn.Amount?.Value,
                    txn.Amount?.Currency,
                    "PayPal has this transaction but eShop has no matching order."));
                continue;
            }

            matchedPaypalIds.Add(txn.TransactionId);
            matchedOrderIds.Add(order.Id);
            rows.Add(new ReconciliationRow(
                "matched",
                order.Id,
                txn.TransactionId,
                txn.EventCode,
                txn.Status,
                order.CapturedAmount ?? order.Total(),
                txn.Amount?.Value,
                txn.Amount?.Currency ?? order.PaymentCurrency,
                null));
        }

        foreach (var order in eshopOrders.Where(o => o.OrderDate >= from && o.OrderDate <= to))
        {
            if (matchedOrderIds.Contains(order.Id))
            {
                continue;
            }

            var ids = PayPalIds(order);
            if (ids.Any(id => matchedPaypalIds.Contains(id)))
            {
                continue;
            }

            rows.Add(new ReconciliationRow(
                "eshop_only",
                order.Id,
                order.PayPalCaptureId ?? order.PayPalAuthorizationId ?? order.PayPalOrderId,
                null,
                order.Status.ToString(),
                order.CapturedAmount ?? order.Total(),
                null,
                order.PaymentCurrency ?? Currency(),
                "eShop has this payment but PayPal's report for the range does not."));
        }

        return new ReconciliationReport(from, to, paypal.LastRefreshed, rows);
    }

    private async Task<string> EnsureFreshAuthorization(Order order, MoneyAmount amount, CancellationToken cancellationToken)
    {
        var authorizationId = order.PayPalAuthorizationId!;
        AuthorizationDetails details;
        try
        {
            details = await _paymentGateway.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (PaymentException ex) when (ex.StatusCode == 404)
        {
            throw CannotRenew(order, "PayPal no longer has this authorization. Ask the shopper to pay again.");
        }

        order.RefreshAuthorization(details.AuthorizationId, details.Status, details.ExpirationTime);

        var stale = IsStale(details);
        if (!stale)
        {
            return details.AuthorizationId;
        }

        return await ReauthorizeOrThrow(order, details.AuthorizationId, amount, cancellationToken);
    }

    private async Task<CaptureResult> CaptureWithRenewal(
        Order order,
        string authorizationId,
        MoneyAmount amount,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _paymentGateway.CaptureAuthorizationAsync(
                authorizationId,
                amount,
                $"eshop-fulfil-{order.PaymentIdempotencyKey}",
                cancellationToken);
        }
        catch (PaymentException ex) when (IsExpiredAuthorization(ex))
        {
            var renewedId = await ReauthorizeOrThrow(order, authorizationId, amount, cancellationToken);
            return await _paymentGateway.CaptureAuthorizationAsync(
                renewedId,
                amount,
                $"eshop-fulfil-{order.PaymentIdempotencyKey}-renewed",
                cancellationToken);
        }
    }

    private async Task<string> ReauthorizeOrThrow(Order order, string authorizationId, MoneyAmount amount, CancellationToken cancellationToken)
    {
        try
        {
            var renewed = await _paymentGateway.ReauthorizeAsync(
                authorizationId,
                amount,
                $"eshop-reauth-{order.PaymentIdempotencyKey}-{authorizationId}",
                cancellationToken);

            order.RefreshAuthorization(renewed.AuthorizationId, renewed.AuthorizationStatus, renewed.ExpirationTime);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation(
                "Renewed authorization for order {OrderId}: {OldId} -> {NewId}",
                order.Id,
                authorizationId,
                renewed.AuthorizationId);
            return renewed.AuthorizationId;
        }
        catch (PaymentException ex)
        {
            throw CannotRenew(order,
                $"The PayPal authorization can no longer be renewed ({ex.Issue ?? "unspecified"}). Ask the shopper to pay again. {ex.Message}");
        }
    }

    private static bool IsStale(AuthorizationDetails details)
    {
        if (string.Equals(details.Status, "VOIDED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(details.Status, "EXPIRED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(details.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (details.ExpirationTime is { } expiration && expiration <= DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return true;
        }

        return false;
    }

    private static bool IsExpiredAuthorization(PaymentException ex)
    {
        var issue = ex.Issue ?? string.Empty;
        return issue.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase)
               || issue.Contains("VOIDED", StringComparison.OrdinalIgnoreCase)
               || issue.Contains("AUTHORIZATION_VOIDED", StringComparison.OrdinalIgnoreCase)
               || issue.Contains("AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase);
    }

    private static PaymentException CannotRenew(Order order, string message)
    {
        return new PaymentException(
            $"Order {order.Id} cannot be fulfilled: {message}",
            409,
            "AUTHORIZATION_NOT_RENEWABLE");
    }

    private async Task<Order> LoadOrder(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new PaymentException($"Order {orderId} was not found.", 404);
        }

        return order;
    }

    private async Task<Order> LoadOrderForBuyer(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await LoadOrder(orderId, cancellationToken);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentException($"Order {orderId} was not found.", 404);
        }

        return order;
    }

    private async Task<string> ResolveVaultId(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerByIdentitySpecification(buyerId), cancellationToken);
        var method = buyer?.GetPaymentMethod(paymentMethodId);
        if (method is null || string.IsNullOrEmpty(method.CardId))
        {
            throw new PaymentException($"Saved payment method {paymentMethodId} was not found.", 404);
        }

        return method.CardId;
    }

    private MoneyAmount OrderMoney(Order order)
    {
        return new MoneyAmount(decimal.Round(order.Total(), 2, MidpointRounding.AwayFromZero), Currency());
    }

    private string Currency()
    {
        if (string.IsNullOrWhiteSpace(_payPalSettings.Currency))
        {
            throw new PaymentException("PayPal:Currency is not configured.", 500);
        }

        return _payPalSettings.Currency;
    }

    private static void EnsureAuthorizedAmountMatches(Order order, MoneyAmount authorized)
    {
        var expected = decimal.Round(order.Total(), 2, MidpointRounding.AwayFromZero);
        var actual = decimal.Round(authorized.Value, 2, MidpointRounding.AwayFromZero);
        if (expected != actual)
        {
            throw new PaymentException(
                $"PayPal authorized {actual} but the order total is {expected}. The hold must equal the order total to the cent.",
                502);
        }
    }

    private static Order? MatchOrder(IReadOnlyList<Order> orders, GatewayTransaction txn)
    {
        foreach (var order in orders)
        {
            if (!string.IsNullOrEmpty(txn.CustomField)
                && string.Equals(txn.CustomField, order.Id.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                return order;
            }

            if (!string.IsNullOrEmpty(txn.InvoiceId)
                && !string.IsNullOrEmpty(order.PaymentIdempotencyKey)
                && txn.InvoiceId.Contains(order.PaymentIdempotencyKey[..8], StringComparison.OrdinalIgnoreCase))
            {
                return order;
            }

            var ids = PayPalIds(order);
            if (ids.Contains(txn.TransactionId) || (!string.IsNullOrEmpty(txn.ReferenceId) && ids.Contains(txn.ReferenceId)))
            {
                return order;
            }
        }

        return null;
    }

    private static HashSet<string> PayPalIds(Order order)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(order.PayPalOrderId)) ids.Add(order.PayPalOrderId);
        if (!string.IsNullOrEmpty(order.PayPalAuthorizationId)) ids.Add(order.PayPalAuthorizationId);
        if (!string.IsNullOrEmpty(order.PayPalCaptureId)) ids.Add(order.PayPalCaptureId);
        foreach (var refund in order.Refunds)
        {
            if (!string.IsNullOrEmpty(refund.PayPalRefundId)) ids.Add(refund.PayPalRefundId);
        }

        return ids;
    }
}
