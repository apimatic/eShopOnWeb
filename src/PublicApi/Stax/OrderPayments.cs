using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Stax;

public class CreateOrderRequest : BaseRequest
{
    public List<OrderLineRequest>? Items { get; set; }
    public ShipToAddressRequest? ShipToAddress { get; set; }
}

public class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; } = 1;
}

public class ShipToAddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class PaymentMethodSelection : BaseRequest
{
    public string? PaymentMethodId { get; set; }
    /// <summary>Optional: the total the shopper was shown at checkout. The payment is refused if it no longer matches.</summary>
    public decimal? ExpectedTotal { get; set; }
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class OrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public List<OrderLineDto> Items { get; set; } = new();
    public decimal Subtotal { get; set; }
    public decimal Fee { get; set; }
    public decimal Total { get; set; }
    public bool Paid { get; set; }
    public string PaymentStatus { get; set; } = "unpaid";
    public string? PaymentMethodId { get; set; }
    public string RefundStatus { get; set; } = OrderPayment.REFUND_NONE;
}

public class AdminOrderDto : OrderDto
{
    public string Buyer { get; set; } = string.Empty;
    public string? TransactionId { get; set; }
    public string? PaymentMethodType { get; set; }
    public string? PaymentMessage { get; set; }
    public string? RefundMessage { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
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

public class RefundDto
{
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string RefundStatus { get; set; } = OrderPayment.REFUND_NONE;
    public string? Message { get; set; }
}

public static class OrderLocks
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();
    public static SemaphoreSlim For(int orderId) => _locks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
}

/// <summary>Orders, checkout quotes, payments, settlement tracking and refunds.</summary>
public class OrderPayments
{
    private readonly CatalogContext _db;
    private readonly StaxClient _stax;
    private readonly ShopperPayments _shoppers;
    private readonly ILogger<OrderPayments> _logger;

    public OrderPayments(CatalogContext db, StaxClient stax, ShopperPayments shoppers, ILogger<OrderPayments> logger)
    {
        _db = db;
        _stax = stax;
        _shoppers = shoppers;
        _logger = logger;
    }

    // ---------- orders ----------

    public async Task<OrderDto> CreateOrderAsync(string buyer, CreateOrderRequest request, CancellationToken ct)
    {
        if (request.Items == null || request.Items.Count == 0) throw ApiException.BadRequest("An order needs at least one item.");
        var lines = request.Items
            .GroupBy(i => i.CatalogItemId)
            .Select(g => new OrderLineRequest { CatalogItemId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToList();
        if (lines.Any(l => l.Quantity < 1 || l.Quantity > 1000)) throw ApiException.BadRequest("Each item quantity must be between 1 and 1000.");

        var ids = lines.Select(l => l.CatalogItemId).ToList();
        var catalog = await _db.CatalogItems.Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var missing = ids.Where(i => !catalog.ContainsKey(i)).ToList();
        if (missing.Count > 0) throw ApiException.NotFound($"Catalog item(s) not found: {string.Join(", ", missing)}.");

        var items = lines.Select(l =>
        {
            var c = catalog[l.CatalogItemId];
            var name = c.Name.Length > 50 ? c.Name[..50] : c.Name;
            var ordered = new CatalogItemOrdered(c.Id, name, string.IsNullOrEmpty(c.PictureUri) ? "eCatalog-item-default.png" : c.PictureUri);
            return new OrderItem(ordered, c.Price, l.Quantity);
        }).ToList();

        var a = request.ShipToAddress;
        var address = new Address(a?.Street ?? string.Empty, a?.City ?? string.Empty, a?.State ?? string.Empty, a?.Country ?? string.Empty, a?.ZipCode ?? string.Empty);
        var order = new Order(buyer, address, items);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);
        return ToDto(order, null);
    }

    private async Task<Order?> LoadOrderAsync(int orderId, CancellationToken ct) =>
        await _db.Orders.Include(o => o.OrderItems).FirstOrDefaultAsync(o => o.Id == orderId, ct);

    private async Task<Order> LoadOwnedAsync(string buyer, int orderId, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct);
        if (order == null || !string.Equals(order.BuyerId, buyer, StringComparison.OrdinalIgnoreCase))
            throw ApiException.NotFound("Order not found.");
        return order;
    }

    public async Task<OrderDto> GetOrderAsync(string buyer, int orderId, CancellationToken ct)
    {
        var order = await LoadOwnedAsync(buyer, orderId, ct);
        await TryRefreshAsync(orderId, ct);
        return ToDto(order, await GetPaymentAsync(orderId, ct));
    }

    public async Task<AdminOrderDto> GetOrderForStaffAsync(int orderId, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct) ?? throw ApiException.NotFound("Order not found.");
        await TryRefreshAsync(orderId, ct);
        var payment = await GetPaymentAsync(orderId, ct);
        var dto = new AdminOrderDto { Buyer = order.BuyerId };
        Fill(dto, order, payment);
        dto.TransactionId = payment?.TransactionId;
        dto.PaymentMethodType = payment?.Method;
        dto.PaymentMessage = payment?.Message;
        dto.RefundMessage = payment?.RefundMessage;
        dto.RefundedAt = payment?.RefundedAt;
        return dto;
    }

    private Task<OrderPayment?> GetPaymentAsync(int orderId, CancellationToken ct)
    {
        _db.ChangeTracker.Clear();
        return _db.OrderPayments.AsNoTracking().FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
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
        dto.Items = order.OrderItems.Select(i => new OrderLineDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            Name = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }).ToList();
        dto.Subtotal = order.Total();
        dto.Fee = 0m;
        dto.Total = dto.Subtotal;
        if (payment != null && payment.Status != OrderPayment.FAILED)
        {
            dto.Fee = payment.Fee;
            dto.Total = payment.Total;
            dto.PaymentMethodId = payment.PaymentMethodId;
        }
        dto.Paid = payment?.Status == OrderPayment.PAID;
        dto.PaymentStatus = payment == null ? "unpaid" : payment.Status.ToLowerInvariant();
        dto.RefundStatus = payment?.RefundStatus ?? OrderPayment.REFUND_NONE;
    }

    // ---------- checkout & pay ----------

    private async Task<CheckoutDto> QuoteAsync(Order order, JsonNode pm, CancellationToken ct)
    {
        var method = (pm.Str("method") ?? "").ToLowerInvariant();
        var subtotal = order.Total();
        var fee = 0m;
        if (method == "card")
        {
            // The shop passes the card processing fee on to the shopper: ask Stax what it will charge.
            var review = await _stax.GetAsync(
                $"surcharge/review?payment_method_id={Uri.EscapeDataString(pm.Str("id")!)}&total={subtotal.ToString("0.00", CultureInfo.InvariantCulture)}", ct);
            fee = Math.Round(review.Dec("surcharge_amount"), 2, MidpointRounding.AwayFromZero);
        }
        return new CheckoutDto
        {
            OrderId = order.Id,
            PaymentMethodId = pm.Str("id")!,
            PaymentMethodType = method,
            Subtotal = subtotal,
            Fee = fee,
            Total = subtotal + fee
        };
    }

    private async Task<(Order order, JsonNode pm)> PrepareAsync(string buyer, int orderId, PaymentMethodSelection? request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.PaymentMethodId)) throw ApiException.BadRequest("paymentMethodId is required.");
        var order = await LoadOwnedAsync(buyer, orderId, ct);
        var pm = await _shoppers.GetOwnedAsync(buyer, request.PaymentMethodId, ct)
                 ?? throw ApiException.NotFound("Payment method not found.");
        if (order.Total() <= 0) throw ApiException.Unprocessable("The order has nothing to pay.");
        return (order, pm);
    }

    public async Task<CheckoutDto> CheckoutAsync(string buyer, int orderId, PaymentMethodSelection? request, CancellationToken ct)
    {
        var (order, pm) = await PrepareAsync(buyer, orderId, request, ct);
        var existing = await GetPaymentAsync(orderId, ct);
        if (existing != null && existing.Status != OrderPayment.FAILED)
            throw ApiException.Conflict("This order has already been paid or is being paid.");
        return await QuoteAsync(order, pm, ct);
    }

    public async Task<OrderDto> PayAsync(string buyer, int orderId, PaymentMethodSelection? request, CancellationToken ct)
    {
        var (order, pm) = await PrepareAsync(buyer, orderId, request, ct);
        var gate = OrderLocks.For(orderId);
        await gate.WaitAsync(ct);
        try
        {
            _db.ChangeTracker.Clear();
            var payment = await _db.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
            if (payment != null && payment.Status != OrderPayment.FAILED)
                throw ApiException.Conflict("This order has already been paid or is being paid.");

            var quote = await QuoteAsync(order, pm, ct);
            if (request!.ExpectedTotal.HasValue && Math.Abs(request.ExpectedTotal.Value - quote.Total) > 0.004m)
                throw ApiException.Conflict($"The total changed to {quote.Total.ToString("0.00", CultureInfo.InvariantCulture)}; run checkout again.");

            if (payment == null)
            {
                payment = new OrderPayment { OrderId = orderId, Attempt = 1 };
                _db.OrderPayments.Add(payment);
            }
            payment.PaymentMethodId = quote.PaymentMethodId;
            payment.Method = quote.PaymentMethodType;
            payment.Subtotal = quote.Subtotal;
            payment.Fee = quote.Fee;
            payment.Total = quote.Total;
            payment.Status = OrderPayment.ATTEMPTING;
            payment.Message = null;
            payment.TransactionId = null;
            await _db.SaveChangesAsync(ct);

            JsonNode? tx;
            try
            {
                tx = await _stax.PostAsync("charge", new JsonObject
                {
                    ["payment_method_id"] = quote.PaymentMethodId,
                    ["total"] = quote.Total,
                    ["pre_auth"] = false,
                    ["idempotency_id"] = $"eshop-order-{orderId}-{payment.Attempt}-{quote.PaymentMethodId[..Math.Min(8, quote.PaymentMethodId.Length)]}",
                    ["meta"] = new JsonObject
                    {
                        ["transaction_initiation_type"] = "CIT",
                        ["transaction_schedule_type"] = "unscheduled",
                        ["payment_note"] = $"eShopOnWeb order {orderId}"
                    }
                }, ct);
            }
            catch (ApiException ex)
            {
                payment.Status = OrderPayment.FAILED;
                payment.Message = ex.Message;
                // A definitive rejection by Stax starts a fresh attempt next time; a transport failure keeps the idempotency key.
                if (ex is StaxApiException) payment.Attempt++;
                await _db.SaveChangesAsync(CancellationToken.None);
                throw new ApiException(ex is StaxApiException ? 402 : ex.StatusCode, ex.Message, ex);
            }

            if (!tx.Bool("success"))
            {
                payment.Status = OrderPayment.FAILED;
                payment.Message = tx.Str("message") ?? "The payment was declined.";
                payment.Attempt++;
                await _db.SaveChangesAsync(CancellationToken.None);
                throw new ApiException(402, $"Payment declined: {payment.Message}");
            }

            payment.TransactionId = tx.Str("id");
            var charged = tx.Dec("total");
            if (charged > 0 && charged != payment.Total)
            {
                payment.Total = charged;
                payment.Fee = charged - payment.Subtotal;
            }
            if (payment.Method == "bank" && string.IsNullOrEmpty(tx.Str("settled_at")))
            {
                // A bank payment goes through when the bank settles it; the reconciler picks that up.
                payment.Status = OrderPayment.PROCESSING;
            }
            else
            {
                payment.Status = OrderPayment.PAID;
                payment.PaidAt = DateTimeOffset.UtcNow;
            }
            await _db.SaveChangesAsync(CancellationToken.None);
            return ToDto(order, payment);
        }
        finally
        {
            gate.Release();
        }
    }

    // ---------- settlement ----------

    private async Task TryRefreshAsync(int orderId, CancellationToken ct)
    {
        try
        {
            await RefreshAsync(orderId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not refresh payment of order {OrderId}", orderId);
        }
    }

    /// <summary>Checks Stax for the outcome of a bank payment that is still in flight.</summary>
    public async Task RefreshAsync(int orderId, CancellationToken ct)
    {
        var gate = OrderLocks.For(orderId);
        await gate.WaitAsync(ct);
        try
        {
            _db.ChangeTracker.Clear();
            var payment = await _db.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
            if (payment == null || payment.Status != OrderPayment.PROCESSING || payment.TransactionId == null
                || payment.RefundStatus == OrderPayment.REFUND_REFUNDED)
                return;

            var tx = await _stax.GetAsync($"transaction/{Uri.EscapeDataString(payment.TransactionId)}", ct);
            var status = (tx.Str("status") ?? "").ToUpperInvariant();
            var failed = !tx.Bool("success") || tx.Bool("is_voided") || status is "FAILED" or "RETURNED" or "REJECTED" or "DECLINED" or "VOIDED";
            if (failed)
            {
                payment.Status = OrderPayment.FAILED;
                payment.Attempt++;
                payment.Message = tx.Str("message") ?? "The bank payment did not go through.";
            }
            else if (!string.IsNullOrEmpty(tx.Str("settled_at")) || status == "SETTLED")
            {
                payment.Status = OrderPayment.PAID;
                payment.PaidAt = tx.Date("settled_at") ?? DateTimeOffset.UtcNow;
            }
            else
            {
                return;
            }
            await _db.SaveChangesAsync(ct);
        }
        finally
        {
            gate.Release();
        }
    }

    // ---------- refunds ----------

    public async Task<RefundDto> RefundAsync(int orderId, CancellationToken ct)
    {
        _ = await LoadOrderAsync(orderId, ct) ?? throw ApiException.NotFound("Order not found.");
        var gate = OrderLocks.For(orderId);
        await gate.WaitAsync(ct);
        try
        {
            _db.ChangeTracker.Clear();
            var payment = await _db.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
            if (payment == null || payment.TransactionId == null
                || (payment.Status != OrderPayment.PAID && payment.Status != OrderPayment.PROCESSING))
                throw ApiException.Conflict("The order has no payment to refund.");
            if (payment.RefundStatus is OrderPayment.REFUND_PENDING or OrderPayment.REFUND_REFUNDED)
                throw ApiException.Conflict($"The order's refund is already {payment.RefundStatus}.");

            payment.RefundStatus = OrderPayment.REFUND_PENDING;
            payment.RefundRequestedAt = DateTimeOffset.UtcNow;
            payment.RefundAttempts = 0;
            payment.RefundMessage = null;
            await _db.SaveChangesAsync(ct);
            await AttemptRefundAsync(payment, ct);
            return new RefundDto { OrderId = orderId, Amount = payment.Total, RefundStatus = payment.RefundStatus, Message = payment.RefundMessage };
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Retries a pending refund; called by the reconciler until Stax lets it through.</summary>
    public async Task RetryRefundAsync(int orderId, CancellationToken ct)
    {
        var gate = OrderLocks.For(orderId);
        await gate.WaitAsync(ct);
        try
        {
            _db.ChangeTracker.Clear();
            var payment = await _db.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
            if (payment == null || payment.RefundStatus != OrderPayment.REFUND_PENDING) return;
            await AttemptRefundAsync(payment, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task AttemptRefundAsync(OrderPayment payment, CancellationToken ct)
    {
        var path = $"transaction/{Uri.EscapeDataString(payment.TransactionId!)}";
        payment.RefundAttempts++;
        try
        {
            var tx = await _stax.GetAsync(path, ct);
            if (!IsFullyReturned(tx, payment.Total))
            {
                // Voids the payment if it has not settled yet, refunds it otherwise.
                await _stax.PostAsync($"{path}/void-or-refund", new JsonObject { ["total"] = payment.Total }, ct);
                tx = await _stax.GetAsync(path, ct);
            }
            if (IsFullyReturned(tx, payment.Total))
            {
                payment.RefundStatus = OrderPayment.REFUND_REFUNDED;
                payment.RefundedAt = DateTimeOffset.UtcNow;
                payment.RefundMessage = null;
            }
            else
            {
                payment.RefundMessage = "Waiting for Stax to complete the refund.";
            }
        }
        catch (StaxApiException ex) when (ex.StaxStatus == 404 || (ex.StaxStatus == 400 && !IsNotYetRefundable(ex.Message)))
        {
            payment.RefundStatus = OrderPayment.REFUND_FAILED;
            payment.RefundMessage = ex.Message;
        }
        catch (ApiException ex)
        {
            // e.g. a bank payment cannot be refunded until it has settled: keep trying, nobody has to do anything.
            payment.RefundMessage = ex.Message;
            if (payment.RefundRequestedAt < DateTimeOffset.UtcNow.AddDays(-30))
                payment.RefundStatus = OrderPayment.REFUND_FAILED;
        }
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>Stax answers 400 while a payment cannot be refunded yet, e.g. "ACH voids and refunds are unavailable until 5 business day(s) after transaction".</summary>
    private static bool IsNotYetRefundable(string message) =>
        message.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
        || message.Contains("until", StringComparison.OrdinalIgnoreCase)
        || message.Contains("not yet", StringComparison.OrdinalIgnoreCase)
        || message.Contains("settle", StringComparison.OrdinalIgnoreCase)
        || message.Contains("batch", StringComparison.OrdinalIgnoreCase);

    private static bool IsFullyReturned(JsonNode? tx, decimal total) =>
        tx.Bool("is_voided") || tx.Dec("total_refunded") >= total - 0.004m;
}
