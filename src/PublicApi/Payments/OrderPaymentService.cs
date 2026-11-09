using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Stax;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderRequest : BaseRequest
{
    public List<OrderLineRequest>? Items { get; set; }
}

public class PaymentMethodChoiceRequest : BaseRequest
{
    public string? PaymentMethodId { get; set; }
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public class OrderDto
{
    public int OrderId { get; set; }
    public string? Buyer { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<OrderLineDto> Items { get; set; } = new();
    public decimal Subtotal { get; set; }
    public decimal Fee { get; set; }
    public decimal Total { get; set; }
    /// <summary>Whether the order's payment has gone through (a bank payment only once the bank has settled it).</summary>
    public bool Paid { get; set; }
    /// <summary>unpaid, processing, pending (bank payment awaiting settlement), paid or failed.</summary>
    public string PaymentStatus { get; set; } = "unpaid";
    public string? PaymentMethodType { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    /// <summary>none, pending (requested, waiting on the bank), refunded or not_required.</summary>
    public string RefundStatus { get; set; } = "none";
    public DateTimeOffset? RefundRequestedAt { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
    public string? RefundError { get; set; }
    public string? StaxTransactionId { get; set; }
    public string? RefundTransactionId { get; set; }
}

public class CheckoutDto
{
    public int OrderId { get; set; }
    public string PaymentMethodId { get; set; } = "";
    public string PaymentMethod { get; set; } = "";
    public decimal Subtotal { get; set; }
    public decimal Fee { get; set; }
    public decimal Total { get; set; }
}

public class OrderPaymentService
{
    private readonly CatalogContext _db;
    private readonly StaxClient _stax;
    private readonly PaymentMethodService _methods;

    public OrderPaymentService(CatalogContext db, StaxClient stax, PaymentMethodService methods)
    {
        _db = db;
        _stax = stax;
        _methods = methods;
    }

    // ---- mapping

    public static OrderDto ToDto(ShopperOrder o, bool admin)
    {
        return new OrderDto
        {
            OrderId = o.Id,
            Buyer = admin ? o.BuyerEmail : null,
            CreatedAt = o.CreatedAt,
            Items = o.Lines.Select(l => new OrderLineDto { CatalogItemId = l.CatalogItemId, ProductName = l.ProductName, UnitPrice = l.UnitPrice, Quantity = l.Units }).ToList(),
            Subtotal = o.Subtotal,
            Fee = o.Fee,
            Total = o.PaymentState is PaymentState.None ? o.Subtotal : o.Total,
            Paid = o.PaymentState == PaymentState.Paid,
            PaymentStatus = o.PaymentState switch
            {
                PaymentState.Processing => "processing",
                PaymentState.Pending => "pending",
                PaymentState.Paid => "paid",
                PaymentState.Failed => "failed",
                _ => "unpaid"
            },
            PaymentMethodType = o.PaymentMethodType,
            PaidAt = o.PaidAt,
            RefundStatus = o.RefundState switch
            {
                RefundState.Pending => "pending",
                RefundState.Refunded => "refunded",
                RefundState.NotRequired => "not_required",
                _ => "none"
            },
            RefundRequestedAt = o.RefundRequestedAt,
            RefundedAt = o.RefundedAt,
            RefundError = admin ? o.RefundError : null,
            StaxTransactionId = admin ? o.StaxTransactionId : null,
            RefundTransactionId = admin ? o.RefundTransactionId : null
        };
    }

    // ---- orders

    public async Task<ShopperOrder> CreateOrderAsync(string email, CreateOrderRequest request, CancellationToken ct)
    {
        if (request.Items is null || request.Items.Count == 0)
            throw new ApiRequestException(400, "An order needs at least one item.");
        if (request.Items.Any(i => i.Quantity < 1 || i.Quantity > 1000))
            throw new ApiRequestException(400, "Each quantity must be between 1 and 1000.");

        var wanted = request.Items.GroupBy(i => i.CatalogItemId).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
        if (wanted.Values.Any(q => q > 1000))
            throw new ApiRequestException(400, "Each quantity must be between 1 and 1000.");
        var ids = wanted.Keys.ToList();
        var items = await _db.CatalogItems.Where(c => ids.Contains(c.Id)).ToListAsync(ct);
        var missing = ids.Except(items.Select(i => i.Id)).ToList();
        if (missing.Count > 0)
            throw new ApiRequestException(422, $"Catalog item(s) not found: {string.Join(", ", missing)}.");

        var order = new ShopperOrder { BuyerEmail = email, PaymentAttempts = 1 };
        foreach (var item in items.OrderBy(i => ids.IndexOf(i.Id)))
        {
            // Prices always come from the catalog, never from the caller.
            order.Lines.Add(new ShopperOrderLine { CatalogItemId = item.Id, ProductName = item.Name, UnitPrice = item.Price, Units = wanted[item.Id] });
        }
        order.Subtotal = Math.Round(order.Lines.Sum(l => l.UnitPrice * l.Units), 2);
        if (order.Subtotal <= 0)
            throw new ApiRequestException(422, "An order must have a total above zero.");
        _db.ShopperOrders.Add(order);
        await _db.SaveChangesAsync(ct);
        return order;
    }

    public async Task<ShopperOrder> GetOwnedOrderAsync(string email, int orderId, CancellationToken ct)
    {
        var order = await _db.ShopperOrders.FirstOrDefaultAsync(o => o.Id == orderId && o.BuyerEmail == email, ct);
        return order ?? throw new ApiRequestException(404, "Order not found.");
    }

    public async Task<ShopperOrder> GetOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _db.ShopperOrders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
        return order ?? throw new ApiRequestException(404, "Order not found.");
    }

    // ---- checkout / pay

    private static void EnsurePayable(ShopperOrder order)
    {
        if (order.PaymentState is PaymentState.Paid or PaymentState.Pending)
            throw new ApiRequestException(409, "This order has already been paid.");
        if (order.PaymentState == PaymentState.Processing)
            throw new ApiRequestException(409, "A payment for this order is already being processed.");
    }

    public async Task<(JsonNode Method, decimal Fee, decimal Total)> QuoteAsync(string email, ShopperOrder order, string? paymentMethodId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(paymentMethodId))
            throw new ApiRequestException(400, "paymentMethodId is required.");
        EnsurePayable(order);
        var pm = await _methods.GetOwnedAsync(email, paymentMethodId, ct);
        var (fee, total) = await _stax.ReviewSurchargeAsync(pm["id"]!.ToString(), order.Subtotal, ct);
        return (pm, fee, total);
    }

    public async Task<CheckoutDto> CheckoutAsync(string email, int orderId, string? paymentMethodId, CancellationToken ct)
    {
        var order = await GetOwnedOrderAsync(email, orderId, ct);
        var (pm, fee, total) = await QuoteAsync(email, order, paymentMethodId, ct);
        return new CheckoutDto
        {
            OrderId = order.Id,
            PaymentMethodId = pm["id"]!.ToString(),
            PaymentMethod = pm["method"]!.ToString(),
            Subtotal = order.Subtotal,
            Fee = fee,
            Total = total
        };
    }

    public async Task<ShopperOrder> PayAsync(string email, int orderId, string? paymentMethodId, CancellationToken ct)
    {
        var order = await GetOwnedOrderAsync(email, orderId, ct);
        var (pm, fee, total) = await QuoteAsync(email, order, paymentMethodId, ct);
        var methodType = pm["method"]!.ToString();

        // Claim the order so concurrent pay requests cannot both charge it.
        order.PaymentState = PaymentState.Processing;
        order.PaymentMethodId = pm["id"]!.ToString();
        order.PaymentMethodType = methodType;
        order.PaymentError = null;
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ApiRequestException(409, "A payment for this order is already being processed.");
        }

        JsonNode tx;
        try
        {
            tx = await _stax.ChargeAsync(order.PaymentMethodId, total, $"eshop-{order.PaymentKey}-{order.PaymentAttempts}",
                new { orderId = order.Id.ToString(), subtotal = order.Subtotal, fee }, ct);
        }
        catch (StaxException ex)
        {
            if (ex.IsRejection)
            {
                order.PaymentState = PaymentState.Failed;
                order.PaymentError = Truncate(ex.Message);
                order.PaymentAttempts++;
                await _db.SaveChangesAsync(CancellationToken.None);
                throw new ApiRequestException(402, $"The payment was refused: {ex.Message}");
            }
            // Outcome unknown (Stax unreachable): keep the same idempotency key for the next try.
            order.PaymentState = PaymentState.None;
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        order.StaxTransactionId = tx["id"]?.ToString();
        if (tx["success"]?.GetValue<bool>() != true)
        {
            order.PaymentState = PaymentState.Failed;
            order.PaymentError = Truncate(tx["message"]?.ToString() ?? "The payment was declined.");
            order.PaymentAttempts++;
            await _db.SaveChangesAsync(CancellationToken.None);
            throw new ApiRequestException(402, $"The payment was declined: {order.PaymentError}");
        }

        order.Fee = fee;
        order.Total = total;
        ApplyTransaction(order, tx);
        await _db.SaveChangesAsync(CancellationToken.None);
        return order;
    }

    /// <summary>
    /// A card payment has gone through once Stax accepts the charge. A bank payment is only accepted by the bank
    /// later, so it stays pending until Stax reports it settled.
    /// </summary>
    private static void ApplyTransaction(ShopperOrder order, JsonNode tx)
    {
        var success = tx["success"]?.GetValue<bool>() == true;
        var settledAt = StaxClient.ParseStaxTime(tx["settled_at"]);
        var isBank = string.Equals(order.PaymentMethodType, "bank", StringComparison.OrdinalIgnoreCase);

        if (!success)
        {
            order.PaymentState = PaymentState.Failed;
            order.PaymentError = Truncate(tx["message"]?.ToString() ?? "The payment failed.");
            if (order.RefundState == RefundState.Pending) order.RefundState = RefundState.NotRequired;
        }
        else if (!isBank)
        {
            order.PaymentState = PaymentState.Paid;
            order.PaidAt ??= StaxClient.ParseStaxTime(tx["created_at"]) ?? DateTimeOffset.UtcNow;
        }
        else if (settledAt is not null)
        {
            order.PaymentState = PaymentState.Paid;
            order.PaidAt = settledAt;
        }
        else
        {
            order.PaymentState = PaymentState.Pending;
        }
    }

    // ---- reconciliation

    /// <summary>Brings the order up to date with Stax: bank settlement and any outstanding refund.</summary>
    public async Task RefreshAsync(ShopperOrder order, CancellationToken ct)
    {
        if (order.PaymentState == PaymentState.Pending && order.StaxTransactionId is not null)
        {
            var tx = await _stax.GetTransactionAsync(order.StaxTransactionId, ct);
            ApplyTransaction(order, tx);
            await _db.SaveChangesAsync(ct);
        }
        if (order.RefundState == RefundState.Pending)
            await TryRefundAsync(order, requestedNow: false, ct);
    }

    // ---- refunds

    public async Task<ShopperOrder> RefundAsync(int orderId, CancellationToken ct)
    {
        var order = await GetOrderAsync(orderId, ct);
        if (order.PaymentState is not (PaymentState.Paid or PaymentState.Pending) || order.StaxTransactionId is null)
            throw new ApiRequestException(409, "Only an order that has been paid can be refunded.");
        if (order.RefundState == RefundState.Refunded)
            throw new ApiRequestException(409, "This order has already been refunded.");
        if (order.RefundState == RefundState.Pending)
            throw new ApiRequestException(409, "A refund for this order is already in progress.");

        order.RefundState = RefundState.Pending;
        order.RefundRequestedAt = DateTimeOffset.UtcNow;
        order.RefundError = null;
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ApiRequestException(409, "A refund for this order is already in progress.");
        }

        await TryRefundAsync(order, requestedNow: true, ct);
        return order;
    }

    private static bool IsBankWaitingPeriod(StaxException ex)
        => ex.Message.Contains("unavailable until", StringComparison.OrdinalIgnoreCase)
           || ex.Message.Contains("business day", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Refunds the whole amount charged. A bank payment cannot be refunded until Stax allows it (days after
    /// the payment), so the refund stays pending and is retried until it goes through.
    /// </summary>
    private async Task TryRefundAsync(ShopperOrder order, bool requestedNow, CancellationToken ct)
    {
        try
        {
            // Already voided / refunded at Stax (e.g. an earlier attempt whose answer was lost)?
            var current = await _stax.GetTransactionAsync(order.StaxTransactionId!, ct);
            if (current["is_voided"]?.GetValue<bool>() == true || (current["total_refunded"]?.GetValue<decimal>() ?? 0m) >= order.Total)
            {
                MarkRefunded(order, null);
                await _db.SaveChangesAsync(CancellationToken.None);
                return;
            }
            if (current["success"]?.GetValue<bool>() != true)
            {
                order.PaymentState = PaymentState.Failed;
                order.RefundState = RefundState.NotRequired;
                await _db.SaveChangesAsync(CancellationToken.None);
                return;
            }

            var result = await _stax.VoidOrRefundAsync(order.StaxTransactionId!, order.Total, ct);
            if (result["success"]?.GetValue<bool>() == false)
                throw new StaxException(422, result["message"]?.ToString() ?? "Stax did not accept the refund.");
            MarkRefunded(order, result["id"]?.ToString());
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (StaxException ex) when (IsBankWaitingPeriod(ex))
        {
            order.RefundError = Truncate(ex.Message);
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (StaxException ex)
        {
            if (requestedNow && ex.IsRejection)
            {
                // Nothing was started: let staff try again later.
                order.RefundState = RefundState.None;
                order.RefundRequestedAt = null;
                order.RefundError = null;
                await _db.SaveChangesAsync(CancellationToken.None);
                throw new ApiRequestException(422, $"The refund was refused: {ex.Message}");
            }
            // Stays pending and is retried.
            order.RefundError = Truncate(ex.Message);
            await _db.SaveChangesAsync(CancellationToken.None);
            if (requestedNow) throw;
        }
    }

    private static void MarkRefunded(ShopperOrder order, string? refundTransactionId)
    {
        order.RefundState = RefundState.Refunded;
        order.RefundedAt = DateTimeOffset.UtcNow;
        order.RefundError = null;
        if (refundTransactionId is not null) order.RefundTransactionId = refundTransactionId;
    }

    private static string Truncate(string s) => s.Length <= 900 ? s : s[..900];
}
