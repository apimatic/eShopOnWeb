using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPayPalPaymentsGateway _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalSettings _payPalSettings;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPayPalPaymentsGateway payPal,
        IUriComposer uriComposer,
        IPayPalSettings payPalSettings)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _payPalSettings = payPalSettings;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<(int CatalogItemId, int Quantity)> items,
        Address shippingAddress,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items is null || items.Count == 0)
        {
            throw new CommerceException(400, "An order must contain at least one catalog item.");
        }

        var catalogItemIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var orderItems = new List<OrderItem>();
        foreach (var (catalogItemId, quantity) in items)
        {
            if (quantity <= 0)
            {
                throw new CommerceException(400, $"Quantity for catalog item {catalogItemId} must be greater than zero.");
            }

            if (!catalogById.TryGetValue(catalogItemId, out var catalogItem))
            {
                throw new CommerceException(400, $"Catalog item {catalogItemId} was not found.");
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, quantity));
        }

        var order = new Order(buyerId, shippingAddress, orderItems);
        return await _orderRepository.AddAsync(order, cancellationToken);
    }

    public async Task<Order> PayAsync(
        int orderId,
        string buyerId,
        CardPaymentDetails? card,
        int? paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var order = await GetOwnedOrder(orderId, buyerId, cancellationToken);

        if (order.Status == OrderStatus.Authorized || order.Status == OrderStatus.Fulfilled
            || order.Status == OrderStatus.PartiallyRefunded || order.Status == OrderStatus.Refunded)
        {
            return order;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new CommerceException(409, "This order was cancelled and cannot be paid.");
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new CommerceException(409, $"Order {orderId} cannot be paid in its current state ({order.Status}).");
        }

        var currency = _payPalSettings.Currency;
        var amount = order.Total();
        if (amount <= 0)
        {
            throw new CommerceException(400, "Order total must be greater than zero.");
        }

        var lineItems = order.OrderItems.Select(i => new PayPalLineItem
        {
            Name = i.ItemOrdered.ProductName,
            Quantity = i.Units,
            UnitPrice = i.UnitPrice
        }).ToList();

        var requestId = $"eshop-pay-{order.Id}";
        PayPalAuthorizeResult authorization;

        if (paymentMethodId.HasValue)
        {
            var saved = await _paymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdAndBuyerSpecification(paymentMethodId.Value, buyerId), cancellationToken);
            if (saved is null)
            {
                throw new CommerceException(404, "Saved payment method was not found.");
            }

            authorization = await _payPal.AuthorizeVaultedCardPaymentAsync(
                order.Id, amount, currency, lineItems, saved.PayPalPaymentTokenId, requestId, cancellationToken);
        }
        else if (card is not null)
        {
            authorization = await _payPal.AuthorizeCardPaymentAsync(
                order.Id, amount, currency, lineItems, card, requestId, cancellationToken);
        }
        else
        {
            throw new CommerceException(400, "Provide card details or a saved paymentMethodId.");
        }

        if (authorization.AuthorizedAmount != decimal.Round(amount, 2))
        {
            if (!string.IsNullOrEmpty(authorization.AuthorizationId))
            {
                await _payPal.VoidAuthorizationAsync(authorization.AuthorizationId, $"eshop-void-mismatch-{order.Id}", cancellationToken);
            }

            throw new CommerceException(502, "PayPal authorized an amount that does not match the order total. The hold was released.");
        }

        if (string.IsNullOrEmpty(authorization.AuthorizationId))
        {
            throw new CommerceException(502, "PayPal did not return an authorization id for the payment hold.");
        }

        order.RecordAuthorization(
            authorization.PayPalOrderId,
            authorization.PayPalOrderStatus,
            authorization.AuthorizationId,
            authorization.AuthorizationStatus ?? "CREATED",
            authorization.AuthorizationExpiresAt,
            authorization.Currency);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrder(orderId, cancellationToken);

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            return order;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new CommerceException(409, "A cancelled order cannot be fulfilled.");
        }

        if (order.Status != OrderStatus.Authorized || string.IsNullOrEmpty(order.AuthorizationId))
        {
            throw new CommerceException(409, "The order must have an authorized payment hold before it can be fulfilled.");
        }

        var authorization = await EnsureCapturableAuthorization(order, cancellationToken);
        var currency = order.Currency ?? _payPalSettings.Currency;
        var amount = order.Total();

        var capture = await _payPal.CaptureAuthorizationAsync(
            authorization.AuthorizationId,
            amount,
            currency,
            $"eshop-fulfil-{order.Id}",
            cancellationToken);

        order.RecordCapture(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PayPalFee, capture.NetAmount);
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

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            throw new CommerceException(409, "A fulfilled order cannot be cancelled. Issue a refund instead.");
        }

        if (order.Status == OrderStatus.Authorized && !string.IsNullOrEmpty(order.AuthorizationId))
        {
            try
            {
                await _payPal.VoidAuthorizationAsync(order.AuthorizationId, $"eshop-cancel-{order.Id}", cancellationToken);
            }
            catch (CommerceException ex) when (ex.StatusCode == 422 || ex.StatusCode == 404)
            {
                var latest = await _payPal.GetAuthorizationAsync(order.AuthorizationId, cancellationToken);
                if (!string.Equals(latest.Status, "VOIDED", StringComparison.OrdinalIgnoreCase))
                {
                    throw;
                }
            }
        }

        order.RecordCancellation();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<OrderRefund> RefundAsync(
        int orderId,
        string buyerId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new CommerceException(400, "A caller-supplied idempotencyKey is required for refunds.");
        }

        var order = await GetOwnedOrder(orderId, buyerId, cancellationToken);

        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        if (order.Status is not OrderStatus.Fulfilled and not OrderStatus.PartiallyRefunded)
        {
            throw new CommerceException(409, "Refunds can only be issued for a fulfilled capture that still has a refundable balance.");
        }

        if (string.IsNullOrEmpty(order.CaptureId) || order.CapturedAmount is null)
        {
            throw new CommerceException(409, "This order has no captured payment to refund.");
        }

        var refundAmount = amount ?? order.RefundableAmount();
        refundAmount = decimal.Round(refundAmount, 2);
        if (refundAmount <= 0)
        {
            throw new CommerceException(400, "Refund amount must be greater than zero.");
        }

        if (refundAmount > order.RefundableAmount())
        {
            throw new CommerceException(409, $"Refund of {refundAmount} exceeds the remaining captured amount of {order.RefundableAmount()}.");
        }

        var result = await _payPal.RefundCaptureAsync(
            order.CaptureId,
            refundAmount,
            order.Currency ?? _payPalSettings.Currency,
            idempotencyKey,
            cancellationToken);

        var refund = order.AddRefund(idempotencyKey, result.Amount, result.PayPalRefundId, result.Status);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return refund;
    }

    public async Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), cancellationToken);
        return orders;
    }

    public Task<Order?> GetBuyerOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
        => GetOwnedOrderOrDefault(orderId, buyerId, cancellationToken);

    private async Task<PayPalAuthorizationDetails> EnsureCapturableAuthorization(Order order, CancellationToken cancellationToken)
    {
        var authorization = await _payPal.GetAuthorizationAsync(order.AuthorizationId!, cancellationToken);

        if (string.Equals(authorization.Status, "CAPTURED", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(order.CaptureId))
        {
            return authorization;
        }

        if (string.Equals(authorization.Status, "VOIDED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(authorization.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            throw new CommerceException(409,
                $"The PayPal authorization {authorization.AuthorizationId} is {authorization.Status} and cannot be captured. Ask the shopper to pay again.");
        }

        var stale = authorization.ExpirationTime is not null
                    && authorization.ExpirationTime <= DateTimeOffset.UtcNow.AddHours(3);

        if (stale || string.Equals(authorization.Status, "EXPIRED", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                authorization = await _payPal.ReauthorizeAsync(
                    authorization.AuthorizationId,
                    order.Total(),
                    order.Currency ?? _payPalSettings.Currency,
                    $"eshop-reauth-{order.Id}",
                    cancellationToken);
            }
            catch (CommerceException ex)
            {
                throw new CommerceException(409,
                    "The payment hold has expired and PayPal can no longer renew it. Ask the shopper to pay the order again before fulfilment. "
                    + ex.Message);
            }

            order.RefreshAuthorization(authorization.AuthorizationId, authorization.Status, authorization.ExpirationTime);
            await _orderRepository.UpdateAsync(order, cancellationToken);
        }

        return authorization;
    }

    private async Task<Order> GetOrder(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpecification(orderId), cancellationToken);
        if (order is null)
        {
            throw new CommerceException(404, $"Order {orderId} was not found.");
        }

        return order;
    }

    private async Task<Order> GetOwnedOrder(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await GetOwnedOrderOrDefault(orderId, buyerId, cancellationToken);
        if (order is null)
        {
            throw new CommerceException(404, $"Order {orderId} was not found.");
        }

        return order;
    }

    private async Task<Order?> GetOwnedOrderOrDefault(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpecification(orderId), cancellationToken);
        if (order is null)
        {
            return null;
        }

        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new CommerceException(404, $"Order {orderId} was not found.");
        }

        return order;
    }
}
