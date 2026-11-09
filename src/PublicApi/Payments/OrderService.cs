using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StaxFattMerchantApi.Models;
using StaxFattMerchantApi.Requests.Charge;
using StaxFattMerchantApi.Requests.Transaction;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>Settings of the shop's own checkout policy (section <c>Payments:</c>).</summary>
public class PaymentSettings
{
    /// <summary>Card processing fee passed on to the shopper: this percentage of the order subtotal...</summary>
    public decimal CardFeePercent { get; set; } = 2.9m;
    /// <summary>...plus this fixed amount. Bank payments carry no fee.</summary>
    public decimal CardFeeFixed { get; set; } = 0.30m;
    public int ReconcileSeconds { get; set; } = 30;
    /// <summary>How long to wait before asking Stax again about a refund it could not carry out yet.</summary>
    public int RefundRetrySeconds { get; set; } = 600;
}

public record OrderItemRequest(int CatalogItemId, int Quantity);
public record CreateOrderRequest(List<OrderItemRequest>? Items);
public record PaymentMethodChoice(string? PaymentMethodId);

public record CheckoutDto(Guid OrderId, string PaymentMethodId, string PaymentMethodType, decimal Subtotal, decimal Fee, decimal Total, string Currency);

public record OrderLineDto(int CatalogItemId, string Name, decimal UnitPrice, int Quantity);

public record OrderDto(Guid Id, DateTime CreatedAt, List<OrderLineDto> Items, decimal Subtotal, decimal? Fee, decimal? Total,
    string Status, bool Paid, string? PaymentMethodId, string? PaymentMethodType, DateTime? PaidAt,
    string RefundStatus);

public record StaffOrderDto(Guid Id, string BuyerEmail, DateTime CreatedAt, List<OrderLineDto> Items, decimal Subtotal, decimal? Fee, decimal? Total,
    string Status, bool Paid, string? PaymentMethodId, string? PaymentMethodType, DateTime? PaidAt, string? PaymentMessage,
    string? StaxTransactionId, string RefundStatus, decimal? RefundAmount, string? RefundTransactionId, string? RefundMessage,
    DateTime? RefundRequestedAt, DateTime? RefundCompletedAt);

public record RefundDto(Guid OrderId, string RefundStatus, decimal Amount, string? Message, string? RefundTransactionId, DateTime? RequestedAt, DateTime? CompletedAt);

public sealed class OrderService
{
    private readonly PaymentsContext _db;
    private readonly StaxGateway _stax;
    private readonly ShopperService _shoppers;
    private readonly IReadRepository<CatalogItem> _catalog;
    private readonly PaymentSettings _settings;
    private readonly ILogger<OrderService> _logger;

    public OrderService(PaymentsContext db, StaxGateway stax, ShopperService shoppers, IReadRepository<CatalogItem> catalog,
        IOptions<PaymentSettings> settings, ILogger<OrderService> logger)
    {
        _db = db;
        _stax = stax;
        _shoppers = shoppers;
        _catalog = catalog;
        _settings = settings.Value;
        _logger = logger;
    }

    // ------------------------------------------------------------------ orders

    public async Task<OrderDto> CreateAsync(string email, CreateOrderRequest req)
    {
        if (req.Items == null || req.Items.Count == 0) throw PaymentException.BadRequest("An order needs at least one item.");
        var order = new ShopOrder { Id = Guid.NewGuid(), BuyerEmail = ShopperService.Normalize(email), CreatedAt = DateTime.UtcNow };
        foreach (var line in req.Items)
        {
            if (line == null || line.Quantity < 1 || line.Quantity > 1000) throw PaymentException.BadRequest("Each item needs a quantity between 1 and 1000.");
            var item = await _catalog.GetByIdAsync(line.CatalogItemId)
                       ?? throw PaymentException.NotFound($"Catalog item {line.CatalogItemId} does not exist.");
            order.Lines.Add(new ShopOrderLine { CatalogItemId = item.Id, Name = item.Name, UnitPrice = item.Price, Quantity = line.Quantity });
        }
        order.Subtotal = order.Lines.Sum(l => l.UnitPrice * l.Quantity);
        if (order.Subtotal <= 0) throw PaymentException.Unprocessable("The order total must be greater than zero.");
        _db.ShopOrders.Add(order);
        await _db.SaveChangesAsync();
        return ToDto(order);
    }

    private async Task<ShopOrder> LoadAsync(Guid id, string? ownerEmail)
    {
        var order = await _db.ShopOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id);
        if (order == null || ownerEmail != null && order.BuyerEmail != ShopperService.Normalize(ownerEmail))
        {
            throw PaymentException.NotFound("No such order.");
        }
        return order;
    }

    public async Task<OrderDto> GetAsync(string email, Guid id)
    {
        var order = await LoadAsync(id, email);
        await RefreshAsync(order);
        return ToDto(order);
    }

    public async Task<StaffOrderDto> StaffGetAsync(Guid id)
    {
        var order = await LoadAsync(id, null);
        await RefreshAsync(order);
        return ToStaffDto(order);
    }

    // ------------------------------------------------------------------ checkout and pay

    public decimal FeeFor(string methodType, decimal subtotal) =>
        methodType == "card"
            ? Math.Round(subtotal * _settings.CardFeePercent / 100m + _settings.CardFeeFixed, 2, MidpointRounding.AwayFromZero)
            : 0m;

    public async Task<CheckoutDto> CheckoutAsync(string email, Guid orderId, PaymentMethodChoice choice)
    {
        var order = await LoadAsync(orderId, email);
        var method = await _shoppers.GetUsableMethodAsync(email, choice.PaymentMethodId);
        EnsurePayable(order);
        var fee = FeeFor(method.Type, order.Subtotal);
        return new CheckoutDto(order.Id, method.Id, method.Type, order.Subtotal, fee, order.Subtotal + fee, "USD");
    }

    private static void EnsurePayable(ShopOrder order)
    {
        if (order.PaymentStatus == PaymentStatuses.Paid) throw PaymentException.Conflict("The order is already paid.");
        if (order.PaymentStatus == PaymentStatuses.Processing) throw PaymentException.Conflict("The order's payment is already being processed.");
        if (order.PaymentStatus == PaymentStatuses.Voided || order.RefundStatus != RefundStatuses.None && order.RefundStatus != RefundStatuses.Failed)
            throw PaymentException.Conflict("The order has been refunded and can't be paid.");
    }

    public async Task<OrderDto> PayAsync(string email, Guid orderId, PaymentMethodChoice choice)
    {
        var gate = ShopperService.LockFor("order:" + orderId);
        await gate.WaitAsync();
        try
        {
            var order = await LoadAsync(orderId, email);
            var method = await _shoppers.GetUsableMethodAsync(email, choice.PaymentMethodId);
            EnsurePayable(order);

            var fee = FeeFor(method.Type, order.Subtotal);
            var total = order.Subtotal + fee;

            JsonElement charge;
            try
            {
                charge = Js.El(await _stax.Call(c => c.Charge.PostCharge(new PostChargeRequest
                {
                    Body = new
                    {
                        payment_method_id = method.Id,
                        total,
                        pre_auth = false,
                        meta = new { reference = order.Id.ToString(), subtotal = order.Subtotal, fee, source = "eShopOnWeb" }
                    }
                })));
            }
            catch (PaymentException ex) when (ex.StatusCode is 422)
            {
                order.PaymentStatus = PaymentStatuses.Failed;
                order.PaymentMessage = ex.Message;
                await _db.SaveChangesAsync();
                throw new PaymentException(402, "The payment was declined. " + ex.Message, ex);
            }

            order.PaymentMethodId = method.Id;
            order.PaymentMethodType = method.Type;
            order.Fee = fee;
            order.Total = total;
            order.StaxTransactionId = charge.Str("id");

            if (charge.Bool("success") != true)
            {
                order.PaymentStatus = PaymentStatuses.Failed;
                order.PaymentMessage = charge.Str("message") ?? charge.Str("error_description") ?? "declined";
                await _db.SaveChangesAsync();
                throw new PaymentException(402, "The payment was declined: " + order.PaymentMessage);
            }

            if (method.Type == "bank" && charge.Str("settled_at") == null)
            {
                // a bank payment goes through when the bank settles it
                order.PaymentStatus = PaymentStatuses.Processing;
            }
            else
            {
                order.PaymentStatus = PaymentStatuses.Paid;
                order.PaidAt = DateTime.UtcNow;
            }
            order.PaymentMessage = null;
            await _db.SaveChangesAsync();
            return ToDto(order);
        }
        finally
        {
            gate.Release();
        }
    }

    // ------------------------------------------------------------------ refunds

    public async Task<RefundDto> RefundAsync(Guid orderId)
    {
        var gate = ShopperService.LockFor("order:" + orderId);
        await gate.WaitAsync();
        try
        {
            var order = await LoadAsync(orderId, null);
            await RefreshCoreAsync(order);
            if (order.StaxTransactionId == null || order.PaymentStatus is not (PaymentStatuses.Paid or PaymentStatuses.Processing))
                throw PaymentException.Conflict("The order has no payment to refund.");
            if (order.RefundStatus == RefundStatuses.Completed) throw PaymentException.Conflict("The order has already been refunded.");
            if (order.RefundStatus == RefundStatuses.Pending) throw PaymentException.Conflict("A refund for this order is already in progress.");

            order.RefundStatus = RefundStatuses.Pending;
            order.RefundRequestedAt = DateTime.UtcNow;
            order.RefundAmount = order.Total;
            order.RefundMessage = null;
            order.RefundTransactionId = null;
            await _db.SaveChangesAsync();

            await AttemptRefundAsync(order);
            await _db.SaveChangesAsync();
            return ToRefundDto(order);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Gets the whole paid amount (items plus fee) back to the shopper: voids the payment while Stax still allows it,
    /// otherwise refunds it. When neither is possible yet the refund stays pending and is retried by the reconciler.
    /// </summary>
    private async Task AttemptRefundAsync(ShopOrder order)
    {
        order.RefundLastAttemptAt = DateTime.UtcNow;
        try
        {
            var txn = await GetTransactionAsync(order.StaxTransactionId!);
            var total = txn.Dec("total") ?? order.Total ?? 0m;
            var refunded = txn.Dec("total_refunded") ?? 0m;

            if (txn.Bool("is_voided") == true)
            {
                Complete(order, null, "The payment was voided.");
            }
            else if (txn.Bool("is_voidable") == true)
            {
                var voided = Js.El(await _stax.Call(c => c.Transaction.PostVoid(new PostVoidRequest { Id = Guid.Parse(order.StaxTransactionId!) })));
                if (voided.Bool("success") == false)
                {
                    order.RefundMessage = voided.Str("message") ?? "Stax could not void the payment yet.";
                }
                else
                {
                    Complete(order, voided.Str("id"), "The payment was voided.");
                }
            }
            else if (txn.Bool("is_refundable") == true || refunded > 0 && total - refunded > 0)
            {
                var remaining = total - refunded;
                var refund = Js.El(await _stax.Call(c => c.Transaction.PostRefund(new PostRefundRequest
                {
                    Id = Guid.Parse(order.StaxTransactionId!),
                    Body = new PostRefundRequestBodyJson { Total = (double)remaining }
                })));
                order.RefundTransactionId = refund.Str("id");
                if (refund.Bool("success") == false)
                {
                    order.RefundMessage = refund.Str("message") ?? "Stax could not refund the payment yet.";
                }
                else if (order.PaymentMethodType == "bank" && refund.Str("settled_at") == null)
                {
                    order.RefundMessage = "The refund has been sent to the bank and completes when the bank settles it.";
                }
                else
                {
                    Complete(order, order.RefundTransactionId, "The payment was refunded.");
                }
            }
            else if (total - refunded <= 0)
            {
                Complete(order, null, "The payment was already refunded.");
            }
            else
            {
                order.RefundMessage = "The payment can't be refunded yet; the refund will be issued as soon as Stax allows it.";
            }
        }
        catch (PaymentException ex) when (ex.StatusCode >= 500)
        {
            order.RefundMessage = ex.Message; // transient: stays pending and is retried
            _logger.LogWarning(ex, "Refund of order {Order} is still pending", order.Id);
        }
        catch (PaymentException ex) when (ex.StatusCode == 422 && IsNotYetAllowed(ex.Message))
        {
            // e.g. "ACH voids and refunds are unavailable until 5 business day(s) after transaction":
            // the refund stays pending and is carried out later without staff doing anything
            order.RefundMessage = ex.Message;
        }
        catch (PaymentException ex)
        {
            order.RefundStatus = RefundStatuses.Failed;
            order.RefundMessage = ex.Message;
        }
    }

    private static bool IsNotYetAllowed(string message) =>
        message.Contains("unavailable until", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("business day", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("not yet", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("settle", StringComparison.OrdinalIgnoreCase);

    private void Complete(ShopOrder order, string? refundTxnId, string message)
    {
        order.RefundStatus = RefundStatuses.Completed;
        order.RefundTransactionId = refundTxnId ?? order.RefundTransactionId;
        order.RefundMessage = message;
        order.RefundCompletedAt = DateTime.UtcNow;
        if (order.PaymentStatus == PaymentStatuses.Processing) order.PaymentStatus = PaymentStatuses.Voided; // never settled
    }

    // ------------------------------------------------------------------ keeping orders in step with Stax

    private async Task<JsonElement> GetTransactionAsync(string id) =>
        Js.El(await _stax.Call(c => c.Transaction.GetTransaction(new GetTransactionRequest { Id = Guid.Parse(id) })));

    /// <summary>Brings the order's payment and refund state up to date with Stax (takes the order lock).</summary>
    private async Task RefreshAsync(ShopOrder order)
    {
        if (order.PaymentStatus != PaymentStatuses.Processing && order.RefundStatus != RefundStatuses.Pending) return;
        var gate = ShopperService.LockFor("order:" + order.Id);
        await gate.WaitAsync();
        try
        {
            await _db.Entry(order).ReloadAsync();
            await RefreshCoreAsync(order);
            await _db.SaveChangesAsync();
        }
        catch (PaymentException ex) when (ex.StatusCode >= 500)
        {
            _logger.LogWarning(ex, "Could not refresh order {Order} from Stax", order.Id); // show what we know
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task RefreshCoreAsync(ShopOrder order)
    {
        if (order.PaymentStatus == PaymentStatuses.Processing && order.StaxTransactionId != null && order.RefundStatus != RefundStatuses.Pending)
        {
            var txn = await GetTransactionAsync(order.StaxTransactionId);
            if (txn.Bool("is_voided") == true)
            {
                order.PaymentStatus = PaymentStatuses.Voided;
            }
            else if (txn.Str("settled_at") != null)
            {
                order.PaymentStatus = PaymentStatuses.Paid;
                order.PaidAt = txn.Time("settled_at") ?? DateTime.UtcNow;
            }
            else if (txn.Bool("success") == false || txn.Str("status") is "FAILED" or "RETURNED" or "REJECTED" or "DECLINED")
            {
                order.PaymentStatus = PaymentStatuses.Failed;
                order.PaymentMessage = txn.Str("message") ?? txn.Str("error_description") ?? "The bank did not settle the payment.";
            }
        }

        if (order.RefundStatus == RefundStatuses.Pending)
        {
            if (order.RefundTransactionId == null)
            {
                var due = order.RefundLastAttemptAt == null ||
                          DateTime.UtcNow - order.RefundLastAttemptAt >= TimeSpan.FromSeconds(_settings.RefundRetrySeconds);
                if (due) await AttemptRefundAsync(order);
            }
            else
            {
                var refund = await GetTransactionAsync(order.RefundTransactionId);
                if (refund.Bool("success") == false)
                {
                    order.RefundStatus = RefundStatuses.Failed;
                    order.RefundMessage = refund.Str("message") ?? "The refund was rejected.";
                }
                else if (order.PaymentMethodType != "bank" || refund.Str("settled_at") != null)
                {
                    Complete(order, order.RefundTransactionId, "The payment was refunded.");
                }
            }
        }
    }

    /// <summary>Background pass: settle bank payments and push pending refunds through.</summary>
    public async Task ReconcileAsync()
    {
        var ids = await _db.ShopOrders
            .Where(o => o.PaymentStatus == PaymentStatuses.Processing || o.RefundStatus == RefundStatuses.Pending)
            .Select(o => o.Id).ToListAsync();
        foreach (var id in ids)
        {
            try
            {
                var order = await _db.ShopOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id);
                if (order != null) await RefreshAsync(order);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reconciling order {Order} failed", id);
            }
        }
    }

    // ------------------------------------------------------------------ mapping

    private static string StatusOf(ShopOrder o) => o.PaymentStatus;

    private static List<OrderLineDto> Lines(ShopOrder o) =>
        o.Lines.OrderBy(l => l.Id).Select(l => new OrderLineDto(l.CatalogItemId, l.Name, l.UnitPrice, l.Quantity)).ToList();

    private static OrderDto ToDto(ShopOrder o) =>
        new(o.Id, o.CreatedAt, Lines(o), o.Subtotal, o.Fee, o.Total, StatusOf(o), o.IsPaid, o.PaymentMethodId, o.PaymentMethodType, o.PaidAt, o.RefundStatus);

    private static StaffOrderDto ToStaffDto(ShopOrder o) =>
        new(o.Id, o.BuyerEmail, o.CreatedAt, Lines(o), o.Subtotal, o.Fee, o.Total, StatusOf(o), o.IsPaid, o.PaymentMethodId, o.PaymentMethodType,
            o.PaidAt, o.PaymentMessage, o.StaxTransactionId, o.RefundStatus, o.RefundAmount, o.RefundTransactionId, o.RefundMessage,
            o.RefundRequestedAt, o.RefundCompletedAt);

    private static RefundDto ToRefundDto(ShopOrder o) =>
        new(o.Id, o.RefundStatus, o.RefundAmount ?? o.Total ?? 0m, o.RefundMessage, o.RefundTransactionId, o.RefundRequestedAt, o.RefundCompletedAt);
}
