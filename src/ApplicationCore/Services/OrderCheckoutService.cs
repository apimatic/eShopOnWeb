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

public class OrderCheckoutService : IOrderCheckoutService
{
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
            throw new PaymentException("The order must contain at least one catalog item.", 400);
        }

        foreach (var item in items)
        {
            if (item.Quantity <= 0)
            {
                throw new PaymentException("Each item quantity must be greater than zero.", 400);
            }
        }

        var catalogItemIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var missing = catalogItemIds.Where(id => !catalogById.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
        {
            throw new PaymentException($"Catalog item(s) not found: {string.Join(", ", missing)}.", 400);
        }

        var orderItems = items.Select(requested =>
        {
            var catalogItem = catalogById[requested.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, requested.Quantity);
        }).ToList();

        var address = shipTo == null
            ? new Address("123 Main St", "Anytown", "CA", "US", "12345")
            : new Address(shipTo.Street, shipTo.City, shipTo.State, shipTo.Country, shipTo.ZipCode);

        var order = new Order(buyerId, address, orderItems);
        return await _orderRepository.AddAsync(order, cancellationToken);
    }

    public async Task<Order> PayAsync(
        int orderId,
        string buyerId,
        CardPaymentSource? card,
        int? savedPaymentMethodId,
        CancellationToken cancellationToken = default)
    {
        if (card == null && savedPaymentMethodId == null)
        {
            throw new PaymentException("Provide card details or a saved paymentMethodId.", 400);
        }

        if (card != null && savedPaymentMethodId != null)
        {
            throw new PaymentException("Provide either card details or a saved paymentMethodId, not both.", 400);
        }

        var order = await LoadOrderAsync(orderId, cancellationToken);
        EnsureShopperOwns(order, buyerId);

        if (order.PaymentStatus is OrderPaymentStatus.Authorized
            or OrderPaymentStatus.Fulfilled
            or OrderPaymentStatus.PartiallyRefunded
            or OrderPaymentStatus.Refunded)
        {
            return order;
        }

        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            throw new PaymentException("A cancelled order cannot be paid.", 409);
        }

        var currency = _payPal.Currency;
        var amount = MoneyFormat.Round(order.Total());
        var requestId = order.NextAuthorizeRequestId();

        try
        {
            PayPalAuthorizationResult auth;
            if (savedPaymentMethodId != null)
            {
                var method = await _paymentMethodRepository.FirstOrDefaultAsync(
                    new SavedPaymentMethodByIdAndBuyerSpec(savedPaymentMethodId.Value, buyerId),
                    cancellationToken);

                if (method == null)
                {
                    throw new PaymentException("Saved payment method not found.", 404);
                }

                auth = await _payPal.AuthorizeVaultedCardAsync(
                    amount, currency, order.InvoiceId, requestId, method.PayPalVaultId, cancellationToken);
            }
            else
            {
                auth = await _payPal.AuthorizeCardAsync(
                    amount, currency, order.InvoiceId, requestId, card!, cancellationToken);
            }

            if (auth.Amount != amount)
            {
                throw new PaymentException(
                    $"PayPal authorized {MoneyFormat.ToPayPalValue(auth.Amount)} {auth.Currency} but the order total is {MoneyFormat.ToPayPalValue(amount)} {currency}.",
                    502);
            }

            order.MarkAuthorized(auth.PayPalOrderId, auth.AuthorizationId, auth.Status, auth.Currency, auth.ExpirationTime);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        }
        catch (PayerActionRequiredException)
        {
            throw;
        }
        catch (PaymentException)
        {
            order.RecordFailedPayAttempt();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            throw;
        }
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);

        if (order.PaymentStatus is OrderPaymentStatus.Fulfilled
            or OrderPaymentStatus.PartiallyRefunded
            or OrderPaymentStatus.Refunded)
        {
            return order;
        }

        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            throw new PaymentException("A cancelled order cannot be fulfilled.", 409);
        }

        if (order.PaymentStatus != OrderPaymentStatus.Authorized || string.IsNullOrEmpty(order.PayPalAuthorizationId))
        {
            throw new PaymentException("The order has not been authorized, so it cannot be fulfilled.", 409);
        }

        var currency = order.Currency ?? _payPal.Currency;
        var amount = MoneyFormat.Round(order.Total());
        var authorizationId = await EnsureFreshAuthorizationAsync(order, amount, currency, cancellationToken);

        PayPalCaptureResult capture;
        try
        {
            capture = await _payPal.CaptureAuthorizationAsync(
                authorizationId,
                amount,
                currency,
                order.InvoiceId,
                order.CaptureRequestId(),
                cancellationToken);
        }
        catch (PaymentException ex) when (IsStaleAuthorization(ex))
        {
            authorizationId = await RenewAuthorizationAsync(order, amount, currency, cancellationToken);
            capture = await _payPal.CaptureAuthorizationAsync(
                authorizationId,
                amount,
                currency,
                order.InvoiceId,
                order.CaptureRequestId() + "-retry",
                cancellationToken);
        }

        order.MarkFulfilled(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PaypalFee, capture.NetProceeds);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);

        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            return order;
        }

        if (order.PaymentStatus is OrderPaymentStatus.Fulfilled
            or OrderPaymentStatus.PartiallyRefunded
            or OrderPaymentStatus.Refunded)
        {
            throw new PaymentException("A fulfilled order cannot be cancelled. Issue a refund instead.", 409);
        }

        if (!string.IsNullOrEmpty(order.PayPalAuthorizationId) && order.PaymentStatus == OrderPaymentStatus.Authorized)
        {
            await _payPal.VoidAuthorizationAsync(order.PayPalAuthorizationId, order.VoidRequestId(), cancellationToken);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<(Order Order, OrderRefund Refund)> RefundAsync(
        int orderId,
        string buyerId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentException("An idempotencyKey is required for refunds.", 400);
        }

        var order = await LoadOrderAsync(orderId, cancellationToken);
        EnsureShopperOwns(order, buyerId);

        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return (order, existing);
        }

        if (string.IsNullOrEmpty(order.PayPalCaptureId) || order.CapturedAmount == null)
        {
            throw new PaymentException("The order has not been captured, so it cannot be refunded.", 409);
        }

        var remaining = order.RemainingRefundable();
        var refundAmount = amount.HasValue ? MoneyFormat.Round(amount.Value) : remaining;
        if (refundAmount <= 0)
        {
            throw new PaymentException("There is no remaining captured amount to refund.", 409);
        }

        if (refundAmount > remaining)
        {
            throw new PaymentException(
                $"Refund of {MoneyFormat.ToPayPalValue(refundAmount)} exceeds the remaining refundable amount of {MoneyFormat.ToPayPalValue(remaining)} {order.Currency}.",
                400);
        }

        var result = await _payPal.RefundCaptureAsync(
            order.PayPalCaptureId,
            refundAmount,
            order.Currency ?? _payPal.Currency,
            idempotencyKey,
            cancellationToken);

        var refund = order.AddRefund(result.RefundId, result.Status, result.Amount, result.Currency, idempotencyKey);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return (order, refund);
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    private async Task<string> EnsureFreshAuthorizationAsync(
        Order order,
        decimal amount,
        string currency,
        CancellationToken cancellationToken)
    {
        var authorizationId = order.PayPalAuthorizationId!;
        var now = DateTimeOffset.UtcNow;

        PayPalAuthorizationDetails details;
        try
        {
            details = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (PaymentException ex) when (ex.StatusCode == 404)
        {
            throw new PaymentException(
                "PayPal no longer has this authorization. Ask the shopper to pay the order again.",
                409,
                ex.PayPalDebugId,
                ex.PayPalIssue);
        }

        order.UpdateAuthorizationStatus(details.Status);

        if (string.Equals(details.Status, "CAPTURED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(details.Status, "PARTIALLY_CAPTURED", StringComparison.OrdinalIgnoreCase))
        {
            return authorizationId;
        }

        if (string.Equals(details.Status, "VOIDED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(details.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException(
                $"PayPal authorization {authorizationId} is {details.Status} and cannot be captured or renewed. Ask the shopper to pay the order again.",
                409);
        }

        var expired = string.Equals(details.Status, "EXPIRED", StringComparison.OrdinalIgnoreCase)
            || (details.ExpirationTime != null && details.ExpirationTime <= now);

        if (expired || order.AuthorizationHonorPeriodElapsed(now))
        {
            return await RenewAuthorizationAsync(order, amount, currency, cancellationToken);
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return authorizationId;
    }

    private async Task<string> RenewAuthorizationAsync(
        Order order,
        decimal amount,
        string currency,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (order.AuthorizationWindowClosed(now))
        {
            throw new PaymentException(
                "This payment authorization can no longer be renewed. PayPal's 29-day authorization window has closed. Ask the shopper to place and pay a new order.",
                409);
        }

        try
        {
            var renewed = await _payPal.ReauthorizeAsync(
                order.PayPalAuthorizationId!,
                amount,
                currency,
                $"eshop-reauth-{order.Id}-{now.ToUnixTimeSeconds()}",
                cancellationToken);

            order.ApplyReauthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpirationTime);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return renewed.AuthorizationId;
        }
        catch (PaymentException ex) when (string.Equals(ex.PayPalIssue, "REAUTHORIZATION_TOO_SOON", StringComparison.OrdinalIgnoreCase))
        {
            return order.PayPalAuthorizationId!;
        }
        catch (PaymentException ex) when (
            string.Equals(ex.PayPalIssue, "AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ex.PayPalIssue, "AUTHORIZATION_VOIDED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ex.PayPalIssue, "MAX_NUMBER_OF_REAUTHORIZATION_EXCEEDED", StringComparison.OrdinalIgnoreCase) ||
            ex.StatusCode is 404 or 422)
        {
            throw new PaymentException(
                "PayPal could not renew this authorization. The hold on the shopper's funds has expired and cannot be recaptured. Ask the shopper to pay the order again.",
                409,
                ex.PayPalDebugId,
                ex.PayPalIssue);
        }
    }

    private static bool IsStaleAuthorization(PaymentException ex) =>
        string.Equals(ex.PayPalIssue, "AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ex.PayPalIssue, "AUTHORIZATION_VOIDED", StringComparison.OrdinalIgnoreCase)
        || (ex.Message?.Contains("expired", StringComparison.OrdinalIgnoreCase) ?? false);

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            throw new PaymentException($"Order {orderId} was not found.", 404);
        }

        return order;
    }

    private static void EnsureShopperOwns(Order order, string buyerId)
    {
        if (!order.OwnedBy(buyerId))
        {
            throw new PaymentException("Order not found.", 404);
        }
    }
}
