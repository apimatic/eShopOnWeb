using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Stax;

public class PaymentMethodDto
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Nickname { get; set; }
    public string? PersonName { get; set; }
    public string? Last4 { get; set; }
    public string? Brand { get; set; }
    public string? CardExpiry { get; set; }
    public string? BankName { get; set; }
    public string? BankType { get; set; }
    public string? BankHolderType { get; set; }
    public string? CustomerId { get; set; }
    public string? CreatedAt { get; set; }
    public string? RemovedAt { get; set; }
}

public class PaymentMethodService
{
    private readonly StaxClient _stax;
    private readonly ShopperService _shoppers;

    public PaymentMethodService(StaxClient stax, ShopperService shoppers)
    {
        _stax = stax;
        _shoppers = shoppers;
    }

    public static PaymentMethodDto ToDto(JsonNode pm)
    {
        var type = pm.Str("method") ?? string.Empty;
        var isCard = type == "card";
        return new PaymentMethodDto
        {
            Id = pm.Str("id") ?? string.Empty,
            Type = type,
            Nickname = pm.Str("nickname"),
            PersonName = pm.Str("person_name"),
            Last4 = pm.Str("card_last_four"),
            Brand = isCard ? pm.Str("card_type") : null,
            CardExpiry = isCard ? pm.Str("card_exp") : null,
            BankName = isCard ? null : pm.Str("bank_name"),
            BankType = isCard ? null : pm.Str("bank_type"),
            BankHolderType = isCard ? null : pm.Str("bank_holder_type"),
            CustomerId = pm.Str("customer_id"),
            CreatedAt = pm.Str("created_at"),
            RemovedAt = pm.Str("deleted_at")
        };
    }

    public async Task<PaymentMethodDto> SaveAsync(string email, JsonObject body, CancellationToken ct)
    {
        var fields = new LenientBody(body);

        var type = fields.Get("type", "method", "paymentMethodType")?.ToLowerInvariant();
        if (type is null)
        {
            type = fields.Get("cardNumber", "number") is not null ? "card"
                : fields.Get("bankAccount", "accountNumber") is not null ? "bank" : null;
        }
        if (type is "ach") type = "bank";
        if (type is not ("card" or "bank"))
        {
            throw ApiException.BadRequest("type must be 'card' or 'bank'.");
        }

        var personName = fields.Get("personName", "name", "holderName", "accountHolder", "cardholderName");
        if (string.IsNullOrWhiteSpace(personName))
        {
            throw ApiException.BadRequest("personName (the account holder's name) is required.");
        }

        var payload = new JsonObject
        {
            ["method"] = type,
            ["person_name"] = personName.Trim()
        };

        if (type == "card")
        {
            var number = new string((fields.Get("cardNumber", "number") ?? "").Where(char.IsDigit).ToArray());
            var cvv = fields.Get("cardCvv", "cvv", "cvc", "securityCode");
            var exp = NormalizeExpiry(fields.Get("cardExp", "cardExpiry", "expiry", "expiration", "exp"));
            if (number.Length < 12 || number.Length > 19) throw ApiException.BadRequest("cardNumber is not a valid card number.");
            if (string.IsNullOrWhiteSpace(cvv)) throw ApiException.BadRequest("cardCvv is required.");
            payload["card_number"] = number;
            payload["card_cvv"] = cvv.Trim();
            payload["card_exp"] = exp;
        }
        else
        {
            var account = fields.Get("bankAccount", "accountNumber");
            var routing = fields.Get("bankRouting", "routingNumber", "routing");
            var bankName = fields.Get("bankName");
            var bankType = fields.Get("bankType", "accountType")?.ToLowerInvariant();
            var holderType = fields.Get("bankHolderType", "holderType", "accountHolderType")?.ToLowerInvariant() ?? "personal";
            if (string.IsNullOrWhiteSpace(account)) throw ApiException.BadRequest("bankAccount is required.");
            if (string.IsNullOrWhiteSpace(routing)) throw ApiException.BadRequest("bankRouting is required.");
            if (string.IsNullOrWhiteSpace(bankName)) throw ApiException.BadRequest("bankName is required.");
            if (bankType is not ("checking" or "savings")) throw ApiException.BadRequest("bankType must be 'checking' or 'savings'.");
            if (holderType is not ("personal" or "business")) throw ApiException.BadRequest("bankHolderType must be 'personal' or 'business'.");
            payload["bank_account"] = account.Trim();
            payload["bank_routing"] = routing.Trim();
            payload["bank_name"] = bankName.Trim();
            payload["bank_type"] = bankType;
            payload["bank_holder_type"] = holderType;
        }

        foreach (var (key, names) in new (string, string[])[]
        {
            ("address_1", new[] { "address1", "addressLine1", "address" }),
            ("address_2", new[] { "address2", "addressLine2" }),
            ("address_city", new[] { "addressCity", "city" }),
            ("address_state", new[] { "addressState", "state" }),
            ("address_zip", new[] { "addressZip", "zip", "zipCode", "postalCode", "billingZip" }),
            ("address_country", new[] { "addressCountry", "country" })
        })
        {
            var v = fields.Get(names);
            if (!string.IsNullOrWhiteSpace(v)) payload[key] = v.Trim();
        }

        payload["customer_id"] = await _shoppers.GetOrCreateCustomerAsync(email, personName, ct);
        var created = await _stax.PostAsync("payment-method", payload, ct);
        return ToDto(created);
    }

    private static string NormalizeExpiry(string? raw)
    {
        var digits = new string((raw ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length == 6) digits = digits[..2] + digits[4..];
        if (digits.Length != 4 || !int.TryParse(digits[..2], out var month) || month is < 1 or > 12)
        {
            throw ApiException.BadRequest("cardExp must be a month and year such as 0427 or 04/27.");
        }
        return digits;
    }

    /// <summary>Active (not removed) payment methods of the caller.</summary>
    public async Task<List<PaymentMethodDto>> ListAsync(string email, CancellationToken ct)
    {
        var result = new List<PaymentMethodDto>();
        foreach (var customerId in await _shoppers.FindCustomerIdsAsync(email, ct))
        {
            var pms = await _stax.GetAsync($"customer/{customerId}/payment-method", ct);
            result.AddRange(pms.Items().Where(p => p.Str("deleted_at") is null).Select(ToDto));
        }
        return result.OrderByDescending(p => p.CreatedAt, StringComparer.Ordinal).ToList();
    }

    /// <summary>Loads a payment method and verifies it is the caller's and still usable.</summary>
    public async Task<JsonNode> RequireUsableAsync(string email, string paymentMethodId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(paymentMethodId))
        {
            throw ApiException.BadRequest("paymentMethodId is required.");
        }
        JsonNode pm;
        try
        {
            pm = await _stax.GetAsync($"payment-method/{Uri.EscapeDataString(paymentMethodId)}", ct);
        }
        catch (StaxException ex) when (ex.StaxStatusCode is 404 or 422 or 400)
        {
            throw ApiException.NotFound("Payment method not found.");
        }
        var owners = await _shoppers.FindCustomerIdsAsync(email, ct);
        var owner = pm.Str("customer_id");
        if (owner is null || !owners.Contains(owner))
        {
            throw ApiException.NotFound("Payment method not found.");
        }
        if (pm.Str("deleted_at") is not null)
        {
            throw ApiException.NotFound("That payment method has been removed.");
        }
        return pm;
    }

    public async Task RemoveAsync(string email, string paymentMethodId, CancellationToken ct)
    {
        await RequireUsableAsync(email, paymentMethodId, ct);
        await _stax.DeleteAsync($"payment-method/{Uri.EscapeDataString(paymentMethodId)}", ct);
    }

    /// <summary>
    /// Everything a shopper ever saved, removed ones included. Stax is the source of truth, so this also
    /// covers methods removed before this app tracked anything.
    /// </summary>
    public async Task<List<PaymentMethodDto>?> ListForSupportAsync(string email, CancellationToken ct)
    {
        var customerIds = await _shoppers.FindCustomerIdsAsync(email, ct);
        if (customerIds.Count == 0) return null;

        var result = new List<PaymentMethodDto>();
        for (var page = 1; page <= 500; page++)
        {
            var batch = await _stax.GetAsync($"payment-method?status=all&per_page=200&page={page}", ct);
            result.AddRange(batch.Items()
                .Where(p => p.Str("customer_id") is { } c && customerIds.Contains(c))
                .Select(ToDto));
            var last = batch["last_page"]?.GetValue<int>() ?? 1;
            if (page >= last) break;
        }

        // The customer-scoped listing is cheap and authoritative for active methods; merge in anything the merchant-wide pages missed.
        foreach (var customerId in customerIds)
        {
            var active = await _stax.GetAsync($"customer/{customerId}/payment-method", ct);
            foreach (var pm in active.Items())
            {
                var id = pm.Str("id");
                if (id is not null && result.All(r => r.Id != id)) result.Add(ToDto(pm));
            }
        }
        return result.OrderByDescending(p => p.CreatedAt, StringComparer.Ordinal).ToList();
    }
}

/// <summary>Reads request fields ignoring case and underscores, so cardNumber, card_number and CardNumber all work.</summary>
public class LenientBody
{
    private readonly Dictionary<string, JsonNode?> _fields = new();

    public LenientBody(JsonObject body)
    {
        foreach (var (key, value) in body)
        {
            _fields[Normalize(key)] = value;
        }
    }

    private static string Normalize(string s) => s.Replace("_", "").Replace("-", "").ToLowerInvariant();

    public string? Get(params string[] names)
    {
        foreach (var name in names)
        {
            if (_fields.TryGetValue(Normalize(name), out var v) && v is not null)
            {
                var s = v is JsonValue jv && jv.TryGetValue<string>(out var str) ? str : v.ToString();
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
        }
        return null;
    }
}
