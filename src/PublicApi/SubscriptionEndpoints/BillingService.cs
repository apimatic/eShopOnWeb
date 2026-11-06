using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscription billing use cases. Maxio is the system of record; nothing is stored locally.
/// A shopper's Maxio customer is identified by the customer reference = the JWT user name.
/// </summary>
public class BillingService
{
    private const string BRAND_FAMILY_SUFFIX = " plans";
    private readonly MaxioClient _maxio;
    private readonly IReadRepository<CatalogBrand> _brands;

    public BillingService(MaxioClient maxio, IReadRepository<CatalogBrand> brands)
    {
        _maxio = maxio;
        _brands = brands;
    }

    private static MaxioApiException Fail(HttpStatusCode status, string message) => new(status, message);

    // ---------- Plans ----------

    public async Task<List<PlanDto>> ListPlansAsync()
    {
        var products = await _maxio.ListAllAsync("products.json");
        return products
            .Select(p => p["product"])
            .Where(p => p != null && p["archived_at"] == null && !string.IsNullOrWhiteSpace(Str(p["handle"])))
            .Select(p => ToPlan(p!))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Id)
            .ToList();
    }

    private async Task<JsonNode> GetSubscribablePlanAsync(string? handle)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw Fail(HttpStatusCode.BadRequest, "The plan handle is required.");
        }
        var node = await _maxio.SendOrNullAsync(HttpMethod.Get, $"products/handle/{Uri.EscapeDataString(handle.Trim())}.json");
        var product = node?["product"];
        if (product == null || product["archived_at"] != null)
        {
            throw Fail(HttpStatusCode.NotFound, $"There is no plan with handle '{handle}'.");
        }
        return product;
    }

    private static PlanDto ToPlan(JsonNode p)
    {
        var family = Str(p["product_family"]?["name"]);
        if (family != null && family.EndsWith(BRAND_FAMILY_SUFFIX, StringComparison.OrdinalIgnoreCase))
        {
            family = family[..^BRAND_FAMILY_SUFFIX.Length];
        }
        return new PlanDto
        {
            Id = Long(p["id"]),
            Handle = Str(p["handle"]) ?? string.Empty,
            Name = Str(p["name"]) ?? string.Empty,
            Description = Str(p["description"]),
            Brand = family,
            MonthlyPrice = Long(p["price_in_cents"]) / 100m,
            Interval = (int)Long(p["interval"]),
            IntervalUnit = Str(p["interval_unit"]) ?? string.Empty
        };
    }

    // ---------- Subscriptions ----------

    public async Task<SubscriptionDto> SubscribeAsync(string userName, string? planHandle)
    {
        var plan = await GetSubscribablePlanAsync(planHandle);
        var customerId = await EnsureCustomerAsync(userName);
        var body = new JsonObject
        {
            ["subscription"] = new JsonObject
            {
                ["product_handle"] = Str(plan["handle"]),
                ["customer_id"] = customerId,
                // No card is collected: the shopper is invoiced instead.
                ["payment_collection_method"] = "remittance"
            }
        };
        var created = await _maxio.SendAsync(HttpMethod.Post, "subscriptions.json", body);
        return ToSubscription(created["subscription"]!);
    }

    private async Task<long> EnsureCustomerAsync(string userName)
    {
        var existing = await FindCustomerIdAsync(userName);
        if (existing != null)
        {
            return existing.Value;
        }

        var local = userName.Split('@')[0];
        var body = new JsonObject
        {
            ["customer"] = new JsonObject
            {
                ["first_name"] = string.IsNullOrWhiteSpace(local) ? "Shopper" : local,
                ["last_name"] = "Shopper",
                ["email"] = userName.Contains('@') ? userName : $"{Uri.EscapeDataString(userName)}@example.invalid",
                ["reference"] = userName
            }
        };
        try
        {
            var created = await _maxio.SendAsync(HttpMethod.Post, "customers.json", body);
            return Long(created["customer"]?["id"]);
        }
        catch (MaxioApiException)
        {
            // Lost a race with a concurrent request creating the same customer?
            var again = await FindCustomerIdAsync(userName);
            if (again != null)
            {
                return again.Value;
            }
            throw;
        }
    }

    private async Task<long?> FindCustomerIdAsync(string userName)
    {
        var node = await _maxio.SendOrNullAsync(HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(userName)}");
        var id = node?["customer"]?["id"];
        return id == null ? null : Long(id);
    }

    /// <summary>Loads a subscription and makes sure it belongs to the shopper; otherwise it "does not exist".</summary>
    private async Task<JsonNode> GetOwnedAsync(string userName, long subscriptionId)
    {
        var node = await _maxio.SendOrNullAsync(HttpMethod.Get, $"subscriptions/{subscriptionId}.json");
        var sub = node?["subscription"];
        var reference = Str(sub?["customer"]?["reference"]);
        if (sub == null || !string.Equals(reference, userName, StringComparison.OrdinalIgnoreCase))
        {
            throw Fail(HttpStatusCode.NotFound, $"Subscription {subscriptionId} was not found.");
        }
        return sub;
    }

    public async Task<SubscriptionDto> GetAsync(string userName, long subscriptionId)
        => ToSubscription(await GetOwnedAsync(userName, subscriptionId));

    public async Task<SubscriptionDto> PauseAsync(string userName, long subscriptionId)
    {
        var sub = await GetOwnedAsync(userName, subscriptionId);
        var state = State(sub);
        if (state == "on_hold")
        {
            throw Fail(HttpStatusCode.Conflict, "The subscription is already paused.");
        }
        if (state == "canceled")
        {
            throw Fail(HttpStatusCode.Conflict, "A cancelled subscription cannot be paused.");
        }
        return await ActAsync(HttpMethod.Post, $"subscriptions/{subscriptionId}/hold.json");
    }

    public async Task<SubscriptionDto> ResumeAsync(string userName, long subscriptionId)
    {
        var sub = await GetOwnedAsync(userName, subscriptionId);
        if (State(sub) != "on_hold")
        {
            throw Fail(HttpStatusCode.Conflict, "Only a paused subscription can be resumed.");
        }
        return await ActAsync(HttpMethod.Post, $"subscriptions/{subscriptionId}/resume.json");
    }

    public async Task<SubscriptionDto> CancelAsync(string userName, long subscriptionId)
    {
        var sub = await GetOwnedAsync(userName, subscriptionId);
        if (State(sub) == "canceled")
        {
            throw Fail(HttpStatusCode.Conflict, "The subscription is already cancelled.");
        }
        return await ActAsync(HttpMethod.Delete, $"subscriptions/{subscriptionId}.json");
    }

    public async Task<SubscriptionDto> RestartAsync(string userName, long subscriptionId)
    {
        var sub = await GetOwnedAsync(userName, subscriptionId);
        if (State(sub) != "canceled")
        {
            throw Fail(HttpStatusCode.Conflict, "Only a cancelled subscription can be restarted.");
        }
        return await ActAsync(HttpMethod.Put, $"subscriptions/{subscriptionId}/reactivate.json");
    }

    public async Task<SubscriptionDto> SwitchPlanAsync(string userName, long subscriptionId, string? planHandle)
    {
        var sub = await GetOwnedAsync(userName, subscriptionId);
        var plan = await GetSubscribablePlanAsync(planHandle);
        var newHandle = Str(plan["handle"])!;
        if (string.Equals(Str(sub["product"]?["handle"]), newHandle, StringComparison.OrdinalIgnoreCase)
            && State(sub) != "canceled")
        {
            throw Fail(HttpStatusCode.Conflict, "The subscription is already on that plan.");
        }

        // Maxio migrates only active subscriptions: bring a cancelled or paused one back first.
        var state = State(sub);
        var wasCanceled = state == "canceled";
        var wasPaused = state == "on_hold";
        if (wasCanceled)
        {
            await ActAsync(HttpMethod.Put, $"subscriptions/{subscriptionId}/reactivate.json");
        }
        else if (wasPaused)
        {
            await ActAsync(HttpMethod.Post, $"subscriptions/{subscriptionId}/resume.json");
        }

        JsonNode migrated;
        try
        {
            var body = new JsonObject { ["migration"] = new JsonObject { ["product_handle"] = newHandle } };
            migrated = await _maxio.SendAsync(HttpMethod.Post, $"subscriptions/{subscriptionId}/migrations.json", body);
        }
        catch (MaxioApiException)
        {
            await RestoreAsync(subscriptionId, wasCanceled, wasPaused);
            throw;
        }

        if (wasPaused)
        {
            // A paused subscription stays paused, on its new plan.
            return await ActAsync(HttpMethod.Post, $"subscriptions/{subscriptionId}/hold.json");
        }
        return ToSubscription(migrated["subscription"]!);
    }

    private async Task RestoreAsync(long subscriptionId, bool wasCanceled, bool wasPaused)
    {
        try
        {
            if (wasCanceled)
            {
                await _maxio.SendOrNullAsync(HttpMethod.Delete, $"subscriptions/{subscriptionId}.json", new JsonObject());
            }
            else if (wasPaused)
            {
                await _maxio.SendOrNullAsync(HttpMethod.Post, $"subscriptions/{subscriptionId}/hold.json", new JsonObject());
            }
        }
        catch (MaxioApiException)
        {
            // Best effort: the original failure is the one worth reporting.
        }
    }

    private async Task<SubscriptionDto> ActAsync(HttpMethod method, string path)
    {
        var result = await _maxio.SendAsync(method, path, new JsonObject());
        return ToSubscription(result["subscription"]!);
    }

    // ---------- Usage ----------

    public async Task<UsageResponse> GetUsageAsync(string userName, long subscriptionId, string? date)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            throw Fail(HttpStatusCode.BadRequest, "The 'date' query parameter is required in the form YYYY-MM-DD.");
        }
        await GetOwnedAsync(userName, subscriptionId);

        var response = new UsageResponse { SubscriptionId = subscriptionId, Date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        var components = await _maxio.ListAllAsync($"subscriptions/{subscriptionId}/components.json");
        foreach (var wrapper in components)
        {
            var component = wrapper["component"];
            if (component == null || Str(component["kind"]) != "metered_component")
            {
                continue;
            }
            var componentId = Long(component["component_id"]);
            // The filter is a coarse window; the exact day is decided below from each record's timestamp.
            var from = day.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var until = day.AddDays(2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var usages = await _maxio.ListAllAsync(
                $"subscriptions/{subscriptionId}/components/{componentId}/usages.json?since_date={from}&until_date={until}");

            var dto = new ComponentUsageDto
            {
                ComponentId = componentId,
                Handle = Str(component["component_handle"]),
                Name = Str(component["name"]) ?? string.Empty,
                UnitName = Str(component["unit_name"])
            };
            foreach (var u in usages.Select(w => w["usage"]).Where(u => u != null))
            {
                var recordedAt = DateTimeOffset.Parse(Str(u!["created_at"])!, CultureInfo.InvariantCulture);
                if (DateOnly.FromDateTime(recordedAt.DateTime) != day)
                {
                    continue;
                }
                var quantity = Dec(u["quantity"]);
                dto.Records.Add(new UsageRecordDto { Id = Long(u["id"]), Quantity = quantity, Memo = Str(u["memo"]), RecordedAt = recordedAt });
                dto.Quantity += quantity;
            }
            response.Components.Add(dto);
        }
        return response;
    }

    // ---------- Staff ----------

    public async Task<PlanDto> CreatePlanAsync(CreatePlanRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw Fail(HttpStatusCode.BadRequest, "The plan name is required.");
        }
        if (request.MonthlyPrice is null || request.MonthlyPrice < 0 || decimal.Round(request.MonthlyPrice.Value, 2) != request.MonthlyPrice)
        {
            throw Fail(HttpStatusCode.BadRequest, "The monthly price must be zero or more, with at most two decimals.");
        }
        if (string.IsNullOrWhiteSpace(request.Brand))
        {
            throw Fail(HttpStatusCode.BadRequest, "The brand is required.");
        }

        var brand = (await _brands.ListAsync())
            .FirstOrDefault(b => string.Equals(b.Brand, request.Brand.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw Fail(HttpStatusCode.UnprocessableEntity, $"'{request.Brand}' is not a brand in the catalog.");

        var name = request.Name.Trim();
        var handle = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (handle.Length == 0)
        {
            throw Fail(HttpStatusCode.BadRequest, "The plan name must contain letters or digits.");
        }
        if (await _maxio.SendOrNullAsync(HttpMethod.Get, $"products/handle/{handle}.json") != null)
        {
            throw Fail(HttpStatusCode.Conflict, $"A plan with handle '{handle}' already exists.");
        }

        var familyId = await EnsureFamilyAsync(brand.Brand);
        var body = new JsonObject
        {
            ["product"] = new JsonObject
            {
                ["name"] = name,
                ["handle"] = handle,
                ["price_in_cents"] = (long)Math.Round(request.MonthlyPrice.Value * 100m),
                ["interval"] = 1,
                ["interval_unit"] = "month",
                // Shoppers subscribe without card details.
                ["request_credit_card"] = false,
                ["require_credit_card"] = false
            }
        };
        var created = await _maxio.SendAsync(HttpMethod.Post, $"product_families/{familyId}/products.json", body);
        return ToPlan(created["product"]!);
    }

    private async Task<long> EnsureFamilyAsync(string brand)
    {
        var familyName = brand + BRAND_FAMILY_SUFFIX;
        var families = await _maxio.ListAllAsync("product_families.json");
        var match = families
            .Select(f => f["product_family"])
            .FirstOrDefault(f => f != null && f["archived_at"] == null
                                 && string.Equals(Str(f["name"]), familyName, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            return Long(match["id"]);
        }

        var handle = Regex.Replace(familyName.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        var body = new JsonObject { ["product_family"] = new JsonObject { ["name"] = familyName, ["handle"] = handle } };
        var created = await _maxio.SendAsync(HttpMethod.Post, "product_families.json", body);
        return Long(created["product_family"]?["id"]);
    }

    public async Task<CurrencyPriceDto> AddCurrencyPriceAsync(long planId, CurrencyPriceRequest request)
    {
        var currency = request.Currency?.Trim().ToUpperInvariant();
        if (currency == null || !Regex.IsMatch(currency, "^[A-Z]{3}$"))
        {
            throw Fail(HttpStatusCode.BadRequest, "The currency must be a three-letter ISO code.");
        }
        if (request.Price is null || request.Price <= 0)
        {
            throw Fail(HttpStatusCode.BadRequest, "The price must be greater than zero.");
        }

        var node = await _maxio.SendAsync(HttpMethod.Get, $"products/{planId}.json", notFound: $"Plan {planId} was not found.");
        var product = node["product"]!;
        var pricePointId = Long(product["product_price_point_id"] ?? product["default_product_price_point_id"]);

        var body = new JsonObject
        {
            ["currency_prices"] = new JsonArray(new JsonObject
            {
                ["currency"] = currency,
                ["price"] = request.Price,
                ["role"] = "baseline"
            })
        };
        var result = await _maxio.SendAsync(HttpMethod.Post, $"product_price_points/{pricePointId}/currency_prices.json", body);
        var price = (result["currency_prices"] as JsonArray)?
            .FirstOrDefault(p => string.Equals(Str(p?["currency"]), currency, StringComparison.OrdinalIgnoreCase));
        return new CurrencyPriceDto { PlanId = planId, Currency = currency, Price = price != null ? Dec(price["price"]) : request.Price.Value };
    }

    private static readonly HashSet<string> Billable = new() { "active", "trialing", "past_due", "unpaid", "soft_failure" };

    public async Task<SubscriptionDto> SetNextBillingDateAsync(long subscriptionId, DateOnly? date)
    {
        if (date is null)
        {
            throw Fail(HttpStatusCode.BadRequest, "The date is required in the form YYYY-MM-DD.");
        }
        if (date.Value <= DateOnly.FromDateTime(DateTime.UtcNow))
        {
            throw Fail(HttpStatusCode.BadRequest, "The next billing date must be in the future.");
        }

        var node = await _maxio.SendAsync(HttpMethod.Get, $"subscriptions/{subscriptionId}.json", notFound: $"Subscription {subscriptionId} was not found.");
        var state = State(node["subscription"]!);
        if (!Billable.Contains(state))
        {
            throw Fail(HttpStatusCode.Conflict, $"A subscription in state '{state}' is not billed, so it has no billing date to change.");
        }

        var iso = date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var body = new JsonObject { ["subscription"] = new JsonObject { ["next_billing_at"] = iso } };
        var updated = await _maxio.SendAsync(HttpMethod.Put, $"subscriptions/{subscriptionId}.json", body);
        var dto = ToSubscription(updated["subscription"]!);
        if (dto.NextBillingAt == null || DateOnly.FromDateTime(dto.NextBillingAt.Value.DateTime) != date.Value)
        {
            throw Fail(HttpStatusCode.UnprocessableEntity, "The billing system did not apply the requested billing date.");
        }
        return dto;
    }

    // ---------- Mapping helpers ----------

    private static SubscriptionDto ToSubscription(JsonNode s)
    {
        var state = State(s);
        var balance = Long(s["balance_in_cents"]);
        var product = s["product"];
        return new SubscriptionDto
        {
            Id = Long(s["id"]),
            State = state,
            PlanId = Long(product?["id"]),
            PlanHandle = Str(product?["handle"]) ?? string.Empty,
            PlanName = Str(product?["name"]) ?? string.Empty,
            Paid = balance <= 0 && state is not ("past_due" or "unpaid" or "soft_failure"),
            BalanceDue = Math.Max(balance, 0) / 100m,
            Currency = Str(s["currency"]),
            NextBillingAt = Date(s["next_assessment_at"]),
            CanceledAt = Date(s["canceled_at"])
        };
    }

    private static string State(JsonNode s) => Str(s["state"]) ?? string.Empty;
    private static string? Str(JsonNode? n) => n is JsonValue v ? v.ToString() : null;
    private static long Long(JsonNode? n) => n == null ? 0 : (long)Math.Round(Dec(n));
    private static decimal Dec(JsonNode? n)
        => n == null ? 0 : decimal.Parse(n.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
    private static DateTimeOffset? Date(JsonNode? n)
        => Str(n) is { } s ? DateTimeOffset.Parse(s, CultureInfo.InvariantCulture) : null;
}
