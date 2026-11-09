using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class PlaceOrderRequest
{
    public List<PlaceOrderLine>? Items { get; set; }
    public ShipToAddressDto? ShipToAddress { get; set; }
}

public class PlaceOrderLine
{
    public int CatalogItemId { get; set; }
    public int Units { get; set; }
}

public class ShipToAddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class PaymentMethodChoiceRequest
{
    public string? PaymentMethodId { get; set; }
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class OrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public List<OrderLineDto> Items { get; set; } = new();
    public decimal Amount { get; set; }
    public decimal Fee { get; set; }
    public decimal Total { get; set; }
    public string? PaymentMethodId { get; set; }
    public string? PaymentMethod { get; set; }
    /// <summary>True once the payment has gone through (for a bank payment: once the bank has settled it).</summary>
    public bool Paid { get; set; }
    /// <summary>unpaid, processing, pending (bank payment awaiting settlement), paid or failed.</summary>
    public string PaymentStatus { get; set; } = "unpaid";
    public DateTimeOffset? PaidAt { get; set; }
    public string? PaymentError { get; set; }
    /// <summary>none, pending, refunded, failed or not_required.</summary>
    public string RefundStatus { get; set; } = "none";
}

public class StaffOrderDto : OrderDto
{
    public string BuyerEmail { get; set; } = "";
    public string? StaxTransactionId { get; set; }
    public DateTimeOffset? RefundRequestedAt { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
    public string? RefundMessage { get; set; }
}

public class CheckoutDto
{
    public int OrderId { get; set; }
    public string PaymentMethodId { get; set; } = "";
    public string PaymentMethod { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal Fee { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = "USD";
}

public class RefundDto
{
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string RefundStatus { get; set; } = "none";
    public string? Message { get; set; }
}

public class OrderPaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> Locks = new();
    private static (DateTimeOffset At, decimal? Rate) _rateCache;

    private readonly PaymentsDbContext _db;
    private readonly CatalogContext _catalog;
    private readonly StaxGateway _stax;
    private readonly PaymentMethodService _methods;
    private readonly IUriComposer _uriComposer;
    private readonly PaymentsSettings _settings;
    private readonly ILogger<OrderPaymentService> _logger;

    public OrderPaymentService(PaymentsDbContext db, CatalogContext catalog, StaxGateway stax, PaymentMethodService methods,
        IUriComposer uriComposer, IOptions<PaymentsSettings> settings, ILogger<OrderPaymentService> logger)
    {
        _db = db;
        _catalog = catalog;
        _stax = stax;
        _methods = methods;
        _uriComposer = uriComposer;
        _settings = settings.Value;
        _logger = logger;
    }

    private static async Task<IDisposable> Lock(int orderId, CancellationToken ct)
    {
        var sem = Locks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        return new Releaser(sem);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _s;
        public Releaser(SemaphoreSlim s) => _s = s;
        public void Dispose() => _s.Release();
    }

    // ---- orders ---------------------------------------------------------------------------------------------------

    public async Task<OrderDto> Place(string email, PlaceOrderRequest request, CancellationToken ct = default)
    {
        var lines = request.Items ?? new();
        if (lines.Count == 0) throw new PaymentApiException(400, "An order needs at least one item.");
        if (lines.Any(l => l.Units < 1 || l.Units > 1000)) throw new PaymentApiException(400, "Each item needs between 1 and 1000 units.");
        if (lines.Any(l => l.CatalogItemId < 1)) throw new PaymentApiException(400, "Each item needs a catalogItemId.");

        var wanted = lines.GroupBy(l => l.CatalogItemId).ToDictionary(g => g.Key, g => g.Sum(l => l.Units));
        var ids = wanted.Keys.ToList();
        var catalogItems = await _catalog.CatalogItems.Where(i => ids.Contains(i.Id)).ToListAsync(ct);
        var missing = ids.Except(catalogItems.Select(i => i.Id)).ToList();
        if (missing.Count > 0) throw new PaymentApiException(404, $"Catalog item(s) not found: {string.Join(", ", missing)}.");

        var items = catalogItems.OrderBy(i => i.Id).Select(i => new OrderItem(
            new CatalogItemOrdered(i.Id, i.Name, _uriComposer.ComposePicUri(i.PictureUri)), i.Price, wanted[i.Id])).ToList();
        var a = request.ShipToAddress;
        var address = new Address(a?.Street ?? "", a?.City ?? "", a?.State ?? "", a?.Country ?? "", a?.ZipCode ?? "");
        var order = new Order(email, address, items);
        if (order.Total() <= 0) throw new PaymentApiException(422, "An order with no cost cannot be paid for.");

        _catalog.Orders.Add(order);
        await _catalog.SaveChangesAsync(ct);

        var op = new OrderPayment { OrderId = order.Id, BuyerEmail = email, Amount = order.Total(), Total = order.Total() };
        _db.OrderPayments.Add(op);
        await _db.SaveChangesAsync(ct);
        return ToDto(order, op, new OrderDto());
    }

    public async Task<OrderDto> GetForShopper(string email, int orderId, CancellationToken ct = default)
    {
        var op = await Load(orderId, ct);
        if (op == null || op.BuyerEmail != email) throw new PaymentApiException(404, "Order not found.");
        await Refresh(op, ct);
        var order = await LoadOrder(orderId, ct);
        return ToDto(order, op, new OrderDto());
    }

    public async Task<StaffOrderDto> GetForStaff(int orderId, CancellationToken ct = default)
    {
        var op = await Load(orderId, ct) ?? throw new PaymentApiException(404, "Order not found.");
        await Refresh(op, ct);
        var order = await LoadOrder(orderId, ct);
        var dto = ToDto(order, op, new StaffOrderDto());
        dto.BuyerEmail = op.BuyerEmail;
        dto.StaxTransactionId = op.StaxTransactionId;
        dto.RefundRequestedAt = op.RefundRequestedAt;
        dto.RefundedAt = op.RefundedAt;
        dto.RefundMessage = op.RefundMessage;
        return dto;
    }

    private Task<OrderPayment?> Load(int orderId, CancellationToken ct) =>
        _db.OrderPayments.FirstOrDefaultAsync(o => o.OrderId == orderId, ct);

    private async Task<Order> LoadOrder(int orderId, CancellationToken ct) =>
        await _catalog.Orders.Include(o => o.OrderItems).FirstOrDefaultAsync(o => o.Id == orderId, ct)
        ?? throw new PaymentApiException(404, "Order not found.");

    private static T ToDto<T>(Order order, OrderPayment op, T dto) where T : OrderDto
    {
        dto.OrderId = order.Id;
        dto.OrderDate = order.OrderDate;
        dto.Items = order.OrderItems.Select(i => new OrderLineDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units,
        }).ToList();
        dto.Amount = op.Amount;
        dto.Fee = op.Fee;
        dto.Total = op.Total;
        dto.PaymentMethodId = op.PaymentMethodId;
        dto.PaymentMethod = op.Method;
        dto.Paid = op.State == PaymentState.Paid;
        dto.PaymentStatus = op.State switch
        {
            PaymentState.Unpaid => "unpaid",
            PaymentState.Processing => "processing",
            PaymentState.PendingSettlement => "pending",
            PaymentState.Paid => "paid",
            _ => "failed",
        };
        dto.PaidAt = op.PaidAt;
        dto.PaymentError = op.LastError;
        dto.RefundStatus = op.RefundState switch
        {
            RefundState.None => "none",
            RefundState.Pending => "pending",
            RefundState.Refunded => "refunded",
            RefundState.Failed => "failed",
            _ => "not_required",
        };
        return dto;
    }

    // ---- fees -----------------------------------------------------------------------------------------------------

    private async Task<decimal> CardFeePercent(CancellationToken ct)
    {
        if (DateTimeOffset.UtcNow - _rateCache.At < TimeSpan.FromMinutes(10) && _rateCache.At != default)
            return _rateCache.Rate ?? _settings.CardFeePercent;
        decimal? fromStax = null;
        try { fromStax = await _stax.GetCardSurchargePercent(ct); }
        catch (StaxException ex) { _logger.LogWarning("Could not read the merchant surcharge rate from Stax: {Message}", ex.Message); }
        _rateCache = (DateTimeOffset.UtcNow, fromStax);
        return fromStax ?? _settings.CardFeePercent;
    }

    private async Task<(decimal Fee, decimal Total)> Quote(decimal amount, StaxPaymentMethod pm, CancellationToken ct)
    {
        // Card networks do not allow surcharging debit or prepaid cards, and bank payments carry no card fee.
        var surchargeable = pm.IsCard && !pm.Prepaid && !string.Equals(pm.BinType, "DEBIT", StringComparison.OrdinalIgnoreCase);
        if (!surchargeable) return (0m, amount);
        var fee = Math.Round(amount * await CardFeePercent(ct) / 100m, 2, MidpointRounding.AwayFromZero);
        return (fee, amount + fee);
    }

    // ---- checkout & pay -------------------------------------------------------------------------------------------

    private async Task<OrderPayment> LoadOwned(string email, int orderId, CancellationToken ct)
    {
        var op = await Load(orderId, ct);
        if (op == null || op.BuyerEmail != email) throw new PaymentApiException(404, "Order not found.");
        return op;
    }

    public async Task<CheckoutDto> Checkout(string email, int orderId, string? paymentMethodId, CancellationToken ct = default)
    {
        var op = await LoadOwned(email, orderId, ct);
        if (op.State is PaymentState.Paid or PaymentState.PendingSettlement) throw new PaymentApiException(409, "The order has already been paid.");
        var pm = await _methods.GetOwnedActive(email, paymentMethodId ?? "", ct);
        var (fee, total) = await Quote(op.Amount, pm, ct);
        return new CheckoutDto
        {
            OrderId = orderId, PaymentMethodId = pm.Id, PaymentMethod = pm.Method, Amount = op.Amount, Fee = fee, Total = total,
        };
    }

    public async Task<OrderDto> Pay(string email, int orderId, string? paymentMethodId, CancellationToken ct = default)
    {
        using var _ = await Lock(orderId, ct);
        _db.ChangeTracker.Clear();
        var op = await LoadOwned(email, orderId, ct);
        if (op.State is PaymentState.Paid or PaymentState.PendingSettlement) throw new PaymentApiException(409, "The order has already been paid.");
        if (op.RefundState is RefundState.Pending or RefundState.Refunded) throw new PaymentApiException(409, "The order has been refunded.");

        var pm = await _methods.GetOwnedActive(email, paymentMethodId ?? "", ct);
        if (op.State == PaymentState.Processing && !string.Equals(op.PaymentMethodId, pm.Id, StringComparison.OrdinalIgnoreCase))
            throw new PaymentApiException(409, "A payment for this order is already in progress with another payment method.");

        var (fee, total) = await Quote(op.Amount, pm, ct);
        if (op.State == PaymentState.Processing && op.Total != total)
            throw new PaymentApiException(409, "The payment in progress was started for a different amount.");

        // An attempt whose outcome is unknown is retried under the same key, so Stax can never charge twice for it.
        op.AttemptKey ??= $"eshop-order-{orderId}-{Guid.NewGuid():N}";
        op.State = PaymentState.Processing;
        op.PaymentMethodId = pm.Id;
        op.Method = pm.Method;
        op.Fee = fee;
        op.Total = total;
        op.LastError = null;
        await _db.SaveChangesAsync(ct);

        StaxTransaction tx;
        try
        {
            tx = await _stax.Charge(pm.Id, total, new Dictionary<string, object?> { ["orderId"] = orderId, ["amount"] = op.Amount, ["fee"] = fee }, op.AttemptKey, ct);
        }
        catch (StaxException ex) when (!ex.IsTransient)
        {
            op.State = PaymentState.Failed;
            op.AttemptKey = null;
            op.LastError = ex.Message;
            await _db.SaveChangesAsync(ct);
            throw new PaymentApiException(402, $"The payment was not accepted: {ex.Message}");
        }
        catch (StaxException ex)
        {
            // outcome unknown: leave the order "processing"; paying again resumes the same attempt
            throw new PaymentApiException(503, $"Stax could not be reached ({ex.Message}). Nothing is lost: try paying again to resume this payment.");
        }

        op.StaxTransactionId = tx.Id;
        if (!tx.Success)
        {
            op.State = PaymentState.Failed;
            op.AttemptKey = null;
            op.LastError = tx.Message ?? "The payment was declined.";
            await _db.SaveChangesAsync(ct);
            throw new PaymentApiException(402, $"The payment was declined: {op.LastError}");
        }

        if (pm.IsCard || tx.SettledAt != null)
        {
            op.State = PaymentState.Paid;
            op.PaidAt = DateTimeOffset.UtcNow;
        }
        else
        {
            op.State = PaymentState.PendingSettlement; // bank payment: goes through when the bank settles it
        }
        await _db.SaveChangesAsync(ct);
        return ToDto(await LoadOrder(orderId, ct), op, new OrderDto());
    }

    // ---- reconciliation with Stax ---------------------------------------------------------------------------------

    /// <summary>Brings a pending bank payment and a pending refund up to date with Stax.</summary>
    public async Task Refresh(OrderPayment op, CancellationToken ct = default, bool submitRefund = false)
    {
        if (op.State != PaymentState.PendingSettlement && op.RefundState != RefundState.Pending) return;
        using var _ = await Lock(op.OrderId, ct);
        await _db.Entry(op).ReloadAsync(ct);
        try
        {
            await SyncPayment(op, ct);
            if (op.RefundState == RefundState.Pending) await AttemptRefund(op, ct, submitRefund);
        }
        catch (StaxException ex)
        {
            _logger.LogWarning("Order {OrderId}: could not reconcile with Stax: {Message}", op.OrderId, ex.Message);
        }
        await _db.SaveChangesAsync(ct);
    }

    private async Task SyncPayment(OrderPayment op, CancellationToken ct)
    {
        if (op.State != PaymentState.PendingSettlement || op.StaxTransactionId == null) return;
        var tx = await _stax.GetTransaction(op.StaxTransactionId, ct);
        if (!tx.Success || (tx.IsVoided && tx.TotalRefunded < tx.Total))
        {
            op.State = PaymentState.Failed;
            op.LastError = tx.Message ?? "The bank payment did not go through.";
            op.AttemptKey = null;
            if (op.RefundState == RefundState.Pending) op.RefundState = RefundState.NotRequired;
        }
        else if (tx.SettledAt != null)
        {
            op.State = PaymentState.Paid;
            op.PaidAt = tx.SettledAt;
        }
    }

    public async Task<List<int>> PendingOrderIds(CancellationToken ct) =>
        await _db.OrderPayments
            .Where(o => o.State == PaymentState.PendingSettlement || o.RefundState == RefundState.Pending)
            .Select(o => o.OrderId).ToListAsync(ct);

    public async Task Reconcile(int orderId, CancellationToken ct)
    {
        var op = await Load(orderId, ct);
        if (op != null) await Refresh(op, ct, submitRefund: true);
    }

    // ---- refunds --------------------------------------------------------------------------------------------------

    public async Task<RefundDto> Refund(int orderId, CancellationToken ct = default)
    {
        using var _ = await Lock(orderId, ct);
        _db.ChangeTracker.Clear();
        var op = await Load(orderId, ct) ?? throw new PaymentApiException(404, "Order not found.");
        if (op.RefundState is RefundState.Pending or RefundState.Refunded)
            throw new PaymentApiException(409, op.RefundState == RefundState.Refunded ? "The order has already been refunded." : "A refund for this order is already in progress.");
        if (op.StaxTransactionId == null || op.State is PaymentState.Unpaid or PaymentState.Processing or PaymentState.Failed)
            throw new PaymentApiException(409, "The order has no completed payment to refund.");

        op.RefundState = RefundState.Pending;
        op.RefundRequestedAt = DateTimeOffset.UtcNow;
        op.RefundMessage = null;
        await _db.SaveChangesAsync(ct);

        try
        {
            await SyncPayment(op, ct);
            if (op.RefundState == RefundState.Pending) await AttemptRefund(op, ct, submit: true);
        }
        catch (StaxException ex) when (ex.IsTransient)
        {
            op.RefundMessage = $"Stax could not be reached: {ex.Message}. The refund will be retried automatically.";
        }
        await _db.SaveChangesAsync(ct);

        if (op.RefundState == RefundState.Failed)
            throw new PaymentApiException(422, $"The refund was rejected: {op.RefundMessage}");
        return new RefundDto
        {
            OrderId = orderId,
            Amount = op.Total,
            RefundStatus = op.RefundState switch { RefundState.Refunded => "refunded", RefundState.Pending => "pending", RefundState.NotRequired => "not_required", _ => "failed" },
            Message = op.RefundMessage,
        };
    }

    private async Task AttemptRefund(OrderPayment op, CancellationToken ct, bool submit)
    {
        var txId = op.StaxTransactionId!;
        var tx = await _stax.GetTransaction(txId, ct);
        if (IsFullyRefunded(tx)) { MarkRefunded(op); return; }
        if (!submit) return;

        try { await _stax.RefundInFull(txId, ct); }
        catch (StaxException ex) when (!ex.IsTransient)
        {
            if (ex.Message.Contains("unavailable until", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("similar request", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("currently being processed", StringComparison.OrdinalIgnoreCase))
            {
                // a bank payment cannot be refunded until it has had time to settle (or Stax is still busy with an
                // earlier request): keep trying on our own
                op.RefundMessage = ex.Message;
                return;
            }
            op.RefundState = RefundState.Failed;
            op.RefundMessage = ex.Message;
            return;
        }

        tx = await _stax.GetTransaction(txId, ct);
        if (IsFullyRefunded(tx)) MarkRefunded(op);
        else op.RefundMessage = "Refund submitted to Stax, waiting for it to complete.";
    }

    private static bool IsFullyRefunded(StaxTransaction tx) => tx.IsVoided || (tx.Total > 0 && tx.TotalRefunded >= tx.Total);

    private static void MarkRefunded(OrderPayment op)
    {
        op.RefundState = RefundState.Refunded;
        op.RefundedAt = DateTimeOffset.UtcNow;
        op.RefundMessage = null;
    }
}
