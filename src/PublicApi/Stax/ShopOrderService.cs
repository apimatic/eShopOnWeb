using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Stax;

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; } = 1;
}

public class ShipToRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class PlaceOrderRequest
{
    public List<OrderItemRequest>? Items { get; set; }
    public ShipToRequest? ShipTo { get; set; }
}

public class PaymentMethodRequest
{
    public string? PaymentMethodId { get; set; }

    /// <summary>Optional: the total the shopper was shown at checkout. The payment is refused if it no longer matches.</summary>
    public decimal? ExpectedTotal { get; set; }

    public static PaymentMethodRequest From(JsonObject body)
    {
        var fields = new LenientBody(body);
        var expected = fields.Get("expectedTotal", "total");
        return new PaymentMethodRequest
        {
            PaymentMethodId = fields.Get("paymentMethodId", "paymentMethod", "paymentMethodID", "id"),
            ExpectedTotal = decimal.TryParse(expected, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : null
        };
    }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public class OrderDto
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public decimal Subtotal { get; set; }
    public decimal Fee { get; set; }
    public decimal Total { get; set; }
    public bool Paid { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? PaymentMethodId { get; set; }
    public string? PaymentMethodType { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public string? PaymentNote { get; set; }
    public string RefundStatus { get; set; } = string.Empty;
}

public class AdminOrderDto : OrderDto
{
    public string Buyer { get; set; } = string.Empty;
    public string? StaxTransactionId { get; set; }
    public DateTimeOffset? RefundRequestedAt { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
    public string? RefundNote { get; set; }
}

public class CheckoutDto
{
    public int OrderId { get; set; }
    public string PaymentMethodId { get; set; } = string.Empty;
    public string PaymentMethodType { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal Fee { get; set; }
    public decimal Total { get; set; }
}

public class ShopOrderService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();

    private readonly CatalogContext _db;
    private readonly StaxClient _stax;
    private readonly PaymentMethodService _paymentMethods;

    public ShopOrderService(CatalogContext db, StaxClient stax, PaymentMethodService paymentMethods)
    {
        _db = db;
        _stax = stax;
        _paymentMethods = paymentMethods;
    }

    // ---------- placing and reading ----------

    public async Task<OrderDto> PlaceAsync(string buyer, PlaceOrderRequest request, CancellationToken ct)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            throw ApiException.BadRequest("An order needs at least one item.");
        }
        if (request.Items.Any(i => i.Quantity < 1 || i.Quantity > 1000))
        {
            throw ApiException.BadRequest("Each quantity must be between 1 and 1000.");
        }

        var wanted = request.Items.GroupBy(i => i.CatalogItemId).Select(g => (Id: g.Key, Qty: g.Sum(i => i.Quantity))).ToList();
        var ids = wanted.Select(w => w.Id).ToList();
        var catalog = await _db.CatalogItems.Where(c => ids.Contains(c.Id)).ToListAsync(ct);
        var missing = ids.Where(id => catalog.All(c => c.Id != id)).ToList();
        if (missing.Count > 0)
        {
            throw ApiException.Unprocessable($"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var items = wanted.Select(w =>
        {
            var c = catalog.First(x => x.Id == w.Id);
            var snapshot = new CatalogItemOrdered(c.Id, c.Name, string.IsNullOrEmpty(c.PictureUri) ? "none" : c.PictureUri);
            return new OrderItem(snapshot, c.Price, w.Qty);
        }).ToList();

        var ship = request.ShipTo;
        var address = new Address(ship?.Street ?? "", ship?.City ?? "", ship?.State ?? "", ship?.Country ?? "", ship?.ZipCode ?? "");
        var order = new Order(buyer, address, items);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);
        return ToDto(order);
    }

    public async Task<Order> RequireOrderAsync(int orderId, string? buyer, CancellationToken ct)
    {
        var order = await _db.Orders.Include(o => o.OrderItems).FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null || (buyer is not null && !ShopperService.SameEmail(order.BuyerId, buyer)))
        {
            throw ApiException.NotFound("Order not found.");
        }
        return order;
    }

    public async Task<OrderDto> GetAsync(string buyer, int orderId, CancellationToken ct)
    {
        var order = await RequireOrderAsync(orderId, buyer, ct);
        await SyncAsync(order, ct);
        return ToDto(order);
    }

    public async Task<AdminOrderDto> GetForStaffAsync(int orderId, CancellationToken ct)
    {
        var order = await RequireOrderAsync(orderId, null, ct);
        await SyncAsync(order, ct);
        var dto = new AdminOrderDto
        {
            Buyer = order.BuyerId,
            StaxTransactionId = order.StaxTransactionId,
            RefundRequestedAt = order.RefundRequestedAt,
            RefundedAt = order.RefundedAt,
            RefundNote = order.RefundNote
        };
        Fill(dto, order);
        return dto;
    }

    public static OrderDto ToDto(Order order)
    {
        var dto = new OrderDto();
        Fill(dto, order);
        return dto;
    }

    private static void Fill(OrderDto dto, Order order)
    {
        var subtotal = order.Total();
        dto.Id = order.Id;
        dto.OrderId = order.Id;
        dto.OrderDate = order.OrderDate;
        dto.Items = order.OrderItems.Select(i => new OrderItemDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Quantity = i.Units
        }).ToList();
        dto.Subtotal = subtotal;
        dto.Fee = order.Fee;
        dto.Total = order.PaymentState is PaymentState.Pending or PaymentState.Paid ? order.AmountCharged : subtotal;
        dto.Paid = order.IsPaid;
        dto.PaymentStatus = order.RefundStatus == RefundStatus.Refunded ? "refunded" : order.PaymentState.ToString().ToLowerInvariant();
        dto.PaymentMethodId = order.PaymentMethodId;
        dto.PaymentMethodType = order.PaymentMethodKind;
        dto.PaidAt = order.PaidAt;
        dto.PaymentNote = order.PaymentNote;
        dto.RefundStatus = order.RefundStatus.ToString().ToLowerInvariant();
    }

    // ---------- checkout and pay ----------

    /// <summary>The exact amount a shopper pays with the chosen method: the order subtotal plus the card fee Stax quotes.</summary>
    public async Task<CheckoutDto> QuoteAsync(string buyer, Order order, string paymentMethodId, CancellationToken ct)
    {
        var pm = await _paymentMethods.RequireUsableAsync(buyer, paymentMethodId, ct);
        var subtotal = order.Total();
        if (subtotal <= 0m)
        {
            throw ApiException.Unprocessable("This order has nothing to pay.");
        }

        var kind = pm.Str("method") ?? "card";
        var fee = 0m;
        if (kind == "card")
        {
            // The shop passes card processing fees on to the shopper; Stax is the authority on the amount.
            var review = await _stax.GetAsync(
                $"surcharge/review?payment_method_id={Uri.EscapeDataString(paymentMethodId)}&total={subtotal.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}", ct);
            fee = Math.Round(review.Dec("surcharge_amount"), 2, MidpointRounding.AwayFromZero);
        }

        return new CheckoutDto
        {
            OrderId = order.Id,
            PaymentMethodId = paymentMethodId,
            PaymentMethodType = kind,
            Subtotal = subtotal,
            Fee = fee,
            Total = subtotal + fee
        };
    }

    public async Task<CheckoutDto> CheckoutAsync(string buyer, int orderId, PaymentMethodRequest request, CancellationToken ct)
    {
        var order = await RequireOrderAsync(orderId, buyer, ct);
        await SyncAsync(order, ct);
        EnsurePayable(order);
        return await QuoteAsync(buyer, order, request.PaymentMethodId ?? "", ct);
    }

    private static void EnsurePayable(Order order)
    {
        if (order.RefundStatus != RefundStatus.None)
        {
            throw ApiException.Conflict(order.RefundStatus == RefundStatus.Pending ? "A refund has been requested for this order." : "This order has been refunded.");
        }
        if (order.PaymentState == PaymentState.Paid)
        {
            throw ApiException.Conflict("This order is already paid.");
        }
        if (order.PaymentState == PaymentState.Pending)
        {
            throw ApiException.Conflict("A payment for this order is already being processed by the bank.");
        }
        if (order.PaymentState == PaymentState.Failed)
        {
            throw ApiException.Conflict("The payment for this order failed and cannot be retried.");
        }
    }

    public async Task<OrderDto> PayAsync(string buyer, int orderId, PaymentMethodRequest request, CancellationToken ct)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var order = await RequireOrderAsync(orderId, buyer, ct);
            await SyncAsync(order, ct);
            EnsurePayable(order);

            var quote = await QuoteAsync(buyer, order, request.PaymentMethodId ?? "", ct);
            if (request.ExpectedTotal is { } expected && expected != quote.Total)
            {
                throw ApiException.Conflict($"The total is now {quote.Total:0.00}, not {expected:0.00}. Run checkout again.");
            }

            var body = new JsonObject
            {
                ["payment_method_id"] = quote.PaymentMethodId,
                ["total"] = quote.Total,
                ["pre_auth"] = false,
                ["idempotency_id"] = IdempotencyKey(order, quote.PaymentMethodId),
                ["meta"] = new JsonObject
                {
                    ["subtotal"] = quote.Subtotal,
                    ["payment_note"] = $"eShopOnWeb order {order.Id}",
                    ["transaction_initiation_type"] = "CIT",
                    ["transaction_schedule_type"] = "unscheduled"
                }
            };
            if (quote.Fee > 0m)
            {
                ((JsonObject)body["meta"]!)["fee"] = quote.Fee;
            }

            JsonNode txn;
            try
            {
                txn = await _stax.PostAsync("charge", body, ct);
            }
            catch (StaxException ex) when (ex.StaxStatusCode is >= 400 and < 500)
            {
                order.RecordPaymentAttemptFailed(Truncate(ex.Message));
                await _db.SaveChangesAsync(ct);
                throw new ApiException(402, $"The payment was not accepted: {ex.Message}");
            }

            if (!txn.Bool("success"))
            {
                var reason = txn.Str("message") ?? txn.Str("error_description") ?? "declined";
                order.RecordPaymentAttemptFailed(Truncate(reason));
                await _db.SaveChangesAsync(ct);
                throw new ApiException(402, $"The payment was not accepted: {reason}");
            }

            // Cards are final immediately; a bank payment only goes through once the bank settles it.
            var settled = quote.PaymentMethodType != "bank" || txn.Str("settled_at") is not null;
            order.RecordPayment(quote.PaymentMethodId, quote.PaymentMethodType, txn.Str("id")!, quote.Fee, quote.Total, settled);
            await _db.SaveChangesAsync(ct);
            return ToDto(order);
        }
        finally
        {
            gate.Release();
        }
    }

    // ---------- keeping our records in step with Stax ----------

    /// <summary>Brings a pending bank payment and a pending refund up to date. Safe to call at any time.</summary>
    public async Task SyncAsync(Order order, CancellationToken ct)
    {
        try
        {
            if (order.PaymentState == PaymentState.Pending && order.StaxTransactionId is not null)
            {
                var txn = await _stax.GetAsync($"transaction/{order.StaxTransactionId}", ct);
                if (txn.Bool("is_voided") || txn.Str("status") == "FAILED" || (txn["success"] is not null && !txn.Bool("success")))
                {
                    order.MarkPaymentFailed("The bank payment did not go through.");
                }
                else if (txn.Str("settled_at") is not null)
                {
                    order.MarkPaid();
                }
            }
            if (order.RefundStatus == RefundStatus.Pending)
            {
                await TryRefundAsync(order, ct);
            }
            await _db.SaveChangesAsync(ct);
        }
        catch (StaxException)
        {
            // Stax is unreachable or refused for now; the order keeps its current state and is retried later.
        }
    }

    // ---------- refunds ----------

    public async Task<AdminOrderDto> RefundAsync(int orderId, CancellationToken ct)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var order = await RequireOrderAsync(orderId, null, ct);
            await SyncAsync(order, ct);
            if (order.StaxTransactionId is null || order.PaymentState is PaymentState.Unpaid or PaymentState.Failed)
            {
                throw ApiException.Conflict("This order has no payment to refund.");
            }
            if (order.RefundStatus == RefundStatus.Refunded)
            {
                throw ApiException.Conflict("This order has already been refunded.");
            }

            order.RequestRefund();
            try
            {
                await TryRefundAsync(order, ct, surfaceErrors: true);
            }
            catch (StaxException ex)
            {
                // Nothing was refunded; leave the request as it was before this call.
                _db.ChangeTracker.Clear();
                throw ex.StaxStatusCode == 0 || ex.StaxStatusCode >= 500
                    ? ex
                    : new ApiException(422, $"Stax cannot refund this order: {ex.Message}");
            }
            await _db.SaveChangesAsync(ct);
            return await GetForStaffAsync(orderId, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Returns the whole amount. Stax voids a payment that has not been batched yet and refunds one that has;
    /// when it is neither yet (e.g. a bank payment in flight) the refund stays pending and is retried.
    /// </summary>
    private async Task TryRefundAsync(Order order, CancellationToken ct, bool surfaceErrors = false)
    {
        var txnId = order.StaxTransactionId!;
        var txn = await _stax.GetAsync($"transaction/{txnId}", ct);

        if (!IsReturned(txn) && (txn.Bool("is_voidable") || txn.Bool("is_refundable")))
        {
            try
            {
                txn = await _stax.PostAsync($"transaction/{txnId}/void-or-refund", null, ct);
                if (!IsReturned(txn))
                {
                    txn = await _stax.GetAsync($"transaction/{txnId}", ct);
                }
            }
            catch (StaxException ex) when (!surfaceErrors || IsTimingRefusal(order, ex))
            {
                // e.g. ACH refunds are refused for several business days; keep the refund pending and retry.
                order.NoteRefund(Truncate($"Waiting for Stax to accept the refund: {ex.Message}"));
                return;
            }
        }

        if (IsReturned(txn))
        {
            order.MarkRefunded();
        }
        else
        {
            order.NoteRefund("Waiting for the payment to become refundable at Stax; it will be refunded automatically.");
        }
    }

    /// <summary>A refusal that is about timing (bank payments cannot be returned for a few business days), not about the request.</summary>
    private static bool IsTimingRefusal(Order order, StaxException ex) =>
        ex.StaxStatusCode is 400 or 422
        && (order.PaymentMethodKind == "bank"
            || ex.Message.Contains("until", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("not yet", StringComparison.OrdinalIgnoreCase));

    private static bool IsReturned(JsonNode txn)
    {
        var total = txn.Dec("total");
        if (txn.Bool("is_voided")) return true;
        if (total > 0m && txn.Dec("total_refunded") >= total) return true;
        return txn["child_transactions"] is JsonArray children
            && children.Any(c => c.Bool("success") && c.Str("type") is "void" or "refund" && c.Dec("total") >= total);
    }

    /// <summary>Stable for one payment attempt (so a double submit is not charged twice), different for every attempt.</summary>
    private static string IdempotencyKey(Order order, string paymentMethodId)
    {
        var raw = $"{order.Id}|{order.OrderDate.UtcTicks}|{order.PaymentAttempts + 1}|{paymentMethodId}";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw));
        return "eshop-" + Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    private static string Truncate(string s) => s.Length <= 480 ? s : s[..480];
}
