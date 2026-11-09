using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Stax;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class SavePaymentMethodRequest : BaseRequest
{
    /// <summary><c>card</c> or <c>bank</c>.</summary>
    public string? Method { get; set; }

    /// <summary>Name of the card holder or the account holder.</summary>
    public string? PersonName { get; set; }

    // card
    public string? CardNumber { get; set; }
    public string? CardExp { get; set; }
    public int? ExpMonth { get; set; }
    public int? ExpYear { get; set; }
    public string? Cvv { get; set; }
    public string? Zip { get; set; }

    // bank
    public string? AccountNumber { get; set; }
    public string? RoutingNumber { get; set; }
    /// <summary><c>checking</c> or <c>savings</c>.</summary>
    public string? AccountType { get; set; }
    /// <summary><c>personal</c> or <c>business</c>.</summary>
    public string? HolderType { get; set; }
    public string? BankName { get; set; }
}

public class PaymentMethodDto
{
    public string PaymentMethodId { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? LastFour { get; set; }
    public string? Nickname { get; set; }
    public string? HolderName { get; set; }
    public int? ExpMonth { get; set; }
    public int? ExpYear { get; set; }
    public string? AccountType { get; set; }
    public string? HolderType { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    /// <summary>When the payment method was removed; null while it is still usable.</summary>
    public DateTimeOffset? RemovedAt { get; set; }
}

public class PaymentMethodService
{
    private static readonly Regex Digits = new(@"^\d+$", RegexOptions.Compiled);

    private readonly CatalogContext _db;
    private readonly StaxGateway _stax;
    private readonly KeyedLock _locks;

    public PaymentMethodService(CatalogContext db, StaxGateway stax, KeyedLock locks)
    {
        _db = db;
        _stax = stax;
        _locks = locks;
    }

    public static string CustomerLockKey(string buyerId) => "shopper:" + buyerId.ToLowerInvariant();

    // ---- customers ------------------------------------------------------------------------------

    /// <summary>The shopper's Stax customer, created on first use.</summary>
    public async Task<StaxCustomer> EnsureCustomerAsync(string buyerId, string? personName, CancellationToken ct)
    {
        var existing = await _db.StaxCustomers.FirstOrDefaultAsync(c => c.BuyerId == buyerId, ct);
        if (existing is not null)
        {
            return existing;
        }

        using var _ = await _locks.AcquireAsync(CustomerLockKey(buyerId));
        existing = await _db.StaxCustomers.FirstOrDefaultAsync(c => c.BuyerId == buyerId, ct);
        if (existing is not null)
        {
            return existing;
        }

        var (first, last) = SplitName(personName, buyerId);
        var customer = await _stax.FindOrCreateCustomerAsync(buyerId, first, last, null, ct);
        var id = customer.Str("id") ?? throw new ApiProblemException(502, "Stax did not return a customer id.");
        existing = new StaxCustomer { BuyerId = buyerId, StaxCustomerId = id };
        _db.StaxCustomers.Add(existing);
        await _db.SaveChangesAsync(ct);
        return existing;
    }

    private static (string First, string Last) SplitName(string? personName, string buyerId)
    {
        var name = string.IsNullOrWhiteSpace(personName) ? buyerId.Split('@')[0] : personName.Trim();
        var parts = name.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 2 ? (parts[0], parts[1]) : (parts.Length == 1 ? parts[0] : "Shopper", "-");
    }

    // ---- save -----------------------------------------------------------------------------------

    public async Task<PaymentMethodDto> SaveAsync(string buyerId, SavePaymentMethodRequest request, CancellationToken ct)
    {
        var body = BuildStaxBody(request);
        var customer = await EnsureCustomerAsync(buyerId, request.PersonName, ct);
        body["customer_id"] = customer.StaxCustomerId;

        var created = await _stax.CreatePaymentMethodAsync(body, ct);
        var id = created.Str("id") ?? throw new ApiProblemException(502, "Stax did not return a payment method id.");

        var row = new SavedPaymentMethod
        {
            StaxPaymentMethodId = id,
            BuyerId = buyerId,
            StaxCustomerId = customer.StaxCustomerId,
        };
        Apply(row, created);
        _db.SavedPaymentMethods.Add(row);
        await _db.SaveChangesAsync(ct);
        return ToDto(created, row);
    }

    private static Dictionary<string, object?> BuildStaxBody(SavePaymentMethodRequest request)
    {
        var method = request.Method?.Trim().ToLowerInvariant();
        if (method is not ("card" or "bank"))
        {
            throw ApiProblemException.BadRequest("method must be 'card' or 'bank'.");
        }

        if (string.IsNullOrWhiteSpace(request.PersonName))
        {
            throw ApiProblemException.BadRequest("personName (the card or account holder) is required.");
        }

        var body = new Dictionary<string, object?>
        {
            ["method"] = method,
            ["person_name"] = request.PersonName.Trim(),
        };

        if (method == "card")
        {
            var number = Regex.Replace(request.CardNumber ?? string.Empty, @"[\s-]", string.Empty);
            if (!Digits.IsMatch(number) || number.Length is < 12 or > 19)
            {
                throw ApiProblemException.BadRequest("cardNumber must have 12 to 19 digits.");
            }

            body["card_number"] = number;
            body["card_exp"] = ParseExpiry(request);
            if (!string.IsNullOrWhiteSpace(request.Cvv))
            {
                if (!Digits.IsMatch(request.Cvv) || request.Cvv.Length is < 3 or > 4)
                {
                    throw ApiProblemException.BadRequest("cvv must have 3 or 4 digits.");
                }

                body["card_cvv"] = request.Cvv;
            }

            if (!string.IsNullOrWhiteSpace(request.Zip))
            {
                body["address_zip"] = request.Zip.Trim();
            }
        }
        else
        {
            var account = Regex.Replace(request.AccountNumber ?? string.Empty, @"[\s-]", string.Empty);
            if (!Digits.IsMatch(account) || account.Length is < 4 or > 17)
            {
                throw ApiProblemException.BadRequest("accountNumber must have 4 to 17 digits.");
            }

            var routing = Regex.Replace(request.RoutingNumber ?? string.Empty, @"[\s-]", string.Empty);
            if (!Digits.IsMatch(routing) || routing.Length != 9)
            {
                throw ApiProblemException.BadRequest("routingNumber must have 9 digits.");
            }

            var accountType = (request.AccountType ?? "checking").Trim().ToLowerInvariant();
            if (accountType is not ("checking" or "savings"))
            {
                throw ApiProblemException.BadRequest("accountType must be 'checking' or 'savings'.");
            }

            var holderType = (request.HolderType ?? "personal").Trim().ToLowerInvariant();
            if (holderType is not ("personal" or "business"))
            {
                throw ApiProblemException.BadRequest("holderType must be 'personal' or 'business'.");
            }

            body["bank_account"] = account;
            body["bank_routing"] = routing;
            body["bank_type"] = accountType;
            body["bank_holder_type"] = holderType;
            if (!string.IsNullOrWhiteSpace(request.BankName))
            {
                body["bank_name"] = request.BankName.Trim();
            }
        }

        return body;
    }

    private static string ParseExpiry(SavePaymentMethodRequest request)
    {
        int month, year;
        if (request.ExpMonth is not null && request.ExpYear is not null)
        {
            month = request.ExpMonth.Value;
            year = request.ExpYear.Value;
        }
        else
        {
            var digits = Regex.Replace(request.CardExp ?? string.Empty, @"[\s/-]", string.Empty);
            if (!Digits.IsMatch(digits) || digits.Length is not (4 or 6))
            {
                throw ApiProblemException.BadRequest("The card's expiry is required, as expMonth and expYear or cardExp (MMYY).");
            }

            month = int.Parse(digits[..2], CultureInfo.InvariantCulture);
            year = int.Parse(digits[2..], CultureInfo.InvariantCulture);
        }

        if (year < 100)
        {
            year += 2000;
        }

        if (month is < 1 or > 12)
        {
            throw ApiProblemException.BadRequest("The card's expiry month must be between 1 and 12.");
        }

        var now = DateTime.UtcNow;
        if (year < now.Year || (year == now.Year && month < now.Month))
        {
            throw ApiProblemException.Unprocessable("The card has expired.");
        }

        return month.ToString("00", CultureInfo.InvariantCulture) + (year % 100).ToString("00", CultureInfo.InvariantCulture);
    }

    // ---- list, remove ---------------------------------------------------------------------------

    /// <summary>The shopper's payment methods that can still be used.</summary>
    public async Task<List<PaymentMethodDto>> ListAsync(string buyerId, CancellationToken ct)
    {
        var customer = await _db.StaxCustomers.FirstOrDefaultAsync(c => c.BuyerId == buyerId, ct);
        if (customer is null)
        {
            return new List<PaymentMethodDto>();
        }

        var active = await _stax.ListCustomerPaymentMethodsAsync(Guid.Parse(customer.StaxCustomerId), ct);
        var local = await _db.SavedPaymentMethods.Where(p => p.BuyerId == buyerId).ToListAsync(ct);
        var result = new List<PaymentMethodDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in active)
        {
            var id = item.Str("id");
            if (id is null || item.Date("deleted_at") is not null)
            {
                continue;
            }

            seen.Add(id);
            var row = local.FirstOrDefault(p => string.Equals(p.StaxPaymentMethodId, id, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                row = new SavedPaymentMethod { StaxPaymentMethodId = id, BuyerId = buyerId, StaxCustomerId = customer.StaxCustomerId };
                _db.SavedPaymentMethods.Add(row);
                local.Add(row);
            }

            row.RemovedAt = null;
            Apply(row, item);
            result.Add(ToDto(item, row));
        }

        // Saved here but no longer active at Stax: removed behind the shop's back.
        foreach (var row in local.Where(p => p.RemovedAt is null && !seen.Contains(p.StaxPaymentMethodId)))
        {
            row.RemovedAt = await RemovedAtAsync(row, ct);
        }

        await _db.SaveChangesAsync(ct);
        return result.OrderBy(p => p.CreatedAt).ToList();
    }

    private async Task<DateTimeOffset> RemovedAtAsync(SavedPaymentMethod row, CancellationToken ct)
    {
        try
        {
            var staxView = await _stax.GetPaymentMethodAsync(Guid.Parse(row.StaxPaymentMethodId), ct);
            return staxView.Date("deleted_at") ?? DateTimeOffset.UtcNow;
        }
        catch (StaxException)
        {
            return DateTimeOffset.UtcNow;
        }
    }

    /// <summary>The caller's own, not yet removed payment method, or a 404.</summary>
    public async Task<SavedPaymentMethod> RequireUsableAsync(string buyerId, string paymentMethodId, CancellationToken ct)
    {
        if (!Guid.TryParse(paymentMethodId, out var guid))
        {
            throw ApiProblemException.NotFound("Payment method not found.");
        }

        var id = guid.ToString("D");
        var row = await _db.SavedPaymentMethods.FirstOrDefaultAsync(p => p.StaxPaymentMethodId == id && p.BuyerId == buyerId, ct);
        if (row is null)
        {
            // Maybe saved at Stax by other means: it is the shopper's if their customer holds it.
            await ListAsync(buyerId, ct);
            row = await _db.SavedPaymentMethods.FirstOrDefaultAsync(p => p.StaxPaymentMethodId == id && p.BuyerId == buyerId, ct);
        }

        if (row is null || row.RemovedAt is not null)
        {
            throw ApiProblemException.NotFound("Payment method not found.");
        }

        return row;
    }

    public async Task RemoveAsync(string buyerId, string paymentMethodId, CancellationToken ct)
    {
        var row = await RequireUsableAsync(buyerId, paymentMethodId, ct);
        try
        {
            await _stax.DeletePaymentMethodAsync(Guid.Parse(row.StaxPaymentMethodId), ct);
        }
        catch (StaxException ex) when (ex.StaxStatus == 404)
        {
            // already gone at Stax
        }

        row.RemovedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    // ---- support --------------------------------------------------------------------------------

    /// <summary>Every payment method the shopper ever saved, removed ones included.</summary>
    public async Task<List<PaymentMethodDto>> ListAllForSupportAsync(string email, CancellationToken ct)
    {
        var key = email.Trim().ToLowerInvariant();
        var customers = await _db.StaxCustomers.Where(c => c.BuyerId.ToLower() == key).ToListAsync(ct);
        var rows = await _db.SavedPaymentMethods.Where(p => p.BuyerId.ToLower() == key).ToListAsync(ct);
        if (customers.Count == 0 && rows.Count == 0)
        {
            throw ApiProblemException.NotFound("No shopper with that e-mail has saved payment methods.");
        }

        var views = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        // What Stax still lists for the customer (this includes methods the shop never recorded) ...
        foreach (var customer in customers)
        {
            foreach (var item in await _stax.ListCustomerPaymentMethodsAsync(Guid.Parse(customer.StaxCustomerId), ct))
            {
                if (item.Str("id") is { } id)
                {
                    views[id] = item;
                }
            }
        }

        // ... plus everything the shop saved, asked for one by one: Stax answers for removed ones too.
        foreach (var row in rows.Where(r => !views.ContainsKey(r.StaxPaymentMethodId)))
        {
            try
            {
                views[row.StaxPaymentMethodId] =
                    await _stax.GetPaymentMethodAsync(Guid.Parse(row.StaxPaymentMethodId), ct);
            }
            catch (StaxException ex) when (ex.StaxStatus == 404)
            {
                // Stax no longer has it: report what the shop remembers.
            }
        }

        var result = new List<PaymentMethodDto>();
        foreach (var (id, view) in views)
        {
            var row = rows.FirstOrDefault(r => string.Equals(r.StaxPaymentMethodId, id, StringComparison.OrdinalIgnoreCase));
            var dto = ToDto(view, row);
            dto.PaymentMethodId = id;
            dto.RemovedAt = view.Date("deleted_at") ?? row?.RemovedAt;
            result.Add(dto);
        }

        foreach (var row in rows.Where(r => !views.ContainsKey(r.StaxPaymentMethodId)))
        {
            result.Add(ToDto(default, row));
        }

        return result.OrderBy(p => p.CreatedAt).ThenBy(p => p.PaymentMethodId).ToList();
    }

    // ---- mapping --------------------------------------------------------------------------------

    private static void Apply(SavedPaymentMethod row, JsonElement view)
    {
        row.Method = view.Str("method") ?? row.Method;
        row.Brand = view.Str("card_type") ?? row.Brand;
        row.LastFour = view.Str("card_last_four") ?? row.LastFour;
        row.Nickname = view.Str("nickname") ?? row.Nickname;
        if (view.Date("created_at") is { } created)
        {
            row.CreatedAt = created;
        }
    }

    private static PaymentMethodDto ToDto(JsonElement view, SavedPaymentMethod? row)
    {
        var dto = new PaymentMethodDto
        {
            PaymentMethodId = row?.StaxPaymentMethodId ?? view.Str("id") ?? string.Empty,
            Method = view.Str("method") ?? row?.Method ?? string.Empty,
            Brand = view.Str("card_type") ?? row?.Brand,
            LastFour = view.Str("card_last_four") ?? row?.LastFour,
            Nickname = view.Str("nickname") ?? row?.Nickname,
            HolderName = view.Str("person_name"),
            AccountType = view.Str("bank_type"),
            HolderType = view.Str("bank_holder_type"),
            CreatedAt = view.Date("created_at") ?? row?.CreatedAt,
            RemovedAt = view.Date("deleted_at") ?? row?.RemovedAt,
        };

        // Stax reports a card's expiry as MMYYYY.
        var exp = view.Str("card_exp");
        if (exp is { Length: 6 } && int.TryParse(exp[..2], out var month) && int.TryParse(exp[2..], out var year))
        {
            dto.ExpMonth = month;
            dto.ExpYear = year;
        }

        if (dto.Method == "bank")
        {
            dto.Brand = null;
        }

        return dto;
    }
}
