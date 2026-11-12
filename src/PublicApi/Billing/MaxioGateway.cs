using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.Invoices;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.ProductPricePoints;
using MaxioAdvancedBilling.Requests.Products;
using MaxioAdvancedBilling.Requests.SubscriptionComponents;
using MaxioAdvancedBilling.Requests.SubscriptionProducts;
using MaxioAdvancedBilling.Requests.SubscriptionStatus;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public record PlanDto(int Id, string Handle, string Name, decimal MonthlyPrice, int BrandId, string BrandName);

public record SubscriptionDto(int Id, string State, string? PlanHandle, string? PlanName, bool Paid, DateTimeOffset? NextBillingAt, string? Currency);

public record UsageComponentDto(int ComponentId, string? Handle, string? Name, string? Unit, decimal Quantity);

public record UsageDto(int SubscriptionId, string Date, decimal TotalQuantity, IReadOnlyList<UsageComponentDto> Components);

public record CurrencyPriceDto(int PlanId, string Currency, decimal Price);

/// <summary>
/// All calls into Maxio (the billing system of record) made by the public API.
/// </summary>
public class MaxioGateway
{
    public const string BRAND_FAMILY_PREFIX = "eshop-brand-";
    public const string CUSTOMER_REFERENCE_PREFIX = "eshop-user-";
    private const int PAGE_SIZE = 200;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly IReadRepository<CatalogBrand> _brands;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<MaxioGateway> _logger;

    public MaxioGateway(MaxioAdvancedBillingClient client, IReadRepository<CatalogBrand> brands,
        UserManager<ApplicationUser> userManager, ILogger<MaxioGateway> logger)
    {
        _client = client;
        _brands = brands;
        _userManager = userManager;
        _logger = logger;
    }

    // ---------------------------------------------------------------- plans

    /// <summary>The plans this shop offers: non-archived products of the families that belong to catalog brands.</summary>
    public async Task<IReadOnlyList<PlanDto>> ListPlansAsync(CancellationToken ct = default)
    {
        var brands = (await _brands.ListAsync(ct)).ToDictionary(b => b.Id, b => b.Brand);
        var plans = new List<PlanDto>();

        var families = await CallAsync("ListProductFamilies",
            () => _client.ProductFamilies.ListProductFamilies(new ListProductFamiliesRequest(), cancellationToken: ct));

        foreach (var familyResponse in families)
        {
            var family = familyResponse.ProductFamily;
            if (family?.Id is null || family.ArchivedAt is not null) continue;
            var brandId = BrandIdOfFamilyHandle(family.Handle);
            if (brandId is null || !brands.TryGetValue(brandId.Value, out var brandName)) continue;

            for (var page = 1; ; page++)
            {
                var products = await CallAsync("ListProductsForProductFamily",
                    () => _client.ProductFamilies.ListProductsForProductFamily(new ListProductsForProductFamilyRequest
                    {
                        ProductFamilyId = family.Id.Value.ToString(CultureInfo.InvariantCulture),
                        Page = page,
                        PerPage = PAGE_SIZE,
                    }, cancellationToken: ct));

                foreach (var p in products.Select(r => r.Product))
                {
                    if (p.Id is null || p.Handle is null || p.ArchivedAt is not null) continue;
                    plans.Add(new PlanDto(p.Id.Value, p.Handle, p.Name ?? p.Handle,
                        (p.PriceInCents ?? 0) / 100m, brandId.Value, brandName));
                }

                if (products.Count < PAGE_SIZE) break;
            }
        }

        return plans;
    }

    public async Task<PlanDto> FindOfferedPlanByHandleAsync(string? handle, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(handle))
            throw new BillingException(400, "A plan handle is required.");
        var plan = (await ListPlansAsync(ct)).FirstOrDefault(p => string.Equals(p.Handle, handle.Trim(), StringComparison.OrdinalIgnoreCase));
        return plan ?? throw new BillingException(404, $"Plan '{handle}' is not offered.");
    }

    public async Task<PlanDto> FindOfferedPlanByIdAsync(int planId, CancellationToken ct = default)
    {
        var plan = (await ListPlansAsync(ct)).FirstOrDefault(p => p.Id == planId);
        return plan ?? throw new BillingException(404, $"Plan {planId} does not exist.");
    }

    public async Task<PlanDto> CreatePlanAsync(string? name, decimal monthlyPrice, int brandId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new BillingException(400, "A plan name is required.");
        name = name.Trim();
        if (monthlyPrice < 0 || decimal.Round(monthlyPrice, 2) != monthlyPrice)
            throw new BillingException(400, "The monthly price must be zero or more, with at most two decimals.");
        var cents = (long)(monthlyPrice * 100m);

        var brand = await _brands.GetByIdAsync(brandId, ct);
        if (brand is null)
            throw new BillingException(400, $"Brand {brandId} is not in the catalog.");

        var slug = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (slug.Length == 0)
            throw new BillingException(400, "The plan name must contain letters or digits.");
        var handle = $"{slug}-b{brandId}";

        var existing = await ListPlansAsync(ct);
        if (existing.Any(p => string.Equals(p.Handle, handle, StringComparison.OrdinalIgnoreCase)
                              || (p.BrandId == brandId && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))))
            throw new BillingException(409, $"A plan named '{name}' already exists for brand '{brand.Brand}'.");

        var familyId = await EnsureBrandFamilyAsync(brand, ct);

        var created = await CallAsync("CreateProduct", () => _client.Products.CreateProduct(new CreateProductRequest
        {
            ProductFamilyId = familyId.ToString(CultureInfo.InvariantCulture),
            Body = new CreateOrUpdateProductRequest
            {
                Product = new CreateOrUpdateProduct
                {
                    Name = name,
                    Handle = handle,
                    Description = name,
                    PriceInCents = cents,
                    Interval = 1,
                    IntervalUnit = IntervalUnit.Month,
                    RequireCreditCard = false,
                },
            },
        }, cancellationToken: ct));

        var p = created.Product;
        return new PlanDto(p.Id!.Value, p.Handle ?? handle, p.Name ?? name, (p.PriceInCents ?? cents) / 100m, brandId, brand.Brand);
    }

    private async Task<int> EnsureBrandFamilyAsync(CatalogBrand brand, CancellationToken ct)
    {
        var handle = BRAND_FAMILY_PREFIX + brand.Id.ToString(CultureInfo.InvariantCulture);
        var families = await CallAsync("ListProductFamilies",
            () => _client.ProductFamilies.ListProductFamilies(new ListProductFamiliesRequest(), cancellationToken: ct));
        var found = families.Select(f => f.ProductFamily)
            .FirstOrDefault(f => f?.Id is not null && f.ArchivedAt is null && string.Equals(f.Handle, handle, StringComparison.Ordinal));
        if (found?.Id is not null) return found.Id.Value;

        var created = await CallAsync("CreateProductFamily", () => _client.ProductFamilies.CreateProductFamily(
            new CreateProductFamilyOperationRequest
            {
                Body = new CreateProductFamilyRequest
                {
                    ProductFamily = new CreateProductFamily { Name = brand.Brand, Handle = handle },
                },
            }, cancellationToken: ct));
        return created.ProductFamily?.Id ?? throw new BillingException(502, "The billing system did not return the new product family.");
    }

    public async Task<CurrencyPriceDto> AddCurrencyPriceAsync(int planId, string? currency, decimal price, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(currency) || !Regex.IsMatch(currency.Trim(), "^[A-Za-z]{3}$"))
            throw new BillingException(400, "The currency must be a three-letter ISO code.");
        currency = currency.Trim().ToUpperInvariant();
        if (price < 0 || decimal.Round(price, 2) != price)
            throw new BillingException(400, "The price must be zero or more, with at most two decimals.");

        await FindOfferedPlanByIdAsync(planId, ct);
        var product = (await CallAsync("ReadProduct",
            () => _client.Products.ReadProduct(new ReadProductRequest { ProductId = planId }, cancellationToken: ct))).Product;
        var pricePointId = product.DefaultProductPricePointId ?? product.ProductPricePointId
            ?? throw new BillingException(409, "The plan has no default price point to price in another currency.");

        // The SDK's currency price is a whole number (Maxio currency units, not cents).
        if (price != decimal.Truncate(price))
            throw new BillingException(400, "The price in another currency must be a whole number of currency units.");

        var result = await CallAsync("CreateProductCurrencyPrices", () => _client.ProductPricePoints.CreateProductCurrencyPrices(
            new CreateProductCurrencyPricesOperationRequest
            {
                ProductPricePointId = pricePointId,
                Body = new CreateProductCurrencyPricesRequest
                {
                    CurrencyPrices =
                    [
                        new CreateProductCurrencyPrice { Currency = currency, Price = (int)price, Role = CurrencyPriceRole.Baseline },
                    ],
                },
            }, cancellationToken: ct));

        var applied = result.CurrencyPrices.FirstOrDefault(c => string.Equals(c.Currency, currency, StringComparison.OrdinalIgnoreCase));
        return new CurrencyPriceDto(planId, currency, applied?.Price is double d ? (decimal)d : price);
    }

    // -------------------------------------------------------- subscriptions

    public static string CustomerReferenceFor(string userName) => CUSTOMER_REFERENCE_PREFIX + userName;

    public async Task<SubscriptionDto> SubscribeAsync(string userName, string? planHandle, CancellationToken ct = default)
    {
        var plan = await FindOfferedPlanByHandleAsync(planHandle, ct);
        var customerId = await EnsureCustomerAsync(userName, ct);

        var created = await CallAsync("CreateSubscription", () => _client.Subscriptions.CreateSubscription(
            new CreateSubscriptionOperationRequest
            {
                Body = new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = plan.Handle,
                        CustomerId = customerId,
                        PaymentCollectionMethod = CollectionMethod.Remittance,
                    },
                },
            }, cancellationToken: ct), writeOutcomeUnknownIsOk: false);

        var sub = created.Subscription ?? throw new BillingException(502, "The billing system did not return the new subscription.");
        return await ToDtoAsync(sub, ct);
    }

    private async Task<int> EnsureCustomerAsync(string userName, CancellationToken ct)
    {
        var reference = CustomerReferenceFor(userName);
        var existing = await FindCustomerAsync(reference, ct);
        if (existing is not null) return existing.Value;

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
            throw new BillingException(401, "The signed-in user is unknown.");
        var email = user.Email ?? user.UserName;
        if (string.IsNullOrWhiteSpace(email))
            throw new BillingException(422, "The user has no email address to bill.");

        try
        {
            var created = await CallAsync("CreateCustomer", () => _client.Customers.CreateCustomer(new CreateCustomerOperationRequest
            {
                Body = new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = userName,
                        LastName = "Shopper",
                        Email = email!,
                        Reference = reference,
                    },
                },
            }, cancellationToken: ct));
            return created.Customer.Id ?? throw new BillingException(502, "The billing system did not return the new customer.");
        }
        catch (BillingException ex) when (ex.StatusCode == 422)
        {
            // A concurrent first subscribe may have created the customer already.
            var raced = await FindCustomerAsync(reference, ct);
            if (raced is not null) return raced.Value;
            throw;
        }
    }

    private async Task<int?> FindCustomerAsync(string reference, CancellationToken ct)
    {
        try
        {
            var found = await CallAsync("ReadCustomerByReference", () => _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: ct));
            return found.Customer.Id;
        }
        catch (BillingException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    /// <summary>Reads a subscription and verifies it belongs to the caller. Others' subscriptions look absent.</summary>
    public async Task<Subscription> GetOwnedSubscriptionAsync(string userName, int subscriptionId, CancellationToken ct = default)
    {
        var sub = await GetSubscriptionAsync(subscriptionId, ct);
        if (!string.Equals(sub.Customer?.Reference, CustomerReferenceFor(userName), StringComparison.Ordinal))
            throw new BillingException(404, $"Subscription {subscriptionId} was not found.");
        return sub;
    }

    public async Task<Subscription> GetSubscriptionAsync(int subscriptionId, CancellationToken ct = default)
    {
        var read = await CallAsync("ReadSubscription",
            () => _client.Subscriptions.ReadSubscription(new ReadSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct));
        return read.Subscription ?? throw new BillingException(404, $"Subscription {subscriptionId} was not found.");
    }

    public async Task<SubscriptionDto> ReadAsync(string userName, int subscriptionId, CancellationToken ct = default)
    {
        var sub = await GetOwnedSubscriptionAsync(userName, subscriptionId, ct);
        return await ToDtoAsync(sub, ct);
    }

    public async Task<SubscriptionDto> PauseAsync(string userName, int subscriptionId, CancellationToken ct = default)
    {
        var sub = await GetOwnedSubscriptionAsync(userName, subscriptionId, ct);
        RequireState(sub, "pause", "active", "trialing", "past_due", "soft_failure");
        var result = await CallAsync("PauseSubscription", () => _client.SubscriptionStatus.PauseSubscription(
            new PauseSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct));
        return await ToDtoAsync(result.Subscription ?? sub, ct);
    }

    public async Task<SubscriptionDto> ResumeAsync(string userName, int subscriptionId, CancellationToken ct = default)
    {
        var sub = await GetOwnedSubscriptionAsync(userName, subscriptionId, ct);
        RequireState(sub, "resume", "on_hold", "paused");
        var result = await CallAsync("ResumeSubscription", () => _client.SubscriptionStatus.ResumeSubscription(
            new ResumeSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct));
        return await ToDtoAsync(result.Subscription ?? sub, ct);
    }

    public async Task<SubscriptionDto> CancelAsync(string userName, int subscriptionId, CancellationToken ct = default)
    {
        var sub = await GetOwnedSubscriptionAsync(userName, subscriptionId, ct);
        if (StateOf(sub) is "canceled" or "expired" or "failed_to_create")
            throw new BillingException(409, $"The subscription is already {StateOf(sub)}.");
        var result = await CallAsync("CancelSubscription", () => _client.SubscriptionStatus.CancelSubscription(
            new CancelSubscriptionRequest
            {
                SubscriptionId = subscriptionId,
                Body = new CancellationRequest { Subscription = new CancellationOptions { CancellationMessage = "Cancelled by the subscriber." } },
            }, cancellationToken: ct));
        return await ToDtoAsync(result.Subscription ?? sub, ct);
    }

    public async Task<SubscriptionDto> RestartAsync(string userName, int subscriptionId, CancellationToken ct = default)
    {
        var sub = await GetOwnedSubscriptionAsync(userName, subscriptionId, ct);
        RequireState(sub, "restart", "canceled");
        var result = await ReactivateAsync(subscriptionId, ct);
        return await ToDtoAsync(result.Subscription ?? sub, ct);
    }

    private Task<SubscriptionResponse> ReactivateAsync(int subscriptionId, CancellationToken ct) =>
        CallAsync("ReactivateSubscription", () => _client.SubscriptionStatus.ReactivateSubscription(
            new ReactivateSubscriptionOperationRequest { SubscriptionId = subscriptionId }, cancellationToken: ct));

    public async Task<SubscriptionDto> SwitchAsync(string userName, int subscriptionId, string? planHandle, CancellationToken ct = default)
    {
        var plan = await FindOfferedPlanByHandleAsync(planHandle, ct);
        var sub = await GetOwnedSubscriptionAsync(userName, subscriptionId, ct);
        var state = StateOf(sub);
        if (string.Equals(sub.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase))
            throw new BillingException(409, "The subscription is already on that plan.");
        RequireState(sub, "switch", "active", "trialing", "canceled");

        var wasCanceled = state == "canceled";
        if (wasCanceled)
            await ReactivateAsync(subscriptionId, ct);

        try
        {
            var migrated = await CallAsync("MigrateSubscriptionProduct", () => _client.SubscriptionProducts.MigrateSubscriptionProduct(
                new MigrateSubscriptionProductRequest
                {
                    SubscriptionId = subscriptionId,
                    Body = new SubscriptionProductMigrationRequest
                    {
                        Migration = new SubscriptionProductMigration { ProductHandle = plan.Handle },
                    },
                }, cancellationToken: ct));
            return await ToDtoAsync(migrated.Subscription ?? sub, ct);
        }
        catch when (wasCanceled)
        {
            // Put it back the way the shopper left it; the switch did not happen.
            try
            {
                await CallAsync("CancelSubscription", () => _client.SubscriptionStatus.CancelSubscription(
                    new CancelSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: CancellationToken.None));
            }
            catch (Exception rollback)
            {
                _logger.LogWarning(rollback, "Could not re-cancel subscription {SubscriptionId} after a failed plan switch", subscriptionId);
            }
            throw;
        }
    }

    public async Task<UsageDto> UsageForDayAsync(string userName, int subscriptionId, DateOnly day, CancellationToken ct = default)
    {
        await GetOwnedSubscriptionAsync(userName, subscriptionId, ct);

        var components = await CallAsync("ListSubscriptionComponents", () => _client.SubscriptionComponents.ListSubscriptionComponents(
            new ListSubscriptionComponentsRequest { SubscriptionId = subscriptionId }, cancellationToken: ct));

        var since = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var until = since.AddDays(1);
        var rows = new List<UsageComponentDto>();

        foreach (var component in components.Select(c => c.Component)
                     .Where(c => c?.ComponentId is not null && c.Kind == ComponentKind.MeteredComponent))
        {
            decimal total = 0;
            for (var page = 1; ; page++)
            {
                var usages = await CallAsync("ListUsages", () => _client.SubscriptionComponents.ListUsages(new ListUsagesRequest
                {
                    SubscriptionIdOrReference = subscriptionId,
                    ComponentId = component!.ComponentId!.Value,
                    SinceDate = since,
                    UntilDate = until,
                    Page = page,
                    PerPage = PAGE_SIZE,
                }, cancellationToken: ct));

                foreach (var u in usages.Select(r => r.Usage))
                {
                    // until_date is inclusive of the next midnight; keep only the picked calendar day.
                    if (u.CreatedAt is { } at && DateOnly.FromDateTime(at.DateTime) != day) continue;
                    total += QuantityOf(u.Quantity);
                }

                if (usages.Count < PAGE_SIZE) break;
            }

            rows.Add(new UsageComponentDto(component!.ComponentId!.Value, component.ComponentHandle, component.Name, component.UnitName, total));
        }

        return new UsageDto(subscriptionId, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), rows.Sum(r => r.Quantity), rows);
    }

    public async Task<SubscriptionDto> SetNextBillingDateAsync(int subscriptionId, DateOnly date, CancellationToken ct = default)
    {
        if (date <= DateOnly.FromDateTime(DateTime.UtcNow))
            throw new BillingException(400, "The next billing date must be in the future.");

        var sub = await GetSubscriptionAsync(subscriptionId, ct);
        RequireState(sub, "change the billing date of", "active", "trialing", "past_due", "soft_failure");

        // Midday UTC keeps the calendar day stable for any site time zone from UTC-12 to UTC+11.
        var at = new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        var updated = await CallAsync("UpdateSubscription", () => _client.Subscriptions.UpdateSubscription(
            new UpdateSubscriptionOperationRequest
            {
                SubscriptionId = subscriptionId,
                Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { NextBillingAt = at } },
            }, cancellationToken: ct));

        var result = updated.Subscription ?? await GetSubscriptionAsync(subscriptionId, ct);
        return await ToDtoAsync(result, ct);
    }

    // -------------------------------------------------------------- helpers

    private static string StateOf(Subscription sub) => sub.State?.Value ?? string.Empty;

    private static void RequireState(Subscription sub, string action, params string[] allowed)
    {
        var state = StateOf(sub);
        if (!allowed.Contains(state))
            throw new BillingException(409, $"A subscription that is '{state}' cannot be asked to {action}.");
    }

    private static decimal QuantityOf(MaxioAdvancedBilling.Models.AnyOf.Quantity1? q)
    {
        if (q is null) return 0;
        if (q.TryGetInt(out var i)) return i;
        if (q.TryGetString(out var s) && decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return d;
        return 0;
    }

    private static int? BrandIdOfFamilyHandle(string? handle) =>
        handle is not null && handle.StartsWith(BRAND_FAMILY_PREFIX, StringComparison.Ordinal)
        && int.TryParse(handle.AsSpan(BRAND_FAMILY_PREFIX.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id : null;

    private async Task<SubscriptionDto> ToDtoAsync(Subscription sub, CancellationToken ct)
    {
        var paid = !await OwesMoneyAsync(sub, ct);
        return new SubscriptionDto(sub.Id ?? 0, StateOf(sub), sub.Product?.Handle, sub.Product?.Name, paid,
            sub.CurrentPeriodEndsAt, sub.Currency);
    }

    /// <summary>True when the subscription carries a balance or has an open invoice with an amount due.</summary>
    private async Task<bool> OwesMoneyAsync(Subscription sub, CancellationToken ct)
    {
        if (sub.BalanceInCents is > 0) return true;
        if (sub.Id is null) return false;

        for (var page = 1; ; page++)
        {
            var invoices = await CallAsync("ListInvoices", () => _client.Invoices.ListInvoices(new ListInvoicesRequest
            {
                SubscriptionId = sub.Id,
                Status = InvoiceStatus.Open,
                Page = page,
                PerPage = PAGE_SIZE,
            }, cancellationToken: ct));

            foreach (var invoice in invoices.Invoices)
            {
                if (decimal.TryParse(invoice.DueAmount, NumberStyles.Any, CultureInfo.InvariantCulture, out var due) && due > 0)
                    return true;
            }

            if (invoices.Invoices.Count < PAGE_SIZE) return false;
        }
    }

    /// <summary>Runs one SDK call and turns every SDK failure into a <see cref="BillingException"/>.</summary>
    private async Task<T> CallAsync<T>(string operation, Func<Task<T>> call, bool writeOutcomeUnknownIsOk = true)
    {
        try
        {
            return await call();
        }
        catch (ApiException ex)
        {
            var status = (int)ex.StatusCode;
            var reason = ReasonOf(ex);
            _logger.LogWarning("Maxio {Operation} answered {Status}: {Reason}", operation, status, reason);
            throw status switch
            {
                404 => new BillingException(404, $"Billing system: not found ({operation}). {reason}".Trim()),
                400 or 409 or 422 => new BillingException(422, $"Billing system refused {operation}: {reason}"),
                401 or 403 => new BillingException(502, "The billing system rejected this application's credentials."),
                429 => new BillingException(503, "The billing system is busy; try again shortly."),
                _ => new BillingException(502, $"The billing system failed while running {operation}."),
            };
        }
        catch (SdkTimeoutException ex)
        {
            _logger.LogWarning(ex, "Maxio {Operation} timed out", operation);
            throw new BillingException(504, $"The billing system timed out during {operation}; the outcome is unknown, read the subscription before retrying.");
        }
        catch (SdkException ex)
        {
            _logger.LogWarning(ex, "Maxio {Operation} failed", operation);
            throw new BillingException(502, $"The billing system could not be reached for {operation}; the outcome is unknown.");
        }
    }

    private static string ReasonOf(ApiException ex)
    {
        var error = ex.GetType().GetProperty("Error", BindingFlags.Public | BindingFlags.Instance)?.GetValue(ex);
        string? text = null;
        try
        {
            if (error is RawError raw) text = raw.ReadAsString();
            else if (error is not null)
            {
                foreach (var m in error.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                             .Where(m => m.Name.StartsWith("TryGet", StringComparison.Ordinal) && m.GetParameters().Length == 1 && m.GetParameters()[0].IsOut))
                {
                    var args = new object?[] { null };
                    if (m.Invoke(error, args) is true && args[0] is not null)
                    {
                        text = args[0] is RawError r ? r.ReadAsString() : JsonSerializer.Serialize(args[0]);
                        if (m.Name != "TryGetRawError") break;
                    }
                }
            }
        }
        catch (Exception)
        {
            // fall through to the generic text
        }

        text = string.IsNullOrWhiteSpace(text) ? ex.StatusCode.ToString() : text;
        return text.Length > 400 ? text[..400] : text;
    }
}
