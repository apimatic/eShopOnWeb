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
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PaymentService : IPaymentService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();
    private readonly CatalogContext _db;
    private readonly IPayPalClient _payPal;
    private readonly string _currency;

    public PaymentService(CatalogContext db, IPayPalClient payPal, IOptions<PayPalOptions> options)
    {
        _db = db;
        _payPal = payPal;
        _currency = options.Value.Currency.ToUpperInvariant();
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineInput> lines, Address address, CancellationToken cancellationToken)
    {
        if (lines.Count == 0) throw Error("EMPTY_ORDER", "At least one catalog item is required.", 400);
        if (lines.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0)) throw Error("INVALID_ORDER_ITEM", "Catalog item IDs and quantities must be positive.", 400);
        var grouped = lines.GroupBy(x => x.CatalogItemId).Select(x => new OrderLineInput(x.Key, x.Sum(y => y.Quantity))).ToList();
        var ids = grouped.Select(x => x.CatalogItemId).ToList();
        var catalogItems = await _db.CatalogItems.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
        var missing = ids.Except(catalogItems.Select(x => x.Id)).ToList();
        if (missing.Count > 0) throw Error("CATALOG_ITEM_NOT_FOUND", $"Catalog item(s) not found: {string.Join(", ", missing)}.", 400);

        var items = grouped.Select(line =>
        {
            var catalog = catalogItems.Single(x => x.Id == line.CatalogItemId);
            return new OrderItem(new CatalogItemOrdered(catalog.Id, catalog.Name, catalog.PictureUri), catalog.Price, line.Quantity);
        }).ToList();
        var order = new Order(buyerId, address, items, _currency);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        return order;
    }

    public async Task<Order> PayAsync(string buyerId, int orderId, CardDetails? card, int? paymentMethodId, CancellationToken cancellationToken)
    {
        if ((card is null) == (paymentMethodId is null)) throw Error("PAYMENT_SOURCE_REQUIRED", "Provide exactly one of card or paymentMethodId.", 400);
        await using var held = await AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await LoadOrderAsync(orderId, buyerId, cancellationToken);
        if (order.Status == OrderStatus.Authorized) return order;
        if (order.Status != OrderStatus.AwaitingPayment || order.Payment is null) throw Error("ORDER_NOT_PAYABLE", "This order is not awaiting payment.");

        string? vaultId = null;
        if (paymentMethodId is not null)
        {
            var method = await _db.PaymentMethods.SingleOrDefaultAsync(x => x.Id == paymentMethodId && x.BuyerId == buyerId, cancellationToken);
            if (method is null) throw Error("PAYMENT_METHOD_NOT_FOUND", "The saved payment method was not found.", 404);
            vaultId = method.PayPalTokenId;
        }

        try
        {
            if (order.Payment.PayPalOrderId is null)
            {
                var created = await _payPal.CreateOrderAsync(order.Id, order.Payment.Amount, order.Payment.Currency, RequestId($"ord-{order.Id}"), cancellationToken);
                order.Payment.SetPayPalOrder(created.Id);
                await _db.SaveChangesAsync(cancellationToken);
            }

            var authorization = await _payPal.AuthorizeOrderAsync(order.Payment.PayPalOrderId, card, vaultId, RequestId($"pay-{order.Id}"), cancellationToken);
            if (authorization.Amount != order.Payment.Amount)
            {
                await _payPal.VoidAsync(authorization.Id, RequestId($"void-bad-{order.Id}"), cancellationToken);
                throw Error("PAYPAL_AMOUNT_MISMATCH", "PayPal authorized a different amount; the hold was released and the order was not paid.", 502);
            }
            if (authorization.Status is not ("CREATED" or "AUTHORIZED")) throw Error("AUTHORIZATION_NOT_ACTIVE", $"PayPal returned authorization status {authorization.Status}.");
            order.Payment.AddAuthorization(authorization.Id, authorization.Status, authorization.Amount, authorization.CreatedAt, authorization.ExpiresAt);
            order.MarkAuthorized();
            await _db.SaveChangesAsync(cancellationToken);
            return order;
        }
        catch (PayPalApiException ex)
        {
            throw Translate(ex, "authorize payment");
        }
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        await using var held = await AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await LoadOrderAsync(orderId, null, cancellationToken);
        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded) return order;
        if (order.Status != OrderStatus.Authorized || order.Payment?.CurrentAuthorization is null)
            throw Error("ORDER_NOT_AUTHORIZED", "The order must have an active authorization before fulfilment.");

        try
        {
            PayPalCaptureResult capture;
            if (order.Payment.CaptureId is not null)
            {
                capture = await _payPal.GetCaptureAsync(order.Payment.CaptureId, cancellationToken);
            }
            else
            {
                var authorization = order.Payment.CurrentAuthorization;
                var stale = authorization.CreatedAt.AddDays(3) <= DateTimeOffset.UtcNow;
                if (stale)
                {
                    try
                    {
                        var renewed = await _payPal.ReauthorizeAsync(authorization.PayPalAuthorizationId, RequestId($"reauth-{order.Id}-{authorization.Id}"), cancellationToken);
                        order.Payment.AddAuthorization(renewed.Id, renewed.Status, renewed.Amount, renewed.CreatedAt, renewed.ExpiresAt);
                        await _db.SaveChangesAsync(cancellationToken);
                        authorization = order.Payment.CurrentAuthorization!;
                    }
                    catch (PayPalApiException ex)
                    {
                        throw Error("AUTHORIZATION_CANNOT_BE_RENEWED", $"PayPal can no longer renew this authorization ({ex.Issue}). Ask the shopper to authorize the order again before fulfilment.", 409);
                    }
                }
                capture = await _payPal.CaptureAsync(authorization.PayPalAuthorizationId, order.Payment.Amount, order.Payment.Currency, RequestId($"capture-{order.Id}"), cancellationToken);
            }

            if (capture.Amount != order.Payment.Amount) throw Error("CAPTURE_AMOUNT_MISMATCH", "PayPal captured an amount different from the order total. Reconcile this order before fulfilment.", 502);
            order.Payment.RecordCapture(capture.Id, capture.Status, capture.Amount, capture.PayPalFee, capture.NetAmount, capture.CreatedAt);
            if (capture.Status == "COMPLETED") order.MarkFulfilled();
            await _db.SaveChangesAsync(cancellationToken);
            if (capture.Status != "COMPLETED") throw Error("CAPTURE_NOT_COMPLETED", $"PayPal capture {capture.Id} is {capture.Status}; retry fulfilment after the operator resolves that PayPal status.", 409);
            return order;
        }
        catch (PayPalApiException ex)
        {
            throw Translate(ex, "capture payment");
        }
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        await using var held = await AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await LoadOrderAsync(orderId, null, cancellationToken);
        if (order.Status == OrderStatus.Cancelled) return order;
        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
            throw Error("ORDER_ALREADY_FULFILLED", "A fulfilled order cannot be cancelled; refund it instead.");
        try
        {
            if (order.Payment?.CurrentAuthorization is { } authorization)
            {
                var status = await _payPal.VoidAsync(authorization.PayPalAuthorizationId, RequestId($"void-{order.Id}"), cancellationToken);
                order.Payment.MarkVoided(status);
            }
            order.MarkCancelled();
            await _db.SaveChangesAsync(cancellationToken);
            return order;
        }
        catch (PayPalApiException ex)
        {
            throw Translate(ex, "release authorization");
        }
    }

    public async Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128) throw Error("INVALID_IDEMPOTENCY_KEY", "IdempotencyKey is required and cannot exceed 128 characters.", 400);
        await using var held = await AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await LoadOrderAsync(orderId, buyerId, cancellationToken);
        var payment = order.Payment;
        if (payment?.CaptureId is null || payment.CapturedAmount is null || order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded))
            throw Error("ORDER_NOT_REFUNDABLE", "Only a fulfilled order with a completed capture can be refunded.");
        var existing = payment.Refunds.SingleOrDefault(x => x.IdempotencyKey == idempotencyKey);
        if (existing is not null) return existing;
        var remaining = payment.CapturedAmount.Value - payment.RefundedAmount;
        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0 || refundAmount > remaining) throw Error("REFUND_AMOUNT_EXCEEDS_CAPTURE", $"The maximum refundable amount is {remaining:0.00} {payment.Currency}.", 400);

        try
        {
            var result = await _payPal.RefundAsync(payment.CaptureId, refundAmount, payment.Currency, HashedRequestId($"refund:{order.Id}:{idempotencyKey}"), cancellationToken);
            var refund = payment.AddRefund(result.Id, idempotencyKey, result.Status, result.Amount, result.CreatedAt);
            order.MarkRefunded(payment.RefundedAmount);
            await _db.SaveChangesAsync(cancellationToken);
            return refund;
        }
        catch (PayPalApiException ex)
        {
            throw Translate(ex, "refund payment");
        }
    }

    public async Task<IReadOnlyList<Order>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken) =>
        await OrderQuery().Where(x => x.BuyerId == buyerId).OrderByDescending(x => x.OrderDate).ToListAsync(cancellationToken);

    public async Task<PaymentMethod> SavePaymentMethodAsync(string buyerId, CardDetails card, CancellationToken cancellationToken)
    {
        await using var held = await AcquireAsync($"buyer:{buyerId}", cancellationToken);
        var customerId = await _db.PaymentMethods.Where(x => x.BuyerId == buyerId).Select(x => x.PayPalCustomerId).FirstOrDefaultAsync(cancellationToken);
        try
        {
            var result = await _payPal.VaultCardAsync(buyerId, customerId, card, HashedRequestId($"vault:{buyerId}:{Guid.NewGuid():N}"), cancellationToken);
            var method = new PaymentMethod(buyerId, result.TokenId, result.CustomerId, result.Brand, result.Last4, result.Expiry);
            _db.PaymentMethods.Add(method);
            await _db.SaveChangesAsync(cancellationToken);
            return method;
        }
        catch (PayPalApiException ex)
        {
            throw Translate(ex, "save card");
        }
    }

    public async Task<IReadOnlyList<PaymentMethod>> GetPaymentMethodsAsync(string buyerId, CancellationToken cancellationToken) =>
        await _db.PaymentMethods.Where(x => x.BuyerId == buyerId).OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        await using var held = await AcquireAsync($"method:{paymentMethodId}", cancellationToken);
        var method = await _db.PaymentMethods.SingleOrDefaultAsync(x => x.Id == paymentMethodId && x.BuyerId == buyerId, cancellationToken);
        if (method is null) throw Error("PAYMENT_METHOD_NOT_FOUND", "The saved payment method was not found.", 404);
        try
        {
            await _payPal.DeletePaymentTokenAsync(method.PayPalTokenId, cancellationToken);
            _db.PaymentMethods.Remove(method);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            throw Translate(ex, "delete saved card");
        }
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (to <= from) throw Error("INVALID_DATE_RANGE", "The to value must be later than from.", 400);
        IReadOnlyList<PayPalTransaction> transactions;
        try
        {
            transactions = await _payPal.SearchTransactionsAsync(from, to, cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            throw Translate(ex, "retrieve PayPal reconciliation data");
        }

        var local = await OrderQuery().Where(x => x.Payment != null && x.OrderDate >= from && x.OrderDate <= to).ToListAsync(cancellationToken);
        var items = new List<ReconciliationItem>();
        var matchedOrders = new HashSet<int>();
        foreach (var transaction in transactions)
        {
            var order = local.FirstOrDefault(x => Matches(x, transaction));
            if (order is not null) matchedOrders.Add(order.Id);
            items.Add(new ReconciliationItem("PayPal", order is null ? "PayPalOnly" : "Matched", order?.Id, transaction.TransactionId,
                order?.Payment?.PayPalOrderId, order?.Payment?.CaptureId, transaction.Status, transaction.Amount, transaction.Currency, transaction.InitiationDate));
        }
        foreach (var order in local.Where(x => !matchedOrders.Contains(x.Id)))
        {
            items.Add(new ReconciliationItem("eShop", "EShopOnly", order.Id, null, order.Payment!.PayPalOrderId, order.Payment.CaptureId,
                order.Payment.CaptureStatus ?? order.Payment.Status.ToString(), order.Payment.CapturedAmount ?? order.Payment.Amount, order.Payment.Currency, order.Payment.CapturedAt ?? order.OrderDate));
        }
        return new ReconciliationReport(from, to, items.OrderBy(x => x.TransactionDate).ToList());
    }

    private IQueryable<Order> OrderQuery() => _db.Orders
        .Include(x => x.OrderItems)
        .Include(x => x.Payment)!.ThenInclude(x => x!.Authorizations)
        .Include(x => x.Payment)!.ThenInclude(x => x!.Refunds);

    private async Task<Order> LoadOrderAsync(int orderId, string? buyerId, CancellationToken cancellationToken)
    {
        var query = OrderQuery().Where(x => x.Id == orderId);
        if (buyerId is not null) query = query.Where(x => x.BuyerId == buyerId);
        return await query.SingleOrDefaultAsync(cancellationToken) ?? throw Error("ORDER_NOT_FOUND", "The order was not found.", 404);
    }

    private static bool Matches(Order order, PayPalTransaction transaction)
    {
        var payment = order.Payment!;
        return transaction.TransactionId == payment.CaptureId
            || transaction.ReferenceId == payment.PayPalOrderId
            || transaction.InvoiceId == $"ESHOP-{order.Id}";
    }

    private static PaymentOperationException Translate(PayPalApiException ex, string operation)
    {
        if (ex.Issue == "PAYER_ACTION_REQUIRED") return Error(ex.Issue, ex.Message, 409);
        return Error(ex.Issue, $"PayPal could not {operation}: {ex.Message} (debug ID: {ex.DebugId ?? "not supplied"}).", 502);
    }

    private static PaymentOperationException Error(string code, string message, int status = 409) => new(code, message, status);
    private static string RequestId(string value) => ("eshop-" + value).Length <= 25 ? "eshop-" + value : HashedRequestId(value)[..25];
    private static string HashedRequestId(string value) => "eshop-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..32];

    private static async Task<AsyncLockHandle> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        var semaphore = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new AsyncLockHandle(semaphore);
    }

    private sealed class AsyncLockHandle : IAsyncDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        public AsyncLockHandle(SemaphoreSlim semaphore) => _semaphore = semaphore;
        public ValueTask DisposeAsync() { _semaphore.Release(); return ValueTask.CompletedTask; }
    }
}
