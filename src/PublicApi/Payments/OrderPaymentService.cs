using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public record Quote(string PaymentMethodId, string Method, decimal Amount, decimal Fee, decimal Total);

/// <summary>Checkout, payment, settlement tracking and refunds of orders, against Stax.</summary>
public class OrderPaymentService
{
    private readonly CatalogContext _db;
    private readonly StaxClient _stax;
    private readonly StaxCustomers _customers;

    public OrderPaymentService(CatalogContext db, StaxClient stax, StaxCustomers customers)
    {
        _db = db;
        _stax = stax;
        _customers = customers;
    }

    public static string Money(decimal d) => d.ToString("0.00", CultureInfo.InvariantCulture);

    public async Task<Order> GetOrderAsync(int orderId, string? buyerEmail, CancellationToken ct = default)
    {
        var order = await _db.Orders.Include(o => o.OrderItems).FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order == null || (buyerEmail != null && !string.Equals(order.BuyerId, buyerEmail, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ApiException(404, "Order not found.");
        }
        return order;
    }

    public async Task<OrderPayment> GetOrCreatePaymentAsync(Order order, CancellationToken ct = default)
    {
        var payment = await _db.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == order.Id, ct);
        if (payment == null)
        {
            payment = new OrderPayment { OrderId = order.Id, BuyerId = order.BuyerId };
            _db.OrderPayments.Add(payment);
            await _db.SaveChangesAsync(ct);
        }
        return payment;
    }

    /// <summary>The exact amount the shopper pays with this payment method, fee included.</summary>
    public async Task<Quote> QuoteAsync(Order order, string buyerEmail, string paymentMethodId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(paymentMethodId)) throw new ApiException(400, "paymentMethodId is required.");
        var method = await _customers.GetOwnedActiveMethodAsync(buyerEmail, paymentMethodId, ct);
        var kind = (string?)method["method"] ?? "card";
        var amount = Math.Round(order.Total(), 2);
        if (amount <= 0) throw new ApiException(409, "The order has nothing to pay.");

        decimal fee = 0m;
        if (kind == "card")
        {
            var review = await _stax.GetAsync(
                $"surcharge/review?payment_method_id={StaxClient.Q(paymentMethodId)}&total={Money(amount)}", ct);
            fee = Math.Round((decimal?)review["surcharge_amount"] ?? 0m, 2);
        }
        return new Quote(paymentMethodId, kind, amount, fee, amount + fee);
    }

    public async Task<OrderPayment> PayAsync(Order order, string buyerEmail, string paymentMethodId, CancellationToken ct = default)
    {
        var payment = await GetOrCreatePaymentAsync(order, ct);
        EnsurePayable(payment);
        var quote = await QuoteAsync(order, buyerEmail, paymentMethodId, ct);

        // Claim the order so a second request cannot charge it again.
        EnsurePayable(await _db.OrderPayments.AsNoTracking().FirstAsync(p => p.Id == payment.Id, ct));
        payment.PaymentStatus = PaymentStatuses.Processing;
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ApiException(409, "This order is already paid or a payment is in progress.");
        }

        JsonNode tx;
        try
        {
            tx = await _stax.PostAsync("charge", new
            {
                payment_method_id = paymentMethodId,
                total = quote.Total,
                pre_auth = false,
                idempotency_id = Guid.NewGuid().ToString(),
                meta = new { reference = $"order-{order.Id}", payment_note = $"eShopOnWeb order {order.Id}" }
            }, ct);
        }
        catch (ApiException ex)
        {
            payment.PaymentStatus = PaymentStatuses.Failed;
            payment.FailureReason = ex.Message;
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        payment.PaymentMethodId = paymentMethodId;
        payment.Method = quote.Method;
        payment.Amount = quote.Amount;
        payment.StaxTransactionId = (string?)tx["id"];

        if ((bool?)tx["success"] != true)
        {
            payment.PaymentStatus = PaymentStatuses.Failed;
            payment.FailureReason = (string?)tx["message"] ?? "The payment was declined.";
            await _db.SaveChangesAsync(CancellationToken.None);
            throw new ApiException(402, "Payment declined: " + payment.FailureReason);
        }

        var charged = (decimal?)tx["total"] ?? quote.Total;
        payment.Total = charged;
        payment.Fee = Math.Max(0m, charged - quote.Amount);
        payment.FailureReason = null;
        if (quote.Method == "bank" && !IsSettled(tx))
        {
            payment.PaymentStatus = PaymentStatuses.Pending;
        }
        else
        {
            payment.PaymentStatus = PaymentStatuses.Paid;
            payment.PaidAt = DateTimeOffset.UtcNow;
        }
        await _db.SaveChangesAsync(CancellationToken.None);
        return payment;
    }

    private static void EnsurePayable(OrderPayment p)
    {
        if (p.PaymentStatus is PaymentStatuses.Paid or PaymentStatuses.Pending or PaymentStatuses.Processing)
        {
            throw new ApiException(409, "This order is already paid or a payment is in progress.");
        }
        if (p.RefundStatus != RefundStatuses.None)
        {
            throw new ApiException(409, "This order has been refunded and cannot be paid again.");
        }
    }

    private static bool IsSettled(JsonNode tx) => tx["settled_at"] != null || (bool?)tx["is_refundable"] == true;

    /// <summary>Brings a bank payment's state up to date with Stax (settled, or rejected).</summary>
    public async Task RefreshPaymentAsync(OrderPayment p, CancellationToken ct = default)
    {
        if (p.PaymentStatus != PaymentStatuses.Pending || p.StaxTransactionId == null) return;
        var tx = await _stax.GetAsync("transaction/" + StaxClient.Q(p.StaxTransactionId), ct);
        if ((bool?)tx["is_voided"] == true)
        {
            p.PaymentStatus = PaymentStatuses.Failed;
            p.FailureReason = "The payment was voided.";
        }
        else if ((bool?)tx["success"] == false)
        {
            p.PaymentStatus = PaymentStatuses.Failed;
            p.FailureReason = (string?)tx["message"] ?? "The bank rejected the payment.";
        }
        else if (IsSettled(tx))
        {
            p.PaymentStatus = PaymentStatuses.Paid;
            p.PaidAt = DateTimeOffset.UtcNow;
        }
        else
        {
            return;
        }
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Starts the refund of a paid order and tries it at once; Stax may need days before it allows it.</summary>
    public async Task<OrderPayment> RequestRefundAsync(Order order, CancellationToken ct = default)
    {
        var p = await GetOrCreatePaymentAsync(order, ct);
        if (p.RefundStatus != RefundStatuses.None) return p;
        if (p.StaxTransactionId == null || p.PaymentStatus is not (PaymentStatuses.Paid or PaymentStatuses.Pending))
        {
            throw new ApiException(409, "The order has no payment to refund.");
        }
        p.RefundStatus = RefundStatuses.Pending;
        p.RefundRequestedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        await TryRefundAsync(p, ct);
        return p;
    }

    /// <summary>Attempts a pending refund at Stax; leaves it pending, with the reason, if Stax is not ready.</summary>
    public async Task TryRefundAsync(OrderPayment p, CancellationToken ct = default)
    {
        if (p.RefundStatus != RefundStatuses.Pending || p.StaxTransactionId == null) return;
        var id = StaxClient.Q(p.StaxTransactionId);
        try
        {
            var tx = await _stax.GetAsync("transaction/" + id, ct);
            if (!IsReversed(tx, p.Total))
            {
                var total = Money(p.Total);
                tx = await _stax.PostAsync($"transaction/{id}/void-or-refund?total={total}", new { total }, ct);
                if (!IsReversed(tx, p.Total)) tx = await _stax.GetAsync("transaction/" + id, ct);
            }
            if (IsReversed(tx, p.Total))
            {
                p.RefundStatus = RefundStatuses.Refunded;
                p.RefundedAt = DateTimeOffset.UtcNow;
                p.RefundNote = null;
            }
            else
            {
                p.RefundNote = "Waiting for Stax to allow the refund.";
            }
        }
        catch (ApiException ex)
        {
            p.RefundNote = ex.Message;
        }
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private static bool IsReversed(JsonNode tx, decimal total)
        => (bool?)tx["is_voided"] == true || ((decimal?)tx["total_refunded"] ?? 0m) >= total;
}
