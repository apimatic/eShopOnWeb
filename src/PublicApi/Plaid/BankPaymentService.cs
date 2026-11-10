using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

public record PaymentOption(string Speed, decimal Fee, DateTime ArrivesOn, decimal Total);

public record CheckoutLine(int CatalogItemId, string Name, string PictureUri, decimal UnitPrice, int Units);

public class ShipToInput
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class CheckoutItemInput
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class StartBankPaymentInput
{
    public int ConnectionId { get; set; }
    public string? AccountId { get; set; }
    public List<CheckoutItemInput>? Items { get; set; }
    public decimal ShippingCost { get; set; }
    public ShipToInput? ShipTo { get; set; }
    public string? LegalName { get; set; }
}

public record CheckoutQuote(Guid CheckoutId, decimal Subtotal, decimal ShippingCost, decimal Total, IReadOnlyList<PaymentOption> Options);

public record OrderConfirmation(int OrderId, string Speed, decimal Fee, decimal Total, DateTime ArrivesOn);

public record CancelResult(int OrderId, decimal Refunded, string Status);

/// <summary>US business-day rules for ACH: weekends and Federal Reserve holidays don't count.</summary>
public static class BusinessCalendar
{
    public static bool IsBusinessDay(DateTime date) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !IsFederalHoliday(date.Date);

    public static DateTime NextBusinessDay(DateTime date)
    {
        var d = date.Date.AddDays(1);
        while (!IsBusinessDay(d)) d = d.AddDays(1);
        return d;
    }

    public static DateTime AddBusinessDays(DateTime date, int days)
    {
        var d = date.Date;
        for (var i = 0; i < days; i++) d = NextBusinessDay(d);
        return d;
    }

    private static bool IsFederalHoliday(DateTime d)
    {
        // Saturday holidays are not observed on Friday by the Federal Reserve; Sunday ones move to Monday.
        bool Fixed(int month, int day)
        {
            var actual = new DateTime(d.Year, month, day);
            return actual.DayOfWeek == DayOfWeek.Sunday ? d == actual.AddDays(1) : d == actual;
        }
        bool Nth(int month, DayOfWeek dow, int n)
        {
            var first = new DateTime(d.Year, month, 1);
            var offset = ((int)dow - (int)first.DayOfWeek + 7) % 7;
            return d == first.AddDays(offset + 7 * (n - 1));
        }
        bool Last(int month, DayOfWeek dow)
        {
            var last = new DateTime(d.Year, month, DateTime.DaysInMonth(d.Year, month));
            var offset = ((int)last.DayOfWeek - (int)dow + 7) % 7;
            return d == last.AddDays(-offset);
        }

        return Fixed(1, 1) || Nth(1, DayOfWeek.Monday, 3) || Nth(2, DayOfWeek.Monday, 3) || Last(5, DayOfWeek.Monday)
            || Fixed(6, 19) || Fixed(7, 4) || Nth(9, DayOfWeek.Monday, 1) || Nth(10, DayOfWeek.Monday, 2)
            || Fixed(11, 11) || Nth(11, DayOfWeek.Thursday, 4) || Fixed(12, 25);
    }
}

public class BankPaymentService
{
    public const string Standard = "standard";
    public const string Express = "express";
    public const decimal ExpressFee = 5.00m;
    /// <summary>NACHA's same-day ACH ceiling per payment.</summary>
    public const decimal SameDayLimit = 1_000_000m;
    private static readonly TimeSpan SameDayCutoff = new(15, 0, 0);

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    private readonly CatalogContext _db;
    private readonly IPlaidClient _plaid;
    private readonly TimeProvider _time;

    public BankPaymentService(CatalogContext db, IPlaidClient plaid, TimeProvider time)
    {
        _db = db;
        _plaid = plaid;
        _time = time;
    }

    // ---- options -------------------------------------------------------------------------------

    private DateTime EasternNow()
    {
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
        catch (TimeZoneNotFoundException) { zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
        return TimeZoneInfo.ConvertTime(_time.GetUtcNow(), zone).DateTime;
    }

    public IReadOnlyList<PaymentOption> ComputeOptions(decimal goodsAndShipping)
    {
        var now = EasternNow();
        var options = new List<PaymentOption>
        {
            new(Standard, 0m, BusinessCalendar.AddBusinessDays(now, 3), goodsAndShipping)
        };

        var expressTotal = goodsAndShipping + ExpressFee;
        if (BusinessCalendar.IsBusinessDay(now) && now.TimeOfDay < SameDayCutoff && expressTotal <= SameDayLimit)
        {
            options.Add(new PaymentOption(Express, ExpressFee, now.Date, expressTotal));
        }
        return options;
    }

    private static CheckoutQuote Quote(BankCheckout c, IReadOnlyList<PaymentOption> options) =>
        new(c.Id, c.Subtotal, c.ShippingCost, c.Subtotal + c.ShippingCost, options);

    private static decimal ValidateShipping(decimal shipping)
    {
        if (shipping < 0 || shipping > 1_000_000_000m)
        {
            throw new ApiException(400, "shippingCost must be zero or more.");
        }
        if (decimal.Round(shipping, 2) != shipping)
        {
            throw new ApiException(400, "shippingCost can have at most two decimal places.");
        }
        return shipping;
    }

    // ---- start ---------------------------------------------------------------------------------

    public async Task<CheckoutQuote> StartAsync(string buyer, StartBankPaymentInput input)
    {
        var connection = await _db.BankConnections
            .FirstOrDefaultAsync(c => c.Id == input.ConnectionId && c.BuyerId == buyer);
        if (connection == null)
        {
            throw new ApiException(404, "Connection not found.");
        }
        if (!connection.HasBankAccounts)
        {
            throw new ApiException(422, "That connection has no bank account to pay from.");
        }

        var accounts = Buyer.ReadAccounts(connection).Where(a => a.Type == "depository").ToList();
        BankAccountInfo? account;
        if (!string.IsNullOrWhiteSpace(input.AccountId))
        {
            account = accounts.FirstOrDefault(a => a.Id == input.AccountId);
            if (account == null) throw new ApiException(404, "Account not found on that connection.");
        }
        else
        {
            account = accounts.FirstOrDefault(a => a.Subtype == "checking") ?? accounts.First();
        }

        var shipping = ValidateShipping(input.ShippingCost);

        if (input.Items == null || input.Items.Count == 0)
        {
            throw new ApiException(400, "items must contain at least one item.");
        }
        if (input.Items.Any(i => i.Quantity < 1))
        {
            throw new ApiException(400, "Every item needs a quantity of at least 1.");
        }

        var wanted = input.Items.GroupBy(i => i.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(i => (long)i.Quantity));
        if (wanted.Values.Any(q => q > int.MaxValue))
        {
            throw new ApiException(400, "Quantity is too large.");
        }

        var ids = wanted.Keys.ToList();
        var catalog = await _db.CatalogItems.Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
        var missing = ids.Where(id => !catalog.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            throw new ApiException(400, $"Unknown catalog item(s): {string.Join(", ", missing)}.");
        }

        // Prices come from the catalog, never from the caller.
        var lines = ids.Select(id => new CheckoutLine(id, catalog[id].Name, catalog[id].PictureUri ?? string.Empty,
            catalog[id].Price, (int)wanted[id])).ToList();
        decimal subtotal;
        try
        {
            subtotal = lines.Sum(l => l.UnitPrice * l.Units);
        }
        catch (OverflowException)
        {
            throw new ApiException(400, "The order is too large.");
        }
        if (subtotal + shipping <= 0)
        {
            throw new ApiException(400, "There is nothing to pay for.");
        }

        var ship = input.ShipTo;
        var checkout = new BankCheckout
        {
            BuyerId = buyer,
            ConnectionId = connection.Id,
            AccountId = account.Id,
            LegalName = string.IsNullOrWhiteSpace(input.LegalName) ? buyer : input.LegalName.Trim(),
            ItemsJson = JsonSerializer.Serialize(lines),
            ShipToJson = JsonSerializer.Serialize(ship ?? new ShipToInput()),
            Subtotal = subtotal,
            ShippingCost = shipping,
        };
        _db.BankCheckouts.Add(checkout);
        await _db.SaveChangesAsync();

        return Quote(checkout, ComputeOptions(subtotal + shipping));
    }

    // ---- shipping ------------------------------------------------------------------------------

    private async Task<BankCheckout> GetCheckoutAsync(string buyer, Guid checkoutId)
    {
        var checkout = await _db.BankCheckouts.FirstOrDefaultAsync(c => c.Id == checkoutId && c.BuyerId == buyer);
        return checkout ?? throw new ApiException(404, "Checkout not found.");
    }

    public async Task<CheckoutQuote> ChangeShippingAsync(string buyer, Guid checkoutId, decimal shippingCost)
    {
        var shipping = ValidateShipping(shippingCost);
        using var _ = await LockAsync(checkoutId.ToString());
        var checkout = await GetCheckoutAsync(buyer, checkoutId);
        if (checkout.Status != BankCheckoutStatus.Open)
        {
            throw new ApiException(409, "This checkout is already confirmed.");
        }
        if (checkout.Subtotal + shipping <= 0)
        {
            throw new ApiException(400, "There is nothing to pay for.");
        }
        checkout.ShippingCost = shipping;
        await _db.SaveChangesAsync();
        return Quote(checkout, ComputeOptions(checkout.Subtotal + shipping));
    }

    // ---- confirm -------------------------------------------------------------------------------

    public async Task<OrderConfirmation> ConfirmAsync(string buyer, Guid checkoutId, string? speed)
    {
        var chosen = (speed ?? string.Empty).Trim().ToLowerInvariant();
        if (chosen.Length == 0)
        {
            throw new ApiException(400, "speed is required.");
        }

        using var _ = await LockAsync(checkoutId.ToString());
        var checkout = await GetCheckoutAsync(buyer, checkoutId);

        if (checkout.Status == BankCheckoutStatus.Confirmed)
        {
            // Confirming twice must not charge twice: answer with the original confirmation.
            return new OrderConfirmation(checkout.OrderId!.Value, checkout.Speed!, checkout.Fee!.Value,
                checkout.Subtotal + checkout.ShippingCost + checkout.Fee.Value, checkout.ArrivesOn!.Value);
        }

        // Always the current total and the options for it, as they were not necessarily the ones first offered.
        var options = ComputeOptions(checkout.Subtotal + checkout.ShippingCost);
        var option = options.FirstOrDefault(o => o.Speed == chosen);
        if (option == null)
        {
            throw new ApiException(422,
                $"Speed '{speed}' is not available for this order. Available: {string.Join(", ", options.Select(o => o.Speed))}.");
        }

        var connection = await _db.BankConnections.FirstOrDefaultAsync(c => c.Id == checkout.ConnectionId && c.BuyerId == buyer);
        if (connection == null)
        {
            throw new ApiException(409, "The bank for this checkout has been disconnected.");
        }

        var amount = option.Total.ToString("0.00", CultureInfo.InvariantCulture);
        var authorization = await _plaid.PostAsync("/transfer/authorization/create", new JsonObject
        {
            ["access_token"] = connection.AccessToken,
            ["account_id"] = checkout.AccountId,
            ["type"] = "debit",
            ["network"] = chosen == Express ? "same-day-ach" : "ach",
            ["amount"] = amount,
            ["ach_class"] = "web",
            ["user"] = new JsonObject { ["legal_name"] = checkout.LegalName },
        });
        var auth = authorization["authorization"]!;
        if (auth["decision"]?.GetValue<string>() != "approved")
        {
            var why = auth["decision_rationale"]?["description"]?.GetValue<string>()
                      ?? auth["decision_rationale"]?["code"]?.GetValue<string>() ?? "no reason given";
            throw new ApiException(422, $"The bank declined the payment: {why}.");
        }

        var transfer = await _plaid.PostAsync("/transfer/create", new JsonObject
        {
            ["access_token"] = connection.AccessToken,
            ["account_id"] = checkout.AccountId,
            ["authorization_id"] = auth["id"]!.GetValue<string>(),
            ["description"] = "eShopOnWeb",
        });
        var transferId = transfer["transfer"]!["id"]!.GetValue<string>();

        try
        {
            var ship = JsonSerializer.Deserialize<ShipToInput>(checkout.ShipToJson) ?? new ShipToInput();
            string Or(string? v) => string.IsNullOrWhiteSpace(v) ? "Not provided" : v.Trim();
            var lines = JsonSerializer.Deserialize<List<CheckoutLine>>(checkout.ItemsJson)!;
            var order = new Order(buyer,
                new Address(Or(ship.Street), Or(ship.City), ship.State?.Trim() ?? string.Empty, Or(ship.Country), Or(ship.ZipCode)),
                lines.Select(l => new OrderItem(
                    new CatalogItemOrdered(l.CatalogItemId, l.Name.Length > 50 ? l.Name[..50] : l.Name,
                        string.IsNullOrEmpty(l.PictureUri) ? "placeholder.png" : l.PictureUri),
                    l.UnitPrice, l.Units)).ToList());
            _db.Orders.Add(order);
            await _db.SaveChangesAsync();

            _db.BankPayments.Add(new BankPayment
            {
                OrderId = order.Id,
                BuyerId = buyer,
                TransferId = transferId,
                Amount = option.Total,
                Fee = option.Fee,
                Speed = option.Speed,
                ArrivesOn = option.ArrivesOn,
            });
            checkout.Status = BankCheckoutStatus.Confirmed;
            checkout.OrderId = order.Id;
            checkout.Speed = option.Speed;
            checkout.Fee = option.Fee;
            checkout.ArrivesOn = option.ArrivesOn;
            await _db.SaveChangesAsync();

            return new OrderConfirmation(order.Id, option.Speed, option.Fee, option.Total, option.ArrivesOn);
        }
        catch
        {
            // The shopper was charged but there is no order: give the money back.
            await ReverseAsync(transferId, option.Total, $"rb-{checkout.Id:N}");
            throw;
        }
    }

    // ---- cancel --------------------------------------------------------------------------------

    public async Task<CancelResult> CancelAsync(string buyer, int orderId)
    {
        using var _ = await LockAsync("order-" + orderId);
        var payment = await _db.BankPayments.FirstOrDefaultAsync(p => p.OrderId == orderId && p.BuyerId == buyer);
        if (payment == null)
        {
            throw new ApiException(404, "Order not found.");
        }
        if (payment.CancelledAt != null)
        {
            return new CancelResult(orderId, payment.Amount, "cancelled");
        }
        if (payment.ShippedAt != null)
        {
            throw new ApiException(409, "The order has already shipped and can no longer be cancelled.");
        }

        // The whole charge comes back: goods, shipping and any express fee.
        await ReverseAsync(payment.TransferId, payment.Amount, $"refund-{payment.Id}");

        payment.CancelledAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        return new CancelResult(orderId, payment.Amount, "cancelled");
    }

    private async Task ReverseAsync(string transferId, decimal amount, string idempotencyKey)
    {
        var status = await TransferStatusAsync(transferId);
        if (status == "pending")
        {
            try
            {
                await _plaid.PostAsync("/transfer/cancel", new JsonObject { ["transfer_id"] = transferId });
                return;
            }
            catch (PlaidApiException)
            {
                // It may have just been sent to the bank; fall through to a refund.
                status = await TransferStatusAsync(transferId);
            }
        }

        if (status is "cancelled" or "failed" or "returned")
        {
            return; // the money never left, or already came back
        }

        await _plaid.PostAsync("/transfer/refund/create", new JsonObject
        {
            ["transfer_id"] = transferId,
            ["amount"] = amount.ToString("0.00", CultureInfo.InvariantCulture),
            ["idempotency_key"] = idempotencyKey,
        });
    }

    private async Task<string> TransferStatusAsync(string transferId)
    {
        var response = await _plaid.PostAsync("/transfer/get", new JsonObject { ["transfer_id"] = transferId });
        return response["transfer"]?["status"]?.GetValue<string>() ?? string.Empty;
    }

    // ---- locking -------------------------------------------------------------------------------

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _gate;
        public Releaser(SemaphoreSlim gate) => _gate = gate;
        public void Dispose() => _gate.Release();
    }

    private static async Task<IDisposable> LockAsync(string key)
    {
        var gate = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        return new Releaser(gate);
    }
}
