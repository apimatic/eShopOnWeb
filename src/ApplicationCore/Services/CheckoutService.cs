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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class CheckoutService : ICheckoutService
{
    private static readonly TimeSpan AuthorizationHonorPeriod = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalGateway _payPal;
    private readonly IAppLogger<CheckoutService> _logger;
    private readonly string _currency;

    public CheckoutService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IUriComposer uriComposer,
        IPayPalGateway payPal,
        IAppLogger<CheckoutService> logger,
        Microsoft.eShopWeb.ApplicationCore.Payments.IPaymentSettings paymentSettings)
    {
        _orderRepository = orderRepository;
        _catalogItemRepository = catalogItemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _uriComposer = uriComposer;
        _payPal = payPal;
        _logger = logger;
        _currency = paymentSettings.Currency;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineRequest> items,
        Address? shipTo,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException("A signed-in shopper is required.", 401);
        }

        if (items is null || items.Count == 0)
        {
            throw new PaymentException("At least one catalog item is required.");
        }

        foreach (var line in items)
        {
            if (line.CatalogItemId <= 0 || line.Quantity <= 0)
            {
                throw new PaymentException("Each item must include a catalogItemId and a quantity greater than zero.");
            }
        }

        var catalogIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItemRepository.ListAsync(new CatalogItemsSpecification(catalogIds), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var missing = catalogIds.Where(id => !catalogById.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
        {
            throw new PaymentException($"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var orderItems = new List<OrderItem>();
        foreach (var line in items)
        {
            var catalogItem = catalogById[line.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var address = shipTo ?? new Address("123 Main St", "Seattle", "WA", "USA", "98101");
        var order = new Order(buyerId, address, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> PayAsync(
        string buyerId,
        int orderId,
        int? paymentMethodId,
        CardDetails? card,
        CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        EnsureBuyer(order, buyerId);

        if (order.Status is OrderStatus.Authorized or OrderStatus.Fulfilled
            or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            return order;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentException("A cancelled order cannot be paid.", 409);
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentException($"Order {order.Id} cannot be paid in its current state ({order.Status}).", 409);
        }

        if ((card is null) == (paymentMethodId is null))
        {
            throw new PaymentException("Provide either card details or a saved paymentMethodId, not both.");
        }

        string? vaultId = null;
        if (paymentMethodId.HasValue)
        {
            var saved = await _paymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdSpec(paymentMethodId.Value, buyerId), cancellationToken);
            if (saved is null)
            {
                throw new PaymentException("Saved payment method was not found.", 404);
            }

            vaultId = saved.PayPalPaymentTokenId;
        }
        else
        {
            ValidateCard(card!);
        }

        var amount = MoneyFormat.ToCents(order.Total());
        if (amount <= 0m)
        {
            throw new PaymentException("The order total must be greater than zero.");
        }

        var invoiceId = $"eshop-{order.Id}-{order.OrderDate.UtcTicks}";
        var authorization = await _payPal.AuthorizePaymentAsync(
            amount,
            _currency,
            invoiceId,
            card,
            vaultId,
            requestId: $"eshop-pay-{order.Id}-{order.OrderDate.UtcTicks}",
            cancellationToken);

        if (!string.Equals(MoneyFormat.ToPayPalValue(amount), MoneyFormat.ToPayPalValue(authorization.Amount), StringComparison.Ordinal)
            && authorization.Amount != 0m
            && authorization.Amount != amount)
        {
            _logger.LogWarning(
                "PayPal authorized {Authorized} {Currency} for order {OrderId} whose total is {Total}.",
                authorization.Amount, authorization.Currency, order.Id, amount);
        }

        order.RecordAuthorization(
            authorization.PayPalOrderId,
            authorization.AuthorizationId,
            authorization.Status,
            authorization.ExpirationTime,
            amount,
            string.IsNullOrWhiteSpace(authorization.Currency) ? _currency : authorization.Currency);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            return order;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentException("A cancelled order cannot be fulfilled.", 409);
        }

        if (order.Status != OrderStatus.Authorized || order.Payment is null)
        {
            throw new PaymentException("An order must be authorized before it can be fulfilled.", 409);
        }

        var amount = MoneyFormat.ToCents(order.Payment.AuthorizedAmount);
        var invoiceId = $"eshop-{order.Id}-{order.OrderDate.UtcTicks}";
        var authorizationId = await EnsureFreshAuthorizationAsync(order, amount, cancellationToken);

        PayPalCaptureResult capture;
        try
        {
            capture = await _payPal.CaptureAuthorizationAsync(
                authorizationId,
                amount,
                order.Payment.Currency,
                invoiceId,
                requestId: $"eshop-capture-{order.Id}-{order.OrderDate.UtcTicks}",
                cancellationToken);
        }
        catch (PaymentException ex) when (IsStaleAuthorization(ex))
        {
            authorizationId = await RenewAuthorizationAsync(order, amount, cancellationToken);
            capture = await _payPal.CaptureAuthorizationAsync(
                authorizationId,
                amount,
                order.Payment.Currency,
                invoiceId,
                requestId: $"eshop-capture-{order.Id}-{order.OrderDate.UtcTicks}-renewed",
                cancellationToken);
        }

        order.RecordCapture(
            capture.CaptureId,
            capture.Status,
            capture.CapturedAmount,
            capture.PayPalFee,
            capture.NetProceeds);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            throw new PaymentException("A fulfilled order cannot be cancelled. Issue a refund instead.", 409);
        }

        if (order.Status == OrderStatus.Authorized && order.Payment is not null)
        {
            await _payPal.VoidAuthorizationAsync(
                order.Payment.AuthorizationId,
                requestId: $"eshop-void-{order.Id}-{order.OrderDate.UtcTicks}",
                cancellationToken);
            order.RecordVoid("VOIDED");
        }
        else
        {
            order.CancelWithoutPayment();
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<(Order Order, OrderRefund Refund)> RefundAsync(
        string buyerId,
        int orderId,
        string idempotencyKey,
        decimal? amount,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentException("A refund idempotencyKey is required.");
        }

        var order = await GetOrderAsync(orderId, cancellationToken);
        EnsureBuyer(order, buyerId);

        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return (order, existing);
        }

        if (order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded))
        {
            throw new PaymentException("Only a fulfilled order can be refunded.", 409);
        }

        if (order.Payment?.CaptureId is null)
        {
            throw new PaymentException("This order has no captured payment to refund.", 409);
        }

        var remaining = order.RemainingRefundable();
        var refundAmount = amount.HasValue ? MoneyFormat.ToCents(amount.Value) : remaining;

        if (refundAmount <= 0m)
        {
            throw new PaymentException("The refund amount must be greater than zero.");
        }

        if (refundAmount > remaining)
        {
            throw new PaymentException(
                $"Refund of {MoneyFormat.ToPayPalValue(refundAmount)} exceeds the remaining captured amount of {MoneyFormat.ToPayPalValue(remaining)}.");
        }

        var result = await _payPal.RefundCaptureAsync(
            order.Payment.CaptureId,
            refundAmount,
            order.Payment.Currency,
            requestId: $"eshop-refund-{order.Id}-{idempotencyKey}",
            cancellationToken);

        var refund = order.RecordRefund(
            result.RefundId,
            result.Status,
            result.Amount == 0m ? refundAmount : result.Amount,
            string.IsNullOrWhiteSpace(result.Currency) ? order.Payment.Currency : result.Currency,
            idempotencyKey);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return (order, refund);
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        return orders;
    }

    private async Task<string> EnsureFreshAuthorizationAsync(Order order, decimal amount, CancellationToken cancellationToken)
    {
        var payment = order.Payment!;
        PayPalAuthorizationDetails details;
        try
        {
            details = await _payPal.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
            order.UpdateAuthorization(details.AuthorizationId, details.Status, details.ExpirationTime);
        }
        catch (PaymentException)
        {
            details = new PayPalAuthorizationDetails(
                payment.AuthorizationId,
                payment.AuthorizationStatus,
                payment.AuthorizationExpiration,
                null,
                payment.AuthorizedAmount,
                payment.Currency);
        }

        var stale = IsAuthorizationStale(details);
        if (!stale && !IsAuthorizationExpired(details.Status))
        {
            return details.AuthorizationId;
        }

        return await RenewAuthorizationAsync(order, amount, cancellationToken);
    }

    private async Task<string> RenewAuthorizationAsync(Order order, decimal amount, CancellationToken cancellationToken)
    {
        try
        {
            var renewed = await _payPal.ReauthorizeAsync(
                order.Payment!.AuthorizationId,
                amount,
                order.Payment.Currency,
                requestId: $"eshop-reauth-{order.Id}-{order.OrderDate.UtcTicks}",
                cancellationToken);

            order.UpdateAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpirationTime);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return renewed.AuthorizationId;
        }
        catch (PaymentException ex)
        {
            throw new PaymentException(
                "This authorization can no longer be renewed. Ask the shopper to pay again, then fulfil the new authorization. " +
                ex.Message,
                409);
        }
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new PaymentException($"Order {orderId} was not found.", 404);
        }

        return order;
    }

    private static void EnsureBuyer(Order order, string buyerId)
    {
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentException("Order was not found.", 404);
        }
    }

    private static void ValidateCard(CardDetails card)
    {
        if (string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry))
        {
            throw new PaymentException("Card number and expiry (YYYY-MM) are required.");
        }
    }

    private static bool IsAuthorizationExpired(string? status) =>
        string.Equals(status, "EXPIRED", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "VOIDED", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "DENIED", StringComparison.OrdinalIgnoreCase);

    private static bool IsAuthorizationStale(PayPalAuthorizationDetails details)
    {
        if (IsAuthorizationExpired(details.Status))
        {
            return true;
        }

        if (details.ExpirationTime is DateTimeOffset expiration && expiration <= DateTimeOffset.UtcNow)
        {
            return true;
        }

        if (details.CreateTime is DateTimeOffset created && DateTimeOffset.UtcNow - created > AuthorizationHonorPeriod)
        {
            return true;
        }

        return false;
    }

    private static bool IsStaleAuthorization(PaymentException ex)
    {
        var text = ex.Message ?? string.Empty;
        return text.Contains("AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase)
            || text.Contains("AUTHORIZATION_VOIDED", StringComparison.OrdinalIgnoreCase)
            || text.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase);
    }
}
