using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.AnyOf;
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
using Microsoft.eShopWeb.PublicApi.BillingEndpoints;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// All billing behaviour of the shop. Maxio is the system of record: nothing is cached or stored locally.
/// </summary>
public class BillingService
{
    private static readonly HashSet<string> NotPausableStates = new(StringComparer.Ordinal)
    {
        "on_hold", "canceled", "expired", "failed_to_create", "pending", "awaiting_signup", "unpaid", "suspended"
    };

    private readonly MaxioClientProvider _clients;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IReadRepository<CatalogBrand> _brands;

    public BillingService(MaxioClientProvider clients, UserManager<ApplicationUser> userManager,
        IReadRepository<CatalogBrand> brands)
    {
        _clients = clients;
        _userManager = userManager;
        _brands = brands;
    }

    private MaxioAdvancedBillingClient Client => _clients.Client;

    private static async Task<T> Call<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw MaxioErrors.Translate(ex);
        }
    }

    private static bool IsNotFound(Exception ex) =>
        ex is ApiException api && (int)api.StatusCode == 404;

    // ------------------------------------------------------------------ plans

    public async Task<List<PlanDto>> ListPlansAsync(CancellationToken ct = default)
    {
        var plans = new List<PlanDto>();
        for (var page = 1; ; page++)
        {
            var batch = await Call(() => Client.Products.ListProducts(
                new ListProductsRequest { Page = page, PerPage = 200, IncludeArchived = false }, null, ct));
            foreach (var item in batch)
            {
                var p = item.Product;
                // A plan can only be subscribed to by handle; archived plans are closed.
                if (p.ArchivedAt != null || string.IsNullOrWhiteSpace(p.Handle)) continue;
                plans.Add(ToPlan(p));
            }
            if (batch.Count < 200) break;
        }
        return plans;
    }

    private static PlanDto ToPlan(Product p) => new()
    {
        Id = p.Id ?? 0,
        Handle = p.Handle,
        Name = p.Name,
        Description = p.Description,
        PriceInCents = p.PriceInCents ?? 0,
        Price = (p.PriceInCents ?? 0) / 100m,
        Interval = p.Interval,
        IntervalUnit = p.IntervalUnit?.Value,
        Brand = BrandFromFamily(p.ProductFamily?.Name),
    };

    private static string? BrandFromFamily(string? familyName)
    {
        if (string.IsNullOrWhiteSpace(familyName)) return familyName;
        const string suffix = " plans";
        return familyName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? familyName[..^suffix.Length]
            : familyName;
    }

    private async Task<Product> GetPlanByHandle(string handle)
    {
        try
        {
            var response = await Client.Products.ReadProductByHandle(new ReadProductByHandleRequest { ApiHandle = handle });
            return response.Product;
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            throw BillingException.NotFound($"There is no plan with handle '{handle}'.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw MaxioErrors.Translate(ex);
        }
    }

    private async Task<Product> GetSubscribablePlan(string? handle)
    {
        if (string.IsNullOrWhiteSpace(handle))
            throw BillingException.BadRequest("The plan's handle is required.");
        var plan = await GetPlanByHandle(handle!);
        if (plan.ArchivedAt != null)
            throw BillingException.NotFound($"The plan '{handle}' is no longer available.");
        return plan;
    }

    // -------------------------------------------------------------- customers

    private async Task<Customer> EnsureCustomer(string userName)
    {
        try
        {
            var existing = await Client.Customers.ReadCustomerByReference(new ReadCustomerByReferenceRequest { Reference = userName });
            if (existing.Customer?.Id != null) return existing.Customer;
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            // not yet a customer
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw MaxioErrors.Translate(ex);
        }

        var user = await _userManager.FindByNameAsync(userName);
        var email = !string.IsNullOrWhiteSpace(user?.Email) ? user!.Email!
            : userName.Contains('@') ? userName
            : $"{Regex.Replace(userName, "[^A-Za-z0-9._-]", "")}@example.com";
        var local = email.Split('@')[0];

        var created = await Call(() => Client.Customers.CreateCustomer(new CreateCustomerOperationRequest
        {
            Body = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = local,
                    LastName = "Shopper",
                    Email = email,
                    Reference = userName,
                }
            }
        }));
        return created.Customer;
    }

    // ----------------------------------------------------------- subscriptions

    public async Task<SubscriptionDto> SubscribeAsync(string userName, string? planHandle)
    {
        var plan = await GetSubscribablePlan(planHandle);
        var customer = await EnsureCustomer(userName);

        var created = await Call(() => Client.Subscriptions.CreateSubscription(new CreateSubscriptionOperationRequest
        {
            Body = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = plan.Handle,
                    CustomerId = customer.Id,
                    // No card details are collected: the shopper is invoiced.
                    PaymentCollectionMethod = CollectionMethod.Invoice,
                }
            }
        }));
        return await ToSubscriptionDto(created.Subscription);
    }

    private async Task<Subscription> GetOwnedSubscription(string userName, int subscriptionId)
    {
        Subscription sub;
        try
        {
            sub = (await Client.Subscriptions.ReadSubscription(new ReadSubscriptionRequest { SubscriptionId = subscriptionId })).Subscription;
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            throw BillingException.NotFound($"Subscription {subscriptionId} was not found.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw MaxioErrors.Translate(ex);
        }

        // Someone else's subscription is reported exactly like a missing one.
        if (!string.Equals(sub.Customer?.Reference, userName, StringComparison.OrdinalIgnoreCase))
            throw BillingException.NotFound($"Subscription {subscriptionId} was not found.");
        return sub;
    }

    public async Task<SubscriptionDto> GetSubscriptionAsync(string userName, int subscriptionId)
    {
        var sub = await GetOwnedSubscription(userName, subscriptionId);
        return await ToSubscriptionDto(sub);
    }

    private static string StateOf(Subscription s) => s.State?.Value ?? string.Empty;

    public async Task<SubscriptionDto> PauseAsync(string userName, int subscriptionId)
    {
        var sub = await GetOwnedSubscription(userName, subscriptionId);
        var state = StateOf(sub);
        if (state == "on_hold") throw BillingException.Conflict("The subscription is already paused.");
        if (NotPausableStates.Contains(state))
            throw BillingException.Conflict($"A subscription that is {state.Replace('_', ' ')} cannot be paused.");

        var result = await Call(() => Client.SubscriptionStatus.PauseSubscription(
            new PauseSubscriptionRequest { SubscriptionId = subscriptionId, Body = new PauseRequest() }));
        return await ToSubscriptionDto(result.Subscription);
    }

    public async Task<SubscriptionDto> ResumeAsync(string userName, int subscriptionId)
    {
        var sub = await GetOwnedSubscription(userName, subscriptionId);
        var state = StateOf(sub);
        if (state != "on_hold")
            throw BillingException.Conflict($"Only a paused subscription can be resumed; this one is {state.Replace('_', ' ')}.");

        var result = await Call(() => Client.SubscriptionStatus.ResumeSubscription(
            new ResumeSubscriptionRequest { SubscriptionId = subscriptionId }));
        return await ToSubscriptionDto(result.Subscription);
    }

    public async Task<SubscriptionDto> CancelAsync(string userName, int subscriptionId)
    {
        var sub = await GetOwnedSubscription(userName, subscriptionId);
        var state = StateOf(sub);
        if (state == "canceled") throw BillingException.Conflict("The subscription is already cancelled.");
        if (state is "expired" or "failed_to_create")
            throw BillingException.Conflict($"A subscription that is {state.Replace('_', ' ')} cannot be cancelled.");

        var result = await Call(() => Client.SubscriptionStatus.CancelSubscription(new CancelSubscriptionRequest
        {
            SubscriptionId = subscriptionId,
            Body = new CancellationRequest { Subscription = new CancellationOptions { CancelAtEndOfPeriod = false } },
        }));
        return await ToSubscriptionDto(result.Subscription);
    }

    public async Task<SubscriptionDto> RestartAsync(string userName, int subscriptionId)
    {
        var sub = await GetOwnedSubscription(userName, subscriptionId);
        var state = StateOf(sub);
        if (state != "canceled")
            throw BillingException.Conflict($"Only a cancelled subscription can be restarted; this one is {state.Replace('_', ' ')}.");

        var result = await Call(() => Client.SubscriptionStatus.ReactivateSubscription(
            new ReactivateSubscriptionOperationRequest { SubscriptionId = subscriptionId, Body = new ReactivateSubscriptionRequest() }));
        return await ToSubscriptionDto(result.Subscription);
    }

    public async Task<SubscriptionDto> SwitchPlanAsync(string userName, int subscriptionId, string? planHandle)
    {
        var plan = await GetSubscribablePlan(planHandle);
        var sub = await GetOwnedSubscription(userName, subscriptionId);
        var state = StateOf(sub);

        if (sub.Product?.Id == plan.Id && state is not ("canceled" or "on_hold"))
            throw BillingException.Conflict($"The subscription is already on the plan '{plan.Handle}'.");
        if (state is "expired" or "failed_to_create" or "pending" or "awaiting_signup")
            throw BillingException.Conflict($"A subscription that is {state.Replace('_', ' ')} cannot change plan.");

        // Maxio only migrates a live subscription. A cancelled one is restarted and a paused one resumed
        // first, so that the shopper ends up active on the new plan; if the migration then fails, the
        // subscription is put back as it was.
        var restoreState = state;
        if (state == "canceled")
        {
            await Call(() => Client.SubscriptionStatus.ReactivateSubscription(
                new ReactivateSubscriptionOperationRequest { SubscriptionId = subscriptionId, Body = new ReactivateSubscriptionRequest() }));
        }
        else if (state == "on_hold")
        {
            await Call(() => Client.SubscriptionStatus.ResumeSubscription(
                new ResumeSubscriptionRequest { SubscriptionId = subscriptionId }));
        }

        try
        {
            var migrated = await Call(() => Client.SubscriptionProducts.MigrateSubscriptionProduct(
                new MigrateSubscriptionProductRequest
                {
                    SubscriptionId = subscriptionId,
                    Body = new SubscriptionProductMigrationRequest
                    {
                        Migration = new SubscriptionProductMigration { ProductId = plan.Id }
                    }
                }));
            return await ToSubscriptionDto(migrated.Subscription);
        }
        catch
        {
            await TryRestore(subscriptionId, restoreState);
            throw;
        }
    }

    private async Task TryRestore(int subscriptionId, string originalState)
    {
        try
        {
            if (originalState == "canceled")
                await Client.SubscriptionStatus.CancelSubscription(new CancelSubscriptionRequest
                {
                    SubscriptionId = subscriptionId,
                    Body = new CancellationRequest { Subscription = new CancellationOptions { CancelAtEndOfPeriod = false } },
                });
            else if (originalState == "on_hold")
                await Client.SubscriptionStatus.PauseSubscription(
                    new PauseSubscriptionRequest { SubscriptionId = subscriptionId, Body = new PauseRequest() });
        }
        catch
        {
            // best effort only; the original failure is what the caller needs to see
        }
    }

    private async Task<SubscriptionDto> ToSubscriptionDto(Subscription sub)
    {
        var owes = (sub.BalanceInCents ?? 0) > 0;
        if (!owes && sub.Id != null)
        {
            // Maxio's balance only tells part of the story for invoiced subscriptions: also look for open invoices.
            var invoices = await Call(() => Client.Invoices.ListInvoices(new ListInvoicesRequest
            {
                SubscriptionId = sub.Id,
                Status = InvoiceStatus.Open,
                PerPage = 200,
            }));
            owes = invoices.Invoices.Any(i =>
                decimal.TryParse(i.DueAmount, NumberStyles.Any, CultureInfo.InvariantCulture, out var due) && due > 0);
        }

        return new SubscriptionDto
        {
            Id = sub.Id ?? 0,
            State = sub.State?.Value,
            Paid = !owes,
            BalanceInCents = sub.BalanceInCents ?? 0,
            Plan = sub.Product != null ? ToPlan(sub.Product) : null,
            NextBillingAt = sub.CurrentPeriodEndsAt,
            CreatedAt = sub.CreatedAt,
            CanceledAt = sub.CanceledAt,
            Currency = sub.Currency,
        };
    }

    // ------------------------------------------------------------------ usage

    public async Task<DailyUsageResponse> GetUsageAsync(string userName, int subscriptionId, DateOnly date)
    {
        await GetOwnedSubscription(userName, subscriptionId);

        var components = await Call(() => Client.SubscriptionComponents.ListSubscriptionComponents(
            new ListSubscriptionComponentsRequest { SubscriptionId = subscriptionId }));

        var result = new DailyUsageResponse { SubscriptionId = subscriptionId, Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };

        // Query a generous window around the day (Maxio reads the bounds in the site's time zone), then keep
        // exactly the usages recorded on the requested calendar day, in the site's own time.
        var since = new DateTimeOffset(date.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var until = new DateTimeOffset(date.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        foreach (var wrapper in components.Where(c => c.Component?.Kind == ComponentKind.MeteredComponent))
        {
            var comp = wrapper.Component!;
            if (comp.ComponentId == null) continue;
            var dto = new ComponentUsageDto
            {
                ComponentId = comp.ComponentId.Value,
                ComponentHandle = comp.ComponentHandle,
                Name = comp.Name,
                UnitName = comp.UnitName,
            };

            for (var page = 1; ; page++)
            {
                var usages = await Call(() => Client.SubscriptionComponents.ListUsages(new ListUsagesRequest
                {
                    SubscriptionIdOrReference = subscriptionId,
                    ComponentId = comp.ComponentId.Value,
                    SinceDate = since,
                    UntilDate = until,
                    Page = page,
                    PerPage = 200,
                }));

                foreach (var u in usages.Select(x => x.Usage).Where(x => x?.CreatedAt != null))
                {
                    var created = u!.CreatedAt!.Value;
                    if (DateOnly.FromDateTime(created.DateTime) != date) continue;
                    var qty = ParseQuantity(u.Quantity);
                    dto.Usages.Add(new UsageEntryDto { Id = u.Id, Quantity = qty, Memo = u.Memo, CreatedAt = created });
                    dto.Quantity += qty;
                }
                if (usages.Count < 200) break;
            }

            result.Components.Add(dto);
            result.TotalQuantity += dto.Quantity;
        }
        return result;
    }

    private static decimal ParseQuantity(Quantity1? q)
    {
        if (q == null) return 0;
        if (q.TryGetInt(out var i)) return i;
        if (q.TryGetString(out var s) &&
            decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return d;
        return 0;
    }

    // ------------------------------------------------------------ staff: plans

    public async Task<PlanDto> CreatePlanAsync(CreatePlanRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw BillingException.BadRequest("The plan's name is required.");
        var price = request.Price ?? request.MonthlyPrice;
        if (price == null) throw BillingException.BadRequest("The plan's monthly price is required.");
        if (price < 0) throw BillingException.BadRequest("The monthly price cannot be negative.");
        if (decimal.Round(price.Value, 2) != price.Value)
            throw BillingException.BadRequest("The monthly price cannot have more than two decimal places.");

        var catalogBrand = await ResolveBrand(request);

        var name = request.Name!.Trim();
        var handle = Slugify(name);
        if (handle.Length == 0) throw BillingException.BadRequest("The plan's name must contain letters or digits.");

        // Handles identify plans across the whole site.
        try
        {
            await Client.Products.ReadProductByHandle(new ReadProductByHandleRequest { ApiHandle = handle });
            throw BillingException.Conflict($"A plan with handle '{handle}' already exists.");
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            // free to use
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not BillingException)
        {
            throw MaxioErrors.Translate(ex);
        }

        var family = await EnsureProductFamily(catalogBrand.Brand);

        var created = await Call(() => Client.Products.CreateProduct(new CreateProductRequest
        {
            ProductFamilyId = family.Id!.Value.ToString(CultureInfo.InvariantCulture),
            Body = new CreateOrUpdateProductRequest
            {
                Product = new CreateOrUpdateProduct
                {
                    Name = name,
                    Handle = handle,
                    Description = string.IsNullOrWhiteSpace(request.Description) ? name : request.Description!.Trim(),
                    PriceInCents = (long)decimal.Round(price.Value * 100m),
                    Interval = 1,
                    IntervalUnit = IntervalUnit.Month,
                    RequireCreditCard = false,
                }
            }
        }));
        return ToPlan(created.Product);
    }

    private async Task<CatalogBrand> ResolveBrand(CreatePlanRequest request)
    {
        var brands = await _brands.ListAsync();
        CatalogBrand? match = null;
        if (request.BrandId != null)
            match = brands.FirstOrDefault(b => b.Id == request.BrandId);
        else if (!string.IsNullOrWhiteSpace(request.Brand))
        {
            var wanted = request.Brand!.Trim();
            match = brands.FirstOrDefault(b => string.Equals(b.Brand, wanted, StringComparison.OrdinalIgnoreCase));
            if (match == null && int.TryParse(wanted, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                match = brands.FirstOrDefault(b => b.Id == id);
        }
        else
            throw BillingException.BadRequest("The plan's brand is required.");

        return match ?? throw BillingException.BadRequest(
            $"The brand is not one of the catalog brands: {string.Join(", ", brands.Select(b => b.Brand))}.");
    }

    /// <summary>A plan belongs to the product family named after its brand.</summary>
    private async Task<ProductFamily> EnsureProductFamily(string brand)
    {
        var families = await Call(() => Client.ProductFamilies.ListProductFamilies(new ListProductFamiliesRequest()));
        var found = families.Select(f => f.ProductFamily)
            .Where(f => f != null && f.ArchivedAt == null)
            .FirstOrDefault(f => string.Equals(f!.Name, brand, StringComparison.OrdinalIgnoreCase))
            ?? families.Select(f => f.ProductFamily)
                .Where(f => f != null && f.ArchivedAt == null)
                .FirstOrDefault(f => string.Equals(f!.Name, brand + " plans", StringComparison.OrdinalIgnoreCase));
        if (found != null) return found;

        var created = await Call(() => Client.ProductFamilies.CreateProductFamily(new CreateProductFamilyOperationRequest
        {
            Body = new CreateProductFamilyRequest
            {
                ProductFamily = new CreateProductFamily { Name = brand }
            }
        }));
        return created.ProductFamily ?? throw new BillingException(502, "The billing system did not return the new product family.");
    }

    private static string Slugify(string name)
    {
        var sb = new StringBuilder();
        var lastDash = true;
        foreach (var ch in name.ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') { sb.Append(ch); lastDash = false; }
            else if (!lastDash) { sb.Append('-'); lastDash = true; }
        }
        return sb.ToString().Trim('-');
    }

    public async Task<CurrencyPriceResponse> AddCurrencyPriceAsync(int planId, string? currency, decimal? price)
    {
        if (string.IsNullOrWhiteSpace(currency) || !Regex.IsMatch(currency.Trim(), "^[A-Za-z]{3}$"))
            throw BillingException.BadRequest("The currency must be a three-letter ISO code such as EUR.");
        if (price == null || price <= 0)
            throw BillingException.BadRequest("The price must be greater than zero.");
        if (decimal.Round(price.Value, 2) != price.Value)
            throw BillingException.BadRequest("The price cannot have more than two decimal places.");
        var code = currency.Trim().ToUpperInvariant();

        Product product;
        try
        {
            product = (await Client.Products.ReadProduct(new ReadProductRequest { ProductId = planId })).Product;
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            throw BillingException.NotFound($"Plan {planId} was not found.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw MaxioErrors.Translate(ex);
        }

        var pricePointId = product.DefaultProductPricePointId ?? product.ProductPricePointId;
        if (pricePointId == null)
            throw BillingException.Conflict("The plan has no price point to attach the currency price to.");

        // The SDK takes whole currency units when creating; fractional prices are applied right after.
        var whole = (int)decimal.Truncate(price.Value);
        var created = await Call(() => Client.ProductPricePoints.CreateProductCurrencyPrices(
            new CreateProductCurrencyPricesOperationRequest
            {
                ProductPricePointId = pricePointId.Value,
                Body = new CreateProductCurrencyPricesRequest
                {
                    CurrencyPrices = new[]
                    {
                        new CreateProductCurrencyPrice { Currency = code, Price = whole, Role = CurrencyPriceRole.Baseline }
                    }
                }
            }));

        var prices = created.CurrencyPrices;
        if ((double)price.Value != whole)
        {
            var mine = prices.FirstOrDefault(p => string.Equals(p.Currency, code, StringComparison.OrdinalIgnoreCase) && p.Id != null);
            if (mine?.Id != null)
            {
                var updated = await Call(() => Client.ProductPricePoints.UpdateProductCurrencyPrices(
                    new UpdateProductCurrencyPricesRequest
                    {
                        ProductPricePointId = pricePointId.Value,
                        Body = new UpdateCurrencyPricesRequest
                        {
                            CurrencyPrices = new[] { new UpdateCurrencyPrice { Id = mine.Id.Value, Price = (double)price.Value } }
                        }
                    }));
                prices = updated.CurrencyPrices;
            }
        }

        return new CurrencyPriceResponse
        {
            PlanId = planId,
            CurrencyPrices = prices.Select(p => new CurrencyPriceDto
            {
                Id = p.Id,
                Currency = p.Currency,
                Price = p.Price,
                FormattedPrice = p.FormattedPrice,
            }).ToList(),
        };
    }

    // ---------------------------------------------------- staff: billing date

    public async Task<SubscriptionDto> SetNextBillingDateAsync(int subscriptionId, DateOnly date)
    {
        Subscription sub;
        try
        {
            sub = (await Client.Subscriptions.ReadSubscription(new ReadSubscriptionRequest { SubscriptionId = subscriptionId })).Subscription;
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            throw BillingException.NotFound($"Subscription {subscriptionId} was not found.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw MaxioErrors.Translate(ex);
        }

        var state = StateOf(sub);
        if (state is "canceled" or "on_hold" or "expired" or "failed_to_create" or "pending" or "awaiting_signup")
            throw BillingException.Conflict($"The next billing date of a subscription that is {state.Replace('_', ' ')} cannot be changed.");

        // Work in the subscription's own time zone: noon on the chosen day.
        var offset = sub.CurrentPeriodEndsAt?.Offset ?? TimeSpan.Zero;
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(offset).DateTime);
        if (date <= today)
            throw BillingException.BadRequest("The next billing date must be in the future.");
        var target = new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), offset);

        var updated = await Call(() => Client.Subscriptions.UpdateSubscription(new UpdateSubscriptionOperationRequest
        {
            SubscriptionId = subscriptionId,
            Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { NextBillingAt = target } },
        }));
        return await ToSubscriptionDto(updated.Subscription);
    }
}
