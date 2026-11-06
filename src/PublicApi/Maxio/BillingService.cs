using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
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
using Microsoft.Extensions.Options;
using MaxioProduct = MaxioAdvancedBilling.Models.Product;
using MaxioSubscription = MaxioAdvancedBilling.Models.Subscription;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Billing use cases. Maxio is the system of record: every call goes to Maxio through the SDK.
/// </summary>
public class BillingService
{
    private const string BRAND_PREFIX = "Brand:";
    private const int PAGE_SIZE = 200;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly IOptions<MaxioSettings> _settings;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IReadRepository<CatalogBrand> _brands;

    public BillingService(MaxioAdvancedBillingClient client, IOptions<MaxioSettings> settings,
        UserManager<ApplicationUser> userManager, IReadRepository<CatalogBrand> brands)
    {
        _client = client;
        _settings = settings;
        _userManager = userManager;
        _brands = brands;
    }

    // ---------------------------------------------------------------- plans

    public async Task<IReadOnlyList<PlanDto>> ListPlansAsync()
    {
        var products = await ListSubscribableProductsAsync();
        return products.Select(ToPlan).ToList();
    }

    public async Task<PlanDto> CreatePlanAsync(string? name, decimal? price, string? brand)
    {
        if (string.IsNullOrWhiteSpace(name)) throw Bad("A plan name is required.");
        name = name.Trim();
        if (name.Length > 100) throw Bad("The plan name must be at most 100 characters.");
        if (price is null) throw Bad("A monthly price is required.");
        var cents = ToCents(price.Value, "price");
        if (string.IsNullOrWhiteSpace(brand)) throw Bad("A brand is required.");

        var brands = await _brands.ListAsync();
        var catalogBrand = brands.FirstOrDefault(b => string.Equals(b.Brand, brand.Trim(), StringComparison.OrdinalIgnoreCase));
        if (catalogBrand is null)
            throw Bad($"'{brand.Trim()}' is not a brand in the catalog. Known brands: {string.Join(", ", brands.Select(b => b.Brand))}.");

        var handle = Slugify(name);
        if (handle.Length == 0) throw Bad("The plan name must contain letters or digits.");

        await EnsureHandleAndNameFreeAsync(handle, name);
        var family = await GetProductFamilyIdAsync();

        var created = await Call(() => _client.Products.CreateProduct(new CreateProductRequest
        {
            ProductFamilyId = family.ToString(CultureInfo.InvariantCulture),
            Body = new CreateOrUpdateProductRequest
            {
                Product = new CreateOrUpdateProduct
                {
                    Name = name,
                    Handle = handle,
                    Description = $"{BRAND_PREFIX} {catalogBrand.Brand}",
                    RequireCreditCard = false,
                    PriceInCents = cents,
                    Interval = 1,
                    IntervalUnit = IntervalUnit.Month
                }
            }
        }));
        return ToPlan(created.Product);
    }

    public async Task<CurrencyPriceDto> AddCurrencyPriceAsync(int planId, string? currency, decimal? price)
    {
        if (string.IsNullOrWhiteSpace(currency) || !Regex.IsMatch(currency.Trim(), "^[A-Za-z]{3}$"))
            throw Bad("A three-letter currency code (for example EUR) is required.");
        if (price is null) throw Bad("A price is required.");
        if (price < 0) throw Bad("The price cannot be negative.");
        if (price != decimal.Truncate(price.Value))
            throw Bad("Currency prices are sent to Maxio in whole currency units; the price must not have a fractional part.");
        if (price > int.MaxValue) throw Bad("The price is too large.");
        currency = currency.Trim().ToUpperInvariant();

        var product = (await Call(() => _client.Products.ReadProduct(new ReadProductRequest { ProductId = planId }), $"Plan {planId} was not found.")).Product;
        if (product.ArchivedAt is not null) throw new BillingException(HttpStatusCode.Conflict, "The plan is archived.");
        var pricePointId = product.ProductPricePointId ?? product.DefaultProductPricePointId;
        if (pricePointId is null) throw new BillingException(HttpStatusCode.Conflict, "The plan has no price point to attach a currency price to.");

        var result = await Call(() => _client.ProductPricePoints.CreateProductCurrencyPrices(new CreateProductCurrencyPricesOperationRequest
        {
            ProductPricePointId = pricePointId.Value,
            Body = new CreateProductCurrencyPricesRequest
            {
                CurrencyPrices = new[]
                {
                    new CreateProductCurrencyPrice { Currency = currency, Price = (int)price.Value, Role = CurrencyPriceRole.Baseline }
                }
            }
        }));
        var saved = result.CurrencyPrices.FirstOrDefault(c => string.Equals(c.Currency, currency, StringComparison.OrdinalIgnoreCase));
        return new CurrencyPriceDto { PlanId = planId, Currency = currency, Price = saved?.Price is { } p ? (decimal)p : price.Value };
    }

    // -------------------------------------------------------- subscriptions

    public async Task<SubscriptionDto> SubscribeAsync(string userName, string? planHandle)
    {
        if (string.IsNullOrWhiteSpace(planHandle)) throw Bad("A plan handle is required.");
        var product = await GetSubscribablePlanAsync(planHandle.Trim());
        var customer = await GetOrCreateCustomerAsync(userName);

        var existing = await ListCustomerSubscriptionsAsync(customer.Id!.Value);
        if (existing.Any(s => string.Equals(s.Product?.Handle, product.Handle, StringComparison.OrdinalIgnoreCase) && IsLive(s)))
            throw new BillingException(HttpStatusCode.Conflict, $"You already have a subscription to plan '{product.Handle}'.");

        var created = await Call(() => _client.Subscriptions.CreateSubscription(new CreateSubscriptionOperationRequest
        {
            Body = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = product.Handle,
                    CustomerId = customer.Id,
                    // no card details: the subscriber is invoiced and pays by remittance
                    PaymentCollectionMethod = CollectionMethod.Remittance
                }
            }
        }));
        return ToDto(created.Subscription);
    }

    public async Task<SubscriptionDto> GetSubscriptionAsync(string userName, int subscriptionId) =>
        ToDto(await GetOwnedAsync(userName, subscriptionId));

    public async Task<SubscriptionDto> PauseAsync(string userName, int subscriptionId)
    {
        var sub = await GetOwnedAsync(userName, subscriptionId);
        if (!IsOneOf(sub, SubscriptionState.Active, SubscriptionState.Trialing, SubscriptionState.PastDue, SubscriptionState.Unpaid, SubscriptionState.SoftFailure))
            throw Conflict(sub, "paused");
        var res = await Call(() => _client.SubscriptionStatus.PauseSubscription(new PauseSubscriptionRequest
        {
            SubscriptionId = subscriptionId,
            Body = new PauseRequest { Hold = new AutoResume() }
        }));
        return ToDto(res.Subscription);
    }

    public async Task<SubscriptionDto> ResumeAsync(string userName, int subscriptionId)
    {
        var sub = await GetOwnedAsync(userName, subscriptionId);
        if (!IsOneOf(sub, SubscriptionState.OnHold, SubscriptionState.Paused))
            throw new BillingException(HttpStatusCode.Conflict, $"Only a paused subscription can be resumed; this one is {StateName(sub)}.");
        var res = await Call(() => _client.SubscriptionStatus.ResumeSubscription(new ResumeSubscriptionRequest { SubscriptionId = subscriptionId }));
        return ToDto(res.Subscription);
    }

    public async Task<SubscriptionDto> CancelAsync(string userName, int subscriptionId)
    {
        var sub = await GetOwnedAsync(userName, subscriptionId);
        if (IsOneOf(sub, SubscriptionState.Canceled, SubscriptionState.Expired))
            throw new BillingException(HttpStatusCode.Conflict, $"The subscription is already {StateName(sub)}.");
        var res = await Call(() => _client.SubscriptionStatus.CancelSubscription(new CancelSubscriptionRequest
        {
            SubscriptionId = subscriptionId,
            Body = new CancellationRequest { Subscription = new CancellationOptions { CancellationMessage = "Canceled by the subscriber" } }
        }));
        return ToDto(res.Subscription);
    }

    public async Task<SubscriptionDto> RestartAsync(string userName, int subscriptionId)
    {
        var sub = await GetOwnedAsync(userName, subscriptionId);
        if (!IsOneOf(sub, SubscriptionState.Canceled))
            throw new BillingException(HttpStatusCode.Conflict, $"Only a canceled subscription can be restarted; this one is {StateName(sub)}.");
        return ToDto(await ReactivateAsync(subscriptionId));
    }

    public async Task<SubscriptionDto> SwitchPlanAsync(string userName, int subscriptionId, string? planHandle)
    {
        if (string.IsNullOrWhiteSpace(planHandle)) throw Bad("A plan handle is required.");
        var sub = await GetOwnedAsync(userName, subscriptionId);
        var product = await GetSubscribablePlanAsync(planHandle.Trim());
        if (IsOneOf(sub, SubscriptionState.Expired))
            throw new BillingException(HttpStatusCode.Conflict, "An expired subscription cannot be switched.");
        var samePlan = string.Equals(sub.Product?.Handle, product.Handle, StringComparison.OrdinalIgnoreCase);
        if (samePlan && !IsOneOf(sub, SubscriptionState.Canceled))
            throw new BillingException(HttpStatusCode.Conflict, $"The subscription is already on plan '{product.Handle}'.");

        if (IsOneOf(sub, SubscriptionState.Canceled))
        {
            // Maxio only migrates live subscriptions, so a canceled one is brought back first.
            var reactivated = await ReactivateAsync(subscriptionId);
            if (samePlan) return ToDto(reactivated);
        }
        else if (IsOneOf(sub, SubscriptionState.OnHold, SubscriptionState.Paused))
        {
            throw new BillingException(HttpStatusCode.Conflict, "A paused subscription must be resumed before it can be switched.");
        }

        var res = await Call(() => _client.SubscriptionProducts.MigrateSubscriptionProduct(new MigrateSubscriptionProductRequest
        {
            SubscriptionId = subscriptionId,
            Body = new SubscriptionProductMigrationRequest
            {
                Migration = new SubscriptionProductMigration { ProductHandle = product.Handle }
            }
        }));
        return ToDto(res.Subscription);
    }

    public async Task<DailyUsageDto> GetUsageAsync(string userName, int subscriptionId, DateOnly date)
    {
        await GetOwnedAsync(userName, subscriptionId);

        var components = await Call(() => _client.SubscriptionComponents.ListSubscriptionComponents(new ListSubscriptionComponentsRequest
        {
            SubscriptionId = subscriptionId
        }));
        var metered = components.Select(c => c.Component).Where(c => c is not null && c.ComponentId is not null &&
            (c.Kind == ComponentKind.MeteredComponent || c.Kind == ComponentKind.PrepaidUsageComponent)).ToList();

        var result = new DailyUsageDto { SubscriptionId = subscriptionId, Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        foreach (var component in metered)
        {
            var dto = new ComponentUsageDto { ComponentId = component!.ComponentId!.Value, Name = component.Name, UnitName = component.UnitName };
            for (var page = 1; ; page++)
            {
                var current = page;
                // The window is widened by a day on each side and filtered below on the site-local date of
                // each record, so the answer does not depend on how Maxio treats the window's edges.
                var usages = await Call(() => _client.SubscriptionComponents.ListUsages(new ListUsagesRequest
                {
                    SubscriptionIdOrReference = subscriptionId,
                    ComponentId = dto.ComponentId,
                    SinceDate = new DateTimeOffset(date.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                    UntilDate = new DateTimeOffset(date.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                    Page = current,
                    PerPage = PAGE_SIZE
                }));
                foreach (var u in usages.Select(r => r.Usage))
                {
                    if (u.CreatedAt is null || DateOnly.FromDateTime(u.CreatedAt.Value.DateTime) != date) continue;
                    var qty = ParseQuantity(u.Quantity);
                    dto.Quantity += qty;
                    dto.Entries.Add(new UsageEntryDto { Id = u.Id, Quantity = qty.ToString(CultureInfo.InvariantCulture), Memo = u.Memo, RecordedAt = u.CreatedAt });
                }
                if (usages.Count < PAGE_SIZE) break;
            }
            result.Components.Add(dto);
        }
        result.TotalQuantity = result.Components.Sum(c => c.Quantity);
        return result;
    }

    public async Task<SubscriptionDto> SetNextBillingDateAsync(int subscriptionId, DateOnly? date)
    {
        if (date is null) throw Bad("A date (YYYY-MM-DD) is required.");
        if (date.Value <= DateOnly.FromDateTime(DateTime.UtcNow))
            throw Bad("The next billing date must be in the future.");

        var sub = await GetByIdAsync(subscriptionId);
        if (!IsOneOf(sub, SubscriptionState.Active, SubscriptionState.Trialing, SubscriptionState.PastDue, SubscriptionState.Unpaid, SubscriptionState.SoftFailure))
            throw new BillingException(HttpStatusCode.Conflict, $"The next billing date cannot be set on a subscription that is {StateName(sub)}.");

        // Noon UTC keeps the calendar date the same in any site time zone.
        var at = new DateTimeOffset(date.Value.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        var res = await Call(() => _client.Subscriptions.UpdateSubscription(new UpdateSubscriptionOperationRequest
        {
            SubscriptionId = subscriptionId,
            Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { NextBillingAt = at } }
        }));
        return ToDto(res.Subscription);
    }

    // -------------------------------------------------------------- helpers

    private async Task<MaxioSubscription> ReactivateAsync(int subscriptionId)
    {
        var res = await Call(() => _client.SubscriptionStatus.ReactivateSubscription(new ReactivateSubscriptionOperationRequest
        {
            SubscriptionId = subscriptionId,
            Body = new ReactivateSubscriptionRequest()
        }));
        return res.Subscription;
    }

    private async Task<MaxioSubscription> GetByIdAsync(int subscriptionId)
    {
        var res = await Call(() => _client.Subscriptions.ReadSubscription(new ReadSubscriptionRequest { SubscriptionId = subscriptionId }),
            $"Subscription {subscriptionId} was not found.");
        return res.Subscription;
    }

    private async Task<MaxioSubscription> GetOwnedAsync(string userName, int subscriptionId)
    {
        var sub = await GetByIdAsync(subscriptionId);
        if (!string.Equals(sub.Customer?.Reference, userName, StringComparison.OrdinalIgnoreCase))
            throw new BillingException(HttpStatusCode.NotFound, $"Subscription {subscriptionId} was not found.");
        return sub;
    }

    private async Task<Customer> GetOrCreateCustomerAsync(string userName)
    {
        EnsureConfigured();
        try
        {
            return (await _client.Customers.ReadCustomerByReference(new ReadCustomerByReferenceRequest { Reference = userName })).Customer;
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // first subscription of this shopper: create the customer below
        }
        catch (ApiException ex)
        {
            throw Map(ex);
        }
        catch (SdkException ex)
        {
            throw Unavailable(ex);
        }

        var user = await _userManager.FindByNameAsync(userName);
        var email = user?.Email ?? (userName.Contains('@') ? userName : $"{userName}@example.invalid");
        var local = email.Split('@')[0];
        var created = await Call(() => _client.Customers.CreateCustomer(new CreateCustomerOperationRequest
        {
            Body = new CreateCustomerRequest
            {
                Customer = new CreateCustomer { FirstName = local, LastName = "(eShopOnWeb)", Email = email, Reference = userName }
            }
        }));
        return created.Customer;
    }

    private async Task<List<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId)
    {
        var all = new List<MaxioSubscription>();
        for (var page = 1; ; page++)
        {
            var current = page;
            var batch = await Call(() => _client.Subscriptions.ListSubscriptions(new ListSubscriptionsRequest
            {
                CustomerId = customerId,
                Page = current,
                PerPage = PAGE_SIZE
            }));
            all.AddRange(batch.Select(b => b.Subscription));
            if (batch.Count < PAGE_SIZE) return all;
        }
    }

    private async Task<List<MaxioProduct>> ListSubscribableProductsAsync()
    {
        var all = new List<MaxioProduct>();
        for (var page = 1; ; page++)
        {
            var current = page;
            var batch = await Call(() => _client.Products.ListProducts(new ListProductsRequest { Page = current, PerPage = PAGE_SIZE, IncludeArchived = false }));
            all.AddRange(batch.Select(b => b.Product));
            if (batch.Count < PAGE_SIZE) break;
        }
        // Shoppers give no card details, so plans that insist on a card are not on offer.
        return all.Where(IsSubscribable).OrderBy(p => p.Id).ToList();
    }

    private async Task<MaxioProduct> GetSubscribablePlanAsync(string handle)
    {
        EnsureConfigured();
        MaxioProduct product;
        try
        {
            product = (await _client.Products.ReadProductByHandle(new ReadProductByHandleRequest { ApiHandle = handle })).Product;
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new BillingException(HttpStatusCode.NotFound, $"Plan '{handle}' was not found.");
        }
        catch (ApiException ex)
        {
            throw Map(ex);
        }
        catch (SdkException ex)
        {
            throw Unavailable(ex);
        }
        if (!IsSubscribable(product))
            throw new BillingException(HttpStatusCode.NotFound, $"Plan '{handle}' is not available for subscription.");
        return product;
    }

    private async Task EnsureHandleAndNameFreeAsync(string handle, string name)
    {
        var all = new List<MaxioProduct>();
        for (var page = 1; ; page++)
        {
            var current = page;
            var batch = await Call(() => _client.Products.ListProducts(new ListProductsRequest { Page = current, PerPage = PAGE_SIZE, IncludeArchived = true }));
            all.AddRange(batch.Select(b => b.Product));
            if (batch.Count < PAGE_SIZE) break;
        }
        if (all.Any(p => string.Equals(p.Handle, handle, StringComparison.OrdinalIgnoreCase)))
            throw new BillingException(HttpStatusCode.Conflict, $"A plan with handle '{handle}' already exists.");
        if (all.Any(p => p.ArchivedAt is null && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new BillingException(HttpStatusCode.Conflict, $"A plan named '{name}' already exists.");
    }

    private async Task<int> GetProductFamilyIdAsync()
    {
        var families = await Call(() => _client.ProductFamilies.ListProductFamilies(new ListProductFamiliesRequest()));
        var family = families.Select(f => f.ProductFamily).FirstOrDefault(f => f?.Id is not null && f.ArchivedAt is null);
        return family?.Id ?? throw new BillingException(HttpStatusCode.Conflict, "The billing site has no product family to hold plans.");
    }

    private static bool IsSubscribable(MaxioProduct p) => p.ArchivedAt is null && p.RequireCreditCard != true && !string.IsNullOrEmpty(p.Handle);

    private static bool IsLive(MaxioSubscription s) => !IsOneOf(s, SubscriptionState.Canceled, SubscriptionState.Expired, SubscriptionState.FailedToCreate);

    private static bool IsOneOf(MaxioSubscription s, params SubscriptionState[] states) => s.State is { } st && states.Contains(st);

    private static string StateName(MaxioSubscription s) => s.State?.Value ?? "in an unknown state";

    private static BillingException Conflict(MaxioSubscription s, string verb) =>
        new(HttpStatusCode.Conflict, $"The subscription cannot be {verb} while it is {StateName(s)}.");

    private static BillingException Bad(string message) => new(HttpStatusCode.BadRequest, message);

    private static long ToCents(decimal price, string field)
    {
        if (price < 0) throw Bad($"The {field} cannot be negative.");
        var cents = price * 100m;
        if (cents != decimal.Truncate(cents)) throw Bad($"The {field} can have at most two decimal places.");
        if (cents > 100_000_000_000m) throw Bad($"The {field} is too large.");
        return (long)cents;
    }

    private static decimal ParseQuantity(MaxioAdvancedBilling.Models.AnyOf.Quantity1? q)
    {
        if (q is null) return 0;
        if (q.TryGetInt(out var i)) return i;
        if (q.TryGetString(out var s) && decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return d;
        return 0;
    }

    private static string Slugify(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            else if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }

    private static PlanDto ToPlan(MaxioProduct p)
    {
        string? brand = null;
        var d = p.Description?.Trim();
        if (d is not null && d.StartsWith(BRAND_PREFIX, StringComparison.OrdinalIgnoreCase))
            brand = d.Substring(BRAND_PREFIX.Length).Trim();
        return new PlanDto
        {
            Id = p.Id ?? 0,
            Handle = p.Handle,
            Name = p.Name,
            Brand = brand,
            PriceInCents = p.PriceInCents ?? 0,
            Price = (p.PriceInCents ?? 0) / 100m,
            Interval = p.Interval ?? 1,
            IntervalUnit = p.IntervalUnit?.Value
        };
    }

    private static SubscriptionDto ToDto(MaxioSubscription s)
    {
        var balance = s.BalanceInCents ?? 0;
        return new SubscriptionDto
        {
            Id = s.Id ?? 0,
            State = s.State?.Value,
            PlanHandle = s.Product?.Handle,
            PlanName = s.Product?.Name,
            PlanId = s.Product?.Id,
            Paid = balance <= 0,
            BalanceDue = Math.Max(balance, 0) / 100m,
            NextBillingAt = s.NextAssessmentAt,
            CreatedAt = s.CreatedAt,
            CanceledAt = s.CanceledAt
        };
    }

    private void EnsureConfigured()
    {
        var s = _settings.Value;
        if (string.IsNullOrWhiteSpace(s.ApiKey) || (string.IsNullOrWhiteSpace(s.Subdomain) && string.IsNullOrWhiteSpace(s.BaseUrl)))
            throw new BillingException(HttpStatusCode.ServiceUnavailable, "Billing is not configured: set Maxio:ApiKey and Maxio:Subdomain.");
    }

    private async Task<T> Call<T>(Func<Task<T>> call, string? notFoundMessage = null)
    {
        EnsureConfigured();
        try
        {
            return await call();
        }
        catch (ApiException ex)
        {
            throw Map(ex, notFoundMessage);
        }
        catch (SdkException ex)
        {
            throw Unavailable(ex);
        }
    }

    private static BillingException Unavailable(Exception ex) =>
        new(HttpStatusCode.BadGateway, "The billing system could not be reached or returned an unreadable answer.", ex);

    private static BillingException Map(ApiException ex, string? notFoundMessage = null)
    {
        var reason = MaxioErrorText.From(ex);
        return ex.StatusCode switch
        {
            HttpStatusCode.NotFound => new BillingException(HttpStatusCode.NotFound, notFoundMessage ?? reason, ex),
            HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest => new BillingException(HttpStatusCode.UnprocessableEntity, reason, ex),
            HttpStatusCode.Conflict => new BillingException(HttpStatusCode.Conflict, reason, ex),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new BillingException(HttpStatusCode.BadGateway, "The billing system rejected the app's credentials.", ex),
            _ => new BillingException(HttpStatusCode.BadGateway, $"The billing system failed the request ({(int)ex.StatusCode}). {reason}", ex)
        };
    }
}
