using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.eShopWeb.PublicApi.Stax;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class PaymentMethodService
{
    private readonly StaxContext _db;
    private readonly StaxClient _stax;
    private readonly ShopperService _shoppers;

    public PaymentMethodService(StaxContext db, StaxClient stax, ShopperService shoppers)
    {
        _db = db;
        _stax = stax;
        _shoppers = shoppers;
    }

    public static object ToDto(SavedPaymentMethod pm) => new
    {
        paymentMethodId = pm.Id,
        type = pm.Method,
        brand = pm.Brand,
        bankName = pm.BankName,
        last4 = pm.Last4,
        expiry = pm.CardExp,
        nickname = pm.Nickname,
        createdAt = pm.CreatedAt,
    };

    public async Task<SavedPaymentMethod> AddAsync(string email, LooseBody body, CancellationToken ct)
    {
        var type = (body.Str("type", "method", "kind") ?? "").ToLowerInvariant();
        if (type == "") type = body.Raw("cardNumber", "number") != null ? "card"
            : body.Raw("bankAccount", "accountNumber") != null ? "bank" : "";
        if (type != "card" && type != "bank")
        {
            throw ApiException.BadRequest("'type' must be either 'card' or 'bank'.");
        }

        var personName = body.Str("personName", "name", "cardholderName", "accountHolderName", "holderName")
            ?? throw ApiException.BadRequest("'personName' (the card or account holder) is required.");

        var req = new Dictionary<string, object?>
        {
            ["method"] = type,
            ["person_name"] = personName,
        };
        AddOptional(req, "address_1", body.Str("address1", "addressLine1", "street"));
        AddOptional(req, "address_2", body.Str("address2", "addressLine2"));
        AddOptional(req, "address_city", body.Str("addressCity", "city"));
        AddOptional(req, "address_state", body.Str("addressState", "state"));
        AddOptional(req, "address_zip", body.Str("addressZip", "zip", "zipCode", "postalCode"));
        AddOptional(req, "address_country", body.Str("addressCountry", "country"));

        if (type == "card")
        {
            var number = (body.Str("cardNumber", "number") ?? "").Replace(" ", "").Replace("-", "");
            if (number.Length < 12 || !number.All(char.IsDigit))
            {
                throw ApiException.BadRequest("'cardNumber' must be the card's digits.");
            }
            var cvv = body.Str("cardCvv", "cvv", "cvc", "securityCode")
                ?? throw ApiException.BadRequest("'cardCvv' is required.");
            req["card_number"] = number;
            req["card_cvv"] = cvv;
            req["card_exp"] = ParseExpiry(body);
        }
        else
        {
            req["bank_account"] = body.Str("bankAccount", "accountNumber")
                ?? throw ApiException.BadRequest("'bankAccount' is required.");
            req["bank_routing"] = body.Str("bankRouting", "routingNumber", "routing")
                ?? throw ApiException.BadRequest("'bankRouting' is required.");
            req["bank_name"] = body.Str("bankName") ?? "Bank";
            var bankType = (body.Str("bankType", "accountType") ?? "checking").ToLowerInvariant();
            var holder = (body.Str("bankHolderType", "holderType") ?? "personal").ToLowerInvariant();
            if (bankType != "checking" && bankType != "savings")
                throw ApiException.BadRequest("'bankType' must be 'checking' or 'savings'.");
            if (holder != "personal" && holder != "business")
                throw ApiException.BadRequest("'bankHolderType' must be 'personal' or 'business'.");
            req["bank_type"] = bankType;
            req["bank_holder_type"] = holder;
        }

        req["customer_id"] = await _shoppers.EnsureCustomerAsync(email, ct);
        var created = await _stax.CreatePaymentMethodAsync(req, ct);

        var saved = new SavedPaymentMethod
        {
            Id = created["id"]!.GetValue<string>(),
            ShopperEmail = email,
            Method = type,
            Nickname = created["nickname"]?.GetValue<string>() ?? "",
            Brand = type == "card" ? created["card_type"]?.GetValue<string>() : null,
            BankName = type == "bank" ? created["bank_name"]?.GetValue<string>() : null,
            Last4 = created["card_last_four"]?.GetValue<string>() ?? "",
            CardExp = type == "card" ? created["card_exp"]?.GetValue<string>() : null,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.SavedPaymentMethods.Add(saved);
        await _db.SaveChangesAsync(ct);
        return saved;
    }

    private static void AddOptional(Dictionary<string, object?> req, string key, string? value)
    {
        if (value != null) req[key] = value;
    }

    private static string ParseExpiry(LooseBody body)
    {
        var month = body.Int("expMonth", "cardExpMonth", "expiryMonth");
        var year = body.Int("expYear", "cardExpYear", "expiryYear");
        if (month == null || year == null)
        {
            var raw = body.Str("cardExp", "expiry", "expiration", "exp")
                ?? throw ApiException.BadRequest("'cardExp' (MMYY) is required.");
            var digits = new string(raw.Where(char.IsDigit).ToArray());
            if (digits.Length == 4) { month = int.Parse(digits[..2]); year = 2000 + int.Parse(digits[2..]); }
            else if (digits.Length == 6) { month = int.Parse(digits[..2]); year = int.Parse(digits[2..]); }
            else throw ApiException.BadRequest("'cardExp' must look like MMYY.");
        }
        if (year < 100) year += 2000;
        if (month < 1 || month > 12) throw ApiException.BadRequest("The card's expiry month is invalid.");
        return $"{month:00}{year % 100:00}";
    }

    public async Task<List<SavedPaymentMethod>> ListAsync(string email, CancellationToken ct)
    {
        var list = await _db.SavedPaymentMethods
            .Where(p => p.ShopperEmail == email && p.RemovedAt == null)
            .ToListAsync(ct);
        return list.OrderByDescending(p => p.CreatedAt).ToList();
    }

    public async Task<SavedPaymentMethod> RequireActiveAsync(string email, string id, CancellationToken ct)
    {
        var pm = await _db.SavedPaymentMethods.FirstOrDefaultAsync(p => p.Id == id && p.ShopperEmail == email, ct);
        if (pm == null || pm.RemovedAt != null)
        {
            throw ApiException.NotFound("There is no saved payment method with that id.");
        }
        return pm;
    }

    public async Task<SavedPaymentMethod> RemoveAsync(string email, string id, CancellationToken ct)
    {
        var pm = await RequireActiveAsync(email, id, ct);
        try
        {
            await _stax.DeletePaymentMethodAsync(id, ct);
        }
        catch (ApiException ex) when (ex.StatusCode is 404 or 422)
        {
            // Already gone at Stax: the shop's copy should still be marked removed.
        }
        pm.RemovedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return pm;
    }

    // ---- support view ----

    public async Task<List<object>?> ListForSupportAsync(string email, CancellationToken ct)
    {
        email = ShopperService.NormalizeEmail(email);
        var customerIds = await _shoppers.ResolveCustomerIdsAsync(email, ct);
        if (customerIds.Count == 0) return null;

        var result = new List<(DateTimeOffset created, object dto)>();
        foreach (var customerId in customerIds)
        {
            foreach (var pm in await _stax.ListPaymentMethodsAsync(customerId, ct))
            {
                var created = ParseStaxTime(pm["created_at"]?.GetValue<string>()) ?? DateTimeOffset.MinValue;
                var method = pm["method"]?.GetValue<string>();
                result.Add((created, new
                {
                    paymentMethodId = pm["id"]?.GetValue<string>(),
                    type = method,
                    brand = method == "card" ? pm["card_type"]?.GetValue<string>() : null,
                    bankName = method == "bank" ? pm["bank_name"]?.GetValue<string>() : null,
                    last4 = pm["card_last_four"]?.GetValue<string>(),
                    nickname = pm["nickname"]?.GetValue<string>(),
                    createdAt = created == DateTimeOffset.MinValue ? (DateTimeOffset?)null : created,
                    removedAt = ParseStaxTime(pm["deleted_at"]?.GetValue<string>()),
                }));
            }
        }
        return result.OrderByDescending(r => r.created).Select(r => r.dto).ToList();
    }

    /// <summary>Stax timestamps look like "2026-11-09 16:31:51" and are UTC.</summary>
    public static DateTimeOffset? ParseStaxTime(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t) ? t : null;
    }
}
