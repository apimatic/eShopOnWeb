using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public record PlanDto(long Id, string Handle, string Name, string? Brand, decimal MonthlyPrice);

public record SubscriptionDto(
    long Id,
    string State,
    string? PlanHandle,
    string? PlanName,
    bool Paid,
    decimal Balance,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? CanceledAt);

public record UsageComponentDto(long ComponentId, string Name, string? UnitName, decimal Quantity);

public record UsageDayDto(long SubscriptionId, string Date, decimal TotalQuantity, List<UsageComponentDto> Components);

public record CurrencyPriceDto(long PlanId, string Currency, decimal Price);

/// <summary>
/// Subscription billing rules on top of Maxio. Maxio is the system of record: nothing is stored locally.
/// </summary>
public class BillingService
{
    private const string BRAND_FAMILY_SUFFIX = " plans";
    private static readonly Regex CurrencyRegex = new("^[A-Za-z]{3}$", RegexOptions.Compiled);

    private readonly MaxioClient _maxio;
    private readonly IReadRepository<CatalogBrand> _brands;

    public BillingService(MaxioClient maxio, IReadRepository<CatalogBrand> brands)
    {
        _maxio = maxio;
        _brands = brands;
    }

    // ---------- identity ----------

    public static string RequireShopper(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new MaxioException(401, "The token does not identify a user.");
        }
        return name;
    }

    // ---------- 1. plans ----------

    public async Task<List<PlanDto>> ListPlansAsync(CancellationToken ct)
    {
        var products = await GetAllAsync("products.json", "product", ct);
        return products
            .Where(p => p["archived_at"] == null && !string.IsNullOrEmpty(p["handle"]?.GetValue<string>()))
            .Select(ToPlan)
            .ToList();
    }

    private static PlanDto ToPlan(JsonNode p) => new(
        p["id"]!.GetValue<long>(),
        p["handle"]!.GetValue<string>(),
        p["name"]?.GetValue<string>() ?? "",
        BrandFromFamily(p["product_family"]?["name"]?.GetValue<string>()),
        (p["price_in_cents"]?.GetValue<long>() ?? 0) / 100m);

    private static string? BrandFromFamily(string? family)
    {
        if (family != null && family.EndsWith(BRAND_FAMILY_SUFFIX, StringComparison.OrdinalIgnoreCase))
        {
            return family[..^BRAND_FAMILY_SUFFIX.Length];
        }
        return family;
    }

    // ---------- 2. subscribe ----------

    public async Task<SubscriptionDto> SubscribeAsync(string shopper, string? planHandle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new MaxioException(400, "A plan handle is required.");
        }
        await GetSubscribablePlanAsync(planHandle, ct);

        var subscription = new JsonObject
        {
            ["product_handle"] = planHandle.Trim(),
            // No card details are collected; the subscriber is invoiced instead.
            ["payment_collection_method"] = "remittance"
        };

        var customerId = await FindCustomerIdAsync(shopper, ct);
        if (customerId != null)
        {
            subscription["customer_id"] = customerId;
        }
        else
        {
            var email = shopper.Contains('@') ? shopper : $"{shopper}@eshop.invalid";
            var first = shopper.Contains('@') ? shopper[..shopper.IndexOf('@')] : shopper;
            subscription["customer_attributes"] = new JsonObject
            {
                ["reference"] = shopper,
                ["first_name"] = first,
                ["last_name"] = "Shopper",
                ["email"] = email
            };
        }

        var created = await _maxio.PostAsync("subscriptions.json", new JsonObject { ["subscription"] = subscription }, ct);
        return await ToSubscriptionDtoAsync(created!["subscription"]!, ct);
    }

    private async Task<JsonNode> GetSubscribablePlanAsync(string handle, CancellationToken ct)
    {
        JsonNode? plan;
        try
        {
            plan = (await _maxio.GetAsync($"products/handle/{Uri.EscapeDataString(handle.Trim())}.json", ct))?["product"];
        }
        catch (MaxioException ex) when (ex.StatusCode == 404)
        {
            throw new MaxioException(404, $"There is no plan with handle '{handle}'.");
        }
        if (plan == null)
        {
            throw new MaxioException(404, $"There is no plan with handle '{handle}'.");
        }
        if (plan["archived_at"] != null)
        {
            throw new MaxioException(422, $"The plan '{handle}' is no longer available.");
        }
        return plan;
    }

    private async Task<long?> FindCustomerIdAsync(string reference, CancellationToken ct)
    {
        try
        {
            var found = await _maxio.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", ct);
            return found?["customer"]?["id"]?.GetValue<long>();
        }
        catch (MaxioException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    // ---------- 3. subscription page ----------

    public async Task<SubscriptionDto> GetSubscriptionAsync(string shopper, long id, CancellationToken ct)
    {
        var sub = await GetOwnedAsync(shopper, id, ct);
        return await ToSubscriptionDtoAsync(sub, ct);
    }

    private async Task<JsonNode> GetOwnedAsync(string shopper, long id, CancellationToken ct)
    {
        var sub = await GetAnyAsync(id, ct);
        // Someone else's subscription is reported exactly like a missing one.
        if (!string.Equals(sub["customer"]?["reference"]?.GetValue<string>(), shopper, StringComparison.Ordinal))
        {
            throw new MaxioException(404, $"There is no subscription {id}.");
        }
        return sub;
    }

    private async Task<JsonNode> GetAnyAsync(long id, CancellationToken ct)
    {
        try
        {
            var node = await _maxio.GetAsync($"subscriptions/{id}.json", ct);
            return node?["subscription"] ?? throw new MaxioException(404, $"There is no subscription {id}.");
        }
        catch (MaxioException ex) when (ex.StatusCode == 404)
        {
            throw new MaxioException(404, $"There is no subscription {id}.");
        }
    }

    private async Task<SubscriptionDto> ToSubscriptionDtoAsync(JsonNode sub, CancellationToken ct)
    {
        var id = sub["id"]!.GetValue<long>();
        var balanceCents = sub["balance_in_cents"]?.GetValue<long>() ?? 0;
        var owes = balanceCents > 0;
        if (!owes)
        {
            // An invoice that is still open can carry an amount the balance does not show yet.
            var open = await GetAllAsync($"invoices.json?subscription_id={id}&status=open", "", ct);
            owes = open.Any(i => ParseDecimal(i["due_amount"]) > 0);
        }

        return new SubscriptionDto(
            id,
            sub["state"]?.GetValue<string>() ?? "",
            sub["product"]?["handle"]?.GetValue<string>(),
            sub["product"]?["name"]?.GetValue<string>(),
            !owes,
            balanceCents / 100m,
            ParseDate(sub["next_assessment_at"]),
            ParseDate(sub["canceled_at"]));
    }

    // ---------- 4/5. pause, resume, cancel, restart ----------

    public async Task<SubscriptionDto> PauseAsync(string shopper, long id, CancellationToken ct)
    {
        var sub = await GetOwnedAsync(shopper, id, ct);
        var state = State(sub);
        if (state == "on_hold") throw new MaxioException(409, "The subscription is already paused.");
        if (IsEnded(state)) throw new MaxioException(409, $"A {state} subscription cannot be paused.");
        var result = await TransitionAsync(() => _maxio.PostAsync($"subscriptions/{id}/hold.json", new JsonObject(), ct));
        return await ToSubscriptionDtoAsync(result["subscription"]!, ct);
    }

    public async Task<SubscriptionDto> ResumeAsync(string shopper, long id, CancellationToken ct)
    {
        var sub = await GetOwnedAsync(shopper, id, ct);
        if (State(sub) != "on_hold") throw new MaxioException(409, "Only a paused subscription can be resumed.");
        var result = await TransitionAsync(() => _maxio.PostAsync($"subscriptions/{id}/resume.json", null, ct));
        return await ToSubscriptionDtoAsync(result["subscription"]!, ct);
    }

    public async Task<SubscriptionDto> CancelAsync(string shopper, long id, CancellationToken ct)
    {
        var sub = await GetOwnedAsync(shopper, id, ct);
        var state = State(sub);
        if (state == "canceled") throw new MaxioException(409, "The subscription is already cancelled.");
        if (IsEnded(state)) throw new MaxioException(409, $"A {state} subscription cannot be cancelled.");
        var result = await TransitionAsync(() => _maxio.DeleteAsync($"subscriptions/{id}.json", new JsonObject(), ct));
        return await ToSubscriptionDtoAsync(result["subscription"]!, ct);
    }

    public async Task<SubscriptionDto> RestartAsync(string shopper, long id, CancellationToken ct)
    {
        var sub = await GetOwnedAsync(shopper, id, ct);
        if (State(sub) != "canceled") throw new MaxioException(409, "Only a cancelled subscription can be restarted.");
        var result = await TransitionAsync(() => _maxio.PutAsync($"subscriptions/{id}/reactivate.json", new JsonObject(), ct));
        return await ToSubscriptionDtoAsync(result["subscription"]!, ct);
    }

    private static string State(JsonNode sub) => sub["state"]?.GetValue<string>() ?? "";
    private static bool IsEnded(string state) => state is "canceled" or "expired" or "failed_to_create";

    /// <summary>Maxio refusing a state change is a conflict with the subscription's state.</summary>
    private static async Task<JsonNode> TransitionAsync(Func<Task<JsonNode?>> call)
    {
        try
        {
            return await call() ?? throw new MaxioException(502, "The billing system returned an empty response.");
        }
        catch (MaxioException ex) when (ex.StatusCode == 422)
        {
            throw new MaxioException(409, ex.Message);
        }
    }

    // ---------- 6. switch plan ----------

    public async Task<SubscriptionDto> SwitchPlanAsync(string shopper, long id, string? planHandle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new MaxioException(400, "A plan handle is required.");
        }
        var sub = await GetOwnedAsync(shopper, id, ct);
        var target = await GetSubscribablePlanAsync(planHandle, ct);

        if (sub["product"]?["id"]?.GetValue<long>() == target["id"]!.GetValue<long>())
        {
            throw new MaxioException(409, "The subscription is already on that plan.");
        }

        var wasCancelled = State(sub) == "canceled";
        if (wasCancelled)
        {
            // Maxio cannot migrate a cancelled subscription, so bring it back first.
            await TransitionAsync(() => _maxio.PutAsync($"subscriptions/{id}/reactivate.json", new JsonObject(), ct));
        }

        JsonNode? migrated;
        try
        {
            migrated = await _maxio.PostAsync($"subscriptions/{id}/migrations.json",
                new JsonObject { ["migration"] = new JsonObject { ["product_handle"] = target["handle"]!.GetValue<string>() } }, ct);
        }
        catch (MaxioException)
        {
            if (wasCancelled)
            {
                try { await _maxio.DeleteAsync($"subscriptions/{id}.json", new JsonObject(), CancellationToken.None); } catch (MaxioException) { }
            }
            throw;
        }
        return await ToSubscriptionDtoAsync(migrated!["subscription"]!, ct);
    }

    // ---------- 7. usage for a day ----------

    public async Task<UsageDayDto> GetUsageAsync(string shopper, long id, string? date, CancellationToken ct)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            throw new MaxioException(400, "The date query parameter is required, formatted YYYY-MM-DD.");
        }
        await GetOwnedAsync(shopper, id, ct);

        var components = await GetAllAsync($"subscriptions/{id}/components.json", "component", ct);
        var result = new List<UsageComponentDto>();
        foreach (var c in components.Where(c => c["kind"]?.GetValue<string>() == "metered_component"))
        {
            var componentId = c["component_id"]!.GetValue<long>();
            var usages = await GetAllAsync($"subscriptions/{id}/components/{componentId}/usages.json", "usage", ct);
            decimal quantity = 0;
            foreach (var u in usages)
            {
                // The day is read in the timezone Maxio stamps the usage with (the site's).
                var at = ParseDate(u["created_at"]);
                if (at != null && DateOnly.FromDateTime(at.Value.DateTime) == day)
                {
                    quantity += ParseDecimal(u["quantity"]);
                }
            }
            result.Add(new UsageComponentDto(componentId, c["name"]?.GetValue<string>() ?? "", c["unit_name"]?.GetValue<string>(), quantity));
        }
        return new UsageDayDto(id, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), result.Sum(r => r.Quantity), result);
    }

    // ---------- 8. new plans ----------

    public async Task<PlanDto> CreatePlanAsync(string? name, decimal? monthlyPrice, string? brand, int? brandId, CancellationToken ct)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name)) throw new MaxioException(400, "The plan name is required.");
        if (monthlyPrice == null) throw new MaxioException(400, "The monthly price is required.");
        if (monthlyPrice < 0 || decimal.Round(monthlyPrice.Value, 2) != monthlyPrice)
        {
            throw new MaxioException(422, "The monthly price must be zero or more, with at most two decimal places.");
        }
        var cents = (long)(monthlyPrice.Value * 100m);

        var brandName = await ResolveBrandAsync(brand, brandId, ct);

        var handle = Slugify(name);
        if (handle.Length == 0) throw new MaxioException(422, "The plan name must contain letters or digits.");

        var existing = await GetAllAsync("products.json", "product", ct);
        if (existing.Any(p => string.Equals(p["name"]?.GetValue<string>(), name, StringComparison.OrdinalIgnoreCase)
                              && p["archived_at"] == null))
        {
            throw new MaxioException(409, $"A plan named '{name}' already exists.");
        }
        if (existing.Any(p => string.Equals(p["handle"]?.GetValue<string>(), handle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new MaxioException(409, $"A plan with handle '{handle}' already exists.");
        }

        var familyId = await GetOrCreateFamilyAsync(brandName, ct);
        var created = await _maxio.PostAsync($"product_families/{familyId}/products.json", new JsonObject
        {
            ["product"] = new JsonObject
            {
                ["name"] = name,
                ["handle"] = handle,
                ["price_in_cents"] = cents,
                ["interval"] = 1,
                ["interval_unit"] = "month",
                ["request_credit_card"] = false,
                ["require_credit_card"] = false
            }
        }, ct);
        var plan = ToPlan(created!["product"]!);
        return plan with { Brand = brandName };
    }

    private async Task<string> ResolveBrandAsync(string? brand, int? brandId, CancellationToken ct)
    {
        if (brandId == null && string.IsNullOrWhiteSpace(brand))
        {
            throw new MaxioException(400, "The brand is required.");
        }
        var brands = await _brands.ListAsync(ct);
        var match = brandId != null
            ? brands.FirstOrDefault(b => b.Id == brandId)
            : brands.FirstOrDefault(b => string.Equals(b.Brand, brand!.Trim(), StringComparison.OrdinalIgnoreCase));
        return match?.Brand ?? throw new MaxioException(422, "The brand is not one of the brands in the catalog.");
    }

    private async Task<long> GetOrCreateFamilyAsync(string brand, CancellationToken ct)
    {
        var families = await GetAllAsync("product_families.json", "product_family", ct);
        var wanted = new[] { brand, brand + BRAND_FAMILY_SUFFIX };
        var family = families.FirstOrDefault(f => f["archived_at"] == null
            && wanted.Contains(f["name"]?.GetValue<string>() ?? "", StringComparer.OrdinalIgnoreCase));
        if (family != null)
        {
            return family["id"]!.GetValue<long>();
        }
        var created = await _maxio.PostAsync("product_families.json", new JsonObject
        {
            ["product_family"] = new JsonObject { ["name"] = brand + BRAND_FAMILY_SUFFIX, ["handle"] = Slugify(brand + BRAND_FAMILY_SUFFIX) }
        }, ct);
        return created!["product_family"]!["id"]!.GetValue<long>();
    }

    // ---------- 9. currency prices ----------

    public async Task<CurrencyPriceDto> AddCurrencyPriceAsync(long planId, string? currency, decimal? price, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(currency) || !CurrencyRegex.IsMatch(currency.Trim()))
        {
            throw new MaxioException(400, "The currency must be a three-letter ISO 4217 code.");
        }
        if (price == null) throw new MaxioException(400, "The price is required.");
        if (price <= 0) throw new MaxioException(422, "The price must be greater than zero.");
        currency = currency.Trim().ToUpperInvariant();

        JsonNode? product;
        try
        {
            product = (await _maxio.GetAsync($"products/{planId}.json", ct))?["product"];
        }
        catch (MaxioException ex) when (ex.StatusCode == 404)
        {
            throw new MaxioException(404, $"There is no plan {planId}.");
        }
        var pricePointId = product?["default_product_price_point_id"]?.GetValue<long>()
            ?? throw new MaxioException(404, $"There is no plan {planId}.");

        await _maxio.PostAsync($"product_price_points/{pricePointId}/currency_prices.json", new JsonObject
        {
            ["currency_prices"] = new JsonArray(new JsonObject
            {
                ["currency"] = currency,
                ["price"] = price.Value,
                ["role"] = "baseline"
            })
        }, ct);
        return new CurrencyPriceDto(planId, currency, price.Value);
    }

    // ---------- 10. next billing date ----------

    public async Task<SubscriptionDto> SetNextBillingDateAsync(long id, DateOnly? date, CancellationToken ct)
    {
        if (date == null) throw new MaxioException(400, "The date is required, formatted YYYY-MM-DD.");
        if (date.Value <= DateOnly.FromDateTime(DateTime.UtcNow))
        {
            throw new MaxioException(422, "The next billing date must be in the future.");
        }
        var sub = await GetAnyAsync(id, ct);
        var state = State(sub);
        if (IsEnded(state) || state == "on_hold")
        {
            throw new MaxioException(409, $"A {state} subscription is not billed, so it has no billing date to set.");
        }

        // Midday UTC keeps the calendar date the same in every timezone the site could use.
        var at = date.Value.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var updated = await TransitionAsync(() => _maxio.PutAsync($"subscriptions/{id}.json",
            new JsonObject { ["subscription"] = new JsonObject { ["next_billing_at"] = at } }, ct));
        return await ToSubscriptionDtoAsync(updated["subscription"]!, ct);
    }

    // ---------- helpers ----------

    /// <summary>Reads every page of a Maxio list; items are unwrapped from <paramref name="wrapper"/> when given.</summary>
    private async Task<List<JsonNode>> GetAllAsync(string path, string wrapper, CancellationToken ct)
    {
        const int perPage = 200;
        var items = new List<JsonNode>();
        string? firstIdOfPreviousPage = null;
        for (var page = 1; ; page++)
        {
            var sep = path.Contains('?') ? '&' : '?';
            var node = await _maxio.GetAsync($"{path}{sep}per_page={perPage}&page={page}", ct);
            var array = node as JsonArray ?? (node as JsonObject)?.Select(kv => kv.Value).OfType<JsonArray>().FirstOrDefault();
            if (array == null || array.Count == 0) break;

            var batch = array.Where(x => x != null)
                .Select(x => (wrapper.Length > 0 ? x![wrapper] : x) ?? x!)
                .ToList();
            var firstId = batch.Count > 0 ? (batch[0]["id"]?.ToString() ?? batch[0].ToJsonString()) : null;
            if (firstId != null && firstId == firstIdOfPreviousPage) break; // endpoint ignores paging
            firstIdOfPreviousPage = firstId;

            items.AddRange(batch.Select(x => x.DeepClone()));
            if (array.Count < perPage) break;
        }
        return items;
    }

    private static decimal ParseDecimal(JsonNode? node)
    {
        if (node == null) return 0;
        return decimal.TryParse(node.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    private static DateTimeOffset? ParseDate(JsonNode? node)
    {
        var s = node?.GetValue<string>();
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    private static string Slugify(string value)
    {
        var sb = new StringBuilder();
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }
}
