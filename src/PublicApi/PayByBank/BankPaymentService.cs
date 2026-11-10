using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using ThePlaidApi.Models;
using ThePlaidApi.Models.Enums;
using ThePlaidApi.Requests;

namespace Microsoft.eShopWeb.PublicApi.PayByBank;

public record BankPaymentItemLine(int CatalogItemId, int Quantity);

public record CheckoutDto(
    Guid CheckoutId,
    decimal Subtotal,
    decimal ShippingCost,
    decimal Total,
    string Currency,
    IReadOnlyList<string> Options,
    decimal ExpressFee);

public record ConfirmationDto(int OrderId, string Speed, decimal Fee, decimal Total, decimal Charged, DateOnly? ArrivesOn);

public record CancellationDto(int OrderId, string Status, decimal Refunded);

public class BankPaymentService
{
    private const string Currency = "USD";
    private const string TransferDescription = "eShop order"; // Plaid allows 15 characters

    private readonly CatalogContext _db;
    private readonly PlaidGateway _plaid;
    private readonly BankTokenProtector _tokens;
    private readonly PaymentSpeedPolicy _speeds;

    public BankPaymentService(CatalogContext db, PlaidGateway plaid, BankTokenProtector tokens, PaymentSpeedPolicy speeds)
    {
        _db = db;
        _plaid = plaid;
        _tokens = tokens;
        _speeds = speeds;
    }

    // ---------------------------------------------------------------- start

    public async Task<CheckoutDto> StartAsync(string userId, int? connectionId, IReadOnlyList<BankPaymentItemLine>? items, decimal? shippingCost)
    {
        if (connectionId is null) throw Fail.BadRequest("connectionId is required.");
        if (items is null || items.Count == 0) throw Fail.BadRequest("items must not be empty.");
        var shipping = ValidateShipping(shippingCost ?? 0m);

        foreach (var line in items)
        {
            if (line.Quantity < 1) throw Fail.BadRequest($"Quantity for catalog item {line.CatalogItemId} must be at least 1.");
        }
        var wanted = items.GroupBy(i => i.CatalogItemId).ToDictionary(g => g.Key, g => g.Sum(i => (long)i.Quantity));

        var connection = await _db.BankConnections.FirstOrDefaultAsync(c => c.Id == connectionId && c.UserId == userId)
                         ?? throw Fail.NotFound("No such bank connection for this shopper.");
        if (connection.Kind != BankConnectionKind.Bank || string.IsNullOrEmpty(connection.AccountId))
            throw Fail.BadRequest("This connection is not a bank account and cannot pay for orders.");

        var ids = wanted.Keys.ToList();
        var catalog = await _db.CatalogItems.Where(i => ids.Contains(i.Id)).ToListAsync();
        var missing = ids.Where(id => catalog.All(c => c.Id != id)).ToList();
        if (missing.Count > 0) throw Fail.BadRequest($"Unknown catalog item(s): {string.Join(", ", missing)}.");

        var checkout = new BankCheckout { UserId = userId, ConnectionId = connection.Id, ShippingCost = shipping };
        foreach (var item in catalog)
        {
            var quantity = wanted[item.Id];
            if (quantity > int.MaxValue) throw Fail.BadRequest($"Quantity for catalog item {item.Id} is too large.");
            checkout.Items.Add(new BankCheckoutItem
            {
                CatalogItemId = item.Id,
                ProductName = item.Name,
                PictureUri = string.IsNullOrEmpty(item.PictureUri) ? "-" : item.PictureUri,
                UnitPrice = item.Price,
                Quantity = (int)quantity
            });
        }
        checkout.Subtotal = Money.Round(checkout.Items.Sum(i => i.UnitPrice * i.Quantity));
        EnsureChargeable(checkout);

        _db.BankCheckouts.Add(checkout);
        await _db.SaveChangesAsync();
        return ToDto(checkout);
    }

    // ------------------------------------------------------------- shipping

    public async Task<CheckoutDto> ChangeShippingAsync(string userId, Guid checkoutId, decimal? shippingCost)
    {
        if (shippingCost is null) throw Fail.BadRequest("shippingCost is required.");
        var shipping = ValidateShipping(shippingCost.Value);

        using var _ = await KeyedLock.EnterAsync("checkout:" + checkoutId);
        var checkout = await LoadCheckoutAsync(userId, checkoutId);
        if (checkout.Status != BankCheckoutStatus.Open)
            throw Fail.Conflict("This checkout was already confirmed; its shipping can no longer change.");

        checkout.ShippingCost = shipping;
        EnsureChargeable(checkout);
        await _db.SaveChangesAsync();
        return ToDto(checkout);
    }

    // -------------------------------------------------------------- confirm

    public async Task<ConfirmationDto> ConfirmAsync(string userId, Guid checkoutId, string? speedText)
    {
        var speed = speedText?.Trim().ToLowerInvariant();
        if (!PaymentSpeedPolicy.IsKnown(speed))
            throw Fail.BadRequest("speed must be 'standard' or 'express'.");

        using var _ = await KeyedLock.EnterAsync("checkout:" + checkoutId);
        var checkout = await LoadCheckoutAsync(userId, checkoutId);

        if (checkout.Status == BankCheckoutStatus.Confirmed)
        {
            // A retried confirm must not charge twice: answer with what already happened.
            var done = await _db.BankPayments.Include(p => p.Transfers).FirstOrDefaultAsync(p => p.CheckoutId == checkoutId);
            if (done != null && done.Speed == speed) return ToConfirmation(done, checkout.Total);
            throw Fail.Conflict("This checkout was already confirmed" + (done != null ? $" with {done.Speed} payment." : "."));
        }

        var offered = _speeds.OptionsFor(checkout.Total);
        if (!offered.Contains(speed!))
            throw Fail.Conflict($"'{speed}' payment is not available for this order right now. Available: {string.Join(", ", offered)}.");

        var connection = await _db.BankConnections.FirstOrDefaultAsync(c => c.Id == checkout.ConnectionId && c.UserId == userId)
                         ?? throw Fail.Conflict("The bank was disconnected; start the payment again with a connected bank.");
        var accessToken = _tokens.Unprotect(connection);

        var fee = _speeds.FeeFor(speed!);
        var charge = checkout.Total + fee;
        var network = speed == PaymentSpeedPolicy.Express ? TransferNetwork.SameDayAch : TransferNetwork.Ach;
        var chunks = Split(charge);

        // Authorise every part first so nothing is charged unless the whole order can be.
        var authorizations = new List<(string Id, decimal Amount)>();
        try
        {
            foreach (var amount in chunks)
            {
                authorizations.Add((await AuthorizeAsync(userId, accessToken, connection.AccountId!, network, amount), amount));
            }
        }
        catch
        {
            await CancelAuthorizationsAsync(authorizations.Select(a => a.Id));
            throw;
        }

        var created = new List<(Transfer Transfer, decimal Amount)>();
        try
        {
            foreach (var (authorizationId, amount) in authorizations)
            {
                var response = await _plaid.Call(c => c.TransferCreate(new TransferCreateOperationRequest
                {
                    Body = new TransferCreateRequest
                    {
                        AccessToken = accessToken,
                        AccountId = connection.AccountId!,
                        AuthorizationId = authorizationId,
                        Description = TransferDescription
                    }
                }));
                created.Add((response.Transfer, amount));
            }

            var arrives = await ArrivalDateAsync(created.Select(c => c.Transfer).ToList());

            var order = new Order(userId, new Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Address("", "", "", "", ""), checkout.Items.Select(i =>
                new OrderItem(new CatalogItemOrdered(i.CatalogItemId, Truncate(i.ProductName, 50), i.PictureUri), i.UnitPrice, i.Quantity)).ToList());
            _db.Orders.Add(order);
            await _db.SaveChangesAsync();

            var payment = new BankPayment
            {
                OrderId = order.Id,
                CheckoutId = checkout.Id,
                UserId = userId,
                Speed = speed!,
                Fee = fee,
                Charged = charge,
                ArrivesOn = arrives?.ToDateTime(TimeOnly.MinValue)
            };
            foreach (var (transfer, amount) in created)
            {
                payment.Transfers.Add(new BankPaymentTransfer { PlaidTransferId = transfer.Id, Amount = amount, Network = network == TransferNetwork.SameDayAch ? "same-day-ach" : "ach" });
            }
            _db.BankPayments.Add(payment);
            checkout.Status = BankCheckoutStatus.Confirmed;
            checkout.OrderId = order.Id;
            await _db.SaveChangesAsync();
            return ToConfirmation(payment, checkout.Total);
        }
        catch
        {
            // The shopper must not be charged for an order we could not record.
            await UndoTransfersAsync(created.Select(c => c.Transfer.Id));
            await CancelAuthorizationsAsync(authorizations.Where(a => created.All(c => c.Transfer.AuthorizationId != a.Id)).Select(a => a.Id));
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    // --------------------------------------------------------------- cancel

    public async Task<CancellationDto> CancelOrderAsync(string userId, int orderId)
    {
        var payment = await _db.BankPayments.Include(p => p.Transfers)
                          .FirstOrDefaultAsync(p => p.OrderId == orderId && p.UserId == userId)
                      ?? throw Fail.NotFound("No order paid by bank with this id for this shopper.");
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId)
                    ?? throw Fail.NotFound("No order paid by bank with this id for this shopper.");

        using var _ = await KeyedLock.EnterAsync("order:" + orderId);
        await _db.Entry(payment).ReloadAsync();
        await _db.Entry(order).ReloadAsync();
        await _db.Entry(payment).Collection(p => p.Transfers).LoadAsync();

        if (order.ShippedOn != null) throw Fail.Conflict("This order has already shipped and can no longer be cancelled.");

        // Every transfer is returned in full (the express fee rides in the same debit). Safe to repeat:
        // finished transfers are skipped and refunds carry a fixed idempotency key.
        foreach (var transfer in payment.Transfers.Where(t => t.ReversedAt == null))
        {
            await ReverseAsync(transfer);
            transfer.ReversedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync();
        }

        order.MarkCancelled();
        await _db.SaveChangesAsync();
        return new CancellationDto(orderId, "cancelled", payment.Charged);
    }

    private async Task ReverseAsync(BankPaymentTransfer record)
    {
        var current = (await _plaid.Call(c => c.TransferGet(new TransferGetOperationRequest
        {
            Body = new TransferGetRequest { TransferId = record.PlaidTransferId }
        }))).Transfer;

        if (current.Status == TransferStatus.Cancelled || current.Status == TransferStatus.Failed || current.Status == TransferStatus.Returned)
            return; // no money is (or stays) with the shop

        if (current.Cancellable)
        {
            try
            {
                await _plaid.Call(c => c.TransferCancel(new TransferCancelOperationRequest
                {
                    Body = new TransferCancelRequest { TransferId = record.PlaidTransferId }
                }));
                return;
            }
            catch (PlaidApiException ex) when (ex.ErrorCode == "TRANSFER_NOT_CANCELLABLE")
            {
                // It moved on to the network in the meantime: refund instead.
                current = (await _plaid.Call(c => c.TransferGet(new TransferGetOperationRequest
                {
                    Body = new TransferGetRequest { TransferId = record.PlaidTransferId }
                }))).Transfer;
            }
        }

        var alreadyRefunded = current.Refunds
            .Where(r => r.Status != TransferRefundStatus.Failed && r.Status != TransferRefundStatus.Cancelled)
            .Sum(r => Money.ParsePlaid(r.Amount));
        var remaining = Money.ParsePlaid(current.Amount) - alreadyRefunded;
        if (remaining <= 0) return;

        await _plaid.Call(c => c.TransferRefundCreate(new TransferRefundCreateOperationRequest
        {
            Body = new TransferRefundCreateRequest
            {
                TransferId = record.PlaidTransferId,
                Amount = Money.ToPlaid(remaining),
                IdempotencyKey = "refund-" + record.PlaidTransferId
            }
        }));
    }

    // -------------------------------------------------------------- helpers

    private async Task<string> AuthorizeAsync(string userId, string accessToken, string accountId, TransferNetwork network, decimal amount)
    {
        var response = await _plaid.Call(c => c.TransferAuthorizationCreate(new TransferAuthorizationCreateOperationRequest
        {
            Body = new TransferAuthorizationCreateRequest
            {
                AccessToken = accessToken,
                AccountId = accountId,
                Type = TransferType.Debit,
                Network = network,
                Amount = Money.ToPlaid(amount),
                AchClass = AchClass.Web,
                IsoCurrencyCode = Currency,
                WithGuarantee = null,
                User = new TransferAuthorizationUserInRequest
                {
                    LegalName = userId,
                    EmailAddress = userId.Contains('@') ? userId : null
                }
            }
        }));

        var authorization = response.Authorization;
        if (authorization.Decision == TransferAuthorizationDecision.Approved) return authorization.Id;

        var why = authorization.DecisionRationale?.Description ?? authorization.DecisionRationale?.Code?.ToString();
        if (authorization.Decision == TransferAuthorizationDecision.UserActionRequired)
            throw new PayByBankException(HttpStatusCode.Conflict, "The bank needs the shopper to take action before it can be charged" + (why != null ? $": {why}" : "."));
        throw new PayByBankException(HttpStatusCode.PaymentRequired, "The bank declined the payment" + (why != null ? $": {why}" : "."));
    }

    private async Task CancelAuthorizationsAsync(IEnumerable<string> authorizationIds)
    {
        foreach (var id in authorizationIds)
        {
            try
            {
                await _plaid.Call(c => c.TransferAuthorizationCancel(new TransferAuthorizationCancelOperationRequest
                {
                    Body = new TransferAuthorizationCancelRequest { AuthorizationId = id }
                }));
            }
            catch (PayByBankException) { /* best effort */ }
        }
    }

    private async Task UndoTransfersAsync(IEnumerable<string> transferIds)
    {
        foreach (var id in transferIds)
        {
            try { await ReverseAsync(new BankPaymentTransfer { PlaidTransferId = id }); }
            catch (PayByBankException) { /* best effort; Plaid transfer events will show the leftover */ }
        }
    }

    private async Task<DateOnly?> ArrivalDateAsync(IReadOnlyList<Transfer> transfers)
    {
        DateOnly? latest = null;
        foreach (var t in transfers)
        {
            var date = t.ExpectedFundsAvailableDate ?? t.ExpectedSettlementDate;
            if (date == null)
            {
                try
                {
                    var fresh = (await _plaid.Call(c => c.TransferGet(new TransferGetOperationRequest
                    {
                        Body = new TransferGetRequest { TransferId = t.Id }
                    }))).Transfer;
                    date = fresh.ExpectedFundsAvailableDate ?? fresh.ExpectedSettlementDate;
                }
                catch (PayByBankException) { }
            }
            if (date == null) continue;
            var d = DateOnly.FromDateTime(date.Value.DateTime);
            if (latest == null || d > latest) latest = d;
        }
        return latest;
    }

    /// <summary>An order bigger than one ACH transfer may hold is charged as several transfers.</summary>
    public static IReadOnlyList<decimal> Split(decimal charge)
    {
        var parts = new List<decimal>();
        var remaining = charge;
        while (remaining > PaymentSpeedPolicy.TransferLimit)
        {
            parts.Add(PaymentSpeedPolicy.TransferLimit);
            remaining -= PaymentSpeedPolicy.TransferLimit;
        }
        parts.Add(remaining);
        return parts;
    }

    private async Task<BankCheckout> LoadCheckoutAsync(string userId, Guid checkoutId)
    {
        var checkout = await _db.BankCheckouts.Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.Id == checkoutId && c.UserId == userId);
        return checkout ?? throw Fail.NotFound("No such bank payment for this shopper.");
    }

    private static decimal ValidateShipping(decimal shipping)
    {
        if (shipping < 0) throw Fail.BadRequest("shippingCost must not be negative.");
        if (shipping != Money.Round(shipping)) throw Fail.BadRequest("shippingCost must have at most two decimal places.");
        if (shipping >= 1_000_000_000_000m) throw Fail.BadRequest("shippingCost is too large.");
        return shipping;
    }

    private void EnsureChargeable(BankCheckout checkout)
    {
        if (checkout.Total <= 0) throw Fail.BadRequest("The order total must be greater than zero.");
        if (checkout.Total >= 1_000_000_000_000_000m) throw Fail.BadRequest("The order total is too large.");
    }

    private CheckoutDto ToDto(BankCheckout c) =>
        new(c.Id, c.Subtotal, c.ShippingCost, c.Total, Currency, _speeds.OptionsFor(c.Total), _speeds.ExpressFee);

    private static ConfirmationDto ToConfirmation(BankPayment p, decimal total) =>
        new(p.OrderId, p.Speed, p.Fee, total, p.Charged, p.ArrivesOn == null ? null : DateOnly.FromDateTime(p.ArrivesOn.Value));

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
