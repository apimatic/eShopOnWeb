using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public record PlanDto(long Id, string Handle, string Name, decimal MonthlyPrice, string Currency, string Brand);

public record SubscriptionDto(long Id, string State, string PlanHandle, string PlanName, bool Paid,
    decimal BalanceDue, string Currency, DateTimeOffset? NextBillingAt, DateTimeOffset? CanceledAt);

public record UsageItemDto(long ComponentId, string Name, string UnitName, decimal Quantity);

public record UsageDto(long SubscriptionId, DateOnly Date, decimal TotalQuantity, IReadOnlyList<UsageItemDto> Items);

/// <summary>
/// Billing rules on top of the Maxio HTTP API. Maxio is the system of record; nothing is stored locally.
/// A shopper is a Maxio customer whose reference is the shopper's user name (taken from the JWT).
/// </summary>
public class BillingService
{
    private static readonly string[] PausableStates = { "active", "trialing", "past_due", "unpaid", "soft_failure" };

    private readonly MaxioClient _maxio;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IReadRepository<CatalogBrand> _brands;

    public BillingService(MaxioClient maxio, UserManager<ApplicationUser> userManager, IReadRepository<CatalogBrand> brands)
    {
        _maxio = maxio;
        _userManager = userManager;
        _brands = brands;
    }

    // ---------- helpers ----------

    private static MaxioApiException Bad(string message) => new(400, message);
    private static MaxioApiException Conflict(string message) => new(409, message);
    private static MaxioApiException Unprocessable(string message) => new(422, message);
    private static MaxioApiException NotFound(string message) => new(404, message);

    private static string Esc(string s) => Uri.EscapeDataString(s);

    private static decimal Dec(JsonNode? n)
    {
        if (n is JsonValue v)
        {
            if (v.TryGetValue<decimal>(out var d)) return d;
            if (v.TryGetValue<string>(out var s) && decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out d)) return d;
        }
        return 0m;
    }

    private static decimal Cents(JsonNode? n) => Dec(n) / 100m;

    private static long? Long(JsonNode? n)
    {
        if (n is JsonValue v)
        {
            if (v.TryGetValue<long>(out var l)) return l;
            if (v.TryGetValue<string>(out var s) && long.TryParse(s, out l)) return l;
        }
        return null;
    }

    private static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static DateTimeOffset? Date(JsonNode? n) =>
        Str(n) is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static string BrandFromFamily(string family) =>
        family.EndsWith(" plans", StringComparison.OrdinalIgnoreCase) ? family[..^6] : family;

    private static PlanDto ToPlan(JsonNode p) => new(
        Long(p["id"]) ?? 0,
        Str(p["handle"]) ?? "",
        Str(p["name"]) ?? "",
        Cents(p["price_in_cents"]),
        "USD",
        BrandFromFamily(Str(p["product_family"]?["name"]) ?? ""));

    private static bool IsSubscribable(JsonNode p) =>
        !string.IsNullOrEmpty(Str(p["handle"])) &&
        p["archived_at"] == null &&
        p["require_credit_card"]?.GetValue<bool>() != true;

    private static string State(JsonNode sub) => Str(sub["state"]) ?? "";

    // ---------- plans ----------

    public async Task<IReadOnlyList<PlanDto>> ListPlansAsync()
    {
        var items = await _maxio.GetAllPagesAsync("products.json");
        return items.Select(i => i["product"]).Where(p => p != null && IsSubscribable(p!)).Select(p => ToPlan(p!)).ToList();
    }

    private async Task<JsonNode> GetSubscribablePlanAsync(string? handle)
    {
        if (string.IsNullOrWhiteSpace(handle)) throw Bad("A plan handle is required.");
        var node = await _maxio.GetAsync($"products/handle/{Esc(handle.Trim())}.json", nullOn404: true);
        var product = node?["product"];
        if (product == null) throw NotFound($"No plan with handle '{handle}'.");
        if (product["archived_at"] != null) throw Unprocessable($"Plan '{handle}' is no longer available.");
        if (product["require_credit_card"]?.GetValue<bool>() == true)
            throw Unprocessable($"Plan '{handle}' requires card details and cannot be subscribed to without them.");
        return product;
    }

    // ---------- shopper subscriptions ----------

    private async Task<JsonNode> GetOwnedAsync(string userName, long id)
    {
        var node = await _maxio.GetAsync($"subscriptions/{id}.json", nullOn404: true);
        var sub = node?["subscription"];
        var reference = Str(sub?["customer"]?["reference"]);
        if (sub == null || !string.Equals(reference, userName, StringComparison.OrdinalIgnoreCase))
        {
            // Do not reveal other shoppers' subscriptions.
            throw NotFound($"Subscription {id} was not found.");
        }
        return sub;
    }

    private async Task<SubscriptionDto> ToDtoAsync(JsonNode sub)
    {
        var id = Long(sub["id"]) ?? 0;
        var owed = Math.Max(Cents(sub["balance_in_cents"]), 0);
        var invoices = await _maxio.GetAllPagesAsync($"invoices.json?subscription_id={id}&status=open");
        var invoiceDue = invoices.Sum(i => Math.Max(Dec(i["due_amount"]), 0));
        owed = Math.Max(owed, invoiceDue);
        return new SubscriptionDto(id, State(sub), Str(sub["product"]?["handle"]) ?? "",
            Str(sub["product"]?["name"]) ?? "", owed <= 0, owed, Str(sub["currency"]) ?? "USD",
            Date(sub["next_assessment_at"]), Date(sub["canceled_at"]));
    }

    public async Task<SubscriptionDto> SubscribeAsync(string userName, string? planHandle)
    {
        var plan = await GetSubscribablePlanAsync(planHandle);
        var customer = await _maxio.GetAsync($"customers/lookup.json?reference={Esc(userName)}", nullOn404: true);
        var customerId = Long(customer?["customer"]?["id"]);

        var sub = new JsonObject
        {
            ["product_handle"] = Str(plan["handle"]),
            // No card is collected: the shopper is invoiced.
            ["payment_collection_method"] = "remittance",
        };
        if (customerId != null)
        {
            sub["customer_id"] = customerId;
        }
        else
        {
            var user = await _userManager.FindByNameAsync(userName);
            var email = user?.Email ?? userName;
            sub["customer_attributes"] = new JsonObject
            {
                ["first_name"] = email.Split('@')[0],
                ["last_name"] = "Shopper",
                ["email"] = email,
                ["reference"] = userName,
            };
        }
        var created = await _maxio.SendAsync(HttpMethod.Post, "subscriptions.json", new JsonObject { ["subscription"] = sub });
        return await ToDtoAsync(created!["subscription"]!);
    }

    public async Task<SubscriptionDto> GetAsync(string userName, long id) => await ToDtoAsync(await GetOwnedAsync(userName, id));

    public async Task<SubscriptionDto> PauseAsync(string userName, long id)
    {
        var sub = await GetOwnedAsync(userName, id);
        var state = State(sub);
        if (state == "on_hold") throw Conflict("The subscription is already paused.");
        if (!PausableStates.Contains(state)) throw Conflict($"A subscription that is {state.Replace('_', ' ')} cannot be paused.");
        var r = await _maxio.SendAsync(HttpMethod.Post, $"subscriptions/{id}/hold.json");
        return await ToDtoAsync(r!["subscription"]!);
    }

    public async Task<SubscriptionDto> ResumeAsync(string userName, long id)
    {
        var sub = await GetOwnedAsync(userName, id);
        if (State(sub) != "on_hold") throw Conflict("Only a paused subscription can be resumed.");
        var r = await _maxio.SendAsync(HttpMethod.Post, $"subscriptions/{id}/resume.json");
        return await ToDtoAsync(r!["subscription"]!);
    }

    public async Task<SubscriptionDto> CancelAsync(string userName, long id)
    {
        var sub = await GetOwnedAsync(userName, id);
        var state = State(sub);
        if (state == "canceled") throw Conflict("The subscription is already cancelled.");
        if (state is "expired" or "purged") throw Conflict($"A {state} subscription cannot be cancelled.");
        var r = await _maxio.SendAsync(HttpMethod.Delete, $"subscriptions/{id}.json");
        return await ToDtoAsync(r!["subscription"]!);
    }

    public async Task<SubscriptionDto> RestartAsync(string userName, long id)
    {
        var sub = await GetOwnedAsync(userName, id);
        if (State(sub) != "canceled") throw Conflict("Only a cancelled subscription can be restarted.");
        var r = await _maxio.SendAsync(HttpMethod.Put, $"subscriptions/{id}/reactivate.json");
        return await ToDtoAsync(r!["subscription"]!);
    }

    public async Task<SubscriptionDto> SwitchAsync(string userName, long id, string? planHandle)
    {
        var sub = await GetOwnedAsync(userName, id);
        var plan = await GetSubscribablePlanAsync(planHandle);
        var newHandle = Str(plan["handle"])!;
        var state = State(sub);
        if (string.Equals(Str(sub["product"]?["handle"]), newHandle, StringComparison.OrdinalIgnoreCase))
            throw Conflict("The subscription is already on that plan.");
        if (state is "expired" or "purged") throw Conflict($"A {state} subscription cannot be switched.");

        // Maxio only migrates active subscriptions: bring a cancelled / paused one back first,
        // and put it back the way it was if the migration fails.
        var wasCanceled = state == "canceled";
        var wasPaused = state == "on_hold";
        if (wasCanceled) await _maxio.SendAsync(HttpMethod.Put, $"subscriptions/{id}/reactivate.json");
        else if (wasPaused) await _maxio.SendAsync(HttpMethod.Post, $"subscriptions/{id}/resume.json");

        try
        {
            await _maxio.SendAsync(HttpMethod.Post, $"subscriptions/{id}/migrations.json",
                new JsonObject { ["migration"] = new JsonObject { ["product_handle"] = newHandle } });
        }
        catch
        {
            try
            {
                if (wasCanceled) await _maxio.SendAsync(HttpMethod.Delete, $"subscriptions/{id}.json");
                else if (wasPaused) await _maxio.SendAsync(HttpMethod.Post, $"subscriptions/{id}/hold.json");
            }
            catch { /* best effort */ }
            throw;
        }

        if (wasPaused)
        {
            await _maxio.SendAsync(HttpMethod.Post, $"subscriptions/{id}/hold.json");
        }
        var fresh = await _maxio.GetAsync($"subscriptions/{id}.json");
        return await ToDtoAsync(fresh!["subscription"]!);
    }

    public async Task<UsageDto> UsageAsync(string userName, long id, string? dateText)
    {
        if (!DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            throw Bad("The 'date' query parameter is required, in the form YYYY-MM-DD.");
        await GetOwnedAsync(userName, id);

        var dayText = day.ToString("yyyy-MM-dd");
        var comps = await _maxio.GetAllPagesAsync($"subscriptions/{id}/components.json");
        var items = new List<UsageItemDto>();
        foreach (var c in comps)
        {
            var comp = c["component"];
            if (comp == null || Str(comp["kind"]) != "metered_component") continue;
            var cid = Long(comp["component_id"]) ?? 0;
            // Fetch a window around the day and select by the day on Maxio's own timestamps,
            // so the result does not depend on how the filter treats its boundaries.
            var since = day.AddDays(-1).ToString("yyyy-MM-dd");
            var until = day.AddDays(2).ToString("yyyy-MM-dd");
            var usages = await _maxio.GetAllPagesAsync($"subscriptions/{id}/components/{cid}/usages.json?since_date={since}&until_date={until}");
            decimal qty = 0;
            foreach (var u in usages)
            {
                var usage = u["usage"];
                var created = Str(usage?["created_at"]);
                if (created == null || created.Length < 10 || created[..10] != dayText) continue;
                qty += Dec(usage!["quantity"]);
            }
            items.Add(new UsageItemDto(cid, Str(comp["name"]) ?? "", Str(comp["unit_name"]) ?? "", qty));
        }
        return new UsageDto(id, day, items.Sum(i => i.Quantity), items);
    }

    // ---------- staff ----------

    public async Task<PlanDto> CreatePlanAsync(string? name, decimal? price, string? brand)
    {
        if (string.IsNullOrWhiteSpace(name)) throw Bad("The plan name is required.");
        if (price == null || price < 0 || decimal.Round(price.Value, 2) != price.Value)
            throw Bad("The monthly price is required and must be zero or more, with at most two decimals.");
        if (string.IsNullOrWhiteSpace(brand)) throw Bad("The brand is required.");
        var handle = Regex.Replace(name.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (handle.Length == 0) throw Bad("The plan name must contain letters or digits.");

        var catalogBrand = (await _brands.ListAsync())
            .FirstOrDefault(b => string.Equals(b.Brand, brand.Trim(), StringComparison.OrdinalIgnoreCase));
        if (catalogBrand == null) throw Unprocessable($"'{brand}' is not a brand in the catalog.");

        if (await _maxio.GetAsync($"products/handle/{Esc(handle)}.json", nullOn404: true) != null)
            throw Conflict($"A plan with handle '{handle}' already exists.");

        // Each catalog brand maps to one Maxio product family, named "<Brand> plans".
        var familyName = catalogBrand.Brand + " plans";
        var families = await _maxio.GetAsync("product_families.json") as JsonArray;
        var family = families?.Select(f => f?["product_family"])
            .FirstOrDefault(f => f != null && f["archived_at"] == null &&
                                 string.Equals(Str(f["name"]), familyName, StringComparison.OrdinalIgnoreCase));
        var familyId = Long(family?["id"]);
        if (familyId == null)
        {
            var made = await _maxio.SendAsync(HttpMethod.Post, "product_families.json",
                new JsonObject { ["product_family"] = new JsonObject { ["name"] = familyName } });
            familyId = Long(made?["product_family"]?["id"]);
        }

        var created = await _maxio.SendAsync(HttpMethod.Post, $"product_families/{familyId}/products.json",
            new JsonObject
            {
                ["product"] = new JsonObject
                {
                    ["name"] = name.Trim(),
                    ["handle"] = handle,
                    ["price_in_cents"] = (long)(price.Value * 100m),
                    ["interval"] = 1,
                    ["interval_unit"] = "month",
                    // Shoppers subscribe without card details.
                    ["require_credit_card"] = false,
                    ["request_credit_card"] = false,
                }
            });
        return ToPlan(created!["product"]!);
    }

    public async Task<object> AddCurrencyPriceAsync(long planId, string? currency, decimal? price)
    {
        if (string.IsNullOrWhiteSpace(currency) || !Regex.IsMatch(currency.Trim(), "^[A-Za-z]{3}$"))
            throw Bad("The currency must be a three-letter code such as EUR.");
        if (price == null || price < 0 || decimal.Round(price.Value, 2) != price.Value)
            throw Bad("The price is required and must be zero or more, with at most two decimals.");
        var code = currency.Trim().ToUpperInvariant();

        var product = (await _maxio.GetAsync($"products/{planId}.json", nullOn404: true))?["product"];
        if (product == null) throw NotFound($"Plan {planId} was not found.");

        var site = (await _maxio.GetAsync("site.json"))?["site"];
        var primary = (Str(site?["currency"]) ?? "USD").ToUpperInvariant();
        var others = (site?["non_primary_currencies"] as JsonArray)?
            .Select(Str).Where(c => c != null).Select(c => c!.ToUpperInvariant()).ToList() ?? new List<string>();
        if (code == primary)
            throw Unprocessable($"{code} is the plan's main currency; its price is set on the plan itself.");
        if (!others.Contains(code))
            throw Unprocessable($"{code} is not enabled for the billing site (enabled: {string.Join(", ", others.Prepend(primary))}).");

        var pricePointId = Long(product["default_product_price_point_id"]) ?? Long(product["product_price_point_id"]);
        if (pricePointId == null) throw Unprocessable("The plan has no price point to price.");

        await _maxio.SendAsync(HttpMethod.Post, $"product_price_points/{pricePointId}/currency_prices.json",
            new JsonObject
            {
                ["currency_prices"] = new JsonArray(new JsonObject { ["currency"] = code, ["price"] = price.Value })
            });
        return new { planId, currency = code, price = price.Value };
    }

    public async Task<SubscriptionDto> SetNextBillingDateAsync(long id, string? dateText)
    {
        if (!DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            throw Bad("The date is required, in the form YYYY-MM-DD.");
        var sub = (await _maxio.GetAsync($"subscriptions/{id}.json", nullOn404: true))?["subscription"];
        if (sub == null) throw NotFound($"Subscription {id} was not found.");
        var state = State(sub);
        if (state is "canceled" or "expired" or "purged" or "on_hold")
            throw Conflict($"A {state.Replace('_', ' ')} subscription is not billed; restart or resume it first.");

        // Keep the billing time of day and offset Maxio already uses, so the date is the site's own date.
        var current = Date(sub["next_assessment_at"]) ?? throw Unprocessable("The subscription has no scheduled billing.");
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(current.Offset).DateTime);
        if (day <= today) throw Bad("The next billing date must be in the future.");
        var target = new DateTimeOffset(day.Year, day.Month, day.Day, current.Hour, current.Minute, current.Second, current.Offset);

        var r = await _maxio.SendAsync(HttpMethod.Put, $"subscriptions/{id}.json",
            new JsonObject { ["subscription"] = new JsonObject { ["next_billing_at"] = target.ToString("o") } });
        return await ToDtoAsync(r!["subscription"]!);
    }
}
