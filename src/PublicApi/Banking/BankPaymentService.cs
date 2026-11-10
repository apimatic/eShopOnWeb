using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ThePlaidApi;
using ThePlaidApi.Models;
using ThePlaidApi.Models.Enums;
using ThePlaidApi.Requests;

namespace Microsoft.eShopWeb.PublicApi.Banking;

public static class PaymentSpeed
{
    public const string Standard = "standard";
    public const string Express = "express";

    public static string Parse(string? speed) => (speed?.Trim().ToLowerInvariant()) switch
    {
        Standard => Standard,
        Express => Express,
        _ => throw new RequestFailedException(400, "speed must be 'standard' or 'express'."),
    };
}

public record CheckoutLineRequest(int CatalogItemId, int Quantity);

public record ShipToRequest(string? Street, string? City, string? State, string? Country, string? ZipCode);

public record CheckoutLine(int CatalogItemId, string Name, string PictureUri, decimal UnitPrice, int Quantity);

public record CheckoutView(Guid CheckoutId, decimal Subtotal, decimal ShippingCost, decimal Total, decimal ExpressFee, List<string> Options);

public record ConfirmationView(int OrderId, Guid CheckoutId, string Speed, decimal Total, decimal Fee, decimal Charged, DateTime ArrivesOn, string TransferId);

public record CancellationView(int OrderId, string Status, decimal Refunded, string Method);

/// <summary>Pay by bank: start a checkout, re-price it, confirm it with a speed, cancel the order.</summary>
public class BankPaymentService(
    ThePlaidApiClient plaid,
    CatalogContext db,
    BankConnectionService connections,
    AccessTokenProtector protector,
    KeyedLocks locks,
    IUriComposer uriComposer,
    IOptions<BankPaymentSettings> settings)
{
    private const string TransferDescription = "eShopOnWeb"; // Plaid allows 15 characters

    private BankPaymentSettings Settings => settings.Value;

    /// <summary>
    /// The speeds the order can be paid with. Standard always; express moves the money through same-day ACH, whose
    /// network limit applies to what is charged, fee included - so very large (wholesale) orders can still be paid, just not express.
    /// </summary>
    public List<string> OptionsFor(decimal total)
    {
        var options = new List<string> { PaymentSpeed.Standard };
        if (total + Settings.ExpressFee <= Settings.SameDayLimit)
        {
            options.Add(PaymentSpeed.Express);
        }

        return options;
    }

    private decimal FeeFor(string speed) => speed == PaymentSpeed.Express ? Settings.ExpressFee : 0m;

    private CheckoutView ToView(BankCheckout checkout) =>
        new(checkout.Id, checkout.Subtotal, checkout.ShippingCost, checkout.Total, Settings.ExpressFee, OptionsFor(checkout.Total));

    public async Task<CheckoutView> StartAsync(string userName, int? connectionId, IReadOnlyList<CheckoutLineRequest>? items, decimal? shippingCost, ShipToRequest? shipTo)
    {
        if (connectionId is null)
        {
            throw new RequestFailedException(400, "connectionId is required.");
        }

        if (items is null || items.Count == 0)
        {
            throw new RequestFailedException(400, "items cannot be empty.");
        }

        if (shippingCost is null)
        {
            throw new RequestFailedException(400, "shippingCost is required.");
        }

        ValidateShipping(shippingCost.Value);

        var connection = await connections.FindAsync(userName, connectionId.Value);
        if (connection.Kind != BankConnectionKind.Bank || connection.AccountId is null)
        {
            throw new RequestFailedException(422, "That connection is not a bank account that can pay for an order.");
        }

        if (items.Any(i => i.Quantity < 1))
        {
            throw new RequestFailedException(400, "Every item needs a quantity of at least 1.");
        }

        var wanted = items.GroupBy(i => i.CatalogItemId).Select(g => (Id: g.Key, Quantity: g.Sum(i => (long)i.Quantity))).ToList();
        if (wanted.Any(w => w.Quantity > 1_000_000))
        {
            throw new RequestFailedException(400, "That quantity is too large.");
        }

        var ids = wanted.Select(w => w.Id).ToList();
        var catalog = await db.CatalogItems.Where(c => ids.Contains(c.Id)).ToListAsync();
        var missing = ids.Except(catalog.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
        {
            throw new RequestFailedException(400, $"Unknown catalog item(s): {string.Join(", ", missing)}.");
        }

        // Prices come from the catalog, never from the caller.
        var lines = wanted.Select(w =>
        {
            var item = catalog.First(c => c.Id == w.Id);
            return new CheckoutLine(item.Id, item.Name, uriComposer.ComposePicUri(item.PictureUri), item.Price, (int)w.Quantity);
        }).ToList();

        var subtotal = lines.Sum(l => l.UnitPrice * l.Quantity);
        if (subtotal + shippingCost.Value <= 0)
        {
            throw new RequestFailedException(422, "There is nothing to pay for: the order total is zero.");
        }

        var checkout = new BankCheckout(userName, connection.Id, JsonSerializer.Serialize(lines), subtotal, shippingCost.Value,
            shipTo?.Street ?? "", shipTo?.City ?? "", shipTo?.State ?? "", shipTo?.Country ?? "", shipTo?.ZipCode ?? "");
        db.BankCheckouts.Add(checkout);
        await db.SaveChangesAsync();
        return ToView(checkout);
    }

    public async Task<CheckoutView> ChangeShippingAsync(string userName, Guid checkoutId, decimal? shippingCost)
    {
        if (shippingCost is null)
        {
            throw new RequestFailedException(400, "shippingCost is required.");
        }

        ValidateShipping(shippingCost.Value);

        using var _ = await locks.AcquireAsync($"checkout:{checkoutId}");
        var checkout = await FindCheckoutAsync(userName, checkoutId);
        if (checkout.IsConfirmed)
        {
            throw new RequestFailedException(409, "That checkout is already confirmed; its shipping cannot change.");
        }

        if (checkout.Subtotal + shippingCost.Value <= 0)
        {
            throw new RequestFailedException(422, "There is nothing to pay for: the order total is zero.");
        }

        checkout.ChangeShipping(shippingCost.Value);
        await db.SaveChangesAsync();
        return ToView(checkout);
    }

    public async Task<ConfirmationView> ConfirmAsync(string userName, Guid checkoutId, string? speedText)
    {
        var speed = PaymentSpeed.Parse(speedText);

        using var _ = await locks.AcquireAsync($"checkout:{checkoutId}");
        var checkout = await FindCheckoutAsync(userName, checkoutId);

        if (checkout.IsConfirmed)
        {
            // A retry of a confirmation that went through must not charge twice.
            if (checkout.ConfirmedSpeed != speed)
            {
                throw new RequestFailedException(409, $"That checkout was already confirmed with {checkout.ConfirmedSpeed} speed.");
            }

            var payment = await db.BankPayments.FirstAsync(p => p.OrderId == checkout.OrderId!.Value);
            return ToConfirmation(checkout, payment);
        }

        var total = checkout.Total;
        if (!OptionsFor(total).Contains(speed))
        {
            throw new RequestFailedException(422, $"{speed} is not available for an order of {Money.ToPlaid(total)}; available: {string.Join(", ", OptionsFor(total))}.");
        }

        var connection = await db.BankConnections.FirstOrDefaultAsync(c => c.Id == checkout.ConnectionId && c.UserName == userName)
            ?? throw new RequestFailedException(409, "The bank connection of this checkout was disconnected; start again with another bank.");
        var accessToken = protector.Unprotect(connection);

        var fee = FeeFor(speed);
        var charged = total + fee;

        var authorization = (await PlaidError.Call(() => plaid.TransferAuthorizationCreate(new TransferAuthorizationCreateOperationRequest
        {
            Body = new TransferAuthorizationCreateRequest
            {
                AccessToken = accessToken,
                AccountId = connection.AccountId!,
                Type = TransferType.Debit,
                Network = speed == PaymentSpeed.Express ? TransferNetwork.SameDayAch : TransferNetwork.Ach,
                Amount = Money.ToPlaid(charged),
                AchClass = AchClass.Web,
                User = new TransferAuthorizationUserInRequest
                {
                    LegalName = userName,
                    EmailAddress = userName.Contains('@') ? userName : null,
                },
                UserPresent = true,
                // Same key for the same attempt: a retried confirmation reuses the authorization.
                IdempotencyKey = $"{checkout.Id:N}-{speed[0]}-{(long)(charged * 100)}",
            },
        }))).Authorization;

        if (authorization.Decision == TransferAuthorizationDecision.UserActionRequired)
        {
            throw new RequestFailedException(409, "The bank needs the shopper to sign in again; please reconnect the bank.");
        }

        if (authorization.Decision != TransferAuthorizationDecision.Approved)
        {
            var why = authorization.DecisionRationale?.Description;
            throw new RequestFailedException(402, "The bank payment was declined" + (string.IsNullOrEmpty(why) ? "." : ": " + why));
        }

        var transfer = (await PlaidError.Call(() => plaid.TransferCreate(new TransferCreateOperationRequest
        {
            Body = new TransferCreateRequest
            {
                AccessToken = accessToken,
                AccountId = connection.AccountId!,
                AuthorizationId = authorization.Id,
                Description = TransferDescription,
            },
        }))).Transfer;

        var arrivesOn = ArrivalDate(speed, transfer);
        Order? order = null;
        try
        {
            var lines = JsonSerializer.Deserialize<List<CheckoutLine>>(checkout.ItemsJson)!;
            order = new Order(userName,
                new ApplicationCore.Entities.OrderAggregate.Address(checkout.Street, checkout.City, checkout.State, checkout.Country, checkout.ZipCode),
                lines.Select(l => new OrderItem(new CatalogItemOrdered(l.CatalogItemId, l.Name, l.PictureUri), l.UnitPrice, l.Quantity)).ToList());
            db.Orders.Add(order);
            await db.SaveChangesAsync();

            var payment = new BankPayment(order.Id, userName, transfer.Id, speed, total, fee, arrivesOn);
            db.BankPayments.Add(payment);
            checkout.MarkConfirmed(order.Id, speed);
            await db.SaveChangesAsync();
            return ToConfirmation(checkout, payment);
        }
        catch
        {
            // The money must not move for an order that was not recorded.
            await UndoTransferAsync(transfer.Id, order);
            throw;
        }
    }

    public async Task<CancellationView> CancelOrderAsync(string userName, int orderId)
    {
        using var _ = await locks.AcquireAsync($"order:{orderId}");

        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId && o.BuyerId == userName)
            ?? throw new RequestFailedException(404, $"Order {orderId} was not found.");
        var payment = await db.BankPayments.FirstOrDefaultAsync(p => p.OrderId == orderId)
            ?? throw new RequestFailedException(409, $"Order {orderId} was not paid by bank, so it cannot be cancelled here.");

        if (order.CancelledOn is not null)
        {
            throw new RequestFailedException(409, $"Order {orderId} is already cancelled.");
        }

        if (order.ShippedOn is not null)
        {
            throw new RequestFailedException(409, $"Order {orderId} has already shipped and can no longer be cancelled.");
        }

        var transfer = (await PlaidError.Call(() => plaid.TransferGet(new TransferGetOperationRequest
        {
            Body = new TransferGetRequest { TransferId = payment.TransferId },
        }))).Transfer;

        var refunded = 0m;
        var method = "none";
        var status = transfer.Status;

        if (status == TransferStatus.Cancelled || status == TransferStatus.Failed || status == TransferStatus.Returned)
        {
            // The money never arrived or already went back: nothing to give back.
        }
        else
        {
            var cancelled = false;
            if (transfer.Cancellable)
            {
                // Not sent to the bank yet: stop it, and the shopper is never charged.
                try
                {
                    await PlaidError.Call(() => plaid.TransferCancel(new TransferCancelOperationRequest
                    {
                        Body = new TransferCancelRequest { TransferId = payment.TransferId },
                    }));
                    cancelled = true;
                    method = "cancelled";
                }
                catch (RequestFailedException)
                {
                    // It left in the meantime; fall back to a refund.
                }
            }

            if (!cancelled)
            {
                // Already moving or settled: give back everything that was charged (the fee included).
                var alreadyRefunded = transfer.Refunds
                    .Where(r => r.Status != TransferRefundStatus.Failed && r.Status != TransferRefundStatus.Cancelled)
                    .Sum(r => decimal.Parse(r.Amount, System.Globalization.CultureInfo.InvariantCulture));
                var remaining = decimal.Parse(transfer.Amount, System.Globalization.CultureInfo.InvariantCulture) - alreadyRefunded;
                if (remaining > 0)
                {
                    var refund = (await PlaidError.Call(() => plaid.TransferRefundCreate(new TransferRefundCreateOperationRequest
                    {
                        Body = new TransferRefundCreateRequest
                        {
                            TransferId = payment.TransferId,
                            Amount = Money.ToPlaid(remaining),
                            IdempotencyKey = $"refund-{payment.Id}-{(long)(remaining * 100)}",
                        },
                    }))).Refund;
                    if (refund.Status == TransferRefundStatus.Failed)
                    {
                        throw new RequestFailedException(502, "The bank refused the refund; the order was not cancelled.");
                    }

                    refunded = remaining;
                    method = "refund";
                }
            }

            if (method == "cancelled")
            {
                refunded = payment.Charged;
            }
        }

        order.MarkCancelled();
        payment.MarkRefunded();
        await db.SaveChangesAsync();
        return new CancellationView(order.Id, "cancelled", refunded, method);
    }

    private async Task<BankCheckout> FindCheckoutAsync(string userName, Guid checkoutId) =>
        await db.BankCheckouts.FirstOrDefaultAsync(c => c.Id == checkoutId && c.UserName == userName)
        ?? throw new RequestFailedException(404, $"Checkout {checkoutId} was not found.");

    private static void ValidateShipping(decimal shippingCost)
    {
        if (shippingCost < 0)
        {
            throw new RequestFailedException(400, "shippingCost cannot be negative.");
        }

        if (Money.HasCents(shippingCost))
        {
            throw new RequestFailedException(400, "shippingCost can have at most two decimals.");
        }

        if (shippingCost > 100_000_000m)
        {
            throw new RequestFailedException(400, "shippingCost is too large.");
        }
    }

    private static ConfirmationView ToConfirmation(BankCheckout checkout, BankPayment payment) =>
        new(payment.OrderId, checkout.Id, payment.Speed, payment.Total, payment.Fee, payment.Charged, payment.ArrivesOn, payment.TransferId);

    /// <summary>Express lands the same business day; standard lands when Plaid expects it, a few business days out.</summary>
    private static DateTime ArrivalDate(string speed, Transfer transfer)
    {
        var today = BankDates.Today();
        var express = BankDates.OnOrAfterBusinessDay(today);
        if (speed == PaymentSpeed.Express)
        {
            return transfer.ExpectedSettlementDate is { } settle ? BankDates.CalendarDate(settle) : express;
        }

        DateTime? planned = transfer.ExpectedSettlementDate is { } s ? BankDates.CalendarDate(s)
            : transfer.ExpectedFundsAvailableDate is { } f ? BankDates.CalendarDate(f)
            : null;
        var standard = planned ?? BankDates.AddBusinessDays(today, 3);
        return standard > express ? standard : BankDates.AddBusinessDays(express, 1);
    }

    private async Task UndoTransferAsync(string transferId, Order? order)
    {
        try
        {
            await plaid.TransferCancel(new TransferCancelOperationRequest { Body = new TransferCancelRequest { TransferId = transferId } });
        }
        catch (Exception)
        {
            // best effort; the transfer is cancellable only until it is sent
        }

        if (order is not null && order.Id != 0)
        {
            try
            {
                db.ChangeTracker.Clear();
                db.Orders.Remove(order);
                await db.SaveChangesAsync();
            }
            catch (Exception)
            {
                // best effort
            }
        }
    }
}
