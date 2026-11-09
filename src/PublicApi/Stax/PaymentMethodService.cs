using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payments;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using StaxFattMerchantApi.Requests.Customer;
using StaxFattMerchantApi.Requests.PaymentMethod;
using StaxFattMerchantApi.Requests.Transaction;

namespace Microsoft.eShopWeb.PublicApi.Stax;

public class SavePaymentMethodRequest
{
    /// <summary>"card" or "bank".</summary>
    public string? Type { get; set; }
    /// <summary>Alias of <see cref="Type"/>.</summary>
    public string? Method { get; set; }
    public string? HolderName { get; set; }

    public string? CardNumber { get; set; }
    public int? ExpMonth { get; set; }
    public int? ExpYear { get; set; }
    public string? Cvv { get; set; }

    public string? AccountNumber { get; set; }
    public string? RoutingNumber { get; set; }
    /// <summary>"checking" (default) or "savings".</summary>
    public string? AccountType { get; set; }
    /// <summary>"personal" (default) or "business".</summary>
    public string? HolderType { get; set; }
    public string? BankName { get; set; }

    public string? Address1 { get; set; }
    public string? Address2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Zip { get; set; }
    public string? Country { get; set; }
}

public class PaymentMethodDto
{
    public string Id { get; set; } = "";
    /// <summary>"card" or "bank".</summary>
    public string Type { get; set; } = "";
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Nickname { get; set; }
    public string? HolderName { get; set; }
    public int? ExpMonth { get; set; }
    public int? ExpYear { get; set; }
    public string? BankName { get; set; }
    public string? AccountType { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}

public class SupportPaymentMethodDto : PaymentMethodDto
{
    public DateTimeOffset? RemovedAt { get; set; }
    public bool Removed => RemovedAt.HasValue;
}

/// <summary>Maps a shop account (its e-mail) to its Stax customer.</summary>
public class StaxCustomers
{
    private static readonly SemaphoreSlim CreateLock = new(1, 1);
    private readonly CatalogContext _db;
    private readonly StaxGateway _stax;

    public StaxCustomers(CatalogContext db, StaxGateway stax)
    {
        _db = db;
        _stax = stax;
    }

    public static string Normalize(string email) => email.Trim().ToLowerInvariant();

    /// <summary>The Stax customer of this shopper, or null when there is none (nothing is created).</summary>
    public async Task<string?> FindAsync(string email, CancellationToken ct)
    {
        email = Normalize(email);
        var profile = await _db.ShopperProfiles.FirstOrDefaultAsync(p => p.Email == email, ct);
        if (profile is not null)
        {
            return profile.StaxCustomerId;
        }

        var found = (await FindByEmailAsync(email, ct)).FirstOrDefault();
        if (found.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var id = found.Str("id")!;
        _db.ShopperProfiles.Add(new ShopperProfile(email, id));
        await TrySaveAsync(ct);
        return id;
    }

    public async Task<string> GetOrCreateAsync(string email, string? personName, CancellationToken ct)
    {
        email = Normalize(email);
        await CreateLock.WaitAsync(ct);
        try
        {
            var existing = await FindAsync(email, ct);
            if (existing is not null)
            {
                return existing;
            }

            var (first, last) = SplitName(personName, email);
            var created = await StaxCall.Json(() => _stax.Shop.Customer.PostCustomer(new PostCustomerRequest
            {
                Body = new Dictionary<string, object?> { ["email"] = email, ["firstname"] = first, ["lastname"] = last }
            }, cancellationToken: ct));
            var id = created.Str("id") ?? throw new RequestRefusedException(502, "Stax did not return the new customer's id.");
            _db.ShopperProfiles.Add(new ShopperProfile(email, id));
            await _db.SaveChangesAsync(ct);
            return id;
        }
        finally
        {
            CreateLock.Release();
        }
    }

    /// <summary>Every live Stax customer whose e-mail is exactly this one.</summary>
    public async Task<List<JsonElement>> FindByEmailAsync(string email, CancellationToken ct)
    {
        email = Normalize(email);
        var result = new List<JsonElement>();
        for (var page = 1; page <= 20; page++)
        {
            // The SDK's GetCustomers cannot filter by e-mail, so the filter is added to the request.
            var answer = await StaxCall.Json(() => _stax.Shop.Customer.GetCustomers(
                new GetCustomersRequest { Page = page, PerPage = 100 },
                StaxCall.WithQuery(("email", email)), ct));
            foreach (var c in answer.Records())
            {
                if (c.Str("deleted_at") is null &&
                    string.Equals(c.Str("email")?.Trim(), email, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(c.Clone());
                }
            }

            if (page >= answer.LastPage())
            {
                break;
            }
        }

        return result;
    }

    private async Task TrySaveAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // another request stored the same profile first
            foreach (var e in _db.ChangeTracker.Entries().ToList())
            {
                e.State = EntityState.Detached;
            }
        }
    }

    private static (string First, string Last) SplitName(string? name, string email)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return (email.Split('@')[0], string.Empty);
        }

        var i = name.IndexOf(' ');
        return i < 0 ? (name, string.Empty) : (name[..i], name[(i + 1)..].Trim());
    }
}

/// <summary>Feature 1 (saved payment methods) and feature 6 (support view).</summary>
public class PaymentMethodService
{
    private readonly CatalogContext _db;
    private readonly StaxGateway _stax;
    private readonly StaxCustomers _customers;

    public PaymentMethodService(CatalogContext db, StaxGateway stax, StaxCustomers customers)
    {
        _db = db;
        _stax = stax;
        _customers = customers;
    }

    public async Task<PaymentMethodDto> SaveAsync(string email, SavePaymentMethodRequest request, CancellationToken ct)
    {
        var type = (request.Type ?? request.Method)?.Trim().ToLowerInvariant();
        if (type is "ach" or "bank_account" or "bankaccount")
        {
            type = "bank";
        }

        if (type is not ("card" or "bank"))
        {
            throw new RequestRefusedException(400, "type must be \"card\" or \"bank\".");
        }

        if (string.IsNullOrWhiteSpace(request.HolderName))
        {
            throw new RequestRefusedException(400, "holderName is required.");
        }

        var body = new Dictionary<string, object?>
        {
            ["method"] = type,
            ["person_name"] = request.HolderName!.Trim(),
        };
        if (type == "card")
        {
            var number = new string((request.CardNumber ?? string.Empty).Where(char.IsDigit).ToArray());
            if (number.Length is < 12 or > 19)
            {
                throw new RequestRefusedException(400, "cardNumber must have 12 to 19 digits.");
            }

            if (request.ExpMonth is null or < 1 or > 12 || request.ExpYear is null)
            {
                throw new RequestRefusedException(400, "expMonth (1-12) and expYear are required for a card.");
            }

            var year = request.ExpYear.Value < 100 ? 2000 + request.ExpYear.Value : request.ExpYear.Value;
            var now = DateTime.UtcNow;
            if (year < now.Year || (year == now.Year && request.ExpMonth.Value < now.Month))
            {
                throw new RequestRefusedException(400, "The card has expired.");
            }

            body["card_number"] = number;
            body["card_exp"] = $"{request.ExpMonth.Value:00}{year % 100:00}";
            if (!string.IsNullOrWhiteSpace(request.Cvv))
            {
                body["card_cvv"] = request.Cvv!.Trim();
            }
        }
        else
        {
            var account = new string((request.AccountNumber ?? string.Empty).Where(char.IsDigit).ToArray());
            var routing = new string((request.RoutingNumber ?? string.Empty).Where(char.IsDigit).ToArray());
            if (account.Length is < 4 or > 17)
            {
                throw new RequestRefusedException(400, "accountNumber must have 4 to 17 digits.");
            }

            if (routing.Length != 9)
            {
                throw new RequestRefusedException(400, "routingNumber must have 9 digits.");
            }

            var accountType = (request.AccountType ?? "checking").Trim().ToLowerInvariant();
            var holderType = (request.HolderType ?? "personal").Trim().ToLowerInvariant();
            if (accountType is not ("checking" or "savings"))
            {
                throw new RequestRefusedException(400, "accountType must be \"checking\" or \"savings\".");
            }

            if (holderType is not ("personal" or "business"))
            {
                throw new RequestRefusedException(400, "holderType must be \"personal\" or \"business\".");
            }

            body["bank_account"] = account;
            body["bank_routing"] = routing;
            body["bank_type"] = accountType;
            body["bank_holder_type"] = holderType;
            if (!string.IsNullOrWhiteSpace(request.BankName))
            {
                body["bank_name"] = request.BankName!.Trim();
            }
        }

        void Optional(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                body[key] = value!.Trim();
            }
        }

        Optional("address_1", request.Address1);
        Optional("address_2", request.Address2);
        Optional("address_city", request.City);
        Optional("address_state", request.State);
        Optional("address_zip", request.Zip);
        Optional("address_country", request.Country);

        var customerId = await _customers.GetOrCreateAsync(email, request.HolderName, ct);
        body["customer_id"] = customerId;
        var created = await StaxCall.Json(() => _stax.Shop.PaymentMethod.PostPaymentMethod(
            new PostPaymentMethodRequest { Body = body }, cancellationToken: ct));
        var id = created.Str("id") ?? throw new RequestRefusedException(502, "Stax did not return the new payment method's id.");

        _db.SavedPaymentMethods.Add(new SavedPaymentMethod(StaxCustomers.Normalize(email), id));
        await _db.SaveChangesAsync(ct);
        return Map(created);
    }

    public async Task<List<PaymentMethodDto>> ListAsync(string email, CancellationToken ct)
    {
        var customerId = await _customers.FindAsync(email, ct);
        if (customerId is null)
        {
            return [];
        }

        var all = await StaxCall.JsonList(() => _stax.Shop.Customer.GetCustomerPaymentMethods(
            new GetCustomerPaymentMethodsRequest { Id = Guid.Parse(customerId) }, cancellationToken: ct));
        return all.Where(p => p.Str("deleted_at") is null).Select(Map).ToList();
    }

    /// <summary>The shopper's own, live payment method, or a 404 refusal.</summary>
    public async Task<JsonElement> GetOwnedAsync(string email, string paymentMethodId, CancellationToken ct)
    {
        var notFound = new RequestRefusedException(404, "Payment method not found.");
        if (!Guid.TryParse(paymentMethodId, out var guid))
        {
            throw notFound;
        }

        var customerId = await _customers.FindAsync(email, ct) ?? throw notFound;
        JsonElement pm;
        try
        {
            pm = await StaxCall.Json(() => _stax.Shop.PaymentMethod.GetPaymentMethod(
                new GetPaymentMethodRequest { Id = guid }, cancellationToken: ct));
        }
        catch (RequestRefusedException ex) when (ex.StatusCode is 404 or 422)
        {
            throw notFound;
        }

        if (!string.Equals(pm.Str("customer_id"), customerId, StringComparison.OrdinalIgnoreCase) || pm.Str("deleted_at") is not null)
        {
            throw notFound;
        }

        return pm;
    }

    public async Task RemoveAsync(string email, string paymentMethodId, CancellationToken ct)
    {
        var pm = await GetOwnedAsync(email, paymentMethodId, ct);
        var id = pm.Str("id")!;
        await StaxCall.Run(() => _stax.Shop.PaymentMethod.DeletePaymentMethod(
            new DeletePaymentMethodRequest { Id = Guid.Parse(id) }, cancellationToken: ct));

        var normalized = StaxCustomers.Normalize(email);
        var row = await _db.SavedPaymentMethods.FirstOrDefaultAsync(r => r.StaxPaymentMethodId == id && r.ShopperEmail == normalized, ct);
        if (row is not null)
        {
            row.MarkRemoved();
            await _db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Everything a shopper ever saved, removed methods included. Stax never lists removed methods, so they are
    /// found through what the shop recorded and through the shopper's past transactions, then read by id.
    /// </summary>
    public async Task<List<SupportPaymentMethodDto>> ListForSupportAsync(string email, CancellationToken ct)
    {
        var normalized = StaxCustomers.Normalize(email);
        var customerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var profile = await _db.ShopperProfiles.FirstOrDefaultAsync(p => p.Email == normalized, ct);
        if (profile is not null)
        {
            customerIds.Add(profile.StaxCustomerId);
        }

        foreach (var c in await _customers.FindByEmailAsync(normalized, ct))
        {
            customerIds.Add(c.Str("id")!);
        }

        var methodIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var known = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        var registry = await _db.SavedPaymentMethods.Where(r => r.ShopperEmail == normalized).ToListAsync(ct);
        foreach (var r in registry)
        {
            methodIds.Add(r.StaxPaymentMethodId);
        }

        if (customerIds.Count == 0 && registry.Count == 0)
        {
            throw new RequestRefusedException(404, $"No shopper with e-mail {normalized} was found.");
        }

        foreach (var customerId in customerIds)
        {
            if (!Guid.TryParse(customerId, out var customerGuid))
            {
                continue;
            }

            foreach (var pm in await StaxCall.JsonList(() => _stax.Shop.Customer.GetCustomerPaymentMethods(
                         new GetCustomerPaymentMethodsRequest { Id = customerGuid }, cancellationToken: ct)))
            {
                methodIds.Add(pm.Str("id")!);
                known[pm.Str("id")!] = pm.Clone();
            }

            // methods that were only ever used for a payment are still visible in the transaction history
            for (var page = 1; page <= 50; page++)
            {
                var transactions = await StaxCall.Json(() => _stax.Shop.Transaction.GetTransactions(
                    new GetTransactionsRequest { Page = page, PerPage = 100 },
                    StaxCall.WithQuery(("customer_id", customerId)), ct));
                foreach (var t in transactions.Records())
                {
                    if (t.Str("payment_method_id") is { Length: > 0 } pmId &&
                        string.Equals(t.Str("customer_id"), customerId, StringComparison.OrdinalIgnoreCase))
                    {
                        methodIds.Add(pmId);
                    }
                }

                if (page >= transactions.LastPage())
                {
                    break;
                }
            }
        }

        var result = new List<SupportPaymentMethodDto>();
        foreach (var id in methodIds)
        {
            if (!known.TryGetValue(id, out var pm))
            {
                if (!Guid.TryParse(id, out var guid))
                {
                    continue;
                }

                try
                {
                    pm = await StaxCall.Json(() => _stax.Shop.PaymentMethod.GetPaymentMethod(
                        new GetPaymentMethodRequest { Id = guid }, cancellationToken: ct));
                }
                catch (RequestRefusedException ex) when (ex.StatusCode is 404 or 422)
                {
                    continue;
                }
            }

            // never show a method of somebody else's account
            if (customerIds.Count > 0 && pm.Str("customer_id") is { } owner && !customerIds.Contains(owner))
            {
                continue;
            }

            var dto = new SupportPaymentMethodDto { RemovedAt = pm.Date("deleted_at") };
            Fill(dto, pm);
            result.Add(dto);
        }

        return result.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).ToList();
    }

    public static PaymentMethodDto Map(JsonElement pm)
    {
        var dto = new PaymentMethodDto();
        Fill(dto, pm);
        return dto;
    }

    private static void Fill(PaymentMethodDto dto, JsonElement pm)
    {
        var isBank = pm.Str("method") == "bank";
        dto.Id = pm.Str("id") ?? "";
        dto.Type = isBank ? "bank" : "card";
        dto.Last4 = pm.Str("card_last_four");
        dto.Nickname = pm.Str("nickname");
        dto.HolderName = pm.Str("person_name");
        dto.CreatedAt = pm.Date("created_at");
        if (isBank)
        {
            dto.BankName = pm.Str("bank_name");
            dto.AccountType = pm.Str("bank_type");
        }
        else
        {
            dto.Brand = pm.Str("card_type");
            var exp = pm.Str("card_exp");
            if (exp is { Length: 4 or 6 } && int.TryParse(exp[..2], out var month) && int.TryParse(exp[2..], out var year))
            {
                dto.ExpMonth = month;
                dto.ExpYear = exp.Length == 4 ? 2000 + year : year;
            }
        }
    }
}
