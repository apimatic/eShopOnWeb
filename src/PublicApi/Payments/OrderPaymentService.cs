using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int ItemId { get => CatalogItemId; set { if (CatalogItemId == 0) CatalogItemId = value; } }
    public int Id { get => CatalogItemId; set { if (CatalogItemId == 0) CatalogItemId = value; } }
    public int Quantity { get; set; }
    public int Units { get => Quantity; set { if (Quantity == 0) Quantity = value; } }
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
    public string? PaymentMethod { get => PaymentMethodId; set => PaymentMethodId ??= value; }
}

public record OrderItemDto(int CatalogItemId, string Name, decimal UnitPrice, int Quantity, decimal LineTotal);

public record OrderDto(
    int OrderId,
    DateTimeOffset OrderDate,
    IReadOnlyList<OrderItemDto> Items,
    decimal Subtotal,
    decimal Fee,
    decimal Total,
    bool Paid,
    string PaymentStatus,
    string? PaymentMethod,
    string RefundStatus);

public record AdminOrderDto(
    int OrderId,
    string Buyer,
    DateTimeOffset OrderDate,
    IReadOnlyList<OrderItemDto> Items,
    decimal Subtotal,
    decimal Fee,
    decimal Total,
    bool Paid,
    string PaymentStatus,
    string? PaymentMethod,
    string? PaymentMethodId,
    string? TransactionId,
    string? PaymentFailure,
    string RefundStatus,
    DateTimeOffset? RefundRequestedAt,
    DateTimeOffset? RefundedAt,
    string? RefundNote);

public record CheckoutDto(int OrderId, string PaymentMethodId, string PaymentMethod, decimal Subtotal, decimal Fee, decimal Total);

public record PayDto(int OrderId, string PaymentMethodId, string PaymentMethod, decimal Subtotal, decimal Fee, decimal Total, bool Paid, string PaymentStatus, string? TransactionId);

public record RefundDto(int OrderId, string RefundStatus, decimal Amount, DateTimeOffset RequestedAt, DateTimeOffset? RefundedAt, string? Note);

public class OrderPaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();

    private readonly CatalogContext _db;
    private readonly StaxGateway _stax;
    private readonly ShopperDirectory _shoppers;
    private readonly IUriComposer _uriComposer;
    private readonly ILogger<OrderPaymentService> _logger;

    public OrderPaymentService(CatalogContext db, StaxGateway stax, ShopperDirectory shoppers, IUriComposer uriComposer, ILogger<OrderPaymentService> logger)
    {
        _db = db;
        _stax = stax;
        _shoppers = shoppers;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    // ------------------------------------------------------------------ orders

    public async Task<OrderDto> CreateOrder(string email, CreateOrderRequest request, CancellationToken ct)
    {
        if (request.Items == null || request.Items.Count == 0)
            throw AppException.BadRequest("An order needs at least one item.");
        if (request.Items.Any(i => i.Quantity < 1 || i.Quantity > 1000))
            throw AppException.BadRequest("Each item's quantity must be between 1 and 1000.");

        var lines = request.Items.GroupBy(i => i.CatalogItemId).Select(g => (Id: g.Key, Quantity: g.Sum(i => i.Quantity))).ToList();
        var ids = lines.Select(l => l.Id).ToList();
        var catalog = await _db.CatalogItems.Where(c => ids.Contains(c.Id)).ToListAsync(ct);
        var missing = ids.Where(id => catalog.All(c => c.Id != id)).ToList();
        if (missing.Count > 0)
            throw AppException.NotFound($"Catalog item(s) not found: {string.Join(", ", missing)}.");

        var items = lines.Select(l =>
        {
            var item = catalog.First(c => c.Id == l.Id);
            var ordered = new CatalogItemOrdered(item.Id, item.Name, _uriComposer.ComposePicUri(item.PictureUri ?? string.Empty) is { Length: > 0 } uri ? uri : "none");
            return new OrderItem(ordered, item.Price, l.Quantity);
        }).ToList();

        var ship = request.ShipTo;
        var address = new Address(ship?.Street ?? string.Empty, ship?.City ?? string.Empty, ship?.State ?? string.Empty,
            ship?.Country ?? string.Empty, ship?.ZipCode ?? string.Empty);
        var order = new Order(email, address, items);
        if (order.Total() <= 0)
            throw AppException.Unprocessable("The order total must be greater than zero.");

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);
        return ToDto(order, null);
    }

    public async Task<OrderDto> GetOrder(string email, int orderId, CancellationToken ct)
    {
        var order = await LoadOrder(orderId, ct);
        if (order == null || !string.Equals(order.BuyerId, email, StringComparison.OrdinalIgnoreCase))
            throw AppException.NotFound("No such order.");
        var payment = await _db.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
        if (payment is { Status: PaymentStatus.PendingSettlement })
            await RefreshPayment(payment, ct);
        return ToDto(order, payment);
    }

    public async Task<AdminOrderDto> GetOrderForStaff(int orderId, CancellationToken ct)
    {
        var order = await LoadOrder(orderId, ct) ?? throw AppException.NotFound("No such order.");
        var payment = await _db.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
        if (payment is { Status: PaymentStatus.PendingSettlement })
            await RefreshPayment(payment, ct);
        if (payment is { RefundStatus: RefundStatus.Pending })
            await TryRefund(payment, initial: false, ct);
        return ToAdminDto(order, payment);
    }

    // ------------------------------------------------------------------ checkout and pay

    public async Task<CheckoutDto> Checkout(string email, int orderId, string? paymentMethodId, CancellationToken ct)
    {
        var order = await RequireOwnOrder(email, orderId, ct);
        var existing = await _db.OrderPayments.AsNoTracking().FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
        EnsurePayable(existing);
        var quote = await Quote(email, order, paymentMethodId, ct);
        return new CheckoutDto(orderId, quote.PaymentMethodId, quote.Method, quote.Subtotal, quote.Fee, quote.Total);
    }

    public async Task<PayDto> Pay(string email, int orderId, string? paymentMethodId, CancellationToken ct)
    {
        var order = await RequireOwnOrder(email, orderId, ct);

        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var payment = await _db.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
            EnsurePayable(payment);
            var quote = await Quote(email, order, paymentMethodId, ct);

            if (payment == null)
            {
                payment = new OrderPayment { OrderId = orderId, BuyerEmail = email };
                _db.OrderPayments.Add(payment);
            }

            // A failed earlier attempt may in fact have gone through (e.g. a lost answer): never charge twice.
            if (payment.Attempts > 0)
            {
                var adopted = await FindExistingCharge(quote.CustomerId, orderId, ct);
                if (adopted.ValueKind == JsonValueKind.Object)
                {
                    Apply(payment, quote, adopted);
                    await _db.SaveChangesAsync(ct);
                    return ToPayDto(payment);
                }
            }

            payment.Attempts++;
            payment.Method = quote.Method;
            payment.PaymentMethodId = quote.PaymentMethodId;
            payment.StaxCustomerId = quote.CustomerId;
            payment.Subtotal = quote.Subtotal;
            payment.Fee = quote.Fee;
            payment.Total = quote.Total;
            payment.Status = PaymentStatus.Processing;
            payment.FailureReason = null;
            await _db.SaveChangesAsync(ct);

            JsonElement tx;
            try
            {
                tx = await _stax.Charge(new Dictionary<string, object?>
                {
                    ["payment_method_id"] = quote.PaymentMethodId,
                    ["total"] = quote.Total,
                    ["pre_auth"] = false,
                    ["meta"] = new Dictionary<string, object?>
                    {
                        ["memo"] = $"eShopOnWeb order {orderId}",
                        ["orderId"] = orderId,
                        ["subtotal"] = quote.Subtotal,
                        ["fee"] = quote.Fee
                    }
                }, ct);
            }
            catch (AppException e)
            {
                payment.Status = PaymentStatus.Failed;
                payment.FailureReason = Truncate(e.Message);
                await _db.SaveChangesAsync(CancellationToken.None);
                throw e.StatusCode == HttpStatusCode.UnprocessableEntity
                    ? new AppException(HttpStatusCode.PaymentRequired, "The payment was not accepted. " + e.Message, e)
                    : e;
            }

            Apply(payment, quote, tx);
            await _db.SaveChangesAsync(ct);

            if (payment.Status == PaymentStatus.Failed)
                throw new AppException(HttpStatusCode.PaymentRequired, "The payment was declined: " + (payment.FailureReason ?? "no reason given") + ".");
            return ToPayDto(payment);
        }
        finally
        {
            gate.Release();
        }
    }

    private static void EnsurePayable(OrderPayment? payment)
    {
        if (payment == null) return;
        switch (payment.Status)
        {
            case PaymentStatus.Paid:
                throw AppException.Conflict("The order is already paid.");
            case PaymentStatus.PendingSettlement:
                throw AppException.Conflict("The order is already paid: the bank payment is waiting to settle.");
            case PaymentStatus.Processing:
                throw AppException.Conflict("A payment for this order is being processed.");
        }
    }

    private record QuoteResult(string PaymentMethodId, string Method, string CustomerId, decimal Subtotal, decimal Fee, decimal Total);

    private async Task<QuoteResult> Quote(string email, Order order, string? paymentMethodId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(paymentMethodId))
            throw AppException.BadRequest("paymentMethodId is required.");
        var customerId = await _shoppers.FindCustomerId(email, ct) ?? throw AppException.NotFound("No such saved payment method.");
        if (!Guid.TryParse(paymentMethodId, out _))
            throw AppException.NotFound("No such saved payment method.");

        JsonElement pm;
        try
        {
            pm = await _stax.GetPaymentMethod(paymentMethodId, ct);
        }
        catch (AppException e) when (e.StatusCode == HttpStatusCode.NotFound || e.StaxStatus == 422)
        {
            throw AppException.NotFound("No such saved payment method.");
        }

        if (!string.Equals(StaxGateway.Str(pm, "customer_id"), customerId, StringComparison.OrdinalIgnoreCase))
            throw AppException.NotFound("No such saved payment method.");
        if (StaxGateway.Str(pm, "deleted_at") != null)
            throw AppException.Unprocessable("That payment method has been removed.");

        var method = PaymentMethodService.TypeOf(pm);
        var subtotal = order.Total();
        var fee = 0m;
        if (method == "card")
        {
            var registration = await _stax.GetRegistration(ct);
            fee = FeeCalculator.Compute(registration, method, StaxGateway.Str(pm, "card_type"), StaxGateway.Str(pm, "bin_type"), subtotal);
        }
        return new QuoteResult(StaxGateway.Str(pm, "id")!, method, customerId, subtotal, fee, subtotal + fee);
    }

    private async Task<JsonElement> FindExistingCharge(string customerId, int orderId, CancellationToken ct)
    {
        foreach (var t in await _stax.ListCustomerTransactions(customerId, ct))
        {
            if (StaxGateway.Str(t, "type") != "charge" || !StaxGateway.Bool(t, "success")) continue;
            if (t.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object &&
                meta.TryGetProperty("orderId", out var o) && o.ValueKind == JsonValueKind.Number && o.TryGetInt32(out var id) && id == orderId)
                return t;
        }
        return default;
    }

    /// <summary>Records the outcome of a charge on the payment.</summary>
    private static void Apply(OrderPayment payment, QuoteResult quote, JsonElement tx)
    {
        payment.Method = quote.Method;
        payment.PaymentMethodId = quote.PaymentMethodId;
        payment.StaxCustomerId = quote.CustomerId;
        payment.Subtotal = quote.Subtotal;
        payment.Fee = quote.Fee;
        payment.Total = quote.Total;
        payment.StaxTransactionId = StaxGateway.Str(tx, "id");
        ApplyTransaction(payment, tx);
    }

    /// <summary>
    /// Card payments have gone through when Stax accepts the charge. A bank payment is only accepted then;
    /// it has gone through when the bank settles it (Stax sets <c>settled_at</c>).
    /// </summary>
    private static void ApplyTransaction(OrderPayment payment, JsonElement tx)
    {
        var failedFlag = tx.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.False;
        var status = StaxGateway.Str(tx, "status");
        if (failedFlag || string.Equals(status, "FAILED", StringComparison.OrdinalIgnoreCase))
        {
            payment.Status = PaymentStatus.Failed;
            payment.FailureReason = Truncate(StaxGateway.Str(tx, "message")
                                             ?? StaxGateway.Str(tx, "error_description")
                                             ?? (tx.TryGetProperty("response", out var r) ? StaxGateway.Str(r, "message") : null)
                                             ?? "declined");
            return;
        }

        if (payment.Method == "bank" && StaxGateway.Str(tx, "settled_at") == null)
        {
            payment.Status = PaymentStatus.PendingSettlement;
            return;
        }

        payment.Status = PaymentStatus.Paid;
        payment.PaidAt ??= StaxGateway.Time(tx, "settled_at") ?? DateTimeOffset.UtcNow;
        payment.FailureReason = null;
    }

    /// <summary>Asks Stax whether a bank payment has settled (or came back).</summary>
    public async Task RefreshPayment(OrderPayment payment, CancellationToken ct)
    {
        if (payment.StaxTransactionId == null) return;
        try
        {
            var tx = await _stax.GetTransaction(payment.StaxTransactionId, ct);
            if (tx.ValueKind != JsonValueKind.Object) return;
            ApplyTransaction(payment, tx);
            await _db.SaveChangesAsync(ct);
        }
        catch (AppException e)
        {
            _logger.LogWarning("Could not refresh payment of order {OrderId}: {Message}", payment.OrderId, e.Message);
        }
    }

    // ------------------------------------------------------------------ refunds

    public async Task<RefundDto> Refund(int orderId, CancellationToken ct)
    {
        _ = await LoadOrder(orderId, ct) ?? throw AppException.NotFound("No such order.");

        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var payment = await _db.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
            if (payment == null || payment.StaxTransactionId == null ||
                payment.Status is not (PaymentStatus.Paid or PaymentStatus.PendingSettlement))
                throw AppException.Conflict("The order has no completed payment to refund.");
            if (payment.RefundStatus == RefundStatus.Refunded)
                throw AppException.Conflict("The order has already been refunded.");
            if (payment.RefundStatus == RefundStatus.Pending)
                throw AppException.Conflict("A refund of this order is already under way.");

            payment.RefundStatus = RefundStatus.Pending;
            payment.RefundRequestedAt = DateTimeOffset.UtcNow;
            payment.RefundNote = null;
            await _db.SaveChangesAsync(ct);

            await TryRefund(payment, initial: true, ct);
            return new RefundDto(orderId, RefundName(payment.RefundStatus), payment.Total, payment.RefundRequestedAt!.Value, payment.RefundedAt, payment.RefundNote);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Gives the whole amount back. Stax voids or refunds depending on whether the payment settled; banks
    /// refuse ACH refunds for the first business days, so those stay pending and are retried until they go through.
    /// </summary>
    public async Task TryRefund(OrderPayment payment, bool initial, CancellationToken ct)
    {
        if (payment.RefundStatus != RefundStatus.Pending || payment.StaxTransactionId == null) return;
        try
        {
            var result = await _stax.VoidOrRefund(payment.StaxTransactionId, payment.Total, ct);
            if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.False)
                throw new AppException(HttpStatusCode.UnprocessableEntity, StaxGateway.Str(result, "message") ?? "The refund was not accepted.");

            payment.RefundStatus = RefundStatus.Refunded;
            payment.RefundedAt = DateTimeOffset.UtcNow;
            payment.RefundTransactionId = LastChildId(result) ?? StaxGateway.Str(result, "id");
            payment.RefundNote = null;
        }
        catch (AppException e) when (e.StaxMessage?.Contains("already been fully refunded", StringComparison.OrdinalIgnoreCase) == true
                                     || e.StaxMessage?.Contains("already been voided", StringComparison.OrdinalIgnoreCase) == true)
        {
            payment.RefundStatus = RefundStatus.Refunded;
            payment.RefundedAt ??= DateTimeOffset.UtcNow;
            payment.RefundNote = null;
        }
        catch (AppException e) when (IsTooEarly(e))
        {
            payment.RefundNote = Truncate(e.StaxMessage ?? e.Message);
        }
        catch (AppException e) when (initial && e.StaxStatus is >= 400 and < 500)
        {
            // Stax will not do it and never will: do not leave a refund promised.
            payment.RefundStatus = RefundStatus.None;
            payment.RefundRequestedAt = null;
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (AppException e)
        {
            // Stax unreachable or odd answer: the shopper is owed the money, so keep trying.
            payment.RefundNote = Truncate(e.Message);
            _logger.LogWarning("Refund of order {OrderId} not done yet: {Message}", payment.OrderId, e.Message);
        }
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private static bool IsTooEarly(AppException e) =>
        e.StaxMessage?.Contains("unavailable until", StringComparison.OrdinalIgnoreCase) == true
        || e.StaxMessage?.Contains("business day", StringComparison.OrdinalIgnoreCase) == true;

    private static string? LastChildId(JsonElement result)
    {
        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("child_transactions", out var kids) && kids.ValueKind == JsonValueKind.Array)
        {
            string? id = null;
            foreach (var k in kids.EnumerateArray()) id = StaxGateway.Str(k, "id") ?? id;
            return id;
        }
        return null;
    }

    // ------------------------------------------------------------------ background work

    /// <summary>Settles bank payments that went through at the bank and completes refunds that had to wait.</summary>
    public async Task Reconcile(CancellationToken ct)
    {
        var pending = await _db.OrderPayments.Where(p => p.Status == PaymentStatus.PendingSettlement).ToListAsync(ct);
        foreach (var p in pending)
            await RefreshPayment(p, ct);

        var refunds = await _db.OrderPayments.Where(p => p.RefundStatus == RefundStatus.Pending).ToListAsync(ct);
        foreach (var p in refunds)
        {
            var gate = OrderLocks.GetOrAdd(p.OrderId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(ct);
            try { await TryRefund(p, initial: false, ct); }
            finally { gate.Release(); }
        }
    }

    // ------------------------------------------------------------------ helpers

    private Task<Order?> LoadOrder(int orderId, CancellationToken ct) =>
        _db.Orders.Include(o => o.OrderItems).FirstOrDefaultAsync(o => o.Id == orderId, ct);

    private async Task<Order> RequireOwnOrder(string email, int orderId, CancellationToken ct)
    {
        var order = await LoadOrder(orderId, ct);
        if (order == null || !string.Equals(order.BuyerId, email, StringComparison.OrdinalIgnoreCase))
            throw AppException.NotFound("No such order.");
        return order;
    }

    private static string PaymentName(OrderPayment? p) => p?.Status switch
    {
        PaymentStatus.Processing => "processing",
        PaymentStatus.PendingSettlement => "pending_settlement",
        PaymentStatus.Paid => "paid",
        PaymentStatus.Failed => "failed",
        _ => "unpaid"
    };

    private static string RefundName(RefundStatus s) => s switch
    {
        RefundStatus.Pending => "pending",
        RefundStatus.Refunded => "refunded",
        _ => "none"
    };

    private static List<OrderItemDto> Items(Order order) => order.OrderItems
        .Select(i => new OrderItemDto(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units, i.UnitPrice * i.Units)).ToList();

    private static OrderDto ToDto(Order order, OrderPayment? p)
    {
        var subtotal = order.Total();
        var charged = p is { Status: not PaymentStatus.Failed };
        return new OrderDto(order.Id, order.OrderDate, Items(order), subtotal,
            charged ? p!.Fee : 0m, charged ? p!.Total : subtotal,
            p?.Status == PaymentStatus.Paid, PaymentName(p), charged ? p!.Method : null, RefundName(p?.RefundStatus ?? RefundStatus.None));
    }

    private static AdminOrderDto ToAdminDto(Order order, OrderPayment? p)
    {
        var subtotal = order.Total();
        var charged = p is { Status: not PaymentStatus.Failed };
        return new AdminOrderDto(order.Id, order.BuyerId, order.OrderDate, Items(order), subtotal,
            charged ? p!.Fee : 0m, charged ? p!.Total : subtotal,
            p?.Status == PaymentStatus.Paid, PaymentName(p), p?.Method, p?.PaymentMethodId, p?.StaxTransactionId, p?.FailureReason,
            RefundName(p?.RefundStatus ?? RefundStatus.None), p?.RefundRequestedAt, p?.RefundedAt, p?.RefundNote);
    }

    private static PayDto ToPayDto(OrderPayment p) =>
        new(p.OrderId, p.PaymentMethodId, p.Method, p.Subtotal, p.Fee, p.Total, p.Status == PaymentStatus.Paid, PaymentName(p), p.StaxTransactionId);

    private static string Truncate(string s) => s.Length > 480 ? s[..480] : s;
}
