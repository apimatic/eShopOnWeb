using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using StaxFattMerchantApi.Models;
using StaxFattMerchantApi.Requests.Charge;
using StaxFattMerchantApi.Requests.Transaction;

namespace Microsoft.eShopWeb.PublicApi.Stax;

// ---- Requests ----------------------------------------------------------------------------------

public class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class CreateOrderRequest
{
    public List<OrderLineRequest>? Items { get; set; }
    public ShipToRequest? ShipTo { get; set; }
}

public class PaymentMethodChoiceRequest
{
    public string? PaymentMethodId { get; set; }
}

public class PayOrderRequest : PaymentMethodChoiceRequest
{
    /// <summary>The total the shopper saw at checkout. When given and different from today's total, nothing is charged.</summary>
    public decimal? ExpectedTotal { get; set; }
}

public class RefundOrderRequest
{
    public string? Reason { get; set; }
}

// ---- Responses ---------------------------------------------------------------------------------

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class OrderDto
{
    public int Id { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public List<OrderLineDto> Items { get; set; } = [];
    /// <summary>The goods total.</summary>
    public decimal Amount { get; set; }
    /// <summary>Card processing fee passed on to the shopper (known once the order was paid).</summary>
    public decimal Fee { get; set; }
    /// <summary>What the shopper pays (goods plus fee); the goods total until a payment was made.</summary>
    public decimal Total { get; set; }
    /// <summary>Whether the payment has gone through. A bank payment only counts once the bank settled it.</summary>
    public bool Paid { get; set; }
    /// <summary>unpaid, pending (bank payment awaiting settlement), paid or failed.</summary>
    public string PaymentStatus { get; set; } = "unpaid";
    public string? PaymentMethodId { get; set; }
    public string? PaymentMethodType { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public string? PaymentNote { get; set; }
    /// <summary>none, pending, refunded, failed or cancelled.</summary>
    public string RefundStatus { get; set; } = "none";
}

public class StaffOrderDto : OrderDto
{
    public string BuyerId { get; set; } = "";
    public string? TransactionId { get; set; }
    public string? RefundTransactionId { get; set; }
    public DateTimeOffset? RefundRequestedAt { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
    public string? RefundNote { get; set; }
}

public class CheckoutDto
{
    public int OrderId { get; set; }
    public string PaymentMethodId { get; set; } = "";
    public string PaymentMethodType { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal Fee { get; set; }
    public decimal Total { get; set; }
}

public class PayDto : CheckoutDto
{
    public bool Paid { get; set; }
    public string PaymentStatus { get; set; } = "";
    public string? TransactionId { get; set; }
}

public class RefundDto
{
    public int OrderId { get; set; }
    public string RefundStatus { get; set; } = "";
    public decimal Amount { get; set; }
    public string? Message { get; set; }
}

// ---- Services ----------------------------------------------------------------------------------

internal static class OrderLocks
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> Locks = new();

    public static async Task<T> RunAsync<T>(int orderId, Func<Task<T>> action, CancellationToken ct)
    {
        var gate = Locks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            return await action();
        }
        finally
        {
            gate.Release();
        }
    }
}

/// <summary>Feature 2: orders.</summary>
public class ShopOrderService
{
    private readonly CatalogContext _db;
    private readonly IUriComposer _uriComposer;

    public ShopOrderService(CatalogContext db, IUriComposer uriComposer)
    {
        _db = db;
        _uriComposer = uriComposer;
    }

    public async Task<OrderDto> CreateAsync(string email, CreateOrderRequest request, CancellationToken ct)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            throw new RequestRefusedException(400, "An order needs at least one item.");
        }

        var lines = new Dictionary<int, int>();
        foreach (var line in request.Items)
        {
            if (line is null || line.CatalogItemId <= 0)
            {
                throw new RequestRefusedException(400, "Every item needs a catalogItemId.");
            }

            if (line.Quantity < 1 || line.Quantity > 1000)
            {
                throw new RequestRefusedException(400, $"The quantity of item {line.CatalogItemId} must be between 1 and 1000.");
            }

            lines[line.CatalogItemId] = lines.GetValueOrDefault(line.CatalogItemId) + line.Quantity;
        }

        var ids = lines.Keys.ToList();
        var catalog = await _db.CatalogItems.Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var items = new List<OrderItem>();
        foreach (var (id, units) in lines)
        {
            if (!catalog.TryGetValue(id, out var item))
            {
                throw new RequestRefusedException(404, $"Catalog item {id} does not exist.");
            }

            var picture = string.IsNullOrEmpty(item.PictureUri) ? "none" : _uriComposer.ComposePicUri(item.PictureUri);
            items.Add(new OrderItem(new CatalogItemOrdered(item.Id, item.Name, picture), item.Price, units));
        }

        var ship = request.ShipTo;
        var address = new Address(ship?.Street?.Trim() ?? "", ship?.City?.Trim() ?? "", ship?.State?.Trim() ?? "",
            ship?.Country?.Trim() ?? "", ship?.ZipCode?.Trim() ?? "");
        var order = new Order(StaxCustomers.Normalize(email), address, items);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);
        return OrderMapper.Map(order);
    }
}

internal static class OrderMapper
{
    public static OrderDto Map(Order o) => Fill(new OrderDto(), o);

    public static StaffOrderDto MapForStaff(Order o)
    {
        var dto = Fill(new StaffOrderDto(), o);
        dto.BuyerId = o.BuyerId;
        dto.TransactionId = o.PaymentTransactionId;
        dto.RefundTransactionId = o.RefundTransactionId;
        dto.RefundRequestedAt = o.RefundRequestedAt;
        dto.RefundedAt = o.RefundedAt;
        dto.RefundNote = o.RefundNote;
        return dto;
    }

    private static T Fill<T>(T dto, Order o) where T : OrderDto
    {
        dto.Id = o.Id;
        dto.OrderDate = o.OrderDate;
        dto.Items = o.OrderItems.Select(i => new OrderLineDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }).ToList();
        dto.Amount = o.Total();
        dto.Fee = o.Fee;
        dto.Total = o.AmountCharged ?? o.Total();
        dto.Paid = o.IsPaid;
        dto.PaymentStatus = o.PaymentState.ToString().ToLowerInvariant();
        dto.PaymentMethodId = o.PaymentMethodId;
        dto.PaymentMethodType = o.PaymentMethodType;
        dto.PaidAt = o.PaidAt;
        dto.PaymentNote = o.PaymentNote;
        dto.RefundStatus = o.RefundState.ToString().ToLowerInvariant();
        return dto;
    }
}

/// <summary>Features 3, 4, 5 and 8: checkout, paying, order status and refunds.</summary>
public class OrderPaymentService
{
    private readonly CatalogContext _db;
    private readonly StaxGateway _stax;
    private readonly PaymentMethodService _methods;
    private readonly ILogger<OrderPaymentService> _logger;

    public OrderPaymentService(CatalogContext db, StaxGateway stax, PaymentMethodService methods, ILogger<OrderPaymentService> logger)
    {
        _db = db;
        _stax = stax;
        _methods = methods;
        _logger = logger;
    }

    private IQueryable<Order> Orders => _db.Orders.Include(o => o.OrderItems);

    private async Task<Order> LoadOwnedAsync(string email, int orderId, CancellationToken ct)
    {
        var buyer = StaxCustomers.Normalize(email);
        var order = await Orders.FirstOrDefaultAsync(o => o.Id == orderId && o.BuyerId == buyer, ct);
        return order ?? throw new RequestRefusedException(404, $"Order {orderId} was not found.");
    }

    private async Task<Order> LoadAsync(int orderId, CancellationToken ct) =>
        await Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct)
        ?? throw new RequestRefusedException(404, $"Order {orderId} was not found.");

    // ---- Checkout --------------------------------------------------------------------------

    public async Task<CheckoutDto> CheckoutAsync(string email, int orderId, string? paymentMethodId, CancellationToken ct)
    {
        var order = await LoadOwnedAsync(email, orderId, ct);
        EnsurePayable(order);
        var pm = await _methods.GetOwnedAsync(email, RequirePaymentMethod(paymentMethodId), ct);
        var quote = await QuoteAsync(order, pm, ct);
        return new CheckoutDto
        {
            OrderId = order.Id,
            PaymentMethodId = quote.PaymentMethodId,
            PaymentMethodType = quote.Type,
            Amount = quote.Amount,
            Fee = quote.Fee,
            Total = quote.Total
        };
    }

    private static string RequirePaymentMethod(string? id) =>
        string.IsNullOrWhiteSpace(id) ? throw new RequestRefusedException(400, "paymentMethodId is required.") : id.Trim();

    private static void EnsurePayable(Order order)
    {
        switch (order.PaymentState)
        {
            case PaymentState.Paid:
                throw new RequestRefusedException(409, $"Order {order.Id} has already been paid.");
            case PaymentState.Pending:
                throw new RequestRefusedException(409, $"Order {order.Id} already has a payment in progress.");
        }

        if (order.RefundState != RefundState.None)
        {
            throw new RequestRefusedException(409, $"Order {order.Id} has been refunded and can no longer be paid.");
        }

        if (order.Total() <= 0)
        {
            throw new RequestRefusedException(409, $"Order {order.Id} has nothing to pay.");
        }
    }

    private record Quote(string PaymentMethodId, string Type, decimal Amount, decimal Fee)
    {
        public decimal Total => Amount + Fee;
    }

    /// <summary>The goods total plus the card processing fee Stax would add for this payment method.</summary>
    private async Task<Quote> QuoteAsync(Order order, JsonElement pm, CancellationToken ct)
    {
        var amount = Math.Round(order.Total(), 2, MidpointRounding.AwayFromZero);
        var id = pm.Str("id")!;
        var type = pm.Str("method") == "bank" ? "bank" : "card";
        var fee = 0m;
        if (type == "card")
        {
            var review = await _stax.ReviewSurchargeAsync(id, amount, ct);
            fee = review.Dec("surcharge_amount")
                  ?? (review.Dec("total_with_surcharge_amount") is { } withFee ? withFee - amount : 0m);
            fee = Math.Max(0m, Math.Round(fee, 2, MidpointRounding.AwayFromZero));
        }

        return new Quote(id, type, amount, fee);
    }

    // ---- Pay -------------------------------------------------------------------------------

    public Task<PayDto> PayAsync(string email, int orderId, PayOrderRequest request, CancellationToken ct) =>
        OrderLocks.RunAsync(orderId, async () =>
        {
            var order = await LoadOwnedAsync(email, orderId, ct);
            EnsurePayable(order);
            var pm = await _methods.GetOwnedAsync(email, RequirePaymentMethod(request.PaymentMethodId), ct);
            var quote = await QuoteAsync(order, pm, ct);
            if (request.ExpectedTotal is { } expected && Math.Round(expected, 2) != quote.Total)
            {
                throw new RequestRefusedException(409,
                    $"The total changed: it is now {quote.Total:0.00} (amount {quote.Amount:0.00} plus fee {quote.Fee:0.00}), not {expected:0.00}. Check out again.");
            }

            var body = new Dictionary<string, object?>
            {
                ["payment_method_id"] = quote.PaymentMethodId,
                ["total"] = quote.Amount,
                ["pre_auth"] = false,
                ["meta"] = new Dictionary<string, object?>
                {
                    ["reference"] = $"order-{order.Id}",
                    ["orderId"] = order.Id,
                    ["subtotal"] = quote.Amount
                }
            };
            var idempotencyKey = $"eshop-order-{order.Id}-{order.OrderDate.UtcTicks}-{order.PaymentAttempts}";
            var txn = await StaxCall.Json(() => _stax.Shop.Charge.PostCharge(new PostChargeRequest { Body = body },
                StaxCall.WithIdempotencyKey(idempotencyKey), ct));

            var txnId = txn.Str("id");
            var success = txn.Bool("success") ?? false;
            if (!success || txnId is null)
            {
                var reason = FailureReason(txn);
                order.RecordDeclinedPayment(quote.PaymentMethodId, quote.Type, reason);
                await _db.SaveChangesAsync(ct);
                throw new RequestRefusedException(402, $"The payment was declined: {reason}");
            }

            var charged = txn.Dec("total") ?? quote.Total;
            var fee = Math.Max(0m, charged - quote.Amount);
            var settled = quote.Type == "card" || txn.Str("settled_at") is not null;
            order.RecordCharge(quote.PaymentMethodId, quote.Type, txnId, charged, fee, settled);
            await _db.SaveChangesAsync(ct);

            return new PayDto
            {
                OrderId = order.Id,
                PaymentMethodId = quote.PaymentMethodId,
                PaymentMethodType = quote.Type,
                Amount = quote.Amount,
                Fee = fee,
                Total = charged,
                Paid = order.IsPaid,
                PaymentStatus = order.PaymentState.ToString().ToLowerInvariant(),
                TransactionId = txnId
            };
        }, ct);

    private static string FailureReason(JsonElement txn)
    {
        var reason = txn.Str("message") ?? txn.Str("error_description");
        if (string.IsNullOrWhiteSpace(reason) && txn.Prop("response") is { } response)
        {
            reason = response.Str("message");
        }

        return string.IsNullOrWhiteSpace(reason) ? "Stax declined the payment." : reason!;
    }

    // ---- Order status ----------------------------------------------------------------------

    public async Task<OrderDto> GetAsync(string email, int orderId, CancellationToken ct)
    {
        var order = await LoadOwnedAsync(email, orderId, ct);
        await TryRefreshAsync(order, ct);
        return OrderMapper.Map(order);
    }

    public async Task<StaffOrderDto> GetForStaffAsync(int orderId, CancellationToken ct)
    {
        var order = await LoadAsync(orderId, ct);
        await TryRefreshAsync(order, ct);
        return OrderMapper.MapForStaff(order);
    }

    private async Task TryRefreshAsync(Order order, CancellationToken ct)
    {
        if (order.PaymentState != PaymentState.Pending && order.RefundState != RefundState.Pending)
        {
            return;
        }

        try
        {
            await OrderLocks.RunAsync(order.Id, async () =>
            {
                await _db.Entry(order).ReloadAsync(ct);
                await RefreshAsync(order, retryRefund: false, ct);
                return 0;
            }, ct);
        }
        catch (RequestRefusedException ex)
        {
            // Stax is unreachable right now: answer with what is known, the background sync catches up later
            _logger.LogWarning(ex, "Could not refresh order {OrderId} from Stax", order.Id);
        }
    }

    /// <summary>Brings a pending payment and a pending refund up to date with Stax. The order lock must be held.</summary>
    public async Task RefreshAsync(Order order, bool retryRefund, CancellationToken ct)
    {
        if (order.PaymentState == PaymentState.Pending && order.PaymentTransactionId is not null)
        {
            var txn = await GetTransactionAsync(order.PaymentTransactionId, ct);
            ApplyTransaction(order, txn);
        }

        if (retryRefund && order.RefundState == RefundState.Pending && order.PaymentState != PaymentState.Failed)
        {
            var outcome = await TryRefundAsync(order, ct);
            ApplyRefundOutcome(order, outcome, keepPendingOnRejection: false);
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task<JsonElement> GetTransactionAsync(string transactionId, CancellationToken ct) =>
        await StaxCall.Json(() => _stax.Shop.Transaction.GetTransaction(
            new GetTransactionRequest { Id = Guid.Parse(transactionId) }, cancellationToken: ct));

    private static void ApplyTransaction(Order order, JsonElement txn)
    {
        var status = txn.Str("status")?.ToUpperInvariant();
        var failed = txn.Bool("success") == false || txn.Bool("is_voided") == true ||
                     status is "FAILED" or "DECLINED" or "RETURNED" or "REJECTED" or "ERROR" or "VOIDED";
        if (failed)
        {
            order.MarkPaymentFailed(FailureReason(txn));
        }
        else if (order.PaymentMethodType == "card" || txn.Str("settled_at") is not null)
        {
            order.MarkPaid();
        }
    }

    // ---- Refunds ---------------------------------------------------------------------------

    private enum RefundOutcome { Done, NotYet, Rejected }

    private sealed class RefundResult
    {
        public RefundOutcome Outcome { get; init; }
        public string? TransactionId { get; init; }
        public string? Reason { get; init; }
        public int StatusCode { get; init; }
    }

    private async Task<RefundResult> TryRefundAsync(Order order, CancellationToken ct)
    {
        try
        {
            var txn = await StaxCall.Json(() => _stax.Shop.Transaction.PostRefund(new PostRefundRequest
            {
                Id = Guid.Parse(order.PaymentTransactionId!),
                Body = new PostRefundRequestBodyJson { Total = (double?)order.AmountCharged }
            }, cancellationToken: ct));
            return new RefundResult { Outcome = RefundOutcome.Done, TransactionId = FindRefundId(txn) };
        }
        catch (RequestRefusedException ex) when (ex.StatusCode == 422 &&
            (ex.Message.Contains("unavailable until", StringComparison.OrdinalIgnoreCase) ||
             ex.Message.Contains("not yet settled", StringComparison.OrdinalIgnoreCase) ||
             ex.Message.Contains("currently being processed", StringComparison.OrdinalIgnoreCase)))
        {
            return new RefundResult { Outcome = RefundOutcome.NotYet, Reason = ex.Message, StatusCode = ex.StatusCode };
        }
        catch (RequestRefusedException ex) when (ex.StatusCode == 422)
        {
            // maybe it was refunded already (for instance by an earlier attempt whose answer got lost)
            try
            {
                var txn = await GetTransactionAsync(order.PaymentTransactionId!, ct);
                if (order.AmountCharged is { } charged && (txn.Dec("total_refunded") ?? 0m) >= charged)
                {
                    return new RefundResult { Outcome = RefundOutcome.Done, TransactionId = FindRefundId(txn) };
                }
            }
            catch (RequestRefusedException)
            {
                // fall through to the original refusal
            }

            return new RefundResult { Outcome = RefundOutcome.Rejected, Reason = ex.Message, StatusCode = ex.StatusCode };
        }
    }

    private static string? FindRefundId(JsonElement txn)
    {
        if (txn.Prop("child_transactions") is { ValueKind: JsonValueKind.Array } children)
        {
            foreach (var c in children.EnumerateArray())
            {
                if (c.Str("type") == "refund")
                {
                    return c.Str("id");
                }
            }
        }

        return null;
    }

    private void ApplyRefundOutcome(Order order, RefundResult result, bool keepPendingOnRejection)
    {
        switch (result.Outcome)
        {
            case RefundOutcome.Done:
                order.MarkRefunded(result.TransactionId, DateTimeOffset.UtcNow);
                break;
            case RefundOutcome.NotYet:
                order.SetRefundNote("Waiting for the bank: it only allows a bank payment to be refunded some business days after it was made. The refund is sent automatically.");
                break;
            default:
                if (!keepPendingOnRejection)
                {
                    order.MarkRefundFailed(result.Reason);
                }
                break;
        }
    }

    public Task<RefundDto> RefundAsync(int orderId, RefundOrderRequest? request, CancellationToken ct) =>
        OrderLocks.RunAsync(orderId, async () =>
        {
            var order = await LoadAsync(orderId, ct);
            if (order.PaymentTransactionId is null || order.PaymentState is PaymentState.Unpaid or PaymentState.Failed)
            {
                throw new RequestRefusedException(409, $"Order {orderId} has no payment to refund.");
            }

            switch (order.RefundState)
            {
                case RefundState.Refunded:
                    throw new RequestRefusedException(409, $"Order {orderId} has already been refunded.");
                case RefundState.Pending:
                    throw new RequestRefusedException(409, $"A refund for order {orderId} is already on its way.");
            }

            if (order.PaymentState == PaymentState.Pending)
            {
                var txn = await GetTransactionAsync(order.PaymentTransactionId, ct);
                ApplyTransaction(order, txn);
                if (order.PaymentState == PaymentState.Failed)
                {
                    await _db.SaveChangesAsync(ct);
                    throw new RequestRefusedException(409, $"The payment of order {orderId} did not go through, so there is nothing to refund.");
                }
            }

            var result = await TryRefundAsync(order, ct);
            if (result.Outcome == RefundOutcome.Rejected)
            {
                throw new RequestRefusedException(422, $"The refund was refused: {result.Reason}");
            }

            order.RequestRefund(DateTimeOffset.UtcNow);
            ApplyRefundOutcome(order, result, keepPendingOnRejection: true);
            await _db.SaveChangesAsync(ct);
            return new RefundDto
            {
                OrderId = order.Id,
                RefundStatus = order.RefundState.ToString().ToLowerInvariant(),
                Amount = order.AmountCharged ?? order.Total(),
                Message = order.RefundNote ?? (order.RefundState == RefundState.Refunded ? "The whole amount was refunded." : null)
            };
        }, ct);

    // ---- Background sync -------------------------------------------------------------------

    public async Task SyncPendingAsync(CancellationToken ct)
    {
        var ids = await _db.Orders
            .Where(o => o.PaymentState == PaymentState.Pending || o.RefundState == RefundState.Pending)
            .Select(o => o.Id)
            .ToListAsync(ct);
        foreach (var id in ids)
        {
            try
            {
                await OrderLocks.RunAsync(id, async () =>
                {
                    var order = await Orders.FirstOrDefaultAsync(o => o.Id == id, ct);
                    if (order is not null)
                    {
                        await _db.Entry(order).ReloadAsync(ct);
                        await RefreshAsync(order, retryRefund: true, ct);
                    }

                    return 0;
                }, ct);
            }
            catch (RequestRefusedException ex)
            {
                _logger.LogWarning(ex, "Syncing order {OrderId} with Stax failed; will retry", id);
            }
        }
    }
}
