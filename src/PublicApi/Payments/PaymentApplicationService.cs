using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentApplicationService
{
    private readonly CatalogContext _db;
    private readonly IPayPalPaymentGateway _payPal;
    private readonly IUriComposer _uriComposer;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();

    public PaymentApplicationService(CatalogContext db, IPayPalPaymentGateway payPal, IUriComposer uriComposer)
    {
        _db = db;
        _payPal = payPal;
        _uriComposer = uriComposer;
    }

    public async Task<OrderView> PlaceOrderAsync(string ownerId, PlaceOrderRequest request, CancellationToken ct)
    {
        if (request.Items.Count == 0 || request.Items.Any(i => i.CatalogItemId <= 0 || i.Quantity <= 0))
            throw BadRequest("An order requires catalog item ids with positive quantities.");
        ValidateShipping(request.ShipToAddress);

        var quantities = request.Items.GroupBy(i => i.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
        var catalogItems = await _db.CatalogItems.Where(i => quantities.Keys.Contains(i.Id)).ToListAsync(ct);
        if (catalogItems.Count != quantities.Count)
            throw new PaymentApplicationException(404, "Catalog item not found", "One or more catalog item ids do not exist.");

        var items = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, _uriComposer.ComposePicUri(item.PictureUri)),
            item.Price, quantities[item.Id])).ToList();
        var address = request.ShipToAddress;
        var order = new Order(ownerId, new Address(address.Street, address.City, address.State,
            address.Country, address.ZipCode), items);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);
        return View(order);
    }

    public async Task<OrderView> PayAsync(string ownerId, int orderId, PayOrderRequest request, CancellationToken ct)
    {
        var hasCard = request.Card is not null;
        var hasSaved = request.PaymentMethodId.HasValue;
        if (hasCard == hasSaved) throw BadRequest("Supply either card or paymentMethodId, but not both.");
        if (request.Card is not null) ValidateCard(request.Card);

        return await WithGate($"order:{orderId}", ct, async () =>
        {
            var order = await GetOrderAsync(orderId, ct);
            EnsureOwner(order, ownerId);
            if (order.FulfilmentStatus != OrderFulfilmentStatus.Unfulfilled)
                throw Conflict("Only an unfulfilled order can be paid.");
            if (order.PaymentStatus is OrderPaymentStatus.Authorized or OrderPaymentStatus.AuthorizationPending or
                OrderPaymentStatus.Captured or OrderPaymentStatus.CapturePending or
                OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded)
                return View(order);
            if (order.PaymentStatus == OrderPaymentStatus.PayerActionRequired)
                throw Conflict(order.PaymentFailureReason ?? "PayPal requires browser approval.");

            string? vaultedToken = null;
            if (request.PaymentMethodId.HasValue)
            {
                var method = await _db.PaymentMethods.SingleOrDefaultAsync(p => p.Id == request.PaymentMethodId.Value &&
                    p.OwnerId == ownerId && p.DeletedAt == null, ct);
                if (method is null) throw new PaymentApplicationException(404, "Payment method not found",
                    "The saved payment method does not exist or is not owned by this shopper.");
                vaultedToken = method.PayPalPaymentTokenId;
            }

            var total = decimal.Round(order.Total(), 2, MidpointRounding.AwayFromZero);
            var invoiceId = InvoiceId(order);
            try
            {
                if (string.IsNullOrWhiteSpace(order.PayPalOrderId))
                {
                    var payPalOrder = await _payPal.CreateOrderAsync(total, invoiceId,
                        $"{invoiceId}-create", ct);
                    order.RecordPayPalOrder(payPalOrder.Id, payPalOrder.Status, _payPal.Currency);
                    await _db.SaveChangesAsync(ct);
                }

                var authorization = await _payPal.AuthorizeAsync(order.PayPalOrderId!, total,
                    request.Card?.ToInput(), vaultedToken, $"{invoiceId}-authorize", ct);
                if (authorization.PayerActionRequired)
                {
                    order.RecordPaymentChallenge(authorization.PayPalOrderStatus);
                    await _db.SaveChangesAsync(ct);
                    throw new PaymentApplicationException(409, "PayPal approval required",
                        "PayPal requires a browser approval challenge. This API intentionally does not implement an approval round-trip.");
                }
                if (string.IsNullOrWhiteSpace(authorization.Id))
                    throw new PayPalProviderException(502, "PayPal did not return an authorization id.");
                if (authorization.Status is not ("CREATED" or "PENDING"))
                    throw new PayPalProviderException(422, $"PayPal returned authorization status {authorization.Status}.");

                order.RecordAuthorization(authorization.Id, authorization.Status, authorization.Amount,
                    authorization.CreatedAt, authorization.ExpiresAt, authorization.StatusReason);
                await _db.SaveChangesAsync(ct);
                return View(order);
            }
            catch (PayPalProviderException ex)
            {
                order.RecordPaymentFailure(ex.Message);
                await _db.SaveChangesAsync(ct);
                throw Provider(ex);
            }
        });
    }

    public async Task<OrderView> FulfilAsync(int orderId, CancellationToken ct) =>
        await WithGate($"order:{orderId}", ct, async () =>
        {
            var order = await GetOrderAsync(orderId, ct);
            if (order.FulfilmentStatus == OrderFulfilmentStatus.Cancelled) throw Conflict("A cancelled order cannot be fulfilled.");
            try
            {
                if (!string.IsNullOrWhiteSpace(order.CaptureId))
                {
                    var existing = await _payPal.GetCaptureAsync(order.CaptureId, ct);
                    order.RecordCapture(existing.Id, existing.Status, existing.Amount, existing.PayPalFee,
                        existing.NetAmount, existing.CreatedAt, existing.StatusReason);
                    await _db.SaveChangesAsync(ct);
                    return View(order);
                }
                if (string.IsNullOrWhiteSpace(order.AuthorizationId) ||
                    order.PaymentStatus is not (OrderPaymentStatus.Authorized or OrderPaymentStatus.AuthorizationPending))
                    throw Conflict("The order must have an active PayPal authorization before fulfilment.");

                var total = decimal.Round(order.Total(), 2, MidpointRounding.AwayFromZero);
                var authorization = await _payPal.GetAuthorizationAsync(order.AuthorizationId, ct);
                if (authorization.CreatedAt.HasValue && authorization.CreatedAt.Value <= DateTimeOffset.UtcNow.AddDays(-3))
                {
                    if (authorization.CreatedAt.Value <= DateTimeOffset.UtcNow.AddDays(-29) ||
                        authorization.ExpiresAt <= DateTimeOffset.UtcNow)
                    {
                        const string reason = "The authorization is outside PayPal's renewable period; ask the shopper to authorize payment again.";
                        order.MarkAuthorizationExpired(reason);
                        await _db.SaveChangesAsync(ct);
                        throw Conflict(reason);
                    }

                    try
                    {
                        authorization = await _payPal.ReauthorizeAsync(order.AuthorizationId, total,
                            $"eshop-order-{order.Id}-reauthorize-{order.AuthorizationId}", ct);
                        order.RecordAuthorization(authorization.Id!, authorization.Status, authorization.Amount,
                            authorization.CreatedAt, authorization.ExpiresAt, authorization.StatusReason);
                        await _db.SaveChangesAsync(ct);
                    }
                    catch (PayPalProviderException ex) when (ex.StatusCode is >= 400 and < 500)
                    {
                        const string reason = "PayPal can no longer renew this authorization; ask the shopper to authorize payment again.";
                        order.MarkAuthorizationExpired(reason);
                        await _db.SaveChangesAsync(ct);
                        throw Conflict(reason);
                    }
                }

                var capture = await _payPal.CaptureAsync(order.AuthorizationId!, total,
                    $"eshop-auth-{order.AuthorizationId}-capture", ct);
                order.RecordCapture(capture.Id, capture.Status, capture.Amount, capture.PayPalFee,
                    capture.NetAmount, capture.CreatedAt, capture.StatusReason);
                await _db.SaveChangesAsync(ct);
                return View(order);
            }
            catch (PayPalProviderException ex)
            {
                throw Provider(ex);
            }
        });

    public async Task<OrderView> CancelAsync(int orderId, CancellationToken ct) =>
        await WithGate($"order:{orderId}", ct, async () =>
        {
            var order = await GetOrderAsync(orderId, ct);
            if (order.FulfilmentStatus == OrderFulfilmentStatus.Cancelled) return View(order);
            if (order.FulfilmentStatus == OrderFulfilmentStatus.Fulfilled || !string.IsNullOrWhiteSpace(order.CaptureId))
                throw Conflict("A fulfilled or captured order must be refunded, not cancelled.");
            try
            {
                if (string.IsNullOrWhiteSpace(order.AuthorizationId)) order.CancelWithoutAuthorization();
                else
                {
                    var result = await _payPal.VoidAsync(order.AuthorizationId,
                        $"eshop-auth-{order.AuthorizationId}-void", ct);
                    order.RecordVoid(result.Status);
                }
                await _db.SaveChangesAsync(ct);
                return View(order);
            }
            catch (PayPalProviderException ex) { throw Provider(ex); }
        });

    public async Task<RefundView> RefundAsync(string ownerId, int orderId, CreateRefundRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey)) throw BadRequest("idempotencyKey is required.");
        return await WithGate($"order:{orderId}", ct, async () =>
        {
            var order = await GetOrderAsync(orderId, ct);
            EnsureOwner(order, ownerId);
            if (order.FulfilmentStatus != OrderFulfilmentStatus.Fulfilled || string.IsNullOrWhiteSpace(order.CaptureId) || !order.CapturedAmount.HasValue)
                throw Conflict("Only a fulfilled order with a captured payment can be refunded.");

            var existing = order.Refunds.SingleOrDefault(r => r.IdempotencyKey == request.IdempotencyKey);
            if (existing is not null)
            {
                if (request.Amount.HasValue && request.Amount.Value != existing.Amount)
                    throw Conflict("This idempotency key was already used with a different refund amount.");
                if (!string.IsNullOrWhiteSpace(existing.PayPalRefundId)) return RefundView(existing);
            }

            var committed = order.Refunds.Where(r => r != existing && r.Status is not ("FAILED" or "CANCELLED"))
                .Sum(r => r.Amount);
            var remaining = order.CapturedAmount.Value - committed;
            var requested = request.Amount ?? remaining;
            if (requested <= 0 || requested > remaining)
                throw BadRequest($"Refund amount must be greater than zero and no more than {remaining:0.00} {order.Currency}.");

            existing ??= order.BeginRefund(request.IdempotencyKey, requested);
            await _db.SaveChangesAsync(ct);
            decimal? providerAmount = !request.Amount.HasValue && committed == 0m ? null : requested;
            try
            {
                var result = await _payPal.RefundAsync(order.CaptureId, providerAmount, request.IdempotencyKey, ct);
                existing.RecordProviderResult(result.Id, result.Status, result.Amount == 0m ? requested : result.Amount,
                    result.UpdatedAt, result.StatusReason);
                order.RecalculateRefundStatus();
                await _db.SaveChangesAsync(ct);
                return RefundView(existing);
            }
            catch (PayPalProviderException ex)
            {
                existing.RecordFailure(ex.Message);
                await _db.SaveChangesAsync(ct);
                throw Provider(ex);
            }
        });
    }

    public async Task<IReadOnlyList<OrderView>> MyOrdersAsync(string ownerId, CancellationToken ct)
    {
        var orders = await _db.Orders.AsNoTracking().Where(o => o.BuyerId == ownerId)
            .Include(o => o.OrderItems).ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Refunds).OrderByDescending(o => o.OrderDate).ToListAsync(ct);
        return orders.Select(View).ToList();
    }

    public async Task<PaymentMethodView> SavePaymentMethodAsync(string ownerId, SavePaymentMethodRequest request, CancellationToken ct)
    {
        ValidateCard(request.Card);
        try
        {
            var result = await _payPal.SaveCardAsync(MerchantCustomerId(ownerId), request.Card.ToInput(),
                $"eshop-vault-{Guid.NewGuid():N}", ct);
            var method = new PaymentMethod(ownerId, result.CustomerId, result.TokenId, result.Name,
                result.Brand, result.LastDigits, result.Expiry, result.Type);
            _db.PaymentMethods.Add(method);
            await _db.SaveChangesAsync(ct);
            return PaymentMethodView(method);
        }
        catch (PayPalProviderException ex) { throw Provider(ex); }
    }

    public async Task<IReadOnlyList<PaymentMethodView>> PaymentMethodsAsync(string ownerId, CancellationToken ct)
    {
        var methods = await _db.PaymentMethods.AsNoTracking()
            .Where(p => p.OwnerId == ownerId && p.DeletedAt == null).OrderBy(p => p.Id).ToListAsync(ct);
        if (methods.Count == 0) return Array.Empty<PaymentMethodView>();
        try
        {
            var providerIds = await _payPal.ListVaultedTokenIdsAsync(methods[0].PayPalCustomerId, ct);
            return methods.Where(m => providerIds.Contains(m.PayPalPaymentTokenId)).Select(PaymentMethodView).ToList();
        }
        catch (PayPalProviderException ex) { throw Provider(ex); }
    }

    public async Task DeletePaymentMethodAsync(string ownerId, int paymentMethodId, CancellationToken ct)
    {
        await WithGate($"method:{paymentMethodId}", ct, async () =>
        {
            var method = await _db.PaymentMethods.SingleOrDefaultAsync(p => p.Id == paymentMethodId &&
                p.OwnerId == ownerId && p.DeletedAt == null, ct);
            if (method is null) throw new PaymentApplicationException(404, "Payment method not found",
                "The saved payment method does not exist or is not owned by this shopper.");
            try
            {
                await _payPal.DeleteVaultedTokenAsync(method.PayPalPaymentTokenId, ct);
                method.Delete();
                await _db.SaveChangesAsync(ct);
                return true;
            }
            catch (PayPalProviderException ex) { throw Provider(ex); }
        });
    }

    public async Task<ReconciliationView> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (to <= from) throw BadRequest("to must be later than from.");
        if (to - from > TimeSpan.FromDays(365 * 3 + 1)) throw BadRequest("PayPal reporting covers at most the previous three years.");
        IReadOnlyList<PayPalTransactionResult> provider;
        try { provider = await _payPal.SearchTransactionsAsync(from, to, ct); }
        catch (PayPalProviderException ex) { throw Provider(ex); }

        var orders = await _db.Orders.AsNoTracking().Where(o => o.OrderDate >= from && o.OrderDate <= to && o.PayPalOrderId != null)
            .Include(o => o.Refunds).ToListAsync(ct);
        var transactionViews = provider.Select(row =>
        {
            var order = orders.FirstOrDefault(o => Matches(o, row));
            return new ReconciliationTransactionView(row.TransactionId, row.PayPalReferenceId, row.EventCode,
                row.InitiationDate, row.Amount, row.Fee, row.Currency, row.Status, row.InvoiceId, order?.Id);
        }).ToList();
        var dataAvailable = provider.Count > 0;
        var matched = transactionViews.Where(t => t.OrderId.HasValue).Select(t => t.OrderId!.Value).ToHashSet();
        var localOnly = dataAvailable
            ? orders.Where(o => !matched.Contains(o.Id)).Select(o => new ReconciliationOrderView(o.Id, o.PayPalOrderId,
                o.AuthorizationId, o.CaptureId, o.Refunds.Where(r => r.PayPalRefundId != null).Select(r => r.PayPalRefundId!).ToList(),
                o.Total(), o.PaymentStatus.ToString())).ToList()
            : new List<ReconciliationOrderView>();
        return new ReconciliationView(from, to, dataAvailable, !dataAvailable, transactionViews, localOnly);
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _db.Orders.Include(o => o.OrderItems).ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Refunds).SingleOrDefaultAsync(o => o.Id == orderId, ct);
        return order ?? throw new PaymentApplicationException(404, "Order not found", "The order does not exist.");
    }

    private static bool Matches(Order order, PayPalTransactionResult row) =>
        row.InvoiceId == InvoiceId(order) || row.TransactionId == order.PayPalOrderId ||
        row.TransactionId == order.AuthorizationId || row.TransactionId == order.CaptureId ||
        row.PayPalReferenceId == order.PayPalOrderId || row.PayPalReferenceId == order.AuthorizationId ||
        row.PayPalReferenceId == order.CaptureId || order.Refunds.Any(r => r.PayPalRefundId == row.TransactionId || r.PayPalRefundId == row.PayPalReferenceId);

    private static OrderView View(Order order) => new(order.Id, order.OrderDate, order.Total(), order.Currency,
        order.PaymentStatus.ToString(), order.FulfilmentStatus.ToString(), order.PayPalOrderId, order.AuthorizationId,
        order.AuthorizationStatus, order.CaptureId, order.CaptureStatus, order.CapturedAmount, order.PayPalFee,
        order.NetProceeds, order.PaymentFailureReason,
        order.OrderItems.Select(i => new OrderItemView(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units)).ToList(),
        order.Refunds.Select(RefundView).ToList());

    private static RefundView RefundView(PaymentRefund refund) => new(refund.PayPalRefundId, refund.IdempotencyKey,
        refund.Amount, refund.Currency, refund.Status, refund.StatusReason);
    private static PaymentMethodView PaymentMethodView(PaymentMethod method) => new(method.Id, method.Name,
        method.Brand, method.LastDigits, method.Expiry, method.Type);

    private static void EnsureOwner(Order order, string ownerId)
    {
        if (!string.Equals(order.BuyerId, ownerId, StringComparison.Ordinal))
            throw new PaymentApplicationException(404, "Order not found", "The order does not exist.");
    }

    private static void ValidateShipping(ShippingAddressRequest address)
    {
        if (string.IsNullOrWhiteSpace(address.Street) || string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.Country) || string.IsNullOrWhiteSpace(address.ZipCode))
            throw BadRequest("shipToAddress requires street, city, country and zipCode.");
    }

    private static void ValidateCard(CardRequestDto card)
    {
        if (string.IsNullOrWhiteSpace(card.Name) || string.IsNullOrWhiteSpace(card.Number) ||
            string.IsNullOrWhiteSpace(card.Expiry) || string.IsNullOrWhiteSpace(card.SecurityCode) ||
            string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode))
            throw BadRequest("Card name, number, expiry, securityCode and billingAddress.countryCode are required.");
    }

    private static string InvoiceId(Order order) =>
        $"eshop-order-{order.Id}-{order.OrderDate.UtcDateTime.Ticks}";
    private static string MerchantCustomerId(string ownerId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(ownerId));
        return $"eshop-{Convert.ToHexString(hash)[..32].ToLowerInvariant()}";
    }

    private static PaymentApplicationException BadRequest(string detail) => new(400, "Invalid request", detail);
    private static PaymentApplicationException Conflict(string detail) => new(409, "Operation conflict", detail);
    private static PaymentApplicationException Provider(PayPalProviderException ex) => new(
        ex.StatusCode is >= 400 and < 500 ? ex.StatusCode : 502,
        "PayPal operation failed",
        ex.DebugId is null ? ex.Message : $"{ex.Message} PayPal debug id: {ex.DebugId}");

    private static async Task<T> WithGate<T>(string key, CancellationToken ct, Func<Task<T>> action)
    {
        var gate = Gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try { return await action(); }
        finally { gate.Release(); }
    }
}
