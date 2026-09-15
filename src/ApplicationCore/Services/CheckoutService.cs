using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class CheckoutService : ICheckoutService
{
    private static readonly Address DefaultShipTo =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalog;
    private readonly IRepository<SavedPaymentMethod> _paymentMethods;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalPaymentGateway _payPal;
    private readonly IPaymentSettings _paymentSettings;

    public CheckoutService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalog,
        IRepository<SavedPaymentMethod> paymentMethods,
        IUriComposer uriComposer,
        IPayPalPaymentGateway payPal,
        IPaymentSettings paymentSettings)
    {
        _orders = orders;
        _catalog = catalog;
        _paymentMethods = paymentMethods;
        _uriComposer = uriComposer;
        _payPal = payPal;
        _paymentSettings = paymentSettings;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CreatePaidOrderItem> items,
        Address? shipTo,
        CancellationToken ct)
    {
        if (items is null || items.Count == 0)
        {
            throw new CheckoutException("An order must contain at least one item.", 400);
        }

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalog.ListAsync(new CatalogItemsSpecification(ids), ct);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var orderItems = new List<OrderItem>();
        foreach (var line in items)
        {
            if (line.Quantity <= 0)
            {
                throw new CheckoutException("Quantity must be greater than zero.", 400);
            }

            if (!catalogById.TryGetValue(line.CatalogItemId, out var catalogItem))
            {
                throw new CheckoutException($"Catalog item {line.CatalogItemId} was not found.", 400);
            }

            var snapshot = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(snapshot, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipTo ?? DefaultShipTo, orderItems);
        return await _orders.AddAsync(order, ct);
    }

    public async Task<Order> PayWithCardAsync(int orderId, string buyerId, CardPaymentInput card, CancellationToken ct)
    {
        var order = await LoadOwnedPendingAsync(orderId, buyerId, ct);
        if (order.PaymentStatus == OrderPaymentStatus.Authorized && !string.IsNullOrEmpty(order.PayPalAuthorizationId))
        {
            return order;
        }

        var key = order.EnsurePaymentIdempotencyKey();
        await _orders.UpdateAsync(order, ct);

        var result = await _payPal.AuthorizeCardAsync(
            order.Id.ToString(CultureInfo.InvariantCulture),
            order.Total(),
            Currency,
            card,
            CreateRequestId(order.Id, key),
            AuthorizeRequestId(order.Id, key),
            ct);

        ApplyAuthorization(order, result);
        await _orders.UpdateAsync(order, ct);
        return order;
    }

    public async Task<Order> PayWithSavedCardAsync(int orderId, string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var order = await LoadOwnedPendingAsync(orderId, buyerId, ct);
        if (order.PaymentStatus == OrderPaymentStatus.Authorized && !string.IsNullOrEmpty(order.PayPalAuthorizationId))
        {
            return order;
        }

        var method = await _paymentMethods.GetByIdAsync(paymentMethodId, ct);
        if (method is null || method.BuyerId != buyerId)
        {
            throw new CheckoutException("Saved card was not found.", 404);
        }

        var key = order.EnsurePaymentIdempotencyKey();
        await _orders.UpdateAsync(order, ct);

        var result = await _payPal.AuthorizeVaultedCardAsync(
            order.Id.ToString(CultureInfo.InvariantCulture),
            order.Total(),
            Currency,
            method.PayPalPaymentTokenId,
            CreateRequestId(order.Id, key),
            AuthorizeRequestId(order.Id, key),
            ct);

        ApplyAuthorization(order, result);
        await _orders.UpdateAsync(order, ct);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct);

        if (order.PaymentStatus is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded
            or OrderPaymentStatus.Refunded)
        {
            if (!string.IsNullOrEmpty(order.PayPalCaptureId))
            {
                return order;
            }
        }

        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            throw new CheckoutException("A cancelled order cannot be fulfilled.", 409);
        }

        if (string.IsNullOrEmpty(order.PayPalAuthorizationId))
        {
            throw new CheckoutException("This order has no payment hold to capture.", 409);
        }

        var authorizationId = await EnsureFreshAuthorizationAsync(order, ct);

        var capture = await _payPal.CaptureAsync(
            authorizationId,
            order.Id.ToString(CultureInfo.InvariantCulture),
            order.Total(),
            Currency,
            CaptureRequestId(order.Id, order.PaymentIdempotencyKey ?? order.Id.ToString(CultureInfo.InvariantCulture)),
            ct);

        order.RecordCapture(
            capture.CaptureId,
            capture.Status,
            PayPalMoney.Parse(capture.AmountValue),
            capture.FeeValue is null ? null : PayPalMoney.Parse(capture.FeeValue),
            capture.NetValue is null ? null : PayPalMoney.Parse(capture.NetValue),
            capture.Currency ?? Currency);

        await _orders.UpdateAsync(order, ct);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct);

        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            return order;
        }

        if (order.PaymentStatus is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded
            or OrderPaymentStatus.Refunded)
        {
            throw new CheckoutException("A fulfilled order cannot be cancelled. Issue a refund instead.", 409);
        }

        if (!string.IsNullOrEmpty(order.PayPalAuthorizationId))
        {
            await _payPal.VoidAsync(order.PayPalAuthorizationId, VoidRequestId(order.Id, order.PaymentIdempotencyKey ?? order.Id.ToString(CultureInfo.InvariantCulture)), ct);
        }

        order.RecordVoid("VOIDED");
        await _orders.UpdateAsync(order, ct);
        return order;
    }

    public async Task<OrderRefund> RefundAsync(
        int orderId,
        string buyerId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new CheckoutException("An idempotency key is required for refunds.", 400);
        }

        var order = await LoadOwnedAsync(orderId, buyerId, ct);
        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        if (order.PaymentStatus is not (OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded))
        {
            throw new CheckoutException("Only a captured payment can be refunded.", 409);
        }

        if (string.IsNullOrEmpty(order.PayPalCaptureId) || order.CapturedAmount is null)
        {
            throw new CheckoutException("This order has no captured payment to refund.", 409);
        }

        var remaining = order.RemainingRefundable();
        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0m)
        {
            throw new CheckoutException("There is no remaining captured amount to refund.", 409);
        }

        if (refundAmount > remaining)
        {
            throw new CheckoutException(
                $"Refund of {PayPalMoney.Format(refundAmount)} exceeds remaining refundable amount {PayPalMoney.Format(remaining)}.",
                422);
        }

        var result = await _payPal.RefundAsync(
            order.PayPalCaptureId,
            amount,
            order.PaymentCurrency ?? Currency,
            $"eshop-refund-{order.Id}-{idempotencyKey}",
            ct);

        var recordedAmount = result.AmountValue is null ? refundAmount : PayPalMoney.Parse(result.AmountValue);
        var refund = order.RecordRefund(result.RefundId, idempotencyKey, recordedAmount, result.Status ?? "COMPLETED");
        await _orders.UpdateAsync(order, ct);
        return refund;
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        return await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
    }

    public async Task<Order?> GetMyOrderAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        if (order is null || order.BuyerId != buyerId)
        {
            return null;
        }

        return order;
    }

    private async Task<string> EnsureFreshAuthorizationAsync(Order order, CancellationToken ct)
    {
        var snapshot = await _payPal.GetAuthorizationAsync(order.PayPalAuthorizationId!, ct);
        var status = snapshot.Status ?? order.PayPalAuthorizationStatus;

        if (IsCapturedStatus(status))
        {
            if (!string.IsNullOrEmpty(order.PayPalCaptureId))
            {
                return order.PayPalAuthorizationId!;
            }
        }

        if (string.Equals(status, "VOIDED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            throw new CheckoutException(
                $"The payment hold is {status} and cannot be captured. Ask the shopper to pay again.",
                409,
                operatorActionable: true);
        }

        var createdAt = ParseTime(snapshot.CreateTime) ?? order.OriginalAuthorizationCreatedAt ?? DateTimeOffset.UtcNow;
        var expiresAt = ParseTime(snapshot.ExpirationTime) ?? order.AuthorizationExpiresAt;
        var now = DateTimeOffset.UtcNow;
        var age = now - createdAt;

        if (age.TotalDays >= 30)
        {
            throw new CheckoutException(
                "The payment hold is older than 30 days and can no longer be renewed. Ask the shopper to place and pay a new order.",
                409,
                operatorActionable: true);
        }

        var honorPeriodActive = expiresAt is not null && expiresAt > now && age.TotalDays <= 3;
        if (honorPeriodActive)
        {
            return snapshot.Id;
        }

        var reauthorized = await _payPal.ReauthorizeAsync(
            snapshot.Id,
            order.Total(),
            order.PaymentCurrency ?? Currency,
            ReauthorizeRequestId(order.Id, order.PaymentIdempotencyKey ?? order.Id.ToString(CultureInfo.InvariantCulture)),
            ct);

        order.ReplaceAuthorization(
            reauthorized.Id,
            reauthorized.Status,
            ParseTime(reauthorized.ExpirationTime));
        await _orders.UpdateAsync(order, ct);
        return reauthorized.Id;
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        if (order is null)
        {
            throw new CheckoutException("Order was not found.", 404);
        }

        return order;
    }

    private async Task<Order> LoadOwnedAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct);
        if (order.BuyerId != buyerId)
        {
            throw new CheckoutException("Order was not found.", 404);
        }

        return order;
    }

    private async Task<Order> LoadOwnedPendingAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var order = await LoadOwnedAsync(orderId, buyerId, ct);
        if (order.PaymentStatus == OrderPaymentStatus.Authorized && !string.IsNullOrEmpty(order.PayPalAuthorizationId))
        {
            return order;
        }

        if (order.PaymentStatus != OrderPaymentStatus.PendingPayment)
        {
            throw new CheckoutException($"Order cannot be paid in status {order.PaymentStatus}.", 409);
        }

        return order;
    }

    private void ApplyAuthorization(Order order, AuthorizationResult result)
    {
        order.RecordAuthorization(
            result.PayPalOrderId,
            result.AuthorizationId,
            result.Status,
            ParseTime(result.ExpirationTime),
            ParseTime(result.CreateTime),
            result.Currency);
    }

    private string Currency =>
        string.IsNullOrWhiteSpace(_paymentSettings.Currency)
            ? throw new CheckoutException("PayPal:Currency is not configured.", 500)
            : _paymentSettings.Currency;

    private static bool IsCapturedStatus(string? status) =>
        string.Equals(status, "CAPTURED", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "PARTIALLY_CAPTURED", StringComparison.OrdinalIgnoreCase);

    private static DateTimeOffset? ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private static string CreateRequestId(int orderId, string key) => $"eshop-create-{orderId}-{key}";
    private static string AuthorizeRequestId(int orderId, string key) => $"eshop-auth-{orderId}-{key}";
    private static string CaptureRequestId(int orderId, string key) => $"eshop-capture-{orderId}-{key}";
    private static string VoidRequestId(int orderId, string key) => $"eshop-void-{orderId}-{key}";
    private static string ReauthorizeRequestId(int orderId, string key) => $"eshop-reauth-{orderId}-{key}";
}
