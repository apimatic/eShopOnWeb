using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class CheckoutService : ICheckoutService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPaymentGateway _paymentGateway;
    private readonly PayPalOptions _payPalOptions;

    public CheckoutService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IUriComposer uriComposer,
        IPaymentGateway paymentGateway,
        PayPalOptions payPalOptions)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _uriComposer = uriComposer;
        _paymentGateway = paymentGateway;
        _payPalOptions = payPalOptions;
    }

    public async Task<Order> PlaceOrderAsync(PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(command.BuyerId, nameof(command.BuyerId));
        if (command.Items is null || command.Items.Count == 0)
        {
            throw new InvalidOrderStateException("An order must contain at least one catalog item.");
        }

        var ids = command.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        if (catalogItems.Count != ids.Length)
        {
            throw new InvalidOrderStateException("One or more catalog items were not found.");
        }

        var items = command.Items.Select(line =>
        {
            if (line.Quantity < 1)
            {
                throw new InvalidOrderStateException("Quantity must be at least 1.");
            }

            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(command.BuyerId, command.ShipTo, items);
        return await _orderRepository.AddAsync(order, cancellationToken);
    }

    public async Task<Order> PayAsync(PayOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(command.OrderId, cancellationToken);
        order.EnsureOwnedBy(command.BuyerId);

        if (order.PaymentStatus is OrderPaymentStatus.Authorized
            or OrderPaymentStatus.Fulfilled
            or OrderPaymentStatus.PartiallyRefunded
            or OrderPaymentStatus.Refunded)
        {
            return order;
        }

        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            throw new InvalidOrderStateException("A cancelled order cannot be paid.");
        }

        if (command.Card is null && command.PaymentMethodId is null)
        {
            throw new InvalidOrderStateException("Provide card details or a saved paymentMethodId.");
        }

        if (command.Card is not null && command.PaymentMethodId is not null)
        {
            throw new InvalidOrderStateException("Provide either card details or a saved paymentMethodId, not both.");
        }

        string? vaultId = null;
        if (command.PaymentMethodId is int paymentMethodId)
        {
            var method = await GetOwnedPaymentMethodAsync(command.BuyerId, paymentMethodId, cancellationToken);
            vaultId = method.PayPalVaultId;
        }

        var result = await _paymentGateway.AuthorizeAsync(
            order.Id,
            order.PayPalInvoiceId ?? $"eshop-{order.Id}",
            order.Total(),
            _payPalOptions.Currency,
            command.Card,
            vaultId,
            cancellationToken);

        order.RecordAuthorization(
            result.PayPalOrderId,
            result.AuthorizationId,
            result.Status,
            result.Expiration,
            result.Currency);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.PaymentStatus is OrderPaymentStatus.Fulfilled
            or OrderPaymentStatus.PartiallyRefunded
            or OrderPaymentStatus.Refunded)
        {
            return order;
        }

        if (order.PaymentStatus != OrderPaymentStatus.Authorized ||
            string.IsNullOrEmpty(order.PayPalAuthorizationId))
        {
            throw new InvalidOrderStateException("The order has no authorization to capture. The shopper must pay first.");
        }

        var authorizationId = order.PayPalAuthorizationId;
        var currency = order.Currency ?? _payPalOptions.Currency;
        var amount = order.Total();

        var authorization = await _paymentGateway.GetAuthorizationAsync(authorizationId, cancellationToken);
        if (string.Equals(authorization.Status, "VOIDED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(authorization.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOrderStateException(
                $"The PayPal authorization is {authorization.Status} and cannot be captured. The shopper must pay again.");
        }

        if (IsStale(authorization))
        {
            try
            {
                var renewed = await _paymentGateway.ReauthorizeAsync(
                    authorizationId,
                    amount,
                    currency,
                    $"eshop-reauth-{order.PayPalInvoiceId}",
                    cancellationToken);
                order.RefreshAuthorization(renewed.AuthorizationId, renewed.Status, renewed.Expiration);
                authorizationId = renewed.AuthorizationId;
            }
            catch (PaymentException ex)
            {
                throw new InvalidOrderStateException(
                    "The PayPal authorization has expired and could not be renewed. Ask the shopper to pay again before fulfilment. " +
                    ex.Message);
            }
        }

        CaptureResult capture;
        try
        {
            capture = await _paymentGateway.CaptureAsync(
                authorizationId,
                amount,
                currency,
                $"eshop-capture-{order.PayPalInvoiceId}",
                cancellationToken);
        }
        catch (PaymentException ex) when (LooksExpired(ex))
        {
            AuthorizationResult renewed;
            try
            {
                renewed = await _paymentGateway.ReauthorizeAsync(
                    authorizationId,
                    amount,
                    currency,
                    $"eshop-reauth-{order.PayPalInvoiceId}",
                    cancellationToken);
            }
            catch (PaymentException renewEx)
            {
                throw new InvalidOrderStateException(
                    "The PayPal authorization could not be renewed after it went stale. Ask the shopper to pay again before fulfilment. " +
                    renewEx.Message);
            }

            order.RefreshAuthorization(renewed.AuthorizationId, renewed.Status, renewed.Expiration);
            capture = await _paymentGateway.CaptureAsync(
                renewed.AuthorizationId,
                amount,
                currency,
                $"eshop-capture-{order.PayPalInvoiceId}",
                cancellationToken);
        }

        if (capture.PaypalFee is null || capture.NetAmount is null)
        {
            capture = await _paymentGateway.GetCaptureAsync(capture.CaptureId, cancellationToken);
        }

        order.RecordCapture(
            capture.CaptureId,
            capture.Status,
            capture.Amount,
            capture.PaypalFee,
            capture.NetAmount,
            capture.Currency);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            return order;
        }

        string? voidStatus = null;
        if (order.PaymentStatus == OrderPaymentStatus.Authorized &&
            !string.IsNullOrEmpty(order.PayPalAuthorizationId))
        {
            voidStatus = await _paymentGateway.VoidAsync(
                order.PayPalAuthorizationId,
                $"eshop-void-{order.PayPalInvoiceId}",
                cancellationToken);
        }

        order.Cancel(voidStatus);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<RefundOutcome> RefundAsync(RefundOrderCommand command, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(command.IdempotencyKey, nameof(command.IdempotencyKey));

        var order = await GetOrderAsync(command.OrderId, cancellationToken);
        order.EnsureOwnedBy(command.BuyerId);

        var existing = order.FindRefundByIdempotencyKey(command.IdempotencyKey);
        if (existing is not null)
        {
            return new RefundOutcome(order, existing);
        }

        if (string.IsNullOrEmpty(order.PayPalCaptureId) || order.CapturedAmount is null)
        {
            throw new InvalidOrderStateException("The order has no captured payment to refund.");
        }

        var remaining = order.RemainingRefundable();
        var amount = command.Amount ?? remaining;
        if (amount <= 0)
        {
            throw new InvalidOrderStateException("Refund amount must be greater than zero.");
        }

        if (amount > remaining)
        {
            throw new InvalidOrderStateException(
                $"Refund of {PayPalMoney.ToValue(amount)} exceeds the remaining captured amount of {PayPalMoney.ToValue(remaining)}.");
        }

        var currency = order.Currency ?? _payPalOptions.Currency;
        decimal? gatewayAmount = amount == remaining && remaining == order.CapturedAmount
            ? null
            : amount;

        var paypalRequestId = $"{order.PayPalCaptureId}:{command.IdempotencyKey}";
        var result = await _paymentGateway.RefundAsync(
            order.PayPalCaptureId,
            gatewayAmount,
            currency,
            paypalRequestId,
            cancellationToken);

        var refund = order.RecordRefund(
            result.PayPalRefundId,
            result.Status,
            result.Amount == 0 ? amount : result.Amount,
            result.Currency,
            command.IdempotencyKey);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return new RefundOutcome(order, refund);
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (to < from)
        {
            throw new InvalidOrderStateException("`to` must be on or after `from`.");
        }

        var paypal = await _paymentGateway.SearchTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersInDateRangeSpecification(from, to), cancellationToken);

        var eshopKeys = new Dictionary<string, Order>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in orders)
        {
            AddKey(eshopKeys, order.PayPalOrderId, order);
            AddKey(eshopKeys, order.PayPalInvoiceId, order);
            AddKey(eshopKeys, order.PayPalAuthorizationId, order);
            AddKey(eshopKeys, order.PayPalCaptureId, order);
            AddKey(eshopKeys, $"eshop-{order.Id}", order);
            AddKey(eshopKeys, order.Id.ToString(), order);
            foreach (var refund in order.Refunds)
            {
                AddKey(eshopKeys, refund.PayPalRefundId, order);
            }
        }

        var matchedOrderIds = new HashSet<int>();
        var rows = new List<ReconciliationRow>();
        var matched = 0;
        var paypalOnly = 0;

        foreach (var txn in paypal)
        {
            Order? order = null;
            if (!string.IsNullOrEmpty(txn.TransactionId))
            {
                eshopKeys.TryGetValue(txn.TransactionId, out order);
            }

            if (order is null && !string.IsNullOrEmpty(txn.ReferenceId))
            {
                eshopKeys.TryGetValue(txn.ReferenceId, out order);
            }

            if (order is null && !string.IsNullOrEmpty(txn.InvoiceId))
            {
                eshopKeys.TryGetValue(txn.InvoiceId, out order);
            }

            if (order is null && !string.IsNullOrEmpty(txn.CustomField))
            {
                eshopKeys.TryGetValue(txn.CustomField, out order);
            }

            if (order is not null)
            {
                matched++;
                matchedOrderIds.Add(order.Id);
                rows.Add(new ReconciliationRow(
                    "matched",
                    order.Id,
                    txn.TransactionId,
                    txn.InvoiceId,
                    "matched",
                    txn.Amount,
                    txn.Status));
            }
            else
            {
                paypalOnly++;
                rows.Add(new ReconciliationRow(
                    "paypal_only",
                    null,
                    txn.TransactionId,
                    txn.InvoiceId,
                    "paypal_only",
                    txn.Amount,
                    txn.Status));
            }
        }

        var eshopOnly = 0;
        foreach (var order in orders.Where(o => !matchedOrderIds.Contains(o.Id)))
        {
            eshopOnly++;
            rows.Add(new ReconciliationRow(
                "eshop_only",
                order.Id,
                order.PayPalCaptureId ?? order.PayPalAuthorizationId,
                order.PayPalInvoiceId ?? $"eshop-{order.Id}",
                "eshop_only",
                PayPalMoney.ToValue(order.Total()),
                order.PaymentStatus.ToString()));
        }

        return new ReconciliationReport(from, to, rows, paypal.Count, orders.Count, matched, paypalOnly, eshopOnly);
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }

        return order;
    }

    private async Task<SavedPaymentMethod> GetOwnedPaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var method = await _paymentMethodRepository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdSpec(paymentMethodId),
            cancellationToken);
        if (method is null)
        {
            throw new PaymentMethodNotFoundException(paymentMethodId);
        }

        if (!string.Equals(method.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new ForbiddenResourceException("This payment method does not belong to the caller.");
        }

        return method;
    }

    private static bool IsStale(AuthorizationResult authorization)
    {
        var now = DateTimeOffset.UtcNow;
        if (authorization.Expiration is DateTimeOffset expiration && expiration <= now)
        {
            return true;
        }

        return false;
    }

    private static bool LooksExpired(PaymentException ex)
    {
        var text = $"{ex.ProviderErrorName} {ex.Message}";
        return text.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("AUTH_EXPIRED", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddKey(Dictionary<string, Order> map, string? key, Order order)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            map[key] = order;
        }
    }
}
