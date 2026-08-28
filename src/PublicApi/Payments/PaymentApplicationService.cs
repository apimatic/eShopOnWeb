using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentApplicationService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();
    private readonly CatalogContext _db;
    private readonly IPayPalGateway _payPal;
    private readonly PayPalOptions _options;

    public PaymentApplicationService(CatalogContext db, IPayPalGateway payPal, IOptions<PayPalOptions> options)
    {
        _db = db;
        _payPal = payPal;
        _options = options.Value;
    }

    public async Task<CreateOrderResponse> PlaceOrderAsync(string owner, PlaceOrderRequest request, CancellationToken ct)
    {
        if (request.Items.Count == 0) throw BadRequest("At least one catalog item is required.");
        if (request.Items.Any(x => x.CatalogItemId <= 0 || x.Quantity is <= 0 or > 100))
            throw BadRequest("Catalog item identifiers and quantities must be positive; quantity may not exceed 100.");

        var requested = request.Items.GroupBy(x => x.CatalogItemId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity));
        if (requested.Values.Any(x => x > 100)) throw BadRequest("The combined quantity of an item may not exceed 100.");
        var catalog = await _db.CatalogItems.Where(x => requested.Keys.Contains(x.Id)).ToListAsync(ct);
        if (catalog.Count != requested.Count) throw BadRequest("One or more catalog items do not exist.");

        var items = catalog.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name,
                string.IsNullOrWhiteSpace(item.PictureUri) ? "eCatalog-item-default.png" : item.PictureUri),
            item.Price, requested[item.Id])).ToList();
        var total = items.Sum(x => x.UnitPrice * x.Units);
        if (total != decimal.Round(total, 2, MidpointRounding.AwayFromZero))
            throw new PaymentApiException(HttpStatusCode.Conflict, "The catalog produced a total that cannot be represented to the cent.");

        var address = request.ShippingAddress;
        var order = new Order(owner,
            new Address(address.Street, address.City, address.State, address.Country, address.ZipCode),
            items, _options.Currency);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);
        return new CreateOrderResponse(order.Id, order.PaymentStatus.ToString(), order.OrderTotal, order.Currency);
    }

    public async Task<PayOrderResponse> PayAsync(string owner, int orderId, PayOrderRequest request, CancellationToken ct)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var order = await OwnedOrder(owner, orderId, ct);
            if (order.PaymentStatus is OrderPaymentStatus.Authorized or OrderPaymentStatus.Captured or
                OrderPaymentStatus.CapturePending or OrderPaymentStatus.RefundPending or
                OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded)
                return PayResponse(order);
            if (order.PaymentStatus == OrderPaymentStatus.Cancelled) throw Conflict("A cancelled order cannot be paid.");
            if ((request.Card is null) == string.IsNullOrWhiteSpace(request.PaymentMethodId))
                throw BadRequest("Supply exactly one of card or paymentMethodId.");

            string? vaultId = null;
            if (!string.IsNullOrWhiteSpace(request.PaymentMethodId))
            {
                var method = await _db.SavedPaymentMethods.SingleOrDefaultAsync(x =>
                    x.OwnerId == owner && x.PayPalTokenId == request.PaymentMethodId && x.IsActive, ct);
                if (method is null) throw NotFound("The payment method was not found.");
                vaultId = method.PayPalTokenId;
            }

            var paymentCorrelation = order.EnsurePaymentCorrelationId();
            var createKey = order.PayPalCreateRequestId ?? $"eshop-order-{paymentCorrelation}-create";
            var authorizeKey = order.PayPalAuthorizeRequestId ?? $"eshop-order-{paymentCorrelation}-authorize";
            order.ReservePayment(createKey, authorizeKey, vaultId is null ? "card" : "saved-card");
            await _db.SaveChangesAsync(ct);

            var result = await _payPal.AuthorizeAsync(order.Id, order.OrderTotal, order.Currency,
                createKey, authorizeKey, request.Card is null ? null : Card(request.Card), vaultId,
                order.PayPalOrderId, ct);
            EnsureMoney(result.Amount, result.Currency, order.OrderTotal, order.Currency, "authorization");
            order.RecordPayPalOrder(result.PayPalOrderId, result.OrderStatus);
            order.RecordAuthorization(result.AuthorizationId, result.AuthorizationStatus, result.Amount,
                result.ExpirationTime, result.CreateTime);
            await _db.SaveChangesAsync(ct);
            return PayResponse(order);
        }
        finally { gate.Release(); }
    }

    public async Task<FulfilOrderResponse> FulfilAsync(int orderId, CancellationToken ct)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var order = await AnyOrder(orderId, ct);
            if (order.PaymentStatus is OrderPaymentStatus.Captured or OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded)
                return FulfilResponse(order);
            if (order.PaymentStatus == OrderPaymentStatus.Cancelled) throw Conflict("A cancelled order cannot be fulfilled.");
            if (string.IsNullOrWhiteSpace(order.AuthorizationId)) throw Conflict("The order has no payment authorization to capture.");

            if (!string.IsNullOrWhiteSpace(order.CaptureId))
            {
                var existingCapture = await _payPal.GetCaptureAsync(order.CaptureId, ct);
                EnsureMoney(existingCapture.Amount, existingCapture.Currency, order.OrderTotal, order.Currency, "capture");
                order.RecordCapture(existingCapture.CaptureId, existingCapture.Status, existingCapture.Amount,
                    existingCapture.PayPalFee, existingCapture.NetAmount);
                await _db.SaveChangesAsync(ct);
                return FulfilResponse(order);
            }

            var authorization = await _payPal.GetAuthorizationAsync(order.AuthorizationId, ct);
            EnsureMoney(authorization.Amount, authorization.Currency, order.OrderTotal, order.Currency, "authorization");
            var authorizationId = authorization.AuthorizationId;
            var now = DateTimeOffset.UtcNow;
            var createdAt = authorization.CreateTime ?? order.AuthorizationCreatedAt;
            var outsideHonorPeriod = createdAt is not null && createdAt <= now.AddDays(-3);
            var stale = outsideHonorPeriod ||
                (authorization.ExpirationTime is not null && authorization.ExpirationTime <= now.AddMinutes(1));
            if (stale)
            {
                if (createdAt is null || createdAt < now.AddDays(-29))
                    throw Conflict("The authorization expired and can no longer be renewed. The shopper must pay again.");
                var renewalKey = order.ReserveReauthorization();
                await _db.SaveChangesAsync(ct);
                authorization = await _payPal.ReauthorizeAsync(authorizationId, order.OrderTotal,
                    order.Currency, renewalKey, ct);
                EnsureMoney(authorization.Amount, authorization.Currency, order.OrderTotal, order.Currency, "renewed authorization");
                authorizationId = authorization.AuthorizationId;
                order.RecordAuthorization(authorization.AuthorizationId, authorization.Status, authorization.Amount,
                    authorization.ExpirationTime, authorization.CreateTime);
                await _db.SaveChangesAsync(ct);
            }

            var captureKey = order.ReserveCapture();
            await _db.SaveChangesAsync(ct);
            var capture = await _payPal.CaptureAsync(authorizationId, order.Id, order.OrderTotal,
                order.Currency, captureKey, ct);
            EnsureMoney(capture.Amount, capture.Currency, order.OrderTotal, order.Currency, "capture");
            if (capture.GrossAmount is not null && capture.GrossAmount != order.OrderTotal)
                throw Conflict("PayPal's gross capture amount does not equal the order total; reconciliation is required.");
            order.RecordCapture(capture.CaptureId, capture.Status, capture.Amount, capture.PayPalFee, capture.NetAmount);
            await _db.SaveChangesAsync(ct);
            return FulfilResponse(order);
        }
        finally { gate.Release(); }
    }

    public async Task<CancelOrderResponse> CancelAsync(int orderId, CancellationToken ct)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var order = await AnyOrder(orderId, ct);
            if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
                return new CancelOrderResponse(order.Id, order.PaymentStatus.ToString(), order.AuthorizationStatus);
            if (order.CaptureId is not null || order.PaymentStatus is OrderPaymentStatus.Captured or
                OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded)
                throw Conflict("A captured order cannot be cancelled; refund it instead.");
            string? providerStatus = order.AuthorizationStatus;
            if (!string.IsNullOrWhiteSpace(order.AuthorizationId))
            {
                var voidKey = order.ReserveVoid();
                await _db.SaveChangesAsync(ct);
                providerStatus = await _payPal.VoidAsync(order.AuthorizationId, voidKey, ct);
            }
            order.MarkCancelled(providerStatus);
            await _db.SaveChangesAsync(ct);
            return new CancelOrderResponse(order.Id, order.PaymentStatus.ToString(), order.AuthorizationStatus);
        }
        finally { gate.Release(); }
    }

    public async Task<RefundOrderResponse> RefundAsync(string owner, int orderId, RefundOrderRequest request,
        CancellationToken ct)
    {
        ValidateIdempotencyKey(request.IdempotencyKey);
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var order = await OwnedOrder(owner, orderId, ct);
            if (string.IsNullOrWhiteSpace(order.CaptureId) || order.CapturedAmount is null)
                throw Conflict("Only a captured order can be refunded.");
            var remaining = order.CapturedAmount.Value - order.RefundedAmount();
            var amount = request.Amount ?? remaining;
            if (amount <= 0 || amount > remaining) throw Conflict("The refund exceeds the remaining captured amount.");

            var refund = order.Refunds.SingleOrDefault(x => x.IdempotencyKey == request.IdempotencyKey)
                ?? order.ReserveRefund(request.IdempotencyKey, amount);
            if (refund.PayPalRefundId is not null) return RefundResponse(order, refund);
            if (refund.Amount != amount) throw Conflict("This idempotency key was already used with a different amount.");
            await _db.SaveChangesAsync(ct);

            var provider = await _payPal.RefundAsync(order.CaptureId, amount, order.Currency,
                request.Amount is null, request.IdempotencyKey, order.Id, ct);
            EnsureMoney(provider.Amount, provider.Currency, amount, order.Currency, "refund");
            refund.Complete(provider.RefundId, provider.Status, provider.Amount);
            order.RefreshRefundState();
            await _db.SaveChangesAsync(ct);
            return RefundResponse(order, refund);
        }
        finally { gate.Release(); }
    }

    public async Task<PaymentMethodResponse> SavePaymentMethodAsync(string owner, SavePaymentMethodRequest request,
        CancellationToken ct)
    {
        var existing = await _db.SavedPaymentMethods.Where(x => x.OwnerId == owner).FirstOrDefaultAsync(ct);
        var merchantCustomerId = MerchantCustomerId(owner);
        var result = await _payPal.SaveCardAsync(merchantCustomerId, existing?.PayPalCustomerId,
            Card(request.Card), $"eshop-vault-setup-{Guid.NewGuid():N}", $"eshop-vault-token-{Guid.NewGuid():N}", ct);
        var saved = new SavedPaymentMethod(owner, result.PaymentTokenId, result.CustomerId,
            merchantCustomerId, result.Brand, result.LastDigits, result.Expiry, result.Name);
        _db.SavedPaymentMethods.Add(saved);
        await _db.SaveChangesAsync(ct);
        return MethodResponse(saved);
    }

    public async Task<IReadOnlyList<PaymentMethodResponse>> ListPaymentMethodsAsync(string owner, CancellationToken ct)
    {
        var local = await _db.SavedPaymentMethods.Where(x => x.OwnerId == owner && x.IsActive).ToListAsync(ct);
        if (local.Count == 0) return Array.Empty<PaymentMethodResponse>();
        var providerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var customerId in local.Select(x => x.PayPalCustomerId).Distinct())
            providerIds.UnionWith(await _payPal.ListPaymentTokenIdsAsync(customerId, ct));
        foreach (var missing in local.Where(x => !providerIds.Contains(x.PayPalTokenId))) missing.Deactivate();
        if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync(ct);
        return local.Where(x => x.IsActive).Select(MethodResponse).ToList();
    }

    public async Task DeletePaymentMethodAsync(string owner, string paymentMethodId, CancellationToken ct)
    {
        var method = await _db.SavedPaymentMethods.SingleOrDefaultAsync(x =>
            x.OwnerId == owner && x.PayPalTokenId == paymentMethodId && x.IsActive, ct);
        if (method is null) throw NotFound("The payment method was not found.");
        await _payPal.DeletePaymentTokenAsync(method.PayPalTokenId, ct);
        method.Deactivate();
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<MyOrderResponse>> MyOrdersAsync(string owner, CancellationToken ct)
    {
        var orders = await _db.Orders.AsNoTracking().Where(x => x.BuyerId == owner)
            .Include(x => x.OrderItems).ThenInclude(x => x.ItemOrdered)
            .Include(x => x.Refunds).OrderByDescending(x => x.OrderDate).ToListAsync(ct);
        return orders.Select(OrderResponse).ToList();
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (from >= to) throw BadRequest("from must be earlier than to.");
        if (to - from > TimeSpan.FromDays(31)) throw BadRequest("The reconciliation range may not exceed 31 days.");
        var report = await _payPal.SearchTransactionsAsync(from, to, ct);
        var orders = await _db.Orders.AsNoTracking()
            .Where(x => x.PaymentUpdatedAt >= from && x.PaymentUpdatedAt <= to)
            .Include(x => x.Refunds).ToListAsync(ct);
        var local = orders.SelectMany(LocalRecords).ToList();
        var matched = new List<ReconciliationMatch>();
        var payPalOnly = new List<PayPalTransactionRecord>();
        var matchedLocalIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var transaction in report.Transactions)
        {
            var match = FindLocal(local, transaction);
            if (match is null) payPalOnly.Add(transaction);
            else
            {
                matchedLocalIds.Add(match.ProviderId);
                matched.Add(new ReconciliationMatch(match.OrderId, match.Kind, match.ProviderId, transaction));
            }
        }
        var recentEmpty = report.Transactions.Count == 0 && to > DateTimeOffset.UtcNow.AddHours(-3);
        var eShopOnly = recentEmpty ? new List<LocalPaymentRecord>() :
            local.Where(x => !matchedLocalIds.Contains(x.ProviderId)).ToList();
        return new ReconciliationResponse(from, to, !recentEmpty, recentEmpty, report.LastRefreshedAt,
            report.PagesRead, matched, payPalOnly, eShopOnly);
    }

    private async Task<Order> OwnedOrder(string owner, int id, CancellationToken ct)
    {
        var order = await _db.Orders.Include(x => x.OrderItems).ThenInclude(x => x.ItemOrdered)
            .Include(x => x.Refunds).SingleOrDefaultAsync(x => x.Id == id && x.BuyerId == owner, ct);
        return order ?? throw NotFound("The order was not found.");
    }

    private async Task<Order> AnyOrder(int id, CancellationToken ct)
    {
        var order = await _db.Orders.Include(x => x.OrderItems).ThenInclude(x => x.ItemOrdered)
            .Include(x => x.Refunds).SingleOrDefaultAsync(x => x.Id == id, ct);
        return order ?? throw NotFound("The order was not found.");
    }

    private static CardInput Card(CardRequestDto card) => new(card.Name, card.Number, card.Expiry,
        card.SecurityCode, new CardBillingAddress(card.BillingAddress.AddressLine1,
            card.BillingAddress.AddressLine2, card.BillingAddress.City, card.BillingAddress.State,
            card.BillingAddress.PostalCode, card.BillingAddress.CountryCode.ToUpperInvariant()));

    private static void EnsureMoney(decimal actual, string actualCurrency, decimal expected, string currency, string kind)
    {
        if (actual != expected || !string.Equals(actualCurrency, currency, StringComparison.OrdinalIgnoreCase))
            throw Conflict($"PayPal's {kind} amount does not equal the order amount; reconciliation is required.");
    }

    private static PayOrderResponse PayResponse(Order order) => new(order.Id, order.PaymentStatus.ToString(),
        order.AuthorizationId, order.AuthorizationStatus, order.AuthorizedAmount, order.Currency,
        order.AuthorizationExpiresAt);
    private static FulfilOrderResponse FulfilResponse(Order order) => new(order.Id,
        order.PaymentStatus.ToString(), order.FulfilledAt, order.CaptureId, order.CaptureStatus,
        order.CapturedAmount, order.PayPalFee, order.NetProceeds, order.Currency);
    private static RefundOrderResponse RefundResponse(Order order, PaymentRefund refund) => new(
        refund.PayPalRefundId ?? $"pending:{refund.IdempotencyKey}", order.Id,
        refund.PayPalStatus ?? refund.Status.ToString(), refund.Amount, refund.Currency,
        Math.Max(0, (order.CapturedAmount ?? 0) - order.RefundedAmount()));
    private static PaymentMethodResponse MethodResponse(SavedPaymentMethod method) => new(
        method.PayPalTokenId, method.Brand, method.LastDigits, method.Expiry, method.CardholderName);
    private static MyOrderResponse OrderResponse(Order order) => new(order.Id, order.OrderDate,
        order.OrderTotal, order.Currency, order.PaymentStatus.ToString(), order.AuthorizationStatus,
        order.CaptureStatus, order.CapturedAmount, order.PayPalFee, order.NetProceeds, order.RefundedAmount(),
        order.OrderItems.Select(x => new MyOrderItemResponse(x.ItemOrdered.CatalogItemId,
            x.ItemOrdered.ProductName, x.UnitPrice, x.Units)).ToList(),
        order.Refunds.Select(x => new MyRefundResponse(x.PayPalRefundId, x.PayPalStatus ?? x.Status.ToString(),
            x.Amount, x.CreatedAt)).ToList());

    private static IEnumerable<LocalPaymentRecord> LocalRecords(Order order)
    {
        if (order.CaptureId is not null)
            yield return new LocalPaymentRecord(order.Id, "capture", order.CaptureId,
                order.CapturedAmount ?? order.OrderTotal, order.Currency, order.CaptureStatus ?? "UNKNOWN", order.PaymentUpdatedAt);
        foreach (var refund in order.Refunds.Where(x => x.PayPalRefundId is not null))
            yield return new LocalPaymentRecord(order.Id, "refund", refund.PayPalRefundId!, refund.Amount,
                refund.Currency, refund.PayPalStatus ?? refund.Status.ToString(), refund.UpdatedAt);
    }

    private static LocalPaymentRecord? FindLocal(IReadOnlyList<LocalPaymentRecord> local, PayPalTransactionRecord transaction)
    {
        var ids = new[] { transaction.TransactionId, transaction.ReferenceId }.Where(x => !string.IsNullOrWhiteSpace(x));
        var direct = local.FirstOrDefault(x => ids.Contains(x.ProviderId, StringComparer.Ordinal));
        if (direct is not null) return direct;
        if (transaction.InvoiceId?.StartsWith("ESHOP-", StringComparison.OrdinalIgnoreCase) == true &&
            int.TryParse(transaction.InvoiceId[6..], NumberStyles.None, CultureInfo.InvariantCulture, out var orderId))
            return local.FirstOrDefault(x => x.OrderId == orderId);
        return null;
    }

    private static string MerchantCustomerId(string owner)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(owner));
        return "eshop-" + Convert.ToHexString(hash).ToLowerInvariant()[..32];
    }

    private static void ValidateIdempotencyKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128 || key.Any(char.IsControl))
            throw BadRequest("idempotencyKey must contain 1 to 128 non-control characters.");
    }

    private static PaymentApiException BadRequest(string message) => new(HttpStatusCode.BadRequest, message);
    private static PaymentApiException NotFound(string message) => new(HttpStatusCode.NotFound, message);
    private static PaymentApiException Conflict(string message) => new(HttpStatusCode.Conflict, message);
}
