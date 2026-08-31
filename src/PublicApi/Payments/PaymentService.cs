using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();
    private readonly CatalogContext _db;
    private readonly IPayPalClient _payPal;

    public PaymentService(CatalogContext db, IPayPalClient payPal)
    {
        _db = db;
        _payPal = payPal;
    }

    public async Task<OrderDto> PlaceOrderAsync(string buyerId, PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        if (request.Items is null || request.Items.Count == 0)
            throw BadRequest("ORDER_ITEMS_REQUIRED", "At least one catalog item is required.");
        if (request.ShippingAddress is null)
            throw BadRequest("SHIPPING_ADDRESS_REQUIRED", "A shipping address is required.");
        if (request.Items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
            throw BadRequest("INVALID_ORDER_ITEM", "Catalog item IDs and quantities must be positive.");

        var quantities = request.Items.GroupBy(x => x.CatalogItemId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity));
        var catalogItems = await _db.CatalogItems.Where(x => quantities.Keys.Contains(x.Id)).ToListAsync(cancellationToken);
        var missing = quantities.Keys.Except(catalogItems.Select(x => x.Id)).ToArray();
        if (missing.Length > 0)
            throw new PaymentApiException(404, "CATALOG_ITEM_NOT_FOUND", $"Catalog item(s) not found: {string.Join(", ", missing)}.");

        var orderItems = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri),
            decimal.Round(item.Price, 2, MidpointRounding.AwayFromZero), quantities[item.Id])).ToList();
        var address = request.ShippingAddress;
        var order = new Order(buyerId, new Address(address.Street, address.City, address.State,
            address.Country, address.ZipCode), orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(order);
    }

    public async Task<PaymentDto> PayAsync(string buyerId, int orderId, PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await LoadOrderAsync(orderId, cancellationToken);
            EnsureOwner(order, buyerId);
            if (order.Status == OrderStatus.Cancelled) throw Conflict("ORDER_CANCELLED", "A cancelled order cannot be paid.");
            if (order.Payment?.Status is PaymentStatus.Authorized or PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
                return ToPaymentDto(order.Payment);

            var hasCard = request.Card is not null;
            var hasSaved = request.PaymentMethodId.HasValue;
            if (hasCard == hasSaved)
                throw BadRequest("PAYMENT_SOURCE_REQUIRED", "Provide either card or paymentMethodId, but not both.");

            string? vaultId = null;
            if (hasSaved)
            {
                var saved = await _db.SavedPaymentMethods.SingleOrDefaultAsync(
                    x => x.Id == request.PaymentMethodId && x.BuyerId == buyerId, cancellationToken);
                if (saved is null)
                    throw new PaymentApiException(404, "PAYMENT_METHOD_NOT_FOUND", "The saved card does not exist or does not belong to this shopper.");
                vaultId = saved.PayPalTokenId;
            }

            if (request.Card is not null) ValidateCard(request.Card);
            var payment = order.StartPayment(_payPal.Currency);
            await _db.SaveChangesAsync(cancellationToken);

            if (payment.PayPalOrderId is null)
            {
                var payPalOrder = await CallPayPal(() => _payPal.CreateOrderAsync(order.Id, payment.InvoiceId, order.Total(), cancellationToken));
                payment.SetPayPalOrder(payPalOrder.Id, payPalOrder.Status);
                await _db.SaveChangesAsync(cancellationToken);
            }

            var authorization = await CallPayPal(() => _payPal.AuthorizeAsync(payment.PayPalOrderId!,
                request.Card, vaultId, payment.InvoiceId, cancellationToken));
            if (authorization.Amount != order.Total())
                throw Conflict("PAYPAL_AMOUNT_MISMATCH", "PayPal authorized an amount different from the order total; do not fulfil this order.");

            payment.SetAuthorization(authorization.Id, authorization.Status, authorization.CreatedAt, authorization.ExpiresAt);
            if (authorization.Status == "CREATED") order.MarkAuthorized();
            await _db.SaveChangesAsync(cancellationToken);
            return ToPaymentDto(payment);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<PaymentDto> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await LoadOrderAsync(orderId, cancellationToken);
            if (order.Status == OrderStatus.Fulfilled && order.Payment is not null) return ToPaymentDto(order.Payment);
            if (order.Status != OrderStatus.Authorized || order.Payment?.AuthorizationId is null)
                throw Conflict("ORDER_NOT_AUTHORIZED", "Authorize the order before fulfilment.");

            var payment = order.Payment;
            var remoteAuthorization = await CallPayPal(() => _payPal.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken));
            if (remoteAuthorization.Amount != payment.Amount)
                throw Conflict("PAYPAL_AUTHORIZATION_AMOUNT_MISMATCH",
                    "PayPal reports an authorization amount different from the order total; do not fulfil this order.");
            payment.SetAuthorizationStatus(remoteAuthorization.Status, remoteAuthorization.ExpiresAt);
            if (remoteAuthorization.Status is "DENIED" or "VOIDED")
                throw Conflict("AUTHORIZATION_NOT_CAPTURABLE", $"PayPal reports the authorization as {remoteAuthorization.Status}. Ask the shopper to pay again.");

            var honorPeriodExpired = (payment.AuthorizedAt ?? remoteAuthorization.CreatedAt) <= DateTimeOffset.UtcNow.AddDays(-3);
            if (honorPeriodExpired && remoteAuthorization.Status is ("CREATED" or "PENDING"))
            {
                try
                {
                    remoteAuthorization = await _payPal.ReauthorizeAsync(payment.AuthorizationId, payment.Amount, payment.InvoiceId, cancellationToken);
                }
                catch (PayPalApiException ex) when (ex.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.NotFound)
                {
                    throw Conflict("AUTHORIZATION_CANNOT_BE_RENEWED",
                        "The PayPal authorization is too old or no longer renewable. Ask the shopper to pay again before fulfilment.");
                }
                payment.SetAuthorization(remoteAuthorization.Id, remoteAuthorization.Status,
                    remoteAuthorization.CreatedAt, remoteAuthorization.ExpiresAt);
                await _db.SaveChangesAsync(cancellationToken);
            }

            var capture = await CallPayPal(() => _payPal.CaptureAsync(payment.AuthorizationId!, payment.Amount,
                payment.InvoiceId, cancellationToken));
            if (capture.Amount != payment.Amount)
                throw Conflict("PAYPAL_CAPTURE_AMOUNT_MISMATCH", "PayPal captured an amount different from the order total; reconcile this order manually.");

            payment.SetCapture(capture.Id, capture.Status, capture.Amount, capture.Fee, capture.Net, capture.CreatedAt);
            if (capture.Status == "COMPLETED") order.MarkFulfilled(capture.CreatedAt ?? DateTimeOffset.UtcNow);
            await _db.SaveChangesAsync(cancellationToken);
            return ToPaymentDto(payment);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<OrderDto> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await LoadOrderAsync(orderId, cancellationToken);
            if (order.Status == OrderStatus.Cancelled) return ToDto(order);
            if (order.Status == OrderStatus.Fulfilled || order.Payment?.CaptureId is not null)
                throw Conflict("ORDER_ALREADY_CAPTURED", "Captured funds cannot be cancelled; issue a refund instead.");

            if (order.Payment?.AuthorizationId is not null)
            {
                var authorization = await CallPayPal(() => _payPal.GetAuthorizationAsync(order.Payment.AuthorizationId, cancellationToken));
                if (authorization.Status is "CAPTURED" or "PARTIALLY_CAPTURED")
                    throw Conflict("PAYMENT_CAPTURED_AT_PAYPAL",
                        "PayPal reports captured funds for this authorization. Retry fulfilment to recover the capture details, then refund if needed.");
                if (authorization.Status is "CREATED" or "PENDING")
                    await CallPayPal(async () => { await _payPal.VoidAsync(order.Payment.AuthorizationId, order.Payment.InvoiceId, cancellationToken); return true; });
                order.Payment.SetAuthorizationStatus("VOIDED");
            }
            order.MarkCancelled(DateTimeOffset.UtcNow);
            await _db.SaveChangesAsync(cancellationToken);
            return ToDto(order);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<RefundDto> RefundAsync(string buyerId, int orderId, RefundOrderRequest request,
        CancellationToken cancellationToken)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await LoadOrderAsync(orderId, cancellationToken);
            EnsureOwner(order, buyerId);
            if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 128)
                throw BadRequest("INVALID_IDEMPOTENCY_KEY", "idempotencyKey is required and must be at most 128 characters.");
            if (request.Note?.Length > 255)
                throw BadRequest("INVALID_REFUND_NOTE", "note must be at most 255 characters.");

            var payment = order.Payment;
            if (order.Status != OrderStatus.Fulfilled || payment?.CaptureId is null || !payment.CapturedAmount.HasValue)
                throw Conflict("ORDER_NOT_CAPTURED", "Only a fulfilled order with captured funds can be refunded.");

            var key = request.IdempotencyKey.Trim();
            var existing = payment.Refunds.SingleOrDefault(x => x.IdempotencyKey == key);
            if (existing is not null) return ToRefundDto(existing);

            var remaining = payment.CapturedAmount.Value - payment.RefundedAmount;
            var amount = request.Amount ?? remaining;
            if (amount <= 0 || amount > remaining)
                throw Conflict("REFUND_EXCEEDS_REMAINING_CAPTURE", $"The maximum refundable amount is {remaining:0.00} {payment.Currency}.");

            var remote = await CallPayPal(() => _payPal.RefundAsync(payment.CaptureId, amount, payment.InvoiceId,
                key, request.Note, cancellationToken));
            if (remote.Amount != amount)
                throw Conflict("PAYPAL_REFUND_AMOUNT_MISMATCH", "PayPal refunded an unexpected amount; reconcile this order manually.");
            var refund = payment.AddRefund(key, remote.Id, remote.Status, remote.Amount, remote.CreatedAt);
            await _db.SaveChangesAsync(cancellationToken);
            return ToRefundDto(refund);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<OrderDto>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _db.Orders.AsNoTracking().Where(x => x.BuyerId == buyerId)
            .Include(x => x.OrderItems).Include(x => x.Payment!).ThenInclude(x => x.Refunds)
            .OrderByDescending(x => x.OrderDate).ToListAsync(cancellationToken);
        return orders.Select(ToDto).ToList();
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.Include(x => x.OrderItems).Include(x => x.Payment!).ThenInclude(x => x.Refunds)
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        return order ?? throw new PaymentApiException(404, "ORDER_NOT_FOUND", "The order does not exist.");
    }

    private static void EnsureOwner(Order order, string buyerId)
    {
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            throw new PaymentApiException(404, "ORDER_NOT_FOUND", "The order does not exist.");
    }

    private static async Task<T> CallPayPal<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch (PayPalApiException ex) when (ex.RequiresPayerAction)
        {
            throw Conflict("PAYPAL_PAYER_ACTION_REQUIRED", "PayPal requires a browser challenge for this card. This direct-card API flow cannot continue.");
        }
        catch (PayPalApiException ex)
        {
            var suffix = ex.DebugId is null ? string.Empty : $" PayPal debug ID: {ex.DebugId}.";
            throw new PaymentApiException((int)(ex.StatusCode is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError
                    ? ex.StatusCode : HttpStatusCode.BadGateway),
                ex.Issue ?? ex.Name ?? "PAYPAL_ERROR", ex.Message + suffix);
        }
    }

    internal static void ValidateCard(CardInput card)
    {
        if (card.BillingAddress is null)
            throw BadRequest("INVALID_CARD", "A billing address is required.");
        var number = card.Number.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        if (number.Length is < 13 or > 19 || number.Any(x => !char.IsDigit(x)))
            throw BadRequest("INVALID_CARD", "Card number must contain 13 to 19 digits.");
        if (!DateOnly.TryParseExact(card.Expiry + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var expiry) || expiry < new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1))
            throw BadRequest("INVALID_CARD", "Card expiry must be a current or future month in YYYY-MM format.");
        if (card.SecurityCode.Length is < 3 or > 4 || card.SecurityCode.Any(x => !char.IsDigit(x)))
            throw BadRequest("INVALID_CARD", "Card securityCode must contain three or four digits.");
        if (string.IsNullOrWhiteSpace(card.Name) || string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode))
            throw BadRequest("INVALID_CARD", "Cardholder name and billing address are required.");
    }

    public static OrderDto ToDto(Order order) => new(order.Id, order.OrderDate, order.Status.ToString(),
        order.Total(), order.Payment?.Currency, order.OrderItems.Select(x => new OrderItemDto(x.ItemOrdered.CatalogItemId,
            x.ItemOrdered.ProductName, x.UnitPrice, x.Units)).ToList(), order.Payment is null ? null : ToPaymentDto(order.Payment));

    public static PaymentDto ToPaymentDto(OrderPayment payment) => new(payment.Status.ToString(), payment.Amount,
        payment.Currency, payment.PayPalOrderId, payment.PayPalOrderStatus, payment.AuthorizationId,
        payment.AuthorizationStatus, payment.AuthorizationExpiresAt, payment.CaptureId, payment.CaptureStatus,
        payment.CapturedAmount, payment.PayPalFee, payment.NetAmount, payment.RefundedAmount,
        payment.Refunds.Select(ToRefundDto).ToList());

    public static RefundDto ToRefundDto(PaymentRefund refund) =>
        new(refund.PayPalRefundId, refund.Status, refund.Amount, refund.CreatedAt, refund.IdempotencyKey);
    private static PaymentApiException BadRequest(string code, string message) => new(400, code, message);
    private static PaymentApiException Conflict(string code, string message) => new(409, code, message);
}

public sealed record PlaceOrderRequest(IReadOnlyList<PlaceOrderItemRequest> Items, ShippingAddressRequest ShippingAddress);
public sealed record PlaceOrderItemRequest(int CatalogItemId, int Quantity);
public sealed record ShippingAddressRequest(string Street, string City, string State, string Country, string ZipCode);
public sealed record PayOrderRequest(CardInput? Card, int? PaymentMethodId);
public sealed record RefundOrderRequest(decimal? Amount, string IdempotencyKey, string? Note);
public sealed record OrderItemDto(int CatalogItemId, string ProductName, decimal UnitPrice, int Quantity);
public sealed record OrderDto(int OrderId, DateTimeOffset OrderDate, string Status, decimal Total, string? Currency,
    IReadOnlyList<OrderItemDto> Items, PaymentDto? Payment);
public sealed record PaymentDto(string Status, decimal Amount, string Currency, string? PayPalOrderId,
    string? PayPalOrderStatus, string? AuthorizationId, string? AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiresAt, string? CaptureId, string? CaptureStatus, decimal? CapturedAmount,
    decimal? PayPalFee, decimal? NetAmount, decimal RefundedAmount, IReadOnlyList<RefundDto> Refunds);
public sealed record RefundDto(string RefundId, string Status, decimal Amount, DateTimeOffset CreatedAt, string IdempotencyKey);
