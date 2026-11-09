using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class SavePaymentMethodRequest
{
    private string? _type, _personName, _postalCode, _cardNumber, _cvv, _accountNumber, _routingNumber, _accountType;
    private int? _expMonth, _expYear;

    /// <summary>"card" or "bank".</summary>
    public string? Type { get => _type; set => _type = value; }
    public string? Method { get => _type; set => _type ??= value; }

    /// <summary>Name on the card / account holder.</summary>
    public string? PersonName { get => _personName; set => _personName = value; }
    public string? Name { get => _personName; set => _personName ??= value; }
    public string? PostalCode { get => _postalCode; set => _postalCode = value; }
    public string? Zip { get => _postalCode; set => _postalCode ??= value; }

    // card
    public string? CardNumber { get => _cardNumber; set => _cardNumber = value; }
    public string? Number { get => _cardNumber; set => _cardNumber ??= value; }
    public int? ExpMonth { get => _expMonth; set => _expMonth = value; }
    public int? ExpiryMonth { get => _expMonth; set => _expMonth ??= value; }
    public int? ExpYear { get => _expYear; set => _expYear = value; }
    public int? ExpiryYear { get => _expYear; set => _expYear ??= value; }

    /// <summary>"MM/YY" or "MM/YYYY", instead of expMonth and expYear.</summary>
    public string? Expiry
    {
        get => null;
        set
        {
            var parts = (value ?? string.Empty).Split('/', '-');
            if (parts.Length == 2 && int.TryParse(parts[0], out var m) && int.TryParse(parts[1], out var y))
            {
                _expMonth ??= m;
                _expYear ??= y;
            }
        }
    }

    public string? Cvv { get => _cvv; set => _cvv = value; }
    public string? Cvc { get => _cvv; set => _cvv ??= value; }

    // bank
    public string? AccountNumber { get => _accountNumber; set => _accountNumber = value; }
    public string? RoutingNumber { get => _routingNumber; set => _routingNumber = value; }

    /// <summary>"checking" or "savings".</summary>
    public string? AccountType { get => _accountType; set => _accountType = value; }

    /// <summary>"personal" or "business".</summary>
    public string? HolderType { get; set; }
}

public record PaymentMethodDto(
    string Id,
    string Type,
    string? Brand,
    string? Last4,
    string? Nickname,
    string? Expires,
    DateTimeOffset? CreatedAt);

public record SupportPaymentMethodDto(
    string Id,
    string Type,
    string? Brand,
    string? Last4,
    string? Nickname,
    string? Expires,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? RemovedAt);

public class PaymentMethodService
{
    private readonly CatalogContext _db;
    private readonly StaxGateway _stax;
    private readonly ShopperDirectory _shoppers;

    public PaymentMethodService(CatalogContext db, StaxGateway stax, ShopperDirectory shoppers)
    {
        _db = db;
        _stax = stax;
        _shoppers = shoppers;
    }

    public async Task<PaymentMethodDto> Save(string email, SavePaymentMethodRequest request, CancellationToken ct)
    {
        var type = request.Type?.Trim().ToLowerInvariant();
        object body;
        var customerId = await ValidateAndFindCustomer(email, ct);
        var name = string.IsNullOrWhiteSpace(request.PersonName) ? null : request.PersonName.Trim();
        var zip = string.IsNullOrWhiteSpace(request.PostalCode) ? null : request.PostalCode.Trim();

        switch (type)
        {
            case "card":
            {
                var number = Digits(request.CardNumber);
                if (number.Length is < 12 or > 19)
                    throw AppException.BadRequest("cardNumber must have 12 to 19 digits.");
                if (request.ExpMonth is null or < 1 or > 12)
                    throw AppException.BadRequest("expMonth must be between 1 and 12.");
                if (request.ExpYear is null)
                    throw AppException.BadRequest("expYear is required.");
                var year = request.ExpYear.Value < 100 ? 2000 + request.ExpYear.Value : request.ExpYear.Value;
                if (year < 2000 || year > 2100)
                    throw AppException.BadRequest("expYear is not valid.");
                var now = DateTime.UtcNow;
                if (year < now.Year || (year == now.Year && request.ExpMonth.Value < now.Month))
                    throw AppException.Unprocessable("The card has expired.");
                if (name == null)
                    throw AppException.BadRequest("personName (the name on the card) is required.");
                var cvv = Digits(request.Cvv);
                if (cvv.Length is < 3 or > 4)
                    throw AppException.BadRequest("cvv must have 3 or 4 digits.");
                body = new Dictionary<string, object?>
                {
                    ["customer_id"] = customerId,
                    ["method"] = "card",
                    ["person_name"] = name,
                    ["card_number"] = number,
                    ["card_exp"] = $"{request.ExpMonth.Value:00}{year % 100:00}",
                    ["card_cvv"] = cvv,
                    ["address_zip"] = zip
                };
                break;
            }
            case "bank":
            {
                var account = Digits(request.AccountNumber);
                if (account.Length is < 4 or > 17)
                    throw AppException.BadRequest("accountNumber must have 4 to 17 digits.");
                var routing = Digits(request.RoutingNumber);
                if (routing.Length != 9)
                    throw AppException.BadRequest("routingNumber must have 9 digits.");
                var accountType = request.AccountType?.Trim().ToLowerInvariant();
                if (accountType is not ("checking" or "savings"))
                    throw AppException.BadRequest("accountType must be \"checking\" or \"savings\".");
                var holder = request.HolderType?.Trim().ToLowerInvariant() ?? "personal";
                if (holder is not ("personal" or "business"))
                    throw AppException.BadRequest("holderType must be \"personal\" or \"business\".");
                if (name == null)
                    throw AppException.BadRequest("personName (the account holder) is required.");
                body = new Dictionary<string, object?>
                {
                    ["customer_id"] = customerId,
                    ["method"] = "bank",
                    ["person_name"] = name,
                    ["bank_account"] = account,
                    ["bank_routing"] = routing,
                    ["bank_type"] = accountType,
                    ["bank_holder_type"] = holder,
                    ["address_zip"] = zip
                };
                break;
            }
            default:
                throw AppException.BadRequest("type must be \"card\" or \"bank\".");
        }

        var withoutBlanks = ((Dictionary<string, object?>)body).Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value);
        var created = await _stax.CreatePaymentMethod(withoutBlanks, ct);
        var id = StaxGateway.Str(created, "id")
                 ?? throw new AppException(HttpStatusCode.BadGateway, "Stax did not return the saved payment method.");
        await Record(email, customerId, created, ct);
        return ToDto(created);
    }

    private async Task<string> ValidateAndFindCustomer(string email, CancellationToken ct) =>
        await _shoppers.EnsureCustomerId(email, ct);

    public async Task<List<PaymentMethodDto>> List(string email, CancellationToken ct)
    {
        var customerId = await _shoppers.FindCustomerId(email, ct);
        if (customerId == null) return new List<PaymentMethodDto>();
        var items = await _stax.ListCustomerPaymentMethods(customerId, ct);
        var result = new List<PaymentMethodDto>();
        foreach (var pm in items.Where(p => StaxGateway.Str(p, "deleted_at") == null))
        {
            await Record(email, customerId, pm, ct);
            result.Add(ToDto(pm));
        }
        return result.OrderBy(p => p.CreatedAt).ToList();
    }

    public async Task Remove(string email, string paymentMethodId, CancellationToken ct)
    {
        var customerId = await _shoppers.FindCustomerId(email, ct);
        if (customerId == null || !Guid.TryParse(paymentMethodId, out _))
            throw AppException.NotFound("No such saved payment method.");

        JsonElement pm;
        try
        {
            pm = await _stax.GetPaymentMethod(paymentMethodId, ct);
        }
        catch (AppException e) when (e.StatusCode == HttpStatusCode.NotFound || e.StaxStatus == 422)
        {
            throw AppException.NotFound("No such saved payment method.");
        }

        if (!string.Equals(StaxGateway.Str(pm, "customer_id"), customerId, StringComparison.OrdinalIgnoreCase)
            || StaxGateway.Str(pm, "deleted_at") != null)
            throw AppException.NotFound("No such saved payment method.");

        var removed = await _stax.DeletePaymentMethod(paymentMethodId, ct);
        await Record(email, customerId, removed.ValueKind == JsonValueKind.Object ? removed : pm, ct, removedFallback: true);
    }

    /// <summary>Keeps the shop-side ledger in line with what Stax says about a payment method.</summary>
    public async Task Record(string email, string customerId, JsonElement pm, CancellationToken ct, bool removedFallback = false)
    {
        var id = StaxGateway.Str(pm, "id");
        if (id == null) return;
        var row = await _db.SavedPaymentMethods.FirstOrDefaultAsync(p => p.Id == id, ct);
        var removedAt = StaxGateway.Time(pm, "deleted_at") ?? (removedFallback ? DateTimeOffset.UtcNow : null);
        if (row == null)
        {
            row = new SavedPaymentMethod
            {
                Id = id,
                BuyerEmail = email,
                StaxCustomerId = customerId,
                CreatedAt = StaxGateway.Time(pm, "created_at") ?? DateTimeOffset.UtcNow
            };
            _db.SavedPaymentMethods.Add(row);
        }
        row.Type = TypeOf(pm);
        row.Brand = Brand(pm);
        row.Last4 = StaxGateway.Str(pm, "card_last_four");
        if (removedAt != null) row.RemovedAt ??= removedAt;
        await _db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ support view

    /// <summary>Every card and bank account a shopper ever saved, active or removed, however it was saved.</summary>
    public async Task<List<SupportPaymentMethodDto>> ForSupport(string email, CancellationToken ct)
    {
        email = ShopperDirectory.Normalize(email);
        var customerIds = await _shoppers.AllCustomerIds(email, ct);
        var known = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        foreach (var customerId in customerIds)
        {
            // Active ones, whoever saved them.
            foreach (var pm in await _stax.ListCustomerPaymentMethods(customerId, ct))
                if (StaxGateway.Str(pm, "id") is { } id) known[id] = pm;

            // Removed ones are not listed by Stax any more, but the transactions made with them are.
            var tx = await _stax.ListCustomerTransactions(customerId, ct);
            foreach (var pmId in tx.Select(t => StaxGateway.Str(t, "payment_method_id"))
                         .Where(i => !string.IsNullOrEmpty(i)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (known.ContainsKey(pmId!)) continue;
                try { known[pmId!] = await _stax.GetPaymentMethod(pmId!, ct); }
                catch (AppException e) when (e.StatusCode == HttpStatusCode.NotFound) { }
            }
        }

        // Methods this shop recorded itself (covers removed ones that never carried a payment).
        var ledger = await _db.SavedPaymentMethods.AsNoTracking()
            .Where(p => p.BuyerEmail == email || customerIds.Contains(p.StaxCustomerId)).ToListAsync(ct);
        foreach (var row in ledger.Where(r => !known.ContainsKey(r.Id)))
        {
            try { known[row.Id] = await _stax.GetPaymentMethod(row.Id, ct); }
            catch (AppException e) when (e.StatusCode == HttpStatusCode.NotFound) { }
        }

        var result = new List<SupportPaymentMethodDto>();
        foreach (var (id, pm) in known)
        {
            // Only methods of this shopper's customers.
            var owner = StaxGateway.Str(pm, "customer_id");
            if (owner == null || !customerIds.Contains(owner, StringComparer.OrdinalIgnoreCase)) continue;
            var d = ToDto(pm);
            var removed = StaxGateway.Time(pm, "deleted_at")
                          ?? ledger.FirstOrDefault(l => l.Id == id)?.RemovedAt;
            result.Add(new SupportPaymentMethodDto(d.Id, d.Type, d.Brand, d.Last4, d.Nickname, d.Expires, d.CreatedAt, removed));
        }
        return result.OrderBy(p => p.CreatedAt).ToList();
    }

    // ------------------------------------------------------------------ helpers

    public static string TypeOf(JsonElement pm) =>
        StaxGateway.Str(pm, "method") == "bank" ? "bank" : "card";

    public static string? Brand(JsonElement pm) =>
        TypeOf(pm) == "bank" ? StaxGateway.Str(pm, "bank_type") : StaxGateway.Str(pm, "card_type");

    public static PaymentMethodDto ToDto(JsonElement pm)
    {
        string? expires = null;
        var exp = StaxGateway.Str(pm, "card_exp");
        if (TypeOf(pm) == "card" && exp is { Length: 6 })
            expires = $"{exp[..2]}/{exp[2..]}";
        return new PaymentMethodDto(
            StaxGateway.Str(pm, "id") ?? string.Empty,
            TypeOf(pm),
            Brand(pm),
            StaxGateway.Str(pm, "card_last_four"),
            StaxGateway.Str(pm, "nickname"),
            expires,
            StaxGateway.Time(pm, "created_at"));
    }

    private static string Digits(string? value) => Regex.Replace(value ?? string.Empty, @"[\s-]", string.Empty) is var s && s.All(char.IsDigit) ? s : string.Empty;
}
