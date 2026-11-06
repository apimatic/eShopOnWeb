using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public class PlanDto
{
    public long Id { get; set; }
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string Interval { get; set; } = "";
    public int BrandId { get; set; }
    public string BrandName { get; set; } = "";
}

public class SubscriptionDto
{
    public long Id { get; set; }
    public string State { get; set; } = "";
    public long PlanId { get; set; }
    public string PlanHandle { get; set; } = "";
    public string PlanName { get; set; } = "";
    /// <summary>True when the shopper owes nothing on the subscription.</summary>
    public bool Paid { get; set; }
    public decimal AmountDue { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
}

public class UsageEntryDto
{
    public long Id { get; set; }
    public decimal Quantity { get; set; }
    public string? Memo { get; set; }
    public DateTimeOffset? RecordedAt { get; set; }
}

public class ComponentUsageDto
{
    public long ComponentId { get; set; }
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public string? UnitName { get; set; }
    public decimal Quantity { get; set; }
    public List<UsageEntryDto> Entries { get; set; } = new();
}

public class DailyUsageDto
{
    public long SubscriptionId { get; set; }
    public string Date { get; set; } = "";
    public List<ComponentUsageDto> Components { get; set; } = new();
}

/// <summary>
/// Billing logic. Maxio is the system of record: nothing about subscriptions is stored locally.
/// A shopper is mapped to a Maxio customer through the customer reference (the JWT user name).
/// </summary>
public class BillingService
{
    public const string BRAND_FAMILY_PREFIX = "catalog-brand-";
    private static readonly Regex BrandHandle = new("^catalog-brand-(\\d+)$", RegexOptions.Compiled);

    private readonly MaxioClient _maxio;
    private readonly IReadRepository<CatalogBrand> _brands;

    public BillingService(MaxioClient maxio, IReadRepository<CatalogBrand> brands)
    {
        _maxio = maxio;
        _brands = brands;
    }

    // ---------- plans ----------

    public async Task<List<PlanDto>> ListPlansAsync(CancellationToken ct)
    {
        var plans = new List<PlanDto>();
        var brands = (await _brands.ListAsync(ct)).ToDictionary(b => b.Id);
        var families = await _maxio.ListAllAsync("product_families.json", ct);
        foreach (var f in families)
        {
            var family = f["product_family"];
            var brandId = BrandIdOf(family?["handle"]?.GetValue<string>());
            if (family == null || brandId == null || !brands.ContainsKey(brandId.Value))
            {
                continue;
            }
            var products = await _maxio.ListAllAsync("product_families/" + family["id"] + "/products.json", ct);
            foreach (var p in products)
            {
                var product = p["product"];
                if (product != null && IsSubscribable(product))
                {
                    plans.Add(ToPlan(product, brandId.Value, brands[brandId.Value].Brand));
                }
            }
        }
        return plans.OrderBy(p => p.BrandName).ThenBy(p => p.Name).ToList();
    }

    /// <summary>Finds a plan by handle that a shopper can subscribe to, or throws 404/422.</summary>
    public async Task<PlanDto> GetSubscribablePlanAsync(string? handle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new BillingException(400, "A plan handle is required.");
        }
        var node = await _maxio.SendAsync(HttpMethod.Get, "products/handle/" + Uri.EscapeDataString(handle.Trim()) + ".json",
            nullOnNotFound: true, ct: ct);
        var product = node?["product"];
        if (product == null || product["archived_at"] != null)
        {
            throw new BillingException(404, $"Plan '{handle}' does not exist.");
        }
        var plan = await ToBrandPlanAsync(product, ct);
        if (plan == null)
        {
            throw new BillingException(404, $"Plan '{handle}' does not exist.");
        }
        if (!IsSubscribable(product))
        {
            throw new BillingException(422, $"Plan '{handle}' requires card details and cannot be subscribed to here.");
        }
        return plan;
    }

    public async Task<PlanDto> CreatePlanAsync(string? name, decimal? price, int? brandId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new BillingException(400, "A plan name is required.");
        }
        name = name.Trim();
        if (price == null || price < 0)
        {
            throw new BillingException(400, "A monthly price of zero or more is required.");
        }
        if (decimal.Round(price.Value, 2) != price.Value)
        {
            throw new BillingException(400, "The monthly price cannot have more than two decimal places.");
        }
        if (brandId == null)
        {
            throw new BillingException(400, "A brandId is required.");
        }
        var brand = await _brands.GetByIdAsync(brandId.Value, ct);
        if (brand == null)
        {
            throw new BillingException(404, $"Brand {brandId} is not in the catalog.");
        }

        var handle = Slugify(name);
        if (handle.Length == 0)
        {
            throw new BillingException(400, "The plan name must contain letters or digits.");
        }

        var family = await EnsureFamilyAsync(brand, ct);
        var familyId = family["id"]!.GetValue<long>();

        var existing = await _maxio.SendAsync(HttpMethod.Get, "products/handle/" + handle + ".json", nullOnNotFound: true, ct: ct);
        if (existing != null)
        {
            throw new BillingException(409, $"A plan named '{name}' already exists.");
        }
        var siblings = await _maxio.ListAllAsync("product_families/" + familyId + "/products.json?include_archived=true", ct);
        if (siblings.Any(s => string.Equals(s["product"]?["name"]?.GetValue<string>(), name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new BillingException(409, $"A plan named '{name}' already exists for brand '{brand.Brand}'.");
        }

        var created = await _maxio.SendAsync(HttpMethod.Post, "product_families/" + familyId + "/products.json", new
        {
            product = new
            {
                name,
                handle,
                price_in_cents = (long)(price.Value * 100m),
                interval = 1,
                interval_unit = "month",
                require_credit_card = false,
                request_credit_card = false
            }
        }, ct: ct);
        return ToPlan(created!["product"]!, brand.Id, brand.Brand);
    }

    public async Task AddCurrencyPriceAsync(long planId, string? currency, decimal? price, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(currency) || !Regex.IsMatch(currency.Trim(), "^[A-Za-z]{3}$"))
        {
            throw new BillingException(400, "A three-letter currency code is required.");
        }
        if (price == null || price <= 0)
        {
            throw new BillingException(400, "A price greater than zero is required.");
        }
        var node = await _maxio.SendAsync(HttpMethod.Get, "products/" + planId + ".json", nullOnNotFound: true, ct: ct);
        var product = node?["product"];
        if (product == null || await ToBrandPlanAsync(product, ct) == null)
        {
            throw new BillingException(404, $"Plan {planId} does not exist.");
        }

        var pricePoints = await _maxio.SendAsync(HttpMethod.Get, "products/" + planId + "/price_points.json", ct: ct);
        var points = pricePoints?["price_points"] as JsonArray;
        var point = points?.FirstOrDefault(p => p?["type"]?.GetValue<string>() == "default" && p["archived_at"] == null)
                    ?? points?.FirstOrDefault(p => p?["archived_at"] == null);
        if (point == null)
        {
            throw new BillingException(422, $"Plan {planId} has no price point to add a currency price to.");
        }

        await _maxio.SendAsync(HttpMethod.Post, "product_price_points/" + point["id"] + "/currency_prices.json", new
        {
            currency_prices = new[]
            {
                new { currency = currency.Trim().ToUpperInvariant(), price = price.Value, role = "baseline" }
            }
        }, ct: ct);
    }

    // ---------- subscriptions ----------

    public async Task<SubscriptionDto> SubscribeAsync(string username, string? planHandle, CancellationToken ct)
    {
        var plan = await GetSubscribablePlanAsync(planHandle, ct);
        var customerId = await EnsureCustomerAsync(username, ct);
        var created = await _maxio.SendAsync(HttpMethod.Post, "subscriptions.json", new
        {
            subscription = new
            {
                product_handle = plan.Handle,
                customer_id = customerId,
                // No card details: the shopper is invoiced.
                payment_collection_method = "remittance"
            }
        }, ct: ct);
        return await ToSubscriptionAsync(created!["subscription"]!, ct);
    }

    public async Task<SubscriptionDto> GetSubscriptionAsync(string username, long id, CancellationToken ct)
    {
        var sub = await GetOwnedAsync(username, id, ct);
        return await ToSubscriptionAsync(sub, ct);
    }

    public async Task<SubscriptionDto> PauseAsync(string username, long id, CancellationToken ct)
    {
        var sub = await GetOwnedAsync(username, id, ct);
        var state = StateOf(sub);
        if (state == "on_hold")
        {
            throw new BillingException(409, "The subscription is already paused.");
        }
        if (state is not ("active" or "trialing" or "past_due" or "unpaid" or "soft_failure"))
        {
            throw new BillingException(409, $"A subscription that is {state} cannot be paused.");
        }
        var result = await _maxio.SendAsync(HttpMethod.Post, "subscriptions/" + id + "/hold.json", new { }, ct: ct);
        return await ToSubscriptionAsync(result!["subscription"]!, ct);
    }

    public async Task<SubscriptionDto> ResumeAsync(string username, long id, CancellationToken ct)
    {
        var sub = await GetOwnedAsync(username, id, ct);
        var state = StateOf(sub);
        if (state != "on_hold")
        {
            throw new BillingException(409, $"Only a paused subscription can be resumed; this one is {state}.");
        }
        var result = await _maxio.SendAsync(HttpMethod.Post, "subscriptions/" + id + "/resume.json", new { }, ct: ct);
        return await ToSubscriptionAsync(result!["subscription"]!, ct);
    }

    public async Task<SubscriptionDto> CancelAsync(string username, long id, CancellationToken ct)
    {
        var sub = await GetOwnedAsync(username, id, ct);
        var state = StateOf(sub);
        if (state == "canceled")
        {
            throw new BillingException(409, "The subscription is already cancelled.");
        }
        if (state is not ("active" or "trialing" or "past_due" or "unpaid" or "soft_failure" or "on_hold"))
        {
            throw new BillingException(409, $"A subscription that is {state} cannot be cancelled.");
        }
        var result = await _maxio.SendAsync(HttpMethod.Delete, "subscriptions/" + id + ".json",
            new { cancellation_message = "Cancelled by the shopper through eShopOnWeb" }, ct: ct);
        return await ToSubscriptionAsync(result!["subscription"]!, ct);
    }

    public async Task<SubscriptionDto> RestartAsync(string username, long id, CancellationToken ct)
    {
        var sub = await GetOwnedAsync(username, id, ct);
        var state = StateOf(sub);
        if (state != "canceled")
        {
            throw new BillingException(409, $"Only a cancelled subscription can be restarted; this one is {state}.");
        }
        var result = await _maxio.SendAsync(HttpMethod.Put, "subscriptions/" + id + "/reactivate.json", new { }, ct: ct);
        return await ToSubscriptionAsync(result!["subscription"]!, ct);
    }

    public async Task<SubscriptionDto> SwitchAsync(string username, long id, string? planHandle, CancellationToken ct)
    {
        var sub = await GetOwnedAsync(username, id, ct);
        var plan = await GetSubscribablePlanAsync(planHandle, ct);
        var state = StateOf(sub);
        if (string.Equals(sub["product"]?["handle"]?.GetValue<string>(), plan.Handle, StringComparison.OrdinalIgnoreCase))
        {
            throw new BillingException(409, "The subscription is already on that plan.");
        }

        // Maxio does not migrate a cancelled or paused subscription, so bring it back first.
        var wasCanceled = state == "canceled";
        var wasPaused = state == "on_hold";
        if (wasCanceled)
        {
            await _maxio.SendAsync(HttpMethod.Put, "subscriptions/" + id + "/reactivate.json", new { }, ct: ct);
        }
        else if (wasPaused)
        {
            await _maxio.SendAsync(HttpMethod.Post, "subscriptions/" + id + "/resume.json", new { }, ct: ct);
        }

        try
        {
            var result = await _maxio.SendAsync(HttpMethod.Post, "subscriptions/" + id + "/migrations.json",
                new { migration = new { product_handle = plan.Handle } }, ct: ct);
            if (wasPaused)
            {
                // A paused subscription stays paused, now on the new plan.
                result = await _maxio.SendAsync(HttpMethod.Post, "subscriptions/" + id + "/hold.json", new { }, ct: ct);
            }
            return await ToSubscriptionAsync(result!["subscription"]!, ct);
        }
        catch (BillingException) when (wasCanceled || wasPaused)
        {
            // Leave the subscription as the shopper had it.
            try
            {
                if (wasCanceled)
                {
                    await _maxio.SendAsync(HttpMethod.Delete, "subscriptions/" + id + ".json",
                        new { cancellation_message = "Plan switch failed; restored cancelled state" }, ct: CancellationToken.None);
                }
                else
                {
                    await _maxio.SendAsync(HttpMethod.Post, "subscriptions/" + id + "/hold.json", new { }, ct: CancellationToken.None);
                }
            }
            catch (BillingException)
            {
            }
            throw;
        }
    }

    public async Task<DailyUsageDto> GetUsageAsync(string username, long id, string? date, CancellationToken ct)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            throw new BillingException(400, "The date query parameter is required, formatted YYYY-MM-DD.");
        }
        await GetOwnedAsync(username, id, ct);

        var result = new DailyUsageDto { SubscriptionId = id, Date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        var components = await _maxio.ListAllAsync("subscriptions/" + id + "/components.json", ct);
        foreach (var c in components)
        {
            var component = c["component"];
            if (component == null || component["kind"]?.GetValue<string>() != "metered_component")
            {
                continue;
            }
            var componentId = component["component_id"]!.GetValue<long>();
            // Maxio's date filters are not inclusive of a single day, so ask for a wider window and
            // keep the entries recorded on the requested day (in the site's own time zone).
            var since = day.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var until = day.AddDays(2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var usages = await _maxio.ListAllAsync(
                "subscriptions/" + id + "/components/" + componentId + "/usages.json?since_date=" + since + "&until_date=" + until, ct);

            var dto = new ComponentUsageDto
            {
                ComponentId = componentId,
                Handle = component["component_handle"]?.GetValue<string>() ?? "",
                Name = component["name"]?.GetValue<string>() ?? "",
                UnitName = component["unit_name"]?.GetValue<string>()
            };
            foreach (var u in usages)
            {
                var usage = u["usage"];
                var createdAt = ParseDate(usage?["created_at"]);
                if (usage == null || createdAt == null || DateOnly.FromDateTime(createdAt.Value.DateTime) != day)
                {
                    continue;
                }
                var quantity = Decimal(usage["quantity"]);
                dto.Entries.Add(new UsageEntryDto
                {
                    Id = usage["id"]?.GetValue<long>() ?? 0,
                    Quantity = quantity,
                    Memo = usage["memo"]?.GetValue<string>(),
                    RecordedAt = createdAt
                });
                dto.Quantity += quantity;
            }
            result.Components.Add(dto);
        }
        return result;
    }

    public async Task<SubscriptionDto> SetNextBillingDateAsync(long id, string? date, CancellationToken ct)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            throw new BillingException(400, "A date formatted YYYY-MM-DD is required.");
        }
        var node = await _maxio.SendAsync(HttpMethod.Get, "subscriptions/" + id + ".json", nullOnNotFound: true, ct: ct);
        var sub = node?["subscription"];
        if (sub == null)
        {
            throw new BillingException(404, $"Subscription {id} does not exist.");
        }
        var state = StateOf(sub);
        if (state is not ("active" or "trialing" or "past_due" or "unpaid" or "soft_failure"))
        {
            throw new BillingException(409, $"A subscription that is {state} is not billed, so its next billing date cannot be set.");
        }
        if (day <= DateOnly.FromDateTime(DateTime.UtcNow))
        {
            throw new BillingException(400, "The next billing date must be in the future.");
        }

        // Midday UTC keeps the calendar date the same in the site's time zone.
        var at = new DateTime(day.Year, day.Month, day.Day, 12, 0, 0, DateTimeKind.Utc);
        var result = await _maxio.SendAsync(HttpMethod.Put, "subscriptions/" + id + ".json",
            new { subscription = new { next_billing_at = at.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) } }, ct: ct);
        return await ToSubscriptionAsync(result!["subscription"]!, ct);
    }

    // ---------- helpers ----------

    private async Task<JsonNode> GetOwnedAsync(string username, long id, CancellationToken ct)
    {
        var node = await _maxio.SendAsync(HttpMethod.Get, "subscriptions/" + id + ".json", nullOnNotFound: true, ct: ct);
        var sub = node?["subscription"];
        var reference = sub?["customer"]?["reference"]?.GetValue<string>();
        if (sub == null || !string.Equals(reference, username, StringComparison.OrdinalIgnoreCase))
        {
            throw new BillingException(404, $"Subscription {id} does not exist.");
        }
        return sub;
    }

    private async Task<long> EnsureCustomerAsync(string username, CancellationToken ct)
    {
        var found = await _maxio.SendAsync(HttpMethod.Get,
            "customers/lookup.json?reference=" + Uri.EscapeDataString(username), nullOnNotFound: true, ct: ct);
        var existing = found?["customer"];
        if (existing != null && string.Equals(existing["reference"]?.GetValue<string>(), username, StringComparison.OrdinalIgnoreCase))
        {
            return existing["id"]!.GetValue<long>();
        }

        var local = username.Contains('@') ? username[..username.IndexOf('@')] : username;
        var email = username.Contains('@') ? username : username + "@shoppers.eshoponweb.invalid";
        var created = await _maxio.SendAsync(HttpMethod.Post, "customers.json", new
        {
            customer = new { first_name = local, last_name = "Shopper", email, reference = username }
        }, ct: ct);
        return created!["customer"]!["id"]!.GetValue<long>();
    }

    private async Task<JsonNode> EnsureFamilyAsync(CatalogBrand brand, CancellationToken ct)
    {
        var handle = BRAND_FAMILY_PREFIX + brand.Id;
        var found = await _maxio.SendAsync(HttpMethod.Get, "product_families/lookup.json?handle=" + handle, nullOnNotFound: true, ct: ct);
        if (found?["product_family"] != null)
        {
            return found["product_family"]!;
        }
        var created = await _maxio.SendAsync(HttpMethod.Post, "product_families.json", new
        {
            product_family = new { name = brand.Brand, handle, description = "Plans for the " + brand.Brand + " brand" }
        }, ct: ct);
        return created!["product_family"]!;
    }

    /// <summary>Maps a Maxio product to a plan when its family stands for a brand in the catalog.</summary>
    private async Task<PlanDto?> ToBrandPlanAsync(JsonNode product, CancellationToken ct)
    {
        var brandId = BrandIdOf(product["product_family"]?["handle"]?.GetValue<string>());
        if (brandId == null)
        {
            return null;
        }
        var brand = await _brands.GetByIdAsync(brandId.Value, ct);
        return brand == null ? null : ToPlan(product, brand.Id, brand.Brand);
    }

    private static int? BrandIdOf(string? familyHandle)
    {
        var m = familyHandle == null ? null : BrandHandle.Match(familyHandle);
        return m is { Success: true } && int.TryParse(m.Groups[1].Value, out var id) ? id : null;
    }

    private static bool IsSubscribable(JsonNode product) =>
        product["archived_at"] == null && product["require_credit_card"]?.GetValue<bool>() != true;

    private static PlanDto ToPlan(JsonNode product, int brandId, string brandName) => new()
    {
        Id = product["id"]!.GetValue<long>(),
        Handle = product["handle"]?.GetValue<string>() ?? "",
        Name = product["name"]?.GetValue<string>() ?? "",
        Price = (product["price_in_cents"]?.GetValue<long>() ?? 0) / 100m,
        Interval = (product["interval"]?.ToString() ?? "1") + " " + product["interval_unit"]?.GetValue<string>(),
        BrandId = brandId,
        BrandName = brandName
    };

    private static string StateOf(JsonNode sub) => sub["state"]?.GetValue<string>() ?? "unknown";

    private async Task<SubscriptionDto> ToSubscriptionAsync(JsonNode sub, CancellationToken ct)
    {
        var id = sub["id"]!.GetValue<long>();
        var invoices = await _maxio.ListAllAsync("invoices.json?subscription_id=" + id, ct);
        // The shopper owes money when any of the subscription's open invoices has an amount due.
        var due = invoices
            .Where(i => i["status"]?.GetValue<string>() == "open")
            .Sum(i => Decimal(i["due_amount"]));
        return new SubscriptionDto
        {
            Id = id,
            State = StateOf(sub),
            PlanId = sub["product"]?["id"]?.GetValue<long>() ?? 0,
            PlanHandle = sub["product"]?["handle"]?.GetValue<string>() ?? "",
            PlanName = sub["product"]?["name"]?.GetValue<string>() ?? "",
            Paid = due <= 0,
            AmountDue = due,
            NextBillingAt = ParseDate(sub["next_assessment_at"]),
            CreatedAt = ParseDate(sub["created_at"]),
            CanceledAt = ParseDate(sub["canceled_at"])
        };
    }

    private static decimal Decimal(JsonNode? n)
    {
        if (n == null) return 0;
        return decimal.TryParse(n.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    private static DateTimeOffset? ParseDate(JsonNode? n) =>
        n != null && DateTimeOffset.TryParse(n.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static string Slugify(string name)
    {
        var slug = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return slug;
    }
}
