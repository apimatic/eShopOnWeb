using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
    private static readonly TimeSpan AuthorizationLifetime = TimeSpan.FromDays(30);
    private static readonly TimeSpan HonorPeriod = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalog;
    private readonly IRepository<SavedPaymentMethod> _paymentMethods;
    private readonly IUriComposer _uriComposer;
    private readonly IPaymentGateway _payments;
    private readonly PayPalOptions _payPal;

    public CheckoutService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalog,
        IRepository<SavedPaymentMethod> paymentMethods,
        IUriComposer uriComposer,
        IPaymentGateway payments,
        PayPalOptions payPal)
    {
        _orders = orders;
        _catalog = catalog;
        _paymentMethods = paymentMethods;
        _uriComposer = uriComposer;
        _payments = payments;
        _payPal = payPal;
    }

    public async Task<PlaceOrderResult> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<PlaceOrderItem> items,
        Address shipTo,
        CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items is null || items.Count == 0)
            throw new PaymentException("At least one catalog item is required.", HttpStatusCode.BadRequest);

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalog.ListAsync(new CatalogItemsSpecification(ids), ct);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var orderItems = new List<OrderItem>();
        foreach (var line in items)
        {
            if (line.Quantity <= 0)
                throw new PaymentException($"Quantity for catalog item {line.CatalogItemId} must be positive.", HttpStatusCode.BadRequest);
            if (!catalogById.TryGetValue(line.CatalogItemId, out var catalogItem))
                throw new PaymentException($"Catalog item {line.CatalogItemId} was not found.", HttpStatusCode.NotFound);

            var snapshot = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(snapshot, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipTo, orderItems);
        await _orders.AddAsync(order, ct);
        return new PlaceOrderResult(order.Id, order.Total(), _payPal.Currency, order.PaymentStatus);
    }

    public async Task<PayOrderResult> PayAsync(
        string buyerId,
        int orderId,
        CardPaymentDetails? card,
        int? paymentMethodId,
        CancellationToken ct)
    {
        var order = await LoadOwnedOrder(buyerId, orderId, ct);

        if (order.PaymentStatus == OrderPaymentStatus.Authorized)
            return ToPayResult(order);

        if (order.PaymentStatus != OrderPaymentStatus.AwaitingPayment)
            throw new PaymentException($"Order {orderId} cannot be paid in state {order.PaymentStatus}.", HttpStatusCode.Conflict);

        if (card is null == paymentMethodId is null)
            throw new PaymentException("Provide either card details or a saved paymentMethodId, not both.", HttpStatusCode.BadRequest);

        var amount = decimal.Round(order.Total(), 2, MidpointRounding.AwayFromZero);
        var requestId = $"pay-{order.Id}-{order.OrderDate.UtcTicks}";
        AuthorizationResult auth;

        if (paymentMethodId is int methodId)
        {
            var method = await _paymentMethods.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdAndBuyerSpecification(methodId, buyerId), ct);
            if (method is null)
                throw new PaymentException("Saved payment method was not found.", HttpStatusCode.NotFound);

            auth = await _payments.AuthorizeVaultedCardAsync(
                order.Id, amount, _payPal.Currency, method.PayPalVaultId, requestId, ct);
        }
        else
        {
            auth = await _payments.AuthorizeCardAsync(
                order.Id, amount, _payPal.Currency, card!, requestId, ct);
        }

        if (decimal.Round(auth.Amount, 2) != amount)
        {
            try
            {
                await _payments.VoidAsync(auth.AuthorizationId, $"mismatch-{order.Id}", ct);
            }
            catch (PaymentException)
            {
                // Best-effort release; the amount mismatch is still the error the caller must see.
            }

            throw new PaymentException(
                $"PayPal authorized {auth.Amount} but the order total is {amount}.",
                HttpStatusCode.BadGateway);
        }

        order.RecordAuthorization(
            auth.PayPalOrderId,
            auth.OrderStatus,
            auth.AuthorizationId,
            auth.AuthorizationStatus,
            auth.Expiration,
            auth.CreatedAt,
            _payPal.Currency);
        await _orders.UpdateAsync(order, ct);
        return ToPayResult(order);
    }

    public async Task<FulfilOrderResult> FulfilAsync(int orderId, CancellationToken ct)
    {
        var order = await LoadOrder(orderId, ct);

        if (order.PaymentStatus is OrderPaymentStatus.Fulfilled
            or OrderPaymentStatus.PartiallyRefunded
            or OrderPaymentStatus.Refunded)
        {
            return ToFulfilResult(order);
        }

        if (order.PaymentStatus != OrderPaymentStatus.Authorized)
            throw new PaymentException($"Order {orderId} cannot be fulfilled in state {order.PaymentStatus}.", HttpStatusCode.Conflict);

        if (string.IsNullOrEmpty(order.PayPalAuthorizationId))
            throw new PaymentException("Order has no PayPal authorization to capture.", HttpStatusCode.Conflict);

        var authorizationId = order.PayPalAuthorizationId;
        var snapshot = await _payments.GetAuthorizationAsync(authorizationId, ct);

        if (string.Equals(snapshot.Status, "VOIDED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(snapshot.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException(
                $"PayPal authorization {authorizationId} is {snapshot.Status} and cannot be captured. Collect a new payment.",
                HttpStatusCode.Conflict);
        }

        if (string.Equals(snapshot.Status, "CAPTURED", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(order.PayPalCaptureId))
        {
            return ToFulfilResult(order);
        }

        var created = order.AuthorizationCreatedAt ?? snapshot.CreatedAt;
        if (created.HasValue && DateTimeOffset.UtcNow - created.Value >= AuthorizationLifetime)
        {
            throw new PaymentException(
                "The PayPal authorization is older than 30 days and can no longer be renewed. Ask the shopper to pay again.",
                HttpStatusCode.Conflict);
        }

        var honorExpired = snapshot.Expiration.HasValue
            ? snapshot.Expiration.Value <= DateTimeOffset.UtcNow
            : created.HasValue && DateTimeOffset.UtcNow - created.Value >= HonorPeriod;

        if (honorExpired)
        {
            try
            {
                var renewed = await _payments.ReauthorizeAsync(
                    authorizationId,
                    order.Total(),
                    order.Currency ?? _payPal.Currency,
                    $"reauth-{order.Id}-{order.OrderDate.UtcTicks}",
                    ct);
                order.ReplaceAuthorization(renewed.AuthorizationId, renewed.Status, renewed.Expiration);
                await _orders.UpdateAsync(order, ct);
                authorizationId = renewed.AuthorizationId;
            }
            catch (PaymentException ex)
            {
                throw new PaymentException(
                    "The PayPal authorization honor period has ended and the hold could not be renewed. Ask the shopper to pay again.",
                    ex,
                    HttpStatusCode.Conflict,
                    ex.DebugId);
            }
        }

        var capture = await _payments.CaptureAsync(
            authorizationId,
            $"fulfil-{order.Id}-{order.OrderDate.UtcTicks}",
            $"eShop-cap-{order.Id}-{order.PayPalAuthorizationId}",
            ct);

        var expected = decimal.Round(order.Total(), 2, MidpointRounding.AwayFromZero);
        if (decimal.Round(capture.CapturedAmount, 2) != expected)
        {
            throw new PaymentException(
                $"PayPal captured {capture.CapturedAmount} but the order total is {expected}.",
                HttpStatusCode.BadGateway);
        }

        order.RecordCapture(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PaypalFee, capture.NetAmount);
        await _orders.UpdateAsync(order, ct);
        return ToFulfilResult(order);
    }

    public async Task<CancelOrderResult> CancelAsync(int orderId, CancellationToken ct)
    {
        var order = await LoadOrder(orderId, ct);

        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
            return new CancelOrderResult(order.Id, order.PaymentStatus, order.PayPalAuthorizationStatus ?? "VOIDED");

        if (order.PaymentStatus != OrderPaymentStatus.Authorized)
            throw new PaymentException($"Order {orderId} cannot be cancelled in state {order.PaymentStatus}.", HttpStatusCode.Conflict);

        if (string.IsNullOrEmpty(order.PayPalAuthorizationId))
            throw new PaymentException("Order has no PayPal authorization to void.", HttpStatusCode.Conflict);

        await _payments.VoidAsync(order.PayPalAuthorizationId, $"cancel-{order.Id}-{order.OrderDate.UtcTicks}", ct);
        order.RecordCancellation("VOIDED");
        await _orders.UpdateAsync(order, ct);
        return new CancelOrderResult(order.Id, order.PaymentStatus, order.PayPalAuthorizationStatus ?? "VOIDED");
    }

    public async Task<RefundOrderResult> RefundAsync(
        string buyerId,
        int orderId,
        string idempotencyKey,
        decimal? amount,
        CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var order = await LoadOwnedOrder(buyerId, orderId, ct);

        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return new RefundOrderResult(
                existing.PayPalRefundId,
                order.Id,
                order.PaymentStatus,
                existing.Amount,
                order.RemainingRefundable(),
                order.Currency ?? _payPal.Currency);
        }

        if (order.PaymentStatus is not (OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded))
            throw new PaymentException($"Order {orderId} cannot be refunded in state {order.PaymentStatus}.", HttpStatusCode.Conflict);

        if (string.IsNullOrEmpty(order.PayPalCaptureId))
            throw new PaymentException("Order has no captured payment to refund.", HttpStatusCode.Conflict);

        var remaining = order.RemainingRefundable();
        var refundAmount = amount.HasValue
            ? decimal.Round(amount.Value, 2, MidpointRounding.AwayFromZero)
            : remaining;

        if (refundAmount <= 0)
            throw new PaymentException("Refund amount must be greater than zero.", HttpStatusCode.BadRequest);
        if (refundAmount > remaining)
            throw new PaymentException(
                $"Refund of {refundAmount} exceeds remaining refundable amount {remaining}.",
                HttpStatusCode.BadRequest);

        var result = await _payments.RefundAsync(
            order.PayPalCaptureId,
            refundAmount == remaining && !amount.HasValue ? null : refundAmount,
            order.Currency ?? _payPal.Currency,
            idempotencyKey,
            ct);

        var recorded = order.RecordRefund(result.RefundId, idempotencyKey, result.Amount, result.Status);
        await _orders.UpdateAsync(order, ct);
        return new RefundOrderResult(
            recorded.PayPalRefundId,
            order.Id,
            order.PaymentStatus,
            recorded.Amount,
            order.RemainingRefundable(),
            order.Currency ?? _payPal.Currency);
    }

    private async Task<Order> LoadOwnedOrder(string buyerId, int orderId, CancellationToken ct)
    {
        var order = await LoadOrder(orderId, ct);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            throw new PaymentException("Order was not found.", HttpStatusCode.NotFound);
        return order;
    }

    private async Task<Order> LoadOrder(int orderId, CancellationToken ct)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), ct);
        if (order is null)
            throw new PaymentException("Order was not found.", HttpStatusCode.NotFound);
        return order;
    }

    private PayOrderResult ToPayResult(Order order) =>
        new(order.Id,
            order.PaymentStatus,
            order.PayPalOrderId ?? string.Empty,
            order.PayPalAuthorizationId ?? string.Empty,
            order.PayPalAuthorizationStatus ?? string.Empty,
            order.AuthorizationExpiration,
            order.Total(),
            order.Currency ?? _payPal.Currency);

    private FulfilOrderResult ToFulfilResult(Order order) =>
        new(order.Id,
            order.PaymentStatus,
            order.PayPalCaptureId ?? string.Empty,
            order.PayPalCaptureStatus ?? string.Empty,
            order.CapturedAmount ?? 0m,
            order.PaypalFee,
            order.NetAmount,
            order.Currency ?? _payPal.Currency);
}
