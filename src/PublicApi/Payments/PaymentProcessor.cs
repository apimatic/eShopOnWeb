using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Stax;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Payments;

// ---- requests and views ---------------------------------------------------------------------------

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

public class PlaceOrderRequest : BaseRequest
{
    public List<OrderLineRequest>? Items { get; set; }
    public ShipToAddressRequest? ShipToAddress { get; set; }
}

public class PayWithMethodRequest : BaseRequest
{
    public string? PaymentMethodId { get; set; }
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class OrderDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public ShipToAddressRequest? ShipToAddress { get; set; }
    public List<OrderLineDto> Items { get; set; } = new();

    /// <summary>The price of the items.</summary>
    public decimal Total { get; set; }

    /// <summary>Whether the order's payment has gone through (a bank payment only once the bank settled it).</summary>
    public bool Paid { get; set; }

    /// <summary><c>unpaid</c>, <c>processing</c>, <c>pending</c> (bank payment not settled yet), <c>paid</c> or <c>failed</c>.</summary>
    public string PaymentStatus { get; set; } = "unpaid";
    public string? PaymentMethodId { get; set; }
    public string? PaymentMethod { get; set; }
    public decimal? Fee { get; set; }
    public decimal? AmountCharged { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public string? PaymentMessage { get; set; }

    /// <summary><c>none</c>, <c>pending</c>, <c>refunded</c> or <c>failed</c>.</summary>
    public string RefundStatus { get; set; } = RefundStatuses.None;
    public decimal? RefundAmount { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
    public string? RefundMessage { get; set; }
    public string? TransactionId { get; set; }
}

public class CheckoutDto
{
    public int OrderId { get; set; }
    public string PaymentMethodId { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    /// <summary>The card processing fee passed on to the shopper.</summary>
    public decimal Fee { get; set; }
    /// <summary>What the shopper will pay.</summary>
    public decimal Total { get; set; }
}

public class PayDto
{
    public int OrderId { get; set; }
    public bool Paid { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal Fee { get; set; }
    public decimal Total { get; set; }
    public string? TransactionId { get; set; }
    public string? Message { get; set; }
}

public class RefundDto
{
    public int OrderId { get; set; }
    public string RefundStatus { get; set; } = string.Empty;
    public decimal? RefundAmount { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
    public string? Message { get; set; }
}

// ---- the service ----------------------------------------------------------------------------------

public class PaymentProcessor
{
    private static readonly TimeSpan RefundRetryDelay = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan StuckProcessing = TimeSpan.FromMinutes(15);
    private static readonly Regex NotYetRefundable = new(@"unavailable until|business day|similar request|currently being processed|not (yet )?(settled|refundable)|until .* settle",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly CatalogContext _db;
    private readonly StaxGateway _stax;
    private readonly PaymentMethodService _methods;
    private readonly KeyedLock _locks;
    private readonly ILogger<PaymentProcessor> _logger;

    public PaymentProcessor(CatalogContext db, StaxGateway stax, PaymentMethodService methods, KeyedLock locks,
        ILogger<PaymentProcessor> logger)
    {
        _db = db;
        _stax = stax;
        _methods = methods;
        _locks = locks;
        _logger = logger;
    }

    public static string OrderLockKey(int orderId) => "order:" + orderId;

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    // ---- orders ---------------------------------------------------------------------------------

    public async Task<OrderDto> PlaceOrderAsync(string buyerId, PlaceOrderRequest request, CancellationToken ct)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            throw ApiProblemException.BadRequest("An order needs at least one item.");
        }

        var wanted = new Dictionary<int, int>();
        foreach (var line in request.Items)
        {
            if (line.Quantity < 1 || line.Quantity > 1000)
            {
                throw ApiProblemException.BadRequest("Each quantity must be between 1 and 1000.");
            }

            wanted[line.CatalogItemId] = wanted.GetValueOrDefault(line.CatalogItemId) + line.Quantity;
        }

        var ids = wanted.Keys.ToList();
        var catalog = await _db.CatalogItems.Where(i => ids.Contains(i.Id)).ToListAsync(ct);
        var missing = ids.Where(id => catalog.All(c => c.Id != id)).ToList();
        if (missing.Count > 0)
        {
            throw ApiProblemException.Unprocessable("Unknown catalog item(s): " + string.Join(", ", missing) + ".");
        }

        var items = request.Items
            .Select(l => l.CatalogItemId).Distinct()
            .Select(id => catalog.First(c => c.Id == id))
            .Select(c => new OrderItem(
                new CatalogItemOrdered(c.Id, c.Name, string.IsNullOrEmpty(c.PictureUri) ? "none" : c.PictureUri),
                c.Price, wanted[c.Id]))
            .ToList();

        var a = request.ShipToAddress;
        var address = new Address(a?.Street ?? string.Empty, a?.City ?? string.Empty, a?.State ?? string.Empty,
            a?.Country ?? string.Empty, a?.ZipCode ?? string.Empty);
        var order = new Order(buyerId, address, items);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);
        return ToDto(order, null);
    }

    public async Task<Order> RequireOrderAsync(int orderId, string? ownerBuyerId, CancellationToken ct)
    {
        var order = await _db.Orders.Include(o => o.OrderItems).FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null || (ownerBuyerId is not null
                              && !string.Equals(order.BuyerId, ownerBuyerId, StringComparison.OrdinalIgnoreCase)))
        {
            throw ApiProblemException.NotFound("Order not found.");
        }

        return order;
    }

    /// <summary>The order as its owner or staff see it; a bank payment still pending is checked with Stax first.</summary>
    public async Task<OrderDto> GetOrderAsync(int orderId, string? ownerBuyerId, CancellationToken ct)
    {
        var order = await RequireOrderAsync(orderId, ownerBuyerId, ct);
        var payment = await _db.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
        if (payment is { Status: PaymentStatuses.Pending })
        {
            using var _ = await _locks.AcquireAsync(OrderLockKey(orderId));
            await _db.Entry(payment).ReloadAsync(ct);
            try
            {
                await RefreshPaymentAsync(payment, ct);
            }
            catch (StaxException ex)
            {
                _logger.LogWarning("Could not refresh payment of order {OrderId}: {Message}", orderId, ex.Message);
            }
        }

        return ToDto(order, payment);
    }

    private static OrderDto ToDto(Order order, OrderPayment? payment)
    {
        var dto = new OrderDto
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            OrderDate = order.OrderDate,
            ShipToAddress = new ShipToAddressRequest
            {
                Street = order.ShipToAddress?.Street,
                City = order.ShipToAddress?.City,
                State = order.ShipToAddress?.State,
                Country = order.ShipToAddress?.Country,
                ZipCode = order.ShipToAddress?.ZipCode,
            },
            Items = order.OrderItems.Select(i => new OrderLineDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units,
            }).ToList(),
            Total = Round(order.Total()),
        };

        if (payment is not null)
        {
            dto.Paid = payment.Status == PaymentStatuses.Paid;
            dto.PaymentStatus = payment.Status;
            dto.PaymentMethodId = payment.StaxPaymentMethodId;
            dto.PaymentMethod = payment.Method;
            dto.Fee = payment.Fee;
            dto.AmountCharged = payment.ChargedTotal;
            dto.PaidAt = payment.PaidAt;
            dto.PaymentMessage = payment.FailureMessage;
            dto.TransactionId = payment.StaxTransactionId;
            dto.RefundStatus = payment.RefundStatus;
            dto.RefundAmount = payment.RefundAmount;
            dto.RefundedAt = payment.RefundedAt;
            dto.RefundMessage = payment.RefundMessage;
        }

        return dto;
    }

    // ---- checkout, pay --------------------------------------------------------------------------

    private async Task<(decimal Subtotal, decimal Fee, decimal Total)> QuoteAsync(Order order, SavedPaymentMethod method, CancellationToken ct)
    {
        var subtotal = Round(order.Total());
        if (subtotal <= 0)
        {
            throw ApiProblemException.Unprocessable("The order has nothing to pay.");
        }

        var review = await _stax.ReviewSurchargeAsync(Guid.Parse(method.StaxPaymentMethodId), subtotal, ct);
        var fee = Math.Max(0, Round(review.Dec("surcharge_amount") ?? 0));
        return (subtotal, fee, subtotal + fee);
    }

    private static void EnsurePayable(OrderPayment? payment)
    {
        if (payment is null)
        {
            return;
        }

        switch (payment.Status)
        {
            case PaymentStatuses.Paid:
                throw ApiProblemException.Conflict("The order is already paid.");
            case PaymentStatuses.Pending:
                throw ApiProblemException.Conflict("The order's bank payment is still being settled.");
            case PaymentStatuses.Processing when DateTimeOffset.UtcNow - payment.CreatedAt < StuckProcessing:
                throw ApiProblemException.Conflict("A payment for the order is being processed.");
        }
    }

    public async Task<CheckoutDto> CheckoutAsync(string buyerId, int orderId, string? paymentMethodId, CancellationToken ct)
    {
        var order = await RequireOrderAsync(orderId, buyerId, ct);
        var method = await _methods.RequireUsableAsync(buyerId, paymentMethodId ?? string.Empty, ct);
        EnsurePayable(await _db.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == orderId, ct));
        var quote = await QuoteAsync(order, method, ct);
        return new CheckoutDto
        {
            OrderId = orderId,
            PaymentMethodId = method.StaxPaymentMethodId,
            PaymentMethod = method.Method,
            Subtotal = quote.Subtotal,
            Fee = quote.Fee,
            Total = quote.Total,
        };
    }

    public async Task<PayDto> PayAsync(string buyerId, int orderId, string? paymentMethodId, CancellationToken ct)
    {
        using var _ = await _locks.AcquireAsync(OrderLockKey(orderId));
        var order = await RequireOrderAsync(orderId, buyerId, ct);
        var method = await _methods.RequireUsableAsync(buyerId, paymentMethodId ?? string.Empty, ct);
        var payment = await _db.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
        EnsurePayable(payment);
        var quote = await QuoteAsync(order, method, ct);

        if (payment is null)
        {
            payment = new OrderPayment { OrderId = orderId, BuyerId = order.BuyerId };
            _db.OrderPayments.Add(payment);
        }

        payment.StaxPaymentMethodId = method.StaxPaymentMethodId;
        payment.Method = method.Method;
        payment.StaxTransactionId = null;
        payment.OrderTotal = quote.Subtotal;
        payment.Fee = quote.Fee;
        payment.ChargedTotal = quote.Total;
        payment.Status = PaymentStatuses.Processing;
        payment.FailureMessage = null;
        payment.PaidAt = null;
        payment.CreatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct); // recorded before charging, so a crash cannot lead to a second charge

        JsonElement charge;
        try
        {
            charge = await _stax.ChargeAsync(Guid.Parse(method.StaxPaymentMethodId), quote.Subtotal,
                new Dictionary<string, object?> { ["orderId"] = orderId.ToString(), ["shop"] = "eShopOnWeb" }, ct);
        }
        catch (StaxException ex) when (ex.StaxStatus is >= 400 and < 500)
        {
            payment.Status = PaymentStatuses.Failed;
            payment.FailureMessage = Truncate(ex.Message);
            await _db.SaveChangesAsync(CancellationToken.None);
            throw new ApiProblemException(402, "The payment was not accepted: " + ex.Message);
        }
        catch (Exception ex)
        {
            // The outcome is unknown; do not leave the order blocked.
            payment.Status = PaymentStatuses.Failed;
            payment.FailureMessage = Truncate("The payment could not be confirmed: " + ex.Message);
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        payment.StaxTransactionId = charge.Str("id");
        if (charge.Bool("success") != true)
        {
            payment.Status = PaymentStatuses.Failed;
            payment.FailureMessage = Truncate(charge.Str("message") ?? charge.Str("error_description") ?? "The payment was declined.");
            await _db.SaveChangesAsync(ct);
            throw new ApiProblemException(402, "The payment was declined: " + payment.FailureMessage);
        }

        var charged = charge.Dec("total");
        if (charged is not null)
        {
            payment.ChargedTotal = Round(charged.Value);
            payment.Fee = Math.Max(0, Round(charge.Dec("service_fee_amount") ?? (payment.ChargedTotal - payment.OrderTotal)));
        }

        ApplyOutcome(payment, charge);
        await _db.SaveChangesAsync(ct);

        return new PayDto
        {
            OrderId = orderId,
            Paid = payment.Status == PaymentStatuses.Paid,
            PaymentStatus = payment.Status,
            PaymentMethod = payment.Method,
            Subtotal = payment.OrderTotal,
            Fee = payment.Fee,
            Total = payment.ChargedTotal,
            TransactionId = payment.StaxTransactionId,
            Message = payment.Status == PaymentStatuses.Pending
                ? "The bank payment was submitted; the order is paid once the bank settles it."
                : null,
        };
    }

    /// <summary>
    /// A card payment that succeeded is paid. A bank payment is accepted at once but only goes through when
    /// the bank settles it, which Stax shows as <c>settled_at</c>.
    /// </summary>
    private static void ApplyOutcome(OrderPayment payment, JsonElement transaction)
    {
        var status = transaction.Str("status");
        var failed = transaction.Bool("success") == false
                     || transaction.Bool("is_voided") == true
                     || string.Equals(status, "FAILED", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(status, "DECLINED", StringComparison.OrdinalIgnoreCase);
        if (failed)
        {
            payment.Status = PaymentStatuses.Failed;
            payment.FailureMessage = Truncate(transaction.Str("message") ?? transaction.Str("error_description")
                                              ?? "The payment did not go through.");
            return;
        }

        var settled = transaction.Date("settled_at");
        if (payment.Method == "bank" && settled is null)
        {
            payment.Status = PaymentStatuses.Pending;
            return;
        }

        payment.Status = PaymentStatuses.Paid;
        payment.PaidAt = settled ?? DateTimeOffset.UtcNow;
    }

    /// <summary>Asks Stax whether a pending (bank) payment has settled. The caller holds the order's lock.</summary>
    public async Task RefreshPaymentAsync(OrderPayment payment, CancellationToken ct)
    {
        if (payment.Status != PaymentStatuses.Pending || payment.StaxTransactionId is null)
        {
            return;
        }

        var transaction = await _stax.GetTransactionAsync(Guid.Parse(payment.StaxTransactionId), ct);
        ApplyOutcome(payment, transaction);
        if (payment.Status == PaymentStatuses.Failed && payment.RefundStatus == RefundStatuses.Pending)
        {
            payment.RefundStatus = RefundStatuses.None;
            payment.RefundMessage = "The payment did not go through, so there is nothing to refund.";
            payment.NextRefundAttemptAt = null;
        }

        await _db.SaveChangesAsync(ct);
    }

    // ---- refunds --------------------------------------------------------------------------------

    public async Task<RefundDto> RefundAsync(int orderId, CancellationToken ct)
    {
        using var _ = await _locks.AcquireAsync(OrderLockKey(orderId));
        await RequireOrderAsync(orderId, null, ct);
        var payment = await _db.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
        if (payment is null || payment.Status is PaymentStatuses.Failed or PaymentStatuses.Processing)
        {
            throw ApiProblemException.Conflict("The order has no payment to refund.");
        }

        if (payment.RefundStatus == RefundStatuses.Refunded)
        {
            throw ApiProblemException.Conflict("The order was already refunded.");
        }

        if (payment.RefundStatus == RefundStatuses.Pending)
        {
            throw ApiProblemException.Conflict("A refund of the order is already on its way.");
        }

        if (payment.StaxTransactionId is null)
        {
            throw ApiProblemException.Conflict("The order's payment has no Stax transaction.");
        }

        payment.RefundStatus = RefundStatuses.Pending;
        payment.RefundAmount = payment.ChargedTotal; // the whole amount the shopper paid, fee included
        payment.RefundRequestedAt = DateTimeOffset.UtcNow;
        payment.RefundAttempts = 0;
        payment.RefundMessage = null;
        await AttemptRefundAsync(payment, ct);

        if (payment.RefundStatus == RefundStatuses.Failed)
        {
            throw ApiProblemException.Unprocessable("The refund was refused: " + payment.RefundMessage);
        }

        return new RefundDto
        {
            OrderId = orderId,
            RefundStatus = payment.RefundStatus,
            RefundAmount = payment.RefundAmount,
            RefundedAt = payment.RefundedAt,
            Message = payment.RefundStatus == RefundStatuses.Pending
                ? "Stax cannot refund this payment yet (bank payments can only be refunded some days after they were made). The refund is retried automatically."
                : null,
        };
    }

    /// <summary>
    /// Tries to refund the whole charge now. When Stax cannot yet (a bank payment too recent, or Stax unreachable)
    /// the refund stays <c>pending</c> and the background reconciler tries again. The caller holds the order's lock.
    /// </summary>
    private async Task AttemptRefundAsync(OrderPayment payment, CancellationToken ct)
    {
        payment.RefundAttempts++;
        try
        {
            var result = await _stax.RefundAsync(Guid.Parse(payment.StaxTransactionId!), payment.RefundAmount ?? payment.ChargedTotal, ct);
            var refunded = result.Dec("total_refunded") ?? 0;
            if (refunded + 0.005m >= (payment.RefundAmount ?? payment.ChargedTotal))
            {
                payment.RefundStatus = RefundStatuses.Refunded;
                payment.RefundedAt = DateTimeOffset.UtcNow;
                payment.NextRefundAttemptAt = null;
                payment.RefundMessage = null;
                payment.StaxRefundTransactionId = LastRefundId(result);
            }
            else
            {
                payment.RefundStatus = RefundStatuses.Failed;
                payment.RefundMessage = Truncate($"Stax refunded only {refunded:0.00} of {payment.RefundAmount:0.00}.");
                payment.NextRefundAttemptAt = null;
            }
        }
        catch (StaxException ex) when (ex.StaxStatus is 0 or 429 or >= 500 || NotYetRefundable.IsMatch(ex.Message))
        {
            payment.RefundStatus = RefundStatuses.Pending;
            payment.RefundMessage = Truncate(ex.Message);
            payment.NextRefundAttemptAt = DateTimeOffset.UtcNow + RefundRetryDelay;
        }
        catch (StaxException ex)
        {
            payment.RefundStatus = RefundStatuses.Failed;
            payment.RefundMessage = Truncate(ex.Message);
            payment.NextRefundAttemptAt = null;
        }

        await _db.SaveChangesAsync(ct);
    }

    private static string? LastRefundId(JsonElement transaction)
    {
        if (transaction.Prop("child_transactions") is { ValueKind: JsonValueKind.Array } children)
        {
            return children.Items().LastOrDefault(c => c.Str("type") == "refund").Str("id");
        }

        return null;
    }

    // ---- background reconciliation --------------------------------------------------------------

    /// <summary>Settles pending bank payments and retries pending refunds. Called periodically.</summary>
    public async Task ReconcileAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var ids = await _db.OrderPayments
            .Where(p => p.Status == PaymentStatuses.Pending
                        || (p.RefundStatus == RefundStatuses.Pending
                            && (p.NextRefundAttemptAt == null || p.NextRefundAttemptAt <= now)))
            .Select(p => p.OrderId)
            .ToListAsync(ct);

        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var _ = await _locks.AcquireAsync(OrderLockKey(id));
                var payment = await _db.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == id, ct);
                if (payment is null)
                {
                    continue;
                }

                await _db.Entry(payment).ReloadAsync(ct);
                if (payment.Status == PaymentStatuses.Pending)
                {
                    await RefreshPaymentAsync(payment, ct);
                }

                if (payment.RefundStatus == RefundStatuses.Pending
                    && (payment.NextRefundAttemptAt == null || payment.NextRefundAttemptAt <= DateTimeOffset.UtcNow))
                {
                    await AttemptRefundAsync(payment, ct);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reconciling order {OrderId} failed; will retry.", id);
            }
        }
    }

    private static string Truncate(string? message) =>
        string.IsNullOrEmpty(message) ? string.Empty : (message.Length > 900 ? message[..900] : message);
}
