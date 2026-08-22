using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private static readonly Address DefaultShippingAddress =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalPaymentGateway _payPal;
    private readonly IPaymentSettings _paymentSettings;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IUriComposer uriComposer,
        IPayPalPaymentGateway payPal,
        IPaymentSettings paymentSettings)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _uriComposer = uriComposer;
        _payPal = payPal;
        _paymentSettings = paymentSettings;
    }

    public async Task<Order> CreateOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLine> items,
        Address? shippingAddress,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(items, nameof(items));

        if (items.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one item.", nameof(items));
        }

        foreach (var line in items)
        {
            if (line.Quantity <= 0)
            {
                throw new ArgumentException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }
        }

        var catalogItemIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var missing = catalogItemIds.Where(id => !catalogById.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            throw new EntityNotFoundException($"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var orderItems = items.Select(line =>
        {
            var catalogItem = catalogById[line.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shippingAddress ?? DefaultShippingAddress, orderItems);
        return await _orderRepository.AddAsync(order, cancellationToken);
    }

    public async Task<Order> PayAsync(
        string buyerId,
        int orderId,
        CardPaymentDetails? card,
        int? paymentMethodId,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var hasCard = card is not null;
        var hasSaved = paymentMethodId.HasValue;
        if (hasCard == hasSaved)
        {
            throw new ArgumentException("Provide either card details or a saved paymentMethodId, not both or neither.");
        }

        var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);

        if (order.Status is OrderStatus.Authorized or OrderStatus.Fulfilled
            or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            return order;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new InvalidOrderStateException($"Order {orderId} was cancelled and cannot be paid.");
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidOrderStateException($"Order {orderId} cannot be paid while it is {order.Status}.");
        }

        var requestId = order.Payment.EnsureAuthorizeRequestId(order.Id);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        var amount = order.Total();
        var currency = _paymentSettings.Currency;
        PayPalAuthorizationResult authorization;

        if (hasSaved)
        {
            var method = await _paymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId!.Value, buyerId),
                cancellationToken);
            if (method is null)
            {
                throw new EntityNotFoundException(
                    $"Saved payment method {paymentMethodId} was not found for this shopper.");
            }

            authorization = await _payPal.AuthorizeVaultedCardAsync(
                order.Id, amount, currency, requestId, method.PayPalPaymentTokenId, cancellationToken);
        }
        else
        {
            authorization = await _payPal.AuthorizeCardAsync(
                order.Id, amount, currency, requestId, card!, cancellationToken);
        }

        AssertAmountMatches(amount, authorization.AuthorizedAmount, order.Id);

        order.Payment.RecordAuthorization(
            authorization.PayPalOrderId,
            authorization.PayPalOrderStatus,
            authorization.AuthorizationId,
            authorization.AuthorizationStatus,
            authorization.ExpirationTime,
            authorization.AuthorizedAmount,
            authorization.Currency);
        order.MarkAuthorized();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Fulfilled
            || order.Status == OrderStatus.PartiallyRefunded
            || order.Status == OrderStatus.Refunded)
        {
            return order;
        }

        if (order.Status != OrderStatus.Authorized)
        {
            throw new InvalidOrderStateException(
                $"Order {orderId} cannot be fulfilled while it is {order.Status}. Authorize payment first.");
        }

        var authorizationId = order.Payment.AuthorizationId
            ?? throw new InvalidOrderStateException($"Order {orderId} has no PayPal authorization to capture.");

        authorizationId = await RenewIfStaleAsync(order, authorizationId, cancellationToken);

        var captureRequestId = order.Payment.EnsureCaptureRequestId(order.Id);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        PayPalCaptureResult capture;
        try
        {
            capture = await _payPal.CaptureAsync(authorizationId, captureRequestId, cancellationToken);
        }
        catch (PayPalProviderException ex) when (ex.StatusCode == 422 || ex.StatusCode == 400)
        {
            authorizationId = await RenewIfStaleAsync(order, authorizationId, cancellationToken, force: true);
            capture = await _payPal.CaptureAsync(authorizationId, captureRequestId, cancellationToken);
        }

        if (string.Equals(capture.Status, "PENDING", StringComparison.OrdinalIgnoreCase)
            || capture.PaypalFee is null || capture.NetAmount is null)
        {
            try
            {
                capture = await _payPal.GetCaptureAsync(capture.CaptureId, cancellationToken);
            }
            catch (PayPalProviderException)
            {
                // Keep the capture we already have; fees may still be pending.
            }
        }

        order.Payment.RecordCapture(
            capture.CaptureId,
            capture.Status,
            capture.CapturedAmount,
            capture.PaypalFee,
            capture.NetAmount);
        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }

        if (order.Status == OrderStatus.Authorized && !string.IsNullOrEmpty(order.Payment.AuthorizationId))
        {
            var voidRequestId = order.Payment.EnsureVoidRequestId(order.Id);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            await _payPal.VoidAsync(order.Payment.AuthorizationId, voidRequestId, cancellationToken);
            order.Payment.RecordVoid("VOIDED");
        }

        order.MarkCancelled();
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
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);

        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        if (order.Status == OrderStatus.Refunded)
        {
            throw new InvalidOrderStateException($"Order {orderId} has already been fully refunded.");
        }

        var remaining = order.RemainingRefundableAmount();
        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0)
        {
            throw new ArgumentException("Refund amount must be greater than zero.");
        }

        if (refundAmount > remaining)
        {
            throw new InvalidOrderStateException(
                $"Refund amount {MoneyFormatter.ToPayPalValue(refundAmount)} exceeds the remaining captured amount {MoneyFormatter.ToPayPalValue(remaining)} for order {orderId}.");
        }

        var captureId = order.Payment.CaptureId
            ?? throw new InvalidOrderStateException($"Order {orderId} has no captured payment to refund.");

        var paypalRefund = await _payPal.RefundAsync(
            captureId,
            amount.HasValue ? refundAmount : null,
            order.Payment.Currency ?? _paymentSettings.Currency,
            idempotencyKey,
            cancellationToken);

        var recordedAmount = paypalRefund.Amount > 0 ? paypalRefund.Amount : refundAmount;
        var refund = order.AddRefund(paypalRefund.RefundId, paypalRefund.Status, recordedAmount, idempotencyKey);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return refund;
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        return orders;
    }

    private async Task<string> RenewIfStaleAsync(
        Order order,
        string authorizationId,
        CancellationToken cancellationToken,
        bool force = false)
    {
        var stale = force;
        if (!stale)
        {
            try
            {
                var details = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
                if (details.ExpirationTime is DateTimeOffset expiration
                    && expiration <= DateTimeOffset.UtcNow.AddMinutes(5))
                {
                    stale = true;
                }

                if (string.Equals(details.Status, "VOIDED", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(details.Status, "DENIED", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(details.Status, "CAPTURED", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOrderStateException(
                        $"The PayPal authorization for order {order.Id} is {details.Status} and cannot be captured. Ask the shopper to pay again.");
                }
            }
            catch (PayPalProviderException ex) when (ex.StatusCode == 404)
            {
                stale = true;
            }
        }

        if (!stale)
        {
            return authorizationId;
        }

        try
        {
            var renewed = await _payPal.ReauthorizeAsync(
                authorizationId,
                order.Payment.AuthorizedAmount ?? order.Total(),
                order.Payment.Currency ?? _paymentSettings.Currency,
                $"eshop-reauth-{order.Id}",
                cancellationToken);

            order.Payment.RecordReauthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpirationTime);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return renewed.AuthorizationId;
        }
        catch (PayPalProviderException ex)
        {
            throw new InvalidOrderStateException(
                $"The PayPal authorization for order {order.Id} is stale and could not be renewed. Ask the shopper to pay again. PayPal debug id: {ex.DebugId ?? "none"}.");
        }
    }

    private async Task<Order> GetOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new ForbiddenAccessException("This order does not belong to the signed-in shopper.");
        }

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

    private static void AssertAmountMatches(decimal orderTotal, decimal authorizedAmount, int orderId)
    {
        if (MoneyFormatter.ToPayPalValue(orderTotal) != MoneyFormatter.ToPayPalValue(authorizedAmount))
        {
            throw new PaymentException(
                $"PayPal authorized {MoneyFormatter.ToPayPalValue(authorizedAmount)} which does not match order {orderId} total {MoneyFormatter.ToPayPalValue(orderTotal)}.");
        }
    }
}
