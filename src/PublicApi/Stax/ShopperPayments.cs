using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Stax;

public class SavePaymentMethodRequest : BaseRequest
{
    /// <summary>"card" or "bank"</summary>
    public string? Type { get; set; }
    public string? PersonName { get; set; }
    public string? CardNumber { get; set; }
    /// <summary>MMYY</summary>
    public string? CardExp { get; set; }
    public string? CardCvv { get; set; }
    public string? BankAccount { get; set; }
    public string? BankRouting { get; set; }
    public string? BankName { get; set; }
    /// <summary>checking | savings</summary>
    public string? BankType { get; set; }
    /// <summary>personal | business</summary>
    public string? BankHolderType { get; set; }
    public string? Address1 { get; set; }
    public string? Address2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Zip { get; set; }
    public string? Country { get; set; }
}

public class PaymentMethodDto
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Nickname { get; set; }
    public string? PersonName { get; set; }
    public string? LastFour { get; set; }
    public string? CardBrand { get; set; }
    public string? CardExpiry { get; set; }
    public string? BankName { get; set; }
    public string? BankType { get; set; }
    public string? BankHolderType { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}

public class AdminPaymentMethodDto : PaymentMethodDto
{
    public DateTimeOffset? RemovedAt { get; set; }
}

public static class Json
{
    public static string? Str(this JsonNode? n, string name)
    {
        var v = n?[name];
        if (v is JsonValue jv)
        {
            if (jv.TryGetValue<string>(out var s)) return s;
            return jv.ToString();
        }
        return null;
    }

    public static decimal Dec(this JsonNode? n, string name)
    {
        var v = n?[name];
        if (v is JsonValue jv)
        {
            if (jv.TryGetValue<decimal>(out var d)) return d;
            if (jv.TryGetValue<double>(out var db)) return (decimal)db;
            if (jv.TryGetValue<string>(out var s) && decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var p)) return p;
        }
        return 0m;
    }

    public static bool Bool(this JsonNode? n, string name)
    {
        var v = n?[name];
        if (v is JsonValue jv)
        {
            if (jv.TryGetValue<bool>(out var b)) return b;
            if (jv.TryGetValue<int>(out var i)) return i != 0;
            if (jv.TryGetValue<string>(out var s)) return s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    public static DateTimeOffset? Date(this JsonNode? n, string name)
    {
        var s = n.Str(name);
        if (string.IsNullOrWhiteSpace(s)) return null;
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d) ? d : null;
    }
}

/// <summary>Stax customers and stored payment methods. Stax is the source of truth; customers are matched by email.</summary>
public class ShopperPayments
{
    private readonly StaxClient _stax;

    public ShopperPayments(StaxClient stax)
    {
        _stax = stax;
    }

    public static string NormalizeEmail(string email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    public static string? EmailOf(ClaimsPrincipal user)
    {
        var name = user.FindFirst(ClaimTypes.Name)?.Value ?? user.Identity?.Name;
        return string.IsNullOrWhiteSpace(name) ? null : NormalizeEmail(name);
    }

    public static bool LooksLikeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254) return false;
        var at = email.IndexOf('@');
        return at > 0 && at == email.LastIndexOf('@') && at < email.Length - 3 && email.IndexOf('.', at) > at + 1 && !email.Any(char.IsWhiteSpace);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _emailLocks = new();

    public async Task<List<JsonNode>> FindCustomersAsync(string email, CancellationToken ct = default)
    {
        email = NormalizeEmail(email);
        var all = await _stax.GetAllAsync($"customer?email={Uri.EscapeDataString(email)}", ct);
        return all.Where(c => string.Equals(NormalizeEmail(c.Str("email") ?? ""), email, StringComparison.Ordinal) && string.IsNullOrEmpty(c.Str("deleted_at"))).ToList();
    }

    public async Task<string> GetOrCreateCustomerIdAsync(string email, string? company = null, CancellationToken ct = default)
    {
        email = NormalizeEmail(email);
        var gate = _emailLocks.GetOrAdd(email, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var existing = (await FindCustomersAsync(email, ct)).OrderBy(c => c.Str("created_at")).FirstOrDefault();
            if (existing != null)
            {
                var id = existing.Str("id")!;
                if (!string.IsNullOrWhiteSpace(company) && existing.Str("company") != company)
                {
                    await _stax.PutAsync($"customer/{id}", new JsonObject { ["company"] = company }, ct);
                }
                return id;
            }
            var local = email.Split('@')[0];
            var created = await _stax.PostAsync("customer", new JsonObject
            {
                ["firstname"] = local,
                ["lastname"] = "Shopper",
                ["company"] = company ?? string.Empty,
                ["email"] = email,
                ["reference"] = email
            }, ct);
            return created.Str("id") ?? throw new ApiException(502, "Stax did not return a customer id.");
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<PaymentMethodDto> SaveAsync(string email, SavePaymentMethodRequest request, CancellationToken ct = default)
    {
        var type = (request.Type ?? "").Trim().ToLowerInvariant();
        var body = new JsonObject();
        if (string.IsNullOrWhiteSpace(request.PersonName)) throw ApiException.BadRequest("personName is required.");
        body["person_name"] = request.PersonName.Trim();
        if (type == "card")
        {
            if (string.IsNullOrWhiteSpace(request.CardNumber)) throw ApiException.BadRequest("cardNumber is required.");
            var digits = new string(request.CardNumber.Where(char.IsDigit).ToArray());
            if (digits.Length < 12 || digits.Length > 19) throw ApiException.BadRequest("cardNumber is not a valid card number.");
            var exp = (request.CardExp ?? "").Replace("/", "").Trim();
            if (exp.Length != 4 || !exp.All(char.IsDigit) || int.Parse(exp[..2]) is < 1 or > 12)
                throw ApiException.BadRequest("cardExp must be MMYY.");
            body["method"] = "card";
            body["card_number"] = digits;
            body["card_exp"] = exp;
            if (!string.IsNullOrWhiteSpace(request.CardCvv)) body["card_cvv"] = request.CardCvv.Trim();
        }
        else if (type == "bank")
        {
            if (string.IsNullOrWhiteSpace(request.BankAccount)) throw ApiException.BadRequest("bankAccount is required.");
            if (string.IsNullOrWhiteSpace(request.BankRouting)) throw ApiException.BadRequest("bankRouting is required.");
            var bankType = (request.BankType ?? "checking").Trim().ToLowerInvariant();
            var holder = (request.BankHolderType ?? "personal").Trim().ToLowerInvariant();
            if (bankType is not ("checking" or "savings")) throw ApiException.BadRequest("bankType must be checking or savings.");
            if (holder is not ("personal" or "business")) throw ApiException.BadRequest("bankHolderType must be personal or business.");
            body["method"] = "bank";
            body["bank_account"] = request.BankAccount.Trim();
            body["bank_routing"] = request.BankRouting.Trim();
            body["bank_name"] = string.IsNullOrWhiteSpace(request.BankName) ? "Bank" : request.BankName.Trim();
            body["bank_type"] = bankType;
            body["bank_holder_type"] = holder;
        }
        else
        {
            throw ApiException.BadRequest("type must be 'card' or 'bank'.");
        }
        if (!string.IsNullOrWhiteSpace(request.Address1)) body["address_1"] = request.Address1;
        if (!string.IsNullOrWhiteSpace(request.Address2)) body["address_2"] = request.Address2;
        if (!string.IsNullOrWhiteSpace(request.City)) body["address_city"] = request.City;
        if (!string.IsNullOrWhiteSpace(request.State)) body["address_state"] = request.State;
        if (!string.IsNullOrWhiteSpace(request.Zip)) body["address_zip"] = request.Zip;
        if (!string.IsNullOrWhiteSpace(request.Country)) body["address_country"] = request.Country;

        body["customer_id"] = await GetOrCreateCustomerIdAsync(email, null, ct);
        var created = await _stax.PostAsync("payment-method", body, ct);
        return ToDto(created!);
    }

    public async Task<List<PaymentMethodDto>> ListAsync(string email, CancellationToken ct = default)
    {
        var result = new List<PaymentMethodDto>();
        foreach (var c in await FindCustomersAsync(email, ct))
        {
            var methods = await _stax.GetAllAsync($"customer/{c.Str("id")}/payment-method", ct);
            result.AddRange(methods.Where(m => string.IsNullOrEmpty(m.Str("deleted_at"))).Select(ToDto));
        }
        return result.OrderBy(m => m.CreatedAt).ToList();
    }

    /// <summary>Everything the shopper ever saved, removed ones included.</summary>
    public async Task<List<AdminPaymentMethodDto>> ListEverAsync(string email, CancellationToken ct = default)
    {
        var map = new Dictionary<string, AdminPaymentMethodDto>();
        foreach (var c in await FindCustomersAsync(email, ct))
        {
            var id = Uri.EscapeDataString(c.Str("id")!);
            foreach (var m in await _stax.GetAllAsync($"payment-method?customer_id={id}", ct))
                map[m.Str("id")!] = ToAdminDto(m);
            foreach (var m in await _stax.GetAllAsync($"payment-method?customer_id={id}&status=deleted", ct))
                map[m.Str("id")!] = ToAdminDto(m);
        }
        return map.Values.OrderBy(m => m.CreatedAt).ToList();
    }

    /// <summary>Loads a payment method and verifies it belongs to the shopper. Returns null when it is not theirs, does not exist or was removed.</summary>
    public async Task<JsonNode?> GetOwnedAsync(string email, string paymentMethodId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(paymentMethodId)) return null;
        JsonNode? pm;
        try
        {
            pm = await _stax.GetAsync($"payment-method/{Uri.EscapeDataString(paymentMethodId)}", ct);
        }
        catch (StaxApiException ex) when (ex.StaxStatus is 404 or 400 or 422)
        {
            return null;
        }
        if (pm == null || !string.IsNullOrEmpty(pm.Str("deleted_at"))) return null;
        var customers = await FindCustomersAsync(email, ct);
        return customers.Any(c => c.Str("id") == pm.Str("customer_id")) ? pm : null;
    }

    public async Task<bool> RemoveAsync(string email, string paymentMethodId, CancellationToken ct = default)
    {
        var pm = await GetOwnedAsync(email, paymentMethodId, ct);
        if (pm == null) return false;
        await _stax.DeleteAsync($"payment-method/{Uri.EscapeDataString(paymentMethodId)}", ct);
        return true;
    }

    public static PaymentMethodDto ToDto(JsonNode m) => Fill(new PaymentMethodDto(), m);

    public static AdminPaymentMethodDto ToAdminDto(JsonNode m)
    {
        var dto = Fill(new AdminPaymentMethodDto(), m);
        dto.RemovedAt = m.Date("deleted_at");
        return dto;
    }

    private static T Fill<T>(T dto, JsonNode m) where T : PaymentMethodDto
    {
        var method = (m.Str("method") ?? "").ToLowerInvariant();
        dto.Id = m.Str("id") ?? string.Empty;
        dto.Type = method;
        dto.Nickname = m.Str("nickname");
        dto.PersonName = m.Str("person_name");
        dto.LastFour = m.Str("card_last_four");
        dto.CreatedAt = m.Date("created_at");
        if (method == "card")
        {
            dto.CardBrand = m.Str("card_type");
            var exp = m.Str("card_exp");
            if (!string.IsNullOrEmpty(exp) && exp.Length >= 4)
            {
                // Stax returns MMYYYY (or MMYY)
                dto.CardExpiry = exp.Length >= 6 ? $"{exp[..2]}/{exp.Substring(2, 4)}" : $"{exp[..2]}/{exp.Substring(2, 2)}";
            }
        }
        else
        {
            dto.BankName = m.Str("bank_name");
            dto.BankType = m.Str("bank_type");
            dto.BankHolderType = m.Str("bank_holder_type");
        }
        return dto;
    }
}
