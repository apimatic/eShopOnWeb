using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
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

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Subscription billing on top of Maxio (the system of record). All state lives in Maxio;
/// a shopper is mapped to a Maxio customer through the customer's <c>reference</c> (the shop user name).
/// </summary>
public class BillingService
{
    private const int PAGE_SIZE = 200;

    private readonly MaxioAdvancedBillingClient _maxio;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IReadRepository<CatalogBrand> _brands;

    public BillingService(MaxioAdvancedBillingClient maxio, UserManager<ApplicationUser> userManager, IReadRepository<CatalogBrand> brands)
    {
        _maxio = maxio;
        _userManager = userManager;
        _brands = brands;
    }

    // ---------------------------------------------------------------- shopper features

    public async Task<IReadOnlyList<PlanDto>> ListPlansAsync()
    {
        var products = await ListAll(page => MaxioErrors.Run(() => _maxio.Products.ListProducts(
            new ListProductsRequest { Page = page, PerPage = PAGE_SIZE, IncludeArchived = false })));
        return products
            .Select(p => p.Product)
            .Where(p => p.ArchivedAt == null)
            .Select(ToPlan)
            .ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(string? userName, string? planHandle)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw BillingException.BadRequest("planHandle is required.");
        }

        var product = await FindPlanByHandle(planHandle.Trim());
        var customerId = await EnsureCustomerAsync(userName);

        var created = await MaxioErrors.Run(() => _maxio.Subscriptions.CreateSubscription(new CreateSubscriptionOperationRequest
        {
            Body = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductId = product.Id,
                    CustomerId = customerId,
                    // No card details: the shopper is invoiced instead of charged.
                    PaymentCollectionMethod = CollectionMethod.Remittance
                }
            }
        }));

        return await BuildSubscriptionDto(created.Subscription!);
    }

    public async Task<SubscriptionDto> GetSubscriptionAsync(string? userName, int subscriptionId)
    {
        var sub = await GetOwnedSubscription(userName, subscriptionId);
        return await BuildSubscriptionDto(sub);
    }

    public async Task<SubscriptionDto> PauseAsync(string? userName, int subscriptionId)
    {
        var sub = await GetOwnedSubscription(userName, subscriptionId);
        if (IsPaused(sub))
        {
            throw BillingException.Conflict("The subscription is already paused.");
        }
        if (IsEnded(sub))
        {
            throw BillingException.Conflict($"A subscription that is {sub.State} cannot be paused.");
        }

        var result = await MaxioErrors.Run(() => _maxio.SubscriptionStatus.PauseSubscription(new PauseSubscriptionRequest
        {
            SubscriptionId = subscriptionId
        }));
        return await BuildSubscriptionDto(result.Subscription!);
    }

    public async Task<SubscriptionDto> ResumeAsync(string? userName, int subscriptionId)
    {
        var sub = await GetOwnedSubscription(userName, subscriptionId);
        if (!IsPaused(sub))
        {
            throw BillingException.Conflict("Only a paused subscription can be resumed.");
        }

        var result = await MaxioErrors.Run(() => _maxio.SubscriptionStatus.ResumeSubscription(new ResumeSubscriptionRequest
        {
            SubscriptionId = subscriptionId
        }));
        return await BuildSubscriptionDto(result.Subscription!);
    }

    public async Task<SubscriptionDto> CancelAsync(string? userName, int subscriptionId)
    {
        var sub = await GetOwnedSubscription(userName, subscriptionId);
        if (sub.State == SubscriptionState.Canceled)
        {
            throw BillingException.Conflict("The subscription is already cancelled.");
        }
        if (IsEnded(sub))
        {
            throw BillingException.Conflict($"A subscription that is {sub.State} cannot be cancelled.");
        }

        var result = await MaxioErrors.Run(() => _maxio.SubscriptionStatus.CancelSubscription(new CancelSubscriptionRequest
        {
            SubscriptionId = subscriptionId,
            Body = new CancellationRequest { Subscription = new CancellationOptions { CancellationMessage = "Cancelled by the shopper" } }
        }));
        return await BuildSubscriptionDto(result.Subscription!);
    }

    public async Task<SubscriptionDto> RestartAsync(string? userName, int subscriptionId)
    {
        var sub = await GetOwnedSubscription(userName, subscriptionId);
        if (sub.State != SubscriptionState.Canceled)
        {
            throw BillingException.Conflict("Only a cancelled subscription can be restarted.");
        }

        var result = await Reactivate(subscriptionId);
        return await BuildSubscriptionDto(result);
    }

    public async Task<SubscriptionDto> SwitchPlanAsync(string? userName, int subscriptionId, string? planHandle)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw BillingException.BadRequest("planHandle is required.");
        }

        var sub = await GetOwnedSubscription(userName, subscriptionId);
        var target = await FindPlanByHandle(planHandle.Trim());
        if (IsEnded(sub) && sub.State != SubscriptionState.Canceled)
        {
            throw BillingException.Conflict($"A subscription that is {sub.State} cannot change plan.");
        }
        if (sub.State != SubscriptionState.Canceled && sub.Product?.Id == target.Id)
        {
            throw BillingException.Conflict("The subscription is already on that plan.");
        }

        // A cancelled subscription is brought back first, so that switching leaves the shopper on the new plan.
        var reactivated = false;
        if (sub.State == SubscriptionState.Canceled)
        {
            await Reactivate(subscriptionId);
            reactivated = true;
        }

        try
        {
            if (sub.Product?.Id != target.Id)
            {
                await MaxioErrors.Run(() => _maxio.SubscriptionProducts.MigrateSubscriptionProduct(new MigrateSubscriptionProductRequest
                {
                    SubscriptionId = subscriptionId,
                    Body = new SubscriptionProductMigrationRequest
                    {
                        Migration = new SubscriptionProductMigration { ProductId = target.Id }
                    }
                }));
            }
        }
        catch (Exception) when (reactivated)
        {
            // Leave the subscription as the shopper had it (cancelled) when the plan change did not go through.
            try
            {
                await MaxioErrors.Run(() => _maxio.SubscriptionStatus.CancelSubscription(new CancelSubscriptionRequest { SubscriptionId = subscriptionId }));
            }
            catch (Exception)
            {
                // best effort
            }
            throw;
        }

        var fresh = await ReadSubscription(subscriptionId);
        return await BuildSubscriptionDto(fresh);
    }

    public async Task<DailyUsageDto> GetUsageAsync(string? userName, int subscriptionId, string? date)
    {
        if (string.IsNullOrWhiteSpace(date)
            || !DateOnly.TryParseExact(date.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            throw BillingException.BadRequest("date is required and must be formatted YYYY-MM-DD.");
        }

        await GetOwnedSubscription(userName, subscriptionId);

        var components = await MaxioErrors.Run(() => _maxio.SubscriptionComponents.ListSubscriptionComponents(
            new ListSubscriptionComponentsRequest { SubscriptionId = subscriptionId }));

        // Maxio treats since_date/until_date as midnights and does not say in which zone, so ask for a window
        // one day wider on each side and cut it down to the requested calendar day below.
        var since = new DateTimeOffset(day.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var until = new DateTimeOffset(day.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var result = new List<ComponentUsageDto>();

        foreach (var component in components.Select(c => c.Component).Where(c => c?.ComponentId != null))
        {
            if (component!.Kind != ComponentKind.MeteredComponent && component.Kind != ComponentKind.PrepaidUsageComponent)
            {
                continue;
            }

            var componentId = component.ComponentId!.Value;
            var usages = await ListAll(page => MaxioErrors.Run(() => _maxio.SubscriptionComponents.ListUsages(new ListUsagesRequest
            {
                SubscriptionIdOrReference = subscriptionId,
                ComponentId = componentId,
                SinceDate = since,
                UntilDate = until,
                Page = page,
                PerPage = PAGE_SIZE
            })));

            var entries = usages
                .Select(u => u.Usage)
                // Maxio reports timestamps in the site's time zone; the day is the calendar day as reported.
                .Where(u => u.CreatedAt == null || DateOnly.FromDateTime(u.CreatedAt.Value.DateTime) == day)
                .Select(u => new UsageEntryDto(u.Id, ParseQuantity(u.Quantity), u.Memo, u.CreatedAt))
                .ToList();

            result.Add(new ComponentUsageDto(componentId, component.Name, component.UnitName, entries.Sum(e => e.Quantity), entries));
        }

        return new DailyUsageDto(subscriptionId, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), result);
    }

    // ---------------------------------------------------------------- staff features

    public async Task<PlanDto> CreatePlanAsync(CreatePlanRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw BillingException.BadRequest("name is required.");
        }
        if (request.MonthlyPrice is null || request.MonthlyPrice <= 0)
        {
            throw BillingException.BadRequest("monthlyPrice must be greater than zero.");
        }
        var price = request.MonthlyPrice.Value;
        if (decimal.Round(price, 2) != price)
        {
            throw BillingException.BadRequest("monthlyPrice cannot have more than two decimal places.");
        }
        var cents = decimal.Round(price * 100, 0, MidpointRounding.AwayFromZero);
        if (cents > int.MaxValue)
        {
            throw BillingException.BadRequest("monthlyPrice is too large.");
        }

        var brand = await ResolveBrand(request);
        var name = request.Name.Trim();
        var handle = Slug(name);
        if (handle.Length == 0)
        {
            throw BillingException.BadRequest("name must contain letters or digits.");
        }

        var existing = await FindPlanByHandleOrNull(handle);
        if (existing != null)
        {
            throw BillingException.Conflict($"A plan with handle '{handle}' already exists.");
        }

        var family = await EnsureProductFamily(brand);

        var created = await MaxioErrors.Run(() => _maxio.Products.CreateProduct(new CreateProductRequest
        {
            ProductFamilyId = family.Id!.Value.ToString(CultureInfo.InvariantCulture),
            Body = new CreateOrUpdateProductRequest
            {
                Product = new CreateOrUpdateProduct
                {
                    Name = name,
                    Handle = handle,
                    Description = name,
                    PriceInCents = (long)cents,
                    Interval = 1,
                    IntervalUnit = IntervalUnit.Month,
                    RequireCreditCard = false
                }
            }
        }));
        return ToPlan(created.Product);
    }

    public async Task<CurrencyPriceDto> AddCurrencyPriceAsync(int planId, CurrencyPriceRequest request)
    {
        var currency = request.Currency?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(currency) || currency.Length != 3 || !currency.All(c => c is >= 'A' and <= 'Z'))
        {
            throw BillingException.BadRequest("currency must be a three-letter ISO 4217 code.");
        }
        if (request.Price is null || request.Price <= 0)
        {
            throw BillingException.BadRequest("price must be greater than zero.");
        }
        var price = request.Price.Value;
        if (decimal.Truncate(price) != price || price > int.MaxValue)
        {
            throw BillingException.BadRequest("price must be a whole number of currency units.");
        }

        ProductResponse product;
        try
        {
            product = await MaxioErrors.Run(() => _maxio.Products.ReadProduct(new ReadProductRequest { ProductId = planId }));
        }
        catch (BillingException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw BillingException.NotFound($"Plan {planId} does not exist.");
        }

        var pricePointId = product.Product.DefaultProductPricePointId ?? product.Product.ProductPricePointId;
        if (pricePointId == null)
        {
            throw BillingException.Conflict("The plan has no price point to price.");
        }

        await MaxioErrors.Run(() => _maxio.ProductPricePoints.CreateProductCurrencyPrices(new CreateProductCurrencyPricesOperationRequest
        {
            ProductPricePointId = pricePointId.Value,
            Body = new CreateProductCurrencyPricesRequest
            {
                CurrencyPrices = new[]
                {
                    new CreateProductCurrencyPrice { Currency = currency, Price = (int)price, Role = CurrencyPriceRole.Baseline }
                }
            }
        }));
        return new CurrencyPriceDto(planId, currency, price);
    }

    public async Task<NextBillingDateDto> SetNextBillingDateAsync(int subscriptionId, DateOnly? date)
    {
        if (date is null)
        {
            throw BillingException.BadRequest("date is required and must be formatted YYYY-MM-DD.");
        }
        var day = date.Value;
        if (day <= DateOnly.FromDateTime(DateTime.UtcNow))
        {
            throw BillingException.BadRequest("The next billing date must be in the future.");
        }

        var sub = await ReadSubscription(subscriptionId);
        if (IsEnded(sub))
        {
            throw BillingException.Conflict($"A subscription that is {sub.State} is not billed.");
        }
        if (IsPaused(sub))
        {
            throw BillingException.Conflict("A paused subscription is not billed; resume it first.");
        }

        // Keep the time of day (and zone) Maxio already bills at, so the date is the date in the site's own zone.
        var current = sub.NextAssessmentAt;
        var target = current is { } c
            ? new DateTimeOffset(day.ToDateTime(TimeOnly.FromTimeSpan(c.TimeOfDay)), c.Offset)
            : new DateTimeOffset(day.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

        var updated = await MaxioErrors.Run(() => _maxio.Subscriptions.UpdateSubscription(new UpdateSubscriptionOperationRequest
        {
            SubscriptionId = subscriptionId,
            Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { NextBillingAt = target } }
        }));

        var next = updated.Subscription?.NextAssessmentAt;
        return new NextBillingDateDto(subscriptionId, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), next);
    }

    // ---------------------------------------------------------------- helpers

    private static PlanDto ToPlan(Product p) =>
        new(p.Id ?? 0, p.Handle, p.Name, (p.PriceInCents ?? 0) / 100m, p.ProductFamily?.Name);

    private static bool IsPaused(Subscription s) => s.State == SubscriptionState.OnHold || s.State == SubscriptionState.Paused;

    private static bool IsEnded(Subscription s) =>
        s.State == SubscriptionState.Canceled || s.State == SubscriptionState.Expired
        || s.State == SubscriptionState.FailedToCreate || s.State == SubscriptionState.Pending
        || s.State == SubscriptionState.AwaitingSignup;

    private static decimal ParseQuantity(MaxioAdvancedBilling.Models.AnyOf.Quantity1? q)
    {
        if (q == null)
        {
            return 0;
        }
        if (q.TryGetInt(out var i))
        {
            return i;
        }
        return q.TryGetString(out var s) && decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    private static string Slug(string value)
    {
        var sb = new StringBuilder();
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                sb.Append(ch);
            }
            else if (sb.Length > 0 && sb[^1] != '-')
            {
                sb.Append('-');
            }
        }
        return sb.ToString().Trim('-');
    }

    private static async Task<List<T>> ListAll<T>(Func<int, Task<IReadOnlyList<T>>> fetch)
    {
        var all = new List<T>();
        for (var page = 1; ; page++)
        {
            var items = await fetch(page);
            all.AddRange(items);
            if (items.Count < PAGE_SIZE)
            {
                return all;
            }
        }
    }

    private async Task<Product> FindPlanByHandle(string handle)
    {
        var product = await FindPlanByHandleOrNull(handle);
        if (product == null)
        {
            throw BillingException.NotFound($"There is no plan with handle '{handle}'.");
        }
        if (product.ArchivedAt != null)
        {
            throw BillingException.Conflict($"The plan '{handle}' is no longer offered.");
        }
        return product;
    }

    private async Task<Product?> FindPlanByHandleOrNull(string handle)
    {
        try
        {
            var response = await MaxioErrors.Run(() => _maxio.Products.ReadProductByHandle(new ReadProductByHandleRequest { ApiHandle = handle }));
            return response.Product;
        }
        catch (BillingException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<ApplicationUser> GetUser(string? userName)
    {
        var user = string.IsNullOrEmpty(userName) ? null : await _userManager.FindByNameAsync(userName);
        if (user == null)
        {
            throw new BillingException(HttpStatusCode.Unauthorized, "The signed-in user is unknown.");
        }
        return user;
    }

    private async Task<int> EnsureCustomerAsync(string? userName)
    {
        var user = await GetUser(userName);
        var reference = user.UserName!;
        try
        {
            var found = await MaxioErrors.Run(() => _maxio.Customers.ReadCustomerByReference(new ReadCustomerByReferenceRequest { Reference = reference }));
            if (found.Customer.Id != null)
            {
                return found.Customer.Id.Value;
            }
        }
        catch (BillingException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // first subscription: create the customer below
        }

        var email = user.Email ?? $"{reference}@example.invalid";
        var created = await MaxioErrors.Run(() => _maxio.Customers.CreateCustomer(new CreateCustomerOperationRequest
        {
            Body = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = reference.Split('@')[0],
                    LastName = "eShopOnWeb",
                    Email = email,
                    Reference = reference
                }
            }
        }));
        return created.Customer.Id!.Value;
    }

    private async Task<Subscription> ReadSubscription(int subscriptionId)
    {
        try
        {
            var response = await MaxioErrors.Run(() => _maxio.Subscriptions.ReadSubscription(new ReadSubscriptionRequest { SubscriptionId = subscriptionId }));
            return response.Subscription ?? throw BillingException.NotFound($"Subscription {subscriptionId} does not exist.");
        }
        catch (BillingException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw BillingException.NotFound($"Subscription {subscriptionId} does not exist.");
        }
    }

    /// <summary>Loads a subscription and makes sure it belongs to the caller; someone else's subscription looks like a missing one.</summary>
    private async Task<Subscription> GetOwnedSubscription(string? userName, int subscriptionId)
    {
        var user = await GetUser(userName);
        var sub = await ReadSubscription(subscriptionId);
        if (!string.Equals(sub.Customer?.Reference, user.UserName, StringComparison.Ordinal))
        {
            throw BillingException.NotFound($"Subscription {subscriptionId} does not exist.");
        }
        return sub;
    }

    private async Task<Subscription> Reactivate(int subscriptionId)
    {
        var result = await MaxioErrors.Run(() => _maxio.SubscriptionStatus.ReactivateSubscription(new ReactivateSubscriptionOperationRequest
        {
            SubscriptionId = subscriptionId
        }));
        return result.Subscription!;
    }

    private async Task<SubscriptionDto> BuildSubscriptionDto(Subscription sub)
    {
        var owed = await AmountOwed(sub);
        return new SubscriptionDto(
            sub.Id ?? 0,
            sub.State?.Value,
            owed <= 0,
            Math.Max(owed, 0),
            sub.Product?.Id,
            sub.Product?.Handle,
            sub.Product?.Name,
            sub.Product?.PriceInCents is { } cents ? cents / 100m : null,
            sub.NextAssessmentAt,
            sub.CurrentPeriodEndsAt,
            sub.CanceledAt);
    }

    /// <summary>What the shopper still owes: the larger of the account balance and the unpaid invoices.</summary>
    private async Task<decimal> AmountOwed(Subscription sub)
    {
        var invoices = await ListAll(page => MaxioErrors.Run(async () =>
            (IReadOnlyList<Invoice>)(await _maxio.Invoices.ListInvoices(new ListInvoicesRequest
            {
                SubscriptionId = sub.Id,
                Page = page,
                PerPage = PAGE_SIZE
            })).Invoices));

        decimal invoiceDue = 0;
        foreach (var invoice in invoices)
        {
            if (invoice.Status == InvoiceStatus.Draft || invoice.Status == InvoiceStatus.Voided || invoice.Status == InvoiceStatus.Canceled)
            {
                continue;
            }
            if (decimal.TryParse(invoice.DueAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var due) && due > 0)
            {
                invoiceDue += due;
            }
        }

        var balance = (sub.BalanceInCents ?? 0) / 100m;
        return Math.Max(invoiceDue, balance);
    }

    private async Task<CatalogBrand> ResolveBrand(CreatePlanRequest request)
    {
        if (request.BrandId is null && string.IsNullOrWhiteSpace(request.Brand))
        {
            throw BillingException.BadRequest("brandId (or brand) is required.");
        }

        CatalogBrand? brand = null;
        if (request.BrandId is { } id)
        {
            brand = await _brands.GetByIdAsync(id);
            if (brand == null)
            {
                throw BillingException.BadRequest($"Brand {id} is not in the catalog.");
            }
        }
        if (!string.IsNullOrWhiteSpace(request.Brand))
        {
            var all = await _brands.ListAsync();
            var byName = all.FirstOrDefault(b => string.Equals(b.Brand, request.Brand.Trim(), StringComparison.OrdinalIgnoreCase));
            if (byName == null)
            {
                throw BillingException.BadRequest($"Brand '{request.Brand}' is not in the catalog.");
            }
            if (brand != null && brand.Id != byName.Id)
            {
                throw BillingException.BadRequest("brandId and brand name refer to different brands.");
            }
            brand = byName;
        }
        return brand!;
    }

    /// <summary>Each catalog brand is a Maxio product family, found (or created) by a handle derived from the brand id.</summary>
    private async Task<ProductFamily> EnsureProductFamily(CatalogBrand brand)
    {
        var handle = $"catalog-brand-{brand.Id}";
        var families = await MaxioErrors.Run(() => _maxio.ProductFamilies.ListProductFamilies(new ListProductFamiliesRequest()));
        var existing = families.Select(f => f.ProductFamily).FirstOrDefault(f => f != null && f.Handle == handle);
        if (existing != null)
        {
            return existing;
        }

        var created = await MaxioErrors.Run(() => _maxio.ProductFamilies.CreateProductFamily(new CreateProductFamilyOperationRequest
        {
            Body = new CreateProductFamilyRequest
            {
                ProductFamily = new CreateProductFamily { Name = brand.Brand, Handle = handle, Description = $"Plans for the {brand.Brand} brand" }
            }
        }));
        return created.ProductFamily!;
    }
}
