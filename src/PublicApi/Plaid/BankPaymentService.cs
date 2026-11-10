using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using ThePlaidApi;
using ThePlaidApi.Models;
using ThePlaidApi.Models.Enums;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

public record AddressRequest(string? Street, string? City, string? State, string? Country, string? ZipCode);

public record CheckoutItemRequest(int CatalogItemId, int Quantity);

public record StartBankPaymentRequest(
    int ConnectionId,
    IReadOnlyList<CheckoutItemRequest>? Items,
    decimal? ShippingCost,
    AddressRequest? ShipToAddress);

public record ChangeShippingRequest(decimal? ShippingCost);

public record ConfirmBankPaymentRequest(string? Speed);

public record BankPaymentQuoteDto(
    Guid CheckoutId,
    decimal ItemsTotal,
    decimal ShippingCost,
    decimal Total,
    string Currency,
    IReadOnlyList<string> Options,
    decimal ExpressFee);

public record OrderConfirmationDto(
    int OrderId,
    Guid CheckoutId,
    string Speed,
    decimal Total,
    decimal Fee,
    decimal TotalCharged,
    DateOnly ArrivesOn);

public record OrderCancellationDto(int OrderId, string Status, decimal Refunded);

public class BankPaymentService
{
    public const string Standard = "standard";
    public const string Express = "express";

    /// <summary>The most a single standard ACH transfer can move.</summary>
    public const decimal AchLimit = 10_000_000.00m;

    /// <summary>The most a single same-day ACH transfer can move.</summary>
    public const decimal SameDayAchLimit = 1_000_000.00m;

    // A guard against absurd orders, not a business limit: this is $10 billion of standard transfers.
    private const int MaxTransfersPerPayment = 1000;

    private readonly ThePlaidApi.ThePlaidApiClient _plaid;
    private readonly CatalogContext _db;
    private readonly AccessTokenProtector _protector;
    private readonly BankConnectionService _connections;
    private readonly IUriComposer _uriComposer;
    private readonly PerKeyLock _locks;
    private readonly BankPaymentSettings _settings;

    public BankPaymentService(ThePlaidApi.ThePlaidApiClient plaid, CatalogContext db, AccessTokenProtector protector,
        BankConnectionService connections, IUriComposer uriComposer, PerKeyLock locks,
        IOptions<BankPaymentSettings> settings)
    {
        _plaid = plaid;
        _db = db;
        _protector = protector;
        _connections = connections;
        _uriComposer = uriComposer;
        _locks = locks;
        _settings = settings.Value;
    }

    // ---------------------------------------------------------------- start

    public async Task<BankPaymentQuoteDto> StartAsync(string userName, StartBankPaymentRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items == null || request.Items.Count == 0)
            throw ApiProblemException.BadRequest("items must contain at least one item.");
        var shipping = ValidateShipping(request.ShippingCost);

        var connection = await _connections.GetOwnedAsync(userName, request.ConnectionId, cancellationToken).ConfigureAwait(false);
        if (connection.Kind != BankConnectionKind.Bank || string.IsNullOrEmpty(connection.PaymentAccountId))
            throw ApiProblemException.Unprocessable("This connection is not a bank account that can pay for an order.");

        var wanted = new Dictionary<int, long>();
        foreach (var item in request.Items)
        {
            if (item.Quantity < 1)
                throw ApiProblemException.BadRequest("Every item needs a quantity of at least 1.");
            wanted[item.CatalogItemId] = wanted.GetValueOrDefault(item.CatalogItemId) + item.Quantity;
        }
        if (wanted.Values.Any(q => q > int.MaxValue))
            throw ApiProblemException.BadRequest("An item quantity is too large.");

        var ids = wanted.Keys.ToList();
        var catalogItems = await _db.CatalogItems.Where(i => ids.Contains(i.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToList();
        if (missing.Count > 0)
            throw ApiProblemException.Unprocessable("These catalog items do not exist: " + string.Join(", ", missing) + ".");

        var address = request.ShipToAddress;
        var checkout = new BankCheckout
        {
            UserName = userName,
            UserKey = CurrentUser.Key(userName),
            BankConnectionId = connection.Id,
            ShippingCost = shipping,
            ShipStreet = Clip(address?.Street, 180) ?? "Not provided",
            ShipCity = Clip(address?.City, 100) ?? "Not provided",
            ShipState = Clip(address?.State, 60) ?? string.Empty,
            ShipCountry = Clip(address?.Country, 90) ?? "Not provided",
            ShipZipCode = Clip(address?.ZipCode, 18) ?? "Not provided"
        };
        foreach (var catalogItem in catalogItems)
        {
            checkout.Items.Add(new BankCheckoutItem
            {
                CatalogItemId = catalogItem.Id,
                ProductName = Clip(catalogItem.Name, 50) ?? "Item",
                PictureUri = _uriComposer.ComposePicUri(catalogItem.PictureUri),
                UnitPrice = catalogItem.Price,
                Units = (int)wanted[catalogItem.Id]
            });
        }

        _db.BankCheckouts.Add(checkout);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Quote(checkout);
    }

    // ------------------------------------------------------------- shipping

    public async Task<BankPaymentQuoteDto> ChangeShippingAsync(string userName, Guid checkoutId, ChangeShippingRequest request,
        CancellationToken cancellationToken)
    {
        var shipping = ValidateShipping(request.ShippingCost);
        using var _ = await _locks.AcquireAsync("checkout:" + checkoutId, cancellationToken).ConfigureAwait(false);
        var checkout = await GetOwnedCheckoutAsync(userName, checkoutId, cancellationToken).ConfigureAwait(false);
        if (checkout.Status != BankCheckoutStatus.Open)
            throw ApiProblemException.Conflict("This payment is already confirmed; its shipping can no longer change.");

        checkout.ShippingCost = shipping;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Quote(checkout);
    }

    // -------------------------------------------------------------- confirm

    public async Task<OrderConfirmationDto> ConfirmAsync(string userName, Guid checkoutId, ConfirmBankPaymentRequest request,
        CancellationToken cancellationToken)
    {
        var speed = request.Speed?.Trim().ToLowerInvariant();
        if (speed != Standard && speed != Express)
            throw ApiProblemException.BadRequest("speed must be 'standard' or 'express'.");

        using var _ = await _locks.AcquireAsync("checkout:" + checkoutId, cancellationToken).ConfigureAwait(false);
        var checkout = await GetOwnedCheckoutAsync(userName, checkoutId, cancellationToken).ConfigureAwait(false);

        if (checkout.Status == BankCheckoutStatus.Confirmed)
        {
            // Confirming twice must not charge twice: answer with the confirmation that already exists.
            var done = await _db.BankPayments.FirstAsync(p => p.CheckoutId == checkoutId, cancellationToken).ConfigureAwait(false);
            return Confirmation(done);
        }

        var quote = Quote(checkout);
        if (!quote.Options.Contains(speed))
            throw ApiProblemException.Unprocessable(
                $"'{speed}' is not available for this order (it would be ${quote.Total + quote.ExpressFee:0.00}); " +
                $"available: {string.Join(", ", quote.Options)}.");

        var connection = await _db.BankConnections.FirstOrDefaultAsync(c => c.Id == checkout.BankConnectionId, cancellationToken)
            .ConfigureAwait(false);
        if (connection == null || string.IsNullOrEmpty(connection.PaymentAccountId))
            throw ApiProblemException.Conflict("The bank for this payment has been disconnected; start the payment again with another bank.");
        var accessToken = _protector.Unprotect(connection.ProtectedAccessToken);

        var fee = speed == Express ? quote.ExpressFee : 0m;
        var charged = quote.Total + fee;
        var network = speed == Express ? TransferNetwork.SameDayAch : TransferNetwork.Ach;
        var chunks = Split(charged, speed == Express ? SameDayAchLimit : AchLimit);
        if (chunks.Count > MaxTransfersPerPayment)
            throw ApiProblemException.Unprocessable("This order is too large to charge in one payment.");

        var created = new List<ThePlaidApi.Models.Transfer>();
        try
        {
            for (var i = 0; i < chunks.Count; i++)
            {
                var amount = chunks[i].ToString("0.00", CultureInfo.InvariantCulture);
                var key = IdempotencyKey(checkoutId, speed, i, amount);

                var authorization = await PlaidCall.RunAsync(() => _plaid.TransferAuthorizationCreate(new()
                {
                    Body = new()
                    {
                        AccessToken = accessToken,
                        AccountId = connection.PaymentAccountId,
                        Type = TransferType.Debit,
                        Network = network,
                        Amount = amount,
                        AchClass = AchClass.Web,
                        IdempotencyKey = key,
                        UserPresent = true,
                        User = new() { LegalName = userName, EmailAddress = userName.Contains('@') ? userName : null }
                    }
                })).ConfigureAwait(false);

                var decision = authorization.Authorization.Decision;
                if (decision != TransferAuthorizationDecision.Approved)
                {
                    var why = authorization.Authorization.DecisionRationale?.Description ?? "the bank declined the payment";
                    throw decision == TransferAuthorizationDecision.UserActionRequired
                        ? ApiProblemException.Conflict($"The bank needs the shopper to sign in again before paying: {why}.")
                        : new ApiProblemException(402, $"The payment was declined: {why}.");
                }

                var transfer = await PlaidCall.RunAsync(() => _plaid.TransferCreate(new()
                {
                    Body = new()
                    {
                        AccessToken = accessToken,
                        AccountId = connection.PaymentAccountId,
                        AuthorizationId = authorization.Authorization.Id,
                        Description = "eShop order"
                    }
                })).ConfigureAwait(false);
                created.Add(transfer.Transfer);
            }

            var order = new Order(checkout.UserName,
                new Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Address(checkout.ShipStreet, checkout.ShipCity, checkout.ShipState, checkout.ShipCountry, checkout.ShipZipCode),
                checkout.Items.Select(i => new OrderItem(
                    new CatalogItemOrdered(i.CatalogItemId, i.ProductName, i.PictureUri), i.UnitPrice, i.Units)).ToList());

            var payment = new BankPayment
            {
                Order = order,
                CheckoutId = checkout.CheckoutId,
                UserName = checkout.UserName,
                UserKey = checkout.UserKey,
                Speed = speed,
                OrderTotal = quote.Total,
                Fee = fee,
                TotalCharged = charged,
                ArrivesOn = ArrivalDate(created, speed).ToDateTime(TimeOnly.MinValue),
                Status = BankPaymentStatus.Paid,
                Transfers = created.Select((t, i) => new BankPaymentTransfer
                {
                    PlaidTransferId = t.Id,
                    Amount = chunks[i],
                    Network = network.Value,
                    State = BankPaymentTransferState.Charged
                }).ToList()
            };
            checkout.Status = BankCheckoutStatus.Confirmed;

            _db.Orders.Add(order);
            _db.BankPayments.Add(payment);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Confirmation(payment);
        }
        catch (Exception)
        {
            // The shopper must not be charged for an order that was not placed: reverse whatever went through.
            foreach (var transfer in created)
                await TryReverseAsync(transfer.Id).ConfigureAwait(false);
            throw;
        }
    }

    // --------------------------------------------------------------- cancel

    public async Task<OrderCancellationDto> CancelOrderAsync(string userName, int orderId, CancellationToken cancellationToken)
    {
        var userKey = CurrentUser.Key(userName);
        var lookup = await _db.BankPayments.AsNoTracking()
            .Where(p => p.OrderId == orderId && p.UserKey == userKey).Select(p => p.Id)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (lookup == 0)
            throw ApiProblemException.NotFound("There is no such order paid by bank.");

        using var _ = await _locks.AcquireAsync("order:" + orderId, cancellationToken).ConfigureAwait(false);
        var payment = await _db.BankPayments.Include(p => p.Transfers)
            .FirstAsync(p => p.Id == lookup, cancellationToken).ConfigureAwait(false);

        if (payment.Status == BankPaymentStatus.Cancelled)
            return new OrderCancellationDto(orderId, "cancelled", payment.TotalCharged);
        if (payment.ShippedAt != null)
            throw ApiProblemException.Conflict("This order has already shipped and can no longer be cancelled.");

        foreach (var transfer in payment.Transfers.Where(t => t.State == BankPaymentTransferState.Charged))
        {
            transfer.State = await ReverseAsync(transfer.PlaidTransferId, transfer.Amount).ConfigureAwait(false);
            // keep progress: a later retry only has to deal with what is left
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        payment.Status = BankPaymentStatus.Cancelled;
        payment.CancelledAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new OrderCancellationDto(orderId, "cancelled", payment.TotalCharged);
    }

    /// <summary>
    /// Gives the shopper the money of one transfer back: cancels it while Plaid still can, refunds it otherwise.
    /// </summary>
    private async Task<BankPaymentTransferState> ReverseAsync(string transferId, decimal amount)
    {
        var current = (await PlaidCall.RunAsync(() => _plaid.TransferGet(new() { Body = new() { TransferId = transferId } }))
            .ConfigureAwait(false)).Transfer;

        if (current.Status == TransferStatus.Cancelled || current.Status == TransferStatus.Failed)
            return BankPaymentTransferState.Cancelled;
        if (current.Status == TransferStatus.Returned)
            return BankPaymentTransferState.Refunded; // the bank already returned the money

        if (current.Cancellable)
        {
            try
            {
                await PlaidCall.RunAsync(() => _plaid.TransferCancel(new() { Body = new() { TransferId = transferId } }))
                    .ConfigureAwait(false);
                return BankPaymentTransferState.Cancelled;
            }
            catch (Exception ex) when (PlaidCall.IsCode(ex, "TRANSFER_NOT_CANCELLABLE"))
            {
                // it moved on in the meantime: fall through to a refund
            }
        }

        var alreadyRefunded = current.Refunds
            .Where(r => r.Status != TransferRefundStatus.Cancelled && r.Status != TransferRefundStatus.Failed)
            .Sum(r => decimal.Parse(r.Amount, CultureInfo.InvariantCulture));
        var remaining = amount - alreadyRefunded;
        if (remaining > 0)
        {
            await PlaidCall.RunAsync(() => _plaid.TransferRefundCreate(new()
            {
                Body = new()
                {
                    TransferId = transferId,
                    Amount = remaining.ToString("0.00", CultureInfo.InvariantCulture),
                    IdempotencyKey = "cancel-" + transferId
                }
            })).ConfigureAwait(false);
        }
        return BankPaymentTransferState.Refunded;
    }

    private async Task TryReverseAsync(string transferId)
    {
        try
        {
            var current = (await _plaid.TransferGet(new() { Body = new() { TransferId = transferId } }).ConfigureAwait(false)).Transfer;
            await ReverseAsync(transferId, decimal.Parse(current.Amount, CultureInfo.InvariantCulture)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // nothing more can be done here; the original failure is the one reported
        }
    }

    // -------------------------------------------------------------- helpers

    private BankPaymentQuoteDto Quote(BankCheckout checkout)
    {
        var items = checkout.ItemsTotal();
        var total = items + checkout.ShippingCost;
        var options = new List<string> { Standard };
        // Same-day ACH can't carry more than its network limit; the fee is part of what is charged.
        if (total + _settings.ExpressFee <= SameDayAchLimit)
            options.Add(Express);
        return new BankPaymentQuoteDto(checkout.CheckoutId, items, checkout.ShippingCost, total, "USD", options, _settings.ExpressFee);
    }

    private static OrderConfirmationDto Confirmation(BankPayment payment) =>
        new(payment.OrderId, payment.CheckoutId, payment.Speed, payment.OrderTotal, payment.Fee, payment.TotalCharged,
            DateOnly.FromDateTime(payment.ArrivesOn));

    private async Task<BankCheckout> GetOwnedCheckoutAsync(string userName, Guid checkoutId, CancellationToken cancellationToken)
    {
        var userKey = CurrentUser.Key(userName);
        var checkout = await _db.BankCheckouts.Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.CheckoutId == checkoutId && c.UserKey == userKey, cancellationToken).ConfigureAwait(false);
        return checkout ?? throw ApiProblemException.NotFound("There is no such bank payment.");
    }

    private static decimal ValidateShipping(decimal? shippingCost)
    {
        if (shippingCost == null)
            throw ApiProblemException.BadRequest("shippingCost is required.");
        if (shippingCost < 0)
            throw ApiProblemException.BadRequest("shippingCost must not be negative.");
        if (decimal.Round(shippingCost.Value, 2) != shippingCost.Value)
            throw ApiProblemException.BadRequest("shippingCost can have at most two decimals.");
        if (shippingCost > 1_000_000_000m)
            throw ApiProblemException.BadRequest("shippingCost is too large.");
        return shippingCost.Value;
    }

    /// <summary>Splits an amount into transfers that each fit within the network limit.</summary>
    public static List<decimal> Split(decimal amount, decimal limit)
    {
        var parts = new List<decimal>();
        var left = amount;
        while (left > limit)
        {
            parts.Add(limit);
            left -= limit;
        }
        parts.Add(left);
        return parts;
    }

    private static DateOnly ArrivalDate(IReadOnlyList<ThePlaidApi.Models.Transfer> transfers, string speed)
    {
        // Plaid's own expected date, kept as the calendar date it names (never shifted by a time zone).
        var dates = transfers.Where(t => t.ExpectedFundsAvailableDate != null)
            .Select(t => DateOnly.FromDateTime(t.ExpectedFundsAvailableDate!.Value.Date)).ToList();
        if (dates.Count > 0)
            return dates.Max();
        return AddBusinessDays(DateOnly.FromDateTime(DateTime.UtcNow), speed == Express ? 0 : 3);
    }

    public static DateOnly AddBusinessDays(DateOnly from, int days)
    {
        var date = from;
        while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            date = date.AddDays(1);
        while (days > 0)
        {
            date = date.AddDays(1);
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                days--;
        }
        return date;
    }

    private static string IdempotencyKey(Guid checkoutId, string speed, int index, string amount)
    {
        // Plaid keys are limited to 50 characters.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{checkoutId:N}|{speed}|{index}|{amount}"));
        return Convert.ToHexString(hash).Substring(0, 40).ToLowerInvariant();
    }

    private static string? Clip(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        value = value.Trim();
        return value.Length <= max ? value : value.Substring(0, max);
    }
}
