using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class PaymentMethodService
{
    private readonly StaxClient _stax;
    private readonly ShopperService _shoppers;
    private readonly UserManager<ApplicationUser> _users;

    public PaymentMethodService(StaxClient stax, ShopperService shoppers, UserManager<ApplicationUser> users)
    {
        _stax = stax;
        _shoppers = shoppers;
        _users = users;
    }

    public async Task<object> CreateAsync(string email, Body body, CancellationToken ct = default)
    {
        var kind = (body.Str("method", "type", "kind") ?? "").ToLowerInvariant();
        if (kind is "ach" or "bank_account" or "bankaccount") kind = "bank";
        if (kind is "credit" or "credit_card" or "creditcard" or "debit") kind = "card";
        if (kind is not ("card" or "bank"))
            throw ApiException.BadRequest("method must be \"card\" or \"bank\".");

        var name = body.Str("personName", "name", "holderName", "nameOnCard", "accountHolderName");
        if (name == null) throw ApiException.BadRequest("personName is required.");

        object payload;
        if (kind == "card")
        {
            var number = new string((body.Str("cardNumber", "number") ?? "").Where(char.IsDigit).ToArray());
            if (number.Length is < 12 or > 19) throw ApiException.BadRequest("cardNumber must be 12 to 19 digits.");
            var exp = ParseExpiry(body);
            var dict = new Dictionary<string, object?>
            {
                ["method"] = "card",
                ["person_name"] = name,
                ["card_number"] = number,
                ["card_exp"] = exp
            };
            var cvv = body.Str("cardCvv", "cvv", "cvc", "securityCode");
            if (cvv != null) dict["card_cvv"] = cvv;
            AddAddress(dict, body);
            payload = dict;
        }
        else
        {
            var account = new string((body.Str("bankAccount", "accountNumber", "bankAccountNumber", "account") ?? "").Where(char.IsDigit).ToArray());
            var routing = new string((body.Str("bankRouting", "routingNumber", "routing", "bankRoutingNumber") ?? "").Where(char.IsDigit).ToArray());
            if (account.Length is < 4 or > 17) throw ApiException.BadRequest("bankAccount must be 4 to 17 digits.");
            if (routing.Length != 9) throw ApiException.BadRequest("bankRouting must be 9 digits.");
            var type = (body.Str("bankType", "accountType") ?? "checking").ToLowerInvariant();
            if (type is not ("checking" or "savings")) throw ApiException.BadRequest("bankType must be \"checking\" or \"savings\".");
            var holder = (body.Str("bankHolderType", "holderType", "accountHolderType") ?? "personal").ToLowerInvariant();
            if (holder is not ("personal" or "business")) throw ApiException.BadRequest("bankHolderType must be \"personal\" or \"business\".");
            var dict = new Dictionary<string, object?>
            {
                ["method"] = "bank",
                ["person_name"] = name,
                ["bank_account"] = account,
                ["bank_routing"] = routing,
                ["bank_type"] = type,
                ["bank_holder_type"] = holder
            };
            var bankName = body.Str("bankName");
            if (bankName != null) dict["bank_name"] = bankName;
            AddAddress(dict, body);
            payload = dict;
        }

        var customerId = await _shoppers.GetOrCreateCustomerIdAsync(email, ct);
        ((Dictionary<string, object?>)payload)["customer_id"] = customerId;
        var created = await _stax.PostAsync("payment-method", payload, ct);
        return Map(created, includeRemoved: false);
    }

    public async Task<List<object>> ListAsync(string email, CancellationToken ct = default)
    {
        var customerId = await _shoppers.FindCustomerIdAsync(email, ct);
        if (customerId == null) return new List<object>();
        var result = await _stax.GetAsync($"customer/{customerId}/payment-method", null, ct);
        var rows = result as JsonArray ?? result["data"] as JsonArray ?? new JsonArray();
        return rows.Where(r => r != null && r["deleted_at"] == null)
            .OrderByDescending(r => ShopperService.ParseStaxTime(r!["created_at"]))
            .Select(r => Map(r!, includeRemoved: false)).ToList();
    }

    /// <summary>Loads a payment method and checks that it is an active one of this shopper.</summary>
    public async Task<JsonNode> RequireOwnActiveAsync(string email, string paymentMethodId, CancellationToken ct = default)
    {
        var customerId = await _shoppers.FindCustomerIdAsync(email, ct);
        JsonNode? pm = null;
        if (customerId != null && IsSafeId(paymentMethodId))
        {
            try { pm = await _stax.GetAsync($"payment-method/{paymentMethodId}", null, ct); }
            catch (StaxException ex) when (ex.UpstreamStatus is 404 or 422 or 400) { }
        }
        if (pm == null || pm["customer_id"]?.GetValue<string>() != customerId || pm["deleted_at"] != null)
            throw ApiException.NotFound("No such saved payment method for this shopper.");
        return pm;
    }

    public async Task RemoveAsync(string email, string paymentMethodId, CancellationToken ct = default)
    {
        await RequireOwnActiveAsync(email, paymentMethodId, ct);
        await _stax.DeleteAsync($"payment-method/{paymentMethodId}", ct);
    }

    /// <summary>Support view: everything the shopper ever saved at Stax, removed ones included.</summary>
    public async Task<List<object>> ListForSupportAsync(string email, CancellationToken ct = default)
    {
        email = ShopperService.NormalizeEmail(email);
        var customers = await _shoppers.FindStaxCustomersAsync(email, ct);
        var mapped = await _shoppers.FindCustomerIdAsync(email, ct);
        var ids = customers.Select(c => c["id"]!.GetValue<string>()).ToList();
        if (mapped != null && !ids.Contains(mapped)) ids.Add(mapped);

        var known = ids.Count > 0 || await _users.FindByEmailAsync(email) != null;
        if (!known) throw ApiException.NotFound("No shopper with that email.");

        var all = new List<JsonNode>();
        foreach (var id in ids)
        {
            try
            {
                all.AddRange(await _stax.GetAllPagesAsync("payment-method",
                    new Dictionary<string, string?> { ["customer_id"] = id, ["status"] = "all" }, ct));
            }
            catch (StaxException ex) when (ex.UpstreamStatus == 404) { }
        }
        return all.Where(p => ids.Contains(p["customer_id"]?.GetValue<string>() ?? ""))
            .GroupBy(p => p["id"]!.GetValue<string>()).Select(g => g.First())
            .OrderByDescending(p => ShopperService.ParseStaxTime(p["created_at"]))
            .Select(p => Map(p, includeRemoved: true)).ToList();
    }

    public static bool IsSafeId(string id) => id.Length is > 0 and <= 64 && id.All(c => char.IsLetterOrDigit(c) || c == '-');

    public static object Map(JsonNode pm, bool includeRemoved)
    {
        var method = pm["method"]?.GetValue<string>();
        var exp = pm["card_exp"]?.GetValue<string>();
        string? expiry = null;
        if (method == "card" && exp is { Length: 6 }) expiry = exp[..2] + "/" + exp[2..];
        var removed = ShopperService.ParseStaxTime(pm["deleted_at"]);
        var created = ShopperService.ParseStaxTime(pm["created_at"]);
        var core = new Dictionary<string, object?>
        {
            ["id"] = pm["id"]?.GetValue<string>(),
            ["method"] = method,
            ["brand"] = method == "card" ? pm["card_type"]?.GetValue<string>() : null,
            ["lastFour"] = pm["card_last_four"]?.GetValue<string>(),
            ["nickname"] = pm["nickname"]?.GetValue<string>(),
            ["personName"] = pm["person_name"]?.GetValue<string>(),
            ["expiry"] = expiry,
            ["bankType"] = pm["bank_type"]?.GetValue<string>(),
            ["bankHolderType"] = pm["bank_holder_type"]?.GetValue<string>(),
            ["createdAt"] = created
        };
        if (includeRemoved)
        {
            core["customerId"] = pm["customer_id"]?.GetValue<string>();
            core["removedAt"] = removed;
        }
        return core;
    }

    private static string ParseExpiry(Body body)
    {
        var month = body.Int("cardExpMonth", "expMonth", "expiryMonth");
        var year = body.Int("cardExpYear", "expYear", "expiryYear");
        if (month == null || year == null)
        {
            var raw = body.Str("cardExp", "expiry", "exp", "expiration", "expirationDate");
            var digits = new string((raw ?? "").Where(char.IsDigit).ToArray());
            if (digits.Length is not (4 or 6)) throw ApiException.BadRequest("cardExp must be MMYY or MM/YYYY.");
            month = int.Parse(digits[..2]);
            year = int.Parse(digits[2..]);
        }
        if (year < 100) year += 2000;
        if (month is < 1 or > 12) throw ApiException.BadRequest("cardExp has an invalid month.");
        var lastDay = new DateTime(year.Value, month.Value, DateTime.DaysInMonth(year.Value, month.Value), 23, 59, 59, DateTimeKind.Utc);
        if (lastDay < DateTime.UtcNow) throw ApiException.BadRequest("The card has expired.");
        return $"{month:00}{year % 100:00}";
    }

    private static void AddAddress(Dictionary<string, object?> dict, Body body)
    {
        var zip = body.Str("addressZip", "zip", "postalCode");
        if (zip != null) dict["address_zip"] = zip;
    }
}
