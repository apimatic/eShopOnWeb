using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Subscription billing use cases, backed by Maxio as the system of record.
/// </summary>
public class BillingService
{
    private const string BRAND_PREFIX = "Brand: ";
    private const HttpStatusCode Unprocessable = (HttpStatusCode)422;

    private readonly MaxioClient _maxio;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IReadRepository<CatalogBrand> _brands;

    public BillingService(MaxioClient maxio, UserManager<ApplicationUser> userManager, IReadRepository<CatalogBrand> brands)
    {
        _maxio = maxio;
        _userManager = userManager;
        _brands = brands;
    }

    // ---------- identity ----------

    /// <summary>The Maxio customer reference for a signed-in shopper.</summary>
    private static string CustomerReference(string? userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new BillingException(HttpStatusCode.Unauthorized, "The token does not identify a user.");
        }
        return userName.Trim().ToLowerInvariant();
    }

    private async Task<long> GetOrCreateCustomerAsync(string? userName, CancellationToken ct)
    {
        var reference = CustomerReference(userName);
        try
        {
            var found = await _maxio.SendAsync(HttpMethod.Get, $"customers/lookup.json?reference={MaxioClient.Segment(reference)}", null, ct);
            var id = found?["customer"]?["id"]?.GetValue<long>();
            if (id != null) return id.Value;
        }
        catch (BillingException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
        }

        var user = await _userManager.FindByNameAsync(userName!);
        var email = user?.Email ?? (userName!.Contains('@') ? userName : $"{reference}@example.invalid");
        var local = email.Split('@')[0];
        var created = await _maxio.SendAsync(HttpMethod.Post, "customers.json", new
        {
            customer = new { first_name = local, last_name = "Shopper", email, reference }
        }, ct);
        return created?["customer"]?["id"]?.GetValue<long>()
            ?? throw new BillingException(HttpStatusCode.BadGateway, "The billing system did not return the customer.");
    }

    // ---------- plans ----------

    private static PlanDto ToPlan(JsonNode p)
    {
        var description = p["description"]?.GetValue<string>();
        string? brand = null;
        if (description != null && description.StartsWith(BRAND_PREFIX, StringComparison.Ordinal))
        {
            brand = description[BRAND_PREFIX.Length..].Trim();
        }
        return new PlanDto
        {
            Id = p["id"]!.GetValue<long>(),
            Handle = p["handle"]?.GetValue<string>() ?? "",
            Name = p["name"]?.GetValue<string>() ?? "",
            Brand = brand,
            PriceInCents = p["price_in_cents"]?.GetValue<long>() ?? 0,
            Interval = $"{p["interval"]} {p["interval_unit"]}"
        };
    }

    private static bool IsArchived(JsonNode p) => p["archived_at"] != null;

    public async Task<List<PlanDto>> ListPlansAsync(CancellationToken ct)
    {
        var items = await _maxio.ListAllAsync("products.json", ct);
        return items
            .Select(i => i["product"])
            .Where(p => p != null && !IsArchived(p))
            .Select(p => ToPlan(p!))
            .ToList();
    }

    private async Task<JsonNode> GetActiveProductByHandleAsync(string? handle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw BillingException.BadRequest("A plan handle is required.");
        }
        JsonNode? product;
        try
        {
            product = (await _maxio.SendAsync(HttpMethod.Get, $"products/handle/{MaxioClient.Segment(handle.Trim())}.json", null, ct))?["product"];
        }
        catch (BillingException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw BillingException.NotFound($"There is no plan with handle '{handle}'.");
        }
        if (product == null || IsArchived(product))
        {
            throw BillingException.NotFound($"There is no plan with handle '{handle}'.");
        }
        return product;
    }

    public async Task<PlanDto> CreatePlanAsync(string? name, decimal monthlyPrice, int brandId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) throw BillingException.BadRequest("A plan name is required.");
        if (monthlyPrice <= 0 || monthlyPrice > 1_000_000) throw BillingException.BadRequest("The monthly price must be greater than zero.");
        if (decimal.Round(monthlyPrice, 2) != monthlyPrice) throw BillingException.BadRequest("The monthly price can have at most two decimal places.");

        var brand = await _brands.GetByIdAsync(brandId, ct);
        if (brand == null) throw BillingException.BadRequest($"There is no catalog brand with id {brandId}.");

        var handle = Regex.Replace(name.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (handle.Length == 0) throw BillingException.BadRequest("The plan name must contain letters or digits.");

        var familyId = await GetProductFamilyIdAsync(ct);
        var created = await _maxio.SendAsync(HttpMethod.Post, $"product_families/{familyId}/products.json", new
        {
            product = new
            {
                name = name.Trim(),
                handle,
                description = BRAND_PREFIX + brand.Brand,
                price_in_cents = (long)(monthlyPrice * 100m),
                interval = 1,
                interval_unit = "month",
                require_credit_card = false,
                request_credit_card = false
            }
        }, ct);
        return ToPlan(created?["product"] ?? throw new BillingException(HttpStatusCode.BadGateway, "The billing system did not return the plan."));
    }

    /// <summary>Plans live in the product family that carries the metered components.</summary>
    private async Task<long> GetProductFamilyIdAsync(CancellationToken ct)
    {
        var families = (await _maxio.ListAllAsync("product_families.json", ct))
            .Select(f => f["product_family"]).Where(f => f != null && f["archived_at"] == null).ToList();
        if (families.Count == 0) throw new BillingException(Unprocessable, "The billing site has no product family to hold plans.");
        foreach (var f in families)
        {
            var id = f!["id"]!.GetValue<long>();
            var comps = await _maxio.SendAsync(HttpMethod.Get, $"product_families/{id}/components.json?per_page=1", null, ct) as JsonArray;
            if (comps is { Count: > 0 }) return id;
        }
        return families[0]!["id"]!.GetValue<long>();
    }

    public async Task<CurrencyPriceResponse> AddCurrencyPriceAsync(long planId, string? currency, decimal price, Guid correlationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(currency) || !Regex.IsMatch(currency.Trim(), "^[A-Za-z]{3}$"))
            throw BillingException.BadRequest("The currency must be a three-letter ISO 4217 code.");
        if (price <= 0 || price > 1_000_000) throw BillingException.BadRequest("The price must be greater than zero.");
        var code = currency.Trim().ToUpperInvariant();

        JsonNode? product;
        try
        {
            product = (await _maxio.SendAsync(HttpMethod.Get, $"products/{planId}.json", null, ct))?["product"];
        }
        catch (BillingException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw BillingException.NotFound($"There is no plan with id {planId}.");
        }
        var pricePointId = product?["default_product_price_point_id"]?.GetValue<long>()
            ?? product?["product_price_point_id"]?.GetValue<long>();
        if (pricePointId == null) throw new BillingException(HttpStatusCode.BadGateway, "The plan has no price point.");

        await _maxio.SendAsync(HttpMethod.Post, $"product_price_points/{pricePointId}/currency_prices.json", new
        {
            currency_prices = new[] { new { currency = code, price, role = "baseline" } }
        }, ct);
        return new CurrencyPriceResponse(correlationId) { PlanId = planId, Currency = code, Price = price };
    }

    // ---------- subscriptions ----------

    private async Task<JsonNode> LoadOwnedSubscriptionAsync(string? userName, long subscriptionId, CancellationToken ct)
    {
        var sub = await LoadSubscriptionAsync(subscriptionId, ct);
        var reference = sub["customer"]?["reference"]?.GetValue<string>();
        if (!string.Equals(reference, CustomerReference(userName), StringComparison.OrdinalIgnoreCase))
        {
            // Don't reveal that someone else's subscription exists.
            throw BillingException.NotFound($"There is no subscription with id {subscriptionId}.");
        }
        return sub;
    }

    private async Task<JsonNode> LoadSubscriptionAsync(long subscriptionId, CancellationToken ct)
    {
        try
        {
            var node = await _maxio.SendAsync(HttpMethod.Get, $"subscriptions/{subscriptionId}.json", null, ct);
            return node?["subscription"] ?? throw BillingException.NotFound($"There is no subscription with id {subscriptionId}.");
        }
        catch (BillingException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw BillingException.NotFound($"There is no subscription with id {subscriptionId}.");
        }
    }

    private async Task<SubscriptionDto> ToDtoAsync(JsonNode sub, CancellationToken ct)
    {
        var id = sub["id"]!.GetValue<long>();
        var balance = sub["balance_in_cents"]?.GetValue<long>() ?? 0;

        var invoices = await _maxio.ListAllAsync($"invoices.json?subscription_id={id}&status=open", ct);
        var due = 0m;
        foreach (var inv in invoices)
        {
            var s = inv["due_amount"]?.ToString();
            if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) due += d;
        }
        var amountDue = Math.Max(Math.Max(balance / 100m, 0m), due);

        return new SubscriptionDto
        {
            Id = id,
            State = sub["state"]?.GetValue<string>() ?? "",
            PlanId = sub["product"]?["id"]?.GetValue<long>() ?? 0,
            PlanHandle = sub["product"]?["handle"]?.GetValue<string>() ?? "",
            PlanName = sub["product"]?["name"]?.GetValue<string>() ?? "",
            Paid = amountDue <= 0m,
            BalanceInCents = balance,
            AmountDue = amountDue,
            Currency = sub["currency"]?.GetValue<string>(),
            NextBillingAt = ParseDate(sub["next_assessment_at"]),
            CanceledAt = ParseDate(sub["canceled_at"])
        };
    }

    private static DateTimeOffset? ParseDate(JsonNode? n) =>
        n != null && DateTimeOffset.TryParse(n.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private async Task<SubscriptionDto> FinishAsync(long subscriptionId, CancellationToken ct) =>
        await ToDtoAsync(await LoadSubscriptionAsync(subscriptionId, ct), ct);

    public async Task<SubscriptionDto> SubscribeAsync(string? userName, string? planHandle, CancellationToken ct)
    {
        var product = await GetActiveProductByHandleAsync(planHandle, ct);
        var customerId = await GetOrCreateCustomerAsync(userName, ct);
        // Remittance collection: the shopper gives no card details and is invoiced instead.
        var created = await _maxio.SendAsync(HttpMethod.Post, "subscriptions.json", new
        {
            subscription = new
            {
                product_handle = product["handle"]!.GetValue<string>(),
                customer_id = customerId,
                payment_collection_method = "remittance"
            }
        }, ct);
        var id = created?["subscription"]?["id"]?.GetValue<long>()
            ?? throw new BillingException(HttpStatusCode.BadGateway, "The billing system did not return the subscription.");
        return await FinishAsync(id, ct);
    }

    public async Task<SubscriptionDto> GetAsync(string? userName, long subscriptionId, CancellationToken ct) =>
        await ToDtoAsync(await LoadOwnedSubscriptionAsync(userName, subscriptionId, ct), ct);

    public async Task<SubscriptionDto> PauseAsync(string? userName, long subscriptionId, CancellationToken ct)
    {
        await LoadOwnedSubscriptionAsync(userName, subscriptionId, ct);
        await _maxio.SendAsync(HttpMethod.Post, $"subscriptions/{subscriptionId}/hold.json", new { }, ct);
        return await FinishAsync(subscriptionId, ct);
    }

    public async Task<SubscriptionDto> ResumeAsync(string? userName, long subscriptionId, CancellationToken ct)
    {
        await LoadOwnedSubscriptionAsync(userName, subscriptionId, ct);
        await _maxio.SendAsync(HttpMethod.Post, $"subscriptions/{subscriptionId}/resume.json", new { }, ct);
        return await FinishAsync(subscriptionId, ct);
    }

    public async Task<SubscriptionDto> CancelAsync(string? userName, long subscriptionId, CancellationToken ct)
    {
        var sub = await LoadOwnedSubscriptionAsync(userName, subscriptionId, ct);
        if (sub["state"]?.GetValue<string>() == "canceled")
            throw new BillingException(Unprocessable, "The subscription is already cancelled.");
        await _maxio.SendAsync(HttpMethod.Delete, $"subscriptions/{subscriptionId}.json", new { }, ct);
        return await FinishAsync(subscriptionId, ct);
    }

    public async Task<SubscriptionDto> RestartAsync(string? userName, long subscriptionId, CancellationToken ct)
    {
        var sub = await LoadOwnedSubscriptionAsync(userName, subscriptionId, ct);
        if (sub["state"]?.GetValue<string>() != "canceled")
            throw new BillingException(Unprocessable, "Only a cancelled subscription can be restarted.");
        await _maxio.SendAsync(HttpMethod.Put, $"subscriptions/{subscriptionId}/reactivate.json", new { }, ct);
        return await FinishAsync(subscriptionId, ct);
    }

    public async Task<SubscriptionDto> SwitchPlanAsync(string? userName, long subscriptionId, string? planHandle, CancellationToken ct)
    {
        var product = await GetActiveProductByHandleAsync(planHandle, ct);
        var handle = product["handle"]!.GetValue<string>();
        var sub = await LoadOwnedSubscriptionAsync(userName, subscriptionId, ct);
        var state = sub["state"]?.GetValue<string>();

        if (sub["product"]?["handle"]?.GetValue<string>() == handle && state != "canceled")
            throw new BillingException(Unprocessable, "The subscription is already on that plan.");

        if (state is "canceled" or "on_hold")
        {
            // Maxio can't migrate these states; change the product directly, then bring a cancelled
            // subscription back so the shopper really is on the new plan (and billed for it).
            await _maxio.SendAsync(HttpMethod.Put, $"subscriptions/{subscriptionId}.json",
                new { subscription = new { product_handle = handle } }, ct);
            if (state == "canceled")
            {
                await _maxio.SendAsync(HttpMethod.Put, $"subscriptions/{subscriptionId}/reactivate.json", new { }, ct);
            }
        }
        else
        {
            await _maxio.SendAsync(HttpMethod.Post, $"subscriptions/{subscriptionId}/migrations.json",
                new { migration = new { product_handle = handle } }, ct);
        }
        return await FinishAsync(subscriptionId, ct);
    }

    public async Task<SubscriptionDto> SetNextBillingDateAsync(long subscriptionId, DateOnly? date, CancellationToken ct)
    {
        if (date == null) throw BillingException.BadRequest("A date (yyyy-MM-dd) is required.");
        var sub = await LoadSubscriptionAsync(subscriptionId, ct);
        var state = sub["state"]?.GetValue<string>() ?? "";
        if (state is "canceled" or "expired" or "trial_ended")
            throw new BillingException(Unprocessable, $"A {state.Replace('_', ' ')} subscription is not billed, so it has no next billing date.");
        await _maxio.SendAsync(HttpMethod.Put, $"subscriptions/{subscriptionId}.json",
            new { subscription = new { next_billing_at = date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) } }, ct);
        return await FinishAsync(subscriptionId, ct);
    }

    // ---------- usage ----------

    public async Task<UsageResponse> GetUsageAsync(string? userName, long subscriptionId, string? date, Guid correlationId, CancellationToken ct)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            throw BillingException.BadRequest("The date query parameter is required, formatted yyyy-MM-dd.");

        await LoadOwnedSubscriptionAsync(userName, subscriptionId, ct);

        var response = new UsageResponse(correlationId) { SubscriptionId = subscriptionId, Date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        var components = await _maxio.ListAllAsync($"subscriptions/{subscriptionId}/components.json", ct);
        // Query a day either side, then keep only usage whose own (site-time) calendar date is the requested day.
        var from = day.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var to = day.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        foreach (var c in components.Select(x => x["component"]).Where(x => x?["kind"]?.GetValue<string>() == "metered_component"))
        {
            var componentId = c!["component_id"]!.GetValue<long>();
            var usages = await _maxio.ListAllAsync(
                $"subscriptions/{subscriptionId}/components/{componentId}/usages.json?date_from={from}&date_to={to}", ct);
            var records = new List<UsageRecordDto>();
            foreach (var u in usages.Select(x => x["usage"]).Where(x => x != null))
            {
                if (!DateTimeOffset.TryParse(u!["created_at"]?.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)) continue;
                if (DateOnly.FromDateTime(at.DateTime) != day) continue;
                records.Add(new UsageRecordDto
                {
                    Id = u["id"]!.GetValue<long>(),
                    Quantity = decimal.Parse(u["quantity"]!.ToString(), CultureInfo.InvariantCulture),
                    Memo = u["memo"]?.GetValue<string>(),
                    RecordedAt = at
                });
            }
            response.Components.Add(new UsageComponentDto
            {
                ComponentId = componentId,
                Handle = c["component_handle"]?.GetValue<string>(),
                Name = c["name"]?.GetValue<string>(),
                UnitName = c["unit_name"]?.GetValue<string>(),
                Quantity = records.Sum(r => r.Quantity),
                Records = records
            });
        }
        response.TotalQuantity = response.Components.Sum(c => c.Quantity);
        return response;
    }
}
