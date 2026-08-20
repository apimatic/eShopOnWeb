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
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class CheckoutService : ICheckoutService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly ISavedPaymentMethodService _paymentMethods;
    private readonly IPayPalPaymentsClient _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<CheckoutService> _logger;

    public CheckoutService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        ISavedPaymentMethodService paymentMethods,
        IPayPalPaymentsClient payPal,
        IUriComposer uriComposer,
        IAppLogger<CheckoutService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethods = paymentMethods;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public string Currency => _payPal.Currency;

    public async Task<Order> PlaceOrderAsync(string buyerId, PlaceOrderRequest request, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(request, nameof(request));

        if (request.Items is null || request.Items.Count == 0)
        {
            throw new OrderPaymentException("An order must contain at least one catalog item.", 400);
        }

        var grouped = request.Items
            .GroupBy(i => i.CatalogItemId)
            .Select(g => new PlaceOrderItem(g.Key, g.Sum(x => x.Quantity)))
            .ToList();

        foreach (var item in grouped)
        {
            if (item.Quantity <= 0)
            {
                throw new OrderPaymentException($"Quantity for catalog item {item.CatalogItemId} must be greater than zero.", 400);
            }
        }

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(grouped.Select(i => i.CatalogItemId).ToArray()),
            cancellationToken);

        var missing = grouped.Select(i => i.CatalogItemId).Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
        {
            throw new EntityNotFoundException($"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var orderItems = grouped.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = request.ShipTo ?? new Address("2211 N First Street", "San Jose", "CA", "US", "95131");
        var order = new Order(buyerId, address, orderItems, OrderLifecycleStatus.AwaitingPayment);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> PayOrderAsync(string buyerId, int orderId, PayOrderRequest request, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderForBuyerAsync(buyerId, orderId, cancellationToken);

        if (order.Status == OrderLifecycleStatus.Authorized && order.HasActiveAuthorization)
        {
            _logger.LogInformation("Pay is idempotent for order {OrderId}; authorization already held.", orderId);
            return order;
        }

        order.EnsureCanPay();

        if (request.Card is not null && request.PaymentMethodId is not null)
        {
            throw new OrderPaymentException("Provide either card details or a saved payment method, not both.", 400);
        }

        var amount = order.Total();
        if (amount <= 0)
        {
            throw new OrderPaymentException("An order total of zero cannot be authorized.", 400);
        }

        var invoiceId = ReconciliationService.InvoiceIdFor(order);
        PayPalAuthorizationResult authorization;
        if (request.PaymentMethodId is not null)
        {
            var saved = await _paymentMethods.GetOwnedAsync(buyerId, request.PaymentMethodId.Value, cancellationToken);
            authorization = await _payPal.AuthorizeVaultedCardAsync(order.Id, amount, saved.PayPalPaymentTokenId, invoiceId, cancellationToken);
        }
        else if (request.Card is not null)
        {
            authorization = await _payPal.AuthorizeCardAsync(order.Id, amount, request.Card, invoiceId, cancellationToken);
        }
        else
        {
            throw new OrderPaymentException("Provide card details or a saved payment method id to pay.", 400);
        }

        order.RecordAuthorization(
            authorization.PayPalOrderId,
            authorization.AuthorizationId,
            authorization.Status,
            Currency,
            authorization.CreateTime,
            authorization.ExpirationTime,
            invoiceId);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        order.EnsureCanFulfil();

        if (order.Status is OrderLifecycleStatus.Fulfilled or OrderLifecycleStatus.PartiallyRefunded or OrderLifecycleStatus.Refunded)
        {
            _logger.LogInformation("Fulfil is idempotent for order {OrderId}; capture already recorded.", orderId);
            return order;
        }

        if (order.Payment?.AuthorizationId is null)
        {
            throw new OrderPaymentException("This order has no PayPal authorization to capture.");
        }

        var authorizationId = await EnsureFreshAuthorizationAsync(order, cancellationToken);
        var capture = await CaptureWithRenewalAsync(order, authorizationId, cancellationToken);

        order.RecordCapture(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PaypalFee, capture.NetAmount);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        order.EnsureCanCancel();

        if (order.Status == OrderLifecycleStatus.Cancelled)
        {
            return order;
        }

        if (order.HasActiveAuthorization && order.Payment?.AuthorizationId is not null)
        {
            var voidId = order.Payment.OriginalAuthorizationId ?? order.Payment.AuthorizationId;
            try
            {
                await _payPal.VoidAuthorizationAsync(voidId, IdempotencyKey("void", order), cancellationToken);
            }
            catch (PayPalClientException ex) when (ex.IsAlreadyVoided)
            {
                _logger.LogInformation("PayPal authorization for order {OrderId} was already released.", orderId);
            }
            catch (PayPalClientException ex) when (ex.MustVoidOriginalAuthorization && order.Payment.OriginalAuthorizationId is not null
                                                   && !string.Equals(voidId, order.Payment.OriginalAuthorizationId, StringComparison.Ordinal))
            {
                await _payPal.VoidAuthorizationAsync(order.Payment.OriginalAuthorizationId, IdempotencyKey("void-original", order), cancellationToken);
            }

            order.RecordVoid("VOIDED");
        }
        else
        {
            order.CancelWithoutPayment();
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<(Order Order, OrderRefund Refund)> RefundOrderAsync(
        string buyerId,
        int orderId,
        RefundOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(request.IdempotencyKey, nameof(request.IdempotencyKey));

        var order = await GetOrderForBuyerAsync(buyerId, orderId, cancellationToken);

        if (order.Payment is not null)
        {
            var existing = order.Payment.FindRefundByIdempotencyKey(request.IdempotencyKey);
            if (existing is not null)
            {
                return (order, existing);
            }
        }

        if (order.Payment?.CaptureId is null)
        {
            throw new OrderPaymentException("This order has no captured payment to refund.");
        }

        var amount = request.Amount ?? order.Payment.RefundableRemaining;
        var remainingBefore = order.Payment.RefundableRemaining;
        if (amount <= 0)
        {
            throw new OrderPaymentException("There is no remaining captured amount to refund.", 400);
        }

        if (amount > remainingBefore)
        {
            throw new OrderPaymentException(
                $"Refund of {amount:0.00} exceeds the remaining captured amount of {remainingBefore:0.00}.", 400);
        }

        var paypalRefund = await _payPal.RefundCaptureAsync(
            order.Payment.CaptureId,
            amount,
            IdempotencyKey($"refund-{request.IdempotencyKey}", order),
            cancellationToken);

        var refund = order.RecordRefund(paypalRefund.RefundId, paypalRefund.Status, paypalRefund.Amount, request.IdempotencyKey);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return (order, refund);
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public async Task<Order> GetOrderForBuyerAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        order.EnsureOwnedBy(buyerId);
        return order;
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new EntityNotFoundException($"Order {orderId} was not found.");
        }

        return order;
    }

    private async Task<string> EnsureFreshAuthorizationAsync(Order order, CancellationToken cancellationToken)
    {
        var payment = order.Payment!;
        var authorizationId = payment.AuthorizationId!;
        var now = DateTimeOffset.UtcNow;

        PayPalAuthorizationDetails details;
        try
        {
            details = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (PayPalClientException ex) when (ex.IsExpiredAuthorization)
        {
            return await RenewAuthorizationAsync(order, now, cancellationToken);
        }

        if (string.Equals(details.Status, "CAPTURED", StringComparison.OrdinalIgnoreCase))
        {
            return authorizationId;
        }

        if (string.Equals(details.Status, "VOIDED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(details.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            throw new OrderPaymentException(
                $"PayPal authorization {authorizationId} is {details.Status}. Ask the shopper to pay again before fulfilling.");
        }

        if (string.Equals(details.Status, "EXPIRED", StringComparison.OrdinalIgnoreCase)
            || order.AuthorizationHonorPeriodElapsed(now))
        {
            return await RenewAuthorizationAsync(order, now, cancellationToken);
        }

        return authorizationId;
    }

    private async Task<string> RenewAuthorizationAsync(Order order, DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        if (order.AuthorizationCanNoLongerBeRenewed(utcNow))
        {
            throw new OrderPaymentException(
                "The PayPal authorization is older than 29 days and can no longer be renewed. " +
                "Ask the shopper to place and pay for a new order, then fulfil that order.");
        }

        var payment = order.Payment!;
        try
        {
            var renewed = await _payPal.ReauthorizeAsync(
                payment.AuthorizationId!,
                order.Total(),
                IdempotencyKey("reauth", order),
                cancellationToken);

            order.RecordReauthorization(
                renewed.AuthorizationId,
                renewed.Status,
                renewed.CreateTime,
                renewed.ExpirationTime);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation("Renewed PayPal authorization for order {OrderId} to {AuthorizationId}.", order.Id, renewed.AuthorizationId);
            return renewed.AuthorizationId;
        }
        catch (PayPalClientException ex) when (ex.IsExpiredAuthorization || ex.CannotReauthorize)
        {
            throw new OrderPaymentException(
                "PayPal could not renew the authorization. It has expired and can no longer be captured. " +
                "Ask the shopper to pay again, then retry fulfilment. " +
                $"PayPal issue: {ex.Issue ?? ex.Message}.");
        }
    }

    private async Task<PayPalCaptureResult> CaptureWithRenewalAsync(Order order, string authorizationId, CancellationToken cancellationToken)
    {
        try
        {
            return await _payPal.CaptureAuthorizationAsync(
                authorizationId,
                order.Total(),
                IdempotencyKey("capture", order),
                cancellationToken);
        }
        catch (PayPalClientException ex) when (ex.IsExpiredAuthorization)
        {
            var renewedId = await RenewAuthorizationAsync(order, DateTimeOffset.UtcNow, cancellationToken);
            return await _payPal.CaptureAuthorizationAsync(
                renewedId,
                order.Total(),
                IdempotencyKey("capture", order),
                cancellationToken);
        }
    }

    private static string IdempotencyKey(string operation, Order order) =>
        $"eshop-{operation}-{order.Id}-{order.OrderDate.ToUnixTimeMilliseconds()}";
}
