using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class CheckoutService : ICheckoutService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderGates = new();

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalGateway _payPal;
    private readonly IPaymentSettings _paymentSettings;
    private readonly IAppLogger<CheckoutService> _logger;

    public CheckoutService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IUriComposer uriComposer,
        IPayPalGateway payPal,
        IPaymentSettings paymentSettings,
        IAppLogger<CheckoutService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _uriComposer = uriComposer;
        _payPal = payPal;
        _paymentSettings = paymentSettings;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLine> lines,
        Address shippingAddress,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shippingAddress, nameof(shippingAddress));

        if (lines is null || lines.Count == 0)
        {
            throw new CheckoutException(400, "An order must contain at least one catalog item.", "EMPTY_ORDER");
        }

        var merged = lines
            .GroupBy(l => l.CatalogItemId)
            .Select(g => new OrderLine(g.Key, g.Sum(x => x.Quantity)))
            .ToList();

        foreach (var line in merged)
        {
            if (line.CatalogItemId <= 0)
            {
                throw new CheckoutException(400, "Catalog item id must be a positive integer.", "INVALID_CATALOG_ITEM");
            }

            if (line.Quantity <= 0)
            {
                throw new CheckoutException(400, $"Quantity for catalog item {line.CatalogItemId} must be greater than zero.", "INVALID_QUANTITY");
            }
        }

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(merged.Select(l => l.CatalogItemId).ToArray()),
            cancellationToken);

        var orderItems = new List<OrderItem>();
        foreach (var line in merged)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
            if (catalogItem is null)
            {
                throw new CheckoutException(400, $"Catalog item {line.CatalogItemId} was not found.", "CATALOG_ITEM_NOT_FOUND");
            }

            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shippingAddress, orderItems, _paymentSettings.Currency);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> PayAsync(
        int orderId,
        string buyerId,
        CardPaymentDetails? card,
        int? paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        EnsureExclusivePaymentSource(card, paymentMethodId);

        return await WithOrderLock(orderId, async () =>
        {
            var order = await GetRequiredOrder(orderId, cancellationToken);
            EnsureBuyer(order, buyerId);

            if (order.HasSuccessfulAuthorization())
            {
                return order;
            }

            if (order.Status == OrderStatus.Cancelled)
            {
                throw new CheckoutException(409, $"Order {orderId} has been cancelled and cannot be paid.", "INVALID_ORDER_STATE");
            }

            var amount = order.Total();
            var currency = order.Currency ?? _paymentSettings.Currency;
            var requestId = order.NextPayRequestId();
            var invoiceId = $"ESHOP-{order.Id}-{Guid.NewGuid():N}";
            await _orderRepository.UpdateAsync(order, cancellationToken);

            PayPalAuthorizationResult authorization;
            if (paymentMethodId.HasValue)
            {
                var saved = await _paymentMethodRepository.GetByIdAsync(paymentMethodId.Value, cancellationToken);
                if (saved is null || saved.BuyerId != buyerId)
                {
                    throw new CheckoutException(404, "Saved card was not found.", "PAYMENT_METHOD_NOT_FOUND");
                }

                authorization = await _payPal.AuthorizeVaultedCardAsync(
                    order.Id, amount, currency, saved.PayPalPaymentTokenId, requestId, invoiceId, cancellationToken);
            }
            else
            {
                authorization = await _payPal.AuthorizeCardAsync(
                    order.Id, amount, currency, card!, requestId, invoiceId, cancellationToken);
            }

            if (authorization.Amount != amount)
            {
                throw new CheckoutException(502,
                    $"PayPal authorized {authorization.Amount} {authorization.Currency} but the order total is {amount} {currency}.",
                    "AUTHORIZATION_AMOUNT_MISMATCH");
            }

            order.RecordAuthorization(
                authorization.PayPalOrderId,
                authorization.AuthorizationId,
                authorization.Status,
                authorization.ExpirationTime,
                authorization.Currency,
                invoiceId);

            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        });
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return await WithOrderLock(orderId, async () =>
        {
            var order = await GetRequiredOrder(orderId, cancellationToken);

            if (order.HasSuccessfulCapture())
            {
                return order;
            }

            if (order.Status != OrderStatus.Authorized || string.IsNullOrEmpty(order.PayPalAuthorizationId))
            {
                throw new CheckoutException(409,
                    $"Order {orderId} cannot be fulfilled until a payment hold is in place.",
                    "INVALID_ORDER_STATE");
            }

            var amount = order.Total();
            var currency = order.Currency ?? _paymentSettings.Currency;
            var authorizationId = await EnsureFreshAuthorization(order, amount, currency, cancellationToken);

            var captureInvoiceId = $"ESHOP-{order.Id}-CAP-{Guid.NewGuid():N}";
            PayPalCaptureResult capture;
            try
            {
                capture = await _payPal.CaptureAuthorizationAsync(
                    authorizationId,
                    amount,
                    currency,
                    captureInvoiceId,
                    $"eshop-order-{order.Id}-capture-{Guid.NewGuid():N}",
                    cancellationToken);
            }
            catch (CheckoutException ex) when (ex.Code is "AUTHORIZATION_EXPIRED" or "AUTHORIZATION_DENIED")
            {
                authorizationId = await RenewAuthorization(order, amount, currency, cancellationToken);
                capture = await _payPal.CaptureAuthorizationAsync(
                    authorizationId,
                    amount,
                    currency,
                    captureInvoiceId,
                    $"eshop-order-{order.Id}-capture-retry-{Guid.NewGuid():N}",
                    cancellationToken);
            }

            order.RecordCapture(
                capture.CaptureId,
                capture.Status,
                capture.CapturedAmount,
                capture.PaypalFee,
                capture.NetAmount);

            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        });
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return await WithOrderLock(orderId, async () =>
        {
            var order = await GetRequiredOrder(orderId, cancellationToken);

            if (order.Status == OrderStatus.Cancelled)
            {
                return order;
            }

            if (!string.IsNullOrEmpty(order.PayPalAuthorizationId) && order.Status == OrderStatus.Authorized)
            {
                await _payPal.VoidAuthorizationAsync(
                    order.PayPalAuthorizationId,
                    $"eshop-order-{order.Id}-void-{Guid.NewGuid():N}",
                    cancellationToken);
            }

            order.Cancel();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        });
    }

    public async Task<PaymentRefund> RefundAsync(
        int orderId,
        string buyerId,
        string idempotencyKey,
        decimal? amount,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        return await WithOrderLock(orderId, async () =>
        {
            var order = await GetRequiredOrder(orderId, cancellationToken);
            EnsureBuyer(order, buyerId);

            var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
            if (existing is not null)
            {
                return existing;
            }

            if (!order.HasSuccessfulCapture() || string.IsNullOrEmpty(order.PayPalCaptureId))
            {
                throw new CheckoutException(409, $"Order {orderId} has no captured payment to refund.", "INVALID_ORDER_STATE");
            }

            var refundAmount = amount.HasValue
                ? decimal.Round(amount.Value, 2, MidpointRounding.AwayFromZero)
                : order.RemainingRefundable();

            if (refundAmount > order.RemainingRefundable())
            {
                throw new CheckoutException(409,
                    $"Refund of {refundAmount} exceeds the remaining captured amount of {order.RemainingRefundable()}.",
                    "REFUND_EXCEEDS_CAPTURE");
            }

            var currency = order.Currency ?? _paymentSettings.Currency;
            var paypalRefund = await _payPal.RefundCaptureAsync(
                order.PayPalCaptureId,
                refundAmount,
                currency,
                idempotencyKey,
                cancellationToken);

            var refund = order.RecordRefund(
                paypalRefund.RefundId,
                paypalRefund.Status,
                paypalRefund.Amount,
                idempotencyKey);

            await _orderRepository.UpdateAsync(order, cancellationToken);
            return refund;
        });
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var orders = await _orderRepository.ListAsync(new CustomerOrdersSpecification(buyerId), cancellationToken);
        return orders;
    }

    public async Task<Order> GetOrderForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var order = await GetRequiredOrder(orderId, cancellationToken);
        EnsureBuyer(order, buyerId);
        return order;
    }

    private async Task<string> EnsureFreshAuthorization(
        Order order,
        decimal amount,
        string currency,
        CancellationToken cancellationToken)
    {
        var authorizationId = order.PayPalAuthorizationId!;

        if (!order.IsAuthorizationStale(DateTimeOffset.UtcNow))
        {
            return authorizationId;
        }

        return await RenewAuthorization(order, amount, currency, cancellationToken);
    }

    private async Task<string> RenewAuthorization(
        Order order,
        decimal amount,
        string currency,
        CancellationToken cancellationToken)
    {
        var authorizationId = order.PayPalAuthorizationId!;
        _logger.LogInformation("Renewing stale PayPal authorization {AuthorizationId} for order {OrderId}.", authorizationId, order.Id);

        try
        {
            var renewed = await _payPal.ReauthorizeAsync(
                authorizationId,
                amount,
                currency,
                $"eshop-order-{order.Id}-reauth-{Guid.NewGuid():N}",
                cancellationToken);

            order.ReplaceAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpirationTime);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return renewed.AuthorizationId;
        }
        catch (CheckoutException ex) when (ex.Code is "AUTHORIZATION_CANNOT_BE_RENEWED" or "AUTHORIZATION_EXPIRED")
        {
            throw new CheckoutException(409,
                "The payment hold has expired and PayPal will not renew it. Ask the shopper to place and pay a new order, then fulfil that authorization.",
                "AUTHORIZATION_CANNOT_BE_RENEWED");
        }
        catch (CheckoutException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to renew PayPal authorization {AuthorizationId} for order {OrderId}: {Message}", authorizationId, order.Id, ex.Message);
            throw new CheckoutException(409,
                "The payment hold is stale and could not be renewed. Ask the shopper to pay again, then retry fulfilment.",
                "AUTHORIZATION_CANNOT_BE_RENEWED");
        }
    }

    private async Task<Order> GetRequiredOrder(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new CheckoutException(404, $"Order {orderId} was not found.", "ORDER_NOT_FOUND");
        }

        return order;
    }

    private static void EnsureBuyer(Order order, string buyerId)
    {
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new CheckoutException(404, $"Order {order.Id} was not found.", "ORDER_NOT_FOUND");
        }
    }

    private static void EnsureExclusivePaymentSource(CardPaymentDetails? card, int? paymentMethodId)
    {
        if (card is not null && paymentMethodId.HasValue)
        {
            throw new CheckoutException(400, "Provide either card details or a saved payment method, not both.", "INVALID_PAYMENT_SOURCE");
        }

        if (card is null && !paymentMethodId.HasValue)
        {
            throw new CheckoutException(400, "Provide card details or a saved payment method id.", "INVALID_PAYMENT_SOURCE");
        }
    }

    private static async Task<T> WithOrderLock<T>(int orderId, Func<Task<T>> action)
    {
        var gate = OrderGates.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            return await action();
        }
        finally
        {
            gate.Release();
        }
    }
}
