using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

public record CheckoutLine(int CatalogItemId, int Quantity);
public record StartBankPaymentRequest(string? ConnectionId, List<CheckoutLine>? Items, decimal? ShippingCost, string? AccountId, string? LegalName);
public record ChangeShippingRequest(decimal? ShippingCost);
public record ConfirmBankPaymentRequest(string? Speed);

/// <summary>Pay-by-bank checkout: start, change shipping, confirm (charge).</summary>
public class CheckoutService
{
    /// <summary>Largest amount the same-day ACH network carries.</summary>
    public const decimal SameDayLimit = 1_000_000m;

    /// <summary>Largest amount a single standard ACH transfer carries.</summary>
    public const decimal StandardLimit = 10_000_000m;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    private readonly PlaidClient _plaid;
    private readonly CatalogContext _db;
    private readonly ConnectionService _connections;
    private readonly TransferReverser _reverser;
    private readonly decimal _expressFee;

    public CheckoutService(PlaidClient plaid, CatalogContext db, ConnectionService connections, TransferReverser reverser, IConfiguration configuration)
    {
        _plaid = plaid;
        _db = db;
        _connections = connections;
        _reverser = reverser;
        _expressFee = decimal.TryParse(configuration["BankPayment:ExpressFee"], NumberStyles.Number, CultureInfo.InvariantCulture, out var fee) && fee >= 0
            ? Math.Round(fee, 2) : 5.00m;
    }

    public decimal FeeFor(string speed) => speed == PaymentSpeeds.Express ? _expressFee : 0m;

    /// <summary>The speeds on offer for an order total. Express is a same-day ACH, which has a network limit.</summary>
    public IReadOnlyList<string> OptionsFor(decimal total)
    {
        var options = new List<string> { PaymentSpeeds.Standard };
        if (total + _expressFee <= SameDayLimit) options.Add(PaymentSpeeds.Express);
        return options;
    }

    // ---- Start ----

    public async Task<IResult> StartAsync(string userName, StartBankPaymentRequest? request, CancellationToken ct)
    {
        if (request is null) return ApiResults.BadRequest("A request body is required.");
        if (string.IsNullOrWhiteSpace(request.ConnectionId)) return ApiResults.BadRequest("connectionId is required.");
        if (request.Items is null || request.Items.Count == 0) return ApiResults.BadRequest("items must contain at least one item.");
        if (request.ShippingCost is null) return ApiResults.BadRequest("shippingCost is required.");
        var shippingError = ValidateShipping(request.ShippingCost.Value);
        if (shippingError is not null) return ApiResults.BadRequest(shippingError);

        var lines = new Dictionary<int, int>();
        foreach (var line in request.Items)
        {
            if (line is null || line.Quantity <= 0) return ApiResults.BadRequest("Every item needs a quantity of at least 1.");
            lines[line.CatalogItemId] = checked(lines.GetValueOrDefault(line.CatalogItemId) + line.Quantity);
        }

        var owner = ApiResults.OwnerKey(userName);
        var connection = await _connections.FindOwnedAsync(owner, request.ConnectionId.Trim(), ct);
        if (connection is null) return ApiResults.NotFound("Connection not found.");
        if (connection.Kind != ConnectionKinds.Bank) return ApiResults.BadRequest("That connection is a brokerage account; pick a bank to pay with.");

        var account = PickAccount(connection, request.AccountId);
        if (account is null)
            return ApiResults.BadRequest(string.IsNullOrWhiteSpace(request.AccountId)
                ? "The connection has no checking or savings account to pay from."
                : "accountId is not a checking or savings account of that connection.");

        var ids = lines.Keys.ToList();
        var catalog = await _db.CatalogItems.Where(i => ids.Contains(i.Id)).ToListAsync(ct);
        var missing = ids.Where(id => catalog.All(c => c.Id != id)).ToList();
        if (missing.Count > 0)
            return ApiResults.BadRequest($"Unknown catalog item(s): {string.Join(", ", missing)}.");

        var checkout = new BankCheckout
        {
            OwnerKey = owner,
            BuyerName = userName,
            LegalName = string.IsNullOrWhiteSpace(request.LegalName) ? userName : request.LegalName.Trim(),
            BankConnectionId = connection.Id,
            PlaidAccountId = account.AccountId,
            ShippingCost = request.ShippingCost.Value,
            Items = catalog.Select(c => new BankCheckoutItem
            {
                CatalogItemId = c.Id,
                Name = c.Name,
                UnitPrice = c.Price,
                Quantity = lines[c.Id]
            }).ToList()
        };
        _db.BankCheckouts.Add(checkout);
        await _db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(checkout, connection.PublicId));
    }

    // ---- Change shipping ----

    public async Task<IResult> ChangeShippingAsync(string userName, string checkoutId, ChangeShippingRequest? request, CancellationToken ct)
    {
        if (request?.ShippingCost is null) return ApiResults.BadRequest("shippingCost is required.");
        var error = ValidateShipping(request.ShippingCost.Value);
        if (error is not null) return ApiResults.BadRequest(error);

        using var _ = await LockAsync(checkoutId, ct);
        var checkout = await LoadAsync(userName, checkoutId, ct);
        if (checkout is null) return ApiResults.NotFound("Checkout not found.");
        if (checkout.Status != CheckoutStatuses.Open) return ApiResults.Conflict("The checkout is already confirmed.");
        if (await _db.Set<BankCheckoutAuthorization>().AnyAsync(a => a.BankCheckoutId == checkout.Id && a.TransferId != null, ct))
            return ApiResults.Conflict("A payment for this checkout is in progress; confirm it before changing the shipping.");

        checkout.ShippingCost = request.ShippingCost.Value;
        await _db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(checkout, await ConnectionIdAsync(checkout, ct)));
    }

    // ---- Confirm ----

    public async Task<IResult> ConfirmAsync(string userName, string checkoutId, ConfirmBankPaymentRequest? request, CancellationToken ct)
    {
        var speed = request?.Speed?.Trim().ToLowerInvariant();
        if (speed != PaymentSpeeds.Standard && speed != PaymentSpeeds.Express)
            return ApiResults.BadRequest("speed must be 'standard' or 'express'.");

        // One confirm at a time per checkout, so a double click cannot charge twice.
        using var _ = await LockAsync(checkoutId, ct);
        var checkout = await LoadAsync(userName, checkoutId, ct);
        if (checkout is null) return ApiResults.NotFound("Checkout not found.");

        if (checkout.Status == CheckoutStatuses.Confirmed)
        {
            var done = await _db.BankOrders.FirstAsync(o => o.Id == checkout.OrderId, ct);
            return done.Speed == speed
                ? Results.Ok(OrderDto(done))
                : ApiResults.Conflict("The checkout is already confirmed with a different speed.");
        }

        var total = checkout.Total;
        if (!OptionsFor(total).Contains(speed))
            return ApiResults.BadRequest($"Express is not available for this order total ({total.ToString("F2", CultureInfo.InvariantCulture)}); the same-day network limit is {SameDayLimit:F0}.");

        var fee = FeeFor(speed);
        var charge = Math.Round(total + fee, 2);
        var amount = charge.ToString("F2", CultureInfo.InvariantCulture);

        var connection = await _db.BankConnections.FirstOrDefaultAsync(c => c.Id == checkout.BankConnectionId, ct);
        if (connection is null) return ApiResults.Conflict("The bank connection was disconnected; start the payment again.");
        var accessToken = _connections.Unprotect(connection);
        var network = speed == PaymentSpeeds.Express ? "same-day-ach" : "ach";

        // A big wholesale order is charged as several transfers, each within the network limit.
        var parts = SplitCharge(charge, speed);
        await _db.Entry(checkout).Collection(c => c.Authorizations).LoadAsync(ct);
        var stored = checkout.Authorizations.OrderBy(a => a.Part).ToList();
        var samePlan = checkout.AuthorizationSpeed == speed && stored.Select(a => a.Amount).SequenceEqual(parts);

        try
        {
            if (!samePlan)
            {
                if (stored.Any(a => a.TransferId is not null))
                    return ApiResults.Conflict("An earlier payment attempt is still in progress; confirm again with the same speed.");

                // Authorize every part before moving any money. An authorization alone charges nothing.
                _db.RemoveRange(stored);
                checkout.Authorizations.Clear();
                for (var i = 0; i < parts.Count; i++)
                {
                    var auth = await _plaid.TransferAuthorizationCreateAsync(accessToken, checkout.PlaidAccountId, network,
                        parts[i].ToString("F2", CultureInfo.InvariantCulture), checkout.LegalName, ct);
                    var authorization = auth.Obj("authorization");
                    if (authorization?.Str("decision") != "approved" || authorization.Value.Str("id") is null)
                    {
                        var why = authorization?.Obj("decision_rationale")?.Str("description")
                            ?? authorization?.Obj("decision_rationale")?.Str("code")
                            ?? "the bank payment was not approved";
                        checkout.AuthorizationSpeed = null;
                        await _db.SaveChangesAsync(ct);
                        return ApiResults.Error(StatusCodes.Status402PaymentRequired, $"The bank payment was declined: {why}.");
                    }
                    checkout.Authorizations.Add(new BankCheckoutAuthorization
                    {
                        Part = i,
                        Amount = parts[i],
                        AuthorizationId = authorization.Value.Str("id")!
                    });
                }
                checkout.AuthorizationSpeed = speed;
                await _db.SaveChangesAsync(ct);
                stored = checkout.Authorizations.OrderBy(a => a.Part).ToList();
            }

            // An authorization turns into one transfer only once, so repeating this step cannot charge twice.
            try
            {
                foreach (var part in stored.Where(a => a.TransferId is null))
                {
                    var created = await _plaid.TransferCreateAsync(accessToken, checkout.PlaidAccountId, part.AuthorizationId,
                        $"eShop {checkout.PublicId[..8]}", ct);
                    var transfer = created.Obj("transfer");
                    part.TransferId = transfer?.Str("id")
                        ?? throw new PlaidException("Plaid did not return the transfer.") { IsUnavailable = true };
                    part.ExpectedArrival = transfer?.Str("expected_funds_available_date");
                    await _db.SaveChangesAsync(ct); // remember it before the next one
                }
            }
            catch (PlaidException ex) when (!ex.IsUnavailable && !ex.IsNotConfigured)
            {
                // Plaid refused a later part: give back what was already taken so the shopper is not half charged.
                foreach (var done in stored.Where(a => a.TransferId is not null))
                    await _reverser.ReverseAsync(done.TransferId!, done.Amount, $"undo-{checkout.PublicId}-{done.Part}", ct);
                _db.RemoveRange(stored);
                checkout.Authorizations.Clear();
                checkout.AuthorizationSpeed = null;
                await _db.SaveChangesAsync(ct);
                throw;
            }

            var arrives = stored.Select(a => ParseDate(a.ExpectedArrival)).Where(d => d is not null).Select(d => d!.Value)
                .DefaultIfEmpty(FallbackArrival(speed)).Max();
            var order = new BankOrder
            {
                OwnerKey = checkout.OwnerKey,
                CheckoutPublicId = checkout.PublicId,
                Subtotal = checkout.Subtotal,
                ShippingCost = checkout.ShippingCost,
                Fee = fee,
                Total = charge,
                Speed = speed,
                ArrivesOn = arrives,
                Transfers = stored.Select(a => new BankOrderTransfer { TransferId = a.TransferId!, Amount = a.Amount }).ToList()
            };
            _db.BankOrders.Add(order);
            await _db.SaveChangesAsync(ct);
            checkout.Status = CheckoutStatuses.Confirmed;
            checkout.OrderId = order.Id;
            await _db.SaveChangesAsync(ct);
            return Results.Ok(OrderDto(order));
        }
        catch (PlaidException ex)
        {
            return ApiResults.FromPlaid(ex);
        }
    }

    /// <summary>Splits a charge into transfers no larger than the network allows. Express is never split.</summary>
    public static List<decimal> SplitCharge(decimal charge, string speed)
    {
        var parts = new List<decimal>();
        if (speed == PaymentSpeeds.Express) { parts.Add(charge); return parts; }
        var left = charge;
        while (left > StandardLimit)
        {
            parts.Add(StandardLimit);
            left -= StandardLimit;
        }
        parts.Add(left);
        return parts;
    }

    // ---- helpers ----

    private static string? ValidateShipping(decimal shipping)
    {
        if (shipping < 0) return "shippingCost cannot be negative.";
        if (Math.Round(shipping, 2) != shipping) return "shippingCost can have at most two decimals.";
        if (shipping > 1_000_000_000m) return "shippingCost is too large.";
        return null;
    }

    private static BankConnectionAccount? PickAccount(BankConnection connection, string? accountId)
    {
        var usable = connection.Accounts.Where(a => a.Type == "depository" && (a.Subtype == "checking" || a.Subtype == "savings")).ToList();
        if (!string.IsNullOrWhiteSpace(accountId))
            return usable.FirstOrDefault(a => a.AccountId == accountId.Trim());
        return usable.FirstOrDefault(a => a.Subtype == "checking") ?? usable.FirstOrDefault();
    }

    private async Task<BankCheckout?> LoadAsync(string userName, string checkoutId, CancellationToken ct)
    {
        var owner = ApiResults.OwnerKey(userName);
        return await _db.BankCheckouts.Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.PublicId == checkoutId && c.OwnerKey == owner, ct);
    }

    private async Task<string> ConnectionIdAsync(BankCheckout checkout, CancellationToken ct) =>
        await _db.BankConnections.Where(c => c.Id == checkout.BankConnectionId).Select(c => c.PublicId).FirstOrDefaultAsync(ct) ?? string.Empty;

    private object ToDto(BankCheckout checkout, string connectionId)
    {
        var total = checkout.Total;
        var options = OptionsFor(total);
        return new
        {
            checkoutId = checkout.PublicId,
            connectionId,
            subtotal = checkout.Subtotal,
            shippingCost = checkout.ShippingCost,
            total,
            options,
            fees = options.ToDictionary(o => o, FeeFor)
        };
    }

    private static object OrderDto(BankOrder o) => new
    {
        orderId = o.Id,
        checkoutId = o.CheckoutPublicId,
        status = o.Status,
        speed = o.Speed,
        subtotal = o.Subtotal,
        shippingCost = o.ShippingCost,
        fee = o.Fee,
        total = o.Total,
        arrivesOn = o.ArrivesOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
    };

    public static object ToOrderDto(BankOrder o) => OrderDto(o);

    private static DateTime? ParseDate(string? s) =>
        DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>Only used if Plaid gives no expected date: next business day for express, three for standard.</summary>
    private static DateTime FallbackArrival(string speed)
    {
        var date = DateTime.UtcNow.Date;
        var days = speed == PaymentSpeeds.Express ? 1 : 3;
        while (days > 0)
        {
            date = date.AddDays(1);
            if (date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday) days--;
        }
        return date;
    }

    public static async Task<IDisposable> LockAsync(string key, CancellationToken ct)
    {
        var gate = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        return new Releaser(gate);
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _gate;
        public Releaser(SemaphoreSlim gate) => _gate = gate;
        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}
