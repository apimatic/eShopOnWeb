using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.eShopWeb.PublicApi.Stax;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public record Quote(decimal Subtotal, decimal Fee, decimal Total);

public class OrderPaymentService
{
    private static readonly TimeSpan RefundRetryEvery = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RefundGiveUpAfter = TimeSpan.FromDays(30);
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();

    private readonly StaxContext _db;
    private readonly CatalogContext _catalog;
    private readonly StaxClient _stax;
    private readonly PaymentMethodService _methods;
    private readonly ILogger<OrderPaymentService> _logger;

    public OrderPaymentService(StaxContext db, CatalogContext catalog, StaxClient stax,
        PaymentMethodService methods, ILogger<OrderPaymentService> logger)
    {
        _db = db;
        _catalog = catalog;
        _stax = stax;
        _methods = methods;
        _logger = logger;
    }

    private static SemaphoreSlim LockFor(int orderId) => OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));

    // ---- orders ----

    public async Task<ShopOrder> CreateOrderAsync(string email, IReadOnlyList<(int itemId, int units)> lines, CancellationToken ct)
    {
        if (lines.Count == 0) throw ApiException.BadRequest("An order needs at least one item.");

        var wanted = lines.GroupBy(l => l.itemId).Select(g => (itemId: g.Key, units: g.Sum(x => x.units))).ToList();
        foreach (var (itemId, units) in wanted)
        {
            if (units < 1 || units > 1000) throw ApiException.BadRequest($"Quantity for item {itemId} must be between 1 and 1000.");
        }

        var ids = wanted.Select(w => w.itemId).ToList();
        var items = await _catalog.CatalogItems.Where(i => ids.Contains(i.Id)).ToListAsync(ct);
        var order = new ShopOrder { ShopperEmail = email, CreatedAt = DateTimeOffset.UtcNow };
        foreach (var (itemId, units) in wanted)
        {
            var item = items.FirstOrDefault(i => i.Id == itemId)
                ?? throw ApiException.Unprocessable($"Catalog item {itemId} does not exist.");
            order.Items.Add(new ShopOrderItem
            {
                CatalogItemId = item.Id,
                ProductName = item.Name,
                UnitPrice = item.Price,
                Units = units,
            });
        }
        order.Subtotal = Math.Round(order.Items.Sum(i => i.UnitPrice * i.Units), 2);
        if (order.Subtotal <= 0) throw ApiException.Unprocessable("An order must have a total above zero.");
        order.Total = order.Subtotal;

        _db.ShopOrders.Add(order);
        await _db.SaveChangesAsync(ct);
        return order;
    }

    public async Task<ShopOrder> GetOwnedOrderAsync(string email, int orderId, CancellationToken ct)
    {
        var order = await _db.ShopOrders.Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.ShopperEmail == email, ct);
        return order ?? throw ApiException.NotFound("There is no such order.");
    }

    public async Task<ShopOrder> GetOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _db.ShopOrders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId, ct);
        return order ?? throw ApiException.NotFound("There is no such order.");
    }

    // ---- checkout / pay ----

    public async Task<(ShopOrder order, SavedPaymentMethod pm, Quote quote)> CheckoutAsync(
        string email, int orderId, string paymentMethodId, CancellationToken ct)
    {
        var order = await GetOwnedOrderAsync(email, orderId, ct);
        EnsurePayable(order);
        var pm = await _methods.RequireActiveAsync(email, paymentMethodId, ct);
        return (order, pm, await QuoteAsync(order, pm, ct));
    }

    private static void EnsurePayable(ShopOrder order)
    {
        switch (order.Status)
        {
            case OrderStatuses.Paid:
                throw ApiException.Conflict("This order has already been paid.");
            case OrderStatuses.PendingSettlement:
                throw ApiException.Conflict("A bank payment for this order is already waiting to settle.");
            case OrderStatuses.Processing:
                throw ApiException.Conflict("A payment for this order is already being processed.");
            case OrderStatuses.Refunded:
                throw ApiException.Conflict("This order has been refunded.");
        }
        if (order.RefundStatus != RefundStatuses.None)
        {
            throw ApiException.Conflict("This order has a refund in progress.");
        }
    }

    /// <summary>The fee is whatever Stax says the shop's surcharge is for this payment method and amount.</summary>
    public async Task<Quote> QuoteAsync(ShopOrder order, SavedPaymentMethod pm, CancellationToken ct)
    {
        var s = await _stax.ReviewSurchargeAsync(pm.Id, order.Subtotal, ct);
        var fee = Math.Round(Num(s["surcharge_amount"]) ?? 0m, 2);
        var withSurcharge = Num(s["total_with_surcharge_amount"]);
        var total = withSurcharge is > 0 ? Math.Round(withSurcharge.Value, 2) : order.Subtotal + fee;
        fee = total - order.Subtotal;
        if (fee < 0) { fee = 0; total = order.Subtotal; }
        return new Quote(order.Subtotal, fee, total);
    }

    private static decimal? Num(JsonNode? n)
    {
        if (n == null) return null;
        try { return n.GetValue<decimal>(); }
        catch { return decimal.TryParse(n.ToString(), System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null; }
    }

    public async Task<ShopOrder> PayAsync(string email, int orderId, string paymentMethodId, CancellationToken ct)
    {
        var gate = LockFor(orderId);
        await gate.WaitAsync(ct);
        try
        {
            var order = await GetOwnedOrderAsync(email, orderId, ct);
            EnsurePayable(order);
            var pm = await _methods.RequireActiveAsync(email, paymentMethodId, ct);
            var quote = await QuoteAsync(order, pm, ct);

            order.Status = OrderStatuses.Processing;
            order.PaymentAttempts++;
            // Stax idempotency ids are shared across the whole merchant account, so they must be unique.
            order.PaymentIdempotencyKey = $"eshop-{order.Id}-{Guid.NewGuid():N}";
            order.ProcessingStartedAt = DateTimeOffset.UtcNow;
            order.PaymentMethodId = pm.Id;
            order.PaymentMethodKind = pm.Method;
            order.Fee = quote.Fee;
            order.Total = quote.Total;
            await _db.SaveChangesAsync(ct);

            JsonNode tx;
            try
            {
                tx = await _stax.ChargeAsync(pm.Id, quote.Total,
                    new { subtotal = quote.Subtotal, surchargeAmount = quote.Fee, reference = $"eshop-order-{order.Id}" },
                    order.PaymentIdempotencyKey, ct);
            }
            catch (ApiException ex)
            {
                await ResetToCreatedAsync(order, ex.Message);
                throw;
            }

            if (tx["success"]?.GetValue<bool>() != true)
            {
                var reason = tx["message"]?.ToString()
                             ?? tx["error_description"]?.ToString()
                             ?? "The payment was declined.";
                await ResetToCreatedAsync(order, reason);
                throw new ApiException(402, "The payment was declined: " + reason);
            }

            order.StaxTransactionId = tx["id"]!.GetValue<string>();
            order.LastPaymentError = null;
            ApplyTransaction(order, tx);
            await _db.SaveChangesAsync(ct);
            return order;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task ResetToCreatedAsync(ShopOrder order, string reason)
    {
        order.Status = OrderStatuses.Created;
        order.LastPaymentError = reason;
        order.Fee = 0;
        order.Total = order.Subtotal;
        order.PaymentMethodId = null;
        order.PaymentMethodKind = null;
        await _db.SaveChangesAsync();
    }

    /// <summary>A card payment is paid once Stax accepts it; a bank payment only once the bank has settled it.</summary>
    private static void ApplyTransaction(ShopOrder order, JsonNode tx)
    {
        var settled = !string.IsNullOrEmpty(tx["settled_at"]?.ToString());
        if (order.PaymentMethodKind == "bank" && !settled)
        {
            order.Status = OrderStatuses.PendingSettlement;
        }
        else
        {
            order.Status = OrderStatuses.Paid;
            order.PaidAt ??= DateTimeOffset.UtcNow;
        }
    }

    // ---- keeping state in step with Stax ----

    /// <summary>
    /// An order stuck in "processing" (the app stopped between charging and saving) is settled by repeating
    /// the charge with the same idempotency key: Stax returns the original transaction if there was one.
    /// </summary>
    public async Task RecoverProcessingAsync(ShopOrder order, CancellationToken ct)
    {
        if (order.Status != OrderStatuses.Processing || order.ProcessingStartedAt == null ||
            DateTimeOffset.UtcNow - order.ProcessingStartedAt < TimeSpan.FromMinutes(2)) return;
        var gate = LockFor(order.Id);
        if (!await gate.WaitAsync(0, ct)) return;
        try
        {
            if (order.PaymentMethodId == null || order.PaymentIdempotencyKey == null)
            {
                await ResetToCreatedAsync(order, "The payment did not complete.");
                return;
            }
            var tx = await _stax.ChargeAsync(order.PaymentMethodId, order.Total,
                new { subtotal = order.Subtotal, surchargeAmount = order.Fee, reference = $"eshop-order-{order.Id}" },
                order.PaymentIdempotencyKey, ct);
            if (tx["success"]?.GetValue<bool>() == true)
            {
                order.StaxTransactionId = tx["id"]!.GetValue<string>();
                ApplyTransaction(order, tx);
                await _db.SaveChangesAsync(ct);
            }
            else
            {
                await ResetToCreatedAsync(order, "The payment did not complete.");
            }
        }
        catch (ApiException ex)
        {
            _logger.LogWarning("Could not recover payment of order {OrderId}: {Message}", order.Id, ex.Message);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Checks a pending bank payment with Stax and moves the order along. Never throws on Stax trouble.</summary>
    public async Task SyncPaymentAsync(ShopOrder order, CancellationToken ct)
    {
        if (order.Status != OrderStatuses.PendingSettlement || order.StaxTransactionId == null) return;
        try
        {
            var tx = await _stax.GetTransactionAsync(order.StaxTransactionId, ct);
            var voided = tx["is_voided"]?.GetValue<bool>() == true;
            var failed = tx["success"]?.GetValue<bool>() == false;
            if (failed && !voided)
            {
                order.Status = OrderStatuses.Created;
                order.LastPaymentError = "The bank payment failed: " + (tx["message"]?.ToString() ?? "returned by the bank");
                order.StaxTransactionId = null;
                order.Fee = 0;
                order.Total = order.Subtotal;
            }
            else if (!voided && !string.IsNullOrEmpty(tx["settled_at"]?.ToString()))
            {
                order.Status = OrderStatuses.Paid;
                order.PaidAt = DateTimeOffset.UtcNow;
            }
            await _db.SaveChangesAsync(ct);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning("Could not refresh payment of order {OrderId}: {Message}", order.Id, ex.Message);
        }
    }

    // ---- refunds ----

    public async Task<ShopOrder> RefundAsync(int orderId, CancellationToken ct)
    {
        var gate = LockFor(orderId);
        await gate.WaitAsync(ct);
        try
        {
            var order = await GetOrderAsync(orderId, ct);
            if (order.RefundStatus == RefundStatuses.Refunded)
                throw ApiException.Conflict("This order has already been refunded.");
            if (order.RefundStatus == RefundStatuses.Pending) return order;

            if (order.Status is not (OrderStatuses.Paid or OrderStatuses.PendingSettlement) || order.StaxTransactionId == null)
                throw ApiException.Conflict("Only an order that has been paid can be refunded.");

            order.RefundStatus = RefundStatuses.Pending;
            order.RefundRequestedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            await TryRefundAsync(order, ct, force: true);
            return order;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Returns the whole amount the shopper paid, fee included. When Stax cannot do it yet
    /// (for example the transaction is not refundable until the batch closes) the refund stays
    /// pending and is retried in the background until it goes through.
    /// </summary>
    public async Task TryRefundAsync(ShopOrder order, CancellationToken ct, bool force = false)
    {
        if (order.RefundStatus != RefundStatuses.Pending || order.StaxTransactionId == null) return;
        if (!force && order.RefundLastAttemptAt != null &&
            DateTimeOffset.UtcNow - order.RefundLastAttemptAt < RefundRetryEvery) return;
        order.RefundAttempts++;
        order.RefundLastAttemptAt = DateTimeOffset.UtcNow;
        try
        {
            var tx = await _stax.VoidOrRefundAsync(order.StaxTransactionId, order.Total, ct);
            if (tx["success"]?.GetValue<bool>() == false)
            {
                order.RefundMessage = tx["message"]?.ToString() ?? "Stax has not completed the refund yet.";
            }
            else
            {
                CompleteRefund(order, tx["id"]?.GetValue<string>());
            }
        }
        catch (ApiException ex) when (ex.StatusCode == 422)
        {
            var m = ex.Message.ToLowerInvariant();
            if (m.Contains("already been voided") || m.Contains("already been refunded") || m.Contains("fully refunded"))
            {
                CompleteRefund(order, null);
            }
            else
            {
                order.RefundMessage = ex.Message;
                if (DateTimeOffset.UtcNow - order.RefundRequestedAt > RefundGiveUpAfter)
                    order.RefundStatus = RefundStatuses.Failed;
            }
        }
        catch (ApiException ex)
        {
            order.RefundMessage = ex.Message;
        }
        await _db.SaveChangesAsync(ct);
    }

    private static void CompleteRefund(ShopOrder order, string? refundTransactionId)
    {
        order.RefundStatus = RefundStatuses.Refunded;
        order.RefundTransactionId = refundTransactionId;
        order.RefundMessage = null;
        order.Status = OrderStatuses.Refunded;
    }

    // ---- views ----

    public static object ToShopperDto(ShopOrder o) => new
    {
        orderId = o.Id,
        status = o.Status,
        paid = o.Status == OrderStatuses.Paid,
        subtotal = o.Subtotal,
        fee = o.Fee,
        total = o.Total,
        paymentMethodId = o.PaymentMethodId,
        refundStatus = o.RefundStatus,
        createdAt = o.CreatedAt,
        paidAt = o.PaidAt,
        lastPaymentError = o.LastPaymentError,
        items = o.Items.Select(i => new
        {
            catalogItemId = i.CatalogItemId,
            name = i.ProductName,
            unitPrice = i.UnitPrice,
            quantity = i.Units,
        }),
    };

    public static object ToStaffDto(ShopOrder o) => new
    {
        orderId = o.Id,
        shopperEmail = o.ShopperEmail,
        status = o.Status,
        paid = o.Status == OrderStatuses.Paid,
        subtotal = o.Subtotal,
        fee = o.Fee,
        total = o.Total,
        paymentMethodId = o.PaymentMethodId,
        paymentMethodType = o.PaymentMethodKind,
        transactionId = o.StaxTransactionId,
        createdAt = o.CreatedAt,
        paidAt = o.PaidAt,
        refundStatus = o.RefundStatus,
        refundRequestedAt = o.RefundRequestedAt,
        refundTransactionId = o.RefundTransactionId,
        refundMessage = o.RefundMessage,
        items = o.Items.Select(i => new
        {
            catalogItemId = i.CatalogItemId,
            name = i.ProductName,
            unitPrice = i.UnitPrice,
            quantity = i.Units,
        }),
    };
}
