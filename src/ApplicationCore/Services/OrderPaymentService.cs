using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IUriComposer _uriComposer;
    private readonly IOptions<PayPalSettings> _payPalSettings;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<Buyer> buyerRepository,
        IRepository<CatalogItem> itemRepository,
        IPaymentGateway paymentGateway,
        IUriComposer uriComposer,
        IOptions<PayPalSettings> payPalSettings)
    {
        _orderRepository = orderRepository;
        _buyerRepository = buyerRepository;
        _itemRepository = itemRepository;
        _paymentGateway = paymentGateway;
        _uriComposer = uriComposer;
        _payPalSettings = payPalSettings;
    }

    private string Currency => _payPalSettings.Value.Currency ?? "USD";

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderItemQuantity> items, Address shipToAddress)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items is null || items.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one item.", nameof(items));
        }

        var catalogItemIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds));

        var orderItems = new List<OrderItem>();
        foreach (var line in items)
        {
            if (line.Quantity <= 0)
            {
                throw new ArgumentException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }

            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
            if (catalogItem is null)
            {
                throw new ArgumentException($"Catalog item {line.CatalogItemId} does not exist.");
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, orderItems);
        return await _orderRepository.AddAsync(order);
    }

    public async Task<Order?> AuthorizePaymentAsync(int orderId, string buyerId, CardDetails? card, int? savedPaymentMethodId)
    {
        if ((card is null) == (savedPaymentMethodId is null))
        {
            throw new ArgumentException("Provide exactly one of card details or a saved payment method id.");
        }

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId));
        if (order is null || order.BuyerId != buyerId)
        {
            return null;
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            // Idempotent: a repeat/double-click on an order past this step just returns the current state
            // rather than authorizing (or erroring) again.
            return order;
        }

        var payment = order.BeginPayment(Currency);
        var requestId = $"authorize-{order.Id}";

        PaymentAuthorizationResult authResult;
        int? usedPaymentMethodId = null;

        if (card is not null)
        {
            authResult = await _paymentGateway.AuthorizeWithCardAsync(requestId, payment.AuthorizedAmount, Currency, card);
        }
        else
        {
            var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId));
            var method = buyer?.PaymentMethods.FirstOrDefault(m => m.Id == savedPaymentMethodId);
            if (method is null)
            {
                throw new ArgumentException($"Saved payment method {savedPaymentMethodId} was not found for this buyer.");
            }

            authResult = await _paymentGateway.AuthorizeWithVaultedCardAsync(requestId, payment.AuthorizedAmount, Currency, method.VaultId);
            usedPaymentMethodId = method.Id;
        }

        if (authResult.RequiresBuyerAction)
        {
            throw new PaymentGatewayException(
                "PayPal requires additional buyer approval (redirect/3-D Secure) for this payment, which this integration does not support.",
                errorCode: "PAYER_ACTION_REQUIRED");
        }

        payment.RecordAuthorization(authResult.PayPalOrderId, authResult.AuthorizationId, authResult.Status, authResult.ExpiresAt, usedPaymentMethodId);
        order.MarkPaymentAuthorized();

        await _orderRepository.UpdateAsync(order);
        return order;
    }

    public async Task<Order?> FulfilOrderAsync(int orderId)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId));
        if (order is null)
        {
            return null;
        }

        if (order.Status == OrderStatus.Fulfilled)
        {
            return order; // idempotent
        }

        if (order.Status != OrderStatus.PaymentAuthorized || order.Payment?.AuthorizationId is null)
        {
            throw new OrderPaymentStateException($"Order {orderId} cannot be fulfilled from status {order.Status}; it must have an authorized payment.");
        }

        var payment = order.Payment;

        if (payment.AuthorizationExpiresAt.HasValue && payment.AuthorizationExpiresAt.Value <= DateTimeOffset.UtcNow)
        {
            try
            {
                var reauth = await _paymentGateway.ReauthorizePaymentAsync($"reauth-{order.Id}", payment.AuthorizationId, payment.AuthorizedAmount, payment.CurrencyCode);
                payment.RecordReauthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
            }
            catch (PaymentGatewayException ex) when (!ex.IsRetryable)
            {
                throw new OrderPaymentStateException(
                    $"The payment authorization for order {orderId} has expired and PayPal will not renew it" +
                    (ex.ErrorCode is not null ? $" ({ex.ErrorCode})" : string.Empty) +
                    $": {ex.Message}. The shopper must pay again to fulfil this order.", ex);
            }
        }

        var capture = await _paymentGateway.CapturePaymentAsync($"capture-{order.Id}", payment.AuthorizationId);
        payment.RecordCapture(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PayPalFeeAmount, capture.NetAmount);
        order.MarkFulfilled();

        await _orderRepository.UpdateAsync(order);
        return order;
    }

    public async Task<Order?> CancelOrderAsync(int orderId)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId));
        if (order is null)
        {
            return null;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            return order; // idempotent
        }

        if (order.Payment?.AuthorizationId is not null && order.Payment.Status == OrderPaymentStatus.Authorized)
        {
            await _paymentGateway.VoidPaymentAsync($"void-{order.Id}", order.Payment.AuthorizationId);
            order.Payment.RecordVoid();
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order);
        return order;
    }

    public async Task<(Order Order, OrderRefund Refund)?> RefundOrderAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId));
        if (order is null || order.BuyerId != buyerId)
        {
            return null;
        }

        var payment = order.Payment;
        if (payment?.CaptureId is null || payment.CapturedAmount is null)
        {
            throw new OrderPaymentStateException($"Order {orderId} cannot be refunded before it has been fulfilled.");
        }

        var existing = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing is not null)
        {
            return (order, existing); // idempotent replay of a previous refund request
        }

        var remaining = payment.CapturedAmount.Value - payment.TotalRefundedAmount;
        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0 || refundAmount > remaining)
        {
            throw new OrderPaymentStateException($"Refund amount {refundAmount} is invalid; at most {remaining} of order {orderId} remains refundable.");
        }

        var requestId = $"refund-{order.Id}-{payment.CaptureId}-{idempotencyKey}";
        var result = await _paymentGateway.RefundCaptureAsync(requestId, payment.CaptureId, amount, payment.CurrencyCode);

        var refund = payment.AddRefund(result.RefundId, result.Amount, result.Status, idempotencyKey);
        order.MarkRefunded(payment.Status == OrderPaymentStatus.Refunded);

        await _orderRepository.UpdateAsync(order);
        return (order, refund);
    }

    public async Task<Order?> GetOrderForBuyerAsync(int orderId, string buyerId)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId));
        return order is not null && order.BuyerId == buyerId ? order : null;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId));
    }
}
