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
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();
    private readonly CatalogContext _db;
    private readonly IPayPalClient _payPal;
    private readonly PayPalOptions _options;

    public PaymentService(CatalogContext db, IPayPalClient payPal, IOptions<PayPalOptions> options)
    {
        _db = db;
        _payPal = payPal;
        _options = options.Value;
    }

    public async Task<OrderResponse> CreateOrderAsync(string buyerId, CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items.Count == 0) throw Invalid("At least one catalog item is required.");
        var requested = request.Items.GroupBy(x => x.CatalogItemId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity));
        if (requested.Values.Any(x => x <= 0 || x > 1000)) throw Invalid("Item quantities must be between 1 and 1000.");
        var catalogItems = await _db.CatalogItems.Where(x => requested.Keys.Contains(x.Id))
            .ToListAsync(cancellationToken);
        var missing = requested.Keys.Except(catalogItems.Select(x => x.Id)).ToArray();
        if (missing.Length > 0) throw new PaymentOperationException(404,
            $"Catalog item(s) not found: {string.Join(", ", missing)}.");

        var items = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri), item.Price, requested[item.Id])).ToList();
        var address = request.ShippingAddress;
        var order = new Order(buyerId,
            new Address(address.Street, address.City, address.State, address.Country, address.ZipCode),
            items, RequiredCurrency());
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(order);
    }

    public async Task<OrderResponse> PayAsync(int orderId, string buyerId, PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        if ((request.Card is null) == (request.PaymentMethodId is null))
            throw Invalid("Supply either card or paymentMethodId, but not both.");
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await OwnedOrderAsync(orderId, buyerId, cancellationToken);
            if (order.PaymentStatus is PaymentStatus.Authorized or PaymentStatus.Captured or
                PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
                return Map(order);
            if (order.FulfilmentStatus == FulfilmentStatus.Cancelled)
                throw Conflict("A cancelled order cannot be paid.");

            string? vaultId = null;
            if (request.PaymentMethodId is int paymentMethodId)
            {
                var paymentMethod = await _db.PaymentMethods.SingleOrDefaultAsync(x => x.Id == paymentMethodId &&
                    x.BuyerId == buyerId && !x.IsDeleted, cancellationToken);
                if (paymentMethod is null) throw new PaymentOperationException(404, "Saved payment method not found.");
                vaultId = paymentMethod.PayPalVaultId;
            }

            if (order.PayPalOrderId is null)
            {
                var payPalOrder = await _payPal.CreateOrderAsync(order.PaymentReference!, order.Total(),
                    order.Currency!, order.PaymentReference + "-create", cancellationToken);
                order.RecordPayPalOrder(payPalOrder.Id, payPalOrder.Status);
                await _db.SaveChangesAsync(cancellationToken);
            }

            var authorization = request.Card is not null
                ? await _payPal.AuthorizeCardAsync(order.PayPalOrderId!, request.Card,
                    order.PaymentReference + "-authorize", cancellationToken)
                : await _payPal.AuthorizeVaultedCardAsync(order.PayPalOrderId!, vaultId!,
                    order.PaymentReference + "-authorize", cancellationToken);
            if (authorization.RequiresPayerAction)
                throw Conflict("PayPal requires an interactive cardholder challenge; this API does not support a browser approval round-trip.");
            EnsureMoney(order, authorization.Amount, authorization.Currency, "authorized");
            order.RecordAuthorization(authorization.Id, authorization.Status, authorization.Amount,
                authorization.CreatedAt, authorization.ExpiresAt, authorization.CardBrand,
                authorization.CardLastDigits);
            await _db.SaveChangesAsync(cancellationToken);
            if (authorization.Status != "CREATED")
                throw Conflict($"PayPal authorization is not ready to capture (status: {authorization.Status}).");
            return Map(order);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<OrderResponse> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await OrderAsync(orderId, cancellationToken);
            if (order.FulfilmentStatus == FulfilmentStatus.Fulfilled) return Map(order);
            if (order.FulfilmentStatus == FulfilmentStatus.Cancelled) throw Conflict("A cancelled order cannot be fulfilled.");
            if (order.PayPalAuthorizationId is null) throw Conflict("The order must be authorized before fulfilment.");

            if (order.PayPalCaptureId is not null)
            {
                var existingCapture = await _payPal.GetCaptureAsync(order.PayPalCaptureId, cancellationToken);
                EnsureMoney(order, existingCapture.Amount, existingCapture.Currency, "captured");
                order.RecordCapture(existingCapture.Id, existingCapture.Status, existingCapture.Amount,
                    existingCapture.Fee, existingCapture.Net, existingCapture.CreatedAt);
                await _db.SaveChangesAsync(cancellationToken);
                if (existingCapture.Status != "COMPLETED")
                    throw Conflict($"PayPal capture is still {existingCapture.Status}; retry fulfilment after it settles.");
                return Map(order);
            }

            var current = await _payPal.GetAuthorizationAsync(order.PayPalAuthorizationId, cancellationToken);
            order.RecordAuthorizationState(current.Status, current.ExpiresAt);
            if (current.Status is not ("CREATED" or "CAPTURED"))
            {
                await _db.SaveChangesAsync(cancellationToken);
                throw Conflict($"PayPal authorization is {current.Status}; ask the shopper to place and pay a replacement order before fulfilment.");
            }

            if (current.Status == "CAPTURED" && current.CaptureId is not null)
            {
                var recoveredCapture = await _payPal.GetCaptureAsync(current.CaptureId, cancellationToken);
                EnsureMoney(order, recoveredCapture.Amount, recoveredCapture.Currency, "captured");
                order.RecordCapture(recoveredCapture.Id, recoveredCapture.Status, recoveredCapture.Amount,
                    recoveredCapture.Fee, recoveredCapture.Net, recoveredCapture.CreatedAt);
                await _db.SaveChangesAsync(cancellationToken);
                if (recoveredCapture.Status != "COMPLETED")
                    throw Conflict($"PayPal capture is still {recoveredCapture.Status}; retry fulfilment after it settles.");
                return Map(order);
            }

            if (current.Status == "CREATED" && current.CreatedAt <= DateTimeOffset.UtcNow.AddDays(-3))
            {
                if (current.ExpiresAt is not null && current.ExpiresAt <= DateTimeOffset.UtcNow)
                {
                    order.MarkAuthorizationExpired();
                    await _db.SaveChangesAsync(cancellationToken);
                    throw Conflict("The PayPal authorization is beyond its renewal window; ask the shopper to place and pay a replacement order.");
                }
                try
                {
                    var renewed = await _payPal.ReauthorizeAsync(current.Id, order.Total(), order.Currency!,
                        order.PaymentReference + $"-reauthorize-{order.AuthorizationRenewalCount + 1}", cancellationToken);
                    EnsureMoney(order, renewed.Amount, renewed.Currency, "reauthorized");
                    order.RecordReauthorization(renewed.Id, renewed.Status, renewed.CreatedAt, renewed.ExpiresAt);
                    await _db.SaveChangesAsync(cancellationToken);
                    if (renewed.Status != "CREATED")
                        throw Conflict($"Renewed PayPal authorization is {renewed.Status}; do not ship until it is ready.");
                }
                catch (PaymentOperationException ex) when (ex.StatusCode is 404 or 409 or 422)
                {
                    order.MarkAuthorizationExpired();
                    await _db.SaveChangesAsync(cancellationToken);
                    throw Conflict($"PayPal could not renew the stale authorization ({ex.PayPalIssue ?? "not renewable"}); ask the shopper to place and pay a replacement order. PayPal debug ID: {ex.PayPalDebugId ?? "unavailable"}.");
                }
            }

            var capture = await _payPal.CaptureAsync(order.PayPalAuthorizationId!, order.Total(),
                order.Currency!, order.PaymentReference!, order.PaymentReference + "-capture", cancellationToken);
            EnsureMoney(order, capture.Amount, capture.Currency, "captured");
            order.RecordCapture(capture.Id, capture.Status, capture.Amount, capture.Fee, capture.Net,
                capture.CreatedAt);
            await _db.SaveChangesAsync(cancellationToken);
            if (capture.Status != "COMPLETED")
                throw Conflict($"PayPal capture is {capture.Status}; retry fulfilment after it settles.");
            return Map(order);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<OrderResponse> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await OrderAsync(orderId, cancellationToken);
            if (order.FulfilmentStatus == FulfilmentStatus.Cancelled) return Map(order);
            if (order.FulfilmentStatus == FulfilmentStatus.Fulfilled) throw Conflict("A fulfilled order must be refunded, not cancelled.");
            var status = order.PayPalAuthorizationId is null
                ? null
                : await _payPal.VoidAsync(order.PayPalAuthorizationId,
                    order.PaymentReference + "-void", cancellationToken);
            order.Cancel(status);
            await _db.SaveChangesAsync(cancellationToken);
            return Map(order);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<RefundResponse> RefundAsync(int orderId, string buyerId,
        CreateRefundRequest request, CancellationToken cancellationToken)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await OwnedOrderAsync(orderId, buyerId, cancellationToken);
            var duplicate = order.Refunds.SingleOrDefault(x => x.CallerIdempotencyKey == request.IdempotencyKey);
            if (duplicate is not null) return Map(duplicate);
            if (order.PayPalCaptureId is null || order.CapturedAmount is null ||
                order.PayPalCaptureStatus != "COMPLETED")
                throw Conflict("Only a completed capture can be refunded.");
            var remaining = order.CapturedAmount.Value - order.RefundedAmount;
            var amount = request.Amount ?? remaining;
            if (amount <= 0 || amount > remaining)
                throw Conflict($"Refund amount must be positive and no more than the remaining {remaining:0.00} {order.Currency}.");
            var requestId = "eshop-refund-" + Hash(request.IdempotencyKey);
            var result = await _payPal.RefundAsync(order.PayPalCaptureId, amount, order.Currency!,
                requestId, request.Note, requestId, cancellationToken);
            if (result.Amount != amount || result.Currency != order.Currency)
                throw new PaymentOperationException(502, "PayPal's refund amount or currency did not match the request.");
            var refund = order.AddRefund(request.IdempotencyKey, requestId, result.Id, result.Status,
                result.Amount, result.CreatedAt);
            await _db.SaveChangesAsync(cancellationToken);
            return Map(refund);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyCollection<OrderResponse>> MyOrdersAsync(string buyerId,
        CancellationToken cancellationToken) => (await _db.Orders.AsNoTracking()
            .Include(x => x.OrderItems).Include(x => x.Refunds)
            .Where(x => x.BuyerId == buyerId).OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken)).Select(Map).ToArray();

    public async Task<PaymentMethodResponse> SavePaymentMethodAsync(string buyerId,
        SavePaymentMethodRequest request, CancellationToken cancellationToken)
    {
        var merchantCustomerId = "eshop_" + Hash(buyerId)[..24];
        var operationId = "eshop-vault-" + Guid.NewGuid().ToString("N");
        var result = await _payPal.SaveCardAsync(request.Card, merchantCustomerId, operationId,
            cancellationToken);
        if (result.RequiresPayerAction)
            throw Conflict("PayPal requires an interactive cardholder challenge; this API does not support a browser approval round-trip.");
        var paymentMethod = new PaymentMethod(buyerId, result.Id, result.CustomerId,
            result.Brand, result.LastDigits, result.Expiry);
        _db.PaymentMethods.Add(paymentMethod);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(paymentMethod);
    }

    public async Task<IReadOnlyCollection<PaymentMethodResponse>> PaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken) => (await _db.PaymentMethods.AsNoTracking()
            .Where(x => x.BuyerId == buyerId && !x.IsDeleted).OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken)).Select(Map).ToArray();

    public async Task DeletePaymentMethodAsync(int paymentMethodId, string buyerId,
        CancellationToken cancellationToken)
    {
        var paymentMethod = await _db.PaymentMethods.SingleOrDefaultAsync(x => x.Id == paymentMethodId &&
            x.BuyerId == buyerId && !x.IsDeleted, cancellationToken);
        if (paymentMethod is null) throw new PaymentOperationException(404, "Saved payment method not found.");
        try
        {
            await _payPal.DeletePaymentTokenAsync(paymentMethod.PayPalVaultId, cancellationToken);
        }
        catch (PaymentOperationException ex) when (ex.StatusCode == 404)
        {
            // A previous attempt may have deleted PayPal's token before the local commit completed.
        }
        paymentMethod.MarkDeleted();
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from >= to) throw Invalid("from must be before to.");
        var all = new Dictionary<string, PayPalTransactionResult>(StringComparer.Ordinal);
        var chunkStart = from;
        while (chunkStart < to)
        {
            var chunkEnd = chunkStart.AddDays(30) < to ? chunkStart.AddDays(30) : to;
            var page = 1;
            do
            {
                var result = await _payPal.SearchTransactionsAsync(chunkStart, chunkEnd, page, 500,
                    cancellationToken);
                foreach (var transaction in result.Transactions)
                {
                    var key = $"{transaction.TransactionId}|{transaction.EventCode}|{transaction.InitiatedAt:O}";
                    all.TryAdd(key, transaction);
                }
                if (page >= result.TotalPages) break;
                page++;
            } while (true);
            chunkStart = chunkEnd;
        }

        var orders = await _db.Orders.AsNoTracking().Include(x => x.Refunds)
            .Where(x => x.PayPalOrderId != null).ToListAsync(cancellationToken);
        var byResourceId = new Dictionary<string, Order>(StringComparer.Ordinal);
        var byReference = new Dictionary<string, Order>(StringComparer.Ordinal);
        foreach (var order in orders)
        {
            Add(byResourceId, order.PayPalOrderId, order);
            Add(byResourceId, order.PayPalAuthorizationId, order);
            Add(byResourceId, order.PayPalCaptureId, order);
            foreach (var refund in order.Refunds) Add(byResourceId, refund.PayPalRefundId, order);
            Add(byReference, order.PaymentReference, order);
        }
        var transactions = all.Values.Select(x =>
        {
            var order = Match(x, byResourceId, byReference);
            return new ReconciliationTransaction(x.TransactionId, x.PayPalReferenceId, x.EventCode,
                x.Status, x.InitiatedAt, x.Amount, x.Currency, x.Fee, x.InvoiceId, x.CustomField, order?.Id);
        }).OrderBy(x => x.InitiatedAt).ToArray();
        var payPalOnly = all.Values.Where(x => Match(x, byResourceId, byReference) is null)
            .Select(x => new ReconciliationMissingLocal(x.TransactionId, x.PayPalReferenceId,
                x.Amount, x.Currency)).ToArray();
        var ids = all.Values.SelectMany(x => new[] { x.TransactionId, x.PayPalReferenceId })
            .Where(x => x is not null).ToHashSet(StringComparer.Ordinal);
        var eShopOnly = new List<ReconciliationMissingPayPal>();
        foreach (var order in orders)
        {
            if (order.PayPalCaptureId is not null && order.CapturedAt >= from && order.CapturedAt <= to &&
                !ids.Contains(order.PayPalCaptureId))
                eShopOnly.Add(new(order.Id, "capture", order.PayPalCaptureId,
                    order.CapturedAmount ?? 0, order.Currency!, order.CapturedAt.Value));
            foreach (var refund in order.Refunds.Where(x => x.CreatedAt >= from && x.CreatedAt <= to &&
                         !ids.Contains(x.PayPalRefundId)))
                eShopOnly.Add(new(order.Id, "refund", refund.PayPalRefundId, refund.Amount,
                    refund.Currency, refund.CreatedAt));
        }
        return new ReconciliationResponse(from, to, transactions, payPalOnly, eShopOnly);
    }

    private async Task<Order> OwnedOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.Include(x => x.OrderItems).Include(x => x.Refunds)
            .SingleOrDefaultAsync(x => x.Id == orderId && x.BuyerId == buyerId, cancellationToken);
        return order ?? throw new PaymentOperationException(404, "Order not found.");
    }

    private async Task<Order> OrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.Include(x => x.OrderItems).Include(x => x.Refunds)
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        return order ?? throw new PaymentOperationException(404, "Order not found.");
    }

    private string RequiredCurrency()
    {
        if (string.IsNullOrWhiteSpace(_options.Currency) || _options.Currency.Length != 3)
            throw new PaymentOperationException(503, "PayPal currency is not configured as a three-letter code.");
        return _options.Currency.ToUpperInvariant();
    }

    private static void EnsureMoney(Order order, decimal amount, string currency, string operation)
    {
        if (amount != order.Total() || !string.Equals(currency, order.Currency, StringComparison.Ordinal))
            throw new PaymentOperationException(502,
                $"PayPal's {operation} amount or currency did not match the order total.");
    }

    private static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static PaymentOperationException Invalid(string message) => new(400, message);
    private static PaymentOperationException Conflict(string message) => new(409, message);

    private static OrderResponse Map(Order order) => new(order.Id, order.OrderDate, order.Total(),
        order.Currency ?? string.Empty, order.PaymentStatus.ToString(), order.FulfilmentStatus.ToString(),
        order.PayPalOrderId, order.PayPalAuthorizationId, order.PayPalAuthorizationStatus,
        order.AuthorizationExpiresAt, order.PayPalCaptureId, order.PayPalCaptureStatus,
        order.CapturedAmount, order.PayPalFee, order.NetAmount, order.RefundedAmount,
        order.CardBrand, order.CardLastDigits,
        order.OrderItems.Select(x => new OrderItemResponse(x.ItemOrdered.CatalogItemId,
            x.ItemOrdered.ProductName, x.UnitPrice, x.Units)).ToArray(),
        order.Refunds.Select(Map).ToArray());
    private static RefundResponse Map(PaymentRefund refund) => new(refund.PayPalRefundId,
        refund.Status, refund.Amount, refund.Currency, refund.CreatedAt);
    private static PaymentMethodResponse Map(PaymentMethod method) => new(method.Id, method.Brand,
        method.LastDigits, method.Expiry, method.CreatedAt);

    private static void Add(Dictionary<string, Order> dictionary, string? key, Order order)
    {
        if (!string.IsNullOrWhiteSpace(key)) dictionary.TryAdd(key, order);
    }

    private static Order? Match(PayPalTransactionResult transaction,
        IReadOnlyDictionary<string, Order> resources, IReadOnlyDictionary<string, Order> references)
    {
        if (resources.TryGetValue(transaction.TransactionId, out var order)) return order;
        if (transaction.PayPalReferenceId is not null && resources.TryGetValue(transaction.PayPalReferenceId, out order)) return order;
        if (transaction.InvoiceId is not null && references.TryGetValue(transaction.InvoiceId, out order)) return order;
        if (transaction.CustomField is not null && references.TryGetValue(transaction.CustomField, out order)) return order;
        return null;
    }
}
