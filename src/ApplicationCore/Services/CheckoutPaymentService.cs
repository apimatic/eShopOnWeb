using System;
using System.Collections.Concurrent;
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

public class CheckoutPaymentService : ICheckoutPaymentService
{
    private static readonly TimeSpan HonorPeriod = TimeSpan.FromDays(3);
    private static readonly TimeSpan AuthorizationLifetime = TimeSpan.FromDays(29);
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalGateway _payPal;

    public CheckoutPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IUriComposer uriComposer,
        IPayPalGateway payPal)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _uriComposer = uriComposer;
        _payPal = payPal;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineRequest> items,
        Address shipToAddress,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));

        if (items == null || items.Count == 0)
        {
            throw new CheckoutException("An order must contain at least one catalog item.");
        }

        var grouped = items
            .GroupBy(i => i.CatalogItemId)
            .Select(g => new OrderLineRequest(g.Key, g.Sum(x => x.Quantity)))
            .ToList();

        foreach (var line in grouped)
        {
            if (line.Quantity <= 0)
            {
                throw new CheckoutException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }
        }

        var catalogIds = grouped.Select(i => i.CatalogItemId).ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogIds), cancellationToken);
        if (catalogItems.Count != catalogIds.Distinct().Count())
        {
            var found = catalogItems.Select(c => c.Id).ToHashSet();
            var missing = catalogIds.First(id => !found.Contains(id));
            throw new CheckoutException($"Catalog item {missing} was not found.", 404);
        }

        var orderItems = grouped.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        order.MarkAwaitingPayment(_payPal.Currency);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> PayAsync(
        int orderId,
        string buyerId,
        CardPaymentDetails? card,
        int? paymentMethodId,
        CancellationToken cancellationToken)
    {
        if (card == null && paymentMethodId == null)
        {
            throw new CheckoutException("Provide card details or a saved paymentMethodId.");
        }

        if (card != null && paymentMethodId != null)
        {
            throw new CheckoutException("Provide either card details or a saved paymentMethodId, not both.");
        }

        return await WithOrderLock(orderId, async () =>
        {
            var order = await GetOwnedOrder(orderId, buyerId, cancellationToken);

            if (order.Status == OrderStatus.Authorized || order.Status == OrderStatus.Fulfilled)
            {
                return order;
            }

            if (order.Status != OrderStatus.AwaitingPayment)
            {
                throw new CheckoutException($"Order {orderId} cannot be paid in status {order.Status}.", 409);
            }

            var amount = MoneyFormatter.Round(order.Total());
            if (amount <= 0)
            {
                throw new CheckoutException("Order total must be greater than zero.");
            }

            var requestId = $"eshop-pay-{order.Id}-{Guid.NewGuid():N}";
            var reference = $"ESHOP-{order.Id}";

            PayPalAuthorizationResult authorization;
            if (paymentMethodId != null)
            {
                var saved = await _paymentMethodRepository.FirstOrDefaultAsync(
                    new SavedPaymentMethodByIdForBuyerSpec(buyerId, paymentMethodId.Value), cancellationToken);
                if (saved == null)
                {
                    throw new CheckoutException("The saved card was not found, or it does not belong to the caller.", 404);
                }

                authorization = await _payPal.AuthorizeVaultedCardAsync(reference, amount, saved.PayPalPaymentTokenId, requestId, cancellationToken);
            }
            else
            {
                authorization = await _payPal.AuthorizeCardAsync(reference, amount, card!, requestId, cancellationToken);
            }

            if (!AmountsMatch(authorization.Amount.Value, amount))
            {
                throw new CheckoutException(
                    $"PayPal authorized {MoneyFormatter.ToPayPalValue(authorization.Amount.Value)} but the order total is {MoneyFormatter.ToPayPalValue(amount)}.",
                    502);
            }

            order.RecordAuthorization(
                authorization.PayPalOrderId,
                authorization.AuthorizationId,
                authorization.Status,
                authorization.ExpirationTime,
                authorization.CreateTime);

            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        });
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        return await WithOrderLock(orderId, async () =>
        {
            var order = await GetOrder(orderId, cancellationToken);

            if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
            {
                return order;
            }

            if (order.Status != OrderStatus.Authorized || string.IsNullOrEmpty(order.PayPalAuthorizationId))
            {
                throw new CheckoutException($"Order {orderId} cannot be fulfilled in status {order.Status}. It must be authorized first.", 409);
            }

            var amount = MoneyFormatter.Round(order.Total());
            var authorizationId = await EnsureFreshAuthorization(order, amount, cancellationToken);

            PayPalCaptureResult capture;
            try
            {
                capture = await _payPal.CaptureAsync(authorizationId, amount, $"eshop-capture-{order.Id}-{Guid.NewGuid():N}", cancellationToken);
            }
            catch (CheckoutException ex) when (
                ex.Message.Contains("AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("AUTH_CAPTURE_PERIOD_EXPIRED", StringComparison.OrdinalIgnoreCase))
            {
                var originalCreated = order.OriginalAuthorizationCreatedAt ?? order.AuthorizationCreatedAt ?? order.OrderDate;
                if (DateTimeOffset.UtcNow >= originalCreated + AuthorizationLifetime)
                {
                    throw new CheckoutException(
                        "This PayPal authorization can no longer be renewed. PayPal only allows reauthorization within 29 days of the original hold. Ask the shopper to place a new order and pay again.",
                        409);
                }

                var renewed = await _payPal.ReauthorizeAsync(
                    authorizationId,
                    amount,
                    $"eshop-reauth-{order.Id}-{DateTimeOffset.UtcNow.UtcTicks}",
                    cancellationToken);
                order.RecordReauthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpirationTime, renewed.CreateTime);
                await _orderRepository.UpdateAsync(order, cancellationToken);
                capture = await _payPal.CaptureAsync(renewed.AuthorizationId, amount, $"eshop-capture-{order.Id}-{renewed.AuthorizationId}", cancellationToken);
            }
            if (string.IsNullOrEmpty(capture.PayPalFee?.CurrencyCode) && capture.Status.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase) == false)
            {
                capture = await _payPal.GetCaptureAsync(capture.CaptureId, cancellationToken);
            }
            else if (capture.PayPalFee == null || capture.NetAmount == null)
            {
                try
                {
                    var detailed = await _payPal.GetCaptureAsync(capture.CaptureId, cancellationToken);
                    capture = detailed with
                    {
                        Amount = detailed.Amount,
                        PayPalFee = detailed.PayPalFee ?? capture.PayPalFee,
                        NetAmount = detailed.NetAmount ?? capture.NetAmount,
                        Status = detailed.Status
                    };
                }
                catch (CheckoutException)
                {
                    // Keep the capture we already have if the follow-up GET fails.
                }
            }

            order.RecordCapture(
                capture.CaptureId,
                capture.Status,
                capture.Amount.Value,
                capture.PayPalFee?.Value,
                capture.NetAmount?.Value);

            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        });
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        return await WithOrderLock(orderId, async () =>
        {
            var order = await GetOrder(orderId, cancellationToken);

            if (order.Status == OrderStatus.Cancelled)
            {
                return order;
            }

            if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
            {
                throw new CheckoutException("A fulfilled order cannot be cancelled. Issue a refund instead.", 409);
            }

            if (order.Status == OrderStatus.Authorized && !string.IsNullOrEmpty(order.PayPalAuthorizationId))
            {
                await _payPal.VoidAuthorizationAsync(order.PayPalAuthorizationId, $"eshop-void-{order.Id}-{Guid.NewGuid():N}", cancellationToken);
                order.MarkCancelled("VOIDED");
            }
            else
            {
                order.MarkCancelled();
            }

            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        });
    }

    public async Task<OrderRefund> RefundAsync(
        int orderId,
        string buyerId,
        string idempotencyKey,
        decimal? amount,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        return await WithOrderLock(orderId, async () =>
        {
            var order = await GetOwnedOrder(orderId, buyerId, cancellationToken);

            var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
            if (existing != null)
            {
                return existing;
            }

            if (order.Status is not OrderStatus.Fulfilled and not OrderStatus.PartiallyRefunded)
            {
                throw new CheckoutException("Only a fulfilled order can be refunded.", 409);
            }

            if (string.IsNullOrEmpty(order.PayPalCaptureId))
            {
                throw new CheckoutException("This order has no captured PayPal payment to refund.", 409);
            }

            var remaining = order.RemainingRefundable();
            var refundAmount = amount.HasValue ? MoneyFormatter.Round(amount.Value) : remaining;
            if (refundAmount <= 0)
            {
                throw new CheckoutException("Refund amount must be greater than zero.");
            }

            if (refundAmount > remaining)
            {
                throw new CheckoutException(
                    $"Refund of {MoneyFormatter.ToPayPalValue(refundAmount)} exceeds the remaining captured amount of {MoneyFormatter.ToPayPalValue(remaining)}.",
                    409);
            }

            var paypalRefund = await _payPal.RefundAsync(
                order.PayPalCaptureId,
                refundAmount == remaining && !amount.HasValue ? null : refundAmount,
                $"eshop-{order.Id}-refund-{idempotencyKey}",
                cancellationToken);

            var refund = order.RecordRefund(
                idempotencyKey,
                paypalRefund.RefundId,
                paypalRefund.Status,
                paypalRefund.Amount.Value,
                paypalRefund.Amount.CurrencyCode);

            await _orderRepository.UpdateAsync(order, cancellationToken);
            return refund;
        });
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), cancellationToken);
    }

    public Task<Order> GetOrderForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken) =>
        GetOwnedOrder(orderId, buyerId, cancellationToken);

    private async Task<string> EnsureFreshAuthorization(Order order, decimal amount, CancellationToken cancellationToken)
    {
        var authorizationId = order.PayPalAuthorizationId!;
        PayPalAuthorizationDetails? live = null;
        try
        {
            live = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
            order.RecordReauthorization(live.AuthorizationId, live.Status, live.ExpirationTime, live.CreateTime ?? order.AuthorizationCreatedAt);
        }
        catch (CheckoutException)
        {
            // Fall back to stored state if PayPal lookup fails; capture/reauthorize will surface a clearer error.
        }

        var status = live?.Status ?? order.PayPalAuthorizationStatus;
        if (status != null && status.Equals("VOIDED", StringComparison.OrdinalIgnoreCase))
        {
            throw new CheckoutException(
                "The PayPal authorization was voided, so fulfilment cannot capture funds. Ask the shopper to pay again.",
                409);
        }

        var now = DateTimeOffset.UtcNow;
        var originalCreated = order.OriginalAuthorizationCreatedAt ?? order.AuthorizationCreatedAt ?? order.OrderDate;
        var expired = status != null && status.Equals("EXPIRED", StringComparison.OrdinalIgnoreCase);
        var pastExpiration = order.AuthorizationExpiresAt != null && order.AuthorizationExpiresAt <= now;
        var pastHonorPeriod = now >= originalCreated + HonorPeriod;
        var withinLifetime = now < originalCreated + AuthorizationLifetime;

        if ((expired || pastExpiration || pastHonorPeriod) && !withinLifetime)
        {
            throw new CheckoutException(
                "This PayPal authorization can no longer be renewed. PayPal only allows reauthorization within 29 days of the original hold. Ask the shopper to place a new order and pay again.",
                409);
        }

        if (expired || pastExpiration || pastHonorPeriod)
        {
            try
            {
                var renewed = await _payPal.ReauthorizeAsync(
                    authorizationId,
                    amount,
                    $"eshop-reauth-{order.Id}-{now.UtcTicks}",
                    cancellationToken);
                order.RecordReauthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpirationTime, renewed.CreateTime);
                await _orderRepository.UpdateAsync(order, cancellationToken);
                return renewed.AuthorizationId;
            }
            catch (CheckoutException ex)
            {
                throw new CheckoutException(
                    "The PayPal authorization is stale and could not be renewed. Ask the shopper to pay again. " + ex.Message,
                    409);
            }
        }

        return authorizationId;
    }

    private async Task<Order> GetOwnedOrder(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await GetOrder(orderId, cancellationToken);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.OrdinalIgnoreCase))
        {
            throw new CheckoutException($"Order {orderId} was not found.", 404);
        }

        return order;
    }

    private async Task<Order> GetOrder(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            throw new CheckoutException($"Order {orderId} was not found.", 404);
        }

        return order;
    }

    private static bool AmountsMatch(decimal left, decimal right) =>
        MoneyFormatter.Round(left) == MoneyFormatter.Round(right);

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
}
