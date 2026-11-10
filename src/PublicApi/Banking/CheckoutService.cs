using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Plaid;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Banking;

public record CheckoutItemRequest(int CatalogItemId, int Quantity);
public record ShipToRequest(string? Street, string? City, string? State, string? Country, string? ZipCode);
public record StartCheckoutRequest(Guid? ConnectionId, List<CheckoutItemRequest>? Items, decimal? ShippingCost, string? LegalName, ShipToRequest? ShipTo);
public record ChangeShippingRequest(decimal? ShippingCost);
public record ConfirmRequest(string? Speed);

public record CheckoutView(Guid CheckoutId, Guid ConnectionId, string Currency, decimal ItemsTotal, decimal ShippingCost, decimal Total,
    IReadOnlyList<string> Options, decimal ExpressFee);

public record OrderConfirmation(int OrderId, Guid CheckoutId, string Speed, decimal Total, decimal Fee, decimal Charged, string ArrivesOn, string Status);
public record CancelResult(int OrderId, string Status, decimal Refunded);

public class CheckoutService
{
    private readonly PlaidClient _plaid;
    private readonly CatalogContext _db;
    private readonly ConnectionService _connections;
    private readonly IUriComposer _uriComposer;
    private readonly BankPaymentOptions _options;

    public CheckoutService(PlaidClient plaid, CatalogContext db, ConnectionService connections, IUriComposer uriComposer,
        IOptions<BankPaymentOptions> options)
    {
        _plaid = plaid;
        _db = db;
        _connections = connections;
        _uriComposer = uriComposer;
        _options = options.Value;
    }

    // ---- Start ----

    public async Task<CheckoutView> StartAsync(string userName, StartCheckoutRequest request, CancellationToken ct)
    {
        if (request.ConnectionId is null) throw new ApiException(HttpStatusCode.BadRequest, "connectionId is required.");
        var connection = await _connections.GetOwnedAsync(userName, request.ConnectionId.Value, ct);
        if (connection.Kind != BankConnectionKinds.Bank || string.IsNullOrEmpty(connection.AccountId))
        {
            throw new ApiException(HttpStatusCode.BadRequest, "That connection is not a bank account that can be paid from.");
        }

        var shipping = ValidateShipping(request.ShippingCost);
        if (request.Items is null || request.Items.Count == 0)
        {
            throw new ApiException(HttpStatusCode.BadRequest, "items must not be empty.");
        }
        if (request.Items.Any(i => i.Quantity < 1))
        {
            throw new ApiException(HttpStatusCode.BadRequest, "Every item needs a quantity of at least 1.");
        }

        var wanted = request.Items.GroupBy(i => i.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(i => (long)i.Quantity));
        if (wanted.Values.Any(q => q > int.MaxValue))
        {
            throw new ApiException(HttpStatusCode.BadRequest, "Quantity is too large.");
        }
        var ids = wanted.Keys.ToList();
        var catalog = await _db.CatalogItems.Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var missing = ids.Where(id => !catalog.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            throw new ApiException(HttpStatusCode.BadRequest, $"Unknown catalog item(s): {string.Join(", ", missing)}.");
        }

        var checkout = new BankCheckout
        {
            BuyerId = userName,
            ConnectionId = connection.Id,
            LegalName = string.IsNullOrWhiteSpace(request.LegalName) ? userName : request.LegalName.Trim(),
            Shipping = shipping,
            ShipStreet = request.ShipTo?.Street,
            ShipCity = request.ShipTo?.City,
            ShipState = request.ShipTo?.State,
            ShipCountry = request.ShipTo?.Country,
            ShipZip = request.ShipTo?.ZipCode
        };
        foreach (var (id, quantity) in wanted)
        {
            var item = catalog[id];
            checkout.Items.Add(new BankCheckoutItem
            {
                CatalogItemId = id,
                ProductName = item.Name.Length > 50 ? item.Name[..50] : item.Name,
                PictureUri = _uriComposer.ComposePicUri(item.PictureUri),
                UnitPrice = item.Price,
                Quantity = (int)quantity
            });
        }

        if (checkout.Total() <= 0)
        {
            throw new ApiException(HttpStatusCode.BadRequest, "The order total must be greater than zero.");
        }

        _db.BankCheckouts.Add(checkout);
        await _db.SaveChangesAsync(ct);
        return ToView(checkout);
    }

    // ---- Change the shipping ----

    public async Task<CheckoutView> ChangeShippingAsync(string userName, Guid checkoutId, decimal? shippingCost, CancellationToken ct)
    {
        var shipping = ValidateShipping(shippingCost);
        var checkout = await LoadCheckoutAsync(userName, checkoutId, ct);
        if (checkout.Status != CheckoutStatuses.Open)
        {
            throw new ApiException(HttpStatusCode.Conflict, "This payment is already confirmed or being confirmed; the shipping can no longer change.");
        }

        checkout.Shipping = shipping;
        checkout.Version = Guid.NewGuid();
        await SaveGuardedAsync(ct, "The payment changed at the same time; try again.");
        return ToView(checkout);
    }

    // ---- Confirm ----

    public async Task<OrderConfirmation> ConfirmAsync(string userName, Guid checkoutId, string? speed, CancellationToken ct)
    {
        var checkout = await LoadCheckoutAsync(userName, checkoutId, ct);

        if (checkout.Status == CheckoutStatuses.Completed)
        {
            // A repeated confirmation never charges twice: it returns the existing confirmation.
            var done = await _db.BankPayments.FirstOrDefaultAsync(p => p.CheckoutId == checkout.Id, ct);
            if (done is not null) return ToConfirmation(done);
        }
        if (checkout.Status != CheckoutStatuses.Open)
        {
            throw new ApiException(HttpStatusCode.Conflict, "This payment is already being confirmed.");
        }

        var chosen = (speed ?? string.Empty).Trim().ToLowerInvariant();
        if (chosen is not (PaymentSpeeds.Standard or PaymentSpeeds.Express))
        {
            throw new ApiException(HttpStatusCode.BadRequest, "speed must be 'standard' or 'express'.");
        }
        var total = checkout.Total();
        if (!Options(total).Contains(chosen))
        {
            throw new ApiException(HttpStatusCode.UnprocessableEntity, $"'{chosen}' is not available for this order (available: {string.Join(", ", Options(total))}).");
        }

        var connection = await _db.BankConnections.FirstOrDefaultAsync(c => c.Id == checkout.ConnectionId && c.UserName == userName, ct)
            ?? throw new ApiException(HttpStatusCode.Conflict, "The bank connection for this payment was disconnected.");

        var fee = chosen == PaymentSpeeds.Express ? _options.ExpressFee : 0m;
        var charged = total + fee;

        // Claim the checkout so a double submit cannot charge twice.
        checkout.Status = CheckoutStatuses.Confirming;
        checkout.Version = Guid.NewGuid();
        await SaveGuardedAsync(ct, "This payment is already being confirmed.");

        var accessToken = _connections.AccessToken(connection);
        var transfers = new List<(string Id, decimal Amount, DateTime? Settles)>();
        try
        {
            var network = chosen == PaymentSpeeds.Express ? "same-day-ach" : "ach";
            var limit = chosen == PaymentSpeeds.Express ? _options.SameDayAchLimit : _options.AchLimit;
            var amounts = Split(charged, limit);

            // Authorize everything first, then create the transfers.
            var authorizations = new List<(string Id, decimal Amount)>();
            foreach (var amount in amounts)
            {
                var auth = await _plaid.PostAsync("transfer/authorization/create", new JsonObject
                {
                    ["access_token"] = accessToken,
                    ["account_id"] = connection.AccountId,
                    ["type"] = "debit",
                    ["network"] = network,
                    ["amount"] = Json.Money(amount),
                    ["ach_class"] = "ppd",
                    ["user"] = new JsonObject { ["legal_name"] = checkout.LegalName }
                }, CancellationToken.None);

                var authorization = auth["authorization"];
                if (Json.Str(authorization?["decision"]) != "approved")
                {
                    var rationale = Json.Str(authorization?["decision_rationale"]?["description"]) ?? "The bank declined the payment.";
                    throw new ApiException(HttpStatusCode.UnprocessableEntity, $"The bank payment was declined: {rationale}");
                }
                authorizations.Add((Json.Str(authorization?["id"])!, amount));
            }

            foreach (var (authId, amount) in authorizations)
            {
                var created = await _plaid.PostAsync("transfer/create", new JsonObject
                {
                    ["access_token"] = accessToken,
                    ["account_id"] = connection.AccountId,
                    ["authorization_id"] = authId,
                    ["description"] = "eShop order"
                }, CancellationToken.None);
                var transfer = created["transfer"];
                DateTime? settles = DateTime.TryParse(Json.Str(transfer?["expected_settlement_date"]), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var s) ? s.Date : null;
                transfers.Add((Json.Str(transfer?["id"])!, amount, settles));
            }

            var address = new Address(
                checkout.ShipStreet ?? "Not provided", checkout.ShipCity ?? "Not provided", checkout.ShipState ?? string.Empty,
                checkout.ShipCountry ?? "Not provided", checkout.ShipZip ?? "Not provided");
            var order = new Order(userName, address,
                checkout.Items.Select(i => new OrderItem(new CatalogItemOrdered(i.CatalogItemId, i.ProductName, i.PictureUri), i.UnitPrice, i.Quantity)).ToList());
            _db.Orders.Add(order);
            await _db.SaveChangesAsync(CancellationToken.None);

            var arrives = chosen == PaymentSpeeds.Express
                ? BusinessCalendar.SameOrNextBusinessDay(BusinessCalendar.TodayEastern())
                : BusinessCalendar.AddBusinessDays(BusinessCalendar.TodayEastern(), _options.StandardBusinessDays);
            if (transfers.All(t => t.Settles is not null))
            {
                var plaidDate = transfers.Max(t => t.Settles!.Value);
                if (plaidDate > arrives) arrives = plaidDate;
            }

            var payment = new BankPayment
            {
                OrderId = order.Id,
                CheckoutId = checkout.Id,
                BuyerId = userName,
                ConnectionId = connection.Id,
                Speed = chosen,
                OrderTotal = total,
                Fee = fee,
                Charged = charged,
                ArrivesOn = arrives,
                Transfers = transfers.Select(t => new BankPaymentTransfer { PlaidTransferId = t.Id, Amount = t.Amount }).ToList()
            };
            _db.BankPayments.Add(payment);
            checkout.Status = CheckoutStatuses.Completed;
            checkout.Version = Guid.NewGuid();
            try
            {
                await _db.SaveChangesAsync(CancellationToken.None);
            }
            catch
            {
                // Do not leave an order without its payment behind.
                var orderId = order.Id;
                _db.ChangeTracker.Clear();
                var orphan = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, CancellationToken.None);
                if (orphan is not null)
                {
                    _db.Orders.Remove(orphan);
                    await _db.SaveChangesAsync(CancellationToken.None);
                }
                throw;
            }
            return ToConfirmation(payment);
        }
        catch
        {
            // Nothing is charged unless everything is: undo what was created and reopen the checkout.
            foreach (var (id, _, _) in transfers)
            {
                try { await _plaid.PostAsync("transfer/cancel", new JsonObject { ["transfer_id"] = id }, CancellationToken.None); }
                catch (ApiException) { }
            }
            try
            {
                _db.ChangeTracker.Clear();
                var reopen = await _db.BankCheckouts.FirstAsync(c => c.Id == checkoutId, CancellationToken.None);
                if (reopen.Status == CheckoutStatuses.Confirming)
                {
                    reopen.Status = CheckoutStatuses.Open;
                    reopen.Version = Guid.NewGuid();
                    await _db.SaveChangesAsync(CancellationToken.None);
                }
            }
            catch (Exception) { }
            throw;
        }
    }

    // ---- Cancel ----

    public async Task<CancelResult> CancelAsync(string userName, int orderId, CancellationToken ct)
    {
        var payment = await _db.BankPayments.Include(p => p.Transfers).FirstOrDefaultAsync(p => p.OrderId == orderId && p.BuyerId == userName, ct)
            ?? throw new ApiException(HttpStatusCode.NotFound, "No bank-paid order with that id.");

        if (payment.Status == PaymentStatuses.Cancelled)
        {
            throw new ApiException(HttpStatusCode.Conflict, "The order is already cancelled.");
        }
        if (payment.ShippedOn is not null)
        {
            throw new ApiException(HttpStatusCode.Conflict, "The order has already shipped and can no longer be cancelled.");
        }

        // Claim the cancellation (a retry after a partial failure resumes where it stopped).
        payment.Status = PaymentStatuses.Cancelling;
        payment.Version = Guid.NewGuid();
        await SaveGuardedAsync(ct, "The order is being cancelled already.");

        foreach (var transfer in payment.Transfers.Where(t => !t.Reversed))
        {
            var info = (await _plaid.PostAsync("transfer/get", new JsonObject { ["transfer_id"] = transfer.PlaidTransferId }, CancellationToken.None))["transfer"];
            var status = Json.Str(info?["status"]);
            var cancellable = info?["cancellable"]?.GetValue<bool>() ?? false;

            if (cancellable)
            {
                await _plaid.PostAsync("transfer/cancel", new JsonObject { ["transfer_id"] = transfer.PlaidTransferId }, CancellationToken.None);
            }
            else if (status is "posted" or "settled" or "funds_available")
            {
                await _plaid.PostAsync("transfer/refund/create", new JsonObject
                {
                    ["transfer_id"] = transfer.PlaidTransferId,
                    ["amount"] = Json.Money(transfer.Amount),
                    ["idempotency_key"] = $"refund-{transfer.PlaidTransferId}"
                }, CancellationToken.None);
            }
            else if (status is not ("cancelled" or "failed" or "returned"))
            {
                throw new ApiException(HttpStatusCode.Conflict, $"The payment cannot be reversed yet (transfer status '{status}'); try again later.");
            }

            transfer.Reversed = true;
            await _db.SaveChangesAsync(CancellationToken.None);
        }

        payment.Status = PaymentStatuses.Cancelled;
        payment.CancelledOn = DateTimeOffset.UtcNow;
        payment.Version = Guid.NewGuid();
        await _db.SaveChangesAsync(CancellationToken.None);
        return new CancelResult(orderId, payment.Status, payment.Charged);
    }

    /// <summary>Marks an order shipped; after that it can no longer be cancelled.</summary>
    public async Task MarkShippedAsync(int orderId, CancellationToken ct)
    {
        var payment = await _db.BankPayments.FirstOrDefaultAsync(p => p.OrderId == orderId, ct)
            ?? throw new ApiException(HttpStatusCode.NotFound, "No bank-paid order with that id.");
        if (payment.Status != PaymentStatuses.Paid)
        {
            throw new ApiException(HttpStatusCode.Conflict, $"The order is {payment.Status}.");
        }
        payment.ShippedOn ??= DateTimeOffset.UtcNow;
        payment.Version = Guid.NewGuid();
        await SaveGuardedAsync(ct, "The order changed at the same time; try again.");
    }

    // ---- Helpers ----

    private IReadOnlyList<string> Options(decimal total)
    {
        // Standard has no practical ceiling (large orders go as several transfers).
        // Express is a single same-day transfer, so it stops at the same-day network limit.
        var options = new List<string> { PaymentSpeeds.Standard };
        if (total + _options.ExpressFee <= _options.SameDayAchLimit) options.Add(PaymentSpeeds.Express);
        return options;
    }

    private CheckoutView ToView(BankCheckout c)
    {
        var total = c.Total();
        return new CheckoutView(c.Id, c.ConnectionId, "USD", c.ItemsTotal(), c.Shipping, total, Options(total), _options.ExpressFee);
    }

    private static OrderConfirmation ToConfirmation(BankPayment p) =>
        new(p.OrderId, p.CheckoutId, p.Speed, p.OrderTotal, p.Fee, p.Charged, p.ArrivesOn.ToString("yyyy-MM-dd"), p.Status);

    private static decimal ValidateShipping(decimal? cost)
    {
        if (cost is null) throw new ApiException(HttpStatusCode.BadRequest, "shippingCost is required.");
        if (cost < 0) throw new ApiException(HttpStatusCode.BadRequest, "shippingCost must not be negative.");
        if (cost > 1_000_000_000m) throw new ApiException(HttpStatusCode.BadRequest, "shippingCost is too large.");
        return Math.Round(cost.Value, 2, MidpointRounding.AwayFromZero);
    }

    private static List<decimal> Split(decimal amount, decimal limit)
    {
        var parts = new List<decimal>();
        while (amount > limit)
        {
            parts.Add(limit);
            amount -= limit;
        }
        parts.Add(amount);
        return parts;
    }

    private async Task<BankCheckout> LoadCheckoutAsync(string userName, Guid checkoutId, CancellationToken ct) =>
        await _db.BankCheckouts.Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == checkoutId && c.BuyerId == userName, ct)
            ?? throw new ApiException(HttpStatusCode.NotFound, "No such payment.");

    private async Task SaveGuardedAsync(CancellationToken ct, string conflictMessage)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ApiException(HttpStatusCode.Conflict, conflictMessage);
        }
    }
}
