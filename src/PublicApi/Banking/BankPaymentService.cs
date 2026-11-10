using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using ThePlaidApi;
using ThePlaidApi.Models;
using ThePlaidApi.Models.Enums;
using ThePlaidApi.Requests;

namespace Microsoft.eShopWeb.PublicApi.Banking;

public record CheckoutLine(int CatalogItemId, int Quantity);

public record ShipTo(string? Street, string? City, string? State, string? Country, string? ZipCode);

public record CheckoutQuote(
    Guid CheckoutId,
    int ConnectionId,
    decimal Subtotal,
    decimal ShippingCost,
    decimal Total,
    IReadOnlyList<string> Options,
    decimal ExpressFee);

public record OrderConfirmation(
    int OrderId,
    Guid CheckoutId,
    string Speed,
    decimal Total,
    decimal Fee,
    decimal TotalCharged,
    DateTime ArrivesOn,
    string TransferId);

public record OrderCancellation(int OrderId, string Status, decimal Refunded, string Method);

/// <summary>
/// Pay by bank: starting a payment, re-pricing its shipping, confirming (charging the bank through Plaid
/// Transfer) and cancelling the order with a full refund.
/// </summary>
public class BankPaymentService
{
    public const string SPEED_STANDARD = "standard";
    public const string SPEED_EXPRESS = "express";

    /// <summary>Largest amount Plaid/ACH moves on the standard network.</summary>
    public const decimal STANDARD_LIMIT = 10_000_000m;

    /// <summary>Largest amount the same-day ACH network moves.</summary>
    public const decimal EXPRESS_LIMIT = 1_000_000m;

    private const int MAX_LINES = 200;
    private const int MAX_UNITS = 100_000;

    private sealed record StoredLine(int CatalogItemId, string Name, string PictureUri, decimal UnitPrice, int Units);

    private readonly ThePlaidApiClient _plaid;
    private readonly CatalogContext _db;
    private readonly AccessTokenProtector _protector;
    private readonly ConnectionLocks _locks;
    private readonly IUriComposer _uriComposer;
    private readonly decimal _expressFee;

    public BankPaymentService(ThePlaidApiClient plaid, CatalogContext db, AccessTokenProtector protector,
        ConnectionLocks locks, IUriComposer uriComposer, IConfiguration configuration)
    {
        _plaid = plaid;
        _db = db;
        _protector = protector;
        _locks = locks;
        _uriComposer = uriComposer;
        _expressFee = decimal.TryParse(configuration["BankPayments:ExpressFee"],
            System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var fee) && fee >= 0
            ? Money.Round(fee)
            : 5.00m;
    }

    // ---------------------------------------------------------------- start

    public async Task<CheckoutQuote> StartAsync(string buyerId, int connectionId, IReadOnlyList<CheckoutLine>? lines,
        decimal shippingCost, ShipTo? shipTo, CancellationToken ct)
    {
        if (lines is null || lines.Count == 0) throw new BankingException(400, "The order has no items.");
        if (lines.Count > MAX_LINES) throw new BankingException(400, $"An order can have at most {MAX_LINES} lines.");
        shippingCost = ValidateShipping(shippingCost);

        var buyer = BankConnectionService.NormalizeBuyer(buyerId);
        var connection = await _db.BankConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == connectionId && c.BuyerId == buyer, ct)
            ?? throw new BankingException(404, "No such bank connection.");
        if (connection.Kind != BankConnection.KIND_BANK || connection.AccountId is null)
        {
            throw new BankingException(422, "That connection is not a bank account that can pay for an order.");
        }

        var merged = new Dictionary<int, int>();
        foreach (var line in lines)
        {
            if (line.Quantity < 1 || line.Quantity > MAX_UNITS)
            {
                throw new BankingException(400, $"Quantity of item {line.CatalogItemId} must be between 1 and {MAX_UNITS}.");
            }
            merged[line.CatalogItemId] = checked((merged.TryGetValue(line.CatalogItemId, out var q) ? q : 0) + line.Quantity);
            if (merged[line.CatalogItemId] > MAX_UNITS)
            {
                throw new BankingException(400, $"Quantity of item {line.CatalogItemId} must be between 1 and {MAX_UNITS}.");
            }
        }

        var ids = merged.Keys.ToList();
        var catalog = await _db.CatalogItems.AsNoTracking().Where(i => ids.Contains(i.Id)).ToListAsync(ct);
        var missing = ids.Where(id => catalog.All(c => c.Id != id)).ToList();
        if (missing.Count > 0)
        {
            throw new BankingException(404, $"Unknown catalog item(s): {string.Join(", ", missing)}.");
        }

        // Prices always come from the catalog, never from the caller.
        var stored = catalog
            .Select(c => new StoredLine(c.Id, c.Name, _uriComposer.ComposePicUri(c.PictureUri), c.Price, merged[c.Id]))
            .OrderBy(l => l.CatalogItemId)
            .ToList();
        var subtotal = Money.Round(stored.Sum(l => l.UnitPrice * l.Units));

        var checkout = new BankCheckout
        {
            BuyerId = buyer,
            ConnectionId = connection.Id,
            ItemsJson = JsonSerializer.Serialize(stored),
            ShipToJson = JsonSerializer.Serialize(shipTo ?? new ShipTo(null, null, null, null, null)),
            Subtotal = subtotal,
            ShippingCost = shippingCost,
        };

        var quote = Quote(checkout);
        if (quote.Total <= 0) throw new BankingException(400, "The order total must be greater than zero.");
        EnsureCanBePaid(quote);

        _db.BankCheckouts.Add(checkout);
        await _db.SaveChangesAsync(ct);
        return quote;
    }

    // ------------------------------------------------------------- shipping

    public async Task<CheckoutQuote> ChangeShippingAsync(string buyerId, Guid checkoutId, decimal shippingCost, CancellationToken ct)
    {
        shippingCost = ValidateShipping(shippingCost);
        using var _ = await _locks.AcquireAsync($"checkout-{checkoutId:N}", ct);

        var checkout = await FindCheckoutAsync(buyerId, checkoutId, ct);
        if (checkout.Status != BankCheckout.STATUS_STARTED)
        {
            throw new BankingException(409, $"The payment is already {checkout.Status}; its shipping can't change.");
        }

        checkout.ShippingCost = shippingCost;
        var quote = Quote(checkout);
        EnsureCanBePaid(quote);
        await _db.SaveChangesAsync(ct);
        return quote;
    }

    // -------------------------------------------------------------- confirm

    public async Task<OrderConfirmation> ConfirmAsync(string buyerId, Guid checkoutId, string? speed, CancellationToken ct)
    {
        var chosen = speed?.Trim().ToLowerInvariant();
        if (chosen is not (SPEED_STANDARD or SPEED_EXPRESS))
        {
            throw new BankingException(400, "speed must be 'standard' or 'express'.");
        }

        using var _ = await _locks.AcquireAsync($"checkout-{checkoutId:N}", ct);
        var checkout = await FindCheckoutAsync(buyerId, checkoutId, ct);

        if (checkout.Status == BankCheckout.STATUS_CONFIRMED)
        {
            // A retried confirm must not charge twice; answer with the original confirmation.
            if (checkout.Speed == chosen) return ToConfirmation(checkout);
            throw new BankingException(409, "The payment is already confirmed.");
        }
        if (checkout.Status != BankCheckout.STATUS_STARTED)
        {
            throw new BankingException(409, $"The payment is {checkout.Status}.");
        }

        // The shipping may have changed since the payment started: always price from the current total.
        var quote = Quote(checkout);
        EnsureCanBePaid(quote);
        if (!quote.Options.Contains(chosen))
        {
            throw new BankingException(422, chosen == SPEED_EXPRESS
                ? $"Express is not available for this order (same-day payments are limited to {EXPRESS_LIMIT:N2})."
                : "Standard payment is not available for this order.");
        }

        var connection = await _db.BankConnections.FirstOrDefaultAsync(c => c.Id == checkout.ConnectionId, ct)
            ?? throw new BankingException(409, "The bank connection was disconnected; pick a connected bank and start again.");
        var accessToken = _protector.Unprotect(connection);

        var fee = chosen == SPEED_EXPRESS ? _expressFee : 0m;
        var charge = Money.Round(quote.Total + fee);
        var network = chosen == SPEED_EXPRESS ? TransferNetwork.SameDayAch : TransferNetwork.Ach;
        var amount = Money.ToPlaid(charge);

        var authorization = await PlaidCall.Run(() => _plaid.TransferAuthorizationCreate(
            new TransferAuthorizationCreateOperationRequest
            {
                Body = new TransferAuthorizationCreateRequest
                {
                    AccessToken = accessToken,
                    AccountId = connection.AccountId!,
                    Type = TransferType.Debit,
                    Network = network,
                    Amount = amount,
                    AchClass = AchClass.Ppd,
                    User = new TransferAuthorizationUserInRequest { LegalName = buyerId.Trim() },
                },
            }, cancellationToken: ct));

        if (authorization.Authorization.Decision != TransferAuthorizationDecision.Approved)
        {
            var rationale = authorization.Authorization.DecisionRationale;
            throw new BankingException(422,
                $"The bank payment was declined: {rationale?.Description ?? rationale?.Code?.ToString() ?? "the bank did not approve it"}.");
        }

        var created = await PlaidCall.Run(() => _plaid.TransferCreate(
            new TransferCreateOperationRequest
            {
                Body = new TransferCreateRequest
                {
                    AccessToken = accessToken,
                    AccountId = connection.AccountId!,
                    AuthorizationId = authorization.Authorization.Id,
                    Description = "eShop order",
                },
            }, cancellationToken: ct));
        var transfer = created.Transfer;

        var order = BuildOrder(checkout);
        try
        {
            // The order's id is generated when it is saved, so save it first and then link it to the payment.
            _db.Orders.Add(order);
            await _db.SaveChangesAsync(ct);

            checkout.Status = BankCheckout.STATUS_CONFIRMED;
            checkout.Speed = chosen;
            checkout.Fee = fee;
            checkout.TotalCharged = charge;
            checkout.AuthorizationId = authorization.Authorization.Id;
            checkout.TransferId = transfer.Id;
            checkout.ArrivesOn = ArrivalDate(transfer, chosen);
            checkout.ConfirmedAt = DateTimeOffset.UtcNow;
            checkout.OrderId = order.Id;
            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            // The shopper was charged but the order wasn't recorded: undo both.
            checkout.Status = BankCheckout.STATUS_STARTED;
            checkout.OrderId = null;
            try
            {
                if (order.Id != 0)
                {
                    _db.Orders.Remove(order);
                    await _db.SaveChangesAsync(CancellationToken.None);
                }
            }
            catch (Exception)
            {
                // Best effort.
            }
            try
            {
                await _plaid.TransferCancel(new TransferCancelOperationRequest
                {
                    Body = new TransferCancelRequest { TransferId = transfer.Id },
                });
            }
            catch (Exception)
            {
                // Nothing more can be done here; the original failure is the one to report.
            }
            throw;
        }

        return ToConfirmation(checkout);
    }

    // --------------------------------------------------------------- cancel

    public async Task<OrderCancellation> CancelOrderAsync(string buyerId, int orderId, CancellationToken ct)
    {
        var buyer = BankConnectionService.NormalizeBuyer(buyerId);
        var checkout = await _db.BankCheckouts.AsNoTracking().FirstOrDefaultAsync(c => c.OrderId == orderId, ct);
        if (checkout is null)
        {
            var order = await _db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, ct);
            if (order is not null && BankConnectionService.NormalizeBuyer(order.BuyerId) == buyer)
            {
                throw new BankingException(409, "This order was not paid by bank.");
            }
            throw new BankingException(404, "No such order.");
        }
        if (checkout.BuyerId != buyer) throw new BankingException(404, "No such order.");

        using var _ = await _locks.AcquireAsync($"checkout-{checkout.CheckoutId:N}", ct);
        var current = await FindCheckoutAsync(buyerId, checkout.CheckoutId, ct);

        if (current.Status == BankCheckout.STATUS_CANCELLED) throw new BankingException(409, "The order is already cancelled.");
        if (current.Status != BankCheckout.STATUS_CONFIRMED || current.TransferId is null)
        {
            throw new BankingException(409, "The order was not paid.");
        }
        if (current.ShippedAt is not null) throw new BankingException(409, "The order has already shipped and can't be cancelled.");

        var (method, refunded, refundId) = await ReverseChargeAsync(current, ct);

        current.Status = BankCheckout.STATUS_CANCELLED;
        current.CancelledAt = DateTimeOffset.UtcNow;
        current.RefundId = refundId;
        await _db.SaveChangesAsync(ct);
        return new OrderCancellation(orderId, BankCheckout.STATUS_CANCELLED, refunded, method);
    }

    /// <summary>Gives the shopper everything they paid back: cancels the debit if it hasn't left yet, otherwise refunds it.</summary>
    private async Task<(string Method, decimal Refunded, string? RefundId)> ReverseChargeAsync(BankCheckout checkout, CancellationToken ct)
    {
        var transferId = checkout.TransferId!;
        var transfer = (await PlaidCall.Run(() => _plaid.TransferGet(
            new TransferGetOperationRequest { Body = new TransferGetRequest { TransferId = transferId } },
            cancellationToken: ct))).Transfer;

        // The debit never completed: no money moved, or it was already returned to the shopper.
        if (transfer.Status == TransferStatus.Cancelled || transfer.Status == TransferStatus.Failed || transfer.Status == TransferStatus.Returned)
        {
            return ("none", 0m, null);
        }

        if (transfer.Cancellable)
        {
            try
            {
                await PlaidCall.Run(() => _plaid.TransferCancel(
                    new TransferCancelOperationRequest { Body = new TransferCancelRequest { TransferId = transferId } },
                    cancellationToken: ct));
                return ("cancelled", 0m, null);
            }
            catch (PlaidCallException ex) when (ex.ErrorCode == "TRANSFER_NOT_CANCELLABLE")
            {
                // It left in the meantime; refund it instead.
            }
        }

        // Refund everything charged, including the express fee.
        var already = transfer.Refunds.Where(r => r.Status != TransferRefundStatus.Failed && r.Status != TransferRefundStatus.Cancelled)
            .Select(r => decimal.Parse(r.Amount, System.Globalization.CultureInfo.InvariantCulture)).Sum();
        var remaining = Money.Round(checkout.TotalCharged - already);
        if (remaining <= 0)
        {
            return ("refunded", checkout.TotalCharged, transfer.Refunds.FirstOrDefault()?.Id);
        }

        try
        {
            var refund = await PlaidCall.Run(() => _plaid.TransferRefundCreate(
                new TransferRefundCreateOperationRequest
                {
                    Body = new TransferRefundCreateRequest
                    {
                        TransferId = transferId,
                        Amount = Money.ToPlaid(remaining),
                        IdempotencyKey = Key("refund", checkout.CheckoutId, "full", Money.ToPlaid(remaining)),
                    },
                }, cancellationToken: ct));
            return ("refunded", checkout.TotalCharged, refund.Refund.Id);
        }
        catch (PlaidCallException ex)
        {
            throw new BankingException(409, $"The order could not be cancelled because the refund failed. {ex.Message}", ex);
        }
    }

    // -------------------------------------------------------------- helpers

    private CheckoutQuote Quote(BankCheckout checkout)
    {
        var total = Money.Round(checkout.Subtotal + checkout.ShippingCost);
        var options = new List<string>();
        if (total <= STANDARD_LIMIT) options.Add(SPEED_STANDARD);
        if (total + _expressFee <= EXPRESS_LIMIT) options.Add(SPEED_EXPRESS);
        return new CheckoutQuote(checkout.CheckoutId, checkout.ConnectionId, checkout.Subtotal, checkout.ShippingCost, total, options, _expressFee);
    }

    private static void EnsureCanBePaid(CheckoutQuote quote)
    {
        if (quote.Options.Count == 0)
        {
            throw new BankingException(422, $"An order of {quote.Total:N2} is more than a bank payment can carry ({STANDARD_LIMIT:N2}).");
        }
    }

    private static decimal ValidateShipping(decimal shippingCost)
    {
        if (shippingCost < 0) throw new BankingException(400, "The shipping cost can't be negative.");
        if (shippingCost > STANDARD_LIMIT) throw new BankingException(400, "The shipping cost is too large.");
        return Money.Round(shippingCost);
    }

    private async Task<BankCheckout> FindCheckoutAsync(string buyerId, Guid checkoutId, CancellationToken ct)
    {
        var buyer = BankConnectionService.NormalizeBuyer(buyerId);
        return await _db.BankCheckouts.FirstOrDefaultAsync(c => c.CheckoutId == checkoutId && c.BuyerId == buyer, ct)
            ?? throw new BankingException(404, "No such bank payment.");
    }

    private static Order BuildOrder(BankCheckout checkout)
    {
        var lines = JsonSerializer.Deserialize<List<StoredLine>>(checkout.ItemsJson) ?? new List<StoredLine>();
        var items = lines
            .Select(l => new OrderItem(new CatalogItemOrdered(l.CatalogItemId, l.Name, l.PictureUri), l.UnitPrice, l.Units))
            .ToList();

        var ship = JsonSerializer.Deserialize<ShipTo>(checkout.ShipToJson) ?? new ShipTo(null, null, null, null, null);
        var address = new Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Address(
            ship.Street ?? "N/A", ship.City ?? "N/A", ship.State ?? "", ship.Country ?? "N/A", ship.ZipCode ?? "N/A");
        return new Order(checkout.BuyerId, address, items);
    }

    private static OrderConfirmation ToConfirmation(BankCheckout c) =>
        new(c.OrderId ?? 0, c.CheckoutId, c.Speed!, c.Total, c.Fee, c.TotalCharged, c.ArrivesOn ?? DateTime.UtcNow.Date, c.TransferId!);

    /// <summary>The date Plaid expects the funds; falls back to the next business day (same day for express).</summary>
    private static DateTime ArrivalDate(Transfer transfer, string speed)
    {
        // Plaid sends a bare date; the SDK reads it as a local-midnight DateTimeOffset, so use its calendar date.
        if (transfer.ExpectedFundsAvailableDate is { } expected) return expected.Date;
        if (transfer.ExpectedSettlementDate is { } settlement) return settlement.Date;

        var day = DateTime.UtcNow.Date;
        var businessDays = speed == SPEED_EXPRESS ? 0 : 1;
        while (IsWeekend(day)) day = day.AddDays(1);
        while (businessDays > 0)
        {
            day = day.AddDays(1);
            if (!IsWeekend(day)) businessDays--;
        }
        return day;
    }

    private static bool IsWeekend(DateTime d) => d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    private static string Key(string purpose, Guid checkoutId, string speed, string amount)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{purpose}|{checkoutId:N}|{speed}|{amount}"));
        return Convert.ToHexString(hash).ToLowerInvariant().Substring(0, 40);
    }
}
