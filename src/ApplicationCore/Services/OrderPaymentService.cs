using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    /// <summary>PayPal allows reauthorization only within this many days of the *original* authorization.</summary>
    private static readonly TimeSpan MaxReauthorizationWindow = TimeSpan.FromDays(29);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IPayPalGateway _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalOptions _options;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<PaymentMethod> paymentMethodRepository,
        IPayPalGateway payPal,
        IUriComposer uriComposer,
        PayPalOptions options)
    {
        _orderRepository = orderRepository;
        _catalogItemRepository = catalogItemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _options = options;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items, Address shipToAddress)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items is null || items.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one item.", nameof(items));
        }

        var catalogItemsSpec = new CatalogItemsSpecification(items.Select(i => i.CatalogItemId).ToArray());
        var catalogItems = await _catalogItemRepository.ListAsync(catalogItemsSpec);

        var orderItems = new List<OrderItem>();
        foreach (var requested in items)
        {
            if (requested.Quantity <= 0)
            {
                throw new ArgumentException($"Quantity for catalog item {requested.CatalogItemId} must be positive.", nameof(items));
            }

            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == requested.CatalogItemId)
                ?? throw new CatalogItemNotFoundException(requested.CatalogItemId);

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, requested.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, orderItems);
        return await _orderRepository.AddAsync(order);
    }

    public async Task<Order> AuthorizePaymentAsync(string buyerId, int orderId, PayPalCardDetails? card, int? paymentMethodId)
    {
        var order = await GetOwnedOrderAsync(buyerId, orderId);

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            if (order.Status == OrderStatus.Cancelled)
            {
                throw new InvalidOrderStateException(orderId, order.Status, "authorize payment for");
            }
            // Already authorized (or further along) - a double-click must not authorize twice.
            return order;
        }

        if ((card is null) == (paymentMethodId is null))
        {
            throw new ArgumentException("Provide either card details or a saved payment method id, not both.");
        }

        string? vaultId = null;
        if (paymentMethodId is not null)
        {
            var paymentMethod = await _paymentMethodRepository.GetByIdAsync(paymentMethodId.Value)
                ?? throw new PaymentMethodNotFoundException(paymentMethodId.Value);
            if (paymentMethod.BuyerId != buyerId)
            {
                throw new ForbiddenAccessException("This payment method does not belong to the caller.");
            }
            vaultId = paymentMethod.PayPalPaymentTokenId;
        }

        var amount = order.Total();
        var currency = RequireCurrency();
        var invoiceId = $"eShop-Order-{orderId}";
        var requestId = $"authorize-order-{orderId}";

        var result = await _payPal.AuthorizeCardPaymentAsync(amount, currency, invoiceId, requestId, card, vaultId);

        if (result.RequiresPayerAction)
        {
            throw new PayPalActionRequiredException(
                $"PayPal requires the shopper to complete an additional verification step (payer action) for order {orderId}. " +
                "This integration is direct server-to-server card processing only and does not support a browser approval round trip.");
        }

        if (string.IsNullOrEmpty(result.AuthorizationId))
        {
            throw new PayPalOperationException($"PayPal did not return an authorization for order {orderId} (order status: {result.OrderStatus}).");
        }

        order.BeginAuthorization(amount, currency, result.PayPalOrderId, requestId, paymentMethodId,
            result.AuthorizationId!, result.AuthorizationStatus ?? "CREATED", result.CreateTime ?? DateTimeOffset.UtcNow, result.ExpirationTime);

        await _orderRepository.UpdateAsync(order);
        return order;
    }

    public async Task<Order> FulfilOrderAsync(int orderId)
    {
        var order = await GetOrderWithPaymentAsync(orderId);

        if (order.Status == OrderStatus.Fulfilled)
        {
            return order;
        }

        if (order.Status != OrderStatus.PaymentAuthorized || order.Payment is null)
        {
            throw new InvalidOrderStateException(orderId, order.Status, "fulfil");
        }

        var payment = order.Payment;
        var now = DateTimeOffset.UtcNow;
        var isStale = payment.AuthorizationExpirationTime is not null && now >= payment.AuthorizationExpirationTime.Value;

        if (isStale)
        {
            var originalCreateTime = payment.OriginalAuthorizationCreateTime ?? payment.AuthorizationCreateTime ?? now;
            if (now - originalCreateTime > MaxReauthorizationWindow)
            {
                throw new PayPalOperationException(
                    $"The authorization for order {orderId} expired more than {MaxReauthorizationWindow.Days} days after it was created and " +
                    "PayPal no longer allows it to be renewed. Cancel this order and collect a new payment from the shopper.");
            }

            var reauthRequestId = $"reauth-order-{orderId}-{payment.AuthorizationId}";
            PayPalReauthorizationResult reauth;
            try
            {
                reauth = await _payPal.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount, payment.CurrencyCode, reauthRequestId);
            }
            catch (PayPalOperationException ex)
            {
                throw new PayPalOperationException(
                    $"The authorization for order {orderId} had expired and PayPal rejected the attempt to renew it ({ex.Message}). " +
                    "Cancel this order and collect a new payment from the shopper.",
                    ex.PayPalErrorName, ex.PayPalDebugId, ex);
            }

            order.RecordReauthorization(reauth.AuthorizationId, reauth.Status, reauth.CreateTime ?? now, reauth.ExpirationTime);
            payment = order.Payment!;
        }

        var captureRequestId = $"capture-order-{orderId}-{payment.AuthorizationId}";
        var capture = await _payPal.CaptureAuthorizationAsync(payment.AuthorizationId!, payment.Amount, payment.CurrencyCode, captureRequestId);

        order.MarkFulfilled(capture.CaptureId, capture.Status, capture.GrossAmount, capture.FeeAmount, capture.NetAmount, captureRequestId, capture.CaptureTime);

        await _orderRepository.UpdateAsync(order);
        return order;
    }

    public async Task<Order> CancelOrderAsync(int orderId)
    {
        var order = await GetOrderWithPaymentAsync(orderId);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }

        if (order.Status != OrderStatus.AwaitingPayment && order.Status != OrderStatus.PaymentAuthorized)
        {
            throw new InvalidOrderStateException(orderId, order.Status, "cancel");
        }

        if (order.Status == OrderStatus.PaymentAuthorized && order.Payment?.AuthorizationId is not null)
        {
            await _payPal.VoidAuthorizationAsync(order.Payment.AuthorizationId);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order);
        return order;
    }

    public async Task<Refund> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await GetOwnedOrderAsync(buyerId, orderId);

        if (order.Payment is null || (order.Status != OrderStatus.Fulfilled && order.Status != OrderStatus.PartiallyRefunded))
        {
            throw new InvalidOrderStateException(orderId, order.Status, "refund");
        }

        var payment = order.Payment;
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            if (amount.HasValue && amount.Value != existing.Amount)
            {
                throw new IdempotencyConflictException(
                    $"Idempotency key '{idempotencyKey}' was already used for a refund of {existing.Amount} {existing.CurrencyCode}; " +
                    $"it cannot be reused for a different amount ({amount.Value}).");
            }
            return existing;
        }

        var refundAmount = amount ?? payment.RemainingRefundableAmount;
        if (refundAmount <= 0m || refundAmount > payment.RemainingRefundableAmount)
        {
            throw new RefundAmountExceededException(refundAmount, payment.RemainingRefundableAmount);
        }

        var payPalRequestId = $"refund-{payment.CaptureId}-{idempotencyKey}";
        var result = await _payPal.RefundCaptureAsync(payment.CaptureId!, refundAmount, payment.CurrencyCode, payPalRequestId);

        var refund = order.ApplyRefund(result.RefundId, result.Amount, result.Status, idempotencyKey, result.CreateTime);
        await _orderRepository.UpdateAsync(order);
        return refund;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var spec = new OrdersByBuyerWithPaymentSpecification(buyerId);
        var orders = await _orderRepository.ListAsync(spec);
        return orders;
    }

    private async Task<Order> GetOrderWithPaymentAsync(int orderId)
    {
        var spec = new OrderByIdWithPaymentSpecification(orderId);
        var order = await _orderRepository.FirstOrDefaultAsync(spec);
        return order ?? throw new OrderNotFoundException(orderId);
    }

    private async Task<Order> GetOwnedOrderAsync(string buyerId, int orderId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var order = await GetOrderWithPaymentAsync(orderId);
        if (order.BuyerId != buyerId)
        {
            throw new ForbiddenAccessException($"Order {orderId} does not belong to the caller.");
        }
        return order;
    }

    private string RequireCurrency()
    {
        if (string.IsNullOrWhiteSpace(_options.Currency))
        {
            throw new InvalidOperationException("PayPal:Currency is not configured.");
        }
        return _options.Currency;
    }
}
