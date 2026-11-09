using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Orders, checkout, payment, order status and refunds.
/// </summary>
public class OrderPaymentService
{
    // Serialises work on one order (process-wide), so an order is never charged or refunded twice at once.
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> Locks = new();

    private const int MaxRefundAttempts = 5;

    // States in which Stax/the card networks do not allow a card surcharge.
    private static readonly HashSet<string> NoSurchargeStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "CT", "MA", "ME", "PR",
    };

    private readonly CatalogContext _db;
    private readonly StaxGateway _stax;
    private readonly ShopperService _shoppers;
    private readonly IUriComposer _uriComposer;

    public OrderPaymentService(CatalogContext db, StaxGateway stax, ShopperService shoppers, IUriComposer uriComposer)
    {
        _db = db;
        _stax = stax;
        _shoppers = shoppers;
        _uriComposer = uriComposer;
    }

    private static async Task<IDisposable> LockAsync(int orderId)
    {
        var gate = Locks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        return new Releaser(gate);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _gate;
        public Releaser(SemaphoreSlim gate) => _gate = gate;
        public void Dispose() => _gate.Release();
    }

    // ---------- create ----------

    public async Task<OrderDto> CreateOrderAsync(string email, CreateOrderRequest request)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            throw new ApiProblemException(400, "An order needs at least one item.");
        }
        if (request.Items.Any(i => i.Quantity < 1 || i.Quantity > 1000))
        {
            throw new ApiProblemException(400, "Each quantity must be between 1 and 1000.");
        }

        var wanted = request.Items.GroupBy(i => i.CatalogItemId).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
        var ids = wanted.Keys.ToList();
        var catalog = await _db.CatalogItems.Where(c => ids.Contains(c.Id)).ToListAsync();
        var missing = ids.Where(id => catalog.All(c => c.Id != id)).ToList();
        if (missing.Count > 0)
        {
            throw new ApiProblemException(404, $"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var lines = catalog.Select(c =>
        {
            var ordered = new CatalogItemOrdered(c.Id, c.Name, _uriComposer.ComposePicUri(c.PictureUri));
            return new OrderItem(ordered, c.Price, wanted[c.Id]);
        }).ToList();

        var ship = request.ShipTo;
        var address = new Address(
            Fill(ship?.Street), Fill(ship?.City), ship?.State ?? string.Empty, Fill(ship?.Country), Fill(ship?.ZipCode));
        var order = new Order(email, address, lines);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var payment = new OrderPayment
        {
            OrderId = order.Id,
            BuyerEmail = email,
            Subtotal = order.Total(),
            Total = order.Total(),
        };
        _db.OrderPayments.Add(payment);
        await _db.SaveChangesAsync();
        return ToDto(order, payment);

        static string Fill(string? s) => string.IsNullOrWhiteSpace(s) ? "-" : s.Trim();
    }

    // ---------- checkout / pay ----------

    public sealed record Quote(Order Order, OrderPayment Payment, SavedPaymentMethod Saved, StaxPaymentMethod AtStax, decimal Fee, decimal Total);

    public async Task<CheckoutResponse> CheckoutAsync(string email, int orderId, string? paymentMethodId)
    {
        var quote = await QuoteAsync(email, orderId, paymentMethodId);
        return new CheckoutResponse
        {
            OrderId = orderId,
            PaymentMethodId = quote.Saved.StaxPaymentMethodId,
            PaymentMethodType = quote.Saved.Kind,
            Subtotal = quote.Payment.Subtotal,
            Fee = quote.Fee,
            Total = quote.Total,
        };
    }

    private async Task<Quote> QuoteAsync(string email, int orderId, string? paymentMethodId)
    {
        if (string.IsNullOrWhiteSpace(paymentMethodId))
        {
            throw new ApiProblemException(400, "paymentMethodId is required.");
        }
        var (order, payment) = await LoadOwnedAsync(email, orderId);
        if (payment.Status is PaymentStatus.Paid or PaymentStatus.Pending)
        {
            throw new ApiProblemException(409, "This order has already been paid.");
        }
        if (payment.Status == PaymentStatus.Processing)
        {
            throw new ApiProblemException(409, "A payment for this order is already in progress.");
        }

        var saved = await _shoppers.FindActiveMethodAsync(email, paymentMethodId);
        var atStax = await _stax.GetPaymentMethodAsync(saved.StaxPaymentMethodId);
        if (atStax is null || atStax.DeletedAt is not null)
        {
            saved.RemovedAt = atStax?.DeletedAt ?? DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync();
            throw new ApiProblemException(404, "No such saved payment method.");
        }

        var fee = await ComputeFeeAsync(atStax, payment.Subtotal);
        return new Quote(order, payment, saved, atStax, fee, payment.Subtotal + fee);
    }

    /// <summary>The processing fee passed on to the shopper: only card (credit) payments carry one.</summary>
    private async Task<decimal> ComputeFeeAsync(StaxPaymentMethod method, decimal subtotal)
    {
        if (method.IsBank)
        {
            return 0m;
        }
        var fees = await _stax.GetMerchantFeesAsync();
        if (!fees.SurchargeEnabled || fees.CreditSurchargePercent is not > 0)
        {
            return 0m;
        }
        if (string.Equals(method.BinType, "DEBIT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(method.BinType, "PREPAID", StringComparison.OrdinalIgnoreCase)
            || method.Prepaid)
        {
            return 0m;
        }
        if (string.Equals(fees.Policy, "state_check", StringComparison.OrdinalIgnoreCase)
            && method.State is not null && NoSurchargeStates.Contains(method.State.Trim()))
        {
            return 0m;
        }
        return Math.Round(subtotal * fees.CreditSurchargePercent.Value / 100m, 2, MidpointRounding.AwayFromZero);
    }

    public async Task<OrderDto> PayAsync(string email, int orderId, string? paymentMethodId)
    {
        using var _ = await LockAsync(orderId);
        var quote = await QuoteAsync(email, orderId, paymentMethodId);
        var payment = quote.Payment;

        payment.Status = PaymentStatus.Processing;
        payment.Fee = quote.Fee;
        payment.Total = quote.Total;
        payment.StaxPaymentMethodId = quote.Saved.StaxPaymentMethodId;
        payment.Method = quote.Saved.Kind;
        payment.LastError = null;
        await _db.SaveChangesAsync();

        StaxTransaction tx;
        try
        {
            tx = await _stax.ChargeAsync(payment.StaxPaymentMethodId, payment.Total, payment.Subtotal, payment.Fee,
                $"eShopOnWeb order {orderId}");
        }
        catch (ApiProblemException ex)
        {
            payment.Status = PaymentStatus.Unpaid;
            payment.LastError = Trim(ex.Message);
            await _db.SaveChangesAsync();
            throw;
        }

        payment.StaxTransactionId = tx.Id;
        if (!tx.Success)
        {
            payment.Status = PaymentStatus.Unpaid;
            payment.LastError = Trim(tx.Message ?? "declined");
            await _db.SaveChangesAsync();
            throw new ApiProblemException(402, $"The payment was declined: {tx.Message ?? "no reason given"}.");
        }

        ApplyCharge(payment, tx);
        await _db.SaveChangesAsync();
        return ToDto(quote.Order, payment);
    }

    /// <summary>Card payments are final once accepted. Bank payments are final when the bank settles them.</summary>
    private static void ApplyCharge(OrderPayment payment, StaxTransaction tx)
    {
        if (payment.Method != "bank")
        {
            payment.Status = tx.Success ? PaymentStatus.Paid : PaymentStatus.Failed;
            payment.PaidAt ??= tx.Success ? DateTimeOffset.UtcNow : null;
            return;
        }

        var status = tx.Status?.Trim().ToUpperInvariant();
        if (!tx.Success || tx.IsVoided || status is "FAILED" or "RETURNED" or "REJECTED" or "DECLINED" or "VOIDED")
        {
            payment.Status = PaymentStatus.Failed;
            payment.PaidAt = null;
        }
        else if (tx.SettledAt is not null || status == "SETTLED")
        {
            payment.Status = PaymentStatus.Paid;
            payment.PaidAt ??= tx.SettledAt ?? DateTimeOffset.UtcNow;
        }
        else
        {
            payment.Status = PaymentStatus.Pending;
        }
    }

    // ---------- status ----------

    public async Task<OrderDto> GetOrderAsync(string email, int orderId)
    {
        var (order, payment) = await LoadOwnedAsync(email, orderId);
        await TrySyncAsync(orderId);
        await _db.Entry(payment).ReloadAsync();
        return ToDto(order, payment);
    }

    public async Task<AdminOrderDto> GetAdminOrderAsync(int orderId)
    {
        var (order, payment) = await LoadAsync(orderId);
        await TrySyncAsync(orderId);
        await _db.Entry(payment).ReloadAsync();
        var dto = new AdminOrderDto
        {
            Buyer = order.BuyerId,
            TransactionId = payment.StaxTransactionId,
            RefundStatus = payment.RefundStatus,
            RefundAmount = payment.RefundAmount,
            RefundRequestedAt = payment.RefundRequestedAt,
        };
        CopyOrder(dto, order, payment);
        return dto;
    }

    private async Task TrySyncAsync(int orderId)
    {
        try
        {
            await SyncAsync(orderId);
        }
        catch (ApiProblemException)
        {
            // Stax is not reachable right now: keep what we know.
        }
    }

    /// <summary>Asks Stax where a pending bank payment or refund stands and records it.</summary>
    public async Task SyncAsync(int orderId)
    {
        using var _ = await LockAsync(orderId);
        var payment = await _db.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == orderId);
        if (payment is null || payment.StaxTransactionId is null)
        {
            return;
        }
        await _db.Entry(payment).ReloadAsync();

        if (payment.Status == PaymentStatus.Pending || payment.RefundStatus == RefundState.Pending
            || payment.RefundStatus == RefundState.Failed)
        {
            var tx = await _stax.GetTransactionAsync(payment.StaxTransactionId);
            if (payment.Status == PaymentStatus.Pending)
            {
                ApplyCharge(payment, tx);
            }
            if (payment.RefundStatus is RefundState.Pending or RefundState.Failed && payment.RefundRequestedAt is not null)
            {
                payment.RefundStatus = DeriveRefundStatus(tx, payment.Total);
                if (payment.RefundStatus == RefundState.Failed && payment.RefundAttempts < MaxRefundAttempts
                    && (payment.RefundLastAttemptAt is null || payment.RefundLastAttemptAt < DateTimeOffset.UtcNow.AddMinutes(-1)))
                {
                    // The refund did not go through: keep trying so the shopper still gets the money back.
                    await RequestRefundAsync(payment);
                }
            }
            await _db.SaveChangesAsync();
        }
    }

    /// <summary>Works through everything awaiting a bank or a refund (used by the background worker).</summary>
    public async Task<int> SyncAllPendingAsync()
    {
        var ids = await _db.OrderPayments
            .Where(p => p.StaxTransactionId != null
                && (p.Status == PaymentStatus.Pending
                    || p.RefundStatus == RefundState.Pending
                    || (p.RefundStatus == RefundState.Failed && p.RefundAttempts < MaxRefundAttempts)))
            .Select(p => p.OrderId).ToListAsync();
        foreach (var id in ids)
        {
            try
            {
                await SyncAsync(id);
            }
            catch (ApiProblemException)
            {
            }
        }
        return ids.Count;
    }

    // ---------- refunds ----------

    public async Task<RefundResponse> RefundAsync(int orderId)
    {
        using var _ = await LockAsync(orderId);
        var (_, payment) = await LoadAsync(orderId);

        if (payment.StaxTransactionId is null || payment.Status is not (PaymentStatus.Paid or PaymentStatus.Pending))
        {
            throw new ApiProblemException(409, "This order has no payment to refund.");
        }
        if (payment.RefundStatus is RefundState.Pending or RefundState.Refunded)
        {
            throw new ApiProblemException(409, $"This order's refund is already {payment.RefundStatus}.");
        }

        payment.RefundAmount = payment.Total;
        payment.RefundRequestedAt = DateTimeOffset.UtcNow;
        payment.RefundAttempts = 0;
        await RequestRefundAsync(payment);
        await _db.SaveChangesAsync();
        return new RefundResponse { OrderId = orderId, Amount = payment.Total, RefundStatus = payment.RefundStatus };
    }

    private async Task RequestRefundAsync(OrderPayment payment)
    {
        payment.RefundAttempts++;
        payment.RefundLastAttemptAt = DateTimeOffset.UtcNow;
        try
        {
            // the whole amount the shopper paid, fee included
            var tx = await _stax.RefundAsync(payment.StaxTransactionId!, payment.Total);
            payment.RefundStatus = DeriveRefundStatus(tx, payment.Total);
            if (payment.RefundStatus == RefundState.None)
            {
                payment.RefundStatus = RefundState.Pending;
            }
        }
        catch (ApiProblemException ex)
        {
            payment.RefundStatus = RefundState.Failed;
            payment.LastError = Trim(ex.Message);
            await _db.SaveChangesAsync();
            if (payment.RefundAttempts == 1)
            {
                // first request by staff: tell them why
                throw;
            }
        }
    }

    private static string DeriveRefundStatus(StaxTransaction tx, decimal total)
    {
        if (tx.IsVoided || (tx.TotalRefunded > 0 && tx.TotalRefunded >= total))
        {
            return RefundState.Refunded;
        }
        var children = tx.Children.Where(c =>
            c.Type.Equals("refund", StringComparison.OrdinalIgnoreCase)
            || c.Type.Equals("void", StringComparison.OrdinalIgnoreCase)).ToList();
        if (children.Any(c => c.Success))
        {
            return RefundState.Refunded;
        }
        if (children.Count == 0 || children.Any(c => string.Equals(c.Status, "PENDING", StringComparison.OrdinalIgnoreCase)))
        {
            return RefundState.Pending;
        }
        return RefundState.Failed;
    }

    // ---------- loading / mapping ----------

    private async Task<(Order Order, OrderPayment Payment)> LoadAsync(int orderId)
    {
        var order = await _db.Orders.Include(o => o.OrderItems).FirstOrDefaultAsync(o => o.Id == orderId);
        var payment = order is null ? null : await _db.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == orderId);
        if (order is null || payment is null)
        {
            throw new ApiProblemException(404, "No such order.");
        }
        return (order, payment);
    }

    private async Task<(Order Order, OrderPayment Payment)> LoadOwnedAsync(string email, int orderId)
    {
        var (order, payment) = await LoadAsync(orderId);
        if (!string.Equals(order.BuyerId, email, StringComparison.OrdinalIgnoreCase))
        {
            // do not reveal other shoppers' orders
            throw new ApiProblemException(404, "No such order.");
        }
        return (order, payment);
    }

    private static OrderDto ToDto(Order order, OrderPayment payment)
    {
        var dto = new OrderDto();
        CopyOrder(dto, order, payment);
        return dto;
    }

    private static void CopyOrder(OrderDto dto, Order order, OrderPayment payment)
    {
        dto.OrderId = order.Id;
        dto.OrderDate = order.OrderDate;
        dto.Items = order.OrderItems.Select(i => new OrderItemDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            Name = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Quantity = i.Units,
        }).ToList();
        dto.Subtotal = payment.Subtotal;
        dto.Fee = payment.Fee;
        dto.Total = payment.Total;
        dto.Paid = payment.Status == PaymentStatus.Paid;
        dto.PaymentStatus = payment.Status;
        dto.PaymentMethodId = payment.StaxPaymentMethodId;
        dto.PaymentMethodType = payment.Method;
        dto.PaidAt = payment.PaidAt;
    }

    private static string Trim(string s) => s.Length > 480 ? s[..480] : s;
}
