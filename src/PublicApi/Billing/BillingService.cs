using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Subscription billing on top of Maxio (the billing system of record).
/// A shopper is a Maxio customer whose reference is the shop user name taken from the JWT.
/// </summary>
public class BillingService
{
    private const int PAGE_SIZE = 200;

    // Resolved on first use, so a missing Maxio configuration only affects calls that need Maxio.
    private readonly Lazy<MaxioAdvancedBillingClient> _client;
    private MaxioAdvancedBillingClient _maxio => _client.Value;
    private readonly IReadRepository<CatalogBrand> _brands;
    private readonly UserManager<ApplicationUser> _userManager;

    public BillingService(IServiceProvider services, IReadRepository<CatalogBrand> brands,
        UserManager<ApplicationUser> userManager)
    {
        _client = new Lazy<MaxioAdvancedBillingClient>(() => services.GetRequiredService<MaxioAdvancedBillingClient>());
        _brands = brands;
        _userManager = userManager;
    }

    // ---------------------------------------------------------------- plans

    public async Task<IReadOnlyList<PlanDto>> ListPlansAsync()
    {
        var brands = await _brands.ListAsync();
        var products = new List<Product>();
        for (var page = 1; ; page++)
        {
            var batch = await Call(() => _maxio.Products.ListProducts(
                new MaxioAdvancedBilling.Requests.Products.ListProductsRequest { Page = page, PerPage = PAGE_SIZE }));
            products.AddRange(batch.Select(b => b.Product).Where(p => p != null).Select(p => p!));
            if (batch.Count < PAGE_SIZE) break;
        }

        return products
            .Where(p => p.ArchivedAt == null && !string.IsNullOrWhiteSpace(p.Handle))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => ToPlan(p, brands))
            .ToList();
    }

    public async Task<PlanDto> CreatePlanAsync(CreatePlanRequest request)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            throw new BillingException(400, "A plan name is required.");
        if (request.Price is null || request.Price < 0)
            throw new BillingException(400, "A monthly price of zero or more is required.");
        if (decimal.Round(request.Price.Value, 2) != request.Price.Value)
            throw new BillingException(400, "The monthly price can have at most two decimal places.");
        if (request.BrandId is null)
            throw new BillingException(400, "A brand is required.");

        var brand = await _brands.GetByIdAsync(request.BrandId.Value);
        if (brand == null)
            throw new BillingException(400, $"Brand {request.BrandId} is not in the catalog.");

        var existingPlans = await ListPlansAsync();
        if (existingPlans.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new BillingException(409, $"A plan named '{name}' already exists.");

        var priceInCents = (long)(request.Price.Value * 100m);
        var familyId = await EnsureBrandFamilyAsync(brand);
        var handle = await UniqueProductHandleAsync(Slugify(name));

        var created = await Call(() => _maxio.Products.CreateProduct(new MaxioAdvancedBilling.Requests.Products.CreateProductRequest
        {
            ProductFamilyId = familyId.ToString(CultureInfo.InvariantCulture),
            Body = new CreateOrUpdateProductRequest
            {
                Product = new CreateOrUpdateProduct
                {
                    Name = name,
                    Handle = handle,
                    Description = $"{name} - monthly {brand.Brand} plan",
                    PriceInCents = priceInCents,
                    Interval = 1,
                    IntervalUnit = IntervalUnit.Month,
                    RequireCreditCard = false,
                }
            }
        }));

        return ToPlan(created.Product!, await _brands.ListAsync());
    }

    public async Task<CurrencyPriceDto> SetCurrencyPriceAsync(int planId, CurrencyPriceRequest request)
    {
        var currency = request.Currency?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(currency) || !Regex.IsMatch(currency, "^[A-Z]{3}$"))
            throw new BillingException(400, "A three-letter ISO currency code is required.");
        if (request.Price is null || request.Price <= 0)
            throw new BillingException(400, "A price greater than zero is required.");
        if (decimal.Round(request.Price.Value, 2) != request.Price.Value)
            throw new BillingException(400, "The price can have at most two decimal places.");

        var product = (await Call(() => _maxio.Products.ReadProduct(
            new MaxioAdvancedBilling.Requests.Products.ReadProductRequest { ProductId = planId }))).Product!;
        if (product.ArchivedAt != null)
            throw new BillingException(409, "The plan is archived.");
        var pricePointId = product.DefaultProductPricePointId ?? product.ProductPricePointId
            ?? throw new BillingException(409, "The plan has no price point to add a currency price to.");

        var price = request.Price.Value;
        var pricePoint = (await Call(() => _maxio.ProductPricePoints.ReadProductPricePoint(
            new MaxioAdvancedBilling.Requests.ProductPricePoints.ReadProductPricePointRequest
            {
                ProductId = planId,
                PricePointId = pricePointId,
                CurrencyPrices = true
            }))).PricePoint;
        var existing = pricePoint.CurrencyPrices?.FirstOrDefault(c =>
            string.Equals(c.Currency, currency, StringComparison.OrdinalIgnoreCase) &&
            (c.Role == null || c.Role == CurrencyPriceRole.Baseline));

        CurrencyPrice? result = null;
        if (existing == null)
        {
            // The create operation takes whole currency units only; refine afterwards if needed.
            var created = await Call(() => _maxio.ProductPricePoints.CreateProductCurrencyPrices(
                new MaxioAdvancedBilling.Requests.ProductPricePoints.CreateProductCurrencyPricesOperationRequest
                {
                    ProductPricePointId = pricePointId,
                    Body = new CreateProductCurrencyPricesRequest
                    {
                        CurrencyPrices = new[]
                        {
                            new CreateProductCurrencyPrice
                            {
                                Currency = currency,
                                Price = (int)Math.Round(price, MidpointRounding.AwayFromZero),
                                Role = CurrencyPriceRole.Baseline
                            }
                        }
                    }
                }));
            result = created.CurrencyPrices?.FirstOrDefault(c =>
                string.Equals(c.Currency, currency, StringComparison.OrdinalIgnoreCase));
            existing = result;
        }

        var rowId = existing?.Id;
        if (rowId != null && (result == null || (decimal)(result.Price ?? 0) != price))
        {
            var updated = await Call(() => _maxio.ProductPricePoints.UpdateProductCurrencyPrices(
                new MaxioAdvancedBilling.Requests.ProductPricePoints.UpdateProductCurrencyPricesRequest
                {
                    ProductPricePointId = pricePointId,
                    Body = new UpdateCurrencyPricesRequest
                    {
                        CurrencyPrices = new[] { new UpdateCurrencyPrice { Id = rowId.Value, Price = (double)price } }
                    }
                }));
            result = updated.CurrencyPrices?.FirstOrDefault(c => c.Id == rowId) ?? result;
        }

        return new CurrencyPriceDto { PlanId = planId, Currency = currency, Price = result?.Price ?? (double)price };
    }

    // -------------------------------------------------------- subscriptions

    public async Task<SubscriptionDto> SubscribeAsync(string userName, string? planHandle)
    {
        var product = await FindAvailablePlanAsync(planHandle);
        var customerId = await EnsureCustomerAsync(userName);

        var response = await Call(() => _maxio.Subscriptions.CreateSubscription(
            new MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest
            {
                Body = new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = product.Handle,
                        CustomerId = customerId,
                        // No card details: the shopper is invoiced.
                        PaymentCollectionMethod = CollectionMethod.Remittance,
                    }
                }
            }));
        return ToDto(response.Subscription!);
    }

    public async Task<SubscriptionDto> GetSubscriptionAsync(string userName, int subscriptionId) =>
        ToDto(await GetOwnedAsync(userName, subscriptionId));

    public async Task<SubscriptionDto> PauseAsync(string userName, int subscriptionId)
    {
        var sub = await GetOwnedAsync(userName, subscriptionId);
        if (sub.State == SubscriptionState.OnHold)
            throw new BillingException(409, "The subscription is already paused.");
        RequireState(sub, "paused", SubscriptionState.Active, SubscriptionState.Trialing);

        var response = await Call(() => _maxio.SubscriptionStatus.PauseSubscription(
            new MaxioAdvancedBilling.Requests.SubscriptionStatus.PauseSubscriptionRequest { SubscriptionId = subscriptionId }));
        return ToDto(response.Subscription!);
    }

    public async Task<SubscriptionDto> ResumeAsync(string userName, int subscriptionId)
    {
        var sub = await GetOwnedAsync(userName, subscriptionId);
        if (sub.State != SubscriptionState.OnHold)
            throw new BillingException(409, $"Only a paused subscription can be resumed; this one is {StateName(sub)}.");

        var response = await Call(() => _maxio.SubscriptionStatus.ResumeSubscription(
            new MaxioAdvancedBilling.Requests.SubscriptionStatus.ResumeSubscriptionRequest { SubscriptionId = subscriptionId }));
        return ToDto(response.Subscription!);
    }

    public async Task<SubscriptionDto> CancelAsync(string userName, int subscriptionId)
    {
        var sub = await GetOwnedAsync(userName, subscriptionId);
        if (sub.State == SubscriptionState.Canceled)
            throw new BillingException(409, "The subscription is already cancelled.");
        if (sub.State == SubscriptionState.Expired)
            throw new BillingException(409, "The subscription has expired.");

        var response = await Call(() => _maxio.SubscriptionStatus.CancelSubscription(CancelRequest(subscriptionId, "Cancelled by the subscriber")));
        return ToDto(response.Subscription!);
    }

    public async Task<SubscriptionDto> RestartAsync(string userName, int subscriptionId)
    {
        var sub = await GetOwnedAsync(userName, subscriptionId);
        if (sub.State != SubscriptionState.Canceled)
            throw new BillingException(409, $"Only a cancelled subscription can be restarted; this one is {StateName(sub)}.");

        return ToDto(await Reactivate(subscriptionId));
    }

    public async Task<SubscriptionDto> SwitchPlanAsync(string userName, int subscriptionId, string? planHandle)
    {
        var sub = await GetOwnedAsync(userName, subscriptionId);
        var target = await FindAvailablePlanAsync(planHandle);
        if (target.Id == sub.Product?.Id)
            throw new BillingException(409, "The subscription is already on that plan.");

        var wasCanceled = sub.State == SubscriptionState.Canceled;
        var wasPaused = sub.State == SubscriptionState.OnHold;
        if (wasCanceled)
        {
            // Maxio only migrates live subscriptions, so a cancelled one is restarted first.
            await Reactivate(subscriptionId);
        }
        else if (wasPaused)
        {
            // ...and refuses to migrate one that is on hold, so resume it for the switch.
            await Call(() => _maxio.SubscriptionStatus.ResumeSubscription(
                new MaxioAdvancedBilling.Requests.SubscriptionStatus.ResumeSubscriptionRequest { SubscriptionId = subscriptionId }));
        }
        else
        {
            RequireState(sub, "switched to another plan", SubscriptionState.Active, SubscriptionState.Trialing,
                SubscriptionState.PastDue, SubscriptionState.SoftFailure, SubscriptionState.Unpaid);
        }

        Subscription migrated;
        try
        {
            migrated = (await Call(() => _maxio.SubscriptionProducts.MigrateSubscriptionProduct(
                new MaxioAdvancedBilling.Requests.SubscriptionProducts.MigrateSubscriptionProductRequest
                {
                    SubscriptionId = subscriptionId,
                    Body = new SubscriptionProductMigrationRequest
                    {
                        Migration = new SubscriptionProductMigration { ProductHandle = target.Handle }
                    }
                }))).Subscription!;
        }
        catch when (wasCanceled || wasPaused)
        {
            // The switch did not happen: leave the subscription as the shopper had it.
            await TryRestoreAsync(subscriptionId, wasCanceled);
            throw;
        }

        if (wasPaused)
        {
            // A paused subscription stays paused on its new plan (best effort).
            var paused = await TryRestoreAsync(subscriptionId, restoreCanceled: false);
            if (paused != null) return ToDto(paused);
        }
        return ToDto(migrated);
    }

    private async Task<Subscription?> TryRestoreAsync(int subscriptionId, bool restoreCanceled)
    {
        try
        {
            if (restoreCanceled)
            {
                return (await Call(() => _maxio.SubscriptionStatus.CancelSubscription(CancelRequest(subscriptionId, "Plan switch failed")))).Subscription;
            }
            return (await Call(() => _maxio.SubscriptionStatus.PauseSubscription(
                new MaxioAdvancedBilling.Requests.SubscriptionStatus.PauseSubscriptionRequest { SubscriptionId = subscriptionId }))).Subscription;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<UsageDto> GetUsageAsync(string userName, int subscriptionId, DateOnly date)
    {
        await GetOwnedAsync(userName, subscriptionId);

        var components = await Call(() => _maxio.SubscriptionComponents.ListSubscriptionComponents(
            new MaxioAdvancedBilling.Requests.SubscriptionComponents.ListSubscriptionComponentsRequest { SubscriptionId = subscriptionId }));

        var result = new UsageDto { SubscriptionId = subscriptionId, Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };

        // Maxio's since/until date filters are date-granular and their boundary handling is not
        // something to rely on, so ask for a day either side and keep what falls on the day itself
        // (in the site's own time zone, which is the offset Maxio stamps its timestamps with).
        var since = new DateTimeOffset(date.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var until = new DateTimeOffset(date.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var usageComponents = components.Select(c => c.Component).Where(c => c != null && c.ComponentId != null &&
            (c.Kind == ComponentKind.MeteredComponent || c.Kind == ComponentKind.PrepaidUsageComponent));

        foreach (var component in usageComponents)
        {
            var componentId = component!.ComponentId!.Value;
            var entries = new List<Usage>();
            for (var page = 1; ; page++)
            {
                var batch = await Call(() => _maxio.SubscriptionComponents.ListUsages(
                    new MaxioAdvancedBilling.Requests.SubscriptionComponents.ListUsagesRequest
                    {
                        SubscriptionIdOrReference = subscriptionId,
                        ComponentId = componentId,
                        SinceDate = since,
                        UntilDate = until,
                        Page = page,
                        PerPage = PAGE_SIZE
                    }));
                entries.AddRange(batch.Select(u => u.Usage).Where(u => u != null).Select(u => u!));
                if (batch.Count < PAGE_SIZE) break;
            }

            var onDay = entries
                .Where(u => u.CreatedAt != null && DateOnly.FromDateTime(u.CreatedAt.Value.DateTime) == date)
                .OrderBy(u => u.CreatedAt)
                .Select(u => new UsageEntryDto { Id = u.Id, Quantity = ToDecimal(u.Quantity), Memo = u.Memo, CreatedAt = u.CreatedAt })
                .ToList();

            result.Components.Add(new ComponentUsageDto
            {
                ComponentId = componentId,
                ComponentHandle = component.ComponentHandle,
                Name = component.Name,
                UnitName = component.UnitName,
                TotalQuantity = onDay.Sum(e => e.Quantity),
                Entries = onDay
            });
        }

        return result;
    }

    public async Task<SubscriptionDto> SetNextBillingDateAsync(int subscriptionId, string? dateText)
    {
        if (!DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new BillingException(400, "The date must be given as yyyy-MM-dd.");

        var sub = await ReadAsync(subscriptionId);
        if (sub.State != SubscriptionState.Active && sub.State != SubscriptionState.Trialing)
            throw new BillingException(409, $"The next billing date can only be set on an active subscription; this one is {StateName(sub)}.");

        // Express "that day" in the site's own time zone: the offset Maxio stamps this subscription with.
        var offset = sub.NextAssessmentAt?.Offset ?? sub.CurrentPeriodEndsAt?.Offset ?? sub.CreatedAt?.Offset ?? TimeSpan.Zero;
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(offset).DateTime);
        if (date <= today)
            throw new BillingException(422, "The next billing date must be in the future.");

        var billAt = new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), offset);
        var response = await Call(() => _maxio.Subscriptions.UpdateSubscription(
            new MaxioAdvancedBilling.Requests.Subscriptions.UpdateSubscriptionOperationRequest
            {
                SubscriptionId = subscriptionId,
                Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { NextBillingAt = billAt } }
            }));

        var updated = response.Subscription!;
        var applied = updated.NextAssessmentAt;
        if (applied == null || DateOnly.FromDateTime(applied.Value.DateTime) != date)
            throw new BillingException(502, "Maxio did not schedule the subscription's next billing on the requested date.");
        return ToDto(updated);
    }

    // -------------------------------------------------------------- helpers

    private static MaxioAdvancedBilling.Requests.SubscriptionStatus.CancelSubscriptionRequest CancelRequest(int subscriptionId, string message) =>
        new()
        {
            SubscriptionId = subscriptionId,
            Body = new CancellationRequest { Subscription = new CancellationOptions { CancellationMessage = message } }
        };

    private async Task<Subscription> ReadAsync(int subscriptionId)
    {
        var response = await Call(() => _maxio.Subscriptions.ReadSubscription(
            new MaxioAdvancedBilling.Requests.Subscriptions.ReadSubscriptionRequest { SubscriptionId = subscriptionId }));
        return response.Subscription ?? throw new BillingException(404, "Subscription not found.");
    }

    /// <summary>Reads a subscription, answering 404 if it is not the caller's.</summary>
    private async Task<Subscription> GetOwnedAsync(string userName, int subscriptionId)
    {
        var sub = await ReadAsync(subscriptionId);
        if (!string.Equals(sub.Customer?.Reference, userName, StringComparison.OrdinalIgnoreCase))
            throw new BillingException(404, "Subscription not found.");
        return sub;
    }

    private async Task<Subscription> Reactivate(int subscriptionId)
    {
        var response = await Call(() => _maxio.SubscriptionStatus.ReactivateSubscription(
            new MaxioAdvancedBilling.Requests.SubscriptionStatus.ReactivateSubscriptionOperationRequest
            {
                SubscriptionId = subscriptionId,
                // Pick the paid-up period back up when there is one, rather than billing a new period.
                Body = new ReactivateSubscriptionRequest { Resume = true }
            }));
        return response.Subscription!;
    }

    private async Task<Product> FindAvailablePlanAsync(string? planHandle)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new BillingException(400, "A plan handle is required.");

        Product product;
        try
        {
            product = (await Call(() => _maxio.Products.ReadProductByHandle(
                new MaxioAdvancedBilling.Requests.Products.ReadProductByHandleRequest { ApiHandle = planHandle.Trim() }))).Product!;
        }
        catch (BillingException e) when (e.StatusCode == 404)
        {
            throw new BillingException(404, $"Plan '{planHandle}' does not exist.");
        }

        if (product.ArchivedAt != null)
            throw new BillingException(409, $"Plan '{planHandle}' is no longer available.");
        return product;
    }

    private async Task<int> EnsureCustomerAsync(string userName)
    {
        try
        {
            var found = await Call(() => _maxio.Customers.ReadCustomerByReference(
                new MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest { Reference = userName }));
            if (found.Customer?.Id is int existingId) return existingId;
        }
        catch (BillingException e) when (e.StatusCode == 404)
        {
            // not a Maxio customer yet
        }

        var user = await _userManager.FindByNameAsync(userName);
        var email = user?.Email ?? (userName.Contains('@') ? userName : $"{userName}@example.invalid");
        var local = email.Split('@')[0];
        var created = await Call(() => _maxio.Customers.CreateCustomer(new MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest
        {
            Body = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = string.IsNullOrWhiteSpace(local) ? "Shopper" : local,
                    LastName = "Shopper",
                    Email = email,
                    Reference = userName,
                }
            }
        }));
        return created.Customer?.Id ?? throw new BillingException(502, "Maxio did not return the new customer.");
    }

    /// <summary>The Maxio product family that holds a brand's plans: "{brand} plans".</summary>
    private async Task<int> EnsureBrandFamilyAsync(CatalogBrand brand)
    {
        var name = $"{brand.Brand} plans";
        var handle = Slugify(name);
        var families = await Call(() => _maxio.ProductFamilies.ListProductFamilies(
            new MaxioAdvancedBilling.Requests.ProductFamilies.ListProductFamiliesRequest()));
        var match = families.Select(f => f.ProductFamily).FirstOrDefault(f => f != null && f.Id != null && f.ArchivedAt == null &&
            (string.Equals(f.Handle, handle, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)));
        if (match != null) return match.Id!.Value;

        var created = await Call(() => _maxio.ProductFamilies.CreateProductFamily(
            new MaxioAdvancedBilling.Requests.ProductFamilies.CreateProductFamilyOperationRequest
            {
                Body = new CreateProductFamilyRequest
                {
                    ProductFamily = new CreateProductFamily { Name = name, Handle = handle }
                }
            }));
        return created.ProductFamily?.Id ?? throw new BillingException(502, "Maxio did not return the new product family.");
    }

    private async Task<string> UniqueProductHandleAsync(string baseHandle)
    {
        if (baseHandle.Length == 0) baseHandle = "plan";
        for (var i = 1; i <= 50; i++)
        {
            var candidate = i == 1 ? baseHandle : $"{baseHandle}-{i}";
            try
            {
                await Call(() => _maxio.Products.ReadProductByHandle(
                    new MaxioAdvancedBilling.Requests.Products.ReadProductByHandleRequest { ApiHandle = candidate }));
            }
            catch (BillingException e) when (e.StatusCode == 404)
            {
                return candidate;
            }
        }
        throw new BillingException(409, "Could not find a free handle for the plan.");
    }

    private static string StateName(Subscription sub) => sub.State?.Value ?? "in an unknown state";

    private static void RequireState(Subscription sub, string action, params SubscriptionState[] allowed)
    {
        if (sub.State == null || !allowed.Contains(sub.State))
            throw new BillingException(409, $"The subscription is {StateName(sub)} and cannot be {action}.");
    }

    private static PlanDto ToPlan(Product p, IReadOnlyList<CatalogBrand> brands)
    {
        var family = p.ProductFamily?.Name;
        var brand = family == null ? null : brands.FirstOrDefault(b =>
            string.Equals($"{b.Brand} plans", family, StringComparison.OrdinalIgnoreCase));
        var cents = p.PriceInCents ?? 0;
        return new PlanDto
        {
            Id = p.Id ?? 0,
            Handle = p.Handle,
            Name = p.Name,
            Description = p.Description,
            PriceInCents = cents,
            Price = cents / 100m,
            Interval = p.Interval ?? 1,
            IntervalUnit = p.IntervalUnit?.Value,
            BrandId = brand?.Id,
            Brand = brand?.Brand,
        };
    }

    private static SubscriptionDto ToDto(Subscription s)
    {
        var balance = s.BalanceInCents ?? 0;
        return new SubscriptionDto
        {
            Id = s.Id ?? 0,
            State = s.State?.Value,
            PlanHandle = s.Product?.Handle,
            PlanName = s.Product?.Name,
            Paid = balance <= 0,
            BalanceInCents = balance,
            NextBillingAt = s.NextAssessmentAt,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
            CreatedAt = s.CreatedAt,
            CanceledAt = s.CanceledAt,
        };
    }

    private static decimal ToDecimal(MaxioAdvancedBilling.Models.AnyOf.Quantity1? q)
    {
        if (q == null) return 0;
        if (q.TryGetInt(out var i)) return i;
        if (q.TryGetString(out var s) && decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return d;
        return 0;
    }

    public static string Slugify(string value)
    {
        var sb = new StringBuilder();
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }

    /// <summary>Runs an SDK call, turning SDK failures into BillingExceptions with a 4xx where the request is at fault.</summary>
    private static async Task<T> Call<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (BillingException)
        {
            throw;
        }
        catch (ApiException e)
        {
            var status = (int)e.StatusCode;
            var reason = MaxioReason(e);
            throw status switch
            {
                404 => new BillingException(404, reason),
                400 or 409 or 422 => new BillingException(422, reason),
                401 or 403 => new BillingException(502, "Maxio rejected the application's credentials."),
                429 => new BillingException(503, "Maxio is rate limiting requests; try again shortly."),
                _ => new BillingException(502, "Maxio could not process the request: " + reason),
            };
        }
        catch (SdkTimeoutException)
        {
            throw new BillingException(504, "Maxio did not answer in time.");
        }
        catch (SdkConnectionException)
        {
            throw new BillingException(503, "Maxio is unreachable.");
        }
    }

    /// <summary>
    /// The reason Maxio gave, taken from its error body. (The exception message only names the URL
    /// and status.) Typed errors are read through their TryGet* accessors and flattened to text.
    /// </summary>
    private static string MaxioReason(ApiException e)
    {
        var fallback = (int)e.StatusCode == 404 ? "Not found in the billing system." : $"Maxio answered {(int)e.StatusCode}.";
        try
        {
            var error = e.GetType().GetProperty("Error")?.GetValue(e);
            if (error == null) return fallback;

            foreach (var method in error.GetType().GetMethods().Where(m => m.Name.StartsWith("TryGet", StringComparison.Ordinal)))
            {
                var parameters = method.GetParameters();
                if (parameters.Length != 1 || !parameters[0].IsOut) continue;
                var args = new object?[1];
                if (!(bool)method.Invoke(error, args)! || args[0] == null) continue;

                var json = args[0] is MaxioAdvancedBilling.Core.ErrorResponse.RawError raw
                    ? raw.ReadAsString()
                    : System.Text.Json.JsonSerializer.Serialize(args[0]);
                var messages = new List<string>();
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    CollectMessages(doc.RootElement, null, messages);
                }
                catch (System.Text.Json.JsonException)
                {
                    if (!string.IsNullOrWhiteSpace(json)) messages.Add(json.Trim());
                }

                var distinct = messages.Distinct().Take(10).ToList();
                if (distinct.Count > 0)
                {
                    var text = string.Join(" ", distinct);
                    return text.Length > 500 ? text[..500] : text;
                }
            }
        }
        catch (Exception)
        {
            // fall through to the generic reason
        }
        return fallback;
    }

    private static void CollectMessages(System.Text.Json.JsonElement element, string? name, List<string> messages)
    {
        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.String:
                var text = element.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                    messages.Add(name == null || name is "errors" or "error" or "message" ? text : $"{name}: {text}");
                break;
            case System.Text.Json.JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) CollectMessages(item, name, messages);
                break;
            case System.Text.Json.JsonValueKind.Object:
                foreach (var property in element.EnumerateObject()) CollectMessages(property.Value, property.Name, messages);
                break;
        }
    }
}
