using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Stax;

namespace Microsoft.eShopWeb.PublicApi.Stax;

public record SavePaymentMethodRequest(
    string? Type,
    string? PersonName,
    string? CardNumber,
    string? Cvv,
    string? Expiry,
    string? AccountNumber,
    string? RoutingNumber,
    string? BankName,
    string? AccountType,
    string? HolderType,
    string? Zip);

public record PaymentMethodDto(
    string Id,
    string Type,
    string? Brand,
    string? Last4,
    string? Expiry,
    string? BankName,
    string? AccountType,
    string? HolderType,
    DateTime? CreatedAt,
    DateTime? RemovedAt);

/// <summary>Everything that talks to Stax about customers and their stored payment methods.</summary>
public class PaymentMethodService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    private readonly StaxClient _stax;
    private readonly StaxContext _db;

    public PaymentMethodService(StaxClient stax, StaxContext db)
    {
        _stax = stax;
        _db = db;
    }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    // ---- customers -----------------------------------------------------------
    private async Task<List<JsonNode>> FindStaxCustomersAsync(string email, CancellationToken ct)
    {
        var found = await _stax.ListAllAsync("/customer?email=" + Uri.EscapeDataString(email), ct);
        return found.Where(c => string.Equals(StaxClient.Str(c, "email"), email, StringComparison.OrdinalIgnoreCase)
                                && StaxClient.Str(c, "deleted_at") == null).ToList();
    }

    /// <summary>All Stax customer ids known for this shopper (locally linked and found by email at Stax).</summary>
    public async Task<List<string>> GetCustomerIdsAsync(string email, CancellationToken ct = default)
    {
        email = NormalizeEmail(email);
        var ids = await _db.CustomerLinks.Where(l => l.Email == email).Select(l => l.StaxCustomerId).ToListAsync(ct);
        foreach (var c in await FindStaxCustomersAsync(email, ct))
        {
            var id = StaxClient.Str(c, "id");
            if (id != null && !ids.Contains(id)) ids.Add(id);
        }
        return ids;
    }

    private async Task<string> EnsureCustomerAsync(string email, string personName, CancellationToken ct)
    {
        email = NormalizeEmail(email);
        var gate = Locks.GetOrAdd(email, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var link = await _db.CustomerLinks.FirstOrDefaultAsync(l => l.Email == email, ct);
            if (link != null)
            {
                try
                {
                    var existing = await _stax.GetAsync("/customer/" + link.StaxCustomerId, ct);
                    if (StaxClient.Str(existing, "deleted_at") == null) return link.StaxCustomerId;
                }
                catch (StaxException ex) when (ex.StaxStatus is 404 or 422) { }
                _db.CustomerLinks.Remove(link);
                await _db.SaveChangesAsync(ct);
            }

            var customers = await FindStaxCustomersAsync(email, ct);
            string customerId;
            if (customers.Count > 0)
            {
                customerId = StaxClient.Str(customers[0], "id")!;
            }
            else
            {
                var parts = personName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                var created = await _stax.PostAsync("/customer", new
                {
                    firstname = parts.Length > 0 ? parts[0] : email,
                    lastname = parts.Length > 1 ? parts[1] : "",
                    email,
                }, ct);
                customerId = StaxClient.Str(created, "id") ?? throw new StaxException(502, "Stax did not return a customer id.");
            }
            _db.CustomerLinks.Add(new StaxCustomerLink { Email = email, StaxCustomerId = customerId });
            await _db.SaveChangesAsync(ct);
            return customerId;
        }
        finally
        {
            gate.Release();
        }
    }

    // ---- payment methods -----------------------------------------------------
    public static PaymentMethodDto ToDto(JsonNode pm)
    {
        var type = StaxClient.Str(pm, "method") ?? "card";
        return new PaymentMethodDto(
            StaxClient.Str(pm, "id")!,
            type,
            type == "card" ? StaxClient.Str(pm, "card_type") : null,
            type == "card" ? StaxClient.Str(pm, "card_last_four") : Last4OfBank(pm),
            type == "card" ? StaxClient.Str(pm, "card_exp") : null,
            type == "bank" ? StaxClient.Str(pm, "bank_name") : null,
            type == "bank" ? StaxClient.Str(pm, "bank_type") : null,
            type == "bank" ? StaxClient.Str(pm, "bank_holder_type") : null,
            ParseDate(StaxClient.Str(pm, "created_at")),
            ParseDate(StaxClient.Str(pm, "deleted_at")));
    }

    private static string? Last4OfBank(JsonNode pm)
    {
        var display = pm["meta"]?["accountDisplay"]?.ToString();
        return display is { Length: >= 4 } ? display[^4..] : null;
    }

    public static DateTime? ParseDate(string? s) =>
        DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var d) ? d : null;

    public async Task<PaymentMethodDto> SaveAsync(string email, SavePaymentMethodRequest r, CancellationToken ct = default)
    {
        var type = r.Type?.Trim().ToLowerInvariant();
        if (type is not ("card" or "bank")) throw ApiException.BadRequest("type must be 'card' or 'bank'.");
        if (string.IsNullOrWhiteSpace(r.PersonName)) throw ApiException.BadRequest("personName is required.");

        object body;
        if (type == "card")
        {
            var number = new string((r.CardNumber ?? "").Where(char.IsDigit).ToArray());
            var expiry = new string((r.Expiry ?? "").Where(char.IsDigit).ToArray());
            if (number.Length is < 12 or > 19) throw ApiException.BadRequest("cardNumber must have 12 to 19 digits.");
            if (string.IsNullOrWhiteSpace(r.Cvv) || r.Cvv.Length is < 3 or > 4 || !r.Cvv.All(char.IsDigit)) throw ApiException.BadRequest("cvv must have 3 or 4 digits.");
            if (expiry.Length != 4 || int.Parse(expiry[..2]) is < 1 or > 12) throw ApiException.BadRequest("expiry must be MMYY.");
            body = new Dictionary<string, object?>
            {
                ["method"] = "card",
                ["person_name"] = r.PersonName.Trim(),
                ["card_number"] = number,
                ["card_cvv"] = r.Cvv,
                ["card_exp"] = expiry,
                ["address_zip"] = r.Zip,
            };
        }
        else
        {
            var accountType = r.AccountType?.Trim().ToLowerInvariant();
            var holderType = r.HolderType?.Trim().ToLowerInvariant() ?? "personal";
            if (string.IsNullOrWhiteSpace(r.AccountNumber) || !r.AccountNumber.All(char.IsDigit)) throw ApiException.BadRequest("accountNumber is required and must be digits.");
            if (string.IsNullOrWhiteSpace(r.RoutingNumber) || r.RoutingNumber.Length != 9 || !r.RoutingNumber.All(char.IsDigit)) throw ApiException.BadRequest("routingNumber must be 9 digits.");
            if (accountType is not ("checking" or "savings")) throw ApiException.BadRequest("accountType must be 'checking' or 'savings'.");
            if (holderType is not ("personal" or "business")) throw ApiException.BadRequest("holderType must be 'personal' or 'business'.");
            body = new Dictionary<string, object?>
            {
                ["method"] = "bank",
                ["person_name"] = r.PersonName.Trim(),
                ["bank_account"] = r.AccountNumber,
                ["bank_routing"] = r.RoutingNumber,
                ["bank_name"] = string.IsNullOrWhiteSpace(r.BankName) ? "Bank" : r.BankName.Trim(),
                ["bank_type"] = accountType,
                ["bank_holder_type"] = holderType,
                ["address_zip"] = r.Zip,
            };
        }

        var customerId = await EnsureCustomerAsync(email, r.PersonName, ct);
        var fields = (Dictionary<string, object?>)body;
        fields["customer_id"] = customerId;
        foreach (var k in fields.Where(f => f.Value is null).Select(f => f.Key).ToList()) fields.Remove(k);
        var created = await _stax.PostAsync("/payment-method", fields, ct);
        return ToDto(created);
    }

    public async Task<List<PaymentMethodDto>> ListAsync(string email, CancellationToken ct = default)
    {
        var result = new List<PaymentMethodDto>();
        foreach (var id in await GetCustomerIdsAsync(email, ct))
        {
            foreach (var pm in await _stax.ListAllAsync($"/customer/{id}/payment-method", ct))
            {
                if (StaxClient.Str(pm, "deleted_at") == null) result.Add(ToDto(pm));
            }
        }
        return result.OrderByDescending(p => p.CreatedAt).ToList();
    }

    /// <summary>Loads a live (not removed) payment method and checks it belongs to the shopper.</summary>
    public async Task<JsonNode> GetOwnedAsync(string email, string paymentMethodId, CancellationToken ct = default)
    {
        const string notFound = "Payment method not found.";
        if (string.IsNullOrWhiteSpace(paymentMethodId)) throw ApiException.BadRequest("paymentMethodId is required.");
        JsonNode pm;
        try
        {
            pm = await _stax.GetAsync("/payment-method/" + Uri.EscapeDataString(paymentMethodId), ct);
        }
        catch (StaxException ex) when (ex.StaxStatus is 400 or 404 or 422)
        {
            throw ApiException.NotFound(notFound);
        }
        var owner = StaxClient.Str(pm, "customer_id");
        if (StaxClient.Str(pm, "deleted_at") != null || owner == null
            || !(await GetCustomerIdsAsync(email, ct)).Contains(owner))
        {
            throw ApiException.NotFound(notFound);
        }
        return pm;
    }

    public async Task RemoveAsync(string email, string paymentMethodId, CancellationToken ct = default)
    {
        var pm = await GetOwnedAsync(email, paymentMethodId, ct);
        await _stax.DeleteAsync("/payment-method/" + StaxClient.Str(pm, "id"), ct);
    }

    // ---- support view --------------------------------------------------------
    public async Task<List<PaymentMethodDto>> ListForSupportAsync(string email, CancellationToken ct = default)
    {
        var ids = await GetCustomerIdsAsync(email, ct);
        var byId = new Dictionary<string, PaymentMethodDto>();
        if (ids.Count == 0) return new List<PaymentMethodDto>();

        foreach (var id in ids)
        {
            foreach (var pm in await _stax.ListAllAsync($"/customer/{id}/payment-method", ct))
            {
                var dto = ToDto(pm);
                byId[dto.Id] = dto;
            }
        }
        // Removed methods are not part of the per-customer list; Stax only exposes them via the merchant-wide list.
        foreach (var pm in await _stax.ListAllAsync("/payment-method?status=deleted", ct))
        {
            if (StaxClient.Str(pm, "customer_id") is { } cid && ids.Contains(cid))
            {
                var dto = ToDto(pm);
                byId[dto.Id] = dto;
            }
        }
        return byId.Values.OrderBy(p => p.CreatedAt).ToList();
    }

    // ---- account closure -----------------------------------------------------
    /// <summary>Erases the shopper's payment methods and customer records at Stax.</summary>
    public async Task EraseAtStaxAsync(string email, CancellationToken ct = default)
    {
        email = NormalizeEmail(email);
        foreach (var customerId in await GetCustomerIdsAsync(email, ct))
        {
            var methods = new Dictionary<string, JsonNode>();
            foreach (var pm in await _stax.ListAllAsync($"/customer/{customerId}/payment-method", ct))
                methods[StaxClient.Str(pm, "id")!] = pm;
            foreach (var pm in await _stax.ListAllAsync("/payment-method?status=deleted", ct))
                if (StaxClient.Str(pm, "customer_id") == customerId) methods[StaxClient.Str(pm, "id")!] = pm;

            foreach (var (id, pm) in methods)
            {
                // Stax only soft-deletes: blank the personal details first, then remove the method.
                try
                {
                    await _stax.PutAsync("/payment-method/" + id, new
                    {
                        person_name = "Closed Account",
                        address_1 = "", address_2 = "", address_city = "", address_state = "", address_zip = "", address_country = "",
                    }, ct);
                }
                catch (StaxException ex) when (ex.StaxStatus is >= 400 and < 500) { }
                if (StaxClient.Str(pm, "deleted_at") == null)
                {
                    try { await _stax.DeleteAsync("/payment-method/" + id, ct); }
                    catch (StaxException ex) when (ex.StaxStatus is 404 or 422) { }
                }
            }

            try
            {
                await _stax.PutAsync("/customer/" + customerId, new
                {
                    firstname = "Closed",
                    lastname = "Account",
                    email = $"closed-{customerId[..8]}@example.invalid",
                    company = "", phone = "", address_1 = "", address_2 = "", address_city = "", address_state = "",
                    address_zip = "", address_country = "", notes = "", reference = "",
                    cc_emails = Array.Empty<string>(), cc_sms = "",
                }, ct);
                await _stax.DeleteAsync("/customer/" + customerId, ct);
            }
            catch (StaxException ex) when (ex.StaxStatus is 404 or 422 && ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)) { }
        }
    }

    public async Task ForgetLocalAsync(string email, CancellationToken ct = default)
    {
        email = NormalizeEmail(email);
        _db.CustomerLinks.RemoveRange(_db.CustomerLinks.Where(l => l.Email == email));
        await _db.SaveChangesAsync(ct);
    }
}
