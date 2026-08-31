using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed class CommerceOperationLock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task<T> RunAsync<T>(string key, Func<Task<T>> action)
    {
        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try { return await action(); }
        finally { gate.Release(); }
    }
}

public sealed class CommerceService
{
    private static readonly Regex IdempotencyKeyPattern = new("^[A-Za-z0-9._:-]{1,64}$", RegexOptions.Compiled);
    private readonly CatalogContext _db;
    private readonly IPayPalClient _payPal;
    private readonly CommerceOperationLock _operationLock;

    public CommerceService(CatalogContext db, IPayPalClient payPal, CommerceOperationLock operationLock)
    {
        _db = db;
        _payPal = payPal;
        _operationLock = operationLock;
    }

    public async Task<OrderResponse> PlaceOrderAsync(string buyerId, PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items.Count == 0) throw BadRequest("At least one catalog item is required.");
        if (request.Items.Count > 100) throw BadRequest("An order cannot contain more than 100 item entries.");
        if (request.Items.Any(i => i.CatalogItemId <= 0 || i.Quantity is <= 0 or > 100))
            throw BadRequest("Catalog item IDs must be positive and quantities must be between 1 and 100.");
        ValidateShippingAddress(request.ShippingAddress);

        var grouped = request.Items.GroupBy(i => i.CatalogItemId)
            .Select(g => new { CatalogItemId = g.Key, Quantity = g.Sum(x => x.Quantity) }).ToList();
        if (grouped.Any(i => i.Quantity > 100)) throw BadRequest("The combined quantity for an item cannot exceed 100.");
        var ids = grouped.Select(i => i.CatalogItemId).ToList();
        var catalogItems = await _db.CatalogItems.Where(i => ids.Contains(i.Id)).ToListAsync(cancellationToken);
        if (catalogItems.Count != ids.Count) throw new CommerceException(404, "Catalog item not found", "One or more catalog items do not exist.");

        var orderItems = grouped.Select(input =>
        {
            var item = catalogItems.Single(c => c.Id == input.CatalogItemId);
            var unitPrice = decimal.Round(item.Price, 2, MidpointRounding.AwayFromZero);
            return new OrderItem(new CatalogItemOrdered(item.Id, item.Name, item.PictureUri), unitPrice, input.Quantity);
        }).ToList();
        var shipping = request.ShippingAddress;
        var order = new Order(buyerId,
            new Address(shipping.Street, shipping.City, shipping.State, shipping.Country, shipping.ZipCode), orderItems);
        if (order.Total() is <= 0 or > 9999999.99m)
            throw BadRequest("The order total must be between 0.01 and 9999999.99.");
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(order);
    }

    public Task<OrderResponse> PayAsync(string buyerId, int orderId, PayOrderRequest request,
        CancellationToken cancellationToken) => _operationLock.RunAsync($"order:{orderId}", async () =>
    {
        var order = await FindOwnedOrderAsync(buyerId, orderId, cancellationToken);
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            if (order.Status is OrderStatus.Authorized or OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
                return Map(order);
            throw Conflict("This order can no longer be paid.");
        }
        if ((request.Card is null) == (request.PaymentMethodId is null))
            throw BadRequest("Provide either card details or paymentMethodId, but not both.");

        if (order.AuthorizationId is not null)
        {
            var existing = await CallPayPal(() => _payPal.GetAuthorizationAsync(order.AuthorizationId, cancellationToken));
            order.Authorize(order.PayPalOrderId!, order.PayPalOrderStatus!, existing.AuthorizationId, existing.Status,
                existing.Amount, existing.Currency, existing.CreatedAt, existing.ExpiresAt);
            await _db.SaveChangesAsync(cancellationToken);
            if (existing.Status == "CREATED") return Map(order);
            throw Conflict($"PayPal authorization is {existing.Status}; payment cannot proceed yet.");
        }

        string? vaultId = null;
        if (request.PaymentMethodId is not null)
        {
            var method = await _db.PaymentMethods.SingleOrDefaultAsync(
                p => p.Id == request.PaymentMethodId.Value && p.OwnerId == buyerId, cancellationToken);
            if (method is null) throw new CommerceException(404, "Payment method not found", "The saved payment method does not exist.");
            vaultId = method.PayPalVaultId;
        }
        else
        {
            ValidateCard(request.Card!);
        }

        try
        {
            var authorization = await _payPal.AuthorizeOrderAsync(order.PaymentReference, order.Total(), request.Card,
                vaultId, order.PaymentAttempt + 1, cancellationToken);
            if (authorization.Amount != order.Total() || !authorization.Currency.Equals(_payPal.Currency, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("PayPal authorized an unexpected amount or currency.");
            order.Authorize(authorization.PayPalOrderId, authorization.PayPalOrderStatus,
                authorization.AuthorizationId, authorization.Status, authorization.Amount,
                authorization.Currency, authorization.CreatedAt, authorization.ExpiresAt);
            await _db.SaveChangesAsync(cancellationToken);
            if (authorization.Status != "CREATED") throw Conflict($"PayPal authorization is {authorization.Status}; payment cannot proceed yet.");
            return Map(order);
        }
        catch (PayPalException ex)
        {
            order.RecordPaymentFailure();
            await _db.SaveChangesAsync(cancellationToken);
            throw MapPayPal(ex);
        }
    });

    public Task<OrderResponse> FulfilAsync(int orderId, CancellationToken cancellationToken) =>
        _operationLock.RunAsync($"order:{orderId}", async () =>
    {
        var order = await FindOrderAsync(orderId, cancellationToken);
        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded) return Map(order);
        if (order.Status != OrderStatus.Authorized) throw Conflict("Only an authorized order can be fulfilled.");

        if (order.CaptureId is not null)
        {
            var existingCapture = await CallPayPal(() => _payPal.GetCaptureAsync(order.CaptureId, cancellationToken));
            RecordCapture(order, existingCapture);
            await _db.SaveChangesAsync(cancellationToken);
            if (existingCapture.Status != "COMPLETED") throw Conflict($"PayPal capture is {existingCapture.Status}; retry fulfilment after the status changes.");
            return Map(order);
        }

        var authorization = await CallPayPal(() => _payPal.GetAuthorizationAsync(order.AuthorizationId!, cancellationToken));
        if (authorization.Status != "CREATED")
            throw Conflict($"PayPal authorization is {authorization.Status}; it cannot be captured.");

        if (authorization.ExpiresAt <= DateTimeOffset.UtcNow)
            throw Conflict("The PayPal authorization is past its reauthorization window. Ask the shopper to pay the order again.");

        if (authorization.CreatedAt.AddDays(3) <= DateTimeOffset.UtcNow)
        {
            try
            {
                authorization = await _payPal.ReauthorizeAsync(order.PaymentReference, authorization.AuthorizationId,
                    order.Total(), cancellationToken);
            }
            catch (PayPalException ex) when (ex.Issue is "AUTHORIZATION_EXPIRED" or "AUTHORIZATION_VOIDED" or "MAX_AUTHORIZATION_COUNT_EXCEEDED")
            {
                throw Conflict($"The PayPal authorization can no longer be renewed ({ex.Issue}). Ask the shopper to pay the order again.");
            }
            catch (PayPalException ex) { throw MapPayPal(ex); }

            if (authorization.Status != "CREATED")
                throw Conflict($"The renewed PayPal authorization is {authorization.Status}; it cannot be captured.");
            order.Reauthorize(authorization.AuthorizationId, authorization.Status, authorization.Amount,
                authorization.CreatedAt, authorization.ExpiresAt);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var capture = await CallPayPal(() => _payPal.CaptureAsync(order.PaymentReference,
            authorization.AuthorizationId, order.Total(), cancellationToken));
        if (capture.Amount != order.Total()) throw new InvalidOperationException("PayPal captured an unexpected amount.");
        RecordCapture(order, capture);
        await _db.SaveChangesAsync(cancellationToken);
        if (capture.Status != "COMPLETED") throw Conflict($"PayPal capture is {capture.Status}; retry fulfilment after the status changes.");
        return Map(order);
    });

    public Task<OrderResponse> CancelAsync(int orderId, CancellationToken cancellationToken) =>
        _operationLock.RunAsync($"order:{orderId}", async () =>
    {
        var order = await FindOrderAsync(orderId, cancellationToken);
        if (order.Status == OrderStatus.Cancelled) return Map(order);
        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
            throw Conflict("A fulfilled order must be refunded rather than cancelled.");
        var status = "NOT_AUTHORIZED";
        if (order.AuthorizationId is not null)
            status = await CallPayPal(() => _payPal.VoidAsync(order.PaymentReference, order.AuthorizationId, cancellationToken));
        order.Cancel(status);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(order);
    });

    public Task<RefundOrderResponse> RefundAsync(string buyerId, int orderId, RefundOrderRequest request,
        CancellationToken cancellationToken) => _operationLock.RunAsync($"order:{orderId}", async () =>
    {
        if (!IdempotencyKeyPattern.IsMatch(request.IdempotencyKey))
            throw BadRequest("idempotencyKey must be 1-64 letters, digits, dots, underscores, colons, or hyphens.");
        if (request.Note?.Length > 255) throw BadRequest("Refund note cannot exceed 255 characters.");
        var order = await FindOwnedOrderAsync(buyerId, orderId, cancellationToken);
        var existing = order.Refunds.SingleOrDefault(r => r.IdempotencyKey == request.IdempotencyKey);
        if (existing is not null)
        {
            if (existing.Status == "PENDING")
            {
                var refreshed = await CallPayPal(() => _payPal.GetRefundAsync(existing.PayPalRefundId, cancellationToken));
                order.RefreshRefund(existing, refreshed.Status);
                await _db.SaveChangesAsync(cancellationToken);
            }
            return new RefundOrderResponse(existing.PayPalRefundId, Map(order));
        }
        if (order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded))
            throw Conflict("Only a fulfilled order can be refunded.");
        var remaining = order.CapturedAmount!.Value - order.RefundedAmount;
        var amount = request.Amount ?? remaining;
        if (amount <= 0 || amount > remaining || decimal.Round(amount, 2) != amount)
            throw BadRequest($"Refund amount must be positive, have at most two decimals, and not exceed {remaining:F2}.");
        var requestId = "eshop-refund-" + Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{order.PaymentReference}:{request.IdempotencyKey}"))).ToLowerInvariant()[..32];
        var result = await CallPayPal(() => _payPal.RefundAsync(requestId, order.CaptureId!, amount,
            order.PaymentReference, request.Note, cancellationToken));
        order.AddRefund(request.IdempotencyKey, result.RefundId, result.Amount, result.Currency, result.Status, result.CreatedAt);
        await _db.SaveChangesAsync(cancellationToken);
        if (result.Status is "FAILED" or "CANCELLED") throw Conflict($"PayPal refund is {result.Status}.");
        return new RefundOrderResponse(result.RefundId, Map(order));
    });

    public async Task<IReadOnlyList<OrderResponse>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken) =>
        (await _db.Orders.AsNoTracking().Where(o => o.BuyerId == buyerId)
            .Include(o => o.OrderItems).Include(o => o.Refunds).OrderByDescending(o => o.OrderDate)
            .ToListAsync(cancellationToken)).Select(Map).ToList();

    public Task<PaymentMethodResponse> SavePaymentMethodAsync(string buyerId, CardRequest card,
        CancellationToken cancellationToken) => _operationLock.RunAsync($"vault:{buyerId}", async () =>
    {
        ValidateCard(card);
        var saved = await CallPayPal(() => _payPal.SaveCardAsync(CustomerId(buyerId), card, cancellationToken));
        var method = new PaymentMethod(buyerId, saved.VaultId, saved.Brand, saved.Last4, saved.Expiry, DateTimeOffset.UtcNow);
        _db.PaymentMethods.Add(method);
        try { await _db.SaveChangesAsync(cancellationToken); }
        catch
        {
            try { await _payPal.DeletePaymentTokenAsync(saved.VaultId, cancellationToken); } catch { }
            throw;
        }
        return Map(method);
    });

    public async Task<IReadOnlyList<PaymentMethodResponse>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken) =>
        (await _db.PaymentMethods.AsNoTracking().Where(p => p.OwnerId == buyerId).OrderBy(p => p.Id)
            .ToListAsync(cancellationToken)).Select(Map).ToList();

    public Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken) =>
        _operationLock.RunAsync($"payment-method:{paymentMethodId}", async () =>
    {
        var method = await _db.PaymentMethods.SingleOrDefaultAsync(
            p => p.Id == paymentMethodId && p.OwnerId == buyerId, cancellationToken);
        if (method is null) throw new CommerceException(404, "Payment method not found", "The saved payment method does not exist.");
        await CallPayPal(async () => { await _payPal.DeletePaymentTokenAsync(method.PayPalVaultId, cancellationToken); return true; });
        _db.PaymentMethods.Remove(method);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    });

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to <= from) throw BadRequest("to must be later than from.");
        var transactions = await CallPayPal(() => _payPal.SearchTransactionsAsync(from, to, cancellationToken));
        var orders = await _db.Orders.AsNoTracking()
            .Where(o => o.PayPalOrderId != null &&
                ((o.AuthorizationCreatedAt != null && o.AuthorizationCreatedAt >= from && o.AuthorizationCreatedAt <= to) ||
                 (o.CapturedAt != null && o.CapturedAt >= from && o.CapturedAt <= to) ||
                 o.Refunds.Any(r => r.CreatedAt >= from && r.CreatedAt <= to)))
            .Include(o => o.Refunds).ToListAsync(cancellationToken);
        var matchedOrderIds = new HashSet<int>();
        var rows = transactions.Select(transaction =>
        {
            var order = orders.FirstOrDefault(o => transaction.InvoiceId == o.PaymentReference ||
                transaction.TransactionId == o.CaptureId || transaction.TransactionId == o.AuthorizationId ||
                transaction.ReferenceId == o.PayPalOrderId || o.Refunds.Any(r => r.PayPalRefundId == transaction.TransactionId));
            if (order is not null) matchedOrderIds.Add(order.Id);
            return new ReconciliationTransactionResponse(transaction.TransactionId, transaction.ReferenceId,
                transaction.EventCode, transaction.InvoiceId, transaction.TransactionTime, transaction.Amount, transaction.Currency,
                transaction.Fee, transaction.Status, order is null ? "PayPalOnly" : "Matched", order?.Id);
        }).ToList();
        var eshopOnly = orders.Where(o => !matchedOrderIds.Contains(o.Id)).Select(o =>
            new ReconciliationOrderResponse(o.Id, o.PaymentReference, o.PaymentStatus.ToString(),
                o.PayPalOrderId, o.AuthorizationId, o.CaptureId)).ToList();
        return new ReconciliationResponse { From = from, To = to, Transactions = rows, EshopOnlyOrders = eshopOnly };
    }

    private async Task<Order> FindOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.Where(o => o.Id == orderId && o.BuyerId == buyerId)
            .Include(o => o.OrderItems).Include(o => o.Refunds).SingleOrDefaultAsync(cancellationToken);
        return order ?? throw new CommerceException(404, "Order not found", "The order does not exist.");
    }

    private async Task<Order> FindOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.Where(o => o.Id == orderId)
            .Include(o => o.OrderItems).Include(o => o.Refunds).SingleOrDefaultAsync(cancellationToken);
        return order ?? throw new CommerceException(404, "Order not found", "The order does not exist.");
    }

    private static void RecordCapture(Order order, PayPalCaptureResult capture) => order.RecordCapture(
        capture.CaptureId, capture.Status, capture.Amount, capture.PayPalFee, capture.NetAmount, capture.CreatedAt);

    private static OrderResponse Map(Order order) => new()
    {
        OrderId = order.Id, OrderDate = order.OrderDate, Total = order.Total(), Currency = order.Currency,
        Status = order.Status.ToString(), PaymentStatus = order.PaymentStatus.ToString(),
        PayPalOrderId = order.PayPalOrderId, AuthorizationId = order.AuthorizationId,
        AuthorizationStatus = order.AuthorizationStatus, AuthorizationExpiresAt = order.AuthorizationExpiresAt,
        AuthorizedAmount = order.AuthorizedAmount, CaptureId = order.CaptureId,
        CaptureStatus = order.CaptureStatus, CapturedAmount = order.CapturedAmount,
        PayPalFee = order.PayPalFee, NetAmount = order.NetAmount, RefundedAmount = order.RefundedAmount,
        Items = order.OrderItems.Select(i => new OrderItemResponse(i.ItemOrdered.CatalogItemId,
            i.ItemOrdered.ProductName, i.UnitPrice, i.Units)).ToList(),
        Refunds = order.Refunds.Select(r => new RefundResponse(r.PayPalRefundId, r.Amount, r.Currency, r.Status, r.CreatedAt)).ToList()
    };
    private static PaymentMethodResponse Map(PaymentMethod method) =>
        new(method.Id, method.Brand, method.Last4, method.Expiry);

    private static void ValidateShippingAddress(ShippingAddressRequest address)
    {
        if (new[] { address.Street, address.City, address.Country, address.ZipCode }.Any(string.IsNullOrWhiteSpace))
            throw BadRequest("street, city, country, and zipCode are required.");
    }

    private static void ValidateCard(CardRequest card)
    {
        var number = card.Number.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(card.Name) || !Regex.IsMatch(number, "^[0-9]{13,19}$") ||
            !Regex.IsMatch(card.Expiry, "^[0-9]{4}-(0[1-9]|1[0-2])$") ||
            !Regex.IsMatch(card.SecurityCode, "^[0-9]{3,4}$") ||
            string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode) || card.BillingAddress.CountryCode.Length != 2)
            throw BadRequest("Card name, number, future expiry (YYYY-MM), security code, and two-letter billing countryCode are required.");
        if (!DateTime.TryParseExact(card.Expiry + "-01", "yyyy-MM-dd", null,
                System.Globalization.DateTimeStyles.None, out var expiry) || expiry.AddMonths(1) <= DateTime.UtcNow.Date)
            throw BadRequest("Card expiry must be in the future.");
    }

    private static string CustomerId(string buyerId) => "eshop_" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(buyerId.ToUpperInvariant()))).ToLowerInvariant()[..24];
    private static CommerceException BadRequest(string message) => new(400, "Invalid request", message);
    private static CommerceException Conflict(string message) => new(409, "Payment state conflict", message);
    private static CommerceException MapPayPal(PayPalException ex)
    {
        var status = ex.StatusCode is >= 400 and < 500 ? 422 : 502;
        var issue = ex.Issue is null ? string.Empty : $" ({ex.Issue})";
        var debug = ex.DebugId is null ? string.Empty : $" PayPal debug ID: {ex.DebugId}.";
        return new CommerceException(status, "PayPal request failed", $"PayPal could not complete the operation{issue}.{debug}");
    }
    private static async Task<T> CallPayPal<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch (PayPalException ex) { throw MapPayPal(ex); }
    }
}
