using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();
    private readonly CatalogContext _db;
    private readonly IPayPalClient _payPal;
    private readonly PayPalOptions _options;

    public PaymentService(CatalogContext db, IPayPalClient payPal, IOptions<PayPalOptions> options)
    {
        _db = db;
        _payPal = payPal;
        _options = options.Value;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items is null || request.Items.Count == 0)
            throw BadRequest("EMPTY_ORDER", "At least one catalog item is required.");
        if (request.Items.Count > 100)
            throw BadRequest("TOO_MANY_ITEMS", "An order cannot contain more than 100 request lines.");
        if (request.ShippingAddress is null || string.IsNullOrWhiteSpace(request.ShippingAddress.Street)
            || string.IsNullOrWhiteSpace(request.ShippingAddress.City)
            || string.IsNullOrWhiteSpace(request.ShippingAddress.Country)
            || string.IsNullOrWhiteSpace(request.ShippingAddress.ZipCode))
            throw BadRequest("INVALID_SHIPPING_ADDRESS", "A complete shipping address is required.");

        var requested = request.Items.GroupBy(x => x.CatalogItemId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity));
        if (requested.Any(x => x.Key <= 0 || x.Value <= 0 || x.Value > 999))
            throw BadRequest("INVALID_QUANTITY", "Catalog item IDs and quantities must be positive; quantity cannot exceed 999.");
        var ids = requested.Keys.ToList();
        var catalogItems = await _db.CatalogItems.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
        if (catalogItems.Count != ids.Count)
        {
            var missing = ids.Except(catalogItems.Select(x => x.Id));
            throw BadRequest("CATALOG_ITEM_NOT_FOUND", $"Catalog item(s) {string.Join(", ", missing)} do not exist.");
        }

        var items = catalogItems.Select(x => new OrderItem(
            new CatalogItemOrdered(x.Id, x.Name, x.PictureUri), x.Price, requested[x.Id])).ToList();
        var shipping = request.ShippingAddress;
        var order = new Order(buyerId,
            new Address(shipping.Street, shipping.City, shipping.State ?? string.Empty,
                shipping.Country, shipping.ZipCode), items, _options.Currency.ToUpperInvariant());
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        return order;
    }

    public async Task<Order> PayAsync(int orderId, string buyerId, PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        using var operationLock = await AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await LoadOrderAsync(orderId, cancellationToken);
        EnsureOwner(order, buyerId);
        if (order.PaymentStatus == OrderPaymentStatus.Authorized) return order;
        if (order.PaymentStatus != OrderPaymentStatus.AwaitingPayment)
            throw Conflict("ORDER_NOT_PAYABLE", $"Order {orderId} is {order.PaymentStatus} and cannot be authorized.");
        if ((request.Card is null) == (request.PaymentMethodId is null))
            throw BadRequest("PAYMENT_SOURCE_REQUIRED", "Supply exactly one of card or paymentMethodId.");

        string? vaultId = null;
        if (request.PaymentMethodId is int paymentMethodId)
        {
            var method = await _db.PaymentMethods.SingleOrDefaultAsync(x => x.Id == paymentMethodId
                && x.BuyerId == buyerId && !x.IsDeleted, cancellationToken);
            if (method is null)
                throw NotFound("PAYMENT_METHOD_NOT_FOUND", "The saved payment method was not found or is no longer usable.");
            vaultId = method.PayPalPaymentTokenId;
        }

        var total = Money(order.Total());
        try
        {
            if (order.PayPalOrderId is null)
            {
                var paypalOrderId = await _payPal.CreateOrderAsync(order.Id, order.PaymentReference, total, order.Currency,
                    StableId($"{order.PaymentReference}:create"), cancellationToken);
                order.RecordPayPalOrder(paypalOrderId);
                await _db.SaveChangesAsync(cancellationToken);
            }
            var authorization = await _payPal.AuthorizeAsync(order.PayPalOrderId!, request.Card, vaultId,
                total, StableId($"{order.PaymentReference}:authorize"), cancellationToken);
            order.RecordAuthorization(authorization.AuthorizationId, authorization.Status,
                authorization.Amount, authorization.CreatedAt, authorization.ExpiresAt);
            await _db.SaveChangesAsync(cancellationToken);
            return order;
        }
        catch (PayPalException ex) { throw Translate(ex); }
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        using var operationLock = await AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order.FulfillmentStatus == OrderFulfillmentStatus.Fulfilled) return order;
        if (order.PaymentStatus != OrderPaymentStatus.Authorized || order.AuthorizationId is null
            || order.AuthorizedAmount is null || order.AuthorizedAt is null)
            throw Conflict("ORDER_NOT_AUTHORIZED", "The order must have an active authorization before fulfilment.");

        try
        {
            var now = DateTimeOffset.UtcNow;
            if (order.AuthorizedAt.Value <= now.AddDays(-3))
            {
                if (order.AuthorizationExpiresAt is not null && order.AuthorizationExpiresAt <= now)
                    throw Conflict("AUTHORIZATION_CANNOT_BE_RENEWED",
                        "The PayPal authorization is beyond its renewal window. Ask the shopper to place and authorize a replacement order.");
                try
                {
                    var renewed = await _payPal.ReauthorizeAsync(order.AuthorizationId, order.PayPalOrderId!,
                        order.AuthorizedAmount.Value, order.Currency, StableId($"{order.PaymentReference}:reauthorize"),
                        cancellationToken);
                    order.RecordAuthorization(renewed.AuthorizationId, renewed.Status, renewed.Amount,
                        renewed.CreatedAt, renewed.ExpiresAt);
                    await _db.SaveChangesAsync(cancellationToken);
                }
                catch (PayPalException ex)
                {
                    throw new PaymentApiException(409, "AUTHORIZATION_CANNOT_BE_RENEWED",
                        $"PayPal could not renew the authorization ({ex.Code}). Ask the shopper to place and authorize a replacement order. PayPal debug ID: {ex.DebugId ?? "unavailable"}.",
                        ex.DebugId, ex);
                }
            }

            var capture = await _payPal.CaptureAsync(order.AuthorizationId!, order.AuthorizedAmount.Value,
                order.Currency, StableId($"{order.PaymentReference}:capture"), cancellationToken);
            order.RecordCapture(capture.CaptureId, capture.Status, capture.Amount, capture.Fee,
                capture.Net, capture.CreatedAt);
            await _db.SaveChangesAsync(cancellationToken);
            return order;
        }
        catch (PayPalException ex) { throw Translate(ex); }
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        using var operationLock = await AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order.PaymentStatus == OrderPaymentStatus.Cancelled) return order;
        if (order.FulfillmentStatus == OrderFulfillmentStatus.Fulfilled || order.CaptureId is not null)
            throw Conflict("ORDER_ALREADY_CAPTURED", "A fulfilled order cannot be cancelled; refund its capture instead.");
        try
        {
            if (order.AuthorizationId is not null)
                await _payPal.VoidAsync(order.AuthorizationId, StableId($"{order.PaymentReference}:void"), cancellationToken);
            order.Cancel(order.AuthorizationId is null ? "NOT_AUTHORIZED" : "VOIDED");
            await _db.SaveChangesAsync(cancellationToken);
            return order;
        }
        catch (PayPalException ex) { throw Translate(ex); }
    }

    public async Task<OrderRefund> RefundAsync(int orderId, string buyerId, RefundOrderRequest request,
        CancellationToken cancellationToken)
    {
        using var operationLock = await AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await LoadOrderAsync(orderId, cancellationToken);
        EnsureOwner(order, buyerId);
        var existing = order.Refunds.SingleOrDefault(x => x.IdempotencyKey == request.IdempotencyKey);
        if (existing is not null) return existing;
        if (order.CaptureId is null || order.CapturedAmount is null
            || order.FulfillmentStatus != OrderFulfillmentStatus.Fulfilled)
            throw Conflict("ORDER_NOT_CAPTURED", "Only a fulfilled order with a capture can be refunded.");

        var remaining = Money(order.CapturedAmount.Value - order.RefundedAmount);
        var amount = request.Amount.HasValue ? Money(request.Amount.Value) : remaining;
        if (amount <= 0 || amount > remaining)
            throw BadRequest("INVALID_REFUND_AMOUNT",
                $"Refund amount must be positive and cannot exceed the remaining captured amount of {remaining:F2} {order.Currency}.");
        try
        {
            var paypalRefund = await _payPal.RefundAsync(order.CaptureId, amount, order.Currency,
                StableId($"refund:{order.CaptureId}:{request.IdempotencyKey}"), cancellationToken);
            if (paypalRefund.Amount != amount)
                throw new PaymentApiException(502, "INVALID_PAYPAL_RESPONSE",
                    $"PayPal refunded {paypalRefund.Amount:F2}, but {amount:F2} was requested.");
            var refund = order.AddRefund(request.IdempotencyKey, paypalRefund.RefundId,
                paypalRefund.Amount, paypalRefund.Status, paypalRefund.CreatedAt);
            await _db.SaveChangesAsync(cancellationToken);
            return refund;
        }
        catch (PayPalException ex) { throw Translate(ex); }
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, CardRequest card,
        CancellationToken cancellationToken)
    {
        using var operationLock = await AcquireAsync($"methods:{buyerId}", cancellationToken);
        try
        {
            var result = await _payPal.SaveCardAsync(MerchantCustomerId(buyerId), card,
                $"eshop-vault-{Guid.NewGuid():N}", cancellationToken);
            var method = new PaymentMethod(buyerId, result.PaymentTokenId, result.CustomerId,
                result.Brand, result.LastFour, result.Expiry, DateTimeOffset.UtcNow);
            _db.PaymentMethods.Add(method);
            await _db.SaveChangesAsync(cancellationToken);
            return method;
        }
        catch (PayPalException ex) { throw Translate(ex); }
    }

    public Task<List<PaymentMethod>> ListPaymentMethodsAsync(string buyerId, CancellationToken cancellationToken) =>
        _db.PaymentMethods.AsNoTracking().Where(x => x.BuyerId == buyerId && !x.IsDeleted)
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);

    public async Task DeletePaymentMethodAsync(int paymentMethodId, string buyerId,
        CancellationToken cancellationToken)
    {
        using var operationLock = await AcquireAsync($"method:{paymentMethodId}", cancellationToken);
        var method = await _db.PaymentMethods.SingleOrDefaultAsync(x => x.Id == paymentMethodId
            && x.BuyerId == buyerId && !x.IsDeleted, cancellationToken);
        if (method is null) throw NotFound("PAYMENT_METHOD_NOT_FOUND", "The saved payment method was not found.");
        try
        {
            await _payPal.DeletePaymentTokenAsync(method.PayPalPaymentTokenId, cancellationToken);
            method.MarkDeleted(DateTimeOffset.UtcNow);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (PayPalException ex) { throw Translate(ex); }
    }

    public Task<List<Order>> MyOrdersAsync(string buyerId, CancellationToken cancellationToken) =>
        _db.Orders.AsNoTracking().Include(x => x.OrderItems).Include(x => x.Refunds)
            .Where(x => x.BuyerId == buyerId).OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);

    public async Task<List<ReconciliationRow>> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from >= to) throw BadRequest("INVALID_DATE_RANGE", "'from' must be earlier than 'to'.");
        if (to - from > TimeSpan.FromDays(366 * 3))
            throw BadRequest("DATE_RANGE_TOO_LARGE", "The reconciliation range cannot exceed three years.");
        IReadOnlyList<PayPalTransaction> paypal;
        try { paypal = await _payPal.ListTransactionsAsync(from, to, cancellationToken); }
        catch (PayPalException ex) { throw Translate(ex); }

        var localOrders = await _db.Orders.AsNoTracking().Include(x => x.Refunds)
            .Where(x => (x.CapturedAt >= from && x.CapturedAt <= to)
                || x.Refunds.Any(r => r.CreatedAt >= from && r.CreatedAt <= to))
            .ToListAsync(cancellationToken);
        var local = localOrders.SelectMany(order =>
        {
            var entries = new List<LocalTransaction>();
            if (order.CaptureId is not null && order.CapturedAt >= from && order.CapturedAt <= to)
                entries.Add(new(order.Id, order.PaymentReference, order.CaptureId, "CAPTURE", order.CapturedAmount,
                    order.Currency, order.CapturedAt));
            entries.AddRange(order.Refunds.Where(x => x.CreatedAt >= from && x.CreatedAt <= to)
                .Select(x => new LocalTransaction(order.Id, order.PaymentReference, x.PayPalRefundId, "REFUND", -x.Amount,
                    order.Currency, x.CreatedAt)));
            return entries;
        }).ToList();

        var rows = new List<ReconciliationRow>();
        var matchedLocalIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var transaction in paypal)
        {
            var match = local.FirstOrDefault(x => x.PayPalTransactionId == transaction.TransactionId
                || string.Equals(x.PaymentReference, transaction.InvoiceId, StringComparison.OrdinalIgnoreCase));
            if (match is not null) matchedLocalIds.Add(match.PayPalTransactionId);
            rows.Add(new ReconciliationRow(match is null ? "PAYPAL_ONLY" : "MATCHED",
                match?.OrderId, transaction, match));
        }
        rows.AddRange(local.Where(x => !matchedLocalIds.Contains(x.PayPalTransactionId))
            .Select(x => new ReconciliationRow("ESHOP_ONLY", x.OrderId, null, x)));
        return rows.OrderBy(x => x.PayPal?.InitiatedAt ?? x.EShop?.OccurredAt).ToList();
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken) =>
        await _db.Orders.Include(x => x.OrderItems).Include(x => x.Refunds)
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken)
        ?? throw NotFound("ORDER_NOT_FOUND", $"Order {orderId} was not found.");

    private static void EnsureOwner(Order order, string buyerId)
    {
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            throw NotFound("ORDER_NOT_FOUND", $"Order {order.Id} was not found.");
    }

    public static OrderView View(Order order) => new(order.Id, order.OrderDate,
        order.FulfillmentStatus.ToString(), order.OrderItems.Select(x => new OrderItemView(
            x.ItemOrdered.CatalogItemId, x.ItemOrdered.ProductName, x.UnitPrice, x.Units)).ToList(),
        new PaymentView(order.PaymentStatus.ToString(), order.Currency, Money(order.Total()),
            order.PayPalOrderId, order.AuthorizationId, order.AuthorizationStatus, order.AuthorizedAmount,
            order.AuthorizationExpiresAt, order.CaptureId, order.CaptureStatus, order.CapturedAmount,
            order.PayPalFee, order.NetAmount, order.RefundedAmount));

    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static string MerchantCustomerId(string buyerId) => "eshop-" + StableId(buyerId)[..32];
    private static string StableId(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static PaymentApiException Translate(PayPalException ex) => new(
        ex.StatusCode >= 500 || ex.StatusCode == 429 ? 502 : 422, ex.Code,
        $"PayPal rejected the operation: {ex.Message} PayPal debug ID: {ex.DebugId ?? "unavailable"}.", ex.DebugId, ex);
    private static PaymentApiException BadRequest(string code, string message) => new(400, code, message);
    private static PaymentApiException NotFound(string code, string message) => new(404, code, message);
    private static PaymentApiException Conflict(string code, string message) => new(409, code, message);

    private static async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        var semaphore = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new LockReleaser(semaphore);
    }

    private sealed class LockReleaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        public LockReleaser(SemaphoreSlim semaphore) => _semaphore = semaphore;
        public void Dispose() => _semaphore.Release();
    }
}

public sealed record LocalTransaction(int OrderId, string PaymentReference, string PayPalTransactionId, string Type,
    decimal? Amount, string Currency, DateTimeOffset? OccurredAt);
public sealed record ReconciliationRow(string MatchStatus, int? OrderId, PayPalTransaction? PayPal,
    LocalTransaction? EShop);
