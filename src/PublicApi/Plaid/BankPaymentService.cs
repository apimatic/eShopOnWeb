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
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

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

public class BankPaymentQuote
{
    public string CheckoutId { get; set; } = string.Empty;
    public decimal ItemsTotal { get; set; }
    public decimal ShippingCost { get; set; }
    public decimal Total { get; set; }
    public decimal ExpressFee { get; set; }
    public List<string> Options { get; set; } = new();
}

public class OrderConfirmation
{
    public int OrderId { get; set; }
    public string CheckoutId { get; set; } = string.Empty;
    public string Speed { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public decimal Fee { get; set; }
    public decimal Charged { get; set; }
    public string? ArrivesOn { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class OrderCancellation
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Refunded { get; set; }
}

public class BankPaymentService
{
    public const string Standard = "standard";
    public const string Express = "express";

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly PlaidClient _plaid;
    private readonly CatalogContext _db;
    private readonly BankConnectionService _connections;
    private readonly decimal _expressFee;

    public BankPaymentService(PlaidClient plaid, CatalogContext db, BankConnectionService connections, IConfiguration configuration)
    {
        _plaid = plaid;
        _db = db;
        _connections = connections;
        _expressFee = decimal.TryParse(configuration["BankPayment:ExpressFee"], NumberStyles.Number, CultureInfo.InvariantCulture, out var fee)
            ? Math.Round(fee, 2) : 5.00m;
    }

    private sealed record ItemSnapshot(int CatalogItemId, string Name, string PictureUri, decimal UnitPrice, int Units);

    private sealed record Authorization(bool Approved, string? Id, string Reason);

    private static async Task<IDisposable> LockAsync(string key, CancellationToken ct)
    {
        var gate = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        return new Releaser(gate);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _gate;
        public Releaser(SemaphoreSlim gate) => _gate = gate;
        public void Dispose() => _gate.Release();
    }

    private static string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal ValidateShipping(decimal? shippingCost)
    {
        if (shippingCost is null || shippingCost < 0)
        {
            throw ApiProblemException.BadRequest("shippingCost is required and cannot be negative.");
        }

        if (decimal.Round(shippingCost.Value, 2) != shippingCost.Value)
        {
            throw ApiProblemException.BadRequest("shippingCost cannot have more than two decimal places.");
        }

        return shippingCost.Value;
    }

    // ---------------------------------------------------------------- start

    public async Task<BankPaymentQuote> StartAsync(string buyer, int? connectionId, List<CheckoutItemRequest>? items,
        decimal? shippingCost, ShipToRequest? shipTo, CancellationToken ct)
    {
        if (connectionId is null)
        {
            throw ApiProblemException.BadRequest("connectionId is required.");
        }

        var shipping = ValidateShipping(shippingCost);
        if (items is null || items.Count == 0)
        {
            throw ApiProblemException.BadRequest("items must contain at least one item.");
        }

        if (items.Any(i => i is null || i.Quantity <= 0))
        {
            throw ApiProblemException.BadRequest("Every item needs a quantity greater than zero.");
        }

        var connection = await _connections.GetOwnedAsync(buyer, connectionId.Value, ct);
        if (connection.Type != BankConnectionTypes.Bank || string.IsNullOrEmpty(connection.AccountId))
        {
            throw ApiProblemException.BadRequest("Only a connected bank account can pay for an order.");
        }

        // Merge repeated lines and price everything from the catalog, never from the request.
        var wanted = items.GroupBy(i => i.CatalogItemId).Select(g => (Id: g.Key, Units: checked(g.Sum(i => i.Quantity)))).ToList();
        var ids = wanted.Select(w => w.Id).ToList();
        var catalog = await _db.CatalogItems.Where(c => ids.Contains(c.Id)).ToListAsync(ct);
        var missing = ids.Where(id => catalog.All(c => c.Id != id)).ToList();
        if (missing.Count > 0)
        {
            throw ApiProblemException.BadRequest($"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var snapshots = wanted.Select(w =>
        {
            var c = catalog.First(x => x.Id == w.Id);
            return new ItemSnapshot(c.Id, c.Name, string.IsNullOrEmpty(c.PictureUri) ? "eCatalog-item-default.png" : c.PictureUri, c.Price, w.Units);
        }).ToList();

        var checkout = new BankCheckout
        {
            BuyerId = BankConnectionService.NormalizeBuyer(buyer),
            ConnectionId = connection.Id,
            ItemsJson = JsonSerializer.Serialize(snapshots),
            ShipToJson = shipTo is null ? null : JsonSerializer.Serialize(shipTo),
            ShippingCost = shipping
        };

        var itemsTotal = snapshots.Sum(s => s.UnitPrice * s.Units);
        if (itemsTotal + shipping <= 0)
        {
            throw ApiProblemException.BadRequest("The order total must be greater than zero.");
        }

        var quote = await QuoteAsync(buyer, connection, checkout, ct);
        _db.BankCheckouts.Add(checkout);
        await _db.SaveChangesAsync(ct);
        return quote;
    }

    private decimal ItemsTotal(BankCheckout checkout) =>
        JsonSerializer.Deserialize<List<ItemSnapshot>>(checkout.ItemsJson, Json)!.Sum(s => s.UnitPrice * s.Units);

    /// <summary>Asks Plaid which payment speeds it will authorize for this order.</summary>
    private async Task<BankPaymentQuote> QuoteAsync(string buyer, BankConnection connection, BankCheckout checkout, CancellationToken ct)
    {
        var itemsTotal = ItemsTotal(checkout);
        var total = itemsTotal + checkout.ShippingCost;
        var achClass = await GetAchClassAsync(connection, ct);

        var standard = AuthorizeAsync(connection, achClass, "ach", total, buyer, ct);
        var express = AuthorizeAsync(connection, achClass, "same-day-ach", total + _expressFee, buyer, ct);
        await Task.WhenAll(standard, express);

        var options = new List<string>();
        if (standard.Result.Approved) options.Add(Standard);
        if (express.Result.Approved) options.Add(Express);

        if (options.Count == 0)
        {
            throw ApiProblemException.Unprocessable(
                $"This order cannot be paid by bank: {standard.Result.Reason}");
        }

        return new BankPaymentQuote
        {
            CheckoutId = checkout.Id,
            ItemsTotal = itemsTotal,
            ShippingCost = checkout.ShippingCost,
            Total = total,
            ExpressFee = _expressFee,
            Options = options
        };
    }

    // ------------------------------------------------------------- shipping

    public async Task<BankPaymentQuote> ChangeShippingAsync(string buyer, string checkoutId, decimal? shippingCost, CancellationToken ct)
    {
        var shipping = ValidateShipping(shippingCost);
        using var _ = await LockAsync("checkout:" + checkoutId, ct);

        var checkout = await GetOwnedCheckoutAsync(buyer, checkoutId, ct);
        if (checkout.Status != BankCheckoutStatus.Open)
        {
            throw ApiProblemException.Conflict("This checkout is already confirmed; its shipping can no longer change.");
        }

        var connection = await GetCheckoutConnectionAsync(buyer, checkout, ct);
        if (ItemsTotal(checkout) + shipping <= 0)
        {
            throw ApiProblemException.BadRequest("The order total must be greater than zero.");
        }

        checkout.ShippingCost = shipping;
        var quote = await QuoteAsync(buyer, connection, checkout, ct);
        await _db.SaveChangesAsync(ct);
        return quote;
    }

    private async Task<BankCheckout> GetOwnedCheckoutAsync(string buyer, string checkoutId, CancellationToken ct)
    {
        var id = BankConnectionService.NormalizeBuyer(buyer);
        var checkout = await _db.BankCheckouts.FirstOrDefaultAsync(c => c.Id == checkoutId && c.BuyerId == id, ct);
        return checkout ?? throw ApiProblemException.NotFound($"Checkout {checkoutId} was not found.");
    }

    private async Task<BankConnection> GetCheckoutConnectionAsync(string buyer, BankCheckout checkout, CancellationToken ct)
    {
        try
        {
            return await _connections.GetOwnedAsync(buyer, checkout.ConnectionId, ct);
        }
        catch (ApiProblemException)
        {
            throw ApiProblemException.Conflict("The bank for this checkout was disconnected; start the checkout again with another bank.");
        }
    }

    // -------------------------------------------------------------- confirm

    public async Task<OrderConfirmation> ConfirmAsync(string buyer, string checkoutId, string? speed, CancellationToken ct)
    {
        var chosen = (speed ?? string.Empty).Trim().ToLowerInvariant();
        if (chosen != Standard && chosen != Express)
        {
            throw ApiProblemException.BadRequest("speed must be 'standard' or 'express'.");
        }

        using var _ = await LockAsync("checkout:" + checkoutId, ct);

        var checkout = await GetOwnedCheckoutAsync(buyer, checkoutId, ct);
        if (checkout.Status == BankCheckoutStatus.Completed)
        {
            // Confirming twice must never charge twice: answer with the existing confirmation.
            var done = await _db.BankPayments.FirstAsync(p => p.CheckoutId == checkout.Id, ct);
            return ToConfirmation(done, checkout.Id);
        }

        var connection = await GetCheckoutConnectionAsync(buyer, checkout, ct);
        var snapshots = JsonSerializer.Deserialize<List<ItemSnapshot>>(checkout.ItemsJson, Json)!;
        var itemsTotal = snapshots.Sum(s => s.UnitPrice * s.Units);
        var total = itemsTotal + checkout.ShippingCost; // the shipping cost as it is now, not as it was at the start
        var fee = chosen == Express ? _expressFee : 0m;
        var charge = total + fee;

        var achClass = await GetAchClassAsync(connection, ct);
        var authorization = await AuthorizeAsync(connection, achClass, chosen == Express ? "same-day-ach" : "ach", charge, buyer, ct);
        if (!authorization.Approved)
        {
            throw ApiProblemException.Unprocessable($"{chosen} payment is not available for this order: {authorization.Reason}");
        }

        var transferJson = await _plaid.PostAsync("/transfer/create", new
        {
            access_token = connection.AccessToken,
            account_id = connection.AccountId,
            authorization_id = authorization.Id,
            description = "eShop order"
        }, ct);
        var transfer = transferJson["transfer"]!;
        var transferId = transfer["id"]!.GetValue<string>();

        Order? order = null;
        try
        {
            var address = ParseAddress(checkout.ShipToJson);
            order = new Order(buyer, address, snapshots
                .Select(s => new OrderItem(new CatalogItemOrdered(s.CatalogItemId, s.Name, s.PictureUri), s.UnitPrice, s.Units))
                .ToList());
            _db.Orders.Add(order);
            await _db.SaveChangesAsync(ct);

            var payment = new BankPayment
            {
                OrderId = order.Id,
                BuyerId = checkout.BuyerId,
                CheckoutId = checkout.Id,
                TransferId = transferId,
                Speed = chosen,
                ItemsTotal = itemsTotal,
                ShippingCost = checkout.ShippingCost,
                Fee = fee,
                Amount = charge,
                ArrivesOn = ParseArrival(transfer, chosen)
            };
            _db.BankPayments.Add(payment);
            checkout.Status = BankCheckoutStatus.Completed;
            checkout.OrderId = order.Id;
            await _db.SaveChangesAsync(ct);
            return ToConfirmation(payment, checkout.Id);
        }
        catch
        {
            // The shopper must not be charged for an order that was not recorded.
            try { await _plaid.PostAsync("/transfer/cancel", new { transfer_id = transferId }, CancellationToken.None); } catch (Exception) { }
            if (order is { Id: > 0 })
            {
                _db.ChangeTracker.Clear();
                try
                {
                    _db.Orders.Remove((await _db.Orders.Include(o => o.OrderItems).FirstAsync(o => o.Id == order.Id, CancellationToken.None)));
                    await _db.SaveChangesAsync(CancellationToken.None);
                }
                catch (Exception) { }
            }
            throw;
        }
    }

    private static Address ParseAddress(string? json)
    {
        var ship = json is null ? null : JsonSerializer.Deserialize<ShipToRequest>(json, Json);
        string Or(string? value) => string.IsNullOrWhiteSpace(value) ? "N/A" : value.Trim();
        return new Address(Or(ship?.Street), Or(ship?.City), Or(ship?.State), Or(ship?.Country), Or(ship?.ZipCode));
    }

    private static DateTime? ParseArrival(JsonNode transfer, string speed)
    {
        var text = transfer["expected_funds_available_date"]?.GetValue<string>();
        if (DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        // Plaid did not say: standard ACH lands in about three business days, same-day ACH the same day.
        return AddBusinessDays(DateTime.UtcNow.Date, speed == Express ? 0 : 3);
    }

    private static DateTime AddBusinessDays(DateTime from, int days)
    {
        var date = from;
        while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) date = date.AddDays(1);
        while (days > 0)
        {
            date = date.AddDays(1);
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) days--;
        }

        return date;
    }

    private static OrderConfirmation ToConfirmation(BankPayment p, string checkoutId) => new()
    {
        OrderId = p.OrderId,
        CheckoutId = checkoutId,
        Speed = p.Speed,
        Total = p.ItemsTotal + p.ShippingCost,
        Fee = p.Fee,
        Charged = p.Amount,
        ArrivesOn = p.ArrivesOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Status = p.Status
    };

    // --------------------------------------------------------------- cancel

    public async Task<OrderCancellation> CancelOrderAsync(string buyer, int orderId, CancellationToken ct)
    {
        using var _ = await LockAsync("order:" + orderId, ct);

        var buyerId = BankConnectionService.NormalizeBuyer(buyer);
        var payment = await _db.BankPayments.FirstOrDefaultAsync(p => p.OrderId == orderId && p.BuyerId == buyerId, ct)
            ?? throw ApiProblemException.NotFound($"Order {orderId} was not found.");

        if (payment.Status == BankPaymentStatus.Cancelled)
        {
            throw ApiProblemException.Conflict("This order is already cancelled.");
        }

        if (payment.ShippedOn is not null)
        {
            throw ApiProblemException.Conflict("This order has shipped and can no longer be cancelled.");
        }

        var transfer = (await _plaid.PostAsync("/transfer/get", new { transfer_id = payment.TransferId }, ct))["transfer"]!;
        var status = transfer["status"]?.GetValue<string>();
        var refunded = 0m;

        if (status is "cancelled" or "failed" or "returned")
        {
            // The money never left the shopper's account (or already came back).
        }
        else
        {
            var cancelled = false;
            if (transfer["cancellable"]?.GetValue<bool>() == true)
            {
                try
                {
                    await _plaid.PostAsync("/transfer/cancel", new { transfer_id = payment.TransferId }, ct);
                    cancelled = true;
                    refunded = payment.Amount;
                }
                catch (PlaidApiException ex) when (ex.ErrorCode == "TRANSFER_NOT_CANCELLABLE")
                {
                    // It moved on in the meantime; refund it instead.
                }
            }

            if (!cancelled)
            {
                var alreadyRefunded = (transfer["refunds"]?.AsArray() ?? new JsonArray())
                    .Where(r => r!["status"]?.GetValue<string>() is not ("failed" or "cancelled"))
                    .Sum(r => decimal.Parse(r!["amount"]!.GetValue<string>(), CultureInfo.InvariantCulture));
                var remaining = payment.Amount - alreadyRefunded;
                if (remaining > 0)
                {
                    var refund = await _plaid.PostAsync("/transfer/refund/create", new
                    {
                        transfer_id = payment.TransferId,
                        amount = Money(remaining),
                        idempotency_key = $"order-{payment.OrderId}-cancel"
                    }, ct);
                    payment.RefundId = refund["refund"]?["id"]?.GetValue<string>();
                }

                refunded = payment.Amount;
            }
        }

        payment.Status = BankPaymentStatus.Cancelled;
        payment.CancelledOn = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new OrderCancellation { OrderId = orderId, Status = payment.Status, Refunded = refunded };
    }

    /// <summary>Marks a paid order as shipped (staff only); from then on it cannot be cancelled.</summary>
    public async Task ShipOrderAsync(int orderId, CancellationToken ct)
    {
        using var _ = await LockAsync("order:" + orderId, ct);
        var payment = await _db.BankPayments.FirstOrDefaultAsync(p => p.OrderId == orderId, ct)
            ?? throw ApiProblemException.NotFound($"Order {orderId} was not found.");
        if (payment.Status == BankPaymentStatus.Cancelled)
        {
            throw ApiProblemException.Conflict("A cancelled order cannot ship.");
        }

        payment.ShippedOn ??= DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------- plaid

    private async Task<string> GetAchClassAsync(BankConnection connection, CancellationToken ct)
    {
        var json = await _plaid.PostAsync("/accounts/get", new
        {
            access_token = connection.AccessToken,
            options = new { account_ids = new[] { connection.AccountId } }
        }, ct);
        var account = json["accounts"]?.AsArray().FirstOrDefault()
            ?? throw ApiProblemException.Conflict("The connected bank account is no longer available; reconnect the bank.");
        // Business accounts (wholesale buyers) are debited as corporate ACH.
        return account["holder_category"]?.GetValue<string>() == "business" ? "ccd" : "ppd";
    }

    private async Task<Authorization> AuthorizeAsync(BankConnection connection, string achClass, string network, decimal amount,
        string legalName, CancellationToken ct)
    {
        try
        {
            var json = await _plaid.PostAsync("/transfer/authorization/create", new
            {
                access_token = connection.AccessToken,
                account_id = connection.AccountId,
                type = "debit",
                network,
                amount = Money(amount),
                ach_class = achClass,
                user = new { legal_name = legalName }
            }, ct);
            var auth = json["authorization"]!;
            if (auth["decision"]?.GetValue<string>() == "approved")
            {
                return new Authorization(true, auth["id"]!.GetValue<string>(), string.Empty);
            }

            var rationale = auth["decision_rationale"]?["description"]?.GetValue<string>() ?? "Plaid declined the transfer.";
            return new Authorization(false, null, rationale);
        }
        catch (PlaidApiException ex) when (ex.ErrorType == "TRANSFER_ERROR")
        {
            // For example TRANSFER_NETWORK_LIMIT_EXCEEDED: this speed is not available for this amount.
            return new Authorization(false, null, ex.Message);
        }
    }
}
