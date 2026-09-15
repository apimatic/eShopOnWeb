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
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalGateway _payPal;
    private readonly IPaymentSettings _paymentSettings;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IUriComposer uriComposer,
        IPayPalGateway payPal,
        IPaymentSettings paymentSettings)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _uriComposer = uriComposer;
        _payPal = payPal;
        _paymentSettings = paymentSettings;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineRequest> items,
        ShippingAddressRequest shippingAddress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException("A signed-in shopper is required.", 401);
        }

        if (items == null || items.Count == 0)
        {
            throw new PaymentException("The order must contain at least one catalog item.", 400);
        }

        if (items.Any(i => i.Quantity <= 0))
        {
            throw new PaymentException("Each item quantity must be greater than zero.", 400);
        }

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids));
        if (catalogItems.Count != ids.Length)
        {
            throw new PaymentException("One or more catalog items were not found.", 400);
        }

        var catalogById = catalogItems.ToDictionary(c => c.Id);
        var orderItems = items.Select(line =>
        {
            var catalogItem = catalogById[line.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = new Address(
            shippingAddress.Street,
            shippingAddress.City,
            shippingAddress.State ?? string.Empty,
            shippingAddress.Country,
            shippingAddress.ZipCode);

        var order = new Order(buyerId, address, orderItems);
        order.SetCurrency(_paymentSettings.Currency);
        await _orderRepository.AddAsync(order);
        return order;
    }

    public async Task<Order> PayAsync(
        string buyerId,
        int orderId,
        CardPaymentDetails? card,
        int? paymentMethodId,
        CancellationToken cancellationToken)
    {
        var order = await GetOwnedOrder(buyerId, orderId);
        if (order.PaymentStatus is OrderPaymentStatus.Authorized
            or OrderPaymentStatus.Captured
            or OrderPaymentStatus.Refunded
            or OrderPaymentStatus.PartiallyRefunded)
        {
            return order;
        }

        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            throw new PaymentException("A cancelled order cannot be paid.", 409);
        }

        if (card == null && paymentMethodId == null)
        {
            throw new PaymentException("Provide card details or a saved paymentMethodId.", 400);
        }

        if (card != null && paymentMethodId != null)
        {
            throw new PaymentException("Provide either card details or a saved paymentMethodId, not both.", 400);
        }

        var amount = order.Total();
        if (amount <= 0)
        {
            throw new PaymentException("The order total must be greater than zero.", 400);
        }

        var currency = order.Currency ?? _paymentSettings.Currency;
        var idempotencyKey = string.IsNullOrEmpty(order.AuthorizeIdempotencyKey)
            ? $"eshop-auth-{order.Id}-{Guid.NewGuid():N}"
            : order.AuthorizeIdempotencyKey;

        try
        {
            AuthorizationHold hold;
            if (paymentMethodId.HasValue)
            {
                var saved = await _paymentMethodRepository.FirstOrDefaultAsync(
                    new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId.Value, buyerId));
                if (saved == null)
                {
                    throw new PaymentException("Saved card was not found.", 404);
                }

                hold = await _payPal.AuthorizeVaultedCardAsync(
                    order.Id, amount, currency, saved.PayPalPaymentTokenId, idempotencyKey, cancellationToken);
            }
            else
            {
                hold = await _payPal.AuthorizeCardAsync(
                    order.Id, amount, currency, card!, idempotencyKey, cancellationToken);
            }

            order.MarkAuthorized(hold, idempotencyKey);
            await _orderRepository.UpdateAsync(order);
            return order;
        }
        catch (PaymentException)
        {
            order.MarkPaymentFailed();
            await _orderRepository.UpdateAsync(order);
            throw;
        }
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrder(orderId);
        if (order.PaymentStatus is OrderPaymentStatus.Captured
            or OrderPaymentStatus.Refunded
            or OrderPaymentStatus.PartiallyRefunded)
        {
            return order;
        }

        if (order.PaymentStatus != OrderPaymentStatus.Authorized
            || string.IsNullOrEmpty(order.PayPalAuthorizationId))
        {
            throw new PaymentException("The order does not have an authorization to capture.", 409);
        }

        var now = DateTimeOffset.UtcNow;
        if (order.AuthorizationPastRenewalWindow(now))
        {
            throw new PaymentException(
                "The authorization is more than 30 days old and cannot be renewed. Ask the shopper to pay again.",
                409);
        }

        var authorizationId = order.PayPalAuthorizationId;
        var current = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
        order.ReplaceAuthorization(current);

        if (current.Status.Equals("VOIDED", System.StringComparison.OrdinalIgnoreCase)
            || current.Status.Equals("DENIED", System.StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException(
                $"The authorization is {current.Status} and cannot be captured. Ask the shopper to pay again.",
                409);
        }

        if (order.AuthorizationNeedsRenewal(now)
            || current.ExpiresAt.HasValue && current.ExpiresAt.Value <= now)
        {
            try
            {
                var renewed = await _payPal.ReauthorizeAsync(
                    authorizationId,
                    order.Total(),
                    order.Currency ?? _paymentSettings.Currency,
                    $"eshop-reauth-{order.Id}",
                    cancellationToken);
                order.ReplaceAuthorization(renewed);
                authorizationId = renewed.AuthorizationId;
                await _orderRepository.UpdateAsync(order);
            }
            catch (PaymentException ex)
            {
                throw new PaymentException(
                    "The authorization could not be renewed. Capture it immediately if it is still in the honor period, or ask the shopper to pay again. "
                    + ex.Message,
                    ex.StatusCode,
                    ex.DebugId,
                    ex);
            }
        }

        var captureKey = $"eshop-capture-{order.Id}";
        CaptureDetails capture;
        try
        {
            capture = await _payPal.CaptureAsync(authorizationId, captureKey, cancellationToken);
        }
        catch (PaymentException ex) when (ex.StatusCode == 422)
        {
            if (order.AuthorizationPastRenewalWindow(DateTimeOffset.UtcNow))
            {
                throw new PaymentException(
                    "The authorization is stale and can no longer be renewed (more than 30 days from the original hold). Ask the shopper to pay again. "
                    + ex.Message,
                    409,
                    ex.DebugId,
                    ex);
            }

            var renewed = await _payPal.ReauthorizeAsync(
                authorizationId,
                order.Total(),
                order.Currency ?? _paymentSettings.Currency,
                $"eshop-reauth-{order.Id}",
                cancellationToken);
            order.ReplaceAuthorization(renewed);
            await _orderRepository.UpdateAsync(order);
            capture = await _payPal.CaptureAsync(renewed.AuthorizationId, captureKey, cancellationToken);
        }

        if (capture.PaypalFee == null || capture.NetAmount == null)
        {
            capture = await _payPal.GetCaptureAsync(capture.CaptureId, cancellationToken);
        }

        order.MarkCaptured(capture, captureKey);
        await _orderRepository.UpdateAsync(order);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrder(orderId);
        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            return order;
        }

        if (order.PaymentStatus is OrderPaymentStatus.Captured
            or OrderPaymentStatus.Refunded
            or OrderPaymentStatus.PartiallyRefunded)
        {
            throw new PaymentException("A fulfilled order cannot be cancelled; refund it instead.", 409);
        }

        var cancelKey = $"eshop-void-{order.Id}";
        if (!string.IsNullOrEmpty(order.PayPalAuthorizationId)
            && order.PaymentStatus == OrderPaymentStatus.Authorized)
        {
            await _payPal.VoidAsync(order.PayPalAuthorizationId, cancelKey, cancellationToken);
        }

        order.MarkCancelled(cancelKey);
        await _orderRepository.UpdateAsync(order);
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
            throw new PaymentException("A refund idempotencyKey is required.", 400);
        }

        var order = await GetOwnedOrder(buyerId, orderId);
        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        if (order.PaymentStatus is not (OrderPaymentStatus.Captured or OrderPaymentStatus.PartiallyRefunded))
        {
            throw new PaymentException("Only a captured order can be refunded.", 409);
        }

        if (string.IsNullOrEmpty(order.PayPalCaptureId) || order.CapturedAmount == null)
        {
            throw new PaymentException("The order has no captured payment to refund.", 409);
        }

        var remaining = order.RemainingRefundable();
        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0)
        {
            throw new PaymentException("The refund amount must be greater than zero.", 400);
        }

        if (refundAmount > remaining)
        {
            throw new PaymentException(
                $"Refund of {refundAmount:0.00} exceeds the remaining refundable amount of {remaining:0.00}.",
                400);
        }

        var result = await _payPal.RefundAsync(
            order.PayPalCaptureId,
            amount.HasValue ? refundAmount : null,
            order.Currency ?? _paymentSettings.Currency,
            idempotencyKey,
            cancellationToken);

        var recorded = order.RecordRefund(result, idempotencyKey);
        await _orderRepository.UpdateAsync(order);
        return recorded;
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
    }

    public async Task<Order?> GetOrderForBuyerAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId));
        if (order == null || order.BuyerId != buyerId)
        {
            return null;
        }

        return order;
    }

    private async Task<Order> GetOwnedOrder(string buyerId, int orderId)
    {
        var order = await GetOrder(orderId);
        if (order.BuyerId != buyerId)
        {
            throw new PaymentException("Order was not found.", 404);
        }

        return order;
    }

    private async Task<Order> GetOrder(int orderId)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId));
        if (order == null)
        {
            throw new PaymentException("Order was not found.", 404);
        }

        return order;
    }
}
