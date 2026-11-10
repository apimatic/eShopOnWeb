using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankingAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ThePlaidApi;
using ThePlaidApi.Models;
using ThePlaidApi.Models.Enums;
using ThePlaidApi.Requests;

namespace Microsoft.eShopWeb.PublicApi.Banking;

public record CheckoutLine(int CatalogItemId, string Name, decimal UnitPrice, int Units);

public record RequestedLine(int CatalogItemId, int Quantity);

public record DeviceInfo(string? IpAddress, string? UserAgent);

public record CheckoutQuote(string CheckoutId, decimal Subtotal, decimal Shipping, decimal Total,
    IReadOnlyList<string> Options, decimal ExpressFee);

public record OrderConfirmation(int OrderId, string CheckoutId, decimal Total, decimal Fee, decimal Charged,
    string Speed, DateOnly? ArrivesOn, string TransferId);

/// <summary>Starting, re-pricing and confirming a pay-by-bank checkout.</summary>
public class CheckoutService
{
    public const string Standard = "standard";
    public const string Express = "express";

    /// <summary>Plaid's ceiling for a single same-day ACH transfer.</summary>
    public const decimal SameDayAchLimit = 1_000_000m;
    private const decimal DefaultExpressFee = 5.00m;
    private const decimal MaxAmount = 1_000_000_000_000m;

    private readonly ThePlaidApiClient _plaid;
    private readonly CatalogContext _db;
    private readonly ConnectionService _connections;
    private readonly KeyedLock _locks;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CheckoutService> _logger;

    public CheckoutService(ThePlaidApiClient plaid, CatalogContext db, ConnectionService connections, KeyedLock locks,
        IConfiguration configuration, ILogger<CheckoutService> logger)
    {
        _plaid = plaid;
        _db = db;
        _connections = connections;
        _locks = locks;
        _configuration = configuration;
        _logger = logger;
    }

    public decimal ExpressFee
    {
        get
        {
            var configured = _configuration["BankPayment:ExpressFee"];
            return decimal.TryParse(configured, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var fee) && fee >= 0
                ? decimal.Round(fee, 2)
                : DefaultExpressFee;
        }
    }

    /// <summary>The speeds available for an order of this total.</summary>
    public IReadOnlyList<string> OptionsFor(decimal total)
    {
        var options = new List<string> { Standard };
        // Same-day ACH is capped by the network: bigger orders (wholesale) can only go standard.
        if (total + ExpressFee <= SameDayAchLimit)
            options.Add(Express);
        return options;
    }

    private decimal FeeFor(string speed) => speed == Express ? ExpressFee : 0m;

    private CheckoutQuote Quote(BankCheckout checkout) =>
        new(checkout.Id, checkout.Subtotal, checkout.Shipping, checkout.Total, OptionsFor(checkout.Total), ExpressFee);

    // ---------------------------------------------------------------- start

    public async Task<CheckoutQuote> StartAsync(string shopperId, int connectionId, IReadOnlyList<RequestedLine>? lines,
        decimal? shippingCost, CancellationToken ct)
    {
        var connection = await _connections.FindOwnedAsync(shopperId, connectionId, ct);
        if (connection.Kind != BankConnectionKind.Bank || connection.AccountId is null)
            throw BankingException.BadRequest("That connection is not a bank account that can pay for an order.");

        if (lines is null || lines.Count == 0)
            throw BankingException.BadRequest("items must contain at least one item.");
        var shipping = ValidateShipping(shippingCost);

        var merged = new Dictionary<int, long>();
        foreach (var line in lines)
        {
            if (line is null || line.Quantity <= 0)
                throw BankingException.BadRequest("Every item needs a quantity above zero.");
            merged[line.CatalogItemId] = merged.GetValueOrDefault(line.CatalogItemId) + line.Quantity;
        }

        var ids = merged.Keys.ToList();
        var catalog = await _db.CatalogItems.Where(i => ids.Contains(i.Id)).ToListAsync(ct);
        var missing = ids.Except(catalog.Select(i => i.Id)).ToList();
        if (missing.Count > 0)
            throw BankingException.NotFound($"Unknown catalog item(s): {string.Join(", ", missing)}.");

        decimal subtotal = 0m;
        var snapshot = new List<CheckoutLine>();
        try
        {
            foreach (var item in catalog.OrderBy(i => merged.Keys.ToList().IndexOf(i.Id)))
            {
                var units = merged[item.Id];
                if (units > int.MaxValue)
                    throw BankingException.BadRequest("Quantity is too large.");
                subtotal = checked(subtotal + item.Price * units);
                snapshot.Add(new CheckoutLine(item.Id, item.Name, item.Price, (int)units));
            }
        }
        catch (OverflowException)
        {
            throw BankingException.BadRequest("The order total is too large.");
        }

        if (subtotal + shipping > MaxAmount)
            throw BankingException.BadRequest("The order total is too large.");
        if (subtotal + shipping <= 0)
            throw BankingException.BadRequest("The order total must be above zero.");

        var checkout = new BankCheckout
        {
            Id = Guid.NewGuid().ToString("N"),
            OwnerId = shopperId,
            ConnectionId = connection.Id,
            ItemsJson = JsonSerializer.Serialize(snapshot),
            Subtotal = subtotal,
            Shipping = shipping,
        };
        _db.BankCheckouts.Add(checkout);
        await _db.SaveChangesAsync(ct);
        return Quote(checkout);
    }

    private static decimal ValidateShipping(decimal? shippingCost)
    {
        var shipping = shippingCost ?? throw BankingException.BadRequest("shippingCost is required.");
        if (shipping < 0)
            throw BankingException.BadRequest("shippingCost cannot be negative.");
        if (!Money.HasCentsOnly(shipping))
            throw BankingException.BadRequest("shippingCost can have at most two decimals.");
        if (shipping > MaxAmount)
            throw BankingException.BadRequest("shippingCost is too large.");
        return shipping;
    }

    // ---------------------------------------------------------------- shipping

    public async Task<CheckoutQuote> ChangeShippingAsync(string shopperId, string checkoutId, decimal? shippingCost,
        CancellationToken ct)
    {
        var shipping = ValidateShipping(shippingCost);
        using var _ = await _locks.AcquireAsync("checkout:" + checkoutId, ct);

        var checkout = await FindOpenAsync(shopperId, checkoutId, ct);
        if (checkout.Subtotal + shipping > MaxAmount)
            throw BankingException.BadRequest("The order total is too large.");
        checkout.Shipping = shipping;
        checkout.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Quote(checkout);
    }

    private async Task<BankCheckout> FindOpenAsync(string shopperId, string checkoutId, CancellationToken ct)
    {
        var checkout = await FindAsync(shopperId, checkoutId, ct);
        if (checkout.Status != BankCheckoutStatus.Open)
            throw BankingException.Conflict("That checkout has already been confirmed.");
        return checkout;
    }

    private async Task<BankCheckout> FindAsync(string shopperId, string checkoutId, CancellationToken ct)
    {
        var checkout = await _db.BankCheckouts.FirstOrDefaultAsync(c => c.Id == checkoutId, ct);
        if (checkout is null || checkout.OwnerId != shopperId)
            throw BankingException.NotFound("No such checkout.");
        return checkout;
    }

    // ---------------------------------------------------------------- confirm

    public async Task<OrderConfirmation> ConfirmAsync(string shopperId, string checkoutId, string? speedText,
        DeviceInfo device, CancellationToken ct)
    {
        var speed = speedText?.Trim().ToLowerInvariant();
        if (speed is not (Standard or Express))
            throw BankingException.BadRequest("speed must be 'standard' or 'express'.");

        // One confirmation at a time per checkout: a double click must not charge twice.
        using var _ = await _locks.AcquireAsync("checkout:" + checkoutId, ct);

        var checkout = await FindAsync(shopperId, checkoutId, ct);
        if (checkout.Status == BankCheckoutStatus.Confirmed)
        {
            var existing = await _db.BankOrders.FirstAsync(o => o.Id == checkout.OrderId, ct);
            if (existing.Speed != speed)
                throw BankingException.Conflict($"That checkout was already confirmed with {existing.Speed} speed.");
            return ToConfirmation(existing);
        }

        if (!OptionsFor(checkout.Total).Contains(speed))
            throw BankingException.BadRequest($"{speed} is not available for this order; available: {string.Join(", ", OptionsFor(checkout.Total))}.");

        var connection = await _connections.FindOwnedAsync(shopperId, checkout.ConnectionId, ct);
        if (connection.AccountId is null)
            throw BankingException.Conflict("That connection cannot pay for an order.");
        var accessToken = _connections.AccessTokenOf(connection);

        var fee = FeeFor(speed);
        var charge = checkout.Total + fee;
        var amount = Money.ToPlaid(charge);
        var network = speed == Express ? TransferNetwork.SameDayAch : TransferNetwork.Ach;
        var hasDevice = !string.IsNullOrWhiteSpace(device.IpAddress) && !string.IsNullOrWhiteSpace(device.UserAgent);

        var authorization = (await PlaidCalls.Run(() => _plaid.TransferAuthorizationCreate(new TransferAuthorizationCreateOperationRequest
        {
            Body = new TransferAuthorizationCreateRequest
            {
                AccessToken = accessToken,
                AccountId = connection.AccountId,
                Type = TransferType.Debit,
                Network = network,
                Amount = amount,
                AchClass = hasDevice ? AchClass.Web : AchClass.Ppd,
                User = new TransferAuthorizationUserInRequest { LegalName = shopperId, EmailAddress = shopperId },
                Device = hasDevice ? new TransferAuthorizationDevice { IpAddress = device.IpAddress!, UserAgent = device.UserAgent! } : null,
                UserPresent = hasDevice ? true : null,
            }
        }, cancellationToken: ct))).Authorization;

        if (authorization.Decision == TransferAuthorizationDecision.UserActionRequired)
            throw BankingException.Conflict("The bank needs the shopper to take action before it can be charged (reconnect the bank).");
        if (authorization.Decision != TransferAuthorizationDecision.Approved)
        {
            var why = authorization.DecisionRationale?.Description;
            throw BankingException.Unprocessable("The bank declined the payment" + (why is null ? "." : $": {why}."));
        }

        Transfer transfer;
        try
        {
            transfer = (await PlaidCalls.Run(() => _plaid.TransferCreate(new TransferCreateOperationRequest
            {
                Body = new TransferCreateRequest
                {
                    AccessToken = accessToken,
                    AccountId = connection.AccountId,
                    AuthorizationId = authorization.Id,
                    Description = "eShop order",
                    Metadata = new Dictionary<string, string> { ["checkoutId"] = checkout.Id },
                }
            }, cancellationToken: ct))).Transfer;
        }
        catch
        {
            await TryCancelAuthorizationAsync(authorization.Id);
            throw;
        }

        var order = new BankOrder
        {
            OwnerId = shopperId,
            CheckoutId = checkout.Id,
            ConnectionId = connection.Id,
            ItemsJson = checkout.ItemsJson,
            Subtotal = checkout.Subtotal,
            Shipping = checkout.Shipping,
            Fee = fee,
            Charged = charge,
            Speed = speed,
            TransferId = transfer.Id,
            ArrivesOn = ArrivalDate(transfer),
        };

        try
        {
            _db.BankOrders.Add(order);
            await _db.SaveChangesAsync(ct);
            checkout.Status = BankCheckoutStatus.Confirmed;
            checkout.OrderId = order.Id;
            checkout.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            // The shopper must not be charged for an order we could not record.
            await TryCancelTransferAsync(transfer.Id);
            throw;
        }

        return ToConfirmation(order);
    }

    private static DateOnly? ArrivalDate(Transfer transfer)
    {
        var date = transfer.ExpectedSettlementDate ?? transfer.ExpectedFundsAvailableDate;
        // Plaid sends a calendar date; read the date as sent, whatever the offset the SDK attached.
        return date is null ? null : DateOnly.FromDateTime(date.Value.DateTime);
    }

    private static OrderConfirmation ToConfirmation(BankOrder o) =>
        new(o.Id, o.CheckoutId, o.Subtotal + o.Shipping, o.Fee, o.Charged, o.Speed, o.ArrivesOn, o.TransferId);

    private async Task TryCancelAuthorizationAsync(string authorizationId)
    {
        try
        {
            await _plaid.TransferAuthorizationCancel(new TransferAuthorizationCancelOperationRequest
            {
                Body = new TransferAuthorizationCancelRequest { AuthorizationId = authorizationId }
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not cancel transfer authorization {AuthorizationId}.", authorizationId);
        }
    }

    private async Task TryCancelTransferAsync(string transferId)
    {
        try
        {
            await _plaid.TransferCancel(new TransferCancelOperationRequest
            {
                Body = new TransferCancelRequest { TransferId = transferId }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not cancel transfer {TransferId} after failing to record its order.", transferId);
        }
    }
}
