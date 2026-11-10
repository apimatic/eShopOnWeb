using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

public record CheckoutLine(int CatalogItemId, int Quantity);
public record ShipTo(string? Street, string? City, string? State, string? Country, string? ZipCode);
public record StartPaymentRequest(Guid? ConnectionId, List<CheckoutLine>? Items, decimal? ShippingCost, ShipTo? ShipToAddress);
public record ShippingRequest(decimal? ShippingCost);
public record ConfirmRequest(string? Speed);

public class PaymentService
{
    public const string Standard = "standard";
    public const string Express = "express";

    /// <summary>Same-day ACH cannot carry more than this per payment (NACHA limit).</summary>
    public const decimal ExpressLimit = 1_000_000m;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    private readonly PlaidClient _plaid;
    private readonly CatalogContext _db;
    private readonly IUriComposer _uriComposer;
    private readonly decimal _expressFee;

    public PaymentService(PlaidClient plaid, CatalogContext db, IUriComposer uriComposer, IConfiguration config)
    {
        _plaid = plaid;
        _db = db;
        _uriComposer = uriComposer;
        _expressFee = decimal.TryParse(config["Checkout:ExpressFee"], NumberStyles.Number, CultureInfo.InvariantCulture, out var f) && f >= 0 ? f : 5.00m;
    }

    private static string Money(decimal d) => d.ToString("F2", CultureInfo.InvariantCulture);

    private static async Task<IDisposable> LockAsync(string key, CancellationToken ct)
    {
        var sem = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        return new Releaser(sem);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _s;
        public Releaser(SemaphoreSlim s) { _s = s; }
        public void Dispose() => _s.Release();
    }

    private static decimal ValidateShipping(decimal? shipping)
    {
        if (shipping == null) throw new AppException(400, "shippingCost is required.");
        if (shipping < 0) throw new AppException(400, "shippingCost cannot be negative.");
        if (decimal.Round(shipping.Value, 2) != shipping.Value) throw new AppException(400, "shippingCost cannot have more than two decimals.");
        return shipping.Value;
    }

    public decimal FeeFor(string speed) => speed == Express ? _expressFee : 0m;

    public List<string> OptionsFor(decimal total)
    {
        var options = new List<string> { Standard };
        if (total + _expressFee <= ExpressLimit) options.Add(Express);
        return options;
    }

    private object Describe(BankCheckout c)
    {
        var total = c.Total();
        var options = OptionsFor(total);
        return new
        {
            checkoutId = c.Id,
            connectionId = c.ConnectionId,
            itemsTotal = c.ItemsTotal(),
            shippingCost = c.Shipping,
            total,
            currency = "USD",
            options,
            optionDetails = options.Select(o => new { speed = o, fee = FeeFor(o), charge = total + FeeFor(o) }),
            status = c.Status,
        };
    }

    public async Task<object> StartAsync(string buyerId, StartPaymentRequest req, CancellationToken ct)
    {
        if (req.ConnectionId == null) throw new AppException(400, "connectionId is required.");
        var shipping = ValidateShipping(req.ShippingCost);
        if (req.Items == null || req.Items.Count == 0) throw new AppException(400, "items must contain at least one item.");
        if (req.Items.Any(i => i.Quantity < 1)) throw new AppException(400, "Every item needs a quantity of at least 1.");

        var conn = await _db.BankConnections.FirstOrDefaultAsync(c => c.Id == req.ConnectionId && c.BuyerId == buyerId && c.DisconnectedAt == null, ct);
        if (conn == null) throw new AppException(404, "Connection not found.");
        if (conn.Kind != ConnectionKinds.Bank) throw new AppException(422, "Only a bank account can pay for an order.");

        var ids = req.Items.Select(i => i.CatalogItemId).Distinct().ToList();
        var catalog = await _db.CatalogItems.Where(i => ids.Contains(i.Id)).ToListAsync(ct);
        var missing = ids.Where(id => catalog.All(c => c.Id != id)).ToList();
        if (missing.Count > 0) throw new AppException(400, $"Unknown catalog item(s): {string.Join(", ", missing)}.");

        var checkout = new BankCheckout
        {
            BuyerId = buyerId,
            ConnectionId = conn.Id,
            Shipping = shipping,
            ShipStreet = req.ShipToAddress?.Street,
            ShipCity = req.ShipToAddress?.City,
            ShipState = req.ShipToAddress?.State,
            ShipCountry = req.ShipToAddress?.Country,
            ShipZip = req.ShipToAddress?.ZipCode,
        };
        foreach (var g in req.Items.GroupBy(i => i.CatalogItemId))
        {
            var item = catalog.First(c => c.Id == g.Key);
            long qty = g.Sum(x => (long)x.Quantity);
            if (qty > int.MaxValue) throw new AppException(400, "Quantity is too large.");
            checkout.Items.Add(new BankCheckoutItem
            {
                CheckoutId = checkout.Id,
                CatalogItemId = item.Id,
                Name = item.Name,
                PictureUri = _uriComposer.ComposePicUri(item.PictureUri),
                UnitPrice = item.Price,
                Quantity = (int)qty,
            });
        }

        decimal total;
        try { total = checkout.Total(); }
        catch (OverflowException) { throw new AppException(400, "The order total is too large."); }
        if (total <= 0) throw new AppException(422, "The order total must be greater than zero to pay by bank.");

        _db.BankCheckouts.Add(checkout);
        await _db.SaveChangesAsync(ct);
        return Describe(checkout);
    }

    private async Task<BankCheckout> LoadCheckoutAsync(string buyerId, Guid id, CancellationToken ct)
    {
        var checkout = await _db.BankCheckouts.Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == id && c.BuyerId == buyerId, ct);
        if (checkout == null) throw new AppException(404, "Checkout not found.");
        return checkout;
    }

    public async Task<object> ChangeShippingAsync(string buyerId, Guid checkoutId, ShippingRequest req, CancellationToken ct)
    {
        var shipping = ValidateShipping(req.ShippingCost);
        using var _ = await LockAsync("checkout:" + checkoutId, ct);
        _db.ChangeTracker.Clear();
        var checkout = await LoadCheckoutAsync(buyerId, checkoutId, ct);
        if (checkout.Status != CheckoutStatuses.Open) throw new AppException(409, "This checkout has already been confirmed.");
        checkout.Shipping = shipping;
        try { checkout.Total(); }
        catch (OverflowException) { throw new AppException(400, "The order total is too large."); }
        await _db.SaveChangesAsync(ct);
        return Describe(checkout);
    }

    private static object ConfirmationOf(BankPayment p) => new
    {
        orderId = p.OrderId,
        checkoutId = p.CheckoutId,
        speed = p.Speed,
        total = p.OrderTotal,
        fee = p.Fee,
        charged = p.Charged,
        arrivesOn = p.ArrivesOn.HasValue ? DateOnly.FromDateTime(p.ArrivesOn.Value) : (DateOnly?)null,
        status = p.Status,
    };

    public async Task<object> ConfirmAsync(string buyerId, Guid checkoutId, ConfirmRequest req, CancellationToken ct)
    {
        var speed = (req.Speed ?? "").Trim().ToLowerInvariant();
        if (speed != Standard && speed != Express) throw new AppException(400, "speed must be 'standard' or 'express'.");

        using var _ = await LockAsync("checkout:" + checkoutId, ct);
        _db.ChangeTracker.Clear();
        var checkout = await LoadCheckoutAsync(buyerId, checkoutId, ct);

        if (checkout.Status == CheckoutStatuses.Confirmed)
        {
            var done = await _db.BankPayments.FirstAsync(p => p.CheckoutId == checkoutId, ct);
            return ConfirmationOf(done); // confirming twice must not charge twice
        }

        var total = checkout.Total();
        var options = OptionsFor(total);
        if (!options.Contains(speed))
            throw new AppException(422, $"'{speed}' is not available for this order. Available: {string.Join(", ", options)}.");

        var conn = await _db.BankConnections.FirstOrDefaultAsync(c => c.Id == checkout.ConnectionId && c.DisconnectedAt == null, ct);
        if (conn == null || conn.AccessToken == null) throw new AppException(409, "The bank connection is no longer connected.");

        var fee = FeeFor(speed);
        var charge = total + fee;
        var user = new JsonObject { ["legal_name"] = buyerId };

        var auth = await _plaid.PostAsync("/transfer/authorization/create", new JsonObject
        {
            ["access_token"] = conn.AccessToken,
            ["account_id"] = conn.AccountId,
            ["type"] = "debit",
            ["network"] = speed == Express ? "same-day-ach" : "ach",
            ["amount"] = Money(charge),
            ["ach_class"] = "web",
            ["user"] = user,
        }, ct: ct);
        var authorization = auth["authorization"]!;
        if (authorization["decision"]?.GetValue<string>() != "approved")
        {
            var why = authorization["decision_rationale"]?["description"]?.GetValue<string>()
                ?? authorization["decision_rationale"]?["code"]?.GetValue<string>() ?? "declined";
            throw new AppException(422, $"The bank payment was not approved: {why}.");
        }

        var created = await _plaid.PostAsync("/transfer/create", new JsonObject
        {
            ["access_token"] = conn.AccessToken,
            ["account_id"] = conn.AccountId,
            ["authorization_id"] = authorization["id"]!.GetValue<string>(),
            ["description"] = "eShop order",
        }, ct: ct);
        var transfer = created["transfer"]!;
        var transferId = transfer["id"]!.GetValue<string>();
        DateTime? arrives = null;
        if (DateTime.TryParseExact(transfer["expected_funds_available_date"]?.GetValue<string>(), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) arrives = d;

        try
        {
            var address = new Address(
                Or(checkout.ShipStreet), Or(checkout.ShipCity), checkout.ShipState ?? "", Or(checkout.ShipCountry), Or(checkout.ShipZip));
            var orderItems = checkout.Items.Select(i =>
                new OrderItem(new CatalogItemOrdered(i.CatalogItemId, i.Name, i.PictureUri), i.UnitPrice, i.Quantity)).ToList();
            var order = new Order(buyerId, address, orderItems);
            _db.Orders.Add(order);
            await _db.SaveChangesAsync(ct);

            var payment = new BankPayment
            {
                OrderId = order.Id,
                CheckoutId = checkout.Id,
                ConnectionId = conn.Id,
                BuyerId = buyerId,
                TransferId = transferId,
                Speed = speed,
                OrderTotal = total,
                Fee = fee,
                Charged = charge,
                ArrivesOn = arrives,
            };
            _db.BankPayments.Add(payment);
            checkout.Status = CheckoutStatuses.Confirmed;
            await _db.SaveChangesAsync(ct);
            return ConfirmationOf(payment);
        }
        catch
        {
            // The money must not move without an order to show for it.
            try { await _plaid.PostAsync("/transfer/cancel", new JsonObject { ["transfer_id"] = transferId }); } catch (Exception) { }
            throw;
        }

        static string Or(string? s) => string.IsNullOrWhiteSpace(s) ? "N/A" : s;
    }

    public async Task<object> CancelOrderAsync(string buyerId, int orderId, CancellationToken ct)
    {
        using var _ = await LockAsync("order:" + orderId, ct);
        _db.ChangeTracker.Clear();
        var payment = await _db.BankPayments.FirstOrDefaultAsync(p => p.OrderId == orderId && p.BuyerId == buyerId, ct);
        if (payment == null) throw new AppException(404, "No bank-paid order with that id was found.");
        if (payment.Status == PaymentStatuses.Cancelled) return CancellationOf(payment);
        if (payment.ShippedAt != null) throw new AppException(409, "This order has already shipped and can no longer be cancelled.");

        var status = await TransferStatusAsync(payment.TransferId, ct);
        string? refundId = null;
        switch (status)
        {
            case "pending":
                try
                {
                    await _plaid.PostAsync("/transfer/cancel", new JsonObject { ["transfer_id"] = payment.TransferId }, ct: ct);
                }
                catch (PlaidApiException e) when (e.ErrorCode == "TRANSFER_NOT_CANCELLABLE")
                {
                    // It moved on since we looked; the money must be refunded instead.
                    refundId = await RefundAsync(payment, ct);
                }
                break;
            case "cancelled":
            case "failed":
            case "returned":
                break; // the money never moved, or already came back
            default:
                refundId = await RefundAsync(payment, ct);
                break;
        }

        payment.Status = PaymentStatuses.Cancelled;
        payment.RefundId = refundId;
        await _db.SaveChangesAsync(ct);
        return CancellationOf(payment);
    }

    private static object CancellationOf(BankPayment p) => new
    {
        orderId = p.OrderId,
        status = p.Status,
        refunded = p.Charged, // order total, shipping and any express fee
        refundId = p.RefundId,
    };

    private async Task<string> TransferStatusAsync(string transferId, CancellationToken ct)
    {
        var res = await _plaid.PostAsync("/transfer/get", new JsonObject { ["transfer_id"] = transferId }, retry: true, ct);
        return res["transfer"]?["status"]?.GetValue<string>() ?? "";
    }

    private async Task<string> RefundAsync(BankPayment p, CancellationToken ct)
    {
        var res = await _plaid.PostAsync("/transfer/refund/create", new JsonObject
        {
            ["transfer_id"] = p.TransferId,
            ["amount"] = Money(p.Charged),
            ["idempotency_key"] = "refund-" + p.TransferId,
        }, ct: ct);
        return res["refund"]!["id"]!.GetValue<string>();
    }
}
