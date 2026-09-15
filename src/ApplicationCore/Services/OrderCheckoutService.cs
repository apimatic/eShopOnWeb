using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payment;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderCheckoutService : IOrderCheckoutService
{
    private static readonly TimeSpan AuthorizationHonorPeriod = TimeSpan.FromDays(3);
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalGateway _payPalGateway;
    private readonly PayPalSettings _payPalSettings;

    public OrderCheckoutService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IUriComposer uriComposer,
        IPayPalGateway payPalGateway,
        PayPalSettings payPalSettings)
    {
        _orderRepository = orderRepository;
        _catalogItemRepository = catalogItemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _uriComposer = uriComposer;
        _payPalGateway = payPalGateway;
        _payPalSettings = payPalSettings;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> items, Address shippingAddress, CancellationToken cancellationToken = default)
    {
        if (items is null || items.Count == 0)
        {
            throw new PaymentException("At least one catalog item is required.");
        }

        var quantities = new Dictionary<int, int>();
        foreach (var item in items)
        {
            if (item.Quantity <= 0)
            {
                throw new PaymentException("Quantity must be greater than zero.");
            }

            quantities[item.CatalogItemId] = quantities.GetValueOrDefault(item.CatalogItemId) + item.Quantity;
        }

        var catalogItems = await _catalogItemRepository.ListAsync(new CatalogItemsSpecification(quantities.Keys.ToArray()), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);
        foreach (var catalogItemId in quantities.Keys)
        {
            if (!catalogById.ContainsKey(catalogItemId))
            {
                throw new PaymentException($"Catalog item {catalogItemId} was not found.", 404);
            }
        }

        var orderItems = quantities.Select(pair =>
        {
            var catalogItem = catalogById[pair.Key];
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, pair.Value);
        }).ToList();

        var order = new Order(buyerId, shippingAddress, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> PayAsync(string buyerId, int orderId, int? paymentMethodId, CardPaymentSource? card, CancellationToken cancellationToken = default)
    {
        return await WithOrderLock(orderId, async () =>
        {
            var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);
            if (order.Status == OrderStatus.Authorized && order.Payment is not null)
            {
                return order;
            }

            if (order.Status != OrderStatus.AwaitingPayment)
            {
                throw new PaymentException($"Order cannot be paid in status {order.Status}.", 409);
            }

            var currency = RequireCurrency();
            var amount = order.Total();
            if (amount <= 0)
            {
                throw new PaymentException("Order total must be greater than zero.");
            }

            string? vaultId = null;
            CardPaymentSource? cardToCharge = card;
            if (paymentMethodId.HasValue)
            {
                var saved = await _paymentMethodRepository.FirstOrDefaultAsync(
                    new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId.Value, buyerId), cancellationToken);
                if (saved is null)
                {
                    throw new PaymentException("Saved payment method was not found.", 404);
                }

                vaultId = saved.PayPalVaultId;
                cardToCharge = null;
            }
            else if (cardToCharge is null)
            {
                throw new PaymentException("Provide card details or a saved paymentMethodId.");
            }

            var paypalOrder = await _payPalGateway.CreateAuthorizedOrderAsync(
                new CreateAuthorizedOrderCommand(
                    InvoiceId: $"ESHOP-{order.PaymentCorrelationId}",
                    CustomId: order.Id.ToString(),
                    Amount: amount,
                    Currency: currency,
                    IdempotencyKey: $"eshop-pay-{order.PaymentCorrelationId}",
                    Card: cardToCharge,
                    VaultId: vaultId),
                cancellationToken);

            var authorization = paypalOrder.Authorizations[0];
            var authorizedAmount = authorization.Amount?.Value ?? amount;
            if (authorizedAmount != amount)
            {
                throw new PaymentException(
                    $"PayPal authorized {authorizedAmount} {currency} but the order total is {amount} {currency}.",
                    502);
            }

            order.RecordAuthorization(new OrderPayment(
                paypalOrder.Id,
                paypalOrder.Status,
                authorization.Id,
                authorization.Status,
                authorization.CreateTime,
                authorization.ExpirationTime,
                currency,
                authorizedAmount));

            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        });
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return await WithOrderLock(orderId, async () =>
        {
            var order = await GetOrderAsync(orderId, cancellationToken);
            if (order.IsCaptured())
            {
                return order;
            }

            if (order.Status != OrderStatus.Authorized || order.Payment is null)
            {
                throw new PaymentException($"Order cannot be fulfilled in status {order.Status}.", 409);
            }

            var currency = order.Payment.Currency;
            var amount = order.Payment.AuthorizedAmount;
            var authorizationId = await EnsureFreshAuthorizationAsync(order, amount, currency, cancellationToken);
            if (order.IsCaptured())
            {
                return order;
            }

            PayPalCaptureResult capture;
            try
            {
                capture = await _payPalGateway.CaptureAuthorizationAsync(
                    authorizationId,
                    amount,
                    currency,
                    $"eshop-capture-{order.PaymentCorrelationId}",
                    cancellationToken);
            }
            catch (PaymentException ex) when (IsStaleAuthorization(ex))
            {
                authorizationId = await RenewAuthorizationAsync(order, amount, currency, cancellationToken);
                capture = await _payPalGateway.CaptureAuthorizationAsync(
                    authorizationId,
                    amount,
                    currency,
                    $"eshop-capture-{order.PaymentCorrelationId}",
                    cancellationToken);
            }

            order.RecordCapture(
                capture.Id,
                capture.Status,
                capture.Amount?.Value ?? amount,
                capture.PaypalFee?.Value,
                capture.NetAmount?.Value);

            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        });
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return await WithOrderLock(orderId, async () =>
        {
            var order = await GetOrderAsync(orderId, cancellationToken);
            if (order.Status == OrderStatus.Cancelled)
            {
                return order;
            }

            if (order.Status == OrderStatus.Authorized && order.Payment?.AuthorizationId is not null)
            {
                try
                {
                    await _payPalGateway.VoidAuthorizationAsync(
                        order.Payment.AuthorizationId,
                        $"eshop-void-{order.PaymentCorrelationId}",
                        cancellationToken);
                }
                catch (PaymentException ex) when (ex.StatusCode == 404 || IsAlreadyVoided(ex))
                {
                    // Authorization is already gone; still cancel locally.
                }
            }

            order.RecordCancellation();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        });
    }

    public async Task<OrderRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentException("A refund idempotencyKey is required.");
        }

        return await WithOrderLock(orderId, async () =>
        {
            var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);
            var existing = order.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
            if (existing is not null)
            {
                return existing;
            }

            if (order.Payment?.CaptureId is null)
            {
                throw new PaymentException("Order has no captured payment to refund.", 409);
            }

            var refundAmount = amount ?? order.RemainingRefundableAmount();
            refundAmount = decimal.Round(refundAmount, 2, MidpointRounding.AwayFromZero);
            if (refundAmount <= 0)
            {
                throw new PaymentException("Refund amount must be greater than zero.");
            }

            if (refundAmount > order.RemainingRefundableAmount())
            {
                throw new PaymentException($"Refund of {refundAmount} exceeds remaining refundable amount {order.RemainingRefundableAmount()}.");
            }

            var paypalRequestId = $"eshop-refund-{order.PaymentCorrelationId}-{idempotencyKey}";
            if (paypalRequestId.Length > 108)
            {
                paypalRequestId = paypalRequestId[..108];
            }

            var paypalRefund = await _payPalGateway.RefundCaptureAsync(
                order.Payment.CaptureId,
                refundAmount,
                order.Payment.Currency,
                paypalRequestId,
                cancellationToken);

            var refund = order.RecordRefund(
                paypalRefund.Id,
                paypalRefund.Status,
                paypalRefund.Amount?.Value ?? refundAmount,
                order.Payment.Currency,
                idempotencyKey);

            await _orderRepository.UpdateAsync(order, cancellationToken);
            return refund;
        });
    }

    public Task<Order> GetOrderAsync(string buyerId, int orderId, bool requireOwner, CancellationToken cancellationToken = default)
    {
        return requireOwner
            ? GetOwnedOrderAsync(buyerId, orderId, cancellationToken)
            : GetOrderAsync(orderId, cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), cancellationToken);
        return orders;
    }

    private async Task<string> EnsureFreshAuthorizationAsync(Order order, decimal amount, string currency, CancellationToken cancellationToken)
    {
        var payment = order.Payment!;
        PayPalAuthorizationResult authorization;
        try
        {
            authorization = await _payPalGateway.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
        }
        catch (PaymentException ex) when (IsStaleAuthorization(ex))
        {
            return await RenewAuthorizationAsync(order, amount, currency, cancellationToken);
        }

        payment.UpdateAuthorization(authorization.Id, authorization.Status, authorization.CreateTime, authorization.ExpirationTime);

        if (string.Equals(authorization.Status, "CAPTURED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(authorization.Status, "PARTIALLY_CAPTURED", StringComparison.OrdinalIgnoreCase))
        {
            var paypalOrder = await _payPalGateway.GetOrderAsync(payment.PayPalOrderId, cancellationToken);
            var existingCapture = paypalOrder.Captures.FirstOrDefault();
            if (existingCapture is not null)
            {
                order.RecordCapture(
                    existingCapture.Id,
                    existingCapture.Status,
                    existingCapture.Amount?.Value ?? amount,
                    existingCapture.PaypalFee?.Value,
                    existingCapture.NetAmount?.Value);
                await _orderRepository.UpdateAsync(order, cancellationToken);
            }

            return authorization.Id;
        }

        if (string.Equals(authorization.Status, "VOIDED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(authorization.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            throw new AuthorizationCannotBeRenewedException(
                $"PayPal authorization {authorization.Id} is {authorization.Status} and cannot be captured. Ask the shopper to place and pay a new order.");
        }

        if (authorization.ExpirationTime is not null && authorization.ExpirationTime <= DateTimeOffset.UtcNow)
        {
            throw new AuthorizationCannotBeRenewedException(
                $"PayPal authorization {authorization.Id} expired at {authorization.ExpirationTime:O} and can no longer be renewed. Ask the shopper to place and pay a new order.");
        }

        var honorExpires = (authorization.CreateTime ?? payment.AuthorizationCreateTime ?? payment.AuthorizedAt ?? DateTimeOffset.UtcNow) + AuthorizationHonorPeriod;
        if (DateTimeOffset.UtcNow >= honorExpires)
        {
            return await RenewAuthorizationAsync(order, amount, currency, cancellationToken);
        }

        return authorization.Id;
    }

    private async Task<string> RenewAuthorizationAsync(Order order, decimal amount, string currency, CancellationToken cancellationToken)
    {
        var payment = order.Payment!;
        try
        {
            var renewed = await _payPalGateway.ReauthorizeAsync(
                payment.AuthorizationId,
                amount,
                currency,
                $"eshop-reauth-{order.PaymentCorrelationId}",
                cancellationToken);

            payment.UpdateAuthorization(renewed.Id, renewed.Status, renewed.CreateTime, renewed.ExpirationTime);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return renewed.Id;
        }
        catch (PaymentException ex)
        {
            throw new AuthorizationCannotBeRenewedException(
                $"PayPal authorization {payment.AuthorizationId} is stale and could not be renewed ({ex.Message.TrimEnd('.')}). Ask the shopper to place and pay a new order.");
        }
    }

    private async Task<Order> GetOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (!order.OwnedBy(buyerId))
        {
            throw new PaymentException("Order was not found.", 404);
        }

        return order;
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new PaymentException("Order was not found.", 404);
        }

        return order;
    }

    private string RequireCurrency()
    {
        if (string.IsNullOrWhiteSpace(_payPalSettings.Currency))
        {
            throw new PaymentException("PayPal:Currency is not configured.", 500);
        }

        return _payPalSettings.Currency;
    }

    private static async Task<T> WithOrderLock<T>(int orderId, Func<Task<T>> action)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
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

    private static bool IsStaleAuthorization(PaymentException exception)
    {
        var text = exception.Message;
        return text.Contains("AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase)
            || text.Contains("AUTHORIZATION_VOIDED", StringComparison.OrdinalIgnoreCase)
            || text.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase)
            || text.Contains("REAUTHORIZE", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAlreadyVoided(PaymentException exception)
    {
        var text = exception.Message;
        return text.Contains("AUTHORIZATION_ALREADY_VOIDED", StringComparison.OrdinalIgnoreCase)
            || text.Contains("VOIDED", StringComparison.OrdinalIgnoreCase)
            || exception.StatusCode == 409;
    }
}
