using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Options;
using ThePlaidApi.Models;
using ThePlaidApi.Models.Enums;
using ThePlaidApi.Requests;

namespace Microsoft.eShopWeb.PublicApi.Banking;

public sealed record CheckoutItemInput(int CatalogItemId, int Quantity);

public sealed record ShipToInput(string? Street, string? City, string? State, string? Country, string? ZipCode);

public sealed record BankPaymentDto(
    Guid CheckoutId,
    decimal Subtotal,
    decimal ShippingCost,
    decimal Total,
    decimal ExpressFee,
    IReadOnlyList<string> Options);

public sealed record OrderConfirmationDto(
    int OrderId,
    string Speed,
    decimal Total,
    decimal Fee,
    decimal Charged,
    DateOnly? ArrivesOn,
    string TransferId);

public sealed record OrderCancellationDto(int OrderId, string Status, decimal Refunded, string? RefundId);

public sealed class BankPaymentService
{
    /// <summary>Same Day ACH moves at most this much per transfer; bigger orders can only go standard.</summary>
    public const decimal ExpressLimit = 1_000_000m;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    private readonly PlaidGateway _plaid;
    private readonly CatalogContext _db;
    private readonly BankPaymentOptions _options;
    private readonly ShopperResolver _shoppers;

    public BankPaymentService(PlaidGateway plaid, CatalogContext db, IOptions<BankPaymentOptions> options, ShopperResolver shoppers)
    {
        _shoppers = shoppers;
        _plaid = plaid;
        _db = db;
        _options = options.Value;
    }

    // ---- pricing -------------------------------------------------------------------------------------------

    public decimal ExpressFee => _options.ExpressFee;

    /// <summary>The payment speeds available for an order of that total.</summary>
    public IReadOnlyList<string> OptionsFor(decimal total)
    {
        var options = new List<string> { BankPaymentSpeeds.Standard };
        // The express charge is the order plus the fee, and has to fit the same-day network limit.
        if (total + _options.ExpressFee <= ExpressLimit)
            options.Add(BankPaymentSpeeds.Express);
        return options;
    }

    private BankPaymentDto ToDto(BankCheckout c) =>
        new(c.Id, c.Subtotal, c.ShippingCost, c.Total, _options.ExpressFee, OptionsFor(c.Total));

    private static void ValidateShipping(decimal? shippingCost)
    {
        if (shippingCost is null || shippingCost < 0)
            throw new BankingException(400, "shippingCost is required and must not be negative.");
        if (!Money.IsCents(shippingCost.Value))
            throw new BankingException(400, "shippingCost must have at most two decimals.");
    }

    // ---- start ---------------------------------------------------------------------------------------------

    public async Task<BankPaymentDto> StartAsync(ClaimsPrincipal principal, int? connectionId,
        IReadOnlyList<CheckoutItemInput>? items, decimal? shippingCost, string? legalName, ShipToInput? shipTo)
    {
        var user = await _shoppers.RequireAsync(principal);
        if (connectionId is null) throw new BankingException(400, "connectionId is required.");
        if (items is null || items.Count == 0) throw new BankingException(400, "items must not be empty.");
        ValidateShipping(shippingCost);

        var connection = await _db.BankConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == connectionId && c.UserId == user.Id)
            ?? throw new BankingException(404, "No such connection.");
        if (connection.Kind != BankConnectionKinds.Bank || connection.AccountId is null)
            throw new BankingException(422, "That connection is not a bank account that can pay.");

        var units = new Dictionary<int, int>();
        foreach (var i in items)
        {
            if (i.Quantity < 1) throw new BankingException(400, "Every item needs a quantity of at least 1.");
            units[i.CatalogItemId] = units.GetValueOrDefault(i.CatalogItemId) + i.Quantity;
        }

        var ids = units.Keys.ToList();
        var catalog = await _db.CatalogItems.AsNoTracking().Where(c => ids.Contains(c.Id)).ToListAsync();
        var missing = ids.Except(catalog.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
            throw new BankingException(404, $"No such catalog item: {string.Join(", ", missing)}.");

        var checkout = new BankCheckout
        {
            UserId = user.Id,
            BuyerName = user.UserName!,
            LegalName = string.IsNullOrWhiteSpace(legalName) ? user.UserName : legalName.Trim(),
            EmailAddress = user.Email,
            BankConnectionId = connection.Id,
            ShippingCost = shippingCost!.Value,
            ShipStreet = shipTo?.Street ?? string.Empty,
            ShipCity = shipTo?.City ?? string.Empty,
            ShipState = shipTo?.State ?? string.Empty,
            ShipCountry = shipTo?.Country ?? string.Empty,
            ShipZipCode = shipTo?.ZipCode ?? string.Empty,
        };
        foreach (var c in catalog)
        {
            checkout.Items.Add(new BankCheckoutItem
            {
                CatalogItemId = c.Id,
                ProductName = c.Name,
                PictureUri = string.IsNullOrEmpty(c.PictureUri) ? "-" : c.PictureUri,
                UnitPrice = c.Price,
                Units = units[c.Id],
            });
        }

        _db.BankCheckouts.Add(checkout);
        await _db.SaveChangesAsync();
        return ToDto(checkout);
    }

    // ---- shipping ------------------------------------------------------------------------------------------

    public async Task<BankPaymentDto> ChangeShippingAsync(ClaimsPrincipal principal, Guid checkoutId, decimal? shippingCost)
    {
        var user = await _shoppers.RequireAsync(principal);
        ValidateShipping(shippingCost);
        using var _ = await LockAsync("checkout:" + checkoutId);
        var checkout = await LoadCheckoutAsync(user, checkoutId);
        if (checkout.Status != BankCheckoutStatus.Open)
            throw new BankingException(409, "That payment is already confirmed; its shipping can no longer change.");
        checkout.ShippingCost = shippingCost!.Value;
        await _db.SaveChangesAsync();
        return ToDto(checkout);
    }

    private async Task<BankCheckout> LoadCheckoutAsync(ApplicationUser user, Guid checkoutId)
    {
        return await _db.BankCheckouts.Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.Id == checkoutId && c.UserId == user.Id)
            ?? throw new BankingException(404, "No such bank payment.");
    }

    // ---- confirm -------------------------------------------------------------------------------------------

    public async Task<OrderConfirmationDto> ConfirmAsync(ClaimsPrincipal principal, Guid checkoutId, string? speed)
    {
        var user = await _shoppers.RequireAsync(principal);
        speed = speed?.Trim().ToLowerInvariant();
        if (speed is null) throw new BankingException(400, "speed is required.");
        if (speed != BankPaymentSpeeds.Standard && speed != BankPaymentSpeeds.Express)
            throw new BankingException(400, "speed must be 'standard' or 'express'.");

        using var _ = await LockAsync("checkout:" + checkoutId);
        var checkout = await LoadCheckoutAsync(user, checkoutId);
        if (checkout.Status != BankCheckoutStatus.Open)
            throw new BankingException(409, "That payment was already confirmed.");

        // The total is worked out now, from what the checkout holds now (shipping may have changed).
        var total = checkout.Total;
        if (!OptionsFor(total).Contains(speed))
            throw new BankingException(422, $"The '{speed}' speed is not available for an order of {Money.ToPlaid(total)}.");
        var fee = speed == BankPaymentSpeeds.Express ? _options.ExpressFee : 0m;
        var charged = total + fee;
        if (charged <= 0) throw new BankingException(422, "There is nothing to charge.");

        var connection = await _db.BankConnections.FirstOrDefaultAsync(c => c.Id == checkout.BankConnectionId && c.UserId == user.Id);
        if (connection is null || connection.AccountId is null)
            throw new BankingException(409, "The bank for this payment is no longer connected.");

        var amount = Money.ToPlaid(charged);
        var network = speed == BankPaymentSpeeds.Express ? TransferNetwork.SameDayAch : TransferNetwork.Ach;

        var auth = await _plaid.CallAsync(c => c.TransferAuthorizationCreate(new TransferAuthorizationCreateOperationRequest
        {
            Body = new TransferAuthorizationCreateRequest
            {
                AccessToken = connection.AccessToken,
                AccountId = connection.AccountId,
                Type = TransferType.Debit,
                Network = network,
                Amount = amount,
                AchClass = AchClass.Web,
                UserPresent = true,
                User = new TransferAuthorizationUserInRequest
                {
                    LegalName = checkout.LegalName ?? checkout.BuyerName,
                    EmailAddress = checkout.EmailAddress,
                }
            }
        }));

        if (auth.Authorization.Decision != TransferAuthorizationDecision.Approved)
        {
            var why = auth.Authorization.DecisionRationale?.Description ?? auth.Authorization.Decision.Value;
            throw new BankingException(422, $"The bank payment was not approved: {why}");
        }

        var transfer = (await _plaid.CallAsync(c => c.TransferCreate(new TransferCreateOperationRequest
        {
            Body = new TransferCreateRequest
            {
                AccessToken = connection.AccessToken,
                AccountId = connection.AccountId,
                AuthorizationId = auth.Authorization.Id,
                Amount = amount,
                Description = "eShop pay",
            }
        }))).Transfer;

        try
        {
            var order = new Order(
                user.UserName!,
                new Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Address(checkout.ShipStreet, checkout.ShipCity, checkout.ShipState, checkout.ShipCountry, checkout.ShipZipCode),
                checkout.Items.Select(i => new OrderItem(
                    new CatalogItemOrdered(i.CatalogItemId, i.ProductName, i.PictureUri), i.UnitPrice, i.Units)).ToList());
            _db.Orders.Add(order);
            await _db.SaveChangesAsync();

            var arrives = ArrivalDate(transfer, speed);
            _db.BankOrders.Add(new BankOrder
            {
                OrderId = order.Id,
                UserId = user.Id,
                CheckoutId = checkout.Id,
                Speed = speed,
                TransferId = transfer.Id,
                AuthorizationId = auth.Authorization.Id,
                Total = total,
                Fee = fee,
                Charged = charged,
                ArrivesOn = arrives?.ToDateTime(TimeOnly.MinValue),
            });
            checkout.Status = BankCheckoutStatus.Completed;
            checkout.OrderId = order.Id;
            await _db.SaveChangesAsync();

            return new OrderConfirmationDto(order.Id, speed, total, fee, charged, arrives, transfer.Id);
        }
        catch
        {
            // The money must not move for an order that does not exist.
            try
            {
                await _plaid.CallAsync(c => c.TransferCancel(new TransferCancelOperationRequest
                {
                    Body = new TransferCancelRequest { TransferId = transfer.Id }
                }));
            }
            catch (Exception)
            {
            }
            throw;
        }
    }

    /// <summary>The date Plaid expects the money to arrive; only when Plaid gives none is one worked out.</summary>
    private static DateOnly? ArrivalDate(Transfer transfer, string speed)
    {
        var date = transfer.ExpectedFundsAvailableDate ?? transfer.ExpectedSettlementDate;
        if (date is not null) return DateOnly.FromDateTime(date.Value.Date);
        return null;
    }

    // ---- cancel --------------------------------------------------------------------------------------------

    public async Task<OrderCancellationDto> CancelAsync(ClaimsPrincipal principal, int orderId)
    {
        var user = await _shoppers.RequireAsync(principal);
        using var _ = await LockAsync("order:" + orderId);
        var order = await _db.BankOrders.FirstOrDefaultAsync(o => o.OrderId == orderId && o.UserId == user.Id)
            ?? throw new BankingException(404, "No such order paid by bank.");
        if (order.Status == BankOrderStatus.Cancelled)
            throw new BankingException(409, "That order is already cancelled.");
        if (order.ShippedOnUtc is not null)
            throw new BankingException(409, "That order has already shipped and can no longer be cancelled.");

        var transfer = (await _plaid.CallAsync(c => c.TransferGet(new TransferGetOperationRequest
        {
            Body = new TransferGetRequest { TransferId = order.TransferId }
        }))).Transfer;

        string? refundId = null;
        var status = transfer.Status;

        if (status == TransferStatus.Cancelled || status == TransferStatus.Failed)
        {
            // The money never moved: nothing to give back.
        }
        else if (transfer.Cancellable)
        {
            await _plaid.CallAsync(c => c.TransferCancel(new TransferCancelOperationRequest
            {
                Body = new TransferCancelRequest { TransferId = order.TransferId }
            }));
        }
        else
        {
            // Already with the bank network: give everything back, fee included.
            var refund = (await _plaid.CallAsync(c => c.TransferRefundCreate(new TransferRefundCreateOperationRequest
            {
                Body = new TransferRefundCreateRequest
                {
                    TransferId = order.TransferId,
                    Amount = Money.ToPlaid(order.Charged),
                    IdempotencyKey = Key($"refund|{order.OrderId}|{order.TransferId}"),
                }
            }))).Refund;
            refundId = refund.Id;
        }

        order.Status = BankOrderStatus.Cancelled;
        order.CancelledOnUtc = DateTime.UtcNow;
        order.RefundId = refundId;
        await _db.SaveChangesAsync();
        // Nothing moved for a transfer that never completed; otherwise the shopper gets the whole charge back.
        var back = status == TransferStatus.Cancelled || status == TransferStatus.Failed ? 0m : order.Charged;
        return new OrderCancellationDto(order.OrderId, order.Status, back, refundId);
    }

    // ---- helpers -------------------------------------------------------------------------------------------

    /// <summary>Plaid idempotency keys are at most 50 characters: derive a fixed-size one.</summary>
    private static string Key(string source) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..40].ToLowerInvariant();

    private static async Task<IDisposable> LockAsync(string key)
    {
        var gate = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        return new Releaser(gate);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _gate;
        public Releaser(SemaphoreSlim gate) => _gate = gate;
        public void Dispose() => _gate.Release();
    }
}
