using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Stax;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class PaymentMethodDto
{
    public string Id { get; set; } = "";
    public string Method { get; set; } = "";
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Nickname { get; set; }
    public string? CardExpiry { get; set; }
    public string? BankType { get; set; }
    public string? BankHolderType { get; set; }
    public string? PersonName { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
}

public class SavePaymentMethodRequest : BaseRequest
{
    /// <summary>"card" or "bank".</summary>
    public string? Method { get; set; }
    public string? PersonName { get; set; }
    // card
    public string? CardNumber { get; set; }
    /// <summary>MMYY or MM/YY.</summary>
    public string? CardExpiry { get; set; }
    public string? CardCvv { get; set; }
    // bank
    public string? BankAccount { get; set; }
    public string? BankRouting { get; set; }
    /// <summary>checking or savings.</summary>
    public string? BankType { get; set; }
    /// <summary>personal or business.</summary>
    public string? BankHolderType { get; set; }
}

/// <summary>Maps shop accounts to Stax customers and manages their saved payment methods.</summary>
public class PaymentMethodService
{
    private readonly CatalogContext _db;
    private readonly StaxClient _stax;

    public PaymentMethodService(CatalogContext db, StaxClient stax)
    {
        _db = db;
        _stax = stax;
    }

    public static PaymentMethodDto ToDto(JsonNode pm)
    {
        var exp = pm["card_exp"]?.ToString();
        string? expiry = null;
        if (!string.IsNullOrEmpty(exp) && exp.Length >= 6) expiry = $"{exp[..2]}/{exp[^4..]}";
        return new PaymentMethodDto
        {
            Id = pm["id"]?.ToString() ?? "",
            Method = pm["method"]?.ToString() ?? "",
            Brand = pm["card_type"]?.ToString(),
            Last4 = pm["card_last_four"]?.ToString(),
            Nickname = pm["nickname"]?.ToString(),
            CardExpiry = expiry,
            BankType = pm["bank_type"]?.ToString(),
            BankHolderType = pm["bank_holder_type"]?.ToString(),
            PersonName = pm["person_name"]?.ToString(),
            CreatedAt = StaxClient.ParseStaxTime(pm["created_at"]),
            RemovedAt = StaxClient.ParseStaxTime(pm["deleted_at"])
        };
    }

    public async Task<string?> FindCustomerIdAsync(string email, CancellationToken ct)
        => (await _db.StaxCustomerLinks.AsNoTracking().FirstOrDefaultAsync(l => l.Email == email, ct))?.StaxCustomerId;

    /// <summary>The shopper's Stax customer, created (or adopted by email) on first use.</summary>
    public async Task<string> EnsureCustomerAsync(string email, string? personName, CancellationToken ct)
    {
        var existing = await FindCustomerIdAsync(email, ct);
        if (existing is not null) return existing;

        var found = (await _stax.FindCustomersByEmailAsync(email, ct)).FirstOrDefault(c => c["deleted_at"] is null);
        string id;
        if (found is not null)
        {
            id = found["id"]!.ToString();
        }
        else
        {
            var parts = (personName ?? "").Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var first = parts.Length > 0 ? parts[0] : email.Split('@')[0];
            var last = parts.Length > 1 ? parts[1] : "Shopper";
            id = (await _stax.CreateCustomerAsync(email, first, last, null, ct))["id"]!.ToString();
        }

        _db.StaxCustomerLinks.Add(new StaxCustomerLink { Email = email, StaxCustomerId = id });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another request linked the same shopper concurrently.
            _db.ChangeTracker.Clear();
            return await FindCustomerIdAsync(email, ct) ?? id;
        }
        return id;
    }

    public async Task<PaymentMethodDto> SaveAsync(string email, SavePaymentMethodRequest r, CancellationToken ct)
    {
        var method = r.Method?.Trim().ToLowerInvariant();
        if (method is not ("card" or "bank"))
            throw new ApiRequestException(400, "method must be \"card\" or \"bank\".");
        if (string.IsNullOrWhiteSpace(r.PersonName))
            throw new ApiRequestException(400, "personName is required.");

        object body;
        if (method == "card")
        {
            if (string.IsNullOrWhiteSpace(r.CardNumber) || string.IsNullOrWhiteSpace(r.CardExpiry))
                throw new ApiRequestException(400, "cardNumber and cardExpiry are required for a card.");
            var exp = r.CardExpiry.Replace("/", "").Replace("-", "").Trim();
            if (exp.Length == 6) exp = exp[..2] + exp[^2..];
            if (exp.Length != 4 || !exp.All(char.IsDigit))
                throw new ApiRequestException(400, "cardExpiry must look like MMYY or MM/YY.");
            body = new
            {
                method = "card",
                person_name = r.PersonName.Trim(),
                card_number = r.CardNumber.Replace(" ", "").Replace("-", ""),
                card_exp = exp,
                card_cvv = r.CardCvv,
                customer_id = "" // replaced below
            };
        }
        else
        {
            if (string.IsNullOrWhiteSpace(r.BankAccount) || string.IsNullOrWhiteSpace(r.BankRouting))
                throw new ApiRequestException(400, "bankAccount and bankRouting are required for a bank account.");
            var type = (r.BankType ?? "checking").Trim().ToLowerInvariant();
            var holder = (r.BankHolderType ?? "personal").Trim().ToLowerInvariant();
            if (type is not ("checking" or "savings"))
                throw new ApiRequestException(400, "bankType must be \"checking\" or \"savings\".");
            if (holder is not ("personal" or "business"))
                throw new ApiRequestException(400, "bankHolderType must be \"personal\" or \"business\".");
            body = new
            {
                method = "bank",
                person_name = r.PersonName.Trim(),
                bank_account = r.BankAccount.Trim(),
                bank_routing = r.BankRouting.Trim(),
                bank_type = type,
                bank_holder_type = holder,
                customer_id = ""
            };
        }

        var customerId = await EnsureCustomerAsync(email, r.PersonName, ct);
        var node = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(body))!.AsObject();
        node["customer_id"] = customerId;
        var created = await _stax.CreatePaymentMethodAsync(node, ct);
        return ToDto(created);
    }

    public async Task<List<PaymentMethodDto>> ListAsync(string email, CancellationToken ct)
    {
        var customerId = await FindCustomerIdAsync(email, ct);
        if (customerId is null) return new();
        return (await _stax.ListActivePaymentMethodsAsync(customerId, ct)).Select(ToDto).ToList();
    }

    /// <summary>A usable (not removed) payment method that belongs to this shopper, or 404.</summary>
    public async Task<JsonNode> GetOwnedAsync(string email, string paymentMethodId, CancellationToken ct)
    {
        var customerId = await FindCustomerIdAsync(email, ct);
        JsonNode? pm = null;
        if (customerId is not null && !string.IsNullOrWhiteSpace(paymentMethodId))
        {
            try { pm = await _stax.GetPaymentMethodAsync(paymentMethodId, ct); }
            catch (StaxException ex) when (ex.IsRejection) { }
        }
        if (pm is null || pm["customer_id"]?.ToString() != customerId || pm["deleted_at"] is not null)
            throw new ApiRequestException(404, "Payment method not found.");
        return pm;
    }

    public async Task RemoveAsync(string email, string paymentMethodId, CancellationToken ct)
    {
        var pm = await GetOwnedAsync(email, paymentMethodId, ct);
        await _stax.DeletePaymentMethodAsync(pm["id"]!.ToString(), ct);
    }

    /// <summary>Support view: everything ever saved by the shopper at Stax, removed methods included.</summary>
    public async Task<List<PaymentMethodDto>> ListEverythingAsync(string email, CancellationToken ct)
    {
        var ids = new HashSet<string>();
        var linked = await FindCustomerIdAsync(email, ct);
        if (linked is not null) ids.Add(linked);
        foreach (var c in await _stax.FindCustomersByEmailAsync(email, ct)) ids.Add(c["id"]!.ToString());

        var all = new List<PaymentMethodDto>();
        foreach (var id in ids)
            all.AddRange((await _stax.ListAllPaymentMethodsAsync(id, ct)).Select(ToDto));
        return all.OrderByDescending(p => p.CreatedAt).ToList();
    }
}
