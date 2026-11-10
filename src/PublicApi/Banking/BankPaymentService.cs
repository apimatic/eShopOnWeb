using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Banking.Plaid;

namespace Microsoft.eShopWeb.PublicApi.Banking;

/// <summary>"BankPayment:" configuration section.</summary>
public class BankPaymentOptions
{
    public const string SectionName = "BankPayment";

    /// <summary>Fee the shopper pays for express (same business day) payment.</summary>
    public decimal ExpressFee { get; set; } = 5.00m;

    /// <summary>The Same-Day ACH network's limit for a single payment.</summary>
    public decimal ExpressLimit { get; set; } = 1_000_000m;

    /// <summary>The ACH network's limit for a single standard transfer; larger payments are split into several.</summary>
    public decimal AchLimit { get; set; } = 10_000_000m;
}

public record CheckoutLine(int CatalogItemId, int Quantity);
public record ShipToInput(string? Street, string? City, string? State, string? Country, string? ZipCode);
public record StartBankPaymentRequest(string? ConnectionId, List<CheckoutLine>? Items, decimal? ShippingCost, string? AccountId, ShipToInput? ShipToAddress);
public record ChangeShippingRequest(decimal? ShippingCost);
public record ConfirmBankPaymentRequest(string? Speed);

public record BankPaymentQuote(string CheckoutId, string ConnectionId, string AccountId, decimal Subtotal, decimal ShippingCost,
    decimal Total, decimal ExpressFee, string Currency, IReadOnlyList<string> Options);

public record OrderConfirmation(int OrderId, string CheckoutId, string Status, string Speed, decimal Total, decimal Fee,
    decimal AmountCharged, string Currency, string ArrivesOn, string TransferId);

public record CancelOrderResponse(int OrderId, string Status, decimal Refunded, string Currency);

/// <summary>
/// Pay-by-bank checkout over Plaid Transfer: quote, shipping changes, charge, and cancel/refund.
/// </summary>
public class BankPaymentService
{
    public const string Standard = "standard";
    public const string Express = "express";

    private class StoredLine
    {
        public int CatalogItemId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string PictureUri { get; set; } = string.Empty;
        public decimal UnitPrice { get; set; }
        public int Units { get; set; }
    }

    private class Charge
    {
        public decimal Amount { get; set; }
        public string Network { get; set; } = string.Empty;
        public string? AuthorizationId { get; set; }
        public string? TransferId { get; set; }
    }

    private readonly CatalogContext _db;
    private readonly PlaidClient _plaid;
    private readonly BankConnectionService _connections;
    private readonly IUriComposer _uriComposer;
    private readonly KeyedLocks _locks;
    private readonly BankPaymentOptions _options;
    private readonly TimeProvider _clock;

    public BankPaymentService(CatalogContext db, PlaidClient plaid, BankConnectionService connections, IUriComposer uriComposer,
        KeyedLocks locks, IOptions<BankPaymentOptions> options, TimeProvider clock)
    {
        _db = db;
        _plaid = plaid;
        _connections = connections;
        _uriComposer = uriComposer;
        _locks = locks;
        _options = options.Value;
        _clock = clock;
    }

    // ---------------------------------------------------------------- start

    public async Task<BankPaymentQuote> StartAsync(string shopperId, StartBankPaymentRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ConnectionId))
        {
            throw BankApiException.BadRequest("connectionId is required.");
        }
        if (request.Items is null || request.Items.Count == 0)
        {
            throw BankApiException.BadRequest("items must list at least one item.");
        }
        var shipping = ValidateMoney(request.ShippingCost ?? 0m, "shippingCost");

        var connection = await _connections.FindOwnedAsync(shopperId, request.ConnectionId, ct)
            ?? throw BankApiException.NotFound("No such connection.");
        if (connection.Kind != ConnectionKind.Bank)
        {
            throw BankApiException.BadRequest("That connection is not a bank account; connect a bank to pay by bank.");
        }

        var lines = await PriceItemsAsync(request.Items, ct);
        decimal subtotal;
        try
        {
            subtotal = lines.Sum(l => l.UnitPrice * l.Units);
            _ = subtotal + shipping + _options.ExpressFee;
        }
        catch (OverflowException)
        {
            throw BankApiException.BadRequest("The order total is too large.");
        }

        var accountId = await ChooseAccountAsync(connection, request.AccountId, ct);

        var checkout = new BankCheckout
        {
            ShopperId = shopperId,
            ConnectionId = connection.Id,
            AccountId = accountId,
            ItemsJson = JsonSerializer.Serialize(lines),
            ShipToJson = request.ShipToAddress is null ? null : JsonSerializer.Serialize(request.ShipToAddress),
            Subtotal = subtotal,
            ShippingCost = shipping,
            Status = BankCheckoutStatus.Started
        };
        if (subtotal + shipping <= 0m)
        {
            throw BankApiException.BadRequest("There is nothing to pay for.");
        }
        _db.BankCheckouts.Add(checkout);
        await _db.SaveChangesAsync(ct);
        return Quote(checkout, connection.PublicId);
    }

    private async Task<List<StoredLine>> PriceItemsAsync(List<CheckoutLine> items, CancellationToken ct)
    {
        if (items.Any(i => i is null))
        {
            throw BankApiException.BadRequest("items must not contain empty entries.");
        }
        if (items.Any(i => i.Quantity < 1))
        {
            throw BankApiException.BadRequest("Every item needs a quantity of at least 1.");
        }

        // prices always come from the catalog, never from the caller
        var merged = items.GroupBy(i => i.CatalogItemId)
            .Select(g => new { Id = g.Key, Quantity = g.Sum(x => (long)x.Quantity) }).ToList();
        if (merged.Any(m => m.Quantity > int.MaxValue))
        {
            throw BankApiException.BadRequest("The quantity of an item is too large.");
        }
        var ids = merged.Select(m => m.Id).ToList();
        var catalog = await _db.CatalogItems.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var missing = ids.Where(id => !catalog.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            throw BankApiException.BadRequest("Unknown catalog item(s): " + string.Join(", ", missing));
        }

        return merged.Select(m =>
        {
            var item = catalog[m.Id];
            return new StoredLine
            {
                CatalogItemId = item.Id,
                Name = item.Name,
                PictureUri = _uriComposer.ComposePicUri(item.PictureUri ?? string.Empty),
                UnitPrice = item.Price,
                Units = (int)m.Quantity
            };
        }).ToList();
    }

    private async Task<string> ChooseAccountAsync(BankConnection connection, string? requested, CancellationToken ct)
    {
        var accounts = (await _plaid.PostAsync<PlaidAccountsResponse>("accounts/get",
            new { access_token = _connections.AccessTokenOf(connection) }, ct)).Accounts
            .Where(a => a.Type == "depository" && a.Subtype is "checking" or "savings").ToList();

        if (!string.IsNullOrWhiteSpace(requested))
        {
            return accounts.FirstOrDefault(a => a.AccountId == requested.Trim())?.AccountId
                ?? throw BankApiException.BadRequest("accountId is not a checking or savings account of that connection.");
        }
        var chosen = accounts.FirstOrDefault(a => a.Subtype == "checking") ?? accounts.FirstOrDefault();
        return chosen?.AccountId
            ?? throw BankApiException.Conflict("That connection has no checking or savings account to pay from.");
    }

    // ---------------------------------------------------------------- shipping

    public async Task<BankPaymentQuote> ChangeShippingAsync(string shopperId, string checkoutId, ChangeShippingRequest request, CancellationToken ct)
    {
        if (request.ShippingCost is null)
        {
            throw BankApiException.BadRequest("shippingCost is required.");
        }
        var shipping = ValidateMoney(request.ShippingCost.Value, "shippingCost");

        using var _ = await LockCheckoutAsync(checkoutId, ct);
        var checkout = await FindCheckoutAsync(shopperId, checkoutId, ct);
        if (checkout.Status != BankCheckoutStatus.Started)
        {
            throw BankApiException.Conflict(checkout.Status == BankCheckoutStatus.Abandoned
                ? "This checkout was abandoned because its bank was disconnected."
                : "This checkout has already been paid; the shipping can no longer change.");
        }
        if (checkout.Subtotal + shipping <= 0m)
        {
            throw BankApiException.BadRequest("There is nothing to pay for.");
        }

        checkout.ShippingCost = shipping;
        await _db.SaveChangesAsync(ct);
        var connection = await _db.BankConnections.AsNoTracking().FirstAsync(c => c.Id == checkout.ConnectionId, ct);
        return Quote(checkout, connection.PublicId);
    }

    // ---------------------------------------------------------------- confirm

    public async Task<OrderConfirmation> ConfirmAsync(string shopperId, string checkoutId, ConfirmBankPaymentRequest request, CancellationToken ct)
    {
        var speed = request.Speed?.Trim().ToLowerInvariant();
        if (speed is not (Standard or Express))
        {
            throw BankApiException.BadRequest("speed must be 'standard' or 'express'.");
        }

        using var _ = await LockCheckoutAsync(checkoutId, ct);
        var checkout = await FindCheckoutAsync(shopperId, checkoutId, ct);

        switch (checkout.Status)
        {
            case BankCheckoutStatus.Confirmed:
                if (checkout.Speed != speed)
                {
                    throw BankApiException.Conflict($"This checkout was already confirmed with {checkout.Speed} speed.");
                }
                return Confirmation(checkout);
            case BankCheckoutStatus.Cancelled:
                throw BankApiException.Conflict("This order was cancelled.");
            case BankCheckoutStatus.Abandoned:
                throw BankApiException.Conflict("This checkout was abandoned because its bank was disconnected.");
            case BankCheckoutStatus.Charged:
                if (checkout.Speed != speed)
                {
                    throw BankApiException.Conflict($"The payment was already made with {checkout.Speed} speed.");
                }
                break;
            default:
                await ChargeAsync(checkout, speed, shopperId, ct);
                break;
        }

        await RecordOrderAsync(checkout, shopperId, ct);
        return Confirmation(checkout);
    }

    private async Task ChargeAsync(BankCheckout checkout, string speed, string shopperId, CancellationToken ct)
    {
        var connection = await _db.BankConnections.FirstOrDefaultAsync(c => c.Id == checkout.ConnectionId, ct)
            ?? throw BankApiException.Conflict("The bank for this checkout is no longer connected.");

        // availability is decided on the current total, which may have changed since the payment was started
        var options = OptionsFor(checkout.Subtotal + checkout.ShippingCost);
        if (!options.Contains(speed))
        {
            throw BankApiException.BadRequest($"'{speed}' is not available for this order; available: {string.Join(", ", options)}.");
        }

        var fee = speed == Express ? _options.ExpressFee : 0m;
        var total = checkout.Subtotal + checkout.ShippingCost;
        var amount = total + fee;
        var network = speed == Express ? "same-day-ach" : "ach";
        var accessToken = _connections.AccessTokenOf(connection);

        // One transfer, or several when the amount is beyond what a single ACH transfer may carry.
        var expected = Split(amount, network);
        var plan = LoadCharges(checkout);
        if (plan is null || plan.Count != expected.Count
            || plan.Where((c, i) => c.Amount != expected[i].Amount || c.Network != network).Any())
        {
            plan = expected;
        }

        // 1. authorize every part first, so a decline leaves the shopper's account untouched
        PlaidTransfer? firstTransfer = null;
        foreach (var charge in plan.Where(c => c.AuthorizationId is null))
        {
            var legalName = await LegalNameAsync(accessToken, checkout, shopperId, ct);
            var auth = (await _plaid.PostAsync<PlaidAuthorizationResponse>("transfer/authorization/create", new
            {
                access_token = accessToken,
                account_id = checkout.AccountId,
                type = "debit",
                network,
                amount = Money(charge.Amount),
                ach_class = "web",
                user = new { legal_name = legalName }
            }, ct)).Authorization;

            if (!string.Equals(auth.Decision, "approved", StringComparison.OrdinalIgnoreCase))
            {
                throw BankApiException.Unprocessable(DeclineReason(auth));
            }
            charge.AuthorizationId = auth.Id;
            checkout.ChargesJson = JsonSerializer.Serialize(plan); // a retry after an uncertain failure never authorizes twice
            await _db.SaveChangesAsync(ct);
        }

        // 2. create the transfers (Plaid answers a repeated authorization with the same transfer)
        var transfers = new List<PlaidTransfer>();
        try
        {
            foreach (var charge in plan)
            {
                var transfer = (await _plaid.PostAsync<PlaidTransferResponse>("transfer/create", new
                {
                    access_token = accessToken,
                    account_id = checkout.AccountId,
                    authorization_id = charge.AuthorizationId,
                    description = "eShop order"
                }, ct)).Transfer;
                charge.TransferId = transfer.Id;
                transfers.Add(transfer);
                firstTransfer ??= transfer;
                checkout.ChargesJson = JsonSerializer.Serialize(plan);
                checkout.TransferId = plan[0].TransferId;
                await _db.SaveChangesAsync(ct);
            }
        }
        catch (PlaidException) when (plan.Count > 1 && transfers.Count > 0)
        {
            // do not leave the shopper charged for only part of the order
            foreach (var done in transfers)
            {
                try { await _plaid.PostAsync<PlaidEmpty>("transfer/cancel", new { transfer_id = done.Id }, ct); }
                catch (PlaidException) { /* best effort */ }
            }
            checkout.ChargesJson = null;
            checkout.TransferId = null;
            await _db.SaveChangesAsync(ct);
            throw;
        }
        var transfer0 = firstTransfer!;

        var today = BusinessCalendar.TodayEastern(_clock);
        var expressArrival = BusinessCalendar.OnOrAfter(today);
        DateTime arrives;
        if (speed == Express)
        {
            arrives = expressArrival; // same business day
        }
        else
        {
            var fromPlaid = DateTime.TryParseExact(transfer0.ExpectedFundsAvailableDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var d) ? d : BusinessCalendar.AddBusinessDays(today, 2);
            arrives = fromPlaid > expressArrival ? fromPlaid : BusinessCalendar.AddBusinessDays(expressArrival, 1);
        }

        checkout.Speed = speed;
        checkout.Fee = fee;
        checkout.ChargedAmount = amount;
        checkout.TransferId = plan[0].TransferId;
        checkout.ArrivesOn = arrives;
        checkout.Status = BankCheckoutStatus.Charged;
        await _db.SaveChangesAsync(ct);
    }

    private async Task RecordOrderAsync(BankCheckout checkout, string shopperId, CancellationToken ct)
    {
        var lines = JsonSerializer.Deserialize<List<StoredLine>>(checkout.ItemsJson) ?? new();
        var items = lines.Select(l => new OrderItem(
            new CatalogItemOrdered(l.CatalogItemId, l.Name, string.IsNullOrEmpty(l.PictureUri) ? "placeholder.png" : l.PictureUri),
            l.UnitPrice, l.Units)).ToList();

        var ship = checkout.ShipToJson is null ? null : JsonSerializer.Deserialize<ShipToInput>(checkout.ShipToJson);
        string Or(string? v) => string.IsNullOrWhiteSpace(v) ? "Not provided" : v.Trim();
        var address = new Address(Or(ship?.Street), Or(ship?.City), ship?.State?.Trim() ?? string.Empty, Or(ship?.Country), Or(ship?.ZipCode));

        var order = new Order(checkout.ShopperId, address, items);
        checkout.Order = order;
        checkout.Status = BankCheckoutStatus.Confirmed;
        checkout.ConfirmedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private List<Charge> Split(decimal amount, string network)
    {
        var limit = network == "ach" ? _options.AchLimit : decimal.MaxValue;
        var charges = new List<Charge>();
        var left = amount;
        while (left > 0m)
        {
            var part = Math.Min(left, limit);
            charges.Add(new Charge { Amount = part, Network = network });
            left -= part;
        }
        return charges;
    }

    private static List<Charge>? LoadCharges(BankCheckout checkout) =>
        string.IsNullOrEmpty(checkout.ChargesJson) ? null : JsonSerializer.Deserialize<List<Charge>>(checkout.ChargesJson);

    private async Task<string> LegalNameAsync(string accessToken, BankCheckout checkout, string shopperId, CancellationToken ct)
    {
        try
        {
            var identity = await _plaid.PostAsync<PlaidIdentityResponse>("identity/get",
                new { access_token = accessToken, options = new { account_ids = new[] { checkout.AccountId } } }, ct, rateLimitRetries: 0);
            var name = identity.Accounts.SelectMany(a => a.Owners ?? new()).SelectMany(o => o.Names ?? new()).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
            if (name is not null) return name.Trim();
        }
        catch (PlaidException)
        {
            // fall back to the shopper's account name below
        }
        var local = shopperId.Split('@')[0];
        return string.IsNullOrWhiteSpace(local) ? shopperId : local;
    }

    private static string DeclineReason(PlaidAuthorization auth)
    {
        var code = auth.DecisionRationale?.Code;
        var description = auth.DecisionRationale?.Description;
        var friendly = code switch
        {
            "NSF" => "Insufficient funds in the selected account.",
            "RISK" => "The payment was declined as too risky.",
            "TRANSFER_LIMIT_REACHED" => "The payment exceeds the limits for this account.",
            "MANUALLY_VERIFIED_ITEM" or "ITEM_LOGIN_REQUIRED" => "The bank needs the shopper to reconnect the account.",
            "ERROR" => "The bank could not verify the account.",
            _ => null
        };
        var reason = friendly ?? description ?? "The bank payment was declined.";
        return code is null ? reason : $"{reason} ({code})";
    }

    // ---------------------------------------------------------------- cancel

    public async Task<CancelOrderResponse> CancelAsync(string shopperId, int orderId, CancellationToken ct)
    {
        var checkout = await _db.BankCheckouts.FirstOrDefaultAsync(c => c.OrderId == orderId, ct);
        if (checkout is null)
        {
            var order = await _db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, ct);
            if (order is null || Shopper.Normalize(order.BuyerId) != shopperId)
            {
                throw BankApiException.NotFound("No such order.");
            }
            throw BankApiException.Conflict("This order was not paid by bank, so it cannot be cancelled here.");
        }
        if (checkout.ShopperId != shopperId)
        {
            throw BankApiException.NotFound("No such order.");
        }

        using var _ = await LockCheckoutAsync(checkout.PublicId.ToString(), ct);
        await _db.Entry(checkout).ReloadAsync(ct);

        if (checkout.Status == BankCheckoutStatus.Cancelled)
        {
            throw BankApiException.Conflict("This order was already cancelled.");
        }
        if (checkout.Status != BankCheckoutStatus.Confirmed || checkout.TransferId is null)
        {
            throw BankApiException.Conflict("This order has no completed bank payment to cancel.");
        }
        if (checkout.ShippedAt is not null)
        {
            throw BankApiException.Conflict("This order has already shipped and can no longer be cancelled.");
        }

        var refunded = await ReturnMoneyAsync(checkout, ct);

        checkout.Status = BankCheckoutStatus.Cancelled;
        checkout.CancelledAt = DateTime.UtcNow;
        checkout.RefundedAmount = refunded;
        await _db.SaveChangesAsync(ct);
        return new CancelOrderResponse(orderId, "cancelled", refunded, checkout.Currency);
    }

    /// <summary>Gives the shopper back everything charged (order total and any express fee); returns the amount.</summary>
    private async Task<decimal> ReturnMoneyAsync(BankCheckout checkout, CancellationToken ct)
    {
        var charges = LoadCharges(checkout)?.Where(c => c.TransferId is not null).ToList() ?? new();
        if (charges.Count == 0 && checkout.TransferId is not null)
        {
            charges.Add(new Charge { TransferId = checkout.TransferId, Amount = checkout.ChargedAmount });
        }

        var refunded = 0m;
        foreach (var charge in charges)
        {
            refunded += await ReturnTransferAsync(checkout, charge, ct);
        }
        return refunded;
    }

    private async Task<decimal> ReturnTransferAsync(BankCheckout checkout, Charge charge, CancellationToken ct)
    {
        var transfer = await GetTransferAsync(charge.TransferId!, ct);

        switch (transfer.Status)
        {
            case "failed":
            case "returned":
                return 0m; // the money never left the shopper's account
            case "cancelled":
                return charge.Amount;
        }

        if (transfer.Cancellable)
        {
            try
            {
                await _plaid.PostAsync<PlaidEmpty>("transfer/cancel", new { transfer_id = transfer.Id }, ct);
                return charge.Amount;
            }
            catch (PlaidException)
            {
                // it may have just been submitted to the bank: look again, then refund instead
                transfer = await GetTransferAsync(charge.TransferId!, ct);
                if (transfer.Status == "cancelled") return charge.Amount;
            }
        }

        var charged = ParseMoney(transfer.Amount) ?? charge.Amount;
        var alreadyRefunded = (transfer.Refunds ?? new())
            .Where(r => r.Status is not ("failed" or "cancelled" or "returned")).Sum(r => ParseMoney(r.Amount) ?? 0m);
        var remaining = charged - alreadyRefunded;
        if (remaining > 0m)
        {
            try
            {
                await _plaid.PostAsync<PlaidRefundResponse>("transfer/refund/create", new
                {
                    transfer_id = transfer.Id,
                    amount = Money(remaining),
                    idempotency_key = "cancel-" + checkout.PublicId.ToString("N") + "-" + transfer.Id
                }, ct);
            }
            catch (PlaidException ex) when (ex.ErrorCode == "AMOUNT_EXCEEDED")
            {
                // an earlier attempt already refunded it
            }
        }
        return charge.Amount;
    }

    private async Task<PlaidTransfer> GetTransferAsync(string transferId, CancellationToken ct) =>
        (await _plaid.PostAsync<PlaidTransferResponse>("transfer/get", new { transfer_id = transferId }, ct)).Transfer;

    // ---------------------------------------------------------------- helpers

    public IReadOnlyList<string> OptionsFor(decimal total)
    {
        var options = new List<string> { Standard };
        if (total + _options.ExpressFee <= _options.ExpressLimit)
        {
            options.Add(Express);
        }
        return options;
    }

    private BankPaymentQuote Quote(BankCheckout checkout, Guid connectionPublicId)
    {
        var total = checkout.Subtotal + checkout.ShippingCost;
        return new BankPaymentQuote(checkout.PublicId.ToString(), connectionPublicId.ToString(), checkout.AccountId,
            checkout.Subtotal, checkout.ShippingCost, total, _options.ExpressFee, checkout.Currency, OptionsFor(total));
    }

    private static OrderConfirmation Confirmation(BankCheckout c) =>
        new(c.OrderId ?? 0, c.PublicId.ToString(), "confirmed", c.Speed ?? Standard, c.Subtotal + c.ShippingCost, c.Fee,
            c.ChargedAmount, c.Currency, (c.ArrivesOn ?? DateTime.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), c.TransferId ?? string.Empty);

    private async Task<BankCheckout> FindCheckoutAsync(string shopperId, string checkoutId, CancellationToken ct)
    {
        var checkout = Guid.TryParse(checkoutId, out var publicId)
            ? await _db.BankCheckouts.FirstOrDefaultAsync(c => c.PublicId == publicId && c.ShopperId == shopperId, ct)
            : null;
        return checkout ?? throw BankApiException.NotFound("No such checkout.");
    }

    private Task<IDisposable> LockCheckoutAsync(string checkoutId, CancellationToken ct) =>
        _locks.AcquireAsync("checkout:" + checkoutId.Trim().ToLowerInvariant(), ct);

    private static decimal ValidateMoney(decimal value, string field)
    {
        if (value < 0m)
        {
            throw BankApiException.BadRequest($"{field} must not be negative.");
        }
        if (decimal.Round(value, 2) != value)
        {
            throw BankApiException.BadRequest($"{field} must have at most 2 decimal places.");
        }
        return value;
    }

    private static string Money(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
}
