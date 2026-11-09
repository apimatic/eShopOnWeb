using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class Quote
{
    public decimal Subtotal { get; set; }
    public decimal Fee { get; set; }
    public decimal Total { get; set; }
    public string Method { get; set; } = "";
    public string PaymentMethodId { get; set; } = "";
}

public class ShopOrderService
{
    private const int MAX_UNITS = 1000;

    private readonly PaymentsDbContext _db;
    private readonly CatalogContext _catalog;
    private readonly StaxClient _stax;
    private readonly PaymentMethodService _methods;

    public ShopOrderService(PaymentsDbContext db, CatalogContext catalog, StaxClient stax, PaymentMethodService methods)
    {
        _db = db;
        _catalog = catalog;
        _stax = stax;
        _methods = methods;
    }

    // ---------------------------------------------------------------- orders

    public async Task<ShopOrder> PlaceAsync(string email, Body body, CancellationToken ct = default)
    {
        var requested = new Dictionary<int, int>();
        var items = body.Array("items", "lines", "orderItems");
        if (items.Count == 0) throw ApiException.BadRequest("items must list at least one catalog item.");
        foreach (var i in items)
        {
            var id = i.Int("catalogItemId", "itemId", "productId", "id");
            var units = i.Int("units", "quantity", "qty") ?? 1;
            if (id == null) throw ApiException.BadRequest("Each item needs a catalogItemId.");
            if (units < 1 || units > MAX_UNITS) throw ApiException.BadRequest($"units must be between 1 and {MAX_UNITS}.");
            requested[id.Value] = requested.GetValueOrDefault(id.Value) + units;
            if (requested[id.Value] > MAX_UNITS) throw ApiException.BadRequest($"units must be between 1 and {MAX_UNITS}.");
        }

        var ids = requested.Keys.ToList();
        var found = await _catalog.CatalogItems.AsNoTracking().Where(c => ids.Contains(c.Id)).ToListAsync(ct);
        var missing = ids.Except(found.Select(f => f.Id)).ToList();
        if (missing.Count > 0)
            throw ApiException.NotFound("Catalog item(s) not found: " + string.Join(", ", missing));

        var order = new ShopOrder { BuyerEmail = email, CreatedAt = DateTimeOffset.UtcNow };
        foreach (var item in found.OrderBy(f => f.Id))
        {
            order.Lines.Add(new ShopOrderLine
            {
                CatalogItemId = item.Id,
                ProductName = item.Name,
                UnitPrice = item.Price,
                Units = requested[item.Id]
            });
        }
        order.Subtotal = order.Lines.Sum(l => l.UnitPrice * l.Units);
        if (order.Subtotal <= 0) throw ApiException.Unprocessable("The order has nothing to pay for.");

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);
        return order;
    }

    public async Task<ShopOrder> GetOwnedAsync(string email, int orderId, CancellationToken ct = default)
    {
        var order = await _db.Orders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == orderId && o.BuyerEmail == email, ct);
        if (order == null) throw ApiException.NotFound("No such order.");
        return order;
    }

    public async Task<ShopOrder> GetAnyAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _db.Orders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order == null) throw ApiException.NotFound("No such order.");
        return order;
    }

    // -------------------------------------------------------------- checkout

    public async Task<Quote> CheckoutAsync(string email, int orderId, string? paymentMethodId, CancellationToken ct = default)
    {
        var order = await GetOwnedAsync(email, orderId, ct);
        EnsurePayable(order);
        return await QuoteAsync(email, order, paymentMethodId, ct);
    }

    private static void EnsurePayable(ShopOrder order)
    {
        if (order.PaymentState == PaymentState.PAID) throw ApiException.Conflict("The order is already paid.");
        if (order.PaymentState == PaymentState.PENDING)
            throw ApiException.Conflict("The order's bank payment is already on its way and awaiting settlement.");
        if (order.PaymentState == PaymentState.PROCESSING)
            throw ApiException.Conflict("A payment for this order is being processed.");
        if (order.RefundStatus != RefundState.NONE && order.RefundStatus != RefundState.FAILED)
            throw ApiException.Conflict("The order has been refunded.");
    }

    private async Task<Quote> QuoteAsync(string email, ShopOrder order, string? paymentMethodId, CancellationToken ct)
    {
        if (paymentMethodId == null) throw ApiException.BadRequest("paymentMethodId is required.");
        var pm = await _methods.RequireOwnActiveAsync(email, paymentMethodId, ct);
        var kind = pm["method"]?.GetValue<string>() ?? "card";

        // The shop passes card processing fees on to shoppers: ask Stax what the surcharge is.
        // Bank payments carry no card fee.
        decimal fee = 0m;
        if (kind == "card")
        {
            var review = await _stax.GetAsync("surcharge/review", new Dictionary<string, string?>
            {
                ["payment_method_id"] = paymentMethodId,
                ["total"] = order.Subtotal.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
            }, ct);
            var amount = review["surcharge_amount"]?.GetValue<decimal>() ?? 0m;
            fee = decimal.Round(Math.Max(0m, amount), 2, MidpointRounding.AwayFromZero);
        }

        return new Quote
        {
            Subtotal = order.Subtotal,
            Fee = fee,
            Total = order.Subtotal + fee,
            Method = kind,
            PaymentMethodId = paymentMethodId
        };
    }

    // ------------------------------------------------------------------- pay

    public async Task<ShopOrder> PayAsync(string email, int orderId, string? paymentMethodId, decimal? expectedTotal, CancellationToken ct = default)
    {
        var order = await GetOwnedAsync(email, orderId, ct);
        EnsurePayable(order);
        var quote = await QuoteAsync(email, order, paymentMethodId, ct);
        if (expectedTotal != null && decimal.Round(expectedTotal.Value, 2) != quote.Total)
            throw ApiException.Conflict($"The amount to pay is now {quote.Total:0.00}, not {expectedTotal:0.00}. Check out again.");

        // Claim the order; a concurrent pay attempt loses the optimistic-concurrency race.
        order.PaymentState = PaymentState.PROCESSING;
        order.Version = Guid.NewGuid();
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw ApiException.Conflict("A payment for this order is already being processed.");
        }

        JsonNode txn;
        try
        {
            txn = await _stax.PostAsync("charge", new
            {
                payment_method_id = quote.PaymentMethodId,
                total = quote.Total,
                pre_auth = false,
                // Same attempt number => same key, so retrying after a lost response cannot double charge.
                idempotency_id = $"eshop-order-{order.Id}-attempt-{order.PayAttempts}",
                meta = new
                {
                    subtotal = quote.Subtotal,
                    fee = quote.Fee,
                    reference = "eShopOnWeb order " + order.Id,
                    lineItems = order.Lines.Select(l => new { item = l.ProductName, quantity = l.Units, price = l.UnitPrice }).ToArray()
                }
            }, ct);
        }
        catch
        {
            order.PaymentState = PaymentState.UNPAID;
            order.Version = Guid.NewGuid();
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        var success = txn["success"]?.GetValue<bool>() ?? false;
        order.StaxTransactionId = txn["id"]?.GetValue<string>();
        order.PaymentMethodId = quote.PaymentMethodId;
        order.PaymentMethodKind = quote.Method;
        order.Fee = quote.Fee;
        order.ChargedTotal = quote.Total;
        order.Version = Guid.NewGuid();

        if (!success)
        {
            var reason = txn["message"]?.GetValue<string>() ?? "The payment was declined.";
            order.PaymentState = PaymentState.UNPAID;
            order.FailureReason = reason;
            order.PayAttempts++;
            order.StaxTransactionId = null;
            await _db.SaveChangesAsync(CancellationToken.None);
            throw new ApiException(402, "Payment declined: " + reason);
        }

        order.FailureReason = null;
        if (quote.Method == "bank")
        {
            // The bank settles a few days later; the order is paid only then.
            order.PaymentState = IsSettled(txn) ? PaymentState.PAID : PaymentState.PENDING;
        }
        else
        {
            order.PaymentState = PaymentState.PAID;
        }
        if (order.PaymentState == PaymentState.PAID) order.PaidAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(CancellationToken.None);
        return order;
    }

    // ------------------------------------------------------ bank settlement

    public static bool IsSettled(JsonNode txn)
    {
        if (txn["settled_at"] != null) return true;
        return txn["is_refundable"] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
    }

    /// <summary>Brings a pending bank payment up to date with what Stax now reports.</summary>
    public async Task RefreshPaymentAsync(ShopOrder order, CancellationToken ct = default)
    {
        if (order.PaymentState != PaymentState.PENDING || order.StaxTransactionId == null) return;
        var txn = await _stax.GetAsync($"transaction/{order.StaxTransactionId}", null, ct);
        var success = txn["success"]?.GetValue<bool>() ?? true;
        if (!success)
        {
            order.PaymentState = PaymentState.FAILED;
            order.FailureReason = txn["message"]?.GetValue<string>() ?? "The bank returned the payment.";
        }
        else if (IsSettled(txn))
        {
            order.PaymentState = PaymentState.PAID;
            order.PaidAt = DateTimeOffset.UtcNow;
        }
        else return;
        order.Version = Guid.NewGuid();
        await _db.SaveChangesAsync(ct);
    }

    // --------------------------------------------------------------- refunds

    public async Task<ShopOrder> RefundAsync(int orderId, CancellationToken ct = default)
    {
        var order = await GetAnyAsync(orderId, ct);
        if (order.StaxTransactionId == null ||
            order.PaymentState is not (PaymentState.PAID or PaymentState.PENDING))
            throw ApiException.Conflict("The order has not been paid, so there is nothing to refund.");
        if (order.RefundStatus == RefundState.REFUNDED) throw ApiException.Conflict("The order is already refunded.");
        if (order.RefundStatus == RefundState.PENDING)
            throw ApiException.Conflict("A refund is already pending; it is carried out as soon as Stax allows it.");

        order.RefundStatus = RefundState.PENDING;
        order.RefundRequestedAt = DateTimeOffset.UtcNow;
        order.RefundNote = null;
        order.NextRefundAttemptAt = null;
        order.Version = Guid.NewGuid();
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw ApiException.Conflict("The order changed; try again."); }

        try
        {
            await TryRefundAsync(order, ct);
        }
        catch (ApiException ex) when (order.RefundStatus == RefundState.FAILED && ex is not StaxException { UpstreamStatus: 0 or >= 500 })
        {
            // Stax refused outright: nothing was recorded as requested.
            order.RefundStatus = RefundState.NONE;
            order.RefundRequestedAt = null;
            order.Version = Guid.NewGuid();
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        return order;
    }

    /// <summary>
    /// One attempt to give the shopper everything back. Sets Refunded, leaves Pending when Stax
    /// cannot do it yet (e.g. ACH refunds are refused until 5 business days after the payment),
    /// or Failed with a note and throws when Stax refuses for good.
    /// </summary>
    public async Task TryRefundAsync(ShopOrder order, CancellationToken ct = default)
    {
        try
        {
            var txn = await _stax.GetAsync($"transaction/{order.StaxTransactionId}", null, ct);
            var total = txn["total"]?.GetValue<decimal>() ?? order.ChargedTotal;
            var refunded = txn["total_refunded"] is JsonValue rv && rv.TryGetValue<decimal>(out var r) ? r : 0m;
            var voided = txn["is_voided"] is JsonValue vv && vv.TryGetValue<bool>(out var vb) && vb;

            if (!voided && refunded < total)
            {
                var remaining = decimal.Round(total - refunded, 2);
                var result = await _stax.PostAsync($"transaction/{order.StaxTransactionId}/void-or-refund", new { total = remaining }, ct);
                var check = await _stax.GetAsync($"transaction/{order.StaxTransactionId}", null, ct);
                voided = check["is_voided"] is JsonValue v2 && v2.TryGetValue<bool>(out var b2) && b2;
                refunded = check["total_refunded"] is JsonValue r2 && r2.TryGetValue<decimal>(out var d2) ? d2 : refunded;
                if (!voided && refunded < total)
                {
                    var ok = result["success"] is JsonValue sv && sv.TryGetValue<bool>(out var sb) && sb;
                    var isRefund = result["type"]?.GetValue<string>() is "refund" or "void";
                    // A refund line that Stax accepted but has not applied to the parent yet counts.
                    if (!(ok && isRefund && result["id"]?.GetValue<string>() != order.StaxTransactionId))
                        throw new StaxException(422, "The refund is not yet complete at Stax.");
                }
            }

            order.RefundStatus = RefundState.REFUNDED;
            order.RefundedAt = DateTimeOffset.UtcNow;
            order.RefundNote = null;
            order.NextRefundAttemptAt = null;
        }
        catch (StaxException ex)
        {
            if (ex.UpstreamStatus == 0 || ex.UpstreamStatus >= 500 || IsTiming(ex.Message))
            {
                order.RefundStatus = RefundState.PENDING;
                order.RefundNote = ex.Message;
                order.NextRefundAttemptAt = DateTimeOffset.UtcNow.AddMinutes(order.PaymentMethodKind == "bank" ? 60 : 10);
                order.Version = Guid.NewGuid();
                await _db.SaveChangesAsync(CancellationToken.None);
                return;
            }
            order.RefundStatus = RefundState.FAILED;
            order.RefundNote = ex.Message;
            order.NextRefundAttemptAt = null;
            order.Version = Guid.NewGuid();
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        order.Version = Guid.NewGuid();
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private static bool IsTiming(string message)
    {
        var m = message.ToLowerInvariant();
        return m.Contains("unavailable until") || m.Contains("not yet") || m.Contains("business day")
               || m.Contains("settle") || m.Contains("batch") || m.Contains("processing") || m.Contains("try again");
    }

    // --------------------------------------------------------------- mapping

    public static object MapForShopper(ShopOrder o) => new
    {
        id = o.Id,
        orderId = o.Id,
        createdAt = o.CreatedAt,
        items = o.Lines.Select(l => new { catalogItemId = l.CatalogItemId, name = l.ProductName, unitPrice = l.UnitPrice, units = l.Units }),
        subtotal = o.Subtotal,
        fee = o.PaymentState is PaymentState.PAID or PaymentState.PENDING ? o.Fee : (decimal?)null,
        total = o.PaymentState is PaymentState.PAID or PaymentState.PENDING ? o.ChargedTotal : o.Subtotal,
        paid = o.PaymentState == PaymentState.PAID,
        paymentStatus = o.PaymentState.ToLowerInvariant(),
        paymentMethod = o.PaymentMethodKind,
        paidAt = o.PaidAt,
        failureReason = o.FailureReason,
        refundStatus = o.RefundStatus
    };

    public static object MapForStaff(ShopOrder o) => new
    {
        id = o.Id,
        orderId = o.Id,
        buyerEmail = o.BuyerEmail,
        createdAt = o.CreatedAt,
        items = o.Lines.Select(l => new { catalogItemId = l.CatalogItemId, name = l.ProductName, unitPrice = l.UnitPrice, units = l.Units }),
        subtotal = o.Subtotal,
        fee = o.Fee,
        total = o.PaymentState is PaymentState.PAID or PaymentState.PENDING ? o.ChargedTotal : o.Subtotal,
        paid = o.PaymentState == PaymentState.PAID,
        paymentStatus = o.PaymentState.ToLowerInvariant(),
        paymentMethod = o.PaymentMethodKind,
        paymentMethodId = o.PaymentMethodId,
        transactionId = o.StaxTransactionId,
        paidAt = o.PaidAt,
        failureReason = o.FailureReason,
        refundStatus = o.RefundStatus,
        refundRequestedAt = o.RefundRequestedAt,
        refundedAt = o.RefundedAt,
        refundNote = o.RefundNote
    };
}
