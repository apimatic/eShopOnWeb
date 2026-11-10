using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.Banking;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

public static class PaymentSpeeds
{
    public const string Standard = "standard";
    public const string Express = "express";

    /// <summary>The fee the shopper pays for express (same business day) payment.</summary>
    public const decimal ExpressFee = 5.00m;

    /// <summary>The Same-Day ACH network limit per transaction.</summary>
    public const decimal SameDayLimit = 1_000_000m;

    public static decimal FeeFor(string speed) => speed == Express ? ExpressFee : 0m;

    /// <summary>The speeds available for an order total. Express can't carry more than the Same-Day ACH limit.</summary>
    public static List<string> OptionsFor(decimal total)
    {
        var options = new List<string> { Standard };
        if (total + ExpressFee <= SameDayLimit)
        {
            options.Add(Express);
        }
        return options;
    }
}

public class ShipToAddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class CheckoutLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class StartBankPaymentRequest
{
    public int ConnectionId { get; set; }
    public List<CheckoutLineRequest>? Items { get; set; }
    public decimal ShippingCost { get; set; }
    public ShipToAddressDto? ShipToAddress { get; set; }
}

public class ChangeShippingRequest
{
    public decimal? ShippingCost { get; set; }
}

public class ConfirmBankPaymentRequest
{
    public string? Speed { get; set; }
}

public class SpeedOptionDto
{
    public string Speed { get; set; } = string.Empty;
    public decimal Fee { get; set; }
    public decimal Total { get; set; }
}

public class BankPaymentQuote
{
    public Guid CheckoutId { get; set; }
    public int ConnectionId { get; set; }
    public decimal ItemsTotal { get; set; }
    public decimal ShippingCost { get; set; }
    /// <summary>The order total: items plus shipping, before any express fee.</summary>
    public decimal Total { get; set; }
    public string Currency { get; set; } = "USD";
    public List<string> Options { get; set; } = new();
    public List<SpeedOptionDto> OptionDetails { get; set; } = new();
}

public class BankPaymentConfirmation
{
    public int OrderId { get; set; }
    public Guid CheckoutId { get; set; }
    public string Speed { get; set; } = string.Empty;
    public decimal Fee { get; set; }
    /// <summary>What was charged: the order total plus the fee.</summary>
    public decimal Total { get; set; }
    public DateOnly ArrivesOn { get; set; }
    public string Status { get; set; } = "paid";
}

public class OrderCancellation
{
    public int OrderId { get; set; }
    public string Status { get; set; } = "cancelled";
    public decimal Refunded { get; set; }
}

internal record CheckoutLine(int CatalogItemId, string Name, string PictureUri, decimal UnitPrice, int Quantity);

public class BankPaymentService
{
    private static readonly TimeSpan ConfirmingTimeout = TimeSpan.FromMinutes(5);

    private readonly CatalogContext _db;
    private readonly PlaidClient _plaid;
    private readonly AccessTokenProtector _protector;
    private readonly IUriComposer _uriComposer;

    public BankPaymentService(CatalogContext db, PlaidClient plaid, AccessTokenProtector protector, IUriComposer uriComposer)
    {
        _db = db;
        _plaid = plaid;
        _protector = protector;
        _uriComposer = uriComposer;
    }

    private static List<CheckoutLine> Lines(BankCheckout checkout) =>
        JsonSerializer.Deserialize<List<CheckoutLine>>(checkout.ItemsJson) ?? new List<CheckoutLine>();

    private static decimal ValidateMoney(decimal amount, string name)
    {
        if (amount < 0)
        {
            throw new ApiException(400, $"{name} can't be negative.");
        }
        if (decimal.Round(amount, 2) != amount)
        {
            throw new ApiException(400, $"{name} can have at most two decimal places.");
        }
        if (amount > 1_000_000_000m)
        {
            throw new ApiException(400, $"{name} is too large.");
        }
        return amount;
    }

    private static BankPaymentQuote Quote(BankCheckout checkout)
    {
        var itemsTotal = Lines(checkout).Sum(l => l.UnitPrice * l.Quantity);
        var total = itemsTotal + checkout.ShippingCost;
        var options = PaymentSpeeds.OptionsFor(total);
        return new BankPaymentQuote
        {
            CheckoutId = checkout.Id,
            ConnectionId = checkout.ConnectionId,
            ItemsTotal = itemsTotal,
            ShippingCost = checkout.ShippingCost,
            Total = total,
            Options = options,
            OptionDetails = options.Select(o => new SpeedOptionDto
            {
                Speed = o,
                Fee = PaymentSpeeds.FeeFor(o),
                Total = total + PaymentSpeeds.FeeFor(o)
            }).ToList()
        };
    }

    public async Task<BankPaymentQuote> StartAsync(string buyerKey, StartBankPaymentRequest request)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            throw new ApiException(400, "items must contain at least one item.");
        }
        if (request.Items.Any(i => i.Quantity < 1))
        {
            throw new ApiException(400, "Every item needs a quantity of at least 1.");
        }
        var shipping = ValidateMoney(request.ShippingCost, "shippingCost");

        var connection = await _db.BankConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.ConnectionId && c.BuyerId == buyerKey)
            ?? throw new ApiException(404, $"Bank connection {request.ConnectionId} was not found.");
        if (connection.Purpose != ConnectionPurpose.Bank || string.IsNullOrEmpty(connection.AccountId))
        {
            throw new ApiException(409, "This connection can't be used to pay: it is not a bank account.");
        }

        var wanted = request.Items.GroupBy(i => i.CatalogItemId).ToDictionary(g => g.Key, g => g.Sum(i => (long)i.Quantity));
        var ids = wanted.Keys.ToList();
        var catalogItems = await _db.CatalogItems.AsNoTracking().Where(i => ids.Contains(i.Id)).ToListAsync();
        var missing = ids.Except(catalogItems.Select(i => i.Id)).ToList();
        if (missing.Count > 0)
        {
            throw new ApiException(400, $"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }
        if (wanted.Values.Any(q => q > 1_000_000_000))
        {
            throw new ApiException(400, "A quantity is too large.");
        }

        var lines = catalogItems.Select(i => new CheckoutLine(i.Id, i.Name, _uriComposer.ComposePicUri(i.PictureUri), i.Price, (int)wanted[i.Id])).ToList();
        var checkout = new BankCheckout
        {
            BuyerId = buyerKey,
            ConnectionId = connection.Id,
            ItemsJson = JsonSerializer.Serialize(lines),
            ShippingCost = shipping,
            ShipStreet = request.ShipToAddress?.Street,
            ShipCity = request.ShipToAddress?.City,
            ShipState = request.ShipToAddress?.State,
            ShipCountry = request.ShipToAddress?.Country,
            ShipZipCode = request.ShipToAddress?.ZipCode
        };

        var quote = Quote(checkout);
        if (quote.Total <= 0)
        {
            throw new ApiException(400, "There is nothing to pay: the order total is zero.");
        }

        _db.BankCheckouts.Add(checkout);
        await _db.SaveChangesAsync();
        return quote;
    }

    private async Task<BankCheckout> GetCheckoutAsync(string buyerKey, Guid checkoutId)
    {
        return await _db.BankCheckouts.FirstOrDefaultAsync(c => c.Id == checkoutId && c.BuyerId == buyerKey)
               ?? throw new ApiException(404, $"Checkout {checkoutId} was not found.");
    }

    public async Task<BankPaymentQuote> ChangeShippingAsync(string buyerKey, Guid checkoutId, decimal? shippingCost)
    {
        if (shippingCost is null)
        {
            throw new ApiException(400, "shippingCost is required.");
        }
        var shipping = ValidateMoney(shippingCost.Value, "shippingCost");

        var checkout = await GetCheckoutAsync(buyerKey, checkoutId);
        if (checkout.Status != BankCheckoutStatus.Started)
        {
            throw new ApiException(409, "The payment has already been confirmed; the shipping can't change any more.");
        }

        checkout.ShippingCost = shipping;
        checkout.Revision++;
        var quote = Quote(checkout);
        if (quote.Total <= 0)
        {
            throw new ApiException(400, "There is nothing to pay: the order total is zero.");
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ApiException(409, "The payment is being confirmed; the shipping can't change now.");
        }
        return quote;
    }

    private static BankPaymentConfirmation ToConfirmation(BankCheckout checkout)
    {
        var total = Quote(checkout).Total;
        return new BankPaymentConfirmation
        {
            OrderId = checkout.OrderId ?? 0,
            CheckoutId = checkout.Id,
            Speed = checkout.Speed ?? string.Empty,
            Fee = checkout.Fee,
            Total = total + checkout.Fee,
            ArrivesOn = DateOnly.FromDateTime(checkout.ArrivesOn ?? DateTime.UtcNow.Date)
        };
    }

    public async Task<BankPaymentConfirmation> ConfirmAsync(string buyerKey, string rawBuyerName, Guid checkoutId, string? speedText)
    {
        var speed = (speedText ?? string.Empty).Trim().ToLowerInvariant();
        if (speed is not (PaymentSpeeds.Standard or PaymentSpeeds.Express))
        {
            throw new ApiException(400, "speed must be 'standard' or 'express'.");
        }

        var checkout = await GetCheckoutAsync(buyerKey, checkoutId);
        if (checkout.Status == BankCheckoutStatus.Confirmed)
        {
            if (checkout.Speed == speed)
            {
                return ToConfirmation(checkout); // a retry: never charge twice
            }
            throw new ApiException(409, $"The payment was already confirmed with speed '{checkout.Speed}'.");
        }
        if (checkout.Status == BankCheckoutStatus.Confirming
            && DateTimeOffset.UtcNow - (checkout.ConfirmingSince ?? DateTimeOffset.MinValue) < ConfirmingTimeout)
        {
            throw new ApiException(409, "The payment is already being confirmed.");
        }

        var quote = Quote(checkout);
        if (!quote.Options.Contains(speed))
        {
            throw new ApiException(400, $"Speed '{speed}' is not available for this order. Available: {string.Join(", ", quote.Options)}.");
        }

        var connection = await _db.BankConnections.FirstOrDefaultAsync(c => c.Id == checkout.ConnectionId && c.BuyerId == buyerKey)
                         ?? throw new ApiException(409, "The bank connection for this payment was disconnected.");
        if (string.IsNullOrEmpty(connection.AccountId))
        {
            throw new ApiException(409, "This connection can't be used to pay.");
        }
        var accessToken = _protector.Unprotect(connection);

        var fee = PaymentSpeeds.FeeFor(speed);
        var amount = quote.Total + fee;

        // Claim the checkout so two confirmations can't both charge.
        checkout.Status = BankCheckoutStatus.Confirming;
        checkout.ConfirmingSince = DateTimeOffset.UtcNow;
        checkout.Revision++;
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ApiException(409, "The payment is already being confirmed or has changed; try again.");
        }

        JsonObject transfer;
        try
        {
            transfer = await ChargeAsync(accessToken, connection.AccountId, rawBuyerName, speed, amount);
        }
        catch
        {
            await ReleaseAsync(checkout);
            throw;
        }

        var transferId = transfer["id"]!.GetValue<string>();
        try
        {
            var arrives = ParseDate(transfer["expected_funds_available_date"]) ?? ParseDate(transfer["expected_settlement_date"])
                          ?? NextBusinessDay(DateTime.UtcNow.Date, speed == PaymentSpeeds.Express ? 0 : 2);

            var order = new Order(rawBuyerName, ShipTo(checkout), Lines(checkout).Select(l =>
                new OrderItem(new CatalogItemOrdered(l.CatalogItemId, l.Name, l.PictureUri), l.UnitPrice, l.Quantity)).ToList());
            order.RecordBankPayment(transferId, amount, fee, checkout.ShippingCost);
            _db.Orders.Add(order);

            checkout.Status = BankCheckoutStatus.Confirmed;
            checkout.Speed = speed;
            checkout.Fee = fee;
            checkout.ArrivesOn = arrives;
            checkout.ConfirmingSince = null;
            checkout.Revision++;
            await _db.SaveChangesAsync();

            checkout.OrderId = order.Id;
            checkout.Revision++;
            await _db.SaveChangesAsync();
        }
        catch
        {
            // The shopper was charged but we have no order: give the money back.
            await UndoChargeAsync(transferId, amount);
            _db.ChangeTracker.Clear();
            var fresh = await _db.BankCheckouts.FirstOrDefaultAsync(c => c.Id == checkoutId);
            if (fresh is not null)
            {
                fresh.Status = BankCheckoutStatus.Started;
                fresh.ConfirmingSince = null;
                fresh.Revision++;
                await _db.SaveChangesAsync();
            }
            throw;
        }

        return ToConfirmation(checkout);
    }

    private static Address ShipTo(BankCheckout c) => new(
        string.IsNullOrWhiteSpace(c.ShipStreet) ? "Not provided" : c.ShipStreet,
        string.IsNullOrWhiteSpace(c.ShipCity) ? "Not provided" : c.ShipCity,
        c.ShipState ?? string.Empty,
        string.IsNullOrWhiteSpace(c.ShipCountry) ? "Not provided" : c.ShipCountry,
        string.IsNullOrWhiteSpace(c.ShipZipCode) ? "Not provided" : c.ShipZipCode);

    private static DateTime? ParseDate(JsonNode? node)
    {
        var text = node?.GetValue<string>();
        return DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    private static DateTime NextBusinessDay(DateTime from, int days)
    {
        var d = from;
        while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            d = d.AddDays(1);
        }
        for (var i = 0; i < days; i++)
        {
            do { d = d.AddDays(1); } while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
        }
        return d;
    }

    private async Task ReleaseAsync(BankCheckout checkout)
    {
        checkout.Status = BankCheckoutStatus.Started;
        checkout.ConfirmingSince = null;
        checkout.Revision++;
        await _db.SaveChangesAsync();
    }

    private async Task<JsonObject> ChargeAsync(string accessToken, string accountId, string buyerName, string speed, decimal amount)
    {
        var authorization = await _plaid.PostAsync("/transfer/authorization/create", new JsonObject
        {
            ["access_token"] = accessToken,
            ["account_id"] = accountId,
            ["type"] = "debit",
            ["network"] = speed == PaymentSpeeds.Express ? "same-day-ach" : "ach",
            ["amount"] = amount.ToString("F2", CultureInfo.InvariantCulture),
            ["ach_class"] = "web",
            ["user"] = new JsonObject { ["legal_name"] = buyerName, ["email_address"] = buyerName.Contains('@') ? buyerName : null }
        });

        var auth = authorization["authorization"] as JsonObject
                   ?? throw new ApiException(502, "Plaid did not return a transfer authorization.");
        if (auth["decision"]?.GetValue<string>() != "approved")
        {
            var rationale = auth["decision_rationale"]?["description"]?.GetValue<string>() ?? "the bank payment was not approved";
            throw new ApiException(402, $"The bank payment was declined: {rationale}.");
        }

        var created = await _plaid.PostAsync("/transfer/create", new JsonObject
        {
            ["access_token"] = accessToken,
            ["account_id"] = accountId,
            ["authorization_id"] = auth["id"]!.GetValue<string>(),
            ["description"] = "eShopOnWeb"
        });
        return created["transfer"] as JsonObject ?? throw new ApiException(502, "Plaid did not return the transfer.");
    }

    private async Task UndoChargeAsync(string transferId, decimal amount)
    {
        try
        {
            await ReturnMoneyAsync(transferId);
        }
        catch (Exception)
        {
            // Best effort: the original exception is the one reported.
        }
    }

    /// <summary>Cancels the transfer if it hasn't left yet, otherwise refunds it in full. Returns the amount returned.</summary>
    private async Task<decimal> ReturnMoneyAsync(string transferId)
    {
        var got = await _plaid.PostAsync("/transfer/get", new JsonObject { ["transfer_id"] = transferId });
        var transfer = got["transfer"] as JsonObject ?? throw new ApiException(502, "Plaid did not return the transfer.");
        var status = transfer["status"]?.GetValue<string>();
        var amount = decimal.Parse(transfer["amount"]!.GetValue<string>(), CultureInfo.InvariantCulture);

        // Money never moved, or has already come back.
        if (status is "cancelled" or "failed" or "returned")
        {
            return 0m;
        }

        if (status == "pending" && transfer["cancellable"]?.GetValue<bool>() == true)
        {
            try
            {
                await _plaid.PostAsync("/transfer/cancel", new JsonObject { ["transfer_id"] = transferId });
                return amount;
            }
            catch (PlaidException ex) when (ex.ErrorCode == "TRANSFER_NOT_CANCELLABLE")
            {
                // it just left: fall through to a refund
            }
        }

        // The idempotency key makes a retry after a failure safe: the shopper is never refunded twice.
        await _plaid.PostAsync("/transfer/refund/create", new JsonObject
        {
            ["transfer_id"] = transferId,
            ["amount"] = amount.ToString("F2", CultureInfo.InvariantCulture),
            ["idempotency_key"] = "eshop-refund-" + transferId
        });
        return amount;
    }

    public async Task<OrderCancellation> CancelOrderAsync(string rawBuyerName, int orderId)
    {
        var key = CallerIdentity.NormaliseKey(rawBuyerName);
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId && o.BuyerId.ToLower() == key)
                    ?? throw new ApiException(404, $"Order {orderId} was not found.");

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new ApiException(409, "The order has already been cancelled.");
        }
        if (order.Status == OrderStatus.Shipped)
        {
            throw new ApiException(409, "The order has already shipped and can't be cancelled.");
        }
        if (string.IsNullOrEmpty(order.PaymentTransferId))
        {
            throw new ApiException(409, "The order was not paid by bank, so it can't be cancelled and refunded here.");
        }

        var refunded = await ReturnMoneyAsync(order.PaymentTransferId);
        order.MarkCancelled();
        await _db.SaveChangesAsync();
        return new OrderCancellation { OrderId = order.Id, Refunded = refunded };
    }
}
