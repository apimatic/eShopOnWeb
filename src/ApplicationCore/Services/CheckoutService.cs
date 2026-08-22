using System.Collections.Generic;
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
    private static readonly Address DefaultShipTo = new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalog;
    private readonly IRepository<Entities.BuyerAggregate.Buyer> _buyers;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalGateway _paypal;
    private readonly IPaymentSettings _paymentSettings;

    public CheckoutService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalog,
        IRepository<Entities.BuyerAggregate.Buyer> buyers,
        IUriComposer uriComposer,
        IPayPalGateway paypal,
        IPaymentSettings paymentSettings)
    {
        _orders = orders;
        _catalog = catalog;
        _buyers = buyers;
        _uriComposer = uriComposer;
        _paypal = paypal;
        _paymentSettings = paymentSettings;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> items, Address? shipTo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
            throw new CheckoutException(401, "A signed-in shopper is required.");
        if (items is null || items.Count == 0)
            throw new CheckoutException(400, "The order must contain at least one catalog item.");

        foreach (var line in items)
        {
            if (line.CatalogItemId <= 0)
                throw new CheckoutException(400, "Each line must name a catalog item.");
            if (line.Quantity <= 0)
                throw new CheckoutException(400, "Each line quantity must be greater than zero.");
        }

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalog.ListAsync(new CatalogItemsSpecification(ids), ct);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var orderItems = new List<OrderItem>();
        foreach (var line in items)
        {
            if (!byId.TryGetValue(line.CatalogItemId, out var catalogItem))
                throw new CheckoutException(400, $"Catalog item {line.CatalogItemId} was not found.");

            var snapshot = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(snapshot, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipTo ?? DefaultShipTo, orderItems);
        await _orders.AddAsync(order, ct);
        return order;
    }

    public async Task<Order> PayAsync(int orderId, string buyerId, CardPaymentSource? card, string? paymentMethodId, CancellationToken ct)
    {
        var order = await GetOwnedOrder(orderId, buyerId, ct);

        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
            throw new CheckoutException(409, "This order was cancelled and cannot be paid.");
        if (order.PaymentStatus is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.Refunded or OrderPaymentStatus.PartiallyRefunded)
            throw new CheckoutException(409, "This order has already been fulfilled.");
        if (order.PaymentStatus == OrderPaymentStatus.Authorized && !string.IsNullOrEmpty(order.PayPalAuthorizationId))
            return order;

        if (!string.IsNullOrWhiteSpace(paymentMethodId) && card is not null)
            throw new CheckoutException(400, "Provide either card details or a saved payment method, not both.");

        string? vaultId = null;
        if (!string.IsNullOrWhiteSpace(paymentMethodId))
        {
            var buyer = await _buyers.FirstOrDefaultAsync(new BuyerByIdentitySpecification(buyerId), ct);
            var saved = buyer?.FindPaymentMethod(paymentMethodId);
            if (saved is null)
                throw new CheckoutException(404, "Saved payment method not found.");
            vaultId = saved.CardId;
        }
        else if (card is null)
        {
            throw new CheckoutException(400, "Provide card details or a saved payment method.");
        }

        var result = await _paypal.AuthorizePaymentAsync(
            new AuthorizeCommand(
                order.Id.ToString(),
                $"eshop-{order.Id}-{order.PaymentIdempotencyKey}",
                order.Total(),
                RequireCurrency(),
                card,
                vaultId),
            createIdempotencyKey: $"eshop-pay-{order.PaymentIdempotencyKey}",
            authorizeIdempotencyKey: $"eshop-auth-{order.PaymentIdempotencyKey}",
            ct);

        if (result.PayerActionRequired)
            throw new CheckoutException(409, "This card requires a shopper challenge that this API does not support. Use a card that can be processed without browser approval.");

        order.RecordAuthorization(
            result.PayPalOrderId,
            result.AuthorizationId,
            result.Status,
            MoneyFormat.Parse(result.AmountValue),
            Order.ParseExpiration(result.ExpirationTime));
        await _orders.UpdateAsync(order, ct);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken ct)
    {
        var order = await GetOrder(orderId, ct);

        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
            throw new CheckoutException(409, "Cannot fulfil a cancelled order.");
        if ((order.PaymentStatus is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.Refunded or OrderPaymentStatus.PartiallyRefunded)
            && !string.IsNullOrEmpty(order.PayPalCaptureId))
            return order;

        if (string.IsNullOrEmpty(order.PayPalAuthorizationId) || order.PaymentStatus != OrderPaymentStatus.Authorized)
            throw new CheckoutException(409, "This order has no authorization to capture. The shopper must pay first.");

        var auth = await _paypal.GetAuthorizationAsync(order.PayPalAuthorizationId, ct);

        if (IsUncapturable(auth.Status))
        {
            throw new CheckoutException(409,
                $"Authorization {auth.AuthorizationId} is {auth.Status} and cannot be captured or renewed. Have the shopper pay again, or cancel the order.")
            {
                ProviderName = auth.Status
            };
        }

        if (IsStale(auth))
        {
            try
            {
                auth = await _paypal.ReauthorizeAsync(
                    auth.AuthorizationId,
                    order.Total(),
                    RequireCurrency(),
                    $"eshop-reauth-{order.PaymentIdempotencyKey}",
                    ct);
                order.RecordAuthorization(
                    order.PayPalOrderId ?? string.Empty,
                    auth.AuthorizationId,
                    auth.Status,
                    MoneyFormat.Parse(auth.AmountValue),
                    Order.ParseExpiration(auth.ExpirationTime));
                await _orders.UpdateAsync(order, ct);
            }
            catch (CheckoutException ex)
            {
                throw new CheckoutException(409,
                    $"Authorization {order.PayPalAuthorizationId} has expired and could not be renewed. Capture it before it expires, or have the shopper pay again. {ex.Message}",
                    ex)
                {
                    ProviderName = ex.ProviderName,
                    ProviderDebugId = ex.ProviderDebugId,
                    Issues = ex.Issues
                };
            }
        }

        var capture = await _paypal.CaptureAsync(auth.AuthorizationId, $"eshop-{order.Id}-{order.PaymentIdempotencyKey}", $"eshop-fulfil-{order.PaymentIdempotencyKey}", ct);
        order.RecordCapture(
            capture.CaptureId,
            capture.Status,
            MoneyFormat.Parse(capture.CapturedAmount),
            capture.PaypalFee is null ? null : MoneyFormat.Parse(capture.PaypalFee),
            capture.NetAmount is null ? null : MoneyFormat.Parse(capture.NetAmount));
        await _orders.UpdateAsync(order, ct);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken ct)
    {
        var order = await GetOrder(orderId, ct);

        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
            return order;
        if (order.PaymentStatus is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.Refunded or OrderPaymentStatus.PartiallyRefunded)
            throw new CheckoutException(409, "A fulfilled order cannot be cancelled. Issue a refund instead.");

        if (!string.IsNullOrEmpty(order.PayPalAuthorizationId))
        {
            await _paypal.VoidAuthorizationAsync(order.PayPalAuthorizationId, $"eshop-cancel-{order.PaymentIdempotencyKey}", ct);
            order.RecordVoid();
        }
        else
        {
            order.MarkCancelledWithoutPayment();
        }

        await _orders.UpdateAsync(order, ct);
        return order;
    }

    public async Task<OrderRefund> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new CheckoutException(400, "A refund idempotency key is required.");

        var order = await GetOwnedOrder(orderId, buyerId, ct);

        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
            return existing;

        if (string.IsNullOrEmpty(order.PayPalCaptureId) || order.CapturedAmount is null)
            throw new CheckoutException(409, "This order has no captured payment to refund.");

        var remaining = order.RemainingRefundable();
        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0m)
            throw new CheckoutException(400, "Refund amount must be greater than zero.");
        if (refundAmount > remaining)
            throw new CheckoutException(400, $"Refund of {MoneyFormat.ToValue(refundAmount)} exceeds the remaining captured amount of {MoneyFormat.ToValue(remaining)}.");

        var result = await _paypal.RefundAsync(
            order.PayPalCaptureId,
            amount.HasValue ? refundAmount : null,
            RequireCurrency(),
            idempotencyKey,
            ct);

        var refund = order.RecordRefund(result.RefundId, result.Status, MoneyFormat.Parse(result.AmountValue), idempotencyKey);
        await _orders.UpdateAsync(order, ct);
        return refund;
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
            throw new CheckoutException(401, "A signed-in shopper is required.");
        return await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
    }

    private async Task<Order> GetOwnedOrder(int orderId, string buyerId, CancellationToken ct)
    {
        var order = await GetOrder(orderId, ct);
        if (!string.Equals(order.BuyerId, buyerId, System.StringComparison.Ordinal))
            throw new CheckoutException(404, "Order not found.");
        return order;
    }

    private async Task<Order> GetOrder(int orderId, CancellationToken ct)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        if (order is null)
            throw new CheckoutException(404, "Order not found.");
        return order;
    }

    private string RequireCurrency()
    {
        if (string.IsNullOrWhiteSpace(_paymentSettings.Currency))
            throw new CheckoutException(500, "PayPal:Currency is not configured.");
        return _paymentSettings.Currency;
    }

    private static bool IsUncapturable(string? status)
    {
        return status is not null && (
            status.Equals("VOIDED", System.StringComparison.OrdinalIgnoreCase) ||
            status.Equals("CAPTURED", System.StringComparison.OrdinalIgnoreCase) ||
            status.Equals("DENIED", System.StringComparison.OrdinalIgnoreCase) ||
            status.Equals("PARTIALLY_CAPTURED", System.StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsStale(AuthorizationSnapshot auth)
    {
        if (IsUncapturable(auth.Status)) return false;
        var expires = Order.ParseExpiration(auth.ExpirationTime);
        return expires is not null && expires <= System.DateTimeOffset.UtcNow;
    }
}
