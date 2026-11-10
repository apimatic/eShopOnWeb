using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Plaid;
using Microsoft.Extensions.Configuration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BankEndpoints;

/// <summary>The payment speeds, their fees and what each one allows.</summary>
public class BankPricing
{
    public const string Standard = "standard";
    public const string Express = "express";

    /// <summary>Same Day ACH can move at most this much in one payment.</summary>
    private readonly decimal _expressLimit;

    public BankPricing(IConfiguration configuration)
    {
        TransferLimit = decimal.TryParse(configuration["BankPayment:TransferLimit"], NumberStyles.Number, CultureInfo.InvariantCulture, out var transferLimit) && transferLimit > 0
            ? transferLimit : 10_000_000m;
        ExpressFee = decimal.TryParse(configuration["BankPayment:ExpressFee"], NumberStyles.Number, CultureInfo.InvariantCulture, out var fee)
            ? fee : 5.00m;
        _expressLimit = decimal.TryParse(configuration["BankPayment:ExpressLimit"], NumberStyles.Number, CultureInfo.InvariantCulture, out var limit)
            ? limit : 1_000_000m;
    }

    public decimal ExpressFee { get; }

    /// <summary>The most Plaid allows in one ACH transfer.</summary>
    public decimal TransferLimit { get; }

    /// <summary>Splits a charge into transfers that each fit the limit.</summary>
    public IReadOnlyList<decimal> SplitCharge(decimal charge)
    {
        var parts = new List<decimal>();
        var left = charge;
        while (left > TransferLimit)
        {
            parts.Add(TransferLimit);
            left -= TransferLimit;
        }
        parts.Add(left);
        return parts;
    }

    public decimal FeeFor(string speed) => speed == Express ? ExpressFee : 0m;

    /// <summary>The speeds available for an order total. Standard has no ceiling, so any size of order can be paid.</summary>
    public IReadOnlyList<string> OptionsFor(decimal total)
    {
        var options = new List<string> { Standard };
        if (total + ExpressFee <= _expressLimit) options.Add(Express);
        return options;
    }

    public static string? NormalizeSpeed(string? speed)
    {
        var s = speed?.Trim().ToLowerInvariant();
        return s is Standard or Express ? s : null;
    }
}

public static class CheckoutDto
{
    public static object From(BankCheckout c, BankPricing pricing) => new
    {
        checkoutId = c.Id,
        connectionId = c.ConnectionId,
        subtotal = c.Subtotal(),
        shippingCost = c.ShippingCost,
        total = c.Total(),
        expressFee = pricing.ExpressFee,
        options = pricing.OptionsFor(c.Total())
    };

    public static bool ValidMoney(decimal? amount) => amount is >= 0 && decimal.Round(amount.Value, 2) == amount.Value;
}

public class CheckoutItemRequest
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

public class StartBankPaymentRequest : BaseRequest
{
    public int ConnectionId { get; set; }
    public List<CheckoutItemRequest>? Items { get; set; }
    public decimal? ShippingCost { get; set; }
    public ShipToRequest? ShipTo { get; set; }
}

/// <summary>Starts a bank payment: prices the order and offers the speeds.</summary>
public class StartBankPaymentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/checkout/bank-payment",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (StartBankPaymentRequest? request, ClaimsPrincipal user, CatalogContext db, BankPricing pricing, CancellationToken ct) =>
                {
                    var buyer = BankApi.UserName(user);
                    if (buyer is null) return BankApi.Fail(401, "The token does not identify a user.");
                    if (request is null) return BankApi.Fail(400, "A request body is required.");

                    var connection = await db.BankConnections.FirstOrDefaultAsync(
                        c => c.Id == request.ConnectionId && c.BuyerId == buyer, ct);
                    if (connection is null) return BankApi.Fail(404, "Connection not found.");
                    if (connection.Kind != BankConnectionKind.Bank || string.IsNullOrEmpty(connection.AccountId))
                        return BankApi.Fail(422, "That connection is not a bank account that can pay for an order.");

                    if (request.Items is null || request.Items.Count == 0) return BankApi.Fail(400, "items must not be empty.");
                    if (request.Items.Any(i => i is null || i.Quantity < 1)) return BankApi.Fail(400, "Each item needs a quantity of at least 1.");
                    var shipping = request.ShippingCost ?? 0m;
                    if (!CheckoutDto.ValidMoney(shipping)) return BankApi.Fail(400, "shippingCost must be zero or more, with at most two decimals.");

                    // the price comes from the catalog, never from the caller
                    var wanted = request.Items.GroupBy(i => i.CatalogItemId)
                        .ToDictionary(g => g.Key, g => g.Sum(i => (long)i.Quantity));
                    if (wanted.Values.Any(q => q > int.MaxValue)) return BankApi.Fail(400, "Quantity is too large.");
                    var ids = wanted.Keys.ToList();
                    var catalog = await db.CatalogItems.Where(i => ids.Contains(i.Id)).ToListAsync(ct);
                    var missing = ids.Where(id => catalog.All(c => c.Id != id)).ToList();
                    if (missing.Count > 0) return BankApi.Fail(400, $"Unknown catalog item(s): {string.Join(", ", missing)}.");

                    var checkout = new BankCheckout
                    {
                        BuyerId = buyer,
                        ConnectionId = connection.Id,
                        ShippingCost = shipping,
                        Street = request.ShipTo?.Street ?? "",
                        City = request.ShipTo?.City ?? "",
                        State = request.ShipTo?.State,
                        Country = request.ShipTo?.Country ?? "",
                        ZipCode = request.ShipTo?.ZipCode ?? "",
                        Items = catalog.Select(c => new BankCheckoutItem
                        {
                            CatalogItemId = c.Id,
                            ProductName = c.Name,
                            PictureUri = string.IsNullOrEmpty(c.PictureUri) ? "none" : c.PictureUri,
                            UnitPrice = c.Price,
                            Units = (int)wanted[c.Id]
                        }).ToList()
                    };
                    if (checkout.Total() <= 0) return BankApi.Fail(400, "The order total must be more than zero.");

                    db.BankCheckouts.Add(checkout);
                    await db.SaveChangesAsync(ct);
                    return Results.Created($"api/checkout/bank-payment/{checkout.Id}", CheckoutDto.From(checkout, pricing));
                })
            .WithTags("BankEndpoints");
    }
}

public class ChangeShippingRequest : BaseRequest
{
    public decimal? ShippingCost { get; set; }
}

/// <summary>Changes the shipping cost of a started payment; confirming then charges the new total.</summary>
public class ChangeShippingEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/checkout/bank-payment/{checkoutId}/shipping",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (Guid checkoutId, ChangeShippingRequest? request, ClaimsPrincipal user, CatalogContext db, BankPricing pricing, CancellationToken ct) =>
                {
                    var buyer = BankApi.UserName(user);
                    if (buyer is null) return BankApi.Fail(401, "The token does not identify a user.");
                    if (request?.ShippingCost is null) return BankApi.Fail(400, "shippingCost is required.");
                    if (!CheckoutDto.ValidMoney(request.ShippingCost)) return BankApi.Fail(400, "shippingCost must be zero or more, with at most two decimals.");

                    var checkout = await db.BankCheckouts.Include(c => c.Items)
                        .FirstOrDefaultAsync(c => c.Id == checkoutId && c.BuyerId == buyer, ct);
                    if (checkout is null) return BankApi.Fail(404, "Checkout not found.");
                    if (checkout.Status != BankCheckoutStatus.Pending)
                        return BankApi.Fail(409, "This payment is already being confirmed or is complete, so its shipping can no longer change.");

                    checkout.ShippingCost = request.ShippingCost.Value;
                    checkout.ConcurrencyStamp = Guid.NewGuid();
                    try
                    {
                        await db.SaveChangesAsync(ct);
                    }
                    catch (DbUpdateConcurrencyException)
                    {
                        return BankApi.Fail(409, "The payment changed at the same time; try again.");
                    }
                    return Results.Ok(CheckoutDto.From(checkout, pricing));
                })
            .WithTags("BankEndpoints");
    }
}

public class ConfirmBankPaymentRequest : BaseRequest
{
    public string? Speed { get; set; }
}

/// <summary>Confirms a started payment with one of the offered speeds and charges the shopper through Plaid.</summary>
public class ConfirmBankPaymentEndpoint : IEndpoint
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Locks = new();

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/checkout/bank-payment/{checkoutId}/confirm",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (Guid checkoutId, ConfirmBankPaymentRequest? request, ClaimsPrincipal user, CatalogContext db, PlaidClient plaid, BankPricing pricing, CancellationToken ct) =>
                {
                    var buyer = BankApi.UserName(user);
                    if (buyer is null) return BankApi.Fail(401, "The token does not identify a user.");
                    var speed = BankPricing.NormalizeSpeed(request?.Speed);
                    if (speed is null) return BankApi.Fail(400, "speed must be 'standard' or 'express'.");

                    var gate = Locks.GetOrAdd(checkoutId, _ => new SemaphoreSlim(1, 1));
                    await gate.WaitAsync(ct);
                    try
                    {
                        return await ConfirmAsync(checkoutId, buyer, speed, db, plaid, pricing, ct);
                    }
                    finally
                    {
                        gate.Release();
                        if (Locks.Count > 5000) Locks.Clear();
                    }
                })
            .WithTags("BankEndpoints");
    }

    private static async Task<IResult> ConfirmAsync(Guid checkoutId, string buyer, string speed, CatalogContext db,
        PlaidClient plaid, BankPricing pricing, CancellationToken ct)
    {
        var checkout = await db.BankCheckouts.Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.Id == checkoutId && c.BuyerId == buyer, ct);
        if (checkout is null) return BankApi.Fail(404, "Checkout not found.");

        if (checkout.Status == BankCheckoutStatus.Completed)
        {
            // a repeated confirm must not charge twice: answer with the original confirmation
            var done = await db.BankPayments.FirstOrDefaultAsync(p => p.CheckoutId == checkoutId, ct);
            if (done is not null && done.Speed == speed) return Results.Ok(Confirmation(done));
            return BankApi.Fail(409, "This payment was already confirmed" + (done is null ? "." : $" with the {done.Speed} speed."));
        }
        if (checkout.Status == BankCheckoutStatus.Processing)
            return BankApi.Fail(409, "This payment is already being confirmed.");

        var total = checkout.Total();   // the total now, shipping changes included
        if (!pricing.OptionsFor(total).Contains(speed))
            return BankApi.Fail(422, $"The {speed} speed is not available for an order of this size.");
        var fee = pricing.FeeFor(speed);
        var charge = total + fee;

        var connection = await db.BankConnections.FirstOrDefaultAsync(c => c.Id == checkout.ConnectionId && c.BuyerId == buyer, ct);
        if (connection is null || string.IsNullOrEmpty(connection.AccountId))
            return BankApi.Fail(409, "The bank connection for this payment was disconnected.");

        checkout.Status = BankCheckoutStatus.Processing;
        checkout.ConcurrencyStamp = Guid.NewGuid();
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return BankApi.Fail(409, "The payment changed at the same time; try again.");
        }

        var createdTransfers = new List<BankPaymentTransfer>();
        try
        {
            var network = speed == BankPricing.Express ? "same-day-ach" : "ach";

            // One ACH transfer can carry only so much; a very large order is paid in several, all authorized first.
            var authorizations = new List<(string Id, decimal Amount)>();
            foreach (var part in pricing.SplitCharge(charge))
            {
                var authorization = await plaid.PostAsync("/transfer/authorization/create", new JsonObject
                {
                    ["access_token"] = connection.AccessToken,
                    ["account_id"] = connection.AccountId,
                    ["type"] = "debit",
                    ["network"] = network,
                    ["amount"] = PlaidClient.Money(part),
                    ["ach_class"] = "web",
                    ["user"] = new JsonObject { ["legal_name"] = buyer }
                }, ct);
                var auth = authorization["authorization"];
                var decision = auth.Str("decision");
                if (decision != "approved")
                {
                    var why = auth?["decision_rationale"].Str("description") ?? auth?["decision_rationale"].Str("code") ?? decision;
                    await Revert(db, checkout);
                    return BankApi.Fail(decision == "user_action_required" ? 409 : 422, $"The bank payment was not approved: {why}.");
                }
                authorizations.Add((auth.Str("id")!, part));
            }

            string? arrivesOn = null;
            foreach (var (authorizationId, part) in authorizations)
            {
                var created = await plaid.PostAsync("/transfer/create", new JsonObject
                {
                    ["access_token"] = connection.AccessToken,
                    ["account_id"] = connection.AccountId,
                    ["authorization_id"] = authorizationId,
                    ["description"] = "eShop order"
                }, ct);
                var transfer = created["transfer"];
                createdTransfers.Add(new BankPaymentTransfer { TransferId = transfer.Str("id")!, Amount = part });
                var date = transfer.Str("expected_funds_available_date");
                if (date is not null && (arrivesOn is null || string.CompareOrdinal(date, arrivesOn) > 0)) arrivesOn = date;
            }
            arrivesOn ??= BusinessDays.Add(DateTime.UtcNow.Date, speed == BankPricing.Express ? 0 : 2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            var address = new Address(
                string.IsNullOrWhiteSpace(checkout.Street) ? "Not provided" : checkout.Street,
                string.IsNullOrWhiteSpace(checkout.City) ? "Not provided" : checkout.City,
                checkout.State ?? "",
                string.IsNullOrWhiteSpace(checkout.Country) ? "Not provided" : checkout.Country,
                string.IsNullOrWhiteSpace(checkout.ZipCode) ? "Not provided" : checkout.ZipCode);
            var order = new Order(buyer, address, checkout.Items.Select(i => new OrderItem(
                new CatalogItemOrdered(i.CatalogItemId, i.ProductName, i.PictureUri), i.UnitPrice, i.Units)).ToList());
            var payment = new BankPayment
            {
                Order = order,
                CheckoutId = checkout.Id,
                BuyerId = buyer,
                ConnectionId = connection.Id,
                Transfers = createdTransfers,
                Speed = speed,
                Total = total,
                Fee = fee,
                ArrivesOn = arrivesOn
            };
            db.BankPayments.Add(payment);
            checkout.Status = BankCheckoutStatus.Completed;
            checkout.ConcurrencyStamp = Guid.NewGuid();
            await db.SaveChangesAsync(CancellationToken.None);
            return Results.Ok(Confirmation(payment));
        }
        catch (PlaidException ex)
        {
            await CancelQuietly(plaid, createdTransfers);
            await Revert(db, checkout);
            return BankApi.Fail(ex);
        }
        catch
        {
            // charged but not recorded: undo the charge rather than keep the shopper's money
            await CancelQuietly(plaid, createdTransfers);
            await Revert(db, checkout);
            throw;
        }
    }

    private static async Task CancelQuietly(PlaidClient plaid, List<BankPaymentTransfer> transfers)
    {
        foreach (var t in transfers)
        {
            try { await plaid.PostAsync("/transfer/cancel", new JsonObject { ["transfer_id"] = t.TransferId }); }
            catch (PlaidException) { }
        }
    }
    private static async Task Revert(CatalogContext db, BankCheckout checkout)
    {
        foreach (var entry in db.ChangeTracker.Entries().Where(e => e.Entity is BankPayment or Order or OrderItem).ToList())
        {
            entry.State = EntityState.Detached;
        }
        checkout.Status = BankCheckoutStatus.Pending;
        checkout.ConcurrencyStamp = Guid.NewGuid();
        await db.SaveChangesAsync(CancellationToken.None);
    }

    internal static object Confirmation(BankPayment p) => new
    {
        orderId = p.OrderId,
        checkoutId = p.CheckoutId,
        speed = p.Speed,
        total = p.Total,
        fee = p.Fee,
        charged = p.Charged,
        arrivesOn = p.ArrivesOn
    };
}

/// <summary>US business days (weekends and federal holidays skipped), used only if Plaid gives no date.</summary>
public static class BusinessDays
{
    public static bool IsBusinessDay(DateTime d) =>
        d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !IsFederalHoliday(d);

    public static DateTime Add(DateTime from, int days)
    {
        var d = from;
        while (!IsBusinessDay(d)) d = d.AddDays(1);
        while (days > 0)
        {
            d = d.AddDays(1);
            if (IsBusinessDay(d)) days--;
        }
        return d;
    }

    private static bool IsFederalHoliday(DateTime d)
    {
        bool Observed(int month, int day)
        {
            var h = new DateTime(d.Year, month, day);
            if (h.DayOfWeek == DayOfWeek.Saturday) h = h.AddDays(-1);
            else if (h.DayOfWeek == DayOfWeek.Sunday) h = h.AddDays(1);
            return h.Date == d.Date;
        }
        bool Nth(int month, DayOfWeek dow, int n)
        {
            var first = new DateTime(d.Year, month, 1);
            var offset = ((int)dow - (int)first.DayOfWeek + 7) % 7;
            return first.AddDays(offset + 7 * (n - 1)).Date == d.Date;
        }
        bool Last(int month, DayOfWeek dow)
        {
            var last = new DateTime(d.Year, month, DateTime.DaysInMonth(d.Year, month));
            var offset = ((int)last.DayOfWeek - (int)dow + 7) % 7;
            return last.AddDays(-offset).Date == d.Date;
        }
        return Observed(1, 1) || Nth(1, DayOfWeek.Monday, 3) || Nth(2, DayOfWeek.Monday, 3) || Last(5, DayOfWeek.Monday)
               || Observed(6, 19) || Observed(7, 4) || Nth(9, DayOfWeek.Monday, 1) || Nth(10, DayOfWeek.Monday, 2)
               || Observed(11, 11) || Nth(11, DayOfWeek.Thursday, 4) || Observed(12, 25);
    }
}

/// <summary>Cancels an order paid by bank, before it ships, and refunds everything.</summary>
public class CancelOrderEndpoint : IEndpoint
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> Locks = new();

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (int orderId, ClaimsPrincipal user, CatalogContext db, PlaidClient plaid, CancellationToken ct) =>
                {
                    var buyer = BankApi.UserName(user);
                    if (buyer is null) return BankApi.Fail(401, "The token does not identify a user.");

                    var gate = Locks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
                    await gate.WaitAsync(ct);
                    try
                    {
                        return await CancelAsync(orderId, buyer, db, plaid, ct);
                    }
                    finally
                    {
                        gate.Release();
                        if (Locks.Count > 5000) Locks.Clear();
                    }
                })
            .WithTags("BankEndpoints");
    }

    private static async Task<IResult> CancelAsync(int orderId, string buyer, CatalogContext db, PlaidClient plaid, CancellationToken ct)
    {
        var payment = await db.BankPayments.FirstOrDefaultAsync(p => p.OrderId == orderId && p.BuyerId == buyer, ct);
        if (payment is null)
        {
            var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId && o.BuyerId == buyer, ct);
            return order is null
                ? BankApi.Fail(404, "Order not found.")
                : BankApi.Fail(409, "This order was not paid by bank, so it cannot be cancelled here.");
        }
        if (payment.Status == BankPaymentStatus.Cancelled) return BankApi.Fail(409, "This order is already cancelled.");
        if (payment.ShippedOn is not null) return BankApi.Fail(409, "This order has already shipped and can no longer be cancelled.");

        try
        {
            string? refundId = null;
            foreach (var part in payment.Transfers)
            {
                var transfer = (await plaid.PostAsync("/transfer/get", new JsonObject { ["transfer_id"] = part.TransferId }, ct))["transfer"];
                var status = transfer.Str("status");

                if (status == "pending")
                {
                    try
                    {
                        await plaid.PostAsync("/transfer/cancel", new JsonObject { ["transfer_id"] = part.TransferId }, ct);
                        status = "cancelled";
                    }
                    catch (PlaidException ex) when (ex.ResponseStatus is 409 or 400 or 422)
                    {
                        // it moved on in the meantime: look again and refund instead
                        transfer = (await plaid.PostAsync("/transfer/get", new JsonObject { ["transfer_id"] = part.TransferId }, ct))["transfer"];
                        status = transfer.Str("status");
                    }
                }

                if (status is "posted" or "settled" or "funds_available")
                {
                    var existing = transfer.Arr("refunds").FirstOrDefault(r => r.Str("status") is not ("failed" or "cancelled"));
                    if (existing is not null)
                    {
                        refundId ??= existing.Str("id");
                    }
                    else
                    {
                        var refund = await plaid.PostAsync("/transfer/refund/create", new JsonObject
                        {
                            ["transfer_id"] = part.TransferId,
                            ["amount"] = PlaidClient.Money(part.Amount),   // the whole transfer, fee included
                            ["idempotency_key"] = $"eshop-refund-{payment.Id}-{part.TransferId}"
                        }, ct);
                        refundId ??= refund["refund"].Str("id");
                    }
                }
                else if (status is not ("cancelled" or "failed" or "returned"))
                {
                    return BankApi.Fail(409, $"The payment cannot be cancelled while its transfer is {status}.");
                }
            }
            payment.Status = BankPaymentStatus.Cancelled;
            payment.CancelledOn = DateTimeOffset.UtcNow;
            payment.RefundId = refundId;
            payment.ConcurrencyStamp = Guid.NewGuid();
            await db.SaveChangesAsync(CancellationToken.None);
            return Results.Ok(new
            {
                orderId,
                status = "cancelled",
                refunded = payment.Charged,
                refundId
            });
        }
        catch (PlaidException ex)
        {
            return BankApi.Fail(ex);
        }
        catch (DbUpdateConcurrencyException)
        {
            return BankApi.Fail(409, "The order changed at the same time; try again.");
        }
    }
}
