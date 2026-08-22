using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderCheckoutService : IOrderCheckoutService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IUriComposer _uriComposer;

    public OrderCheckoutService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Buyer> buyerRepository,
        IPaymentGateway paymentGateway,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _buyerRepository = buyerRepository;
        _paymentGateway = paymentGateway;
        _uriComposer = uriComposer;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineRequest> items,
        Address shippingAddress,
        CancellationToken cancellationToken)
    {
        if (items is null || items.Count == 0)
        {
            throw new CheckoutException("An order requires at least one catalog item.", 400);
        }

        var grouped = items
            .GroupBy(i => i.CatalogItemId)
            .Select(g => new { CatalogItemId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToList();

        if (grouped.Any(g => g.Quantity <= 0))
        {
            throw new CheckoutException("Each item quantity must be greater than zero.", 400);
        }

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(grouped.Select(g => g.CatalogItemId).ToArray()),
            cancellationToken);

        var orderItems = new List<OrderItem>();
        foreach (var line in grouped)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
            if (catalogItem is null)
            {
                throw new CheckoutException($"Catalog item {line.CatalogItemId} was not found.", 400);
            }

            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shippingAddress, orderItems);
        return await _orderRepository.AddAsync(order, cancellationToken);
    }

    public async Task<Order> PayAsync(
        string buyerId,
        int orderId,
        CardPaymentSource? card,
        int? paymentMethodId,
        CancellationToken cancellationToken)
    {
        var order = await GetOwnedOrder(buyerId, orderId, cancellationToken);

        if (order.Status == OrderPaymentStatus.Authorized ||
            order.Status == OrderPaymentStatus.Fulfilled ||
            order.Status == OrderPaymentStatus.Refunded ||
            order.Status == OrderPaymentStatus.PartiallyRefunded)
        {
            return order;
        }

        if (order.Status == OrderPaymentStatus.Cancelled)
        {
            throw new CheckoutException("A cancelled order cannot be paid.", 409);
        }

        if (card is null && paymentMethodId is null)
        {
            throw new CheckoutException("Provide card details or a saved paymentMethodId.", 400);
        }

        if (card is not null && paymentMethodId is not null)
        {
            throw new CheckoutException("Provide either card details or a saved paymentMethodId, not both.", 400);
        }

        var amount = MoneyFormatter.Round(order.Total());
        if (amount <= 0m)
        {
            throw new CheckoutException("The order total must be greater than zero.", 400);
        }

        var requestId = order.AuthorizeRequestId;
        if (string.IsNullOrEmpty(requestId))
        {
            requestId = $"eshop-order-{order.Id}-authorize-{Guid.NewGuid():N}";
            order.SetAuthorizeRequestId(requestId);
            await _orderRepository.UpdateAsync(order, cancellationToken);
        }
        AuthorizationHold hold;

        if (paymentMethodId is int methodId)
        {
            var vaultId = await ResolveVaultId(buyerId, methodId, cancellationToken);
            hold = await _paymentGateway.AuthorizeVaultedCardAsync(
                order.Id, amount, vaultId, requestId, order.PayPalOrderId, cancellationToken);
        }
        else
        {
            hold = await _paymentGateway.AuthorizeCardAsync(
                order.Id, amount, card!, requestId, order.PayPalOrderId, cancellationToken);
        }

        order.AttachPayPalOrder(hold.PayPalOrderId, requestId);
        order.RecordAuthorization(
            hold.AuthorizationId,
            hold.Status,
            hold.ExpirationTime,
            hold.CreateTime,
            hold.Currency);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrder(orderId, cancellationToken);

        if (order.Status is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.Refunded
            or OrderPaymentStatus.PartiallyRefunded)
        {
            return order;
        }

        if (order.Status == OrderPaymentStatus.Cancelled)
        {
            throw new CheckoutException("A cancelled order cannot be fulfilled.", 409);
        }

        if (order.Status != OrderPaymentStatus.Authorized || string.IsNullOrEmpty(order.PayPalAuthorizationId))
        {
            throw new CheckoutException("The order has no authorization to capture. The shopper must pay first.", 409);
        }

        if (order.AuthorizationCreatedAt is DateTimeOffset created &&
            created.UtcDateTime.AddDays(30) <= DateTime.UtcNow)
        {
            throw new CheckoutException(
                "The PayPal authorization is older than 30 days and can no longer be renewed. Take a new payment from the shopper, then fulfil again.",
                409);
        }

        var captureRequestId = order.CaptureRequestId;
        if (string.IsNullOrEmpty(captureRequestId))
        {
            captureRequestId = $"eshop-order-{order.Id}-capture-{Guid.NewGuid():N}";
        }
        var authorizationId = order.PayPalAuthorizationId;
        var amount = MoneyFormatter.Round(order.Total());

        var authorization = await _paymentGateway.GetAuthorizationAsync(authorizationId, cancellationToken);
        if (string.Equals(authorization.Status, "CAPTURED", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(order.PayPalCaptureId))
        {
            return order;
        }

        if (string.Equals(authorization.Status, "VOIDED", StringComparison.OrdinalIgnoreCase))
        {
            throw new CheckoutException("The authorization was voided and cannot be captured. Take a new payment from the shopper.", 409);
        }

        if (IsStale(authorization))
        {
            if (order.AuthorizationCreatedAt is DateTimeOffset original &&
                original.UtcDateTime.AddDays(30) <= DateTime.UtcNow)
            {
                throw new CheckoutException(
                    "The PayPal authorization can no longer be renewed (more than 30 days after the original hold). Take a new payment from the shopper, then fulfil again.",
                    409);
            }

            var reauthRequestId = $"eshop-order-{order.Id}-reauthorize";
            try
            {
                var renewed = await _paymentGateway.ReauthorizeAsync(
                    authorizationId, amount, reauthRequestId, cancellationToken);
                order.RecordReauthorization(
                    renewed.AuthorizationId, renewed.Status, renewed.ExpirationTime, renewed.CreateTime);
                authorizationId = renewed.AuthorizationId;
                await _orderRepository.UpdateAsync(order, cancellationToken);
            }
            catch (CheckoutException ex) when (ex.StatusCode is 422 or 400 or 409)
            {
                throw new CheckoutException(
                    "The PayPal authorization is stale and could not be renewed. Take a new payment from the shopper, then fulfil again. " + ex.Message,
                    409);
            }
        }

        var capture = await _paymentGateway.CaptureAsync(authorizationId, captureRequestId, cancellationToken);
        order.RecordCapture(
            capture.CaptureId,
            capture.Status,
            capture.CapturedAmount,
            capture.PaypalFee,
            capture.NetAmount,
            capture.Currency,
            captureRequestId);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrder(orderId, cancellationToken);

        if (order.Status == OrderPaymentStatus.Cancelled)
        {
            return order;
        }

        if (order.Status is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.Refunded
            or OrderPaymentStatus.PartiallyRefunded)
        {
            throw new CheckoutException("A fulfilled order cannot be cancelled. Issue a refund instead.", 409);
        }

        if (order.Status == OrderPaymentStatus.Authorized && !string.IsNullOrEmpty(order.PayPalAuthorizationId))
        {
            var voidRequestId = order.VoidRequestId;
            if (string.IsNullOrEmpty(voidRequestId))
            {
                voidRequestId = $"eshop-order-{order.Id}-void-{Guid.NewGuid():N}";
            }
            await _paymentGateway.VoidAsync(order.PayPalAuthorizationId, voidRequestId, cancellationToken);
            order.MarkCancelled(voidRequestId);
        }
        else
        {
            order.MarkCancelled(null);
        }

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
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new CheckoutException("A refund requires an idempotencyKey.", 400);
        }

        var order = await GetOwnedOrder(buyerId, orderId, cancellationToken);

        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        if (order.Status is not (OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded))
        {
            throw new CheckoutException("Only a fulfilled order can be refunded.", 409);
        }

        if (string.IsNullOrEmpty(order.PayPalCaptureId) || order.CapturedAmount is null)
        {
            throw new CheckoutException("The order has no captured payment to refund.", 409);
        }

        var remaining = order.RemainingRefundable();
        var refundAmount = amount is null ? remaining : MoneyFormatter.Round(amount.Value);

        if (refundAmount <= 0m)
        {
            throw new CheckoutException("The refund amount must be greater than zero.", 400);
        }

        if (refundAmount > remaining)
        {
            throw new CheckoutException(
                $"The refund amount {MoneyFormatter.ToPayPalValue(refundAmount, order.PaymentCurrency ?? _paymentGateway.Currency)} exceeds the remaining captured amount {MoneyFormatter.ToPayPalValue(remaining, order.PaymentCurrency ?? _paymentGateway.Currency)}.",
                400);
        }

        var isFullRemaining = refundAmount == remaining;
        var paypalRequestId = $"eshop-order-{order.Id}-refund-{idempotencyKey}";
        var result = await _paymentGateway.RefundAsync(
            order.PayPalCaptureId,
            isFullRemaining && amount is null ? null : refundAmount,
            paypalRequestId,
            cancellationToken);

        var refund = order.AddRefund(
            result.RefundId,
            result.Amount == 0m ? refundAmount : result.Amount,
            result.Currency,
            result.Status,
            idempotencyKey);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return refund;
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    private async Task<Order> GetOrder(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new CheckoutException($"Order {orderId} was not found.", 404);
        }

        return order;
    }

    private async Task<Order> GetOwnedOrder(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrder(orderId, cancellationToken);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new CheckoutException("The order does not belong to the signed-in shopper.", 403);
        }

        return order;
    }

    private async Task<string> ResolveVaultId(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(
            new BuyerByIdentitySpecification(buyerId), cancellationToken);
        var method = buyer?.GetPaymentMethod(paymentMethodId);
        if (method is null || string.IsNullOrEmpty(method.CardId))
        {
            throw new CheckoutException("The saved card was not found or is no longer usable.", 404);
        }

        return method.CardId;
    }

    private static bool IsStale(AuthorizationHold authorization)
    {
        if (authorization.ExpirationTime is DateTimeOffset expiration)
        {
            return expiration.UtcDateTime <= DateTime.UtcNow;
        }

        if (authorization.CreateTime is DateTimeOffset created)
        {
            return created.UtcDateTime.AddDays(3) <= DateTime.UtcNow;
        }

        return false;
    }
}
