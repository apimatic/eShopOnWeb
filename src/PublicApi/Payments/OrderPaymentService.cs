using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Stax;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class AddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderRequest
{
    public AddressRequest? ShipToAddress { get; set; }
    public List<OrderItemRequest>? Items { get; set; }
}

public class PaymentMethodChoiceRequest
{
    public string? PaymentMethodId { get; set; }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class OrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public decimal Subtotal { get; set; }
    public decimal Fee { get; set; }
    public decimal Total { get; set; }
    /// <summary>Whether the order's payment has gone through (a bank payment only once the bank has settled it).</summary>
    public bool Paid { get; set; }
    /// <summary>none | processing | pending | paid | failed</summary>
    public string PaymentStatus { get; set; } = "none";
    public string? PaymentMethodId { get; set; }
    public string? PaymentMethod { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public string RefundStatus { get; set; } = RefundStatuses.None;
}

public class AdminOrderDto : OrderDto
{
    public string BuyerEmail { get; set; } = string.Empty;
    public string? StaxTransactionId { get; set; }
    public decimal RefundedAmount { get; set; }
    public string? RefundMessage { get; set; }
}

public class CheckoutDto
{
    public int OrderId { get; set; }
    public string PaymentMethodId { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal Fee { get; set; }
    public decimal Total { get; set; }
}

public class RefundDto
{
    public int OrderId { get; set; }
    /// <summary>none | pending | refunded | failed</summary>
    public string RefundStatus { get; set; } = RefundStatuses.None;
    public decimal Amount { get; set; }
    public string? Message { get; set; }
}

public class OrderPaymentService
{
    private readonly StaxClient _stax;
    private readonly CatalogContext _db;
    private readonly PaymentMethodService _methods;

    public OrderPaymentService(StaxClient stax, CatalogContext db, PaymentMethodService methods)
    {
        _stax = stax;
        _db = db;
        _methods = methods;
    }

    // ---------------------------------------------------------------- orders

    public async Task<OrderDto> CreateOrderAsync(string email, CreateOrderRequest request, CancellationToken ct)
    {
        var a = request.ShipToAddress;
        if (a == null || string.IsNullOrWhiteSpace(a.Street) || string.IsNullOrWhiteSpace(a.City)
            || string.IsNullOrWhiteSpace(a.Country) || string.IsNullOrWhiteSpace(a.ZipCode))
        {
            throw ApiException.BadRequest("shipToAddress needs street, city, country and zipCode.");
        }
        if (request.Items == null || request.Items.Count == 0) throw ApiException.BadRequest("An order needs at least one item.");
        if (request.Items.Any(i => i.Quantity < 1 || i.Quantity > 1000)) throw ApiException.BadRequest("Each quantity must be between 1 and 1000.");

        var wanted = request.Items.GroupBy(i => i.CatalogItemId).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
        var catalog = await _db.CatalogItems.Where(c => wanted.Keys.Contains(c.Id)).ToListAsync(ct);
        var missing = wanted.Keys.Where(id => catalog.All(c => c.Id != id)).ToList();
        if (missing.Count > 0) throw ApiException.NotFound("Catalog item(s) not found: " + string.Join(", ", missing));

        var items = catalog.Select(c => new OrderItem(
            new CatalogItemOrdered(c.Id, Truncate(c.Name, 50), string.IsNullOrEmpty(c.PictureUri) ? "none" : c.PictureUri),
            c.Price, wanted[c.Id])).ToList();
        var order = new Order(PaymentMethodService.Normalize(email),
            new Address(a.Street.Trim(), a.City.Trim(), a.State?.Trim() ?? string.Empty, a.Country.Trim(), a.ZipCode.Trim()), items);
        if (order.Total() <= 0) throw ApiException.Unprocessable("The order total must be greater than zero.");

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);
        return ToDto(order, null);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken ct)
    {
        return await _db.Orders.Include(o => o.OrderItems).FirstOrDefaultAsync(o => o.Id == orderId, ct)
               ?? throw ApiException.NotFound("Order not found.");
    }

    private async Task<Order> LoadOwnOrderAsync(int orderId, string email, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct);
        if (order.BuyerId != PaymentMethodService.Normalize(email)) throw ApiException.NotFound("Order not found.");
        return order;
    }

    public async Task<OrderDto> GetOrderAsync(int orderId, string email, CancellationToken ct)
    {
        var order = await LoadOwnOrderAsync(orderId, email, ct);
        var payment = await _db.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
        if (payment != null) await ReconcileAsync(payment, ct);
        return ToDto(order, payment);
    }

    public async Task<AdminOrderDto> GetOrderForStaffAsync(int orderId, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct);
        var payment = await _db.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
        if (payment != null) await ReconcileAsync(payment, ct);
        var dto = new AdminOrderDto();
        Fill(dto, order, payment);
        dto.BuyerEmail = order.BuyerId;
        dto.StaxTransactionId = payment?.StaxTransactionId;
        dto.RefundedAmount = payment?.RefundedAmount ?? 0;
        dto.RefundMessage = payment?.RefundMessage;
        return dto;
    }

    private static OrderDto ToDto(Order order, OrderPayment? payment)
    {
        var dto = new OrderDto();
        Fill(dto, order, payment);
        return dto;
    }

    private static void Fill(OrderDto dto, Order order, OrderPayment? payment)
    {
        dto.OrderId = order.Id;
        dto.OrderDate = order.OrderDate;
        dto.Items = order.OrderItems.Select(i => new OrderItemDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }).ToList();
        dto.Subtotal = order.Total();
        dto.Fee = payment?.Fee ?? 0m;
        dto.Total = payment?.Total ?? dto.Subtotal;
        dto.PaymentStatus = payment?.Status ?? "none";
        dto.Paid = payment?.Status == PaymentStatuses.Paid;
        dto.PaymentMethodId = payment?.StaxPaymentMethodId;
        dto.PaymentMethod = payment?.Method;
        dto.PaidAt = payment?.PaidAt;
        dto.RefundStatus = payment?.RefundStatus ?? RefundStatuses.None;
    }

    // ---------------------------------------------------------------- checkout

    public async Task<CheckoutDto> CheckoutAsync(int orderId, string email, string? paymentMethodId, CancellationToken ct)
    {
        var order = await LoadOwnOrderAsync(orderId, email, ct);
        var method = await RequireMethodAsync(email, paymentMethodId, ct);
        await EnsureNotPaidAsync(orderId, ct, allowFailedRetry: true);
        var (fee, total) = await QuoteAsync(method, order.Total(), ct);
        return new CheckoutDto
        {
            OrderId = orderId,
            PaymentMethodId = method.StaxPaymentMethodId,
            PaymentMethod = method.Method,
            Subtotal = order.Total(),
            Fee = fee,
            Total = total
        };
    }

    private async Task<SavedPaymentMethod> RequireMethodAsync(string email, string? paymentMethodId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(paymentMethodId)) throw ApiException.BadRequest("paymentMethodId is required.");
        return await _methods.FindOwnedAsync(email, paymentMethodId.Trim(), ct)
               ?? throw ApiException.NotFound("No saved payment method with that id.");
    }

    private async Task EnsureNotPaidAsync(int orderId, CancellationToken ct, bool allowFailedRetry)
    {
        var existing = await _db.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
        if (existing == null) return;
        if (existing.Status == PaymentStatuses.Failed && allowFailedRetry) return;
        throw ApiException.Conflict(existing.Status switch
        {
            PaymentStatuses.Paid => "This order has already been paid.",
            PaymentStatuses.Pending => "A bank payment for this order is already awaiting settlement.",
            _ => "A payment for this order is already in progress."
        });
    }

    /// <summary>The fee for a card is whatever Stax says the surcharge is; bank payments carry no fee.</summary>
    private async Task<(decimal Fee, decimal Total)> QuoteAsync(SavedPaymentMethod method, decimal subtotal, CancellationToken ct)
    {
        if (method.Method != "card") return (0m, subtotal);
        var review = await _stax.GetAsync(
            $"surcharge/review?payment_method_id={Uri.EscapeDataString(method.StaxPaymentMethodId)}&total={subtotal.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}", ct);
        var fee = Math.Round(review.Dec("surcharge_amount"), 2);
        return (fee, subtotal + fee);
    }

    // ---------------------------------------------------------------- pay

    public async Task<OrderDto> PayAsync(int orderId, string email, string? paymentMethodId, CancellationToken ct)
    {
        var order = await LoadOwnOrderAsync(orderId, email, ct);
        var method = await RequireMethodAsync(email, paymentMethodId, ct);
        var subtotal = order.Total();

        // claim the order: the unique index on OrderId makes a second concurrent payment fail here
        var payment = await _db.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
        if (payment != null)
        {
            await EnsureNotPaidAsync(orderId, ct, allowFailedRetry: true);
            _db.OrderPayments.Remove(payment);
            await _db.SaveChangesAsync(ct);
        }
        payment = new OrderPayment
        {
            OrderId = orderId,
            BuyerEmail = order.BuyerId,
            StaxPaymentMethodId = method.StaxPaymentMethodId,
            Method = method.Method,
            Subtotal = subtotal,
            Total = subtotal
        };
        _db.OrderPayments.Add(payment);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.Entry(payment).State = EntityState.Detached;
            throw ApiException.Conflict("A payment for this order is already in progress.");
        }

        JsonNode tx;
        try
        {
            tx = await _stax.PostAsync("charge", new
            {
                payment_method_id = method.StaxPaymentMethodId,
                total = subtotal,
                pre_auth = false,
                idempotency_id = $"eshop-order-{orderId}-{Guid.NewGuid():N}",
                meta = new { memo = $"eShopOnWeb order {orderId}", orderId, subtotal }
            }, ct);
        }
        catch (StaxException ex)
        {
            await DropAsync(payment, ct);
            throw new ApiException(ex.StatusCode == 502 ? 502 : 402, "The payment was not taken: " + ex.Message);
        }
        catch (ApiException)
        {
            await DropAsync(payment, ct);
            throw;
        }

        if (!tx.Bool("success"))
        {
            await DropAsync(payment, ct);
            throw new ApiException(402, "The payment was declined: " + (tx.Str("message") ?? "no reason given") + ".");
        }

        payment.StaxTransactionId = tx.Str("id");
        var charged = tx.Dec("total");
        if (charged > 0)
        {
            payment.Total = charged;
            payment.Fee = Math.Max(0, charged - subtotal);
        }
        ApplyTransaction(payment, tx);
        await _db.SaveChangesAsync(ct);
        return ToDto(order, payment);
    }

    private async Task DropAsync(OrderPayment payment, CancellationToken ct)
    {
        _db.OrderPayments.Remove(payment);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>A card payment has gone through once Stax accepted it; a bank payment only once settled.</summary>
    private static void ApplyTransaction(OrderPayment payment, JsonNode tx)
    {
        if (payment.Status is PaymentStatuses.Paid && payment.PaidAt != null) return;
        if (!tx.Bool("success") || tx.Bool("is_voided"))
        {
            payment.Status = PaymentStatuses.Failed;
            payment.LastError = tx.Str("message");
            return;
        }
        var settled = payment.Method == "card" || !string.IsNullOrEmpty(tx.Str("settled_at"));
        if (settled)
        {
            payment.Status = PaymentStatuses.Paid;
            payment.PaidAt = tx.Time("settled_at") ?? DateTimeOffset.UtcNow;
        }
        else
        {
            payment.Status = PaymentStatuses.Pending;
        }
    }

    // ---------------------------------------------------------------- reconcile (settlement + refunds)

    /// <summary>Brings an order's payment and refund state in line with Stax.</summary>
    public async Task ReconcileAsync(OrderPayment payment, CancellationToken ct)
    {
        if (payment.StaxTransactionId == null) return;
        try
        {
            if (payment.Status == PaymentStatuses.Pending)
            {
                var tx = await _stax.GetAsync($"transaction/{payment.StaxTransactionId}", ct);
                ApplyTransaction(payment, tx);
                await _db.SaveChangesAsync(ct);
            }
            if (payment.RefundStatus == RefundStatuses.Pending)
            {
                await TryRefundAsync(payment, ct);
            }
        }
        catch (ApiException)
        {
            // Stax unavailable right now; the background reconciler tries again
        }
    }

    // ---------------------------------------------------------------- refunds

    public async Task<RefundDto> RefundAsync(int orderId, CancellationToken ct)
    {
        await LoadOrderAsync(orderId, ct);
        var payment = await _db.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
        if (payment == null || payment.StaxTransactionId == null || payment.Status is PaymentStatuses.Failed or PaymentStatuses.Processing)
            throw ApiException.Conflict("This order has no payment to refund.");
        if (payment.RefundStatus == RefundStatuses.Refunded) throw ApiException.Conflict("This order has already been refunded.");
        if (payment.RefundStatus == RefundStatuses.Pending) throw ApiException.Conflict("A refund for this order is already in progress.");

        payment.RefundStatus = RefundStatuses.Pending;
        payment.RefundRequestedAt = DateTimeOffset.UtcNow;
        payment.RefundMessage = "Refund requested.";
        await _db.SaveChangesAsync(ct);

        try
        {
            await ReconcileAsync(payment, CancellationToken.None);
        }
        catch (ApiException) { }
        return ToRefundDto(payment);
    }

    public static RefundDto ToRefundDto(OrderPayment p) => new()
    {
        OrderId = p.OrderId,
        RefundStatus = p.RefundStatus,
        Amount = p.RefundStatus == RefundStatuses.Refunded ? p.RefundedAmount : p.Total,
        Message = p.RefundMessage
    };

    /// <summary>
    /// Asks Stax to give the whole amount back. ACH payments cannot be refunded until a waiting period after the
    /// payment has passed, so the refund stays pending and is retried by the background reconciler.
    /// </summary>
    private async Task TryRefundAsync(OrderPayment payment, CancellationToken ct)
    {
        var txPath = $"transaction/{payment.StaxTransactionId}";
        var tx = await _stax.GetAsync(txPath, ct);
        var remaining = tx.Dec("total") - tx.Dec("total_refunded");

        if (remaining > 0)
        {
            try
            {
                await _stax.PostAsync(txPath + "/refund", new { total = remaining }, ct);
            }
            catch (StaxException ex) when (ex.StatusCode is 422 or 409)
            {
                var waiting = ex.Message.Contains("unavailable until", StringComparison.OrdinalIgnoreCase)
                              || ex.Message.Contains("similar request", StringComparison.OrdinalIgnoreCase)
                              || ex.Message.Contains("being processed", StringComparison.OrdinalIgnoreCase)
                              || ex.Message.Contains("not yet", StringComparison.OrdinalIgnoreCase);
                payment.RefundStatus = waiting ? RefundStatuses.Pending : RefundStatuses.Failed;
                payment.RefundMessage = ex.Message;
                await _db.SaveChangesAsync(ct);
                return;
            }
            tx = await _stax.GetAsync(txPath, ct);
        }

        if (tx.Dec("total_refunded") >= tx.Dec("total") && tx.Dec("total") > 0)
        {
            payment.RefundStatus = RefundStatuses.Refunded;
            payment.RefundedAmount = tx.Dec("total_refunded");
            payment.RefundedAt = DateTimeOffset.UtcNow;
            payment.RefundMessage = "The whole amount has been refunded.";
        }
        else
        {
            payment.RefundMessage = "Refund submitted; waiting for Stax to complete it.";
        }
        await _db.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------- account closing support

    /// <summary>Throws when the shopper still has money in flight that closing would orphan.</summary>
    public async Task EnsureNothingInFlightAsync(string email, CancellationToken ct)
    {
        var key = PaymentMethodService.Normalize(email);
        var payments = await _db.OrderPayments.Where(p => p.BuyerEmail == key).ToListAsync(ct);
        foreach (var p in payments) await ReconcileAsync(p, ct);
        if (payments.Any(p => p.Status is PaymentStatuses.Pending or PaymentStatuses.Processing || p.RefundStatus == RefundStatuses.Pending))
            throw ApiException.Conflict("The account cannot be closed while a payment or refund is still being processed.");
    }

    public async Task EraseLocalAsync(string email, CancellationToken ct)
    {
        var key = PaymentMethodService.Normalize(email);
        _db.OrderPayments.RemoveRange(_db.OrderPayments.Where(p => p.BuyerEmail == key));
        _db.Orders.RemoveRange(await _db.Orders.Include(o => o.OrderItems).Where(o => o.BuyerId == key).ToListAsync(ct));
        _db.Baskets.RemoveRange(await _db.Baskets.Include(b => b.Items).Where(b => b.BuyerId == key || b.BuyerId == email).ToListAsync(ct));
        _db.SavedPaymentMethods.RemoveRange(_db.SavedPaymentMethods.Where(p => p.BuyerEmail == key));
        _db.StaxCustomerLinks.RemoveRange(_db.StaxCustomerLinks.Where(l => l.Email == key));
        await _db.SaveChangesAsync(ct);
    }
}
