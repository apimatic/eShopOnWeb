using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payments;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Stax;

public record OrderLineInput(int CatalogItemId, int Units);
public record AddressInput(string? Street, string? City, string? State, string? Country, string? ZipCode);

public record OrderLineView(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

public record OrderView(
    int OrderId, DateTimeOffset OrderDate, IReadOnlyList<OrderLineView> Items,
    decimal Subtotal, decimal Fee, decimal Total,
    bool Paid, string PaymentStatus, string? PaymentMethodId, string? PaymentMethod,
    string RefundStatus);

public record AdminOrderView(
    int OrderId, string BuyerId, DateTimeOffset OrderDate, IReadOnlyList<OrderLineView> Items,
    decimal Subtotal, decimal Fee, decimal Total,
    bool Paid, string PaymentStatus, string? PaymentMethodId, string? PaymentMethod,
    string? StaxTransactionId, DateTimeOffset? PaidAt,
    string RefundStatus, decimal? RefundAmount, DateTimeOffset? RefundRequestedAt, DateTimeOffset? RefundedAt, string? RefundMessage);

public record CheckoutQuote(int OrderId, string PaymentMethodId, string Method, decimal Subtotal, decimal Fee, decimal Total);

public record PayResult(int OrderId, bool Paid, string PaymentStatus, decimal Subtotal, decimal Fee, decimal Total,
    string? TransactionId, string Method);

public record RefundResult(int OrderId, string RefundStatus, decimal Amount, string? Message);

/// <summary>Serialises work on one order (pay, refund, reconcile) inside this process.</summary>
public class OrderLocks
{
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> LockAsync(int orderId, CancellationToken ct)
    {
        var gate = _locks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        return new Releaser(gate);
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _gate;
        public Releaser(SemaphoreSlim gate) => _gate = gate;
        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}

public class OrderPaymentService
{
    // States that do not allow a card surcharge under Stax's "state_check" policy.
    private static readonly HashSet<string> NoSurchargeStates = new(StringComparer.OrdinalIgnoreCase) { "CT", "MA", "PR" };

    private readonly CatalogContext _db;
    private readonly StaxGateway _stax;
    private readonly PaymentMethodService _methods;
    private readonly IUriComposer _uri;
    private readonly OrderLocks _locks;

    public OrderPaymentService(CatalogContext db, StaxGateway stax, PaymentMethodService methods, IUriComposer uri, OrderLocks locks)
    {
        _db = db;
        _stax = stax;
        _methods = methods;
        _uri = uri;
        _locks = locks;
    }

    // ------------------------------------------------------------------ orders

    public async Task<OrderView> PlaceOrderAsync(Shopper shopper, IReadOnlyList<OrderLineInput>? lines, AddressInput? address, CancellationToken ct)
    {
        if (lines is null || lines.Count == 0)
            throw PaymentRequestException.BadRequest("An order needs at least one item.");
        foreach (var line in lines)
        {
            if (line.Units < 1) throw PaymentRequestException.BadRequest("Every item needs units of at least 1.");
            if (line.Units > 10000) throw PaymentRequestException.BadRequest("Too many units of one item.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToList();
        var catalog = await _db.CatalogItems.Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var missing = ids.Where(i => !catalog.ContainsKey(i)).ToList();
        if (missing.Count > 0)
            throw PaymentRequestException.NotFound($"Catalog item(s) not found: {string.Join(", ", missing)}.");

        var items = new List<OrderItem>();
        foreach (var group in lines.GroupBy(l => l.CatalogItemId))
        {
            var item = catalog[group.Key];
            var picture = string.IsNullOrEmpty(item.PictureUri) ? "none" : _uri.ComposePicUri(item.PictureUri);
            items.Add(new OrderItem(new CatalogItemOrdered(item.Id, item.Name, picture), item.Price, group.Sum(g => g.Units)));
        }

        var ship = new Address(address?.Street ?? "", address?.City ?? "", address?.State ?? "", address?.Country ?? "", address?.ZipCode ?? "");
        var order = new Order(shopper.BuyerId, ship, items);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);
        return ToView(order, null);
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken ct) =>
        await _db.Orders.Include(o => o.OrderItems).FirstOrDefaultAsync(o => o.Id == orderId, ct)
        ?? throw PaymentRequestException.NotFound("No such order.");

    private async Task<Order> LoadOwnOrderAsync(Shopper shopper, int orderId, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct);
        // someone else's order looks the same as a missing one
        if (!string.Equals(order.BuyerId, shopper.BuyerId, StringComparison.Ordinal))
            throw PaymentRequestException.NotFound("No such order.");
        return order;
    }

    private static List<OrderLineView> Lines(Order order) =>
        order.OrderItems.Select(i => new OrderLineView(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units)).ToList();

    private static OrderView ToView(Order order, OrderPayment? p)
    {
        var subtotal = order.Total();
        return new OrderView(order.Id, order.OrderDate, Lines(order),
            subtotal, p?.Fee ?? 0m, p?.Total ?? subtotal,
            IsPaid(p), p?.Status ?? "unpaid", p?.StaxPaymentMethodId, p?.Method,
            p?.RefundStatus ?? RefundStatuses.None);
    }

    /// <summary>Paid: the money has gone through and has not been given back.</summary>
    private static bool IsPaid(OrderPayment? p) =>
        p is { Status: PaymentStatuses.Paid } && p.RefundStatus != RefundStatuses.Refunded;

    public async Task<OrderView> GetOrderAsync(Shopper shopper, int orderId, CancellationToken ct)
    {
        var order = await LoadOwnOrderAsync(shopper, orderId, ct);
        var p = await RefreshAsync(orderId, ct);
        return ToView(order, p);
    }

    public async Task<AdminOrderView> GetAdminOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct);
        var p = await RefreshAsync(orderId, ct);
        var subtotal = order.Total();
        return new AdminOrderView(order.Id, order.BuyerId, order.OrderDate, Lines(order),
            subtotal, p?.Fee ?? 0m, p?.Total ?? subtotal,
            IsPaid(p), p?.Status ?? "unpaid", p?.StaxPaymentMethodId, p?.Method,
            p?.StaxTransactionId, p?.PaidAt,
            p?.RefundStatus ?? RefundStatuses.None, p?.RefundAmount, p?.RefundRequestedAt, p?.RefundedAt, p?.RefundMessage);
    }

    // ------------------------------------------------------------------ fees

    /// <summary>
    /// The fee a payment method carries: card processing fees are passed on to the shopper, as configured at Stax.
    /// A bank payment, a debit or prepaid card carries none.
    /// </summary>
    public static decimal ComputeFee(decimal subtotal, JsonElement paymentMethod, SurchargeSettings settings)
    {
        if (!string.Equals(paymentMethod.Str("method"), "card", StringComparison.OrdinalIgnoreCase)) return 0m;
        if (!settings.Enabled || settings.CreditRate is not { } rate || rate <= 0m) return 0m;
        if (!string.Equals(paymentMethod.Str("bin_type"), "CREDIT", StringComparison.OrdinalIgnoreCase)) return 0m;
        if (paymentMethod.Prop("meta")?.Bool("prepaid") == true) return 0m;
        if (string.Equals(settings.Policy, "state_check", StringComparison.OrdinalIgnoreCase) &&
            paymentMethod.Str("address_state") is { } state && NoSurchargeStates.Contains(state.Trim()))
            return 0m;

        // Stax keeps the rate as a percentage (3 = 3%); a fraction (0.03) is read the same way
        var fraction = rate > 0.1m ? rate / 100m : rate;
        return Math.Round(subtotal * fraction, 2, MidpointRounding.AwayFromZero);
    }

    private async Task<(JsonElement Pm, decimal Fee)> QuoteAsync(decimal subtotal, string paymentMethodId, CancellationToken ct)
    {
        var pm = await _stax.GetPaymentMethodAsync(paymentMethodId, ct);
        if (pm is not { } element || element.Time("deleted_at") is not null)
            throw PaymentRequestException.NotFound("That payment method is no longer available.");
        var settings = await _stax.GetSurchargeSettingsAsync(ct);
        return (element, ComputeFee(subtotal, element, settings));
    }

    private static void EnsureOrderPayable(Order order, OrderPayment? existing)
    {
        if (order.Total() <= 0m)
            throw PaymentRequestException.Unprocessable("This order has nothing to pay.");
        if (existing is not null && existing.Status != PaymentStatuses.Failed)
            throw PaymentRequestException.Conflict(existing.Status == PaymentStatuses.Paid
                ? "This order is already paid."
                : "A payment for this order is already in progress.");
    }

    public async Task<CheckoutQuote> CheckoutAsync(Shopper shopper, int orderId, string? paymentMethodId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(paymentMethodId))
            throw PaymentRequestException.BadRequest("paymentMethodId is required.");
        var order = await LoadOwnOrderAsync(shopper, orderId, ct);
        var existing = await _db.OrderPayments.AsNoTracking().FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
        EnsureOrderPayable(order, existing);
        var saved = await _methods.GetActiveAsync(shopper, paymentMethodId!, ct);
        var subtotal = order.Total();
        var (_, fee) = await QuoteAsync(subtotal, saved.StaxPaymentMethodId, ct);
        return new CheckoutQuote(orderId, saved.StaxPaymentMethodId, saved.Method, subtotal, fee, subtotal + fee);
    }

    // ------------------------------------------------------------------ paying

    public async Task<PayResult> PayAsync(Shopper shopper, int orderId, string? paymentMethodId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(paymentMethodId))
            throw PaymentRequestException.BadRequest("paymentMethodId is required.");

        using var _ = await _locks.LockAsync(orderId, ct);
        var order = await LoadOwnOrderAsync(shopper, orderId, ct);
        var payment = await _db.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
        EnsureOrderPayable(order, payment);
        var saved = await _methods.GetActiveAsync(shopper, paymentMethodId!, ct);

        var subtotal = order.Total();
        var (_, fee) = await QuoteAsync(subtotal, saved.StaxPaymentMethodId, ct);
        var total = subtotal + fee;

        if (payment is null)
        {
            payment = new OrderPayment { OrderId = orderId, BuyerId = shopper.BuyerId };
            _db.OrderPayments.Add(payment);
        }
        payment.StaxPaymentMethodId = saved.StaxPaymentMethodId;
        payment.Method = saved.Method;
        payment.Subtotal = subtotal;
        payment.Fee = fee;
        payment.Total = total;
        payment.Status = PaymentStatuses.Processing;
        payment.FailureReason = null;
        payment.StaxTransactionId = null;
        await _db.SaveChangesAsync(ct);

        JsonElement txn;
        try
        {
            txn = await _stax.ChargeAsync(new Dictionary<string, object?>
            {
                ["payment_method_id"] = saved.StaxPaymentMethodId,
                ["total"] = total,
                ["pre_auth"] = false,
                ["meta"] = new Dictionary<string, object?>
                {
                    ["orderId"] = orderId.ToString(),
                    ["subtotal"] = subtotal,
                    ["fee"] = fee
                }
            }, ct);
        }
        catch (StaxApiException ex)
        {
            payment.Status = PaymentStatuses.Failed;
            payment.FailureReason = Truncate(ex.Message);
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        payment.StaxTransactionId = txn.Str("id");
        if (!txn.Bool("success"))
        {
            payment.Status = PaymentStatuses.Failed;
            payment.FailureReason = Truncate(txn.Str("message") ?? txn.Prop("response")?.Str("message") ?? "The payment was declined.");
            await _db.SaveChangesAsync(ct);
            throw new PaymentRequestException(System.Net.HttpStatusCode.PaymentRequired, $"The payment was declined: {payment.FailureReason}");
        }

        ApplyTransaction(payment, txn);
        await _db.SaveChangesAsync(ct);
        return new PayResult(orderId, IsPaid(payment), payment.Status, subtotal, fee, total, payment.StaxTransactionId, payment.Method);
    }

    private static string Truncate(string s) => s.Length > 900 ? s[..900] : s;

    /// <summary>
    /// A card payment has gone through when Stax approves it. A bank payment has gone through once the bank settles it.
    /// </summary>
    private static void ApplyTransaction(OrderPayment payment, JsonElement txn)
    {
        var status = txn.Str("status");
        if (!txn.Bool("success") || string.Equals(status, "FAILED", StringComparison.OrdinalIgnoreCase))
        {
            payment.Status = PaymentStatuses.Failed;
            payment.FailureReason ??= Truncate(txn.Str("message") ?? "The payment did not go through.");
            return;
        }

        var settledAt = txn.Time("settled_at");
        if (payment.Method == "bank" && settledAt is null)
        {
            payment.Status = PaymentStatuses.Pending;
            return;
        }
        payment.Status = PaymentStatuses.Paid;
        payment.PaidAt ??= settledAt ?? DateTimeOffset.UtcNow;
    }

    // ------------------------------------------------------------------ keeping payments up to date

    /// <summary>
    /// Brings an order's payment up to date with Stax: a bank payment that has settled becomes paid, and a refund that
    /// could not be made yet is attempted again. Called when an order is read and by the background worker.
    /// </summary>
    public async Task<OrderPayment?> RefreshAsync(int orderId, CancellationToken ct)
    {
        using var _ = await _locks.LockAsync(orderId, ct);
        var payment = await _db.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
        if (payment is null) return null;
        try
        {
            if (payment.Status == PaymentStatuses.Pending && payment.StaxTransactionId is not null)
            {
                var txn = await _stax.GetTransactionAsync(payment.StaxTransactionId, ct);
                ApplyTransaction(payment, txn);
                await _db.SaveChangesAsync(ct);
            }
            if (payment.RefundStatus == RefundStatuses.Pending)
            {
                await AttemptRefundAsync(payment, ct);
            }
        }
        catch (StaxApiException)
        {
            // Stax is unavailable right now: show what we know; the worker tries again
        }
        return payment;
    }

    /// <summary>Ids of the orders that still wait for Stax (a settlement or a refund).</summary>
    public async Task<List<int>> OpenOrderIdsAsync(CancellationToken ct) =>
        await _db.OrderPayments.AsNoTracking()
            .Where(p => p.Status == PaymentStatuses.Pending || p.RefundStatus == RefundStatuses.Pending)
            .Select(p => p.OrderId)
            .ToListAsync(ct);

    // ------------------------------------------------------------------ refunds

    public async Task<RefundResult> RefundAsync(string staffId, int orderId, CancellationToken ct)
    {
        using var _ = await _locks.LockAsync(orderId, ct);
        await LoadOrderAsync(orderId, ct);
        var payment = await _db.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
        if (payment is null || payment.StaxTransactionId is null ||
            payment.Status is PaymentStatuses.Failed or PaymentStatuses.Processing)
            throw PaymentRequestException.Conflict("This order has no payment to refund.");
        if (payment.RefundStatus == RefundStatuses.Refunded)
            throw PaymentRequestException.Conflict("This order has already been refunded.");
        if (payment.RefundStatus == RefundStatuses.Pending)
            throw PaymentRequestException.Conflict("A refund for this order is already in progress.");

        payment.RefundStatus = RefundStatuses.Pending;
        payment.RefundAmount = payment.Total; // the whole amount, the card fee included
        payment.RefundRequestedBy = staffId;
        payment.RefundRequestedAt = DateTimeOffset.UtcNow;
        payment.RefundMessage = null;
        payment.RefundAttempts = 0;
        await _db.SaveChangesAsync(ct);

        try
        {
            await AttemptRefundAsync(payment, ct, throwOnRejection: true);
        }
        catch (StaxApiException)
        {
            // Stax refused outright (or is down): nothing was requested after all
            payment.RefundStatus = RefundStatuses.None;
            payment.RefundAmount = null;
            payment.RefundRequestedAt = null;
            payment.RefundRequestedBy = null;
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        return new RefundResult(orderId, payment.RefundStatus, payment.RefundAmount ?? payment.Total, payment.RefundMessage);
    }

    private static bool IsFullyReturned(JsonElement txn, decimal total) =>
        txn.Bool("is_voided") || (txn.Dec("total_refunded") ?? 0m) >= total;

    /// <summary>
    /// Asks Stax to return the money. A bank payment cannot be returned until Stax allows it (some business days after
    /// the payment); the refund then stays pending and the worker keeps trying until it goes through.
    /// </summary>
    private async Task AttemptRefundAsync(OrderPayment payment, CancellationToken ct, bool throwOnRejection = false)
    {
        var total = payment.RefundAmount ?? payment.Total;
        payment.RefundAttempts++;
        try
        {
            var txn = await _stax.GetTransactionAsync(payment.StaxTransactionId!, ct);
            if (!IsFullyReturned(txn, total))
            {
                try
                {
                    await _stax.VoidOrRefundAsync(payment.StaxTransactionId!, total, ct);
                }
                catch (StaxApiException ex) when (ex.StaxStatus is >= 400 and < 500 and not 401 and not 403 and not 429)
                {
                    var after = await _stax.GetTransactionAsync(payment.StaxTransactionId!, ct);
                    if (!IsFullyReturned(after, total))
                    {
                        if (IsNotYetAvailable(ex))
                        {
                            payment.RefundMessage = Truncate("Waiting for the bank: " + ex.Reason);
                            await _db.SaveChangesAsync(ct);
                            return; // stays pending
                        }
                        if (throwOnRejection) throw;
                        payment.RefundStatus = RefundStatuses.Failed;
                        payment.RefundMessage = Truncate(ex.Reason);
                        await _db.SaveChangesAsync(ct);
                        return;
                    }
                }
                txn = await _stax.GetTransactionAsync(payment.StaxTransactionId!, ct);
            }

            if (IsFullyReturned(txn, total))
            {
                payment.RefundStatus = RefundStatuses.Refunded;
                payment.RefundedAt = DateTimeOffset.UtcNow;
                payment.RefundMessage = null;
            }
            await _db.SaveChangesAsync(ct);
        }
        catch (StaxApiException) when (!throwOnRejection)
        {
            // transient: stays pending for the next try
            await _db.SaveChangesAsync(ct);
        }
    }

    private static bool IsNotYetAvailable(StaxApiException ex)
    {
        var text = ex.Message;
        return text.Contains("unavailable until", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("business day", StringComparison.OrdinalIgnoreCase);
    }
}
