using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public record PlanDto(long Id, string Handle, string Name, string? Brand, decimal Price, int Interval, string IntervalUnit);

public record SubscriptionDto(long Id, string State, long PlanId, string PlanHandle, string PlanName, bool Paid,
    decimal Balance, string? NextBillingDate, string? CreatedAt, string? CanceledAt, string? PausedAt);

public record UsageComponentDto(long ComponentId, string Name, string? Handle, string? UnitName, decimal Quantity);

public record UsageDto(long SubscriptionId, string Date, decimal TotalQuantity, List<UsageComponentDto> Components);

public record CurrencyPriceDto(long PlanId, string Currency, decimal Price);

/// <summary>
/// Billing operations. Maxio is the system of record: nothing is stored locally.
/// A shopper maps to a Maxio customer whose reference is the shopper's user name.
/// </summary>
public class BillingService
{
    private const string BRAND_PREFIX = "Brand: ";
    private const int PAGE_SIZE = 200;
    private readonly MaxioClient _maxio;
    private readonly IReadRepository<CatalogBrand> _brands;

    public BillingService(MaxioClient maxio, IReadRepository<CatalogBrand> brands)
    {
        _maxio = maxio;
        _brands = brands;
    }

    // ---------- plans ----------

    public async Task<List<PlanDto>> ListPlansAsync()
    {
        var plans = new List<PlanDto>();
        for (var page = 1; ; page++)
        {
            var items = await _maxio.GetAsync($"/products.json?per_page={PAGE_SIZE}&page={page}") as JsonArray ?? new JsonArray();
            foreach (var item in items)
            {
                var p = item?["product"];
                if (p is not null && IsSubscribable(p)) plans.Add(ToPlan(p));
            }
            if (items.Count < PAGE_SIZE) break;
        }
        return plans;
    }

    /// <summary>Plans a shopper may subscribe to: not archived and not requiring card details.</summary>
    private static bool IsSubscribable(JsonNode product) =>
        product["archived_at"] is null && !(product["require_credit_card"]?.GetValue<bool>() ?? false);

    private static PlanDto ToPlan(JsonNode p)
    {
        var description = (string?)p["description"];
        string? brand = description is not null && description.StartsWith(BRAND_PREFIX, StringComparison.Ordinal)
            ? description[BRAND_PREFIX.Length..].Trim() : null;
        return new PlanDto((long)p["id"]!, (string)p["handle"]!, (string)p["name"]!, brand,
            (long)p["price_in_cents"]! / 100m, (int)p["interval"]!, (string)p["interval_unit"]!);
    }

    private async Task<JsonNode> GetSubscribablePlanAsync(string? handle)
    {
        if (string.IsNullOrWhiteSpace(handle))
            throw new ApiException(400, "A plan handle is required.");
        var node = await _maxio.GetOrNullAsync($"/products/handle/{Uri.EscapeDataString(handle.Trim())}.json");
        var product = node?["product"];
        if (product is null || !IsSubscribable(product))
            throw new ApiException(404, $"Plan '{handle}' was not found or cannot be subscribed to.");
        return product;
    }

    public async Task<PlanDto> CreatePlanAsync(string? name, decimal price, string? brand)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ApiException(400, "A plan name is required.");
        if (price < 0 || decimal.Round(price, 2) != price)
            throw new ApiException(400, "The monthly price must not be negative and may have at most two decimal places.");
        if (string.IsNullOrWhiteSpace(brand)) throw new ApiException(400, "A brand is required.");

        var catalogBrand = (await _brands.ListAsync())
            .FirstOrDefault(b => string.Equals(b.Brand, brand.Trim(), StringComparison.OrdinalIgnoreCase));
        if (catalogBrand is null)
            throw new ApiException(422, $"Brand '{brand}' is not one of the brands in the catalog.");

        var handle = Slugify(name);
        if (await _maxio.GetOrNullAsync($"/products/handle/{handle}.json") is not null)
            throw new DuplicateException($"A plan with handle {handle} already exists");

        var familyId = await GetProductFamilyIdAsync();
        var body = new JsonObject
        {
            ["product"] = new JsonObject
            {
                ["name"] = name.Trim(),
                ["handle"] = handle,
                ["description"] = BRAND_PREFIX + catalogBrand.Brand,
                ["price_in_cents"] = (long)decimal.Round(price * 100m),
                ["interval"] = 1,
                ["interval_unit"] = "month",
                ["require_credit_card"] = false,
                ["request_credit_card"] = false
            }
        };
        var created = await _maxio.PostAsync($"/product_families/{familyId}/products.json", body);
        return ToPlan(created!["product"]!);
    }

    private async Task<long> GetProductFamilyIdAsync()
    {
        var families = await _maxio.GetAsync("/product_families.json") as JsonArray;
        var family = families?.Select(f => f?["product_family"]).FirstOrDefault(f => f is not null && f["archived_at"] is null);
        if (family is null) throw new ApiException(409, "There is no product family to add plans to.");
        return (long)family["id"]!;
    }

    public async Task<CurrencyPriceDto> AddCurrencyPriceAsync(long planId, string? currency, decimal price)
    {
        if (string.IsNullOrWhiteSpace(currency) || !Regex.IsMatch(currency.Trim(), "^[A-Za-z]{3}$"))
            throw new ApiException(400, "The currency must be a three-letter ISO code.");
        if (price <= 0) throw new ApiException(400, "The price must be greater than zero.");

        var node = await _maxio.GetOrNullAsync($"/products/{planId}.json");
        var product = node?["product"];
        if (product is null) throw new ApiException(404, $"Plan {planId} was not found.");
        var pricePointId = (long?)product["product_price_point_id"] ?? (long?)product["default_product_price_point_id"];
        if (pricePointId is null) throw new ApiException(409, "The plan has no price point to price.");

        var code = currency.Trim().ToUpperInvariant();
        var body = new JsonObject
        {
            ["currency_prices"] = new JsonArray(new JsonObject { ["currency"] = code, ["price"] = price })
        };
        await _maxio.PostAsync($"/product_price_points/{pricePointId}/currency_prices.json", body);
        return new CurrencyPriceDto(planId, code, price);
    }

    private static string Slugify(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            if (ch is (>= 'a' and <= 'z') or (>= '0' and <= '9')) sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "plan-" + Guid.NewGuid().ToString("N")[..12] : slug;
    }

    // ---------- subscriptions ----------

    public async Task<SubscriptionDto> SubscribeAsync(string user, string? planHandle)
    {
        var plan = await GetSubscribablePlanAsync(planHandle);
        var customerId = await GetOrCreateCustomerAsync(user);
        // No card details are collected, so bill by invoice (remittance).
        var body = new JsonObject
        {
            ["subscription"] = new JsonObject
            {
                ["product_handle"] = (string)plan["handle"]!,
                ["customer_id"] = customerId,
                ["payment_collection_method"] = "remittance"
            }
        };
        var created = await _maxio.PostAsync("/subscriptions.json", body);
        return ToSubscription(created!["subscription"]!);
    }

    private static string Reference(string user) => user.Trim().ToLowerInvariant();

    private async Task<long> GetOrCreateCustomerAsync(string user)
    {
        var reference = Reference(user);
        var found = await _maxio.GetOrNullAsync($"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}");
        if (found?["customer"]?["id"] is { } id) return (long)id;

        var local = reference.Contains('@') ? reference[..reference.IndexOf('@')] : reference;
        var body = new JsonObject
        {
            ["customer"] = new JsonObject
            {
                ["first_name"] = local,
                ["last_name"] = "(eShopOnWeb)",
                ["email"] = reference.Contains('@') ? reference : $"{local}@users.invalid",
                ["reference"] = reference
            }
        };
        var created = await _maxio.PostAsync("/customers.json", body);
        return (long)created!["customer"]!["id"]!;
    }

    /// <summary>Loads a subscription that belongs to <paramref name="user"/>; anything else is a 404.</summary>
    private async Task<JsonNode> GetOwnedAsync(string user, long id)
    {
        var node = await _maxio.GetOrNullAsync($"/subscriptions/{id}.json");
        var sub = node?["subscription"];
        var owner = (string?)sub?["customer"]?["reference"];
        if (sub is null || owner is null || !string.Equals(owner, Reference(user), StringComparison.OrdinalIgnoreCase))
            throw new ApiException(404, $"Subscription {id} was not found.");
        return sub;
    }

    private async Task<JsonNode> GetAnyAsync(long id)
    {
        var node = await _maxio.GetOrNullAsync($"/subscriptions/{id}.json");
        return node?["subscription"] ?? throw new ApiException(404, $"Subscription {id} was not found.");
    }

    private static SubscriptionDto ToSubscription(JsonNode s)
    {
        var balanceCents = (long?)s["balance_in_cents"] ?? 0;
        var product = s["product"]!;
        return new SubscriptionDto((long)s["id"]!, (string)s["state"]!, (long)product["id"]!, (string)product["handle"]!,
            (string)product["name"]!, balanceCents <= 0, balanceCents / 100m,
            (string?)s["next_assessment_at"], (string?)s["created_at"], (string?)s["canceled_at"], (string?)s["on_hold_at"]);
    }

    private static string State(JsonNode s) => (string)s["state"]!;

    public async Task<SubscriptionDto> GetSubscriptionAsync(string user, long id) =>
        ToSubscription(await GetOwnedAsync(user, id));

    public async Task<SubscriptionDto> PauseAsync(string user, long id)
    {
        var sub = await GetOwnedAsync(user, id);
        var state = State(sub);
        if (state == "on_hold") throw new ApiException(409, "The subscription is already paused.");
        if (state is not ("active" or "trialing" or "past_due" or "soft_failure" or "unpaid"))
            throw new ApiException(409, $"A subscription in state '{state}' cannot be paused.");
        return await ReloadAfter(id, await _maxio.PostAsync($"/subscriptions/{id}/hold.json", new JsonObject()));
    }

    public async Task<SubscriptionDto> ResumeAsync(string user, long id)
    {
        var sub = await GetOwnedAsync(user, id);
        if (State(sub) != "on_hold") throw new ApiException(409, "Only a paused subscription can be resumed.");
        return await ReloadAfter(id, await _maxio.PostAsync($"/subscriptions/{id}/resume.json", new JsonObject()));
    }

    public async Task<SubscriptionDto> CancelAsync(string user, long id)
    {
        var sub = await GetOwnedAsync(user, id);
        var state = State(sub);
        if (state == "canceled") throw new ApiException(409, "The subscription is already cancelled.");
        if (state is "expired" or "archived") throw new ApiException(409, $"A subscription in state '{state}' cannot be cancelled.");
        return await ReloadAfter(id, await _maxio.DeleteAsync($"/subscriptions/{id}.json",
            new JsonObject { ["cancellation_message"] = "Cancelled by subscriber" }));
    }

    public async Task<SubscriptionDto> RestartAsync(string user, long id)
    {
        var sub = await GetOwnedAsync(user, id);
        if (State(sub) != "canceled") throw new ApiException(409, "Only a cancelled subscription can be restarted.");
        return await ReloadAfter(id, await _maxio.PutAsync($"/subscriptions/{id}/reactivate.json", new JsonObject()));
    }

    public async Task<SubscriptionDto> SwitchPlanAsync(string user, long id, string? planHandle)
    {
        var sub = await GetOwnedAsync(user, id);
        var plan = await GetSubscribablePlanAsync(planHandle);
        var newHandle = (string)plan["handle"]!;
        if (string.Equals((string)sub["product"]!["handle"]!, newHandle, StringComparison.Ordinal))
            throw new ApiException(409, "The subscription is already on that plan.");

        var state = State(sub);
        if (state is not ("active" or "trialing" or "past_due" or "soft_failure" or "unpaid" or "on_hold" or "canceled"))
            throw new ApiException(409, $"A subscription in state '{state}' cannot switch plans.");

        // Maxio cannot migrate a cancelled subscription, so it is reactivated first and
        // cancelled again if the migration fails, leaving it as it was.
        var reactivated = false;
        if (state == "canceled")
        {
            await _maxio.PutAsync($"/subscriptions/{id}/reactivate.json", new JsonObject());
            reactivated = true;
        }

        try
        {
            var body = new JsonObject { ["migration"] = new JsonObject { ["product_handle"] = newHandle } };
            var migrated = await _maxio.PostAsync($"/subscriptions/{id}/migrations.json", body);
            return await ReloadAfter(id, migrated);
        }
        catch when (reactivated)
        {
            try
            {
                await _maxio.DeleteAsync($"/subscriptions/{id}.json",
                    new JsonObject { ["cancellation_message"] = "Plan switch failed; restoring cancelled state" });
            }
            catch (ApiException)
            {
            }
            throw;
        }
    }

    public async Task<SubscriptionDto> SetNextBillingDateAsync(long id, DateOnly date)
    {
        var sub = await GetAnyAsync(id);
        var state = State(sub);
        if (state is "canceled" or "expired" or "archived")
            throw new ApiException(409, $"A subscription in state '{state}' is not billed.");
        // A date-only value is taken by Maxio as midday in the site's time zone.
        var updated = await _maxio.PutAsync($"/subscriptions/{id}.json", new JsonObject
        {
            ["subscription"] = new JsonObject { ["next_billing_at"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }
        });
        return await ReloadAfter(id, updated);
    }

    private async Task<SubscriptionDto> ReloadAfter(long id, JsonNode? response)
    {
        var sub = response?["subscription"];
        if (sub?["product"] is null) sub = await GetAnyAsync(id);
        return ToSubscription(sub);
    }

    // ---------- usage ----------

    public async Task<UsageDto> GetUsageAsync(string user, long id, DateOnly date)
    {
        await GetOwnedAsync(user, id);

        var components = new List<JsonNode>();
        for (var page = 1; ; page++)
        {
            var node = await _maxio.GetAsync($"/subscriptions/{id}/components.json?per_page={PAGE_SIZE}&page={page}") as JsonArray ?? new JsonArray();
            foreach (var n in node)
            {
                var c = n?["component"];
                if (c is not null && (string?)c["kind"] == "metered_component") components.Add(c);
            }
            if (node.Count < PAGE_SIZE) break;
        }

        // Maxio's date filters are day-granular in the site's time zone, so ask for a
        // wider window and keep only usages recorded on the requested calendar day.
        var since = date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var until = date.AddDays(2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var result = new List<UsageComponentDto>();
        foreach (var c in components)
        {
            var componentId = (long)c["component_id"]!;
            decimal total = 0;
            for (var page = 1; ; page++)
            {
                var node = await _maxio.GetAsync(
                    $"/subscriptions/{id}/components/{componentId}/usages.json?since_date={since}&until_date={until}&per_page={PAGE_SIZE}&page={page}") as JsonArray ?? new JsonArray();
                foreach (var item in node)
                {
                    var usage = item?["usage"];
                    var created = (string?)usage?["created_at"];
                    if (created is null || !DateTimeOffset.TryParse(created, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)) continue;
                    if (DateOnly.FromDateTime(at.DateTime) == date) total += (decimal)usage!["quantity"]!;
                }
                if (node.Count < PAGE_SIZE) break;
            }
            result.Add(new UsageComponentDto(componentId, (string)c["name"]!, (string?)c["component_handle"], (string?)c["unit_name"], total));
        }
        return new UsageDto(id, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), result.Sum(r => r.Quantity), result);
    }
}
