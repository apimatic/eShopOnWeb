using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
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
    private static readonly TimeSpan AuthorizationHonorPeriod = TimeSpan.FromDays(3);
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderGates = new();

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPayPalPaymentsClient _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderCheckoutService> _logger;

    public OrderCheckoutService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPayPalPaymentsClient payPal,
        IUriComposer uriComposer,
        IAppLogger<OrderCheckoutService> logger)
    {
        _orderRepository = orderRepository;
        _catalogRepository = catalogRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(PlaceOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            throw new PaymentOperationException(401, "A signed-in shopper is required to place an order.");
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            throw new PaymentOperationException(400, "At least one catalog item is required.");
        }

        var quantities = new Dictionary<int, int>();
        foreach (var line in request.Items)
        {
            if (line.CatalogItemId <= 0 || line.Quantity <= 0)
            {
                throw new PaymentOperationException(400, "Each item must include a catalogItemId and a quantity greater than zero.");
            }

            quantities[line.CatalogItemId] = quantities.TryGetValue(line.CatalogItemId, out var existing)
                ? existing + line.Quantity
                : line.Quantity;
        }

        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(quantities.Keys.ToArray()));
        if (catalogItems.Count != quantities.Count)
        {
            var found = catalogItems.Select(c => c.Id).ToHashSet();
            var missing = quantities.Keys.Where(id => !found.Contains(id)).ToArray();
            throw new PaymentOperationException(400, $"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = catalogItems.Select(catalogItem =>
        {
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, quantities[catalogItem.Id]);
        }).ToList();

        var shipTo = request.ShipTo ?? new Address("123 Main Street", "Seattle", "WA", "USA", "98101");
        var order = new Order(request.BuyerId, shipTo, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> PayOrderAsync(PayOrderRequest request, CancellationToken cancellationToken = default)
    {
        var gate = OrderGates.GetOrAdd(request.OrderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await LoadOrderAsync(request.OrderId, cancellationToken);
            EnsureBuyer(order, request.BuyerId);

            if (order.Status is OrderStatus.Authorized or OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
            {
                return order;
            }

            if (order.Status == OrderStatus.Cancelled)
            {
                throw new PaymentOperationException(409, $"Order {order.Id} is cancelled and cannot be paid.");
            }

            var hasCard = request.Card is not null && !string.IsNullOrWhiteSpace(request.Card.Number);
            var hasSaved = request.PaymentMethodId.HasValue;
            if (hasCard == hasSaved)
            {
                throw new PaymentOperationException(400, "Provide either card details or paymentMethodId, not both or neither.");
            }

            var amount = order.Total();
            if (amount <= 0)
            {
                throw new PaymentOperationException(400, "Order total must be greater than zero.");
            }

            var items = BuildPayPalItems(order);
            var requestId = order.EnsurePayIdempotencyKey();
            await _orderRepository.UpdateAsync(order, cancellationToken);

            PayPalAuthorizationResult authorization;
            if (hasSaved)
            {
                var method = await _paymentMethodRepository.FirstOrDefaultAsync(
                    new SavedPaymentMethodByBuyerAndIdSpec(request.BuyerId, request.PaymentMethodId!.Value),
                    cancellationToken);
                if (method is null)
                {
                    throw new PaymentOperationException(404, "Saved card was not found, does not belong to this shopper, or has been removed.");
                }

                authorization = await _payPal.AuthorizeVaultedCardPaymentAsync(
                    order.Id, amount, items, method.PayPalPaymentTokenId, requestId, cancellationToken);
            }
            else
            {
                ValidateCard(request.Card!);
                authorization = await _payPal.AuthorizeCardPaymentAsync(
                    order.Id, amount, items, request.Card!, requestId, cancellationToken);
            }

            order.MarkAuthorized(
                authorization.PayPalOrderId,
                authorization.AuthorizationId,
                authorization.AuthorizationStatus,
                authorization.ExpirationTime,
                authorization.CreateTime,
                _payPal.Currency,
                authorization.InvoiceId);

            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation("Authorized order {OrderId} with PayPal authorization {AuthorizationId}.", order.Id, authorization.AuthorizationId);
            return order;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Order> FulfilOrderAsync(FulfilOrderRequest request, CancellationToken cancellationToken = default)
    {
        var gate = OrderGates.GetOrAdd(request.OrderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await LoadOrderAsync(request.OrderId, cancellationToken);

            if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
            {
                return order;
            }

            if (order.Status == OrderStatus.Cancelled)
            {
                throw new PaymentOperationException(409, $"Order {order.Id} is cancelled and cannot be fulfilled.");
            }

            if (order.Status != OrderStatus.Authorized || string.IsNullOrEmpty(order.PayPalAuthorizationId))
            {
                throw new PaymentOperationException(409, $"Order {order.Id} has not been paid yet. Capture happens only after a successful authorization.");
            }

            var authorizationId = await EnsureFreshAuthorizationAsync(order, cancellationToken);
            var captureRequestId = order.EnsureCaptureIdempotencyKey();
            await _orderRepository.UpdateAsync(order, cancellationToken);

            PayPalCaptureResult capture;
            try
            {
                capture = await _payPal.CaptureAuthorizationAsync(
                    authorizationId, order.Total(), captureRequestId, finalCapture: true, cancellationToken);
            }
            catch (PaymentOperationException ex) when (PayPalPaymentsClientStale(ex))
            {
                _logger.LogWarning("Capture of order {OrderId} hit a stale authorization; renewing hold.", order.Id);
                authorizationId = await RenewAuthorizationAsync(order, cancellationToken);
                capture = await _payPal.CaptureAuthorizationAsync(
                    authorizationId, order.Total(), captureRequestId + "-retry", finalCapture: true, cancellationToken);
            }

            order.MarkFulfilled(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PayPalFee, capture.NetAmount);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation("Fulfilled order {OrderId} with PayPal capture {CaptureId}.", order.Id, capture.CaptureId);
            return order;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Order> CancelOrderAsync(CancelOrderRequest request, CancellationToken cancellationToken = default)
    {
        var gate = OrderGates.GetOrAdd(request.OrderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await LoadOrderAsync(request.OrderId, cancellationToken);

            if (order.Status == OrderStatus.Cancelled)
            {
                return order;
            }

            if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
            {
                throw new PaymentOperationException(409,
                    $"Order {order.Id} has already been fulfilled. Cancel is only available before capture; use a refund to return captured funds.");
            }

            if (!string.IsNullOrEmpty(order.PayPalAuthorizationId) && order.Status == OrderStatus.Authorized)
            {
                var requestId = order.EnsureCancelIdempotencyKey();
                await _orderRepository.UpdateAsync(order, cancellationToken);
                await _payPal.VoidAuthorizationAsync(order.PayPalAuthorizationId, requestId, cancellationToken);
            }

            order.MarkCancelled();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation("Cancelled order {OrderId}; any PayPal hold was released.", order.Id);
            return order;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<(Order Order, OrderRefund Refund)> RefundOrderAsync(RefundOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new PaymentOperationException(400, "A caller-supplied idempotencyKey is required for refunds.");
        }

        var gate = OrderGates.GetOrAdd(request.OrderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await LoadOrderAsync(request.OrderId, cancellationToken);
            EnsureBuyer(order, request.BuyerId);

            var existing = order.FindRefundByIdempotencyKey(request.IdempotencyKey);
            if (existing is not null)
            {
                return (order, existing);
            }

            if (order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded))
            {
                throw new PaymentOperationException(409,
                    $"Order {order.Id} cannot be refunded from status {order.Status}. Refunds are available after fulfilment.");
            }

            if (string.IsNullOrEmpty(order.PayPalCaptureId))
            {
                throw new PaymentOperationException(409, $"Order {order.Id} has no captured PayPal payment to refund.");
            }

            var remaining = order.RemainingRefundable();
            var amount = request.Amount ?? remaining;
            if (amount <= 0)
            {
                throw new PaymentOperationException(409, $"Order {order.Id} has no remaining refundable amount.");
            }

            if (amount - remaining > 0.0001m)
            {
                throw new PaymentOperationException(409,
                    $"Refund of {amount} exceeds the remaining refundable amount {remaining} (captured {order.CapturedAmount}).");
            }

            var currency = order.Currency ?? _payPal.Currency;
            var paypalRequestId = $"eshop-{order.Id}-{order.PayPalCaptureId}-{request.IdempotencyKey}";
            var paypalRefund = await _payPal.RefundCaptureAsync(
                order.PayPalCaptureId,
                amount,
                currency,
                paypalRequestId,
                cancellationToken);

            var refundAmount = paypalRefund.Amount > 0 ? paypalRefund.Amount : amount;
            var refund = order.AddRefund(paypalRefund.RefundId, paypalRefund.Status, refundAmount, paypalRefund.Currency, request.IdempotencyKey);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation("Refunded {Amount} on order {OrderId} (PayPal refund {RefundId}).", refundAmount, order.Id, paypalRefund.RefundId);
            return (order, refund);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        return orders;
    }

    public async Task<Order?> GetBuyerOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            return null;
        }

        return order;
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new PaymentOperationException(404, $"Order {orderId} was not found.");
        }

        return order;
    }

    private static void EnsureBuyer(Order order, string buyerId)
    {
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentOperationException(404, $"Order {order.Id} was not found.");
        }
    }

    private async Task<string> EnsureFreshAuthorizationAsync(Order order, CancellationToken cancellationToken)
    {
        var authorizationId = order.PayPalAuthorizationId!;
        var now = DateTimeOffset.UtcNow;
        var honorExpired = order.PayPalAuthorizationCreatedAt.HasValue &&
                           now > order.PayPalAuthorizationCreatedAt.Value.Add(AuthorizationHonorPeriod);
        var holdExpired = order.PayPalAuthorizationExpiration.HasValue &&
                          now >= order.PayPalAuthorizationExpiration.Value.AddMinutes(-5);

        PayPalAuthorizationResult? live = null;
        try
        {
            live = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (PaymentOperationException ex)
        {
            _logger.LogWarning("Could not refresh PayPal authorization {AuthorizationId} for order {OrderId}: {Message}",
                authorizationId, order.Id, ex.Message);
        }

        if (live is not null)
        {
            order.UpdateAuthorization(live.AuthorizationId, live.AuthorizationStatus, live.ExpirationTime);
            if (string.Equals(live.AuthorizationStatus, "VOIDED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(live.AuthorizationStatus, "DENIED", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentOperationException(409,
                    $"PayPal reports authorization {live.AuthorizationId} as {live.AuthorizationStatus}. Ask the shopper to pay the order again.");
            }

            holdExpired = live.ExpirationTime.HasValue && now >= live.ExpirationTime.Value.AddMinutes(-5);
            honorExpired = live.CreateTime.HasValue && now > live.CreateTime.Value.Add(AuthorizationHonorPeriod);
            authorizationId = live.AuthorizationId;
        }

        if (honorExpired || holdExpired)
        {
            authorizationId = await RenewAuthorizationAsync(order, cancellationToken);
        }

        return authorizationId;
    }

    private async Task<string> RenewAuthorizationAsync(Order order, CancellationToken cancellationToken)
    {
        var originalId = order.PayPalAuthorizationId
                         ?? throw new PaymentOperationException(409, $"Order {order.Id} has no PayPal authorization to renew.");
        var renewed = await _payPal.ReauthorizeAsync(
            originalId,
            order.Total(),
            $"reauth-{order.Id}-{Guid.NewGuid():N}",
            cancellationToken);

        order.UpdateAuthorization(renewed.AuthorizationId, renewed.AuthorizationStatus, renewed.ExpirationTime);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Renewed PayPal authorization for order {OrderId}: {OldId} -> {NewId}.",
            order.Id, originalId, renewed.AuthorizationId);
        return renewed.AuthorizationId;
    }

    private IReadOnlyList<PayPalPurchaseItem> BuildPayPalItems(Order order)
    {
        var currency = _payPal.Currency;
        return order.OrderItems.Select(item => new PayPalPurchaseItem
        {
            Name = item.ItemOrdered.ProductName,
            Quantity = item.Units.ToString(CultureInfo.InvariantCulture),
            UnitAmount = new PayPalMoney { CurrencyCode = currency, Value = _payPal.FormatMoney(item.UnitPrice) },
            Sku = item.ItemOrdered.CatalogItemId.ToString(CultureInfo.InvariantCulture)
        }).ToList();
    }

    private static void ValidateCard(CardPaymentDetails card)
    {
        if (string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry))
        {
            throw new PaymentOperationException(400, "Card number and expiry (YYYY-MM) are required.");
        }

        if (card.BillingAddress is not null && string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode))
        {
            throw new PaymentOperationException(400, "Billing address countryCode is required when a billing address is supplied.");
        }
    }

    private static bool PayPalPaymentsClientStale(PaymentOperationException ex)
        => ex.Message.Contains("AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase)
           || ex.Message.Contains("EXPIRED_AUTHORIZATION", StringComparison.OrdinalIgnoreCase)
           || ex.Message.Contains("AUTHORIZATION_VOIDED", StringComparison.OrdinalIgnoreCase);
}
