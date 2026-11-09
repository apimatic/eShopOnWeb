using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Stax;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class SavePaymentMethodRequest
{
    /// <summary>"card" or "bank"</summary>
    public string? Method { get; set; }
    public string? PersonName { get; set; }

    // card
    public string? CardNumber { get; set; }
    /// <summary>Either CardExpMonth + CardExpYear, or CardExp as MMYY / MM/YY / MM/YYYY.</summary>
    public string? CardExp { get; set; }
    public int? CardExpMonth { get; set; }
    public int? CardExpYear { get; set; }
    public string? CardCvv { get; set; }
    public string? Zip { get; set; }

    // bank
    public string? BankAccount { get; set; }
    public string? BankRouting { get; set; }
    /// <summary>checking | savings</summary>
    public string? BankType { get; set; }
    /// <summary>personal | business</summary>
    public string? BankHolderType { get; set; }
    public string? BankName { get; set; }
}

public class PaymentMethodDto
{
    public string Id { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? BankType { get; set; }
    public string? Expiration { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}

public class SupportPaymentMethodDto : PaymentMethodDto
{
    public string? CustomerId { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
}

/// <summary>Stax customers and the cards / bank accounts saved on them.</summary>
public class PaymentMethodService
{
    private readonly StaxClient _stax;
    private readonly CatalogContext _db;

    public PaymentMethodService(StaxClient stax, CatalogContext db)
    {
        _stax = stax;
        _db = db;
    }

    public static string Normalize(string email) => email.Trim().ToLowerInvariant();

    // ---------------------------------------------------------------- customers

    /// <summary>All non-deleted Stax customers with this exact email (not creating anything).</summary>
    public async Task<List<JsonNode>> FindCustomersAsync(string email, CancellationToken ct)
    {
        var rows = await _stax.GetAllAsync("customer?email=" + Uri.EscapeDataString(email), ct);
        return rows.Where(c => string.Equals(c.Str("email")?.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase)
                               && string.IsNullOrEmpty(c.Str("deleted_at"))).ToList();
    }

    public async Task<string?> FindCustomerIdAsync(string email, CancellationToken ct)
    {
        var key = Normalize(email);
        var link = await _db.StaxCustomerLinks.FirstOrDefaultAsync(l => l.Email == key, ct);
        if (link != null) return link.StaxCustomerId;
        var found = (await FindCustomersAsync(email, ct)).FirstOrDefault();
        if (found == null) return null;
        return await LinkAsync(key, found.Str("id")!, ct);
    }

    public async Task<string> EnsureCustomerAsync(string email, CancellationToken ct)
    {
        var existing = await FindCustomerIdAsync(email, ct);
        if (existing != null) return existing;

        var local = email.Split('@')[0];
        var created = await _stax.PostAsync("customer", new
        {
            firstname = string.IsNullOrWhiteSpace(local) ? "Shopper" : local,
            lastname = "Shopper",
            email = email.Trim(),
            reference = "eShopOnWeb shopper"
        }, ct);
        return await LinkAsync(Normalize(email), created.Str("id")!, ct);
    }

    private async Task<string> LinkAsync(string key, string staxId, CancellationToken ct)
    {
        _db.StaxCustomerLinks.Add(new StaxCustomerLink { Email = key, StaxCustomerId = staxId });
        try
        {
            await _db.SaveChangesAsync(ct);
            return staxId;
        }
        catch (DbUpdateException)
        {
            // another request linked this shopper first
            _db.ChangeTracker.Clear();
            var link = await _db.StaxCustomerLinks.AsNoTracking().FirstAsync(l => l.Email == key, ct);
            return link.StaxCustomerId;
        }
    }

    // ---------------------------------------------------------------- shopper payment methods

    public async Task<PaymentMethodDto> SaveAsync(string email, SavePaymentMethodRequest request, CancellationToken ct)
    {
        var body = BuildStaxBody(request, out var method);
        var customerId = await EnsureCustomerAsync(email, ct);
        body["customer_id"] = customerId;

        var created = await _stax.PostAsync("payment-method", body, ct);
        var id = created.Str("id") ?? throw new ApiException(502, "Stax did not return a payment method id.");

        _db.SavedPaymentMethods.Add(new SavedPaymentMethod
        {
            BuyerEmail = Normalize(email),
            StaxPaymentMethodId = id,
            Method = method,
            Last4 = created.Str("card_last_four") ?? string.Empty,
            Description = Describe(created)
        });
        await _db.SaveChangesAsync(ct);
        return ToDto(created);
    }

    private static Dictionary<string, object?> BuildStaxBody(SavePaymentMethodRequest r, out string method)
    {
        method = (r.Method ?? string.Empty).Trim().ToLowerInvariant();
        var body = new Dictionary<string, object?>();
        if (string.IsNullOrWhiteSpace(r.PersonName)) throw ApiException.BadRequest("personName is required.");
        body["person_name"] = r.PersonName.Trim();

        if (method == "card")
        {
            var number = new string((r.CardNumber ?? string.Empty).Where(char.IsDigit).ToArray());
            if (number.Length < 12 || number.Length > 19) throw ApiException.BadRequest("cardNumber must have 12 to 19 digits.");
            int month, year;
            if (r.CardExpMonth.HasValue && r.CardExpYear.HasValue)
            {
                month = r.CardExpMonth.Value;
                year = r.CardExpYear.Value;
            }
            else
            {
                var digits = new string((r.CardExp ?? string.Empty).Where(char.IsDigit).ToArray());
                if (digits.Length != 4 && digits.Length != 6) throw ApiException.BadRequest("Provide the card expiry as cardExpMonth and cardExpYear, or cardExp (MMYY).");
                month = int.Parse(digits[..2]);
                year = int.Parse(digits[2..]);
            }
            if (year < 100) year += 2000;
            if (month < 1 || month > 12) throw ApiException.BadRequest("The card expiry month must be between 1 and 12.");
            if (new DateTime(year, month, 1).AddMonths(1) <= DateTime.UtcNow) throw ApiException.Unprocessable("The card has expired.");

            body["method"] = "card";
            body["card_number"] = number;
            body["card_exp"] = $"{month:00}{year % 100:00}";
            if (!string.IsNullOrWhiteSpace(r.CardCvv)) body["card_cvv"] = r.CardCvv.Trim();
            if (!string.IsNullOrWhiteSpace(r.Zip)) body["address_zip"] = r.Zip.Trim();
        }
        else if (method == "bank")
        {
            var account = new string((r.BankAccount ?? string.Empty).Where(char.IsDigit).ToArray());
            var routing = new string((r.BankRouting ?? string.Empty).Where(char.IsDigit).ToArray());
            if (account.Length < 4 || account.Length > 17) throw ApiException.BadRequest("bankAccount must have 4 to 17 digits.");
            if (routing.Length != 9) throw ApiException.BadRequest("bankRouting must have 9 digits.");
            var type = (r.BankType ?? "checking").Trim().ToLowerInvariant();
            var holder = (r.BankHolderType ?? "personal").Trim().ToLowerInvariant();
            if (type != "checking" && type != "savings") throw ApiException.BadRequest("bankType must be checking or savings.");
            if (holder != "personal" && holder != "business") throw ApiException.BadRequest("bankHolderType must be personal or business.");

            body["method"] = "bank";
            body["bank_account"] = account;
            body["bank_routing"] = routing;
            body["bank_type"] = type;
            body["bank_holder_type"] = holder;
            if (!string.IsNullOrWhiteSpace(r.BankName)) body["bank_name"] = r.BankName.Trim();
        }
        else
        {
            throw ApiException.BadRequest("method must be \"card\" or \"bank\".");
        }
        return body;
    }

    public async Task<List<PaymentMethodDto>> ListAsync(string email, CancellationToken ct)
    {
        var key = Normalize(email);
        var customerId = await FindCustomerIdAsync(email, ct);
        if (customerId == null) return new List<PaymentMethodDto>();

        var live = await _stax.GetAsync($"customer/{customerId}/payment-method", ct);
        var rows = (live as JsonArray)?.Where(x => x != null).Select(x => x!).ToList()
                   ?? (live["data"] as JsonArray)?.Where(x => x != null).Select(x => x!).ToList()
                   ?? new List<JsonNode>();
        rows = rows.Where(r => string.IsNullOrEmpty(r.Str("deleted_at"))).ToList();

        var known = await _db.SavedPaymentMethods.Where(p => p.BuyerEmail == key).ToListAsync(ct);
        var liveIds = rows.Select(r => r.Str("id")!).ToHashSet();
        var changed = false;
        foreach (var r in rows)
        {
            if (known.All(k => k.StaxPaymentMethodId != r.Str("id")))
            {
                _db.SavedPaymentMethods.Add(new SavedPaymentMethod
                {
                    BuyerEmail = key,
                    StaxPaymentMethodId = r.Str("id")!,
                    Method = r.Str("method") ?? "card",
                    Last4 = r.Str("card_last_four") ?? string.Empty,
                    Description = Describe(r)
                });
                changed = true;
            }
        }
        foreach (var k in known.Where(k => k.RemovedAt == null && !liveIds.Contains(k.StaxPaymentMethodId)))
        {
            k.RemovedAt = DateTimeOffset.UtcNow; // removed at Stax outside the shop
            changed = true;
        }
        if (changed) await _db.SaveChangesAsync(ct);

        return rows.Select(ToDto).ToList();
    }

    /// <summary>The shopper's saved, not-removed payment method, or null.</summary>
    public async Task<SavedPaymentMethod?> FindOwnedAsync(string email, string paymentMethodId, CancellationToken ct)
    {
        var key = Normalize(email);
        var row = await _db.SavedPaymentMethods.FirstOrDefaultAsync(p => p.StaxPaymentMethodId == paymentMethodId && p.BuyerEmail == key, ct);
        if (row == null)
        {
            await ListAsync(email, ct); // picks up methods saved at Stax outside this app
            row = await _db.SavedPaymentMethods.FirstOrDefaultAsync(p => p.StaxPaymentMethodId == paymentMethodId && p.BuyerEmail == key, ct);
        }
        return row is { RemovedAt: null } ? row : null;
    }

    public async Task<bool> RemoveAsync(string email, string paymentMethodId, CancellationToken ct)
    {
        var row = await FindOwnedAsync(email, paymentMethodId, ct)
                  ?? throw ApiException.NotFound("No saved payment method with that id.");
        try
        {
            // a soft-deleted payment method can no longer be edited, so blank the holder's name first
            await ScrubAsync(paymentMethodId, ct);
            await _stax.DeleteAsync($"payment-method/{paymentMethodId}", ct);
        }
        catch (StaxException ex) when (ex.StaxStatus == 404)
        {
            // already gone at Stax
        }
        row.RemovedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private async Task ScrubAsync(string paymentMethodId, CancellationToken ct)
    {
        try
        {
            await _stax.PutAsync($"payment-method/{paymentMethodId}", new { person_name = "Closed Account", nickname = "Removed payment method" }, ct);
        }
        catch (StaxException ex) when (ex.StaxStatus is 404 or 422)
        {
            // already removed or not editable; nothing to blank
        }
    }

    // ---------------------------------------------------------------- support view

    /// <summary>
    /// Every card and bank account ever saved for this email: live ones, ones the shop recorded, and
    /// ones only visible through the customer's payment history at Stax - removed ones included.
    /// </summary>
    public async Task<List<SupportPaymentMethodDto>> ListAllForSupportAsync(string email, CancellationToken ct)
    {
        var key = Normalize(email);
        var customers = await FindCustomersAsync(email, ct);
        var customerIds = customers.Select(c => c.Str("id")!).ToHashSet();
        var linked = await _db.StaxCustomerLinks.Where(l => l.Email == key).Select(l => l.StaxCustomerId).ToListAsync(ct);
        foreach (var l in linked) customerIds.Add(l);

        var byId = new Dictionary<string, JsonNode>();
        foreach (var cid in customerIds)
        {
            try
            {
                var live = await _stax.GetAsync($"customer/{cid}/payment-method", ct);
                var arr = (live as JsonArray) ?? live["data"] as JsonArray;
                if (arr != null) foreach (var r in arr.Where(x => x != null)) byId[r!.Str("id")!] = r;
            }
            catch (StaxException ex) when (ex.StaxStatus == 404) { }
        }

        var candidateIds = new HashSet<string>();
        foreach (var cid in customerIds)
        {
            var txs = await _stax.GetAllAsync("transaction?customer_id=" + cid, ct);
            foreach (var t in txs)
            {
                var pm = t.Str("payment_method_id");
                if (!string.IsNullOrEmpty(pm)) candidateIds.Add(pm);
            }
        }
        var localRows = await _db.SavedPaymentMethods.Where(p => p.BuyerEmail == key).ToListAsync(ct);
        foreach (var l in localRows) candidateIds.Add(l.StaxPaymentMethodId);

        foreach (var id in candidateIds.Where(i => !byId.ContainsKey(i)))
        {
            try
            {
                var pm = await _stax.GetAsync($"payment-method/{id}", ct);
                var owner = pm.Str("customer_id");
                if (owner != null && (customerIds.Contains(owner) || localRows.Any(l => l.StaxPaymentMethodId == id)))
                    byId[id] = pm;
            }
            catch (StaxException ex) when (ex.StaxStatus == 404) { }
        }

        return byId.Values.Select(pm =>
        {
            var dto = new SupportPaymentMethodDto();
            Fill(dto, pm);
            dto.CustomerId = pm.Str("customer_id");
            dto.RemovedAt = pm.Time("deleted_at")
                            ?? localRows.FirstOrDefault(l => l.StaxPaymentMethodId == dto.Id)?.RemovedAt;
            return dto;
        }).OrderBy(d => d.CreatedAt).ToList();
    }

    // ---------------------------------------------------------------- account closing support

    /// <summary>Erases the personal data held at Stax for this shopper: payment methods, invoice schedules and customer.</summary>
    public async Task EraseAtStaxAsync(string email, CancellationToken ct)
    {
        var methods = await ListAllForSupportAsync(email, ct);
        foreach (var m in methods)
        {
            try
            {
                if (m.RemovedAt == null)
                {
                    await ScrubAsync(m.Id, ct);
                    await _stax.DeleteAsync($"payment-method/{m.Id}", ct);
                }
            }
            catch (StaxException ex) when (ex.StaxStatus == 404) { }
        }

        var key = Normalize(email);
        var customerIds = (await FindCustomersAsync(email, ct)).Select(c => c.Str("id")!).ToHashSet();
        foreach (var l in await _db.StaxCustomerLinks.Where(l => l.Email == key).ToListAsync(ct)) customerIds.Add(l.StaxCustomerId);

        foreach (var cid in customerIds)
        {
            try
            {
                var schedules = await _stax.GetAllAsync("invoice/schedule?customer_id=" + cid, ct);
                foreach (var s in schedules.Where(s => s.Str("customer_id") == cid && string.IsNullOrEmpty(s.Str("deleted_at"))))
                {
                    await _stax.DeleteAsync($"invoice/schedule/{s.Str("id")}", ct);
                }
                await _stax.PutAsync($"customer/{cid}", new
                {
                    firstname = "Closed",
                    lastname = "Account",
                    company = "",
                    email = "",
                    phone = "",
                    address_1 = "",
                    address_2 = "",
                    address_city = "",
                    address_state = "",
                    address_zip = "",
                    address_country = "",
                    notes = "",
                    reference = ""
                }, ct);
                await _stax.DeleteAsync($"customer/{cid}", ct);
            }
            catch (StaxException ex) when (ex.StaxStatus == 404) { }
        }
    }

    // ---------------------------------------------------------------- mapping

    private static PaymentMethodDto ToDto(JsonNode pm)
    {
        var dto = new PaymentMethodDto();
        Fill(dto, pm);
        return dto;
    }

    /// <summary>Built from brand and last four only: Stax nicknames carry the holder name.</summary>
    private static string Describe(JsonNode pm)
    {
        var last4 = pm.Str("card_last_four");
        return pm.Str("method") == "card"
            ? $"{(pm.Str("card_type") ?? "card").ToUpperInvariant()} card ending in {last4}"
            : $"{(pm.Str("bank_type") ?? "bank")} account ending in {last4}";
    }

    private static void Fill(PaymentMethodDto dto, JsonNode pm)
    {
        dto.Id = pm.Str("id") ?? string.Empty;
        dto.Method = pm.Str("method") ?? string.Empty;
        dto.Last4 = pm.Str("card_last_four") ?? string.Empty;
        if (dto.Method == "card")
        {
            dto.Brand = pm.Str("card_type");
            var exp = pm.Str("card_exp");
            if (!string.IsNullOrEmpty(exp) && exp.Length >= 6) dto.Expiration = exp[..2] + "/" + exp[2..];
        }
        else
        {
            dto.BankType = pm.Str("bank_type");
        }
        dto.Description = Describe(pm);
        dto.CreatedAt = pm.Time("created_at");
    }
}
