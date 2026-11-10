using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ThePlaidApi.Models;
using ThePlaidApi.Models.Enums;
using ThePlaidApi.Requests;

namespace Microsoft.eShopWeb.PublicApi.Banking;

public sealed record CheckoutItemRequest(int CatalogItemId, int Quantity);

public sealed record ShipToRequest(string? Street, string? City, string? State, string? Country, string? ZipCode);

public sealed record StartBankPaymentRequest(
    string? ConnectionId,
    List<CheckoutItemRequest>? Items,
    decimal? ShippingCost,
    ShipToRequest? ShipTo);

public sealed record BankCheckoutDto(
    string CheckoutId,
    string ConnectionId,
    string? Bank,
    decimal ItemsTotal,
    decimal ShippingCost,
    decimal Total,
    IReadOnlyList<string> Options,
    decimal ExpressFee,
    DateTimeOffset ExpiresAt);

public sealed record OrderConfirmationDto(
    int OrderId,
    string CheckoutId,
    string Speed,
    decimal Total,
    decimal Fee,
    decimal Charged,
    string ArrivesOn,
    string Status);

/// <summary>Starting, changing and confirming a pay-by-bank checkout.</summary>
public class BankCheckoutService
{
    public const string Standard = "standard";
    public const string Express = "express";

    /// <summary>The largest amount one ACH transfer can carry; bigger orders are split over several transfers.</summary>
    public const decimal AchTransferLimit = 10_000_000m;

    /// <summary>The largest amount one same-day ACH transfer can carry.</summary>
    public const decimal SameDayAchLimit = 1_000_000m;

    private static readonly TimeSpan CheckoutLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan SameDayCutoff = new(15, 30, 0);

    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Locks = new();

    private readonly PlaidApi _plaid;
    private readonly CatalogContext _db;
    private readonly BankConnectionService _connections;
    private readonly TimeProvider _time;
    private readonly ILogger<BankCheckoutService> _logger;
    private readonly decimal _expressFee;

    public BankCheckoutService(PlaidApi plaid, CatalogContext db, BankConnectionService connections,
        TimeProvider time, IConfiguration configuration, ILogger<BankCheckoutService> logger)
    {
        _plaid = plaid;
        _db = db;
        _connections = connections;
        _time = time;
        _logger = logger;
        _expressFee = decimal.TryParse(configuration["BankPayment:ExpressFee"], NumberStyles.Number,
            CultureInfo.InvariantCulture, out var fee) && fee >= 0
            ? Math.Round(fee, 2, MidpointRounding.AwayFromZero)
            : 5.00m;
    }

    private sealed record Line(int CatalogItemId, string Name, string PictureUri, decimal UnitPrice, int Quantity);

    // ---- start ------------------------------------------------------------------------------------------------

    public async Task<BankCheckoutDto> StartAsync(string userName, StartBankPaymentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ConnectionId))
        {
            throw new ApiRequestException(HttpStatusCode.BadRequest, "connectionId is required.");
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            throw new ApiRequestException(HttpStatusCode.BadRequest, "The order has no items.");
        }

        var shipping = ValidateShipping(request.ShippingCost ?? 0m);

        var connection = await _connections.FindOwnedAsync(userName, request.ConnectionId!);
        if (connection.Kind != BankConnectionKind.Bank)
        {
            throw new ApiRequestException(HttpStatusCode.UnprocessableEntity,
                "A brokerage account can't be used to pay for an order.");
        }

        if (string.IsNullOrEmpty(connection.PaymentAccountId))
        {
            throw new ApiRequestException(HttpStatusCode.UnprocessableEntity,
                "The connected bank has no checking or savings account to pay from.");
        }

        var merged = new Dictionary<int, int>();
        foreach (var item in request.Items)
        {
            if (item.Quantity < 1)
            {
                throw new ApiRequestException(HttpStatusCode.BadRequest, "Item quantities must be at least 1.");
            }

            merged[item.CatalogItemId] = checked(merged.GetValueOrDefault(item.CatalogItemId) + item.Quantity);
        }

        var ids = merged.Keys.ToList();
        var catalog = await _db.CatalogItems.AsNoTracking().Where(i => ids.Contains(i.Id)).ToListAsync();
        var missing = ids.Except(catalog.Select(i => i.Id)).ToList();
        if (missing.Count > 0)
        {
            throw new ApiRequestException(HttpStatusCode.UnprocessableEntity,
                "Unknown catalog item(s): " + string.Join(", ", missing));
        }

        var lines = catalog.OrderBy(i => i.Id)
            .Select(i => new Line(i.Id, i.Name, i.PictureUri ?? string.Empty, i.Price, merged[i.Id])).ToList();
        var itemsTotal = lines.Sum(l => l.UnitPrice * l.Quantity);

        var total = itemsTotal + shipping;
        if (total <= 0m)
        {
            throw new ApiRequestException(HttpStatusCode.UnprocessableEntity, "The order total must be more than zero.");
        }

        ShipToRequest? shipTo = request.ShipTo;
        var checkout = new BankCheckout
        {
            UserName = userName,
            BankConnectionId = connection.Id,
            ItemsJson = JsonSerializer.Serialize(lines),
            ShipToJson = shipTo is null ? null : JsonSerializer.Serialize(shipTo),
            ItemsTotal = itemsTotal,
            ShippingCost = shipping,
            Status = BankCheckoutStatus.Open,
            CreatedAt = _time.GetUtcNow(),
            ExpiresAt = _time.GetUtcNow().Add(CheckoutLifetime)
        };
        _db.BankCheckouts.Add(checkout);
        await _db.SaveChangesAsync();

        return ToDto(checkout, connection);
    }

    // ---- shipping ---------------------------------------------------------------------------------------------

    public async Task<BankCheckoutDto> ChangeShippingAsync(string userName, string checkoutId, decimal? shippingCost)
    {
        if (shippingCost is null)
        {
            throw new ApiRequestException(HttpStatusCode.BadRequest, "shippingCost is required.");
        }

        var shipping = ValidateShipping(shippingCost.Value);
        var id = ParseId(checkoutId);

        using var _ = await LockAsync(id);
        var checkout = await FindCheckoutAsync(userName, id);
        EnsureOpen(checkout);

        if (checkout.ItemsTotal + shipping <= 0m)
        {
            throw new ApiRequestException(HttpStatusCode.UnprocessableEntity, "The order total must be more than zero.");
        }

        checkout.ShippingCost = shipping;
        checkout.Version++;
        await SaveCheckoutAsync();

        var connection = await _db.BankConnections.AsNoTracking().FirstOrDefaultAsync(c => c.Id == checkout.BankConnectionId);
        return ToDto(checkout, connection);
    }

    // ---- confirm ----------------------------------------------------------------------------------------------

    public async Task<OrderConfirmationDto> ConfirmAsync(string userName, string checkoutId, string? speed)
    {
        speed = speed?.Trim().ToLowerInvariant();
        if (speed is not (Standard or Express))
        {
            throw new ApiRequestException(HttpStatusCode.BadRequest, "speed must be 'standard' or 'express'.");
        }

        var id = ParseId(checkoutId);
        using var _ = await LockAsync(id);
        var checkout = await FindCheckoutAsync(userName, id);

        if (checkout.Status == BankCheckoutStatus.Confirmed)
        {
            // Confirming again never charges again: answer with the order that was placed.
            var done = await _db.BankPayments.AsNoTracking().Include(p => p.Transfers)
                .FirstAsync(p => p.CheckoutId == checkout.Id);
            if (done.Speed != speed)
            {
                throw new ApiRequestException(HttpStatusCode.Conflict,
                    $"This payment was already confirmed with {done.Speed} speed.");
            }

            return ToConfirmation(done);
        }

        if (checkout.Status == BankCheckoutStatus.Confirming)
        {
            throw new ApiRequestException(HttpStatusCode.Conflict, "This payment is already being confirmed.");
        }

        EnsureOpen(checkout);

        var connection = await _db.BankConnections.FirstOrDefaultAsync(c => c.Id == checkout.BankConnectionId)
                         ?? throw new ApiRequestException(HttpStatusCode.Conflict,
                             "The bank was disconnected, start the payment again with another bank.");
        var total = checkout.ItemsTotal + checkout.ShippingCost;
        var options = OptionsFor(total);
        if (!options.Contains(speed))
        {
            throw new ApiRequestException(HttpStatusCode.UnprocessableEntity,
                $"{speed} is not available for this order. Available: {string.Join(", ", options)}.");
        }

        var fee = speed == Express ? _expressFee : 0m;
        var charged = total + fee;
        var accessToken = _connections.RevealAccessToken(connection);

        // From here on a single confirmation owns the checkout.
        checkout.Status = BankCheckoutStatus.Confirming;
        checkout.Version++;
        await SaveCheckoutAsync();

        var created = new List<(string Id, decimal Amount, Transfer Transfer)>();
        try
        {
            var chunks = Split(charged, speed == Express ? SameDayAchLimit : AchTransferLimit);
            var network = speed == Express ? TransferNetwork.SameDayAch : TransferNetwork.Ach;

            // Authorize every part first so nothing is charged unless the whole order can be.
            var authorizations = new List<string>();
            for (var i = 0; i < chunks.Count; i++)
            {
                var amount = chunks[i];
                var key = Key(checkout.Id, speed, checkout.Version, amount, i);
                var authorization = (await _plaid.CallAsync(c => c.TransferAuthorizationCreate(
                    new TransferAuthorizationCreateOperationRequest
                    {
                        Body = new TransferAuthorizationCreateRequest
                        {
                            AccessToken = accessToken,
                            AccountId = connection.PaymentAccountId!,
                            Type = TransferType.Debit,
                            Network = network,
                            Amount = FormatAmount(amount),
                            AchClass = AchClass.Web,
                            User = new TransferAuthorizationUserInRequest { LegalName = userName },
                            IdempotencyKey = key,
                            UserPresent = true
                        }
                    }))).Authorization;

                if (authorization.Decision != TransferAuthorizationDecision.Approved)
                {
                    var why = authorization.DecisionRationale?.Description ?? "the bank did not approve the payment";
                    throw new ApiRequestException(HttpStatusCode.UnprocessableEntity,
                        $"The payment was {authorization.Decision.Value.Replace('_', ' ')}: {why}.");
                }

                authorizations.Add(authorization.Id);
            }

            for (var i = 0; i < chunks.Count; i++)
            {
                var amount = chunks[i];
                var authorizationId = authorizations[i];
                var transfer = (await _plaid.CallAsync(c => c.TransferCreate(new TransferCreateOperationRequest
                {
                    Body = new TransferCreateRequest
                    {
                        AccessToken = accessToken,
                        AccountId = connection.PaymentAccountId!,
                        AuthorizationId = authorizationId,
                        Description = "eShop order"
                    }
                }))).Transfer;
                created.Add((transfer.Id, amount, transfer));
            }

            var arrivesOn = ArrivesOn(speed, created.Select(c => c.Transfer));

            var order = BuildOrder(userName, checkout);
            _db.Orders.Add(order);
            await _db.SaveChangesAsync();

            var payment = new BankPayment
            {
                OrderId = order.Id,
                UserName = userName,
                CheckoutId = checkout.Id,
                Speed = speed,
                Total = total,
                Fee = fee,
                ArrivesOn = arrivesOn,
                Status = BankPaymentStatus.Paid,
                CreatedAt = _time.GetUtcNow(),
                Transfers = created.Select(c => new BankPaymentTransfer { PlaidTransferId = c.Id, Amount = c.Amount }).ToList()
            };
            _db.BankPayments.Add(payment);
            checkout.Status = BankCheckoutStatus.Confirmed;
            checkout.OrderId = order.Id;
            checkout.Version++;
            await _db.SaveChangesAsync();

            return ToConfirmation(payment);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Confirming checkout {CheckoutId} failed", checkout.Id);
            await UndoAsync(checkout, created.Select(c => c.Id).ToList());
            throw;
        }
    }

    /// <summary>Nothing was charged for the order: cancel what was created and let the shopper try again.</summary>
    private async Task UndoAsync(BankCheckout checkout, List<string> transferIds)
    {
        foreach (var transferId in transferIds)
        {
            try
            {
                await _plaid.CallAsync(c => c.TransferCancel(new TransferCancelOperationRequest
                {
                    Body = new TransferCancelRequest { TransferId = transferId }
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not cancel transfer {TransferId}", transferId);
            }
        }

        try
        {
            foreach (var entry in _db.ChangeTracker.Entries().Where(e => e.State is EntityState.Added).ToList())
            {
                entry.State = EntityState.Detached;
            }

            var keepOrder = await _db.BankPayments.AnyAsync(p => p.CheckoutId == checkout.Id);
            if (!keepOrder)
            {
                checkout.Status = BankCheckoutStatus.Open;
                checkout.OrderId = null;
                checkout.Version++;
                await _db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not reopen checkout {CheckoutId}", checkout.Id);
        }
    }

    // ---- helpers ----------------------------------------------------------------------------------------------

    public IReadOnlyList<string> OptionsFor(decimal total)
    {
        var options = new List<string> { Standard };
        if (total + _expressFee <= SameDayAchLimit)
        {
            options.Add(Express);
        }

        return options;
    }

    private DateTime ArrivesOn(string speed, IEnumerable<Transfer> transfers)
    {
        var now = BusinessCalendar.EasternNow(_time.GetUtcNow());
        // Express moves the money the same business day, or the next one once today's window has closed.
        var express = BusinessCalendar.IsBusinessDay(now) && now.TimeOfDay < SameDayCutoff
            ? now.Date
            : BusinessCalendar.NextBusinessDay(now);
        if (speed == Express)
        {
            return express;
        }

        var plaid = transfers
            .Select(t => t.ExpectedSettlementDate ?? t.ExpectedFundsAvailableDate)
            .Where(d => d.HasValue).Select(d => d!.Value.Date).DefaultIfEmpty(DateTime.MinValue).Max();
        var standard = plaid == DateTime.MinValue ? BusinessCalendar.NextBusinessDay(now) : plaid;
        standard = BusinessCalendar.OnOrAfterBusinessDay(standard);
        return standard < express ? express : standard;
    }

    private Order BuildOrder(string userName, BankCheckout checkout)
    {
        var lines = JsonSerializer.Deserialize<List<Line>>(checkout.ItemsJson)!;
        var ship = checkout.ShipToJson is null ? null : JsonSerializer.Deserialize<ShipToRequest>(checkout.ShipToJson);
        string Or(string? v) => string.IsNullOrWhiteSpace(v) ? "Not provided" : v.Trim();
        var address = new Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Address(Or(ship?.Street), Or(ship?.City), ship?.State?.Trim() ?? string.Empty,
            Or(ship?.Country), Or(ship?.ZipCode));
        var items = lines.Select(l => new OrderItem(
            new CatalogItemOrdered(l.CatalogItemId, l.Name, string.IsNullOrEmpty(l.PictureUri) ? "/images/products/placeholder.png" : l.PictureUri),
            l.UnitPrice, l.Quantity)).ToList();
        return new Order(userName, address, items);
    }

    public static List<decimal> Split(decimal amount, decimal limit)
    {
        var parts = new List<decimal>();
        var rest = amount;
        while (rest > limit)
        {
            parts.Add(limit);
            rest -= limit;
        }

        parts.Add(rest);
        return parts;
    }

    public static string FormatAmount(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero).ToString("F2", CultureInfo.InvariantCulture);

    private static string Key(Guid checkoutId, string speed, int version, decimal amount, int index)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{checkoutId:N}|{speed}|{version}|{FormatAmount(amount)}|{index}"));
        return Convert.ToHexString(hash)[..40].ToLowerInvariant();
    }

    private static decimal ValidateShipping(decimal shipping)
    {
        if (shipping < 0m)
        {
            throw new ApiRequestException(HttpStatusCode.BadRequest, "shippingCost cannot be negative.");
        }

        if (shipping != Math.Round(shipping, 2))
        {
            throw new ApiRequestException(HttpStatusCode.BadRequest, "shippingCost can have two decimals at most.");
        }

        if (shipping > 1_000_000_000m)
        {
            throw new ApiRequestException(HttpStatusCode.BadRequest, "shippingCost is too large.");
        }

        return shipping;
    }

    private static Guid ParseId(string checkoutId) =>
        Guid.TryParse(checkoutId, out var id)
            ? id
            : throw new ApiRequestException(HttpStatusCode.NotFound, "The checkout was not found.");

    private async Task<BankCheckout> FindCheckoutAsync(string userName, Guid id) =>
        await _db.BankCheckouts.FirstOrDefaultAsync(c => c.Id == id && c.UserName == userName)
        ?? throw new ApiRequestException(HttpStatusCode.NotFound, "The checkout was not found.");

    private void EnsureOpen(BankCheckout checkout)
    {
        if (checkout.Status != BankCheckoutStatus.Open)
        {
            throw new ApiRequestException(HttpStatusCode.Conflict, "This payment was already confirmed.");
        }

        if (checkout.ExpiresAt <= _time.GetUtcNow())
        {
            throw new ApiRequestException(HttpStatusCode.Gone, "This checkout expired, start the payment again.");
        }
    }

    private async Task SaveCheckoutAsync()
    {
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ApiRequestException(HttpStatusCode.Conflict, "The checkout changed at the same time, try again.", ex);
        }
    }

    private static async Task<IDisposable> LockAsync(Guid id)
    {
        var gate = Locks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        return new Releaser(gate);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _gate;
        public Releaser(SemaphoreSlim gate) => _gate = gate;
        public void Dispose() => _gate.Release();
    }

    private BankCheckoutDto ToDto(BankCheckout c, BankConnection? connection)
    {
        var total = c.ItemsTotal + c.ShippingCost;
        return new BankCheckoutDto(c.Id.ToString(), connection?.PublicId.ToString() ?? string.Empty,
            connection?.InstitutionName, c.ItemsTotal, c.ShippingCost, total, OptionsFor(total), _expressFee, c.ExpiresAt);
    }

    public static OrderConfirmationDto ToConfirmation(BankPayment p) => new(
        p.OrderId, p.CheckoutId.ToString(), p.Speed, p.Total, p.Fee, p.Total + p.Fee,
        p.ArrivesOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        p.Status == BankPaymentStatus.Cancelled ? "cancelled" : "paid");
}
