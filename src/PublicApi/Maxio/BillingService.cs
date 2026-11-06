using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public record PlanDto(long Id, string Handle, string Name, string? Description, string? Brand, decimal Price, string Currency, int Interval, string IntervalUnit);

public record SubscriptionDto(
    long Id, string State, string PlanHandle, string PlanName, decimal PlanPrice, string Currency,
    bool Paid, decimal BalanceDue, DateTimeOffset? NextBillingAt, DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? CanceledAt, DateTimeOffset? CreatedAt);

public record UsageComponentDto(long ComponentId, string? Handle, string Name, string? UnitName, decimal Quantity);

public record UsageDto(long SubscriptionId, string Date, decimal TotalQuantity, List<UsageComponentDto> Components);

public record CurrencyPriceDto(long PlanId, string Currency, decimal Price);

/// <summary>
/// Subscription billing logic. Maxio is the system of record: nothing is persisted locally.
/// </summary>
public partial class BillingService
{
    private const string BRAND_PREFIX = "Brand: ";
    private static readonly string[] CHANGEABLE_STATES = { "active", "trialing", "past_due", "unpaid", "soft_failure", "trial_ended" };

    private readonly MaxioClient _maxio;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IReadRepository<CatalogBrand> _brands;

    public BillingService(MaxioClient maxio, UserManager<ApplicationUser> userManager, IReadRepository<CatalogBrand> brands)
    {
        _maxio = maxio;
        _userManager = userManager;
        _brands = brands;
    }

    // ---------- Plans ----------

    public async Task<List<PlanDto>> ListPlansAsync(CancellationToken ct)
    {
        var products = await _maxio.GetAllAsync("products.json", null, 200, ct);
        return products
            .Select(p => p.GetProperty("product"))
            .Where(p => IsNull(p, "archived_at"))
            .Select(ToPlan)
            .ToList();
    }

    private async Task<JsonElement> GetPlanByHandleAsync(string handle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new ApiException(400, "A plan handle is required.");
        }

        JsonElement json;
        try
        {
            json = await _maxio.GetAsync($"products/handle/{Uri.EscapeDataString(handle.Trim())}.json", ct);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            throw new ApiException(404, $"Plan '{handle}' does not exist.");
        }

        var product = json.GetProperty("product");
        if (!IsNull(product, "archived_at"))
        {
            throw new ApiException(404, $"Plan '{handle}' is no longer available.");
        }

        return product;
    }

    // ---------- Subscriptions (shopper) ----------

    public async Task<SubscriptionDto> SubscribeAsync(string username, string planHandle, CancellationToken ct)
    {
        var plan = await GetPlanByHandleAsync(planHandle, ct);
        var customerId = await GetOrCreateCustomerAsync(username, ct);

        var created = await _maxio.PostAsync("subscriptions.json", new
        {
            subscription = new
            {
                product_handle = plan.GetProperty("handle").GetString(),
                customer_id = customerId,
                // No card on file: the shopper is invoiced instead.
                payment_collection_method = "remittance"
            }
        }, ct);

        return await ToSubscriptionAsync(created.GetProperty("subscription"), ct);
    }

    public async Task<SubscriptionDto> GetSubscriptionAsync(string username, long id, CancellationToken ct)
    {
        return await ToSubscriptionAsync(await GetOwnedAsync(username, id, ct), ct);
    }

    public async Task<SubscriptionDto> PauseAsync(string username, long id, CancellationToken ct)
    {
        var sub = await GetOwnedAsync(username, id, ct);
        var state = State(sub);
        if (state == "on_hold") throw new ApiException(409, "The subscription is already paused.");
        RequireState(state, CHANGEABLE_STATES, "paused");

        var result = await _maxio.PostAsync($"subscriptions/{id}/hold.json", new { }, ct);
        return await ToSubscriptionAsync(result.GetProperty("subscription"), ct);
    }

    public async Task<SubscriptionDto> ResumeAsync(string username, long id, CancellationToken ct)
    {
        var sub = await GetOwnedAsync(username, id, ct);
        if (State(sub) != "on_hold") throw new ApiException(409, $"Only a paused subscription can be resumed; this one is {State(sub)}.");

        var result = await _maxio.PostAsync($"subscriptions/{id}/resume.json", new { }, ct);
        return await ToSubscriptionAsync(result.GetProperty("subscription"), ct);
    }

    public async Task<SubscriptionDto> CancelAsync(string username, long id, CancellationToken ct)
    {
        var sub = await GetOwnedAsync(username, id, ct);
        var state = State(sub);
        if (state == "canceled") throw new ApiException(409, "The subscription is already cancelled.");
        if (state is "expired" or "failed_to_create" or "purged") throw new ApiException(409, $"A {state} subscription cannot be cancelled.");

        var result = await _maxio.DeleteAsync($"subscriptions/{id}.json", new { cancellation_message = "Cancelled by the shopper" }, ct);
        return await ToSubscriptionAsync(result.GetProperty("subscription"), ct);
    }

    public async Task<SubscriptionDto> RestartAsync(string username, long id, CancellationToken ct)
    {
        var sub = await GetOwnedAsync(username, id, ct);
        if (State(sub) != "canceled") throw new ApiException(409, $"Only a cancelled subscription can be restarted; this one is {State(sub)}.");

        var result = await _maxio.PutAsync($"subscriptions/{id}/reactivate.json", new { }, ct);
        return await ToSubscriptionAsync(result.GetProperty("subscription"), ct);
    }

    public async Task<SubscriptionDto> SwitchPlanAsync(string username, long id, string planHandle, CancellationToken ct)
    {
        var sub = await GetOwnedAsync(username, id, ct);
        var plan = await GetPlanByHandleAsync(planHandle, ct);
        var newHandle = plan.GetProperty("handle").GetString()!;
        var state = State(sub);

        if (state is "expired" or "failed_to_create" or "purged") throw new ApiException(409, $"A {state} subscription cannot be switched.");
        if (sub.GetProperty("product").GetProperty("handle").GetString() == newHandle && state != "canceled")
        {
            throw new ApiException(409, "The subscription is already on that plan.");
        }

        // Maxio only migrates live subscriptions, so bring a cancelled / paused one back first.
        var undo = state switch
        {
            "canceled" => (Func<Task>)(() => _maxio.DeleteAsync($"subscriptions/{id}.json", new { cancellation_message = "Switch plan failed" }, ct)),
            "on_hold" => () => _maxio.PostAsync($"subscriptions/{id}/hold.json", new { }, ct),
            _ => null
        };

        if (state == "canceled") await _maxio.PutAsync($"subscriptions/{id}/reactivate.json", new { }, ct);
        else if (state == "on_hold") await _maxio.PostAsync($"subscriptions/{id}/resume.json", new { }, ct);

        JsonElement migrated;
        try
        {
            migrated = await _maxio.PostAsync($"subscriptions/{id}/migrations.json", new { migration = new { product_handle = newHandle } }, ct);
        }
        catch
        {
            if (undo != null)
            {
                try { await undo(); } catch { /* best effort */ }
            }

            throw;
        }

        // A paused subscription stays paused after switching plan.
        if (state == "on_hold")
        {
            migrated = await _maxio.PostAsync($"subscriptions/{id}/hold.json", new { }, ct);
        }

        return await ToSubscriptionAsync(migrated.GetProperty("subscription"), ct);
    }

    public async Task<UsageDto> GetUsageAsync(string username, long id, DateOnly date, CancellationToken ct)
    {
        await GetOwnedAsync(username, id, ct);

        var components = await _maxio.GetAllAsync($"subscriptions/{id}/components.json", null, 200, ct);
        var result = new List<UsageComponentDto>();
        // Maxio's date filter boundaries are fuzzy (site timezone, exclusive end), so ask for a wider window and cut the day ourselves.
        var since = date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var until = date.AddDays(2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        foreach (var c in components.Select(c => c.GetProperty("component")))
        {
            if (Str(c, "kind") != "metered_component")
            {
                continue;
            }

            var componentId = c.GetProperty("component_id").GetInt64();
            var usages = await _maxio.GetAllAsync($"subscriptions/{id}/components/{componentId}/usages.json?since_date={since}&until_date={until}", null, 200, ct);
            decimal quantity = 0;
            foreach (var u in usages.Select(u => u.GetProperty("usage")))
            {
                var createdAt = DateTimeOffset.Parse(Str(u, "created_at")!, CultureInfo.InvariantCulture);
                if (DateOnly.FromDateTime(createdAt.DateTime) == date)
                {
                    quantity += Dec(u, "quantity");
                }
            }

            result.Add(new UsageComponentDto(componentId, Str(c, "component_handle"), Str(c, "name") ?? "", Str(c, "unit_name"), quantity));
        }

        return new UsageDto(id, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), result.Sum(r => r.Quantity), result);
    }

    // ---------- Staff ----------

    public async Task<PlanDto> CreatePlanAsync(string? name, decimal? price, string? brandName, int? brandId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ApiException(400, "A plan name is required.");
        name = name.Trim();
        if (name.Length > 255) throw new ApiException(400, "The plan name is too long.");
        if (price is null) throw new ApiException(400, "A monthly price is required.");
        var cents = ToCents(price.Value);

        var handle = Slug(name);
        if (handle.Length == 0) throw new ApiException(400, "The plan name must contain letters or digits.");

        var brand = await ResolveBrandAsync(brandName, brandId, ct);

        var families = await _maxio.GetAllAsync("product_families.json", null, 200, ct);
        if (families.Count == 0) throw new ApiException(409, "There is no product family to add the plan to.");
        var familyId = families[0].GetProperty("product_family").GetProperty("id").GetInt64();

        // Maxio would reject a duplicate handle, but check names too, since plans are listed by name.
        var existing = await ListPlansAsync(ct);
        if (existing.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) || p.Handle == handle))
        {
            throw new ApiException(409, $"A plan named '{name}' already exists.");
        }

        var created = await _maxio.PostAsync($"product_families/{familyId}/products.json", new
        {
            product = new
            {
                name,
                handle,
                description = BRAND_PREFIX + brand.Brand,
                price_in_cents = cents,
                interval = 1,
                interval_unit = "month",
                request_credit_card = false,
                require_credit_card = false
            }
        }, ct);

        return ToPlan(created.GetProperty("product"));
    }

    public async Task<CurrencyPriceDto> AddCurrencyPriceAsync(long planId, string? currency, decimal? price, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(currency) || !Regex.IsMatch(currency.Trim(), "^[A-Za-z]{3}$")) throw new ApiException(400, "currency must be a 3-letter ISO code.");
        if (price is null) throw new ApiException(400, "A price is required.");
        if (price.Value <= 0) throw new ApiException(400, "The price must be greater than zero.");
        currency = currency.Trim().ToUpperInvariant();

        var product = await GetPlanByIdAsync(planId, ct);
        var pricePointId = product.GetProperty("default_product_price_point_id").GetInt64();
        var body = new { currency_prices = new[] { new { currency, price = price.Value, role = "baseline" } } };

        try
        {
            await _maxio.PostAsync($"product_price_points/{pricePointId}/currency_prices.json", body, ct);
        }
        catch (ApiException ex) when (ex.StatusCode == 422 && Regex.IsMatch(ex.Message, "already|taken|exist", RegexOptions.IgnoreCase))
        {
            await _maxio.PutAsync($"product_price_points/{pricePointId}/currency_prices.json", body, ct);
        }

        return new CurrencyPriceDto(planId, currency, price.Value);
    }

    public async Task<SubscriptionDto> SetNextBillingDateAsync(long id, DateOnly? date, CancellationToken ct)
    {
        if (date is null) throw new ApiException(400, "A date is required.");

        JsonElement sub;
        try
        {
            sub = (await _maxio.GetAsync($"subscriptions/{id}.json", ct)).GetProperty("subscription");
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            throw new ApiException(404, "Subscription not found.");
        }

        var state = State(sub);
        RequireState(state, new[] { "active", "trialing", "past_due", "unpaid", "soft_failure", "trial_ended" }, "given a billing date");

        // Billing happens at the stored time of day; use noon in the site's own offset so the calendar date is unambiguous.
        var offset = Date(sub, "current_period_ends_at")?.Offset ?? Date(sub, "created_at")?.Offset ?? TimeSpan.Zero;
        var target = new DateTimeOffset(date.Value.Year, date.Value.Month, date.Value.Day, 12, 0, 0, offset);
        if (target <= DateTimeOffset.UtcNow) throw new ApiException(400, "The next billing date must be in the future.");

        var updated = await _maxio.PutAsync($"subscriptions/{id}.json", new { subscription = new { next_billing_at = target.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture) } }, ct);
        return await ToSubscriptionAsync(updated.GetProperty("subscription"), ct);
    }

    // ---------- helpers ----------

    private async Task<JsonElement> GetPlanByIdAsync(long planId, CancellationToken ct)
    {
        try
        {
            return (await _maxio.GetAsync($"products/{planId}.json", ct)).GetProperty("product");
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            throw new ApiException(404, "Plan not found.");
        }
    }

    private async Task<CatalogBrand> ResolveBrandAsync(string? brandName, int? brandId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(brandName) && brandId is null) throw new ApiException(400, "A brand is required.");

        var brands = await _brands.ListAsync(ct);
        CatalogBrand? match = null;
        if (brandId is not null)
        {
            match = brands.FirstOrDefault(b => b.Id == brandId);
        }

        if (!string.IsNullOrWhiteSpace(brandName))
        {
            var byName = brands.FirstOrDefault(b => string.Equals(b.Brand, brandName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (brandId is not null && byName?.Id != match?.Id) throw new ApiException(400, "brand and brandId refer to different brands.");
            match = byName;
        }

        return match ?? throw new ApiException(400, "The brand is not one of the brands in the catalog.");
    }

    private async Task<long> GetOrCreateCustomerAsync(string username, CancellationToken ct)
    {
        try
        {
            var found = await _maxio.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(username)}", ct);
            return found.GetProperty("customer").GetProperty("id").GetInt64();
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
        }

        var user = await _userManager.FindByNameAsync(username);
        var email = string.IsNullOrWhiteSpace(user?.Email) ? username : user!.Email!;
        var created = await _maxio.PostAsync("customers.json", new
        {
            customer = new { first_name = username, last_name = "(eShopOnWeb shopper)", email, reference = username }
        }, ct);
        return created.GetProperty("customer").GetProperty("id").GetInt64();
    }

    /// <summary>Loads a subscription, hiding other shoppers' subscriptions behind a 404.</summary>
    private async Task<JsonElement> GetOwnedAsync(string username, long id, CancellationToken ct)
    {
        try
        {
            var sub = (await _maxio.GetAsync($"subscriptions/{id}.json", ct)).GetProperty("subscription");
            var reference = sub.TryGetProperty("customer", out var c) ? Str(c, "reference") : null;
            if (reference == username) return sub;
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
        }

        throw new ApiException(404, "Subscription not found.");
    }

    private async Task<SubscriptionDto> ToSubscriptionAsync(JsonElement sub, CancellationToken ct)
    {
        // Re-read so the answer reflects the state Maxio settled on (invoices from signup / migration, etc.).
        var id = sub.GetProperty("id").GetInt64();
        try
        {
            sub = (await _maxio.GetAsync($"subscriptions/{id}.json", ct)).GetProperty("subscription");
        }
        catch (ApiException)
        {
        }

        var openInvoices = await _maxio.GetAllAsync($"invoices.json?subscription_id={id}&status=open", "invoices", 200, ct);
        var invoiceDue = openInvoices.Sum(i => Dec(i, "due_amount"));
        var balance = Dec(sub, "balance_in_cents") / 100m;
        var due = Math.Max(Math.Max(balance, invoiceDue), 0m);

        var product = sub.GetProperty("product");
        return new SubscriptionDto(
            id, State(sub), Str(product, "handle") ?? "", Str(product, "name") ?? "",
            Dec(sub, "product_price_in_cents") / 100m, Str(sub, "currency") ?? "USD",
            due == 0m, due, Date(sub, "next_assessment_at"), Date(sub, "current_period_ends_at"),
            Date(sub, "canceled_at"), Date(sub, "created_at"));
    }

    private static PlanDto ToPlan(JsonElement p)
    {
        var description = Str(p, "description");
        var brand = description != null && description.StartsWith(BRAND_PREFIX, StringComparison.Ordinal) ? description[BRAND_PREFIX.Length..] : null;
        return new PlanDto(
            p.GetProperty("id").GetInt64(), Str(p, "handle") ?? "", Str(p, "name") ?? "", description, brand,
            Dec(p, "price_in_cents") / 100m, "USD", (int)Dec(p, "interval"), Str(p, "interval_unit") ?? "month");
    }

    private static void RequireState(string state, string[] allowed, string action)
    {
        if (!allowed.Contains(state))
        {
            throw new ApiException(409, $"A subscription that is {state} cannot be {action}.");
        }
    }

    private static long ToCents(decimal price)
    {
        if (price < 0) throw new ApiException(400, "The price cannot be negative.");
        if (price > 1_000_000m) throw new ApiException(400, "The price is too large.");
        var cents = price * 100m;
        if (cents != decimal.Truncate(cents)) throw new ApiException(400, "The price can have at most two decimal places.");
        return (long)cents;
    }

    private static string Slug(string name)
    {
        return Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
    }

    private static string State(JsonElement sub) => Str(sub, "state") ?? "";

    private static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool IsNull(JsonElement e, string name)
        => !e.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null;

    /// <summary>Reads a number that Maxio may send as a JSON number or a string.</summary>
    private static decimal Dec(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDecimal(),
            JsonValueKind.String => decimal.Parse(v.GetString()!, CultureInfo.InvariantCulture),
            _ => 0
        };
    }

    private static DateTimeOffset? Date(JsonElement e, string name)
    {
        var s = Str(e, name);
        return s == null ? null : DateTimeOffset.Parse(s, CultureInfo.InvariantCulture);
    }
}
