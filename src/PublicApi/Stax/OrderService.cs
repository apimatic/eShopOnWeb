using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Stax;

namespace Microsoft.eShopWeb.PublicApi.Stax;

public record OrderItemRequest(int CatalogItemId, int Quantity);
public record CreateOrderRequest(List<OrderItemRequest>? Items);
public record PaymentMethodRequest(string? PaymentMethodId, decimal? ExpectedTotal = null);

public record OrderLineDto(int CatalogItemId, string Name, decimal UnitPrice, int Units);

public record OrderDto(
    int Id,
    string Status,
    bool Paid,
    string PaymentStatus,
    List<OrderLineDto> Items,
    decimal Subtotal,
    decimal Fee,
    decimal Total,
    string? PaymentMethodId,
    string? PaymentMethodType,
    string RefundStatus,
    DateTime CreatedAt,
    DateTime? PaidAt);

public record AdminOrderDto(
    int Id,
    string BuyerEmail,
    string Status,
    bool Paid,
    string PaymentStatus,
    List<OrderLineDto> Items,
    decimal Subtotal,
    decimal Fee,
    decimal Total,
    string? PaymentMethodId,
    string? PaymentMethodType,
    string? TransactionId,
    string RefundStatus,
    decimal RefundedAmount,
    string? RefundNote,
    DateTime? RefundRequestedAt,
    DateTime? RefundCompletedAt,
    DateTime CreatedAt,
    DateTime? PaidAt);

public record CheckoutDto(int OrderId, string PaymentMethodId, string PaymentMethodType, decimal Subtotal, decimal Fee, decimal Total);

public class ShopOrderService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> Locks = new();
    private static readonly TimeSpan RefundGiveUp = TimeSpan.FromDays(30);

    private readonly StaxContext _db;
    private readonly CatalogContext _catalog;
    private readonly StaxClient _stax;
    private readonly PaymentMethodService _methods;

    public ShopOrderService(StaxContext db, CatalogContext catalog, StaxClient stax, PaymentMethodService methods)
    {
        _db = db;
        _catalog = catalog;
        _stax = stax;
        _methods = methods;
    }

    public static decimal Round(decimal d) => Math.Round(d, 2, MidpointRounding.AwayFromZero);
    private static string Money(decimal d) => d.ToString("0.00", CultureInfo.InvariantCulture);

    // ---- orders ---------------------------------------------------------------
    public async Task<OrderDto> CreateAsync(string email, CreateOrderRequest request, CancellationToken ct = default)
    {
        if (request.Items == null || request.Items.Count == 0) throw ApiException.BadRequest("An order needs at least one item.");
        if (request.Items.Count > 100) throw ApiException.BadRequest("An order can have at most 100 different items.");
        if (request.Items.Any(i => i.Quantity < 1 || i.Quantity > 1000)) throw ApiException.BadRequest("Each quantity must be between 1 and 1000.");

        var wanted = request.Items.GroupBy(i => i.CatalogItemId).Select(g => (Id: g.Key, Units: g.Sum(i => i.Quantity))).ToList();
        var ids = wanted.Select(w => w.Id).ToList();
        var items = await _catalog.CatalogItems.Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var missing = ids.Where(id => !items.ContainsKey(id)).ToList();
        if (missing.Count > 0) throw ApiException.Unprocessable("Catalog item(s) not found: " + string.Join(", ", missing));

        var order = new ShopOrder { BuyerEmail = PaymentMethodService.NormalizeEmail(email) };
        foreach (var (id, units) in wanted)
        {
            order.Lines.Add(new ShopOrderLine { CatalogItemId = id, ProductName = items[id].Name, UnitPrice = items[id].Price, Units = units });
        }
        order.Subtotal = Round(order.Lines.Sum(l => l.UnitPrice * l.Units));
        order.Total = order.Subtotal;
        if (order.Subtotal <= 0) throw ApiException.Unprocessable("The order total must be greater than zero.");
        _db.ShopOrders.Add(order);
        await _db.SaveChangesAsync(ct);
        return ToDto(order);
    }

    private async Task<ShopOrder> LoadAsync(int orderId, string? ownerEmail, CancellationToken ct)
    {
        var q = _db.ShopOrders.Include(o => o.Lines).Where(o => o.Id == orderId);
        if (ownerEmail != null)
        {
            var e = PaymentMethodService.NormalizeEmail(ownerEmail);
            q = q.Where(o => o.BuyerEmail == e);
        }
        return await q.FirstOrDefaultAsync(ct) ?? throw ApiException.NotFound("Order not found.");
    }

    public async Task<OrderDto> GetAsync(string email, int orderId, CancellationToken ct = default)
    {
        var order = await LoadAsync(orderId, email, ct);
        await ReconcileAsync(order, ct);
        return ToDto(order);
    }

    public async Task<AdminOrderDto> GetForAdminAsync(int orderId, CancellationToken ct = default)
    {
        var order = await LoadAsync(orderId, null, ct);
        await ReconcileAsync(order, ct);
        return ToAdminDto(order);
    }

    // ---- checkout / pay -------------------------------------------------------
    private async Task<(string Type, decimal Fee)> QuoteAsync(ShopOrder order, JsonNode pm, CancellationToken ct)
    {
        var type = StaxClient.Str(pm, "method") ?? "card";
        if (type != "card") return (type, 0m); // the shop only passes on card processing fees
        var review = await _stax.GetAsync(
            $"/surcharge/review?payment_method_id={Uri.EscapeDataString(StaxClient.Str(pm, "id")!)}&total={Money(order.Subtotal)}", ct);
        return (type, Round(StaxClient.Dec(review, "surcharge_amount")));
    }

    private static void EnsurePayable(ShopOrder order)
    {
        if (order.PayState is PayState.Paid) throw ApiException.Conflict("This order is already paid.");
        if (order.PayState is PayState.PendingSettlement) throw ApiException.Conflict("A bank payment for this order is awaiting settlement.");
        if (order.PayState is PayState.Charging) throw ApiException.Conflict("A payment for this order is already in progress.");
        if (order.RefundStatus != RefundState.None) throw ApiException.Conflict("This order has been refunded.");
    }

    public async Task<CheckoutDto> CheckoutAsync(string email, int orderId, PaymentMethodRequest r, CancellationToken ct = default)
    {
        var order = await LoadAsync(orderId, email, ct);
        EnsurePayable(order);
        var pm = await _methods.GetOwnedAsync(email, r.PaymentMethodId ?? "", ct);
        var (type, fee) = await QuoteAsync(order, pm, ct);
        return new CheckoutDto(order.Id, StaxClient.Str(pm, "id")!, type, order.Subtotal, fee, Round(order.Subtotal + fee));
    }

    public async Task<OrderDto> PayAsync(string email, int orderId, PaymentMethodRequest r, CancellationToken ct = default)
    {
        var gate = Locks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var order = await LoadAsync(orderId, email, ct);
            EnsurePayable(order);
            var pm = await _methods.GetOwnedAsync(email, r.PaymentMethodId ?? "", ct);
            var (type, fee) = await QuoteAsync(order, pm, ct);
            var total = Round(order.Subtotal + fee);
            if (r.ExpectedTotal.HasValue && Round(r.ExpectedTotal.Value) != total)
                throw ApiException.Conflict($"The amount changed: it is now {Money(total)}. Run checkout again.");

            order.PayAttempts++;
            order.PayIdempotencyKey ??= Guid.NewGuid().ToString("N");
            order.PayState = PayState.Charging;
            order.PaymentMethodId = StaxClient.Str(pm, "id");
            order.PaymentMethodType = type;
            order.Fee = fee;
            order.Total = total;
            await _db.SaveChangesAsync(ct);

            JsonNode tx;
            try
            {
                tx = await _stax.PostAsync("/charge", new
                {
                    payment_method_id = order.PaymentMethodId,
                    total,
                    pre_auth = false,
                    idempotency_id = order.PayIdempotencyKey,
                    meta = new
                    {
                        subtotal = order.Subtotal,
                        fee,
                        lineItems = order.Lines.Select(l => new { id = l.CatalogItemId.ToString(), item = l.ProductName, quantity = l.Units, price = l.UnitPrice.ToString(CultureInfo.InvariantCulture) }),
                    },
                }, ct);
            }
            catch (StaxException ex)
            {
                order.PayState = PayState.Failed;
                if (ex.StaxStatus is >= 400 and < 500) order.PayIdempotencyKey = null; // definite answer; the next try is a new charge
                order.PaymentMessage = Trim(ex.Message);
                order.Fee = 0; order.Total = order.Subtotal;
                await _db.SaveChangesAsync(CancellationToken.None);
                throw;
            }

            order.StaxTransactionId = StaxClient.Str(tx, "id");
            order.PayIdempotencyKey = null;
            ApplyTransaction(order, tx);
            await _db.SaveChangesAsync(ct);
            if (order.PayState == PayState.Failed)
            {
                var msg = order.PaymentMessage ?? "The payment was declined.";
                order.Fee = 0; order.Total = order.Subtotal;
                await _db.SaveChangesAsync(ct);
                throw new ApiException(402, "Payment failed: " + msg);
            }
            return ToDto(order);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Derives the payment state of an order from a Stax transaction.</summary>
    private static void ApplyTransaction(ShopOrder order, JsonNode tx)
    {
        var status = (StaxClient.Str(tx, "status") ?? "").ToUpperInvariant();
        var failedStatus = status.Contains("FAIL") || status.Contains("DECLIN") || status.Contains("RETURN") || status.Contains("REJECT") || status.Contains("VOID");
        var message = StaxClient.Str(tx, "message") ?? StaxClient.Str(tx["response"], "message");
        order.LastCheckedAtUtc = DateTime.UtcNow;

        if (!StaxClient.Bool(tx, "success") || failedStatus || StaxClient.Bool(tx, "is_voided"))
        {
            order.PayState = PayState.Failed;
            order.PaymentMessage = Trim(message ?? (failedStatus ? "Stax reports status " + status : "The payment was declined."));
            return;
        }

        var method = StaxClient.Str(tx, "method") ?? order.PaymentMethodType;
        var settled = !string.IsNullOrWhiteSpace(StaxClient.Str(tx, "settled_at"));
        if (method == "bank" && !settled)
        {
            order.PayState = PayState.PendingSettlement;
            order.PaymentMessage = "Bank payment submitted; awaiting settlement.";
            return;
        }
        order.PayState = PayState.Paid;
        order.PaidAtUtc ??= DateTime.UtcNow;
        order.PaymentMessage = null;
    }

    // ---- background reconciliation ---------------------------------------------
    /// <summary>Brings an order up to date with Stax: bank settlement and pending refunds.</summary>
    public async Task ReconcileAsync(ShopOrder order, CancellationToken ct = default)
    {
        var gate = Locks.GetOrAdd(order.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            await _db.Entry(order).ReloadAsync(ct);
            if (order.PayState == PayState.PendingSettlement && order.StaxTransactionId != null)
            {
                try
                {
                    var tx = await _stax.GetAsync("/transaction/" + order.StaxTransactionId, ct);
                    ApplyTransaction(order, tx);
                    await _db.SaveChangesAsync(ct);
                }
                catch (StaxException) { /* try again on the next pass */ }
            }
            if (order.RefundStatus == RefundState.Pending)
            {
                await AttemptRefundAsync(order, ct);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ReconcileAllAsync(CancellationToken ct)
    {
        var ids = await _db.ShopOrders
            .Where(o => o.PayState == PayState.PendingSettlement || o.RefundStatus == RefundState.Pending)
            .Select(o => o.Id).ToListAsync(ct);
        foreach (var id in ids)
        {
            var order = await _db.ShopOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id, ct);
            if (order != null) await ReconcileAsync(order, ct);
        }
    }

    // ---- refunds -----------------------------------------------------------------
    public async Task<AdminOrderDto> RefundAsync(int orderId, CancellationToken ct = default)
    {
        var gate = Locks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var order = await LoadAsync(orderId, null, ct);
            if (order.RefundStatus == RefundState.Refunded) throw ApiException.Conflict("This order has already been refunded.");
            if (order.RefundStatus == RefundState.Pending) throw ApiException.Conflict("A refund for this order is already in progress.");
            if (order.PayState is not (PayState.Paid or PayState.PendingSettlement) || order.StaxTransactionId == null)
                throw ApiException.Conflict("This order has no completed payment to refund.");

            order.RefundStatus = RefundState.Pending;
            order.RefundRequestedAtUtc = DateTime.UtcNow;
            order.RefundNote = "Refund requested.";
            await _db.SaveChangesAsync(ct);
            await AttemptRefundAsync(order, ct);
            return ToAdminDto(order);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Tries to return the full amount paid. Bank payments cannot be refunded until they have settled, so this is retried until it works.</summary>
    private async Task AttemptRefundAsync(ShopOrder order, CancellationToken ct)
    {
        try
        {
            var tx = await _stax.GetAsync("/transaction/" + order.StaxTransactionId, ct);
            var already = StaxClient.Dec(tx, "total_refunded");
            if (already < order.Total)
            {
                tx = await _stax.PostAsync($"/transaction/{order.StaxTransactionId}/refund", new { total = Round(order.Total - already) }, ct);
                already = StaxClient.Dec(tx, "total_refunded");
            }
            if (already >= order.Total)
            {
                order.RefundStatus = RefundState.Refunded;
                order.RefundedAmount = already;
                order.RefundCompletedAtUtc = DateTime.UtcNow;
                order.RefundTransactionId = (tx["child_transactions"] as JsonArray)?
                    .FirstOrDefault(c => StaxClient.Str(c, "type") == "refund") is { } child ? StaxClient.Str(child, "id") : null;
                order.RefundNote = "Refunded in full.";
            }
            else
            {
                order.RefundedAmount = already;
                order.RefundNote = "Refund in progress.";
            }
        }
        catch (StaxException ex)
        {
            if (ex.Message.Contains("already been fully refunded", StringComparison.OrdinalIgnoreCase))
            {
                order.RefundStatus = RefundState.Refunded;
                order.RefundedAmount = order.Total;
                order.RefundCompletedAtUtc = DateTime.UtcNow;
                order.RefundNote = "Refunded in full.";
            }
            else if (DateTime.UtcNow - (order.RefundRequestedAtUtc ?? DateTime.UtcNow) > RefundGiveUp)
            {
                order.RefundStatus = RefundState.Failed;
                order.RefundNote = Trim("Giving up: " + ex.Message);
            }
            else
            {
                order.RefundNote = Trim("Waiting to retry: " + ex.Message);
            }
        }
        await _db.SaveChangesAsync(ct);
    }

    // ---- account closure -----------------------------------------------------------
    public async Task<bool> HasPendingMoneyAsync(string email, CancellationToken ct = default)
    {
        var e = PaymentMethodService.NormalizeEmail(email);
        return await _db.ShopOrders.AnyAsync(o => o.BuyerEmail == e &&
            (o.PayState == PayState.PendingSettlement || o.PayState == PayState.Charging || o.RefundStatus == RefundState.Pending), ct);
    }

    public async Task AnonymizeAsync(string email, CancellationToken ct = default)
    {
        var e = PaymentMethodService.NormalizeEmail(email);
        foreach (var o in await _db.ShopOrders.Where(o => o.BuyerEmail == e).ToListAsync(ct))
        {
            o.BuyerEmail = $"closed-{Guid.NewGuid():N}@example.invalid";
        }
        await _db.SaveChangesAsync(ct);
    }

    // ---- mapping -------------------------------------------------------------------
    private static string Trim(string s) => s.Length > 480 ? s[..480] : s;

    private static List<OrderLineDto> Lines(ShopOrder o) =>
        o.Lines.Select(l => new OrderLineDto(l.CatalogItemId, l.ProductName, l.UnitPrice, l.Units)).ToList();

    private static string StatusOf(ShopOrder o) =>
        o.RefundStatus == RefundState.Refunded ? "refunded"
        : o.RefundStatus == RefundState.Pending ? "refund_pending"
        : o.PayState == PayState.Paid ? "paid"
        : o.PayState == PayState.PendingSettlement ? "payment_pending"
        : o.PayState == PayState.Failed ? "payment_failed"
        : "awaiting_payment";

    public static OrderDto ToDto(ShopOrder o) => new(
        o.Id, StatusOf(o), o.PayState == PayState.Paid, o.PayState, Lines(o), o.Subtotal, o.Fee, o.Total,
        o.PaymentMethodId, o.PaymentMethodType, o.RefundStatus, o.CreatedAtUtc, o.PaidAtUtc);

    public static AdminOrderDto ToAdminDto(ShopOrder o) => new(
        o.Id, o.BuyerEmail, StatusOf(o), o.PayState == PayState.Paid, o.PayState, Lines(o), o.Subtotal, o.Fee, o.Total,
        o.PaymentMethodId, o.PaymentMethodType, o.StaxTransactionId, o.RefundStatus, o.RefundedAmount, o.RefundNote,
        o.RefundRequestedAtUtc, o.RefundCompletedAtUtc, o.CreatedAtUtc, o.PaidAtUtc);
}
