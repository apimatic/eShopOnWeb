using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Plaid.Data;
using ThePlaidApi.Models;
using Address = Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Address;
using ThePlaidApi.Models.Enums;
using ThePlaidApi.Requests;

namespace Microsoft.eShopWeb.PublicApi.Plaid.Services;

public sealed record BankPaymentItemRequest(int CatalogItemId, int Quantity);

public sealed record ShipToRequest(string? Street, string? City, string? State, string? Country, string? ZipCode);

public sealed record SpeedOption(string Speed, decimal Fee, decimal Total, DateTime ArrivesOn);

public sealed record CheckoutView(BankPayment Payment, IReadOnlyList<SpeedOption> Options)
{
    public decimal Total => Payment.ItemsTotal + Payment.ShippingCost;
}

public sealed record OrderConfirmation(BankPayment Payment, decimal Total);

public sealed class BankPaymentService
{
    public const string Standard = "standard";
    public const string Express = "express";

    private sealed record StoredItem(int CatalogItemId, string Name, string PictureUri, decimal UnitPrice, int Units);

    private readonly PlaidContext _db;
    private readonly PlaidAccess _plaid;
    private readonly BankConnectionService _connections;
    private readonly IReadRepository<CatalogItem> _catalog;
    private readonly IRepository<Order> _orders;
    private readonly IUriComposer _uriComposer;
    private readonly BankPaymentSettings _settings;

    public BankPaymentService(PlaidContext db, PlaidAccess plaid, BankConnectionService connections,
        IReadRepository<CatalogItem> catalog, IRepository<Order> orders, IUriComposer uriComposer,
        IOptions<BankPaymentSettings> settings)
    {
        _db = db;
        _plaid = plaid;
        _connections = connections;
        _catalog = catalog;
        _orders = orders;
        _uriComposer = uriComposer;
        _settings = settings.Value;
    }

    // ---- speeds ----------------------------------------------------------------------------------------------

    private TimeSpan Cutoff => TimeSpan.TryParse(_settings.SameDayCutoffEastern, CultureInfo.InvariantCulture, out var t)
        ? t : new TimeSpan(15, 30, 0);

    /// <summary>
    /// The speeds on offer for an order. Standard carries any amount. Express rides the same-day ACH network, which
    /// refuses anything above its limit, so a large (wholesale) order is simply not offered express.
    /// </summary>
    public IReadOnlyList<SpeedOption> OptionsFor(decimal orderTotal, DateTimeOffset now)
    {
        var processing = BusinessCalendar.ProcessingDay(now, Cutoff);
        var options = new List<SpeedOption>
        {
            new(Standard, 0m, orderTotal, BusinessCalendar.AddBusinessDays(processing, 1)),
        };
        var fee = _settings.ExpressFee;
        if (orderTotal + fee <= _settings.ExpressMaxAmount)
            options.Add(new SpeedOption(Express, fee, orderTotal + fee, processing));
        return options;
    }

    // ---- start -----------------------------------------------------------------------------------------------

    public async Task<CheckoutView> StartAsync(string userName, Guid? connectionId, string? accountId,
        IReadOnlyList<BankPaymentItemRequest>? items, decimal? shippingCost, ShipToRequest? shipTo, CancellationToken ct)
    {
        if (connectionId is null) throw BankApiException.BadRequest("connectionId is required.");
        if (items is null || items.Count == 0) throw BankApiException.BadRequest("The order has no items.");
        if (shippingCost is null) throw BankApiException.BadRequest("shippingCost is required.");
        var shipping = ValidateShipping(shippingCost.Value);

        var connection = await _connections.GetOwnedAsync(userName, connectionId.Value, ct);
        if (!connection.IsBank)
            throw BankApiException.Unprocessable("That connection is a brokerage account and can't pay for orders.");
        var account = PickAccount(connection, accountId);

        var lines = new List<StoredItem>();
        foreach (var group in items.GroupBy(i => i.CatalogItemId))
        {
            var quantity = group.Sum(i => (long)i.Quantity);
            if (group.Any(i => i.Quantity < 1) || quantity > 1_000_000)
                throw BankApiException.BadRequest($"Item {group.Key} has an invalid quantity.");
            lines.Add(new StoredItem(group.Key, "", "", 0m, (int)quantity));
        }

        // Prices always come from the catalog, never from the caller.
        var catalogItems = await _catalog.ListAsync(new CatalogItemsSpecification(lines.Select(l => l.CatalogItemId).ToArray()), ct);
        for (var i = 0; i < lines.Count; i++)
        {
            var found = catalogItems.FirstOrDefault(c => c.Id == lines[i].CatalogItemId)
                ?? throw BankApiException.NotFound($"There is no catalog item {lines[i].CatalogItemId}.");
            lines[i] = lines[i] with
            {
                Name = found.Name,
                PictureUri = _uriComposer.ComposePicUri(found.PictureUri),
                UnitPrice = found.Price,
            };
        }

        var itemsTotal = lines.Sum(l => l.UnitPrice * l.Units);
        if (itemsTotal + shipping <= 0)
            throw BankApiException.BadRequest("The order total must be more than zero.");

        var payment = new BankPayment
        {
            UserName = BankConnectionService.Normalize(userName),
            ConnectionId = connection.Id,
            PlaidAccountId = account.PlaidAccountId,
            ItemsJson = JsonSerializer.Serialize(lines),
            ItemsTotal = decimal.Round(itemsTotal, 2, MidpointRounding.AwayFromZero),
            ShippingCost = shipping,
            ShipToJson = ValidateShipTo(shipTo),
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync(ct);
        return ViewOf(payment);
    }

    private static decimal ValidateShipping(decimal shippingCost)
    {
        if (shippingCost < 0) throw BankApiException.BadRequest("shippingCost can't be negative.");
        if (shippingCost > 1_000_000_000m) throw BankApiException.BadRequest("shippingCost is too large.");
        return decimal.Round(shippingCost, 2, MidpointRounding.AwayFromZero);
    }

    private static string? ValidateShipTo(ShipToRequest? s)
    {
        if (s is null) return null;
        if (string.IsNullOrWhiteSpace(s.Street) || string.IsNullOrWhiteSpace(s.City)
            || string.IsNullOrWhiteSpace(s.Country) || string.IsNullOrWhiteSpace(s.ZipCode))
            throw BankApiException.BadRequest("shipToAddress needs street, city, country and zipCode.");
        return JsonSerializer.Serialize(s);
    }

    /// <summary>The account that pays: the one asked for, else a checking, else a savings account.</summary>
    private static BankAccount PickAccount(BankConnection connection, string? accountId)
    {
        if (!string.IsNullOrWhiteSpace(accountId))
        {
            return connection.Accounts.FirstOrDefault(a => a.PlaidAccountId == accountId || a.Id.ToString() == accountId)
                ?? throw BankApiException.NotFound("That account is not part of the connection.");
        }

        var depository = connection.Accounts.Where(a => a.Type == "depository").ToList();
        return depository.FirstOrDefault(a => a.Subtype == "checking")
            ?? depository.FirstOrDefault(a => a.Subtype == "savings")
            ?? throw BankApiException.Unprocessable("The connection has no checking or savings account to pay from.");
    }

    private CheckoutView ViewOf(BankPayment p) => new(p, OptionsFor(p.ItemsTotal + p.ShippingCost, DateTimeOffset.UtcNow));

    // ---- change shipping -------------------------------------------------------------------------------------

    public async Task<CheckoutView> GetAsync(string userName, Guid checkoutId, CancellationToken ct) =>
        ViewOf(await GetOwnedPaymentAsync(userName, checkoutId, ct));

    public async Task<CheckoutView> ChangeShippingAsync(string userName, Guid checkoutId, decimal? shippingCost, CancellationToken ct)
    {
        if (shippingCost is null) throw BankApiException.BadRequest("shippingCost is required.");
        var shipping = ValidateShipping(shippingCost.Value);

        using var _ = await PlaidAccess.LockAsync(checkoutId, ct);
        var payment = await GetOwnedPaymentAsync(userName, checkoutId, ct);
        if (payment.Status != BankPaymentStatus.Started)
            throw BankApiException.Conflict("The payment is already confirmed, its shipping can't change.");
        if (payment.ItemsTotal + shipping <= 0)
            throw BankApiException.BadRequest("The order total must be more than zero.");

        payment.ShippingCost = shipping;
        payment.Revision++;
        await SaveAsync(ct);
        return ViewOf(payment);
    }

    // ---- confirm ---------------------------------------------------------------------------------------------

    public async Task<OrderConfirmation> ConfirmAsync(string userName, Guid checkoutId, string? speedText, CancellationToken ct)
    {
        var speed = speedText?.Trim().ToLowerInvariant();
        if (speed != Standard && speed != Express)
            throw BankApiException.BadRequest("speed must be 'standard' or 'express'.");

        using var _ = await PlaidAccess.LockAsync(checkoutId, ct);
        var payment = await GetOwnedPaymentAsync(userName, checkoutId, ct);

        if (payment.Status == BankPaymentStatus.Cancelled)
            throw BankApiException.Conflict("This payment was cancelled.");
        if (payment.Status == BankPaymentStatus.Paid)
        {
            // A repeat of a confirmation that already went through is answered again, never charged twice.
            if (payment.Speed != speed)
                throw BankApiException.Conflict($"This payment was already confirmed with the {payment.Speed} speed.");
            await EnsureOrderAsync(payment, ct);
            return new OrderConfirmation(payment, payment.ItemsTotal + payment.ShippingCost + payment.Fee);
        }

        var orderTotal = payment.ItemsTotal + payment.ShippingCost;
        var option = OptionsFor(orderTotal, DateTimeOffset.UtcNow).FirstOrDefault(o => o.Speed == speed)
            ?? throw BankApiException.Unprocessable($"The {speed} speed is not available for this order.");

        var connection = await _db.Connections.FirstOrDefaultAsync(c => c.Id == payment.ConnectionId, ct)
            ?? throw BankApiException.Conflict("The bank account for this payment was disconnected.");
        var accessToken = _plaid.Unprotect(connection.ProtectedAccessToken);

        // Same key for the same amount and speed, so a retry can never charge twice; a changed total gets a new key.
        var key = $"{payment.Id:N}-{payment.Revision}{(speed == Express ? "x" : "s")}";
        var amount = PlaidAccess.Money(option.Total);
        var network = speed == Express ? TransferNetwork.SameDayAch : TransferNetwork.Ach;

        TransferAuthorization authorization;
        Transfer transfer;
        try
        {
            authorization = (await _plaid.Client.TransferAuthorizationCreate(new TransferAuthorizationCreateOperationRequest
            {
                Body = new TransferAuthorizationCreateRequest
                {
                    AccessToken = accessToken,
                    AccountId = payment.PlaidAccountId,
                    Type = TransferType.Debit,
                    Network = network,
                    Amount = amount,
                    AchClass = AchClass.Web,
                    User = new TransferAuthorizationUserInRequest { LegalName = payment.UserName },
                    UserPresent = true,
                    IdempotencyKey = key,
                },
            }, null, ct)).Authorization;

            if (authorization.Decision != TransferAuthorizationDecision.Approved)
            {
                var why = authorization.DecisionRationale?.Description ?? "the bank declined the payment";
                throw authorization.Decision == TransferAuthorizationDecision.UserActionRequired
                    ? BankApiException.Conflict($"The payment needs the shopper to re-connect the bank: {why}.")
                    : BankApiException.Unprocessable($"The payment was declined: {why}.");
            }

            transfer = await CreateTransferAsync(accessToken, payment, authorization.Id, ct);
        }
        catch (Exception e) when (e is not BankApiException && PlaidFailure.ToApiException(e) is { } mapped)
        {
            throw mapped;
        }

        payment.Status = BankPaymentStatus.Paid;
        payment.Speed = speed;
        payment.Fee = option.Fee;
        payment.AuthorizationId = authorization.Id;
        payment.TransferId = transfer.Id;
        payment.ArrivesOn = transfer.ExpectedSettlementDate?.Date ?? option.ArrivesOn;
        payment.Revision++;
        await SaveAsync(ct);

        await EnsureOrderAsync(payment, ct);
        return new OrderConfirmation(payment, option.Total);
    }

    /// <summary>
    /// An authorization can be turned into a transfer once. If a repeated confirm finds it already used, the transfer
    /// it produced is the one to report, not a failure and never a second charge.
    /// </summary>
    private async Task<Transfer> CreateTransferAsync(string accessToken, BankPayment payment, string authorizationId, CancellationToken ct)
    {
        try
        {
            return (await _plaid.Client.TransferCreate(new TransferCreateOperationRequest
            {
                Body = new TransferCreateRequest
                {
                    AccessToken = accessToken,
                    AccountId = payment.PlaidAccountId,
                    AuthorizationId = authorizationId,
                    Description = "eShopOnWeb",
                    Metadata = new Dictionary<string, string> { ["checkout_id"] = payment.Id.ToString("N") },
                },
            }, null, ct)).Transfer;
        }
        catch (Exception createFailure) when (PlaidFailure.Read(createFailure) is not null)
        {
            try
            {
                return (await _plaid.Client.TransferGet(new TransferGetOperationRequest
                {
                    Body = new TransferGetRequest { AuthorizationId = authorizationId },
                }, null, ct)).Transfer;
            }
            catch (Exception)
            {
                throw createFailure is BankApiException ? createFailure : PlaidFailure.ToApiException(createFailure)!;
            }
        }
    }

    /// <summary>The order exists once the money is taken; created here, and completed on a repeated confirm if it failed.</summary>
    private async Task EnsureOrderAsync(BankPayment payment, CancellationToken ct)
    {
        if (payment.OrderId is not null) return;

        var lines = JsonSerializer.Deserialize<List<StoredItem>>(payment.ItemsJson)!;
        var address = payment.ShipToJson is null
            ? new Address("Not provided", "Not provided", "", "Not provided", "Not provided")
            : ToAddress(JsonSerializer.Deserialize<ShipToRequest>(payment.ShipToJson)!);
        var orderItems = lines.Select(l =>
            new OrderItem(new CatalogItemOrdered(l.CatalogItemId, l.Name, l.PictureUri), l.UnitPrice, l.Units)).ToList();

        var order = await _orders.AddAsync(new Order(payment.UserName, address, orderItems), ct);
        payment.OrderId = order.Id;
        payment.Revision++;
        await SaveAsync(ct);
    }

    private static Address ToAddress(ShipToRequest s) =>
        new(s.Street!.Trim(), s.City!.Trim(), s.State?.Trim() ?? "", s.Country!.Trim(), s.ZipCode!.Trim());

    // ---- cancel ----------------------------------------------------------------------------------------------

    public async Task<BankPayment> CancelOrderAsync(string userName, int orderId, CancellationToken ct)
    {
        var user = BankConnectionService.Normalize(userName);
        var found = await _db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.OrderId == orderId && p.UserName == user, ct)
            ?? throw BankApiException.NotFound("There is no such order paid by bank.");

        using var _ = await PlaidAccess.LockAsync(found.Id, ct);
        var payment = await _db.Payments.FirstAsync(p => p.Id == found.Id, ct);
        await _db.Entry(payment).ReloadAsync(ct);

        if (payment.Status == BankPaymentStatus.Cancelled)
            throw BankApiException.Conflict("The order was already cancelled.");
        if (payment.ShippedAt is not null)
            throw BankApiException.Conflict("The order has shipped and can't be cancelled any more.");
        if (payment.Status != BankPaymentStatus.Paid || payment.TransferId is null)
            throw BankApiException.Conflict("The order has not been paid.");

        var charged = payment.ItemsTotal + payment.ShippingCost + payment.Fee;
        try
        {
            var transfer = (await _plaid.Client.TransferGet(new TransferGetOperationRequest
            {
                Body = new TransferGetRequest { TransferId = payment.TransferId },
            }, null, ct)).Transfer;

            var gone = transfer.Status == TransferStatus.Cancelled || transfer.Status == TransferStatus.Failed;
            var needsRefund = !gone;
            if (!gone && transfer.Cancellable)
            {
                try
                {
                    // The money has not moved yet: stopping the transfer gives all of it back.
                    await _plaid.Client.TransferCancel(new TransferCancelOperationRequest
                    {
                        Body = new TransferCancelRequest { TransferId = payment.TransferId },
                    }, null, ct);
                    needsRefund = false;
                }
                catch (Exception e) when (PlaidFailure.HasCode(e, "TRANSFER_NOT_CANCELLABLE"))
                {
                    // It moved on in the meantime; refund instead.
                }
            }

            if (needsRefund)
            {
                var refund = (await _plaid.Client.TransferRefundCreate(new TransferRefundCreateOperationRequest
                {
                    Body = new TransferRefundCreateRequest
                    {
                        TransferId = payment.TransferId,
                        Amount = PlaidAccess.Money(charged),
                        IdempotencyKey = $"cancel-{payment.Id:N}",
                    },
                }, null, ct)).Refund;
                payment.RefundId = refund.Id;
            }
        }
        catch (Exception e) when (e is not BankApiException && PlaidFailure.ToApiException(e) is { } mapped)
        {
            throw mapped;
        }

        payment.Status = BankPaymentStatus.Cancelled;
        payment.CancelledAt = DateTimeOffset.UtcNow;
        payment.Revision++;
        await SaveAsync(ct);
        return payment;
    }

    // ---- helpers ---------------------------------------------------------------------------------------------

    private async Task<BankPayment> GetOwnedPaymentAsync(string userName, Guid checkoutId, CancellationToken ct)
    {
        var user = BankConnectionService.Normalize(userName);
        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.Id == checkoutId && p.UserName == user, ct);
        if (payment is null) throw BankApiException.NotFound("There is no such checkout.");
        // Another request may have changed it while this one waited for the lock.
        await _db.Entry(payment).ReloadAsync(ct);
        return payment;
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw BankApiException.Conflict("The payment was changed by another request, please try again.");
        }
    }
}
