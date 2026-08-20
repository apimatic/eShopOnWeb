using System;
using System.Collections.Concurrent;
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

public class OrderPaymentService : IOrderPaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderGates = new();
    private static readonly TimeSpan ReauthorizeHorizon = TimeSpan.FromDays(29);
    private static readonly TimeSpan ExpirationBuffer = TimeSpan.FromMinutes(2);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IPayPalSettings _payPalSettings;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IUriComposer uriComposer,
        IPaymentGateway paymentGateway,
        IPayPalSettings payPalSettings)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _uriComposer = uriComposer;
        _paymentGateway = paymentGateway;
        _payPalSettings = payPalSettings;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<PlaceOrderItem> items,
        Address? shipTo,
        CancellationToken cancellationToken)
    {
        if (items is null || items.Count == 0)
        {
            throw new PaymentException("An order must contain at least one catalog item.", 400);
        }

        if (items.Any(i => i.Quantity <= 0))
        {
            throw new PaymentException("Each item quantity must be greater than zero.", 400);
        }

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        foreach (var id in ids)
        {
            if (!catalogById.ContainsKey(id))
            {
                throw new PaymentException($"Catalog item {id} was not found.", 400);
            }
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

        var address = shipTo ?? new Address("2211 N First St", "San Jose", "CA", "US", "95131");
        var order = new Order(buyerId, address, orderItems, _payPalSettings.Currency);
        return await _orderRepository.AddAsync(order, cancellationToken);
    }

    public async Task<Order> PayAsync(
        int orderId,
        string buyerId,
        CardDetails? card,
        int? paymentMethodId,
        CancellationToken cancellationToken)
    {
        if (card is null && paymentMethodId is null)
        {
            throw new PaymentException("Provide card details or a saved payment method.", 400);
        }

        if (card is not null && paymentMethodId is not null)
        {
            throw new PaymentException("Provide either card details or a saved payment method, not both.", 400);
        }

        return await WithOrderLock(orderId, async () =>
        {
            var order = await GetOwnedOrder(orderId, buyerId, cancellationToken);

            if (order.PaymentStatus == OrderPaymentStatus.Authorized && !string.IsNullOrEmpty(order.AuthorizationId))
            {
                return order;
            }

            if (order.PaymentStatus != OrderPaymentStatus.AwaitingPayment)
            {
                throw new PaymentException($"Order {orderId} cannot be paid from status {order.PaymentStatus}.", 409);
            }

            string? vaultId = null;
            if (paymentMethodId is int methodId)
            {
                var method = await _paymentMethodRepository.FirstOrDefaultAsync(
                    new SavedPaymentMethodByIdAndBuyerSpec(methodId, buyerId), cancellationToken);
                if (method is null)
                {
                    throw new PaymentException("Saved payment method was not found.", 404);
                }

                vaultId = method.PayPalPaymentTokenId;
            }

            var result = await _paymentGateway.AuthorizeAsync(new AuthorizePaymentRequest
            {
                Amount = order.Total(),
                Currency = _payPalSettings.Currency,
                CustomId = order.PaymentReference,
                InvoiceId = $"eShop-{order.PaymentReference}",
                IdempotencyKey = $"pay-{order.PaymentReference}",
                Card = card,
                VaultId = vaultId
            }, cancellationToken);

            if (result.PayerActionRequired)
            {
                throw new PaymentException(
                    "This card requires shopper approval in a browser (3-D Secure). That flow is not supported.",
                    409);
            }

            order.RecordAuthorization(
                result.PayPalOrderId,
                result.PayPalOrderStatus,
                result.AuthorizationId,
                result.AuthorizationStatus,
                result.Expiration,
                _payPalSettings.Currency);

            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        });
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        return await WithOrderLock(orderId, async () =>
        {
            var order = await GetOrder(orderId, cancellationToken);

            if (order.PaymentStatus is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded
                && !string.IsNullOrEmpty(order.CaptureId))
            {
                return order;
            }

            if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
            {
                throw new PaymentException($"Order {orderId} is cancelled and cannot be fulfilled.", 409);
            }

            if (order.PaymentStatus != OrderPaymentStatus.Authorized || string.IsNullOrEmpty(order.AuthorizationId))
            {
                throw new PaymentException($"Order {orderId} has no payment authorization to capture.", 409);
            }

            var authorizationId = await EnsureCapturableAuthorization(order, cancellationToken);

            CaptureResult capture;
            try
            {
                capture = await _paymentGateway.CaptureAuthorizationAsync(
                    authorizationId,
                    $"capture-{order.PaymentReference}",
                    cancellationToken);
            }
            catch (PaymentException ex) when (IsExpiredAuthorization(ex))
            {
                authorizationId = await RenewAuthorization(order, cancellationToken);
                capture = await _paymentGateway.CaptureAuthorizationAsync(
                    authorizationId,
                    $"capture-{order.PaymentReference}",
                    cancellationToken);
            }

            if (capture.IsPending || (capture.PaypalFee is null && capture.NetAmount is null))
            {
                capture = await _paymentGateway.GetCaptureAsync(capture.CaptureId, cancellationToken);
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

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        return await WithOrderLock(orderId, async () =>
        {
            var order = await GetOrder(orderId, cancellationToken);

            if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
            {
                return order;
            }

            if (!string.IsNullOrEmpty(order.AuthorizationId)
                && order.PaymentStatus == OrderPaymentStatus.Authorized)
            {
                await _paymentGateway.VoidAuthorizationAsync(
                    order.AuthorizationId,
                    $"void-{order.PaymentReference}",
                    cancellationToken);
            }

            order.MarkCancelled();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        });
    }

    public async Task<(Order Order, OrderRefund Refund)> RefundAsync(
        int orderId,
        string buyerId,
        string idempotencyKey,
        decimal? amount,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentException("A refund idempotency key is required.", 400);
        }

        return await WithOrderLock(orderId, async () =>
        {
            var order = await GetOwnedOrder(orderId, buyerId, cancellationToken);

            var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
            if (existing is not null)
            {
                return (order, existing);
            }

            if (order.PaymentStatus is not (OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded))
            {
                throw new PaymentException($"Order {orderId} cannot be refunded from status {order.PaymentStatus}.", 409);
            }

            if (string.IsNullOrEmpty(order.CaptureId) || order.CapturedAmount is null)
            {
                throw new PaymentException($"Order {orderId} has no captured payment to refund.", 409);
            }

            var remaining = order.RemainingRefundable();
            if (remaining <= 0m)
            {
                throw new PaymentException("This order has already been refunded in full.", 409);
            }

            var refundAmount = amount ?? remaining;
            if (refundAmount <= 0m)
            {
                throw new PaymentException("Refund amount must be greater than zero.", 400);
            }

            if (refundAmount > remaining)
            {
                throw new PaymentException(
                    $"Refund amount {PayPalMoney.Format(refundAmount, order.Currency ?? _payPalSettings.Currency)} exceeds remaining capturable amount {PayPalMoney.Format(remaining, order.Currency ?? _payPalSettings.Currency)}.",
                    400);
            }

            var result = await _paymentGateway.RefundCaptureAsync(
                order.CaptureId,
                amount is null && refundAmount == order.CapturedAmount ? null : refundAmount,
                order.Currency ?? _payPalSettings.Currency,
                $"{order.PaymentReference}:{idempotencyKey}",
                cancellationToken);

            var refund = new OrderRefund(result.RefundId, idempotencyKey, result.Status ?? "COMPLETED", result.Amount);
            order.AddRefund(refund);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return (order, refund);
        });
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), cancellationToken);
    }

    private async Task<string> EnsureCapturableAuthorization(Order order, CancellationToken cancellationToken)
    {
        var snapshot = await _paymentGateway.GetAuthorizationAsync(order.AuthorizationId!, cancellationToken);
        var status = snapshot.Status ?? string.Empty;

        if (string.Equals(status, "VOIDED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException(
                $"The payment authorization is {status} and cannot be captured. Ask the shopper to pay again.",
                409,
                issue: status);
        }

        if (string.Equals(status, "CAPTURED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "PARTIALLY_CAPTURED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException(
                "The payment authorization has already been captured. Refresh the order before fulfilling.",
                409,
                issue: status);
        }

        var stale = snapshot.Expiration is DateTimeOffset expiration
            && expiration <= DateTimeOffset.UtcNow + ExpirationBuffer;

        if (!stale)
        {
            return snapshot.AuthorizationId;
        }

        return await RenewAuthorization(order, cancellationToken);
    }

    private async Task<string> RenewAuthorization(Order order, CancellationToken cancellationToken)
    {
        if (order.OriginalAuthorizationTime is DateTimeOffset original
            && DateTimeOffset.UtcNow - original >= ReauthorizeHorizon)
        {
            throw new PaymentException(
                "The payment authorization is older than 29 days and cannot be renewed. Ask the shopper to authorize payment again.",
                409,
                issue: "AUTHORIZATION_EXPIRED");
        }

        try
        {
            var renewed = await _paymentGateway.ReauthorizeAsync(
                order.AuthorizationId!,
                order.Total(),
                order.Currency ?? _payPalSettings.Currency,
                $"reauth-{order.PaymentReference}",
                cancellationToken);

            order.ReplaceAuthorization(renewed.AuthorizationId, renewed.Status, renewed.Expiration);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return renewed.AuthorizationId;
        }
        catch (PaymentException ex)
        {
            throw new PaymentException(
                "The payment authorization has expired and cannot be renewed. Ask the shopper to authorize payment again."
                + (string.IsNullOrEmpty(ex.DebugId) ? string.Empty : $" PayPal debug id: {ex.DebugId}."),
                409,
                ex,
                ex.DebugId,
                ex.Issue ?? "AUTHORIZATION_EXPIRED");
        }
    }

    private static bool IsExpiredAuthorization(PaymentException ex)
    {
        return string.Equals(ex.Issue, "AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase)
            || (ex.Message?.Contains("expired", StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private async Task<Order> GetOwnedOrder(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await GetOrder(orderId, cancellationToken);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentException("Order was not found.", 404);
        }

        return order;
    }

    private async Task<Order> GetOrder(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new PaymentException("Order was not found.", 404);
        }

        return order;
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
