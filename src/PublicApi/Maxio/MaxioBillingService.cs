using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.AnyOf;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.ProductPricePoints;
using MaxioAdvancedBilling.Requests.Products;
using MaxioAdvancedBilling.Requests.SubscriptionComponents;
using MaxioAdvancedBilling.Requests.SubscriptionProducts;
using MaxioAdvancedBilling.Requests.SubscriptionStatus;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public record PlanDto(int Id, string Handle, string Name, string? Description, decimal MonthlyPrice, string? Brand, int? BrandId);

public record SubscriptionDto(
    int Id,
    string State,
    bool Paid,
    decimal BalanceDue,
    string? PlanHandle,
    string? PlanName,
    decimal? MonthlyPrice,
    string? Currency,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? CanceledAt);

public record UsageEntryDto(long? Id, int? ComponentId, string? ComponentName, string? UnitName, decimal Quantity, string? Memo, DateTimeOffset? RecordedAt);

public record UsageTotalDto(int? ComponentId, string? ComponentName, string? UnitName, decimal TotalQuantity);

public record UsageDto(int SubscriptionId, DateOnly Date, IReadOnlyList<UsageTotalDto> Totals, IReadOnlyList<UsageEntryDto> Entries);

public record CurrencyPriceDto(int PlanId, string Currency, decimal Price);

public interface IMaxioBillingService
{
    Task<IReadOnlyList<PlanDto>> ListPlansAsync(CancellationToken ct);
    Task<SubscriptionDto> SubscribeAsync(string userName, string planHandle, CancellationToken ct);
    Task<SubscriptionDto> GetSubscriptionAsync(string userName, int subscriptionId, CancellationToken ct);
    Task<SubscriptionDto> PauseAsync(string userName, int subscriptionId, CancellationToken ct);
    Task<SubscriptionDto> ResumeAsync(string userName, int subscriptionId, CancellationToken ct);
    Task<SubscriptionDto> CancelAsync(string userName, int subscriptionId, CancellationToken ct);
    Task<SubscriptionDto> RestartAsync(string userName, int subscriptionId, CancellationToken ct);
    Task<SubscriptionDto> SwitchPlanAsync(string userName, int subscriptionId, string planHandle, CancellationToken ct);
    Task<UsageDto> GetUsageAsync(string userName, int subscriptionId, DateOnly date, CancellationToken ct);
    Task<PlanDto> CreatePlanAsync(string name, decimal monthlyPrice, int brandId, string brandName, string? description, CancellationToken ct);
    Task<CurrencyPriceDto> SetCurrencyPriceAsync(int planId, string currency, decimal price, CancellationToken ct);
    Task<SubscriptionDto> SetNextBillingDateAsync(int subscriptionId, DateOnly date, CancellationToken ct);
}

/// <summary>
/// The one place that talks to Maxio (through the SDK). Every call is bounded by a total budget and every
/// failure is translated into a <see cref="BillingException"/> carrying a status and a reason.
/// </summary>
public sealed class MaxioBillingService : IMaxioBillingService
{
    private const int PAGE_SIZE = 200;
    private const int MAX_PAGES = 50;

    // Serialises concurrent subscribe calls of the same shopper for the same plan inside this process.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeGates = new();

    private readonly MaxioAdvancedBillingClient _client;
    private readonly IReadRepository<CatalogBrand> _brands;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<MaxioBillingService> _logger;
    private readonly TimeSpan _budget;

    public MaxioBillingService(
        MaxioAdvancedBillingClient client,
        IReadRepository<CatalogBrand> brands,
        UserManager<ApplicationUser> userManager,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _brands = brands;
        _userManager = userManager;
        _logger = logger;
        _budget = TimeSpan.FromSeconds(Math.Max(5, settings.Value.CallBudgetSeconds));
    }

    // ------------------------------------------------------------------ plans

    public async Task<IReadOnlyList<PlanDto>> ListPlansAsync(CancellationToken ct)
    {
        return await Bounded(async token => await LoadOfferedPlansAsync(token), ct);
    }

    private async Task<List<PlanDto>> LoadOfferedPlansAsync(CancellationToken ct)
    {
        var brands = await _brands.ListAsync(ct);
        var brandByName = new Dictionary<string, CatalogBrand>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in brands)
        {
            brandByName.TryAdd(b.Brand.Trim(), b);
        }

        var plans = new List<PlanDto>();
        for (var page = 1; ; page++)
        {
            if (page > MAX_PAGES)
            {
                throw new BillingException(502, "The plan list is too large to be read completely; refusing to return a partial list.");
            }

            var products = await _client.Products.ListProducts(
                new ListProductsRequest { Page = page, PerPage = PAGE_SIZE, IncludeArchived = false }, cancellationToken: ct);

            foreach (var item in products)
            {
                var p = item.Product;
                if (p.ArchivedAt != null || p.Id == null || string.IsNullOrEmpty(p.Handle))
                {
                    continue;
                }

                var familyName = p.ProductFamily?.Name?.Trim();
                if (familyName == null || !brandByName.TryGetValue(familyName, out var brand))
                {
                    continue; // not attached to one of the shop's catalog brands: not offered
                }

                plans.Add(ToPlan(p, brand));
            }

            if (products.Count < PAGE_SIZE)
            {
                break;
            }
        }

        return plans;
    }

    private static PlanDto ToPlan(Product p, CatalogBrand? brand) =>
        new(p.Id!.Value, p.Handle!, p.Name ?? p.Handle!, p.Description, (p.PriceInCents ?? 0) / 100m, brand?.Brand ?? p.ProductFamily?.Name, brand?.Id);

    private async Task<PlanDto> FindOfferedPlanAsync(string handle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new BillingException(400, "A plan handle is required.");
        }

        var plans = await LoadOfferedPlansAsync(ct);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, handle.Trim(), StringComparison.Ordinal));
        return plan ?? throw new BillingException(404, $"There is no plan with handle '{handle}' that can be subscribed to.");
    }

    private async Task<PlanDto> FindOfferedPlanByIdAsync(int planId, CancellationToken ct)
    {
        var plans = await LoadOfferedPlansAsync(ct);
        return plans.FirstOrDefault(p => p.Id == planId)
               ?? throw new BillingException(404, $"There is no plan with id {planId}.");
    }

    // ------------------------------------------------------------ subscribing

    public async Task<SubscriptionDto> SubscribeAsync(string userName, string planHandle, CancellationToken ct)
    {
        return await Bounded(async token =>
        {
            var plan = await FindOfferedPlanAsync(planHandle, token);
            var customerId = await EnsureCustomerAsync(userName, token);

            var gate = SubscribeGates.GetOrAdd($"{userName}|{plan.Handle}", _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(token);
            try
            {
                var existing = await Read(() => _client.Customers.ListCustomerSubscriptions(
                    new ListCustomerSubscriptionsRequest { CustomerId = customerId }, cancellationToken: token));
                if (existing.Any(s => s.Subscription?.Product?.Handle == plan.Handle && IsLive(s.Subscription)))
                {
                    throw new BillingException(409, $"You already have a subscription to plan '{plan.Handle}'.");
                }

                var reference = Guid.NewGuid().ToString("N");
                var request = new CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = plan.Handle,
                            CustomerId = customerId,
                            // no card details are given: the shopper is invoiced instead of charged
                            PaymentCollectionMethod = CollectionMethod.Remittance,
                            Reference = reference
                        }
                    }
                };

                SubscriptionResponse response;
                try
                {
                    response = await _client.Subscriptions.CreateSubscription(request, cancellationToken: token);
                }
                catch (Exception ex) when (IsUnknownOutcome(ex) && !token.IsCancellationRequested)
                {
                    // The write may have landed: settle it by the reference we sent.
                    response = await SettleCreatedAsync(reference, ex, ct);
                }

                return ToDto(response.Subscription ?? throw new BillingException(502, "Billing system returned no subscription."));
            }
            finally
            {
                gate.Release();
            }
        }, ct, conflictOn422: false);
    }

    private async Task<SubscriptionResponse> SettleCreatedAsync(string reference, Exception original, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            return await _client.Subscriptions.FindSubscription(new FindSubscriptionRequest { Reference = reference }, cancellationToken: cts.Token);
        }
        catch (ApiException<MaxioAdvancedBilling.Errors.FindSubscriptionError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new BillingException(502, "The billing system could not be reached and the subscription was not created. Please try again.", original);
        }
        catch (Exception ex) when (ex is SdkException or OperationCanceledException)
        {
            throw new BillingException(502, "The billing system could not be reached and it is unknown whether the subscription was created. Please check your subscriptions before trying again.", original);
        }
    }

    private async Task<int> EnsureCustomerAsync(string userName, CancellationToken ct)
    {
        try
        {
            var found = await _client.Customers.ReadCustomerByReference(new ReadCustomerByReferenceRequest { Reference = userName }, cancellationToken: ct);
            if (found.Customer.Id is int id)
            {
                return id;
            }
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // fall through: first subscription of this shopper
        }
        catch (Exception ex) when (ex is SdkException)
        {
            throw Translate(ex, false);
        }

        var (first, last, email) = await ResolveProfileAsync(userName);
        try
        {
            var created = await _client.Customers.CreateCustomer(new CreateCustomerOperationRequest
            {
                Body = new CreateCustomerRequest
                {
                    Customer = new CreateCustomer { FirstName = first, LastName = last, Email = email, Reference = userName }
                }
            }, cancellationToken: ct);
            return created.Customer.Id ?? throw new BillingException(502, "Billing system returned no customer id.");
        }
        catch (ApiException)
        {
            // Another request may have created the customer meanwhile (the reference is unique): re-read it.
            try
            {
                var found = await _client.Customers.ReadCustomerByReference(new ReadCustomerByReferenceRequest { Reference = userName }, cancellationToken: ct);
                if (found.Customer.Id is int id)
                {
                    return id;
                }
            }
            catch (Exception)
            {
                // report the original failure below
            }

            throw;
        }
    }

    private async Task<(string First, string Last, string Email)> ResolveProfileAsync(string userName)
    {
        var user = await _userManager.FindByNameAsync(userName);
        var email = !string.IsNullOrWhiteSpace(user?.Email) ? user!.Email! : userName.Contains('@') ? userName : $"{userName}@eshop.invalid";
        var local = userName.Split('@')[0];
        return (string.IsNullOrWhiteSpace(local) ? "Shopper" : local, "Shopper", email);
    }

    private static bool IsLive(Subscription? s) =>
        s?.State != null && s.State != SubscriptionState.Canceled && s.State != SubscriptionState.Expired
        && s.State != SubscriptionState.FailedToCreate;

    // ------------------------------------------------------------ reading

    public async Task<SubscriptionDto> GetSubscriptionAsync(string userName, int subscriptionId, CancellationToken ct) =>
        await Bounded(async token => ToDto(await GetOwnedSubscriptionAsync(userName, subscriptionId, token)), ct);

    /// <summary>
    /// Reads a subscription and proves it belongs to the caller (Maxio customer reference == JWT name).
    /// Someone else's subscription is reported as not found.
    /// </summary>
    private async Task<Subscription> GetOwnedSubscriptionAsync(string userName, int subscriptionId, CancellationToken ct)
    {
        var sub = await GetSubscriptionOrNotFoundAsync(subscriptionId, ct);
        if (!string.Equals(sub.Customer?.Reference, userName, StringComparison.Ordinal))
        {
            throw new BillingException(404, $"Subscription {subscriptionId} was not found.");
        }

        return sub;
    }

    private async Task<Subscription> GetSubscriptionOrNotFoundAsync(int subscriptionId, CancellationToken ct)
    {
        try
        {
            var r = await _client.Subscriptions.ReadSubscription(new ReadSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct);
            return r.Subscription ?? throw new BillingException(404, $"Subscription {subscriptionId} was not found.");
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new BillingException(404, $"Subscription {subscriptionId} was not found.");
        }
    }

    // ------------------------------------------------------------ lifecycle

    private static bool IsPaused(Subscription s) => s.State == SubscriptionState.OnHold || s.State == SubscriptionState.Paused;

    private static bool IsEnded(Subscription s) => s.State == SubscriptionState.Canceled || s.State == SubscriptionState.Expired;

    public async Task<SubscriptionDto> PauseAsync(string userName, int subscriptionId, CancellationToken ct) =>
        await Bounded(async token =>
        {
            var sub = await GetOwnedSubscriptionAsync(userName, subscriptionId, token);
            if (IsPaused(sub)) throw new BillingException(409, "The subscription is already paused.");
            if (IsEnded(sub)) throw new BillingException(409, "A cancelled subscription cannot be paused; restart it first.");

            var after = await Write(
                () => _client.SubscriptionStatus.PauseSubscription(new PauseSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: token),
                subscriptionId, IsPaused, token, ct);
            return ToDto(after);
        }, ct);

    public async Task<SubscriptionDto> ResumeAsync(string userName, int subscriptionId, CancellationToken ct) =>
        await Bounded(async token =>
        {
            var sub = await GetOwnedSubscriptionAsync(userName, subscriptionId, token);
            if (!IsPaused(sub)) throw new BillingException(409, "The subscription is not paused.");

            var after = await Write(
                () => _client.SubscriptionStatus.ResumeSubscription(new ResumeSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: token),
                subscriptionId, s => !IsPaused(s) && !IsEnded(s), token, ct);
            return ToDto(after);
        }, ct);

    public async Task<SubscriptionDto> CancelAsync(string userName, int subscriptionId, CancellationToken ct) =>
        await Bounded(async token =>
        {
            var sub = await GetOwnedSubscriptionAsync(userName, subscriptionId, token);
            if (IsEnded(sub)) throw new BillingException(409, "The subscription is already cancelled.");

            var after = await Write(
                () => _client.SubscriptionStatus.CancelSubscription(new CancelSubscriptionRequest
                {
                    SubscriptionId = subscriptionId,
                    Body = new CancellationRequest { Subscription = new CancellationOptions { CancellationMessage = "Cancelled by the subscriber." } }
                }, cancellationToken: token),
                subscriptionId, IsEnded, token, ct);
            return ToDto(after);
        }, ct);

    public async Task<SubscriptionDto> RestartAsync(string userName, int subscriptionId, CancellationToken ct) =>
        await Bounded(async token =>
        {
            var sub = await GetOwnedSubscriptionAsync(userName, subscriptionId, token);
            if (!IsEnded(sub)) throw new BillingException(409, "Only a cancelled subscription can be restarted.");

            var after = await Write(
                () => _client.SubscriptionStatus.ReactivateSubscription(new ReactivateSubscriptionOperationRequest { SubscriptionId = subscriptionId }, cancellationToken: token),
                subscriptionId, s => !IsEnded(s), token, ct);
            return ToDto(after);
        }, ct);

    public async Task<SubscriptionDto> SwitchPlanAsync(string userName, int subscriptionId, string planHandle, CancellationToken ct) =>
        await Bounded(async token =>
        {
            var plan = await FindOfferedPlanAsync(planHandle, token);
            var sub = await GetOwnedSubscriptionAsync(userName, subscriptionId, token);

            if (sub.Product?.Handle == plan.Handle && !IsEnded(sub))
            {
                throw new BillingException(409, $"The subscription is already on plan '{plan.Handle}'.");
            }

            if (IsEnded(sub))
            {
                // A cancelled subscription is brought back first so that the plan change leaves the shopper on the new plan.
                await Write(
                    () => _client.SubscriptionStatus.ReactivateSubscription(new ReactivateSubscriptionOperationRequest { SubscriptionId = subscriptionId }, cancellationToken: token),
                    subscriptionId, s => !IsEnded(s), token, ct);
            }

            var after = await Write(
                () => _client.SubscriptionProducts.MigrateSubscriptionProduct(new MigrateSubscriptionProductRequest
                {
                    SubscriptionId = subscriptionId,
                    Body = new SubscriptionProductMigrationRequest
                    {
                        Migration = new SubscriptionProductMigration { ProductHandle = plan.Handle }
                    }
                }, cancellationToken: token),
                subscriptionId, s => s.Product?.Handle == plan.Handle, token, ct);
            return ToDto(after);
        }, ct);

    // ------------------------------------------------------------ usage

    public async Task<UsageDto> GetUsageAsync(string userName, int subscriptionId, DateOnly date, CancellationToken ct) =>
        await Bounded(async token =>
        {
            await GetOwnedSubscriptionAsync(userName, subscriptionId, token);

            var components = await Read(() => _client.SubscriptionComponents.ListSubscriptionComponents(
                new ListSubscriptionComponentsRequest { SubscriptionId = subscriptionId }, cancellationToken: token));
            var metered = components
                .Select(c => c.Component)
                .Where(c => c?.ComponentId != null && c.Kind == ComponentKind.MeteredComponent)
                .ToList();

            // The window is widened by a day on each side; the exact day is cut below on the site's own clock.
            var since = new DateTimeOffset(date.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var until = new DateTimeOffset(date.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

            var entries = new List<UsageEntryDto>();
            foreach (var comp in metered)
            {
                for (var page = 1; ; page++)
                {
                    if (page > MAX_PAGES)
                    {
                        throw new BillingException(502, "The usage list is too large to be read completely; refusing to return a partial answer.");
                    }

                    var usages = await Read(() => _client.SubscriptionComponents.ListUsages(new ListUsagesRequest
                    {
                        SubscriptionIdOrReference = subscriptionId,
                        ComponentId = comp!.ComponentId!.Value,
                        SinceDate = since,
                        UntilDate = until,
                        Page = page,
                        PerPage = PAGE_SIZE
                    }, cancellationToken: token));

                    foreach (var u in usages.Select(x => x.Usage))
                    {
                        if (u.CreatedAt is not DateTimeOffset at || DateOnly.FromDateTime(at.DateTime) != date)
                        {
                            continue;
                        }

                        entries.Add(new UsageEntryDto(u.Id, comp!.ComponentId, comp.Name, comp.UnitName, ParseQuantity(u.Quantity), u.Memo, at));
                    }

                    if (usages.Count < PAGE_SIZE) break;
                }
            }

            var totals = entries
                .GroupBy(e => e.ComponentId)
                .Select(g => new UsageTotalDto(g.Key, g.First().ComponentName, g.First().UnitName, g.Sum(e => e.Quantity)))
                .ToList();
            // components with no usage that day still show up with a zero total
            foreach (var comp in metered.Where(c => totals.All(t => t.ComponentId != c!.ComponentId)))
            {
                totals.Add(new UsageTotalDto(comp!.ComponentId, comp.Name, comp.UnitName, 0m));
            }

            return new UsageDto(subscriptionId, date, totals, entries.OrderBy(e => e.RecordedAt).ToList());
        }, ct);

    private static decimal ParseQuantity(Quantity1? q)
    {
        if (q == null) return 0m;
        if (q.TryGetInt(out var i)) return i;
        if (q.TryGetString(out var s) && decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) return d;
        return 0m;
    }

    // ------------------------------------------------------------ staff

    public async Task<PlanDto> CreatePlanAsync(string name, decimal monthlyPrice, int brandId, string brandName, string? description, CancellationToken ct) =>
        await Bounded(async token =>
        {
            var familyId = await EnsureFamilyAsync(brandName, token);
            var cents = decimal.Round(monthlyPrice * 100m, 0, MidpointRounding.AwayFromZero);
            try
            {
                var response = await _client.Products.CreateProduct(new CreateProductRequest
                {
                    ProductFamilyId = familyId.ToString(CultureInfo.InvariantCulture),
                    Body = new CreateOrUpdateProductRequest
                    {
                        Product = new CreateOrUpdateProduct
                        {
                            Name = name.Trim(),
                            Handle = Slug(name),
                            Description = string.IsNullOrWhiteSpace(description) ? name.Trim() : description.Trim(),
                            PriceInCents = (long)cents,
                            Interval = 1,
                            IntervalUnit = IntervalUnit.Month,
                            RequireCreditCard = false
                        }
                    }
                }, cancellationToken: token);
                var p = response.Product;
                return new PlanDto(p.Id ?? 0, p.Handle ?? string.Empty, p.Name ?? name, p.Description, (p.PriceInCents ?? 0) / 100m, brandName, brandId);
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                var reason = Describe(ex);
                var duplicate = reason.Contains("taken", StringComparison.OrdinalIgnoreCase) || reason.Contains("already", StringComparison.OrdinalIgnoreCase);
                throw new BillingException(duplicate ? 409 : 422, reason, ex);
            }
        }, ct, conflictOn422: false);

    private async Task<int> EnsureFamilyAsync(string brandName, CancellationToken ct)
    {
        async Task<int?> FindAsync()
        {
            var families = await _client.ProductFamilies.ListProductFamilies(new ListProductFamiliesRequest(), cancellationToken: ct);
            return families
                .Select(f => f.ProductFamily)
                .FirstOrDefault(f => f?.ArchivedAt == null && string.Equals(f?.Name?.Trim(), brandName.Trim(), StringComparison.OrdinalIgnoreCase))?.Id;
        }

        if (await FindAsync() is int existing) return existing;

        try
        {
            var created = await _client.ProductFamilies.CreateProductFamily(new CreateProductFamilyOperationRequest
            {
                Body = new CreateProductFamilyRequest { ProductFamily = new CreateProductFamily { Name = brandName.Trim() } }
            }, cancellationToken: ct);
            return created.ProductFamily?.Id ?? throw new BillingException(502, "Billing system returned no product family.");
        }
        catch (ApiException)
        {
            if (await FindAsync() is int raced) return raced; // created by a concurrent request
            throw;
        }
    }

    public async Task<CurrencyPriceDto> SetCurrencyPriceAsync(int planId, string currency, decimal price, CancellationToken ct) =>
        await Bounded(async token =>
        {
            var plan = await FindOfferedPlanByIdAsync(planId, token);
            var product = (await Read(() => _client.Products.ReadProduct(new ReadProductRequest { ProductId = planId }, cancellationToken: token))).Product;
            var pricePointId = product.DefaultProductPricePointId
                ?? throw new BillingException(409, $"Plan {planId} has no default price point to price in another currency.");

            var site = (await Read(() => _client.Sites.ReadSite(cancellationToken: token))).Site;
            var allowed = site.NonPrimaryCurrencies;
            if (string.Equals(site.Currency, currency, StringComparison.OrdinalIgnoreCase))
            {
                throw new BillingException(409, $"{currency} is the plan's primary currency; give the other currency to price in.");
            }

            if (allowed != null && !allowed.Contains(currency, StringComparer.OrdinalIgnoreCase))
            {
                var list = allowed.Count == 0 ? "none" : string.Join(", ", allowed);
                throw new BillingException(422, $"{currency} is not defined as an additional currency of the billing site (available: {list}).");
            }

            var points = await Read(() => _client.ProductPricePoints.ListProductPricePoints(
                new ListProductPricePointsRequest { ProductId = planId, CurrencyPrices = true, PerPage = 100 }, cancellationToken: token));
            var point = points.PricePoints.FirstOrDefault(p => p.Id == pricePointId);
            var existing = point?.CurrencyPrices?.FirstOrDefault(c =>
                string.Equals(c.Currency, currency, StringComparison.OrdinalIgnoreCase) && c.Role == CurrencyPriceRole.Baseline);

            try
            {
                CurrencyPricesResponse response;
                if (existing?.Id is int existingId)
                {
                    response = await _client.ProductPricePoints.UpdateProductCurrencyPrices(new UpdateProductCurrencyPricesRequest
                    {
                        ProductPricePointId = pricePointId,
                        Body = new UpdateCurrencyPricesRequest
                        {
                            CurrencyPrices = new[] { new UpdateCurrencyPrice { Id = existingId, Price = (double)price } }
                        }
                    }, cancellationToken: token);
                }
                else
                {
                    if (price != decimal.Truncate(price))
                    {
                        throw new BillingException(400, "The billing system accepts whole-number prices for other currencies.");
                    }

                    response = await _client.ProductPricePoints.CreateProductCurrencyPrices(new CreateProductCurrencyPricesOperationRequest
                    {
                        ProductPricePointId = pricePointId,
                        Body = new CreateProductCurrencyPricesRequest
                        {
                            CurrencyPrices = new[]
                            {
                                new CreateProductCurrencyPrice { Currency = currency, Price = (int)price, Role = CurrencyPriceRole.Baseline }
                            }
                        }
                    }, cancellationToken: token);
                }

                var saved = response.CurrencyPrices.FirstOrDefault(c => string.Equals(c.Currency, currency, StringComparison.OrdinalIgnoreCase));
                return new CurrencyPriceDto(plan.Id, currency, saved?.Price is double d ? (decimal)d : price);
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                throw new BillingException(422, Describe(ex), ex);
            }
        }, ct, conflictOn422: false);

    public async Task<SubscriptionDto> SetNextBillingDateAsync(int subscriptionId, DateOnly date, CancellationToken ct) =>
        await Bounded(async token =>
        {
            var sub = await GetSubscriptionOrNotFoundAsync(subscriptionId, token);
            if (IsEnded(sub)) throw new BillingException(409, "A cancelled subscription is not billed; restart it first.");

            // Noon on the subscription's own clock keeps the date stable whatever the site's time zone is.
            var offset = (sub.CurrentPeriodEndsAt ?? sub.NextAssessmentAt ?? sub.CreatedAt)?.Offset ?? TimeSpan.Zero;
            var target = new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), offset);
            if (target <= DateTimeOffset.UtcNow)
            {
                throw new BillingException(400, "The next billing date must be in the future.");
            }

            var after = await Write(
                () => _client.Subscriptions.UpdateSubscription(new UpdateSubscriptionOperationRequest
                {
                    SubscriptionId = subscriptionId,
                    Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { NextBillingAt = target } }
                }, cancellationToken: token),
                subscriptionId, s => BillingDate(s) == date, token, ct);

            if (BillingDate(after) != date)
            {
                throw new BillingException(422, "The billing system did not accept that next billing date.");
            }

            return ToDto(after);
        }, ct, conflictOn422: false);

    private static DateOnly? BillingDate(Subscription s) =>
        (s.CurrentPeriodEndsAt ?? s.NextAssessmentAt) is DateTimeOffset at ? DateOnly.FromDateTime(at.DateTime) : null;

    // ------------------------------------------------------------ helpers

    private static SubscriptionDto ToDto(Subscription s)
    {
        var balance = (s.BalanceInCents ?? 0) / 100m;
        return new SubscriptionDto(
            s.Id ?? 0,
            s.State?.Value ?? "unknown",
            Paid: (s.BalanceInCents ?? 0) <= 0,
            balance > 0 ? balance : 0,
            s.Product?.Handle,
            s.Product?.Name,
            s.Product?.PriceInCents is long c ? c / 100m : null,
            s.Currency,
            s.CurrentPeriodEndsAt ?? s.NextAssessmentAt,
            s.CreatedAt,
            s.CanceledAt);
    }

    /// <summary>
    /// Runs a state-changing call. When the connection fails after the request may have been sent, the subscription is
    /// re-read to settle the outcome instead of reporting a failure.
    /// </summary>
    private async Task<Subscription> Write(Func<Task<SubscriptionResponse>> call, int subscriptionId, Func<Subscription, bool> reachedExpectedState, CancellationToken token, CancellationToken callerToken)
    {
        try
        {
            var r = await call();
            return r.Subscription ?? throw new BillingException(502, "Billing system returned no subscription.");
        }
        catch (Exception ex) when (IsUnknownOutcome(ex) && !callerToken.IsCancellationRequested)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                var current = await GetSubscriptionOrNotFoundAsync(subscriptionId, cts.Token);
                if (reachedExpectedState(current)) return current;
            }
            catch (Exception)
            {
                // still unknown
            }

            throw new BillingException(502, "The billing system could not be reached and it is unknown whether the change was applied. Please check the subscription before trying again.", ex);
        }
    }

    private static bool IsUnknownOutcome(Exception ex) => ex is SdkConnectionException or SdkTimeoutException;

    private async Task<T> Read<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (Exception ex) when (ex is SdkException)
        {
            throw Translate(ex, false);
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> op, CancellationToken ct, bool conflictOn422 = true)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_budget);
        try
        {
            return await op(cts.Token);
        }
        catch (BillingException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new BillingException(504, "The billing system did not answer in time.");
        }
        catch (SdkException ex)
        {
            throw Translate(ex, conflictOn422);
        }
    }

    private BillingException Translate(Exception ex, bool conflictOn422)
    {
        switch (ex)
        {
            case ApiException api:
                var reason = Describe(api);
                var status = (int)api.StatusCode;
                _logger.LogWarning("Maxio answered {Status} for {Method} {Uri}: {Reason}", status, api.Method, api.RequestUri.AbsolutePath, reason);
                if (status == 404) return new BillingException(404, $"Not found in the billing system: {reason}", ex);
                if (status == 422 || status == 400) return new BillingException(conflictOn422 ? 409 : 422, reason, ex);
                if (status == 401 || status == 403) return new BillingException(502, "The billing system refused the integration credentials.", ex);
                if (status == 429) return new BillingException(503, "The billing system is busy; please try again shortly.", ex);
                return new BillingException(502, $"The billing system answered with an error ({status}).", ex);
            case SdkTimeoutException:
                return new BillingException(504, "The billing system did not answer in time.", ex);
            case SdkConnectionException:
                return new BillingException(502, "The billing system could not be reached.", ex);
            default:
                _logger.LogError(ex, "Unexpected Maxio SDK failure");
                return new BillingException(502, "The billing system call failed.", ex);
        }
    }

    /// <summary>Pulls the human-readable reason out of whichever typed (or raw) error body Maxio sent.</summary>
    private static string Describe(ApiException ex)
    {
        try
        {
            var error = ex.GetType().GetProperty("Error")?.GetValue(ex);
            var text = error == null ? null : DescribeError(error);
            if (!string.IsNullOrWhiteSpace(text)) return text!;
        }
        catch (Exception)
        {
            // fall back to the generic text below
        }

        return $"The billing system rejected the request ({(int)ex.StatusCode}).";
    }

    private static string? DescribeError(object error)
    {
        switch (error)
        {
            case RawError raw:
                return Trim(raw.ReadAsString());
            case ErrorListResponse1 list:
                return string.Join("; ", list.Errors);
            case ErrorArrayMapResponse1 map:
                return map.Errors == null ? null : Trim(string.Join("; ", map.Errors.Select(kv => $"{kv.Key}: {FlattenErrorValue(kv.Value)}")));
            case ApiError typed:
                foreach (var m in typed.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                             .Where(m => m.Name.StartsWith("TryGet", StringComparison.Ordinal) && m.Name != "TryGetRawError"
                                         && m.GetParameters().Length == 1 && m.GetParameters()[0].IsOut))
                {
                    var args = new object?[] { null };
                    if (m.Invoke(typed, args) is true && args[0] != null)
                    {
                        var inner = args[0]!;
                        return DescribeError(inner) ?? Trim(inner as string ?? JsonSerializer.Serialize(inner, inner.GetType()));
                    }
                }

                return typed.TryGetRawError(out var fallback) ? Trim(fallback.ReadAsString()) : null;
            default:
                return null;
        }
    }

    private static string FlattenErrorValue(object? value)
    {
        if (value is JsonElement e && e.ValueKind == JsonValueKind.Array)
        {
            return string.Join(", ", e.EnumerateArray().Select(x => x.ToString()).Distinct());
        }

        return value?.ToString() ?? string.Empty;
    }

    private static string Trim(string s) => s.Length > 500 ? s[..500] : s;

    private static string Slug(string name)
    {
        var chars = name.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var slug = string.Join("-", new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length == 0 ? "plan" : slug;
    }
}
