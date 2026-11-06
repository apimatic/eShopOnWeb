using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public record PlanDto(long Id, string? Handle, string Name, decimal Price, string Currency, string? Brand, string Interval);

public record SubscriptionDto(
    long Id,
    string State,
    long? PlanId,
    string? PlanHandle,
    string? PlanName,
    bool Paid,
    decimal Balance,
    string? Currency,
    DateTimeOffset? NextBillingDate);

public record UsageComponentDto(long ComponentId, string? Name, string? UnitName, decimal Quantity);

public record UsageDto(long SubscriptionId, string Date, List<UsageComponentDto> Components);

/// <summary>
/// Billing operations. Maxio is the system of record: every call goes to Maxio over HTTP.
/// A Maxio customer is identified by its reference, which is the user name taken from the JWT.
/// </summary>
public class BillingService
{
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
        var items = await _maxio.GetAllPagesAsync("products.json");
        return items
            .Select(i => i["product"])
            .Where(p => p != null && p["archived_at"] == null && !string.IsNullOrWhiteSpace((string?)p["handle"]))
            .Select(p => ToPlan(p!))
            .ToList();
    }

    private static PlanDto ToPlan(JsonNode p)
    {
        var family = (string?)p["product_family"]?["name"];
        return new PlanDto(
            (long)p["id"]!,
            (string?)p["handle"],
            (string)p["name"]!,
            ((decimal?)p["price_in_cents"] ?? 0m) / 100m,
            "USD",
            BrandFromFamily(family),
            p["interval"] + " " + (string?)p["interval_unit"]);
    }

    private const string FAMILY_SUFFIX = " plans";

    private static string? BrandFromFamily(string? family) =>
        family != null && family.EndsWith(FAMILY_SUFFIX, StringComparison.OrdinalIgnoreCase)
            ? family[..^FAMILY_SUFFIX.Length]
            : family;

    private async Task<JsonNode> GetSubscribablePlanAsync(string? handle)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new BillingException(400, "A plan handle is required.");
        }
        var result = await _maxio.TryGetAsync("products/handle/" + Uri.EscapeDataString(handle.Trim()) + ".json");
        var product = result?["product"];
        if (product == null || product["archived_at"] != null)
        {
            throw new BillingException(404, "There is no plan with handle '" + handle.Trim() + "'.");
        }
        return product;
    }

    // ---------- subscriptions ----------

    public async Task<SubscriptionDto> SubscribeAsync(string user, string? email, string? planHandle)
    {
        var plan = await GetSubscribablePlanAsync(planHandle);

        var customer = new JsonObject();
        var existing = await _maxio.TryGetAsync("customers/lookup.json?reference=" + Uri.EscapeDataString(user));
        var existingId = existing?["customer"]?["id"];
        if (existingId != null)
        {
            customer["customer_id"] = (long)existingId;
        }
        else
        {
            var address = !string.IsNullOrWhiteSpace(email) ? email : user;
            var local = user.Contains('@') ? user[..user.IndexOf('@')] : user;
            customer["customer_attributes"] = new JsonObject
            {
                ["first_name"] = local,
                ["last_name"] = local,
                ["email"] = address,
                ["reference"] = user
            };
        }

        var sub = new JsonObject
        {
            ["product_handle"] = (string)plan["handle"]!,
            // Invoice-based collection: no card details are needed.
            ["payment_collection_method"] = "remittance"
        };
        foreach (var kv in customer)
        {
            sub[kv.Key] = kv.Value?.DeepClone();
        }

        var created = await _maxio.PostAsync("subscriptions.json", new JsonObject { ["subscription"] = sub });
        return await ToSubscriptionAsync(created!["subscription"]!);
    }

    private async Task<JsonNode> GetOwnedSubscriptionNodeAsync(long id, string user)
    {
        var result = await _maxio.TryGetAsync("subscriptions/" + id + ".json");
        var sub = result?["subscription"];
        var reference = (string?)sub?["customer"]?["reference"];
        // Someone else's subscription looks exactly like a missing one.
        if (sub == null || !string.Equals(reference, user, StringComparison.OrdinalIgnoreCase))
        {
            throw new BillingException(404, "Subscription " + id + " was not found.");
        }
        return sub;
    }

    public async Task<SubscriptionDto> GetSubscriptionAsync(long id, string user) =>
        await ToSubscriptionAsync(await GetOwnedSubscriptionNodeAsync(id, user));

    private async Task<SubscriptionDto> ToSubscriptionAsync(JsonNode sub)
    {
        var id = (long)sub["id"]!;
        var balanceCents = (decimal?)sub["balance_in_cents"] ?? 0m;
        var owes = balanceCents > 0;
        if (!owes)
        {
            // Open invoices with an amount due also count as money still owed.
            var invoices = await _maxio.GetAsync("invoices.json?subscription_id=" + id + "&status=open&per_page=200");
            var list = invoices?["invoices"] as JsonArray;
            owes = list != null && list.Any(i => ParseMoney(i?["due_amount"]) > 0m);
        }

        var product = sub["product"];
        var next = (string?)sub["next_assessment_at"];
        return new SubscriptionDto(
            id,
            (string)sub["state"]!,
            (long?)product?["id"],
            (string?)product?["handle"],
            (string?)product?["name"],
            !owes,
            balanceCents / 100m,
            (string?)sub["currency"],
            next == null ? null : DateTimeOffset.Parse(next, CultureInfo.InvariantCulture));
    }

    private static decimal ParseMoney(JsonNode? n)
    {
        if (n == null) return 0m;
        var s = n.ToString();
        return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;
    }

    private async Task<SubscriptionDto> ActionAsync(long id, string user, Func<string, Task<JsonNode?>> action)
    {
        await GetOwnedSubscriptionNodeAsync(id, user);
        var result = await action("subscriptions/" + id);
        var sub = result?["subscription"] ?? (await _maxio.GetAsync("subscriptions/" + id + ".json"))?["subscription"];
        return await ToSubscriptionAsync(sub!);
    }

    public Task<SubscriptionDto> PauseAsync(long id, string user) =>
        ActionAsync(id, user, p => _maxio.PostAsync(p + "/hold.json"));

    public Task<SubscriptionDto> ResumeAsync(long id, string user) =>
        ActionAsync(id, user, p => _maxio.PostAsync(p + "/resume.json"));

    public Task<SubscriptionDto> CancelAsync(long id, string user) =>
        ActionAsync(id, user, p => _maxio.DeleteAsync(p + ".json"));

    public Task<SubscriptionDto> RestartAsync(long id, string user) =>
        ActionAsync(id, user, p => _maxio.PutAsync(p + "/reactivate.json"));

    public async Task<SubscriptionDto> SwitchAsync(long id, string user, string? planHandle)
    {
        var sub = await GetOwnedSubscriptionNodeAsync(id, user);
        var plan = await GetSubscribablePlanAsync(planHandle);
        var state = (string?)sub["state"];
        var path = "subscriptions/" + id;

        if (state != "canceled" && state != "on_hold"
            && (string?)sub["product"]?["handle"] == (string?)plan["handle"])
        {
            throw new BillingException(409, "The subscription is already on that plan.");
        }

        // Maxio only migrates active subscriptions, so a cancelled or paused one is
        // brought back first and then put back as it was if the switch fails/for pause.
        if (state == "canceled")
        {
            await _maxio.PutAsync(path + "/reactivate.json");
        }
        else if (state == "on_hold")
        {
            await _maxio.PostAsync(path + "/resume.json");
        }

        try
        {
            await _maxio.PostAsync(path + "/migrations.json", new JsonObject
            {
                ["migration"] = new JsonObject { ["product_handle"] = (string)plan["handle"]! }
            });
        }
        catch
        {
            await RestoreAsync(path, state);
            throw;
        }

        // A paused subscription stays paused on its new plan; a cancelled one is now on the new plan, active.
        if (state == "on_hold")
        {
            await RestoreAsync(path, state);
        }

        var fresh = await _maxio.GetAsync(path + ".json");
        return await ToSubscriptionAsync(fresh!["subscription"]!);
    }

    private async Task RestoreAsync(string path, string? originalState)
    {
        try
        {
            if (originalState == "canceled") await _maxio.DeleteAsync(path + ".json");
            else if (originalState == "on_hold") await _maxio.PostAsync(path + "/hold.json");
        }
        catch (BillingException)
        {
            // best effort
        }
    }

    // ---------- usage ----------

    public async Task<UsageDto> GetUsageAsync(long id, string user, DateTime date)
    {
        await GetOwnedSubscriptionNodeAsync(id, user);

        var componentsResult = await _maxio.GetAllPagesAsync("subscriptions/" + id + "/components.json");
        var components = componentsResult
            .Select(c => c["component"])
            .Where(c => c != null && (string?)c["kind"] == "metered_component")
            .ToList();

        // Maxio's until_date is exclusive and compared in the site's time zone, so ask for a
        // wider window and keep only usages recorded on the requested calendar day.
        var since = date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var until = date.AddDays(2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var result = new List<UsageComponentDto>();
        foreach (var c in components)
        {
            var componentId = (long)c!["component_id"]!;
            var usages = await _maxio.GetAllPagesAsync(
                "subscriptions/" + id + "/components/" + componentId + "/usages.json?since_date=" + since + "&until_date=" + until);
            var seen = new HashSet<long>();
            decimal total = 0;
            foreach (var u in usages)
            {
                var usage = u["usage"];
                var created = (string?)usage?["created_at"];
                if (usage == null || created == null) continue;
                if (DateTimeOffset.Parse(created, CultureInfo.InvariantCulture).Date != date.Date) continue;
                if (!seen.Add((long)usage["id"]!)) continue;
                total += ParseMoney(usage["quantity"]);
            }
            result.Add(new UsageComponentDto(componentId, (string?)c["name"], (string?)c["unit_name"], total));
        }

        return new UsageDto(id, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), result);
    }

    // ---------- staff ----------

    public async Task<PlanDto> CreatePlanAsync(string? name, decimal? price, string? brand)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new BillingException(400, "A plan name is required.");
        if (price == null || price < 0) throw new BillingException(400, "A monthly price of zero or more is required.");
        if (decimal.Round(price.Value, 2) != price.Value) throw new BillingException(400, "The price can have at most two decimals.");
        if (string.IsNullOrWhiteSpace(brand)) throw new BillingException(400, "A brand is required.");

        var brands = await _brands.ListAsync();
        var catalogBrand = brands.FirstOrDefault(b => string.Equals(b.Brand, brand.Trim(), StringComparison.OrdinalIgnoreCase));
        if (catalogBrand == null)
        {
            throw new BillingException(400, "'" + brand.Trim() + "' is not a brand in the catalog.");
        }

        var handle = Slugify(name);
        if (handle.Length == 0) throw new BillingException(400, "The plan name must contain letters or digits.");
        if (await _maxio.TryGetAsync("products/handle/" + handle + ".json") != null)
        {
            throw new BillingException(409, "A plan with handle '" + handle + "' already exists.");
        }

        var familyId = await GetOrCreateFamilyAsync(catalogBrand.Brand);
        var created = await _maxio.PostAsync("product_families/" + familyId + "/products.json", new JsonObject
        {
            ["product"] = new JsonObject
            {
                ["name"] = name.Trim(),
                ["handle"] = handle,
                ["description"] = name.Trim(),
                ["price_in_cents"] = (long)decimal.Round(price.Value * 100m),
                ["interval"] = 1,
                ["interval_unit"] = "month"
            }
        });
        return ToPlan(created!["product"]!);
    }

    private async Task<long> GetOrCreateFamilyAsync(string brand)
    {
        var familyName = brand + FAMILY_SUFFIX;
        var families = await _maxio.GetAllPagesAsync("product_families.json");
        var match = families
            .Select(f => f["product_family"])
            .FirstOrDefault(f => f != null && f["archived_at"] == null
                && string.Equals((string?)f["name"], familyName, StringComparison.OrdinalIgnoreCase));
        if (match != null) return (long)match["id"]!;

        var created = await _maxio.PostAsync("product_families.json", new JsonObject
        {
            ["product_family"] = new JsonObject { ["name"] = familyName, ["handle"] = Slugify(familyName) }
        });
        return (long)created!["product_family"]!["id"]!;
    }

    private static string Slugify(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }

    public async Task<object> AddCurrencyPriceAsync(long planId, string? currency, decimal? price)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3 || !currency.Trim().All(char.IsLetter))
        {
            throw new BillingException(400, "A three-letter currency code is required.");
        }
        if (price == null || price < 0) throw new BillingException(400, "A price of zero or more is required.");

        var product = (await _maxio.TryGetAsync("products/" + planId + ".json"))?["product"];
        if (product == null) throw new BillingException(404, "Plan " + planId + " was not found.");

        var code = currency.Trim().ToUpperInvariant();
        var pricePointId = (long)product["default_product_price_point_id"]!;
        var result = await _maxio.PostAsync("product_price_points/" + pricePointId + "/currency_prices.json", new JsonObject
        {
            ["currency_prices"] = new JsonArray
            {
                new JsonObject { ["currency"] = code, ["price"] = price.Value, ["role"] = "baseline" }
            }
        });
        return new { planId, currency = code, price = price.Value, maxio = result };
    }

    public async Task<SubscriptionDto> SetNextBillingDateAsync(long id, DateTime? date)
    {
        if (date == null) throw new BillingException(400, "A date (YYYY-MM-DD) is required.");

        var sub = (await _maxio.TryGetAsync("subscriptions/" + id + ".json"))?["subscription"];
        if (sub == null) throw new BillingException(404, "Subscription " + id + " was not found.");

        var state = (string?)sub["state"];
        var current = (string?)sub["next_assessment_at"];
        if (current == null || state is "canceled" or "on_hold" or "expired" or "trial_ended" or "unpaid" or "suspended")
        {
            throw new BillingException(409, "A subscription in state '" + state + "' is not billed, so it has no billing date to change.");
        }

        // Keep the time of day and offset of the existing billing moment, only change the day.
        var existing = DateTimeOffset.Parse(current, CultureInfo.InvariantCulture);
        var target = new DateTimeOffset(date.Value.Date + existing.TimeOfDay, existing.Offset);
        if (target <= DateTimeOffset.UtcNow)
        {
            throw new BillingException(400, "The next billing date must be in the future.");
        }

        var updated = await _maxio.PutAsync("subscriptions/" + id + ".json", new JsonObject
        {
            ["subscription"] = new JsonObject { ["next_billing_at"] = target.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture) }
        });
        return await ToSubscriptionAsync(updated!["subscription"]!);
    }
}
