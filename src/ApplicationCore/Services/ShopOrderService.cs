using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ShopOrderService : IShopOrderService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();
    private static readonly TimeSpan HonorPeriod = TimeSpan.FromDays(3);
    private static readonly TimeSpan ReauthWindow = TimeSpan.FromDays(30);

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalog;
    private readonly IRepository<SavedPaymentMethod> _savedMethods;
    private readonly IUriComposer _uriComposer;
    private readonly IPaymentGateway _payments;
    private readonly IPaymentSettings _settings;

    public ShopOrderService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalog,
        IRepository<SavedPaymentMethod> savedMethods,
        IUriComposer uriComposer,
        IPaymentGateway payments,
        IPaymentSettings settings)
    {
        _orders = orders;
        _catalog = catalog;
        _savedMethods = savedMethods;
        _uriComposer = uriComposer;
        _payments = payments;
        _settings = settings;
    }

    public async Task<ShopOrderResult> PlaceAsync(
        string buyerId,
        IReadOnlyList<PlaceOrderItem> items,
        Address? shipTo,
        CancellationToken ct)
    {
        if (items is null || items.Count == 0)
        {
            throw new OrderPaymentException("At least one catalog item is required.", 400);
        }

        foreach (var item in items)
        {
            if (item.Quantity <= 0)
            {
                throw new OrderPaymentException("Quantity must be greater than zero.", 400);
            }
        }

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalog.ListAsync(new CatalogItemsSpecification(ids), ct);
        var missing = ids.Except(catalogItems.Select(c => c.Id)).ToArray();
        if (missing.Length > 0)
        {
            throw new OrderPaymentException($"Catalog item(s) not found: {string.Join(", ", missing)}.", 400);
        }

        var orderItems = items.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var picture = _uriComposer.ComposePicUri(catalogItem.PictureUri);
            var ordered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, picture);
            return new OrderItem(ordered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = shipTo ?? new Address("123 Main St.", "Kent", "OH", "United States", "44240");
        var order = new Order(buyerId, address, orderItems);
        await _orders.AddAsync(order, ct);
        return Map(order);
    }

    public async Task<ShopOrderResult> PayAsync(
        string buyerId,
        int orderId,
        CardPaymentSource? card,
        int? paymentMethodId,
        CancellationToken ct)
    {
        var gate = LockFor(orderId);
        await gate.WaitAsync(ct);
        try
        {
            var order = await LoadOwned(orderId, buyerId, ct);

            if (order.Status is ShopOrderStatus.Authorized or ShopOrderStatus.Fulfilled
                or ShopOrderStatus.PartiallyRefunded or ShopOrderStatus.Refunded)
            {
                return Map(order);
            }

            if (order.Status == ShopOrderStatus.Cancelled)
            {
                throw new OrderPaymentException("A cancelled order cannot be paid.", 409);
            }

            if (order.Total() <= 0)
            {
                throw new OrderPaymentException("Order total must be greater than zero.", 400);
            }

            var source = await ResolveCardSource(buyerId, card, paymentMethodId, ct);
            var currency = _settings.Currency;
            var createRequestId = order.Payment.CreateRequestId ?? $"eshop-order-{order.Id}-create-{Guid.NewGuid():N}";
            var authorizeRequestId = order.Payment.AuthorizeRequestId ?? $"eshop-order-{order.Id}-authorize-{Guid.NewGuid():N}";
            var invoiceId = order.Payment.InvoiceId ?? $"ESHOP-{order.Id}-{Guid.NewGuid():N}";
            order.Payment.EnsureCreateRequestId(createRequestId);
            order.Payment.EnsureAuthorizeRequestId(authorizeRequestId);
            order.Payment.EnsureInvoiceId(invoiceId);
            await _orders.UpdateAsync(order, ct);

            if (string.IsNullOrWhiteSpace(order.Payment.PayPalOrderId))
            {
                var payPalOrderId = await _payments.CreateOrderAsync(
                    invoiceId: invoiceId,
                    customId: order.Id.ToString(),
                    amount: order.Total(),
                    currency: currency,
                    createRequestId: createRequestId,
                    ct: ct);
                order.RecordPayPalOrderCreated(payPalOrderId, createRequestId, currency, invoiceId);
                await _orders.UpdateAsync(order, ct);
            }

            var hold = await _payments.AuthorizeExistingOrderAsync(
                order.Payment.PayPalOrderId!,
                source,
                authorizeRequestId,
                ct);

            order.MarkAuthorized(
                hold.PayPalOrderId,
                hold.AuthorizationId,
                hold.Status,
                hold.ExpirationTime,
                hold.CreateTime,
                createRequestId,
                authorizeRequestId,
                hold.Currency ?? currency);
            await _orders.UpdateAsync(order, ct);
            return Map(order);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ShopOrderResult> FulfilAsync(int orderId, CancellationToken ct)
    {
        var gate = LockFor(orderId);
        await gate.WaitAsync(ct);
        try
        {
            var order = await Load(orderId, ct);

            if (order.Status is ShopOrderStatus.Fulfilled or ShopOrderStatus.PartiallyRefunded or ShopOrderStatus.Refunded)
            {
                return Map(order);
            }

            if (order.Status == ShopOrderStatus.Cancelled)
            {
                throw new OrderPaymentException("A cancelled order cannot be fulfilled.", 409);
            }

            if (order.Status != ShopOrderStatus.Authorized || string.IsNullOrWhiteSpace(order.Payment.AuthorizationId))
            {
                throw new OrderPaymentException("The order must be authorized before it can be fulfilled.", 409);
            }

            var authorizationId = order.Payment.AuthorizationId;
            var snapshot = await _payments.GetAuthorizationAsync(authorizationId, ct);
            var status = snapshot.Status.ToUpperInvariant();

            if (status is "VOIDED" or "DENIED")
            {
                throw new OrderPaymentException(
                    $"Authorization {authorizationId} is {snapshot.Status} and cannot be captured. The shopper must pay again.",
                    409);
            }

            if (status == "CAPTURED")
            {
                if (!string.IsNullOrWhiteSpace(order.Payment.CaptureId))
                {
                    var existing = await _payments.GetCaptureAsync(order.Payment.CaptureId, ct);
                    order.MarkFulfilled(
                        existing.CaptureId,
                        existing.Status,
                        existing.GrossAmount,
                        existing.PaypalFee,
                        existing.NetAmount,
                        existing.Currency,
                        order.Payment.CaptureRequestId ?? $"eshop-order-{order.Id}-capture-{Guid.NewGuid():N}");
                    await _orders.UpdateAsync(order, ct);
                    return Map(order);
                }

                throw new OrderPaymentException(
                    "Authorization is already captured but this order has no capture id. Inspect PayPal for the capture and retry.",
                    409);
            }

            if (IsStale(snapshot, order.Payment.OriginalAuthorizationTime))
            {
                var original = order.Payment.OriginalAuthorizationTime ?? snapshot.CreateTime ?? DateTimeOffset.UtcNow;
                if (DateTimeOffset.UtcNow - original >= ReauthWindow)
                {
                    throw new OrderPaymentException(
                        "The authorization is past the 30-day reauthorization window and can no longer be renewed. The shopper must pay again.",
                        409);
                }

                var reauthRequestId = $"eshop-order-{order.Id}-reauth-{authorizationId}";
                try
                {
                    var renewed = await _payments.ReauthorizeAsync(
                        authorizationId,
                        order.Total(),
                        _settings.Currency,
                        reauthRequestId,
                        ct);
                    order.MarkReauthorized(renewed.AuthorizationId, renewed.Status, renewed.ExpirationTime);
                    authorizationId = renewed.AuthorizationId;
                    await _orders.UpdateAsync(order, ct);
                }
                catch (PaymentGatewayException ex)
                {
                    throw new OrderPaymentException(
                        $"The authorization could not be renewed ({ex.ProviderName ?? "PayPal"}{(ex.Issue is null ? "" : $": {ex.Issue}")}). {ex.Message} The shopper may need to pay again.",
                        ex.StatusCode is >= 400 and < 500 ? ex.StatusCode : 409);
                }
            }

            var captureRequestId = order.Payment.CaptureRequestId ?? $"eshop-order-{order.Id}-capture-{Guid.NewGuid():N}";
            order.Payment.EnsureCaptureRequestId(captureRequestId);
            await _orders.UpdateAsync(order, ct);

            CaptureResult captured;
            try
            {
                captured = await _payments.CaptureAsync(
                    authorizationId,
                    captureRequestId,
                    order.Payment.InvoiceId,
                    ct);
            }
            catch (PaymentGatewayException ex) when (ex.StatusCode == 409)
            {
                var after = await _payments.GetAuthorizationAsync(authorizationId, ct);
                if (!string.Equals(after.Status, "CAPTURED", StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(order.Payment.CaptureId))
                {
                    throw;
                }

                captured = await _payments.GetCaptureAsync(order.Payment.CaptureId, ct);
            }

            if (string.Equals(captured.Status, "PENDING", StringComparison.OrdinalIgnoreCase))
            {
                captured = await _payments.GetCaptureAsync(captured.CaptureId, ct);
            }

            order.MarkFulfilled(
                captured.CaptureId,
                captured.Status,
                captured.GrossAmount,
                captured.PaypalFee,
                captured.NetAmount,
                captured.Currency,
                captureRequestId);
            await _orders.UpdateAsync(order, ct);
            return Map(order);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ShopOrderResult> CancelAsync(int orderId, CancellationToken ct)
    {
        var gate = LockFor(orderId);
        await gate.WaitAsync(ct);
        try
        {
            var order = await Load(orderId, ct);

            if (order.Status == ShopOrderStatus.Cancelled)
            {
                return Map(order);
            }

            if (order.Status is ShopOrderStatus.Fulfilled or ShopOrderStatus.PartiallyRefunded or ShopOrderStatus.Refunded)
            {
                throw new OrderPaymentException("A fulfilled order cannot be cancelled; refund the capture instead.", 409);
            }

            var voidRequestId = order.Payment.VoidRequestId ?? $"eshop-order-{order.Id}-void-{Guid.NewGuid():N}";
            order.Payment.EnsureVoidRequestId(voidRequestId);

            if (order.Status == ShopOrderStatus.Authorized && !string.IsNullOrWhiteSpace(order.Payment.AuthorizationId))
            {
                try
                {
                    var status = await _payments.VoidAsync(order.Payment.AuthorizationId, voidRequestId, ct);
                    order.MarkCancelled(status, voidRequestId);
                }
                catch (PaymentGatewayException ex) when (ex.StatusCode == 409)
                {
                    var snapshot = await _payments.GetAuthorizationAsync(order.Payment.AuthorizationId, ct);
                    if (!string.Equals(snapshot.Status, "VOIDED", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new OrderPaymentException(
                            $"The authorization could not be released ({snapshot.Status}).",
                            409);
                    }

                    order.MarkCancelled(snapshot.Status, voidRequestId);
                }
            }
            else
            {
                order.MarkCancelled(null, voidRequestId);
            }

            await _orders.UpdateAsync(order, ct);
            return Map(order);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ShopRefundResult> RefundAsync(
        string buyerId,
        int orderId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new OrderPaymentException("An idempotency key is required for refunds.", 400);
        }

        var gate = LockFor(orderId);
        await gate.WaitAsync(ct);
        try
        {
            var order = await LoadOwned(orderId, buyerId, ct);

            var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
            if (existing is not null)
            {
                return MapRefund(existing);
            }

            if (order.Status is not ShopOrderStatus.Fulfilled and not ShopOrderStatus.PartiallyRefunded)
            {
                throw new OrderPaymentException("Only a fulfilled order can be refunded.", 409);
            }

            if (string.IsNullOrWhiteSpace(order.Payment.CaptureId))
            {
                throw new OrderPaymentException("This order has no captured payment to refund.", 409);
            }

            var capture = await _payments.GetCaptureAsync(order.Payment.CaptureId, ct);
            if (string.Equals(capture.Status, "REFUNDED", StringComparison.OrdinalIgnoreCase))
            {
                throw new OrderPaymentException("This capture has already been refunded in full.", 409);
            }

            var remaining = order.RemainingRefundable();
            var refundAmount = amount ?? remaining;
            if (refundAmount <= 0)
            {
                throw new OrderPaymentException("Refund amount must be greater than zero.", 400);
            }

            if (refundAmount > remaining)
            {
                throw new OrderPaymentException(
                    $"Refund of {refundAmount:0.00} exceeds remaining refundable amount {remaining:0.00}.",
                    400);
            }

            var result = await _payments.RefundAsync(
                order.Payment.CaptureId,
                amount.HasValue ? refundAmount : null,
                order.Payment.Currency ?? _settings.Currency,
                idempotencyKey,
                ct);

            var recorded = order.AddRefund(
                result.RefundId,
                result.Status,
                result.Amount == 0 ? refundAmount : result.Amount,
                result.Currency ?? _settings.Currency,
                idempotencyKey,
                result.TotalRefundedAmount);
            await _orders.UpdateAsync(order, ct);
            return MapRefund(recorded);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<ShopOrderResult>> ListMineAsync(string buyerId, CancellationToken ct)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), ct);
        return orders.Select(Map).ToList();
    }

    private async Task<CardPaymentSource> ResolveCardSource(
        string buyerId,
        CardPaymentSource? card,
        int? paymentMethodId,
        CancellationToken ct)
    {
        if (paymentMethodId is not null && card is not null)
        {
            throw new OrderPaymentException("Provide either card details or a saved payment method, not both.", 400);
        }

        if (paymentMethodId is not null)
        {
            var saved = await _savedMethods.GetByIdAsync(paymentMethodId.Value, ct);
            if (saved is null || !string.Equals(saved.BuyerId, buyerId, StringComparison.OrdinalIgnoreCase))
            {
                throw new OrderPaymentException("Saved payment method not found.", 404);
            }

            return new CardPaymentSource
            {
                VaultId = saved.PayPalPaymentTokenId,
                UseStoredCredential = true
            };
        }

        if (card is null || string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry)
            || string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            throw new OrderPaymentException("Card details or a saved payment method is required.", 400);
        }

        return new CardPaymentSource
        {
            Name = card.Name,
            Number = card.Number.Replace(" ", string.Empty, StringComparison.Ordinal),
            Expiry = CardExpiryNormalizer.Normalize(card.Expiry),
            SecurityCode = card.SecurityCode,
            BillingAddress = card.BillingAddress
        };
    }

    private async Task<Order> Load(int orderId, CancellationToken ct)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), ct);
        if (order is null)
        {
            throw new OrderPaymentException("Order not found.", 404);
        }

        return order;
    }

    private async Task<Order> LoadOwned(int orderId, string buyerId, CancellationToken ct)
    {
        var order = await Load(orderId, ct);
        if (!order.BelongsTo(buyerId))
        {
            throw new OrderPaymentException("Order not found.", 404);
        }

        return order;
    }

    private static bool IsStale(AuthorizationSnapshot snapshot, DateTimeOffset? originalAuthorizationTime)
    {
        if (snapshot.ExpirationTime is DateTimeOffset expiry && expiry <= DateTimeOffset.UtcNow)
        {
            return true;
        }

        var start = originalAuthorizationTime ?? snapshot.CreateTime;
        return start is DateTimeOffset created && DateTimeOffset.UtcNow - created >= HonorPeriod;
    }

    private static SemaphoreSlim LockFor(int orderId) =>
        OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));

    private ShopOrderResult Map(Order order) => new()
    {
        OrderId = order.Id,
        Status = order.Status.ToString(),
        Total = order.Total(),
        Currency = order.Payment.Currency ?? _settings.Currency,
        OrderDate = order.OrderDate,
        PayPalOrderId = order.Payment.PayPalOrderId,
        AuthorizationId = order.Payment.AuthorizationId,
        AuthorizationStatus = order.Payment.AuthorizationStatus,
        AuthorizationExpiration = order.Payment.AuthorizationExpiration,
        CaptureId = order.Payment.CaptureId,
        CaptureStatus = order.Payment.CaptureStatus,
        CapturedAmount = order.Payment.CapturedAmount,
        PaypalFee = order.Payment.PaypalFee,
        NetAmount = order.Payment.NetAmount,
        RemainingRefundable = order.RemainingRefundable(),
        Items = order.OrderItems.Select(i => new ShopOrderItemResult
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Quantity = i.Units
        }).ToList(),
        Refunds = order.Refunds.Select(MapRefund).ToList()
    };

    private static ShopRefundResult MapRefund(OrderRefundRecord refund) => new()
    {
        RefundId = refund.Id,
        PayPalRefundId = refund.PayPalRefundId,
        Status = refund.Status,
        Amount = refund.Amount,
        Currency = refund.Currency
    };
}
