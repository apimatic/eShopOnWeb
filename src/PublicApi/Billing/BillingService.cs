using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
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
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public class BillingService : IBillingService
{
    private const int PAGE_SIZE = 200;
    private readonly MaxioAdvancedBillingClient _client;
    private readonly IReadRepository<CatalogBrand> _brands;
    private readonly UserManager<ApplicationUser> _userManager;

    public BillingService(MaxioAdvancedBillingClient client, IReadRepository<CatalogBrand> brands,
        UserManager<ApplicationUser> userManager)
    {
        _client = client;
        _brands = brands;
        _userManager = userManager;
    }

    // ---------------------------------------------------------------- plans

    public async Task<IReadOnlyList<PlanDto>> ListPlansAsync(CancellationToken ct)
    {
        var plans = new List<PlanDto>();
        for (var page = 1; ; page++)
        {
            var batch = await Call("list plans",
                () => _client.Products.ListProducts(new ListProductsRequest { Page = page, PerPage = PAGE_SIZE }, null, ct));
            plans.AddRange(batch.Select(p => p.Product).Where(p => p.ArchivedAt == null && p.Id != null).Select(ToPlan));
            if (batch.Count < PAGE_SIZE) break;
        }

        return plans;
    }

    public async Task<PlanDto> CreatePlanAsync(string? name, decimal? monthlyPrice, string? brand, int? brandId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) throw Bad("A plan needs a name.");
        if (monthlyPrice == null || monthlyPrice < 0 || decimal.Round(monthlyPrice.Value, 2) != monthlyPrice)
            throw Bad("monthlyPrice must be a non-negative amount with at most two decimals.");
        if (brandId == null && string.IsNullOrWhiteSpace(brand)) throw Bad("A plan needs a brand from the catalog.");

        var catalogBrand = await FindCatalogBrandAsync(brand, brandId);
        var handle = Slug(name);
        if (handle.Length == 0) throw Bad("The plan name must contain letters or digits.");

        var familyId = await FindOrCreateFamilyAsync(catalogBrand.Brand, ct);
        var cents = (long)decimal.Round(monthlyPrice.Value * 100m, 0);

        var created = await Call("create the plan", () => _client.Products.CreateProduct(new CreateProductRequest
        {
            ProductFamilyId = familyId.ToString(CultureInfo.InvariantCulture),
            Body = new CreateOrUpdateProductRequest
            {
                Product = new CreateOrUpdateProduct
                {
                    Name = name.Trim(),
                    Handle = handle,
                    Description = $"{name.Trim()} ({catalogBrand.Brand})",
                    PriceInCents = cents,
                    Interval = 1,
                    IntervalUnit = IntervalUnit.Month
                }
            }
        }, null, ct));
        return ToPlan(created.Product);
    }

    public async Task<CurrencyPriceDto> AddCurrencyPriceAsync(int planId, string? currency, decimal? price, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(currency) || !Regex.IsMatch(currency.Trim(), "^[A-Za-z]{3}$"))
            throw Bad("currency must be a three-letter currency code.");
        if (price == null || price <= 0) throw Bad("price must be greater than zero.");
        if (price != decimal.Truncate(price.Value) || price > int.MaxValue)
            throw Bad("price must be a whole number of currency units.");

        var product = (await Call("read the plan", () => _client.Products.ReadProduct(new ReadProductRequest { ProductId = planId }, null, ct))).Product;
        if (product.ArchivedAt != null) throw new BillingException(404, "Plan not found.");
        if (product.DefaultProductPricePointId == null)
            throw new BillingException(422, "The plan has no price point to attach a currency price to.");

        var code = currency.Trim().ToUpperInvariant();
        var response = await Call("add the currency price", () => _client.ProductPricePoints.CreateProductCurrencyPrices(
            new CreateProductCurrencyPricesOperationRequest
            {
                ProductPricePointId = product.DefaultProductPricePointId.Value,
                Body = new CreateProductCurrencyPricesRequest
                {
                    CurrencyPrices = new[]
                    {
                        new CreateProductCurrencyPrice { Currency = code, Price = (int)price.Value, Role = CurrencyPriceRole.Baseline }
                    }
                }
            }, null, ct));

        var stored = response.CurrencyPrices.FirstOrDefault(c => string.Equals(c.Currency, code, StringComparison.OrdinalIgnoreCase));
        return new CurrencyPriceDto { PlanId = planId, Currency = code, Price = stored?.Price != null ? (decimal)stored.Price.Value : price.Value };
    }

    // -------------------------------------------------------- subscriptions

    public async Task<SubscriptionDto> SubscribeAsync(string userName, string? planHandle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(planHandle)) throw Bad("planHandle is required.");
        var plan = await FindOfferedPlanAsync(planHandle, ct);

        // No card details: collect by invoice (remittance on Relationship Invoicing sites).
        var site = (await Call("read the billing site", () => _client.Sites.ReadSite(null, ct))).Site;
        var method = site.RelationshipInvoicingEnabled == true ? CollectionMethod.Remittance : CollectionMethod.Invoice;

        var create = new CreateSubscription { ProductHandle = plan.Handle, PaymentCollectionMethod = method };
        var existing = await FindCustomerAsync(userName, ct);
        if (existing?.Id != null)
        {
            create = create with { CustomerId = existing.Id };
        }
        else
        {
            var user = await _userManager.FindByNameAsync(userName);
            var email = user?.Email ?? userName;
            var local = email.Split('@')[0];
            create = create with
            {
                CustomerAttributes = new CustomerAttributes
                {
                    Reference = userName,
                    Email = email,
                    FirstName = string.IsNullOrWhiteSpace(local) ? "Shopper" : local,
                    LastName = "Shopper"
                }
            };
        }

        var created = await Call("subscribe to the plan", () => _client.Subscriptions.CreateSubscription(
            new CreateSubscriptionOperationRequest { Body = new CreateSubscriptionRequest { Subscription = create } }, null, ct));
        return ToSubscription(created.Subscription!);
    }

    public async Task<SubscriptionDto> GetSubscriptionAsync(string userName, int subscriptionId, CancellationToken ct)
        => ToSubscription(await ReadOwnedAsync(userName, subscriptionId, ct));

    public async Task<SubscriptionDto> PauseAsync(string userName, int subscriptionId, CancellationToken ct)
    {
        var sub = await ReadOwnedAsync(userName, subscriptionId, ct);
        if (sub.State == SubscriptionState.OnHold) throw new BillingException(409, "The subscription is already paused.");
        if (sub.State == SubscriptionState.Canceled) throw new BillingException(409, "A cancelled subscription cannot be paused; restart it first.");
        return ToSubscription(await Call("pause the subscription", async () =>
            (await _client.SubscriptionStatus.PauseSubscription(new PauseSubscriptionRequest { SubscriptionId = subscriptionId }, null, ct)).Subscription!));
    }

    public async Task<SubscriptionDto> ResumeAsync(string userName, int subscriptionId, CancellationToken ct)
    {
        var sub = await ReadOwnedAsync(userName, subscriptionId, ct);
        if (sub.State != SubscriptionState.OnHold) throw new BillingException(409, "Only a paused subscription can be resumed.");
        return ToSubscription(await Call("resume the subscription", async () =>
            (await _client.SubscriptionStatus.ResumeSubscription(new ResumeSubscriptionRequest { SubscriptionId = subscriptionId }, null, ct)).Subscription!));
    }

    public async Task<SubscriptionDto> CancelAsync(string userName, int subscriptionId, CancellationToken ct)
    {
        var sub = await ReadOwnedAsync(userName, subscriptionId, ct);
        if (sub.State == SubscriptionState.Canceled) throw new BillingException(409, "The subscription is already cancelled.");
        return ToSubscription(await Call("cancel the subscription", async () =>
            (await _client.SubscriptionStatus.CancelSubscription(new CancelSubscriptionRequest
            {
                SubscriptionId = subscriptionId,
                Body = new CancellationRequest { Subscription = new CancellationOptions() }
            }, null, ct)).Subscription!));
    }

    public async Task<SubscriptionDto> RestartAsync(string userName, int subscriptionId, CancellationToken ct)
    {
        var sub = await ReadOwnedAsync(userName, subscriptionId, ct);
        if (sub.State != SubscriptionState.Canceled) throw new BillingException(409, "Only a cancelled subscription can be restarted.");
        return ToSubscription(await Reactivate(subscriptionId, ct));
    }

    public async Task<SubscriptionDto> SwitchPlanAsync(string userName, int subscriptionId, string? planHandle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(planHandle)) throw Bad("planHandle is required.");
        var sub = await ReadOwnedAsync(userName, subscriptionId, ct);
        var plan = await FindOfferedPlanAsync(planHandle, ct);
        if (sub.Product?.Id == plan.Id) throw new BillingException(409, "The subscription is already on that plan.");

        // Maxio only migrates active or trialing subscriptions; bring a cancelled or paused one back first.
        var wasCanceled = sub.State == SubscriptionState.Canceled;
        var wasPaused = sub.State == SubscriptionState.OnHold;
        if (wasCanceled)
        {
            await Reactivate(subscriptionId, ct);
        }
        else if (wasPaused)
        {
            await Call("resume the subscription", () => _client.SubscriptionStatus.ResumeSubscription(
                new ResumeSubscriptionRequest { SubscriptionId = subscriptionId }, null, ct));
        }

        Subscription migrated;
        try
        {
            migrated = await Call("switch the plan", async () => (await _client.SubscriptionProducts.MigrateSubscriptionProduct(
                new MigrateSubscriptionProductRequest
                {
                    SubscriptionId = subscriptionId,
                    Body = new SubscriptionProductMigrationRequest
                    {
                        Migration = new SubscriptionProductMigration { ProductId = plan.Id }
                    }
                }, null, ct)).Subscription!);
        }
        catch (BillingException)
        {
            // Put the subscription back the way it was; best effort.
            try
            {
                if (wasCanceled)
                {
                    await _client.SubscriptionStatus.CancelSubscription(new CancelSubscriptionRequest
                    {
                        SubscriptionId = subscriptionId,
                        Body = new CancellationRequest { Subscription = new CancellationOptions() }
                    });
                }
                else if (wasPaused)
                {
                    await _client.SubscriptionStatus.PauseSubscription(new PauseSubscriptionRequest { SubscriptionId = subscriptionId });
                }
            }
            catch (Exception)
            {
                // the original failure is the one worth reporting
            }

            throw;
        }

        if (wasPaused)
        {
            try
            {
                migrated = (await _client.SubscriptionStatus.PauseSubscription(
                    new PauseSubscriptionRequest { SubscriptionId = subscriptionId }, null, ct)).Subscription ?? migrated;
            }
            catch (Exception)
            {
                // Maxio refuses a hold close to the billing date; the switch itself succeeded.
            }
        }

        return ToSubscription(migrated);
    }

    public async Task<UsageDto> GetUsageAsync(string userName, int subscriptionId, string? date, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(date)
            || !DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            throw Bad("date is required and must look like YYYY-MM-DD.");

        var sub = await ReadOwnedAsync(userName, subscriptionId, ct);
        // Maxio reports timestamps in the site's time zone, so "a day" is a day at that offset.
        var offset = sub.CreatedAt?.Offset ?? TimeSpan.Zero;
        var since = new DateTimeOffset(day.AddDays(-1), offset);
        var until = new DateTimeOffset(day.AddDays(2), offset);

        var components = await Call("list the subscription's components", () => _client.SubscriptionComponents.ListSubscriptionComponents(
            new ListSubscriptionComponentsRequest { SubscriptionId = subscriptionId }, null, ct));

        var result = new UsageDto { SubscriptionId = subscriptionId, Date = date };
        foreach (var c in components.Select(r => r.Component).Where(c => c?.Id != null && c.ComponentId != null))
        {
            var kind = c!.Kind;
            if (kind != ComponentKind.MeteredComponent && kind != ComponentKind.PrepaidUsageComponent) continue;

            var entries = new List<UsageEntryDto>();
            for (var page = 1; ; page++)
            {
                var batch = await Call("read usage", () => _client.SubscriptionComponents.ListUsages(new ListUsagesRequest
                {
                    SubscriptionIdOrReference = SubscriptionIdOrReference.Int(subscriptionId),
                    ComponentId = ComponentIdModel.Int(c.ComponentId!.Value),
                    SinceDate = since,
                    UntilDate = until,
                    Page = page,
                    PerPage = PAGE_SIZE
                }, null, ct));
                entries.AddRange(batch.Select(u => u.Usage)
                    .Where(u => u.CreatedAt != null && u.CreatedAt.Value.Date == day.Date)
                    .Select(u => new UsageEntryDto { Id = u.Id, Quantity = ParseQuantity(u.Quantity), Memo = u.Memo, RecordedAt = u.CreatedAt }));
                if (batch.Count < PAGE_SIZE) break;
            }

            result.Components.Add(new ComponentUsageDto
            {
                ComponentId = c.ComponentId!.Value,
                ComponentName = c.Name,
                ComponentHandle = c.ComponentHandle,
                UnitName = c.UnitName,
                TotalQuantity = entries.Sum(e => e.Quantity),
                Entries = entries
            });
        }

        return result;
    }

    public async Task<SubscriptionDto> SetNextBillingDateAsync(int subscriptionId, string? date, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(date)
            || !DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            throw Bad("date is required and must look like YYYY-MM-DD.");

        var sub = await Call("read the subscription", async () =>
            (await _client.Subscriptions.ReadSubscription(new ReadSubscriptionRequest { SubscriptionId = subscriptionId }, null, ct)).Subscription!);
        if (sub.State == SubscriptionState.Canceled) throw new BillingException(409, "A cancelled subscription is not billed; restart it first.");

        var offset = sub.CreatedAt?.Offset ?? TimeSpan.Zero;
        var today = DateTimeOffset.UtcNow.ToOffset(offset).Date;
        if (day.Date <= today) throw Bad("The next billing date must be in the future.");

        // A bare date means noon in the site's time zone; say so explicitly.
        var billAt = new DateTimeOffset(day.Date.AddHours(12), offset);
        var updated = await Call("set the next billing date", async () => (await _client.Subscriptions.UpdateSubscription(
            new UpdateSubscriptionOperationRequest
            {
                SubscriptionId = subscriptionId,
                Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { NextBillingAt = billAt } }
            }, null, ct)).Subscription!);

        // The update response omits the new date; Maxio shows it as the end of the current period.
        var dto = ToSubscription(updated);
        dto.NextBillingDate = updated.CurrentPeriodEndsAt ?? updated.NextAssessmentAt ?? billAt;
        return dto;
    }

    // --------------------------------------------------------------- helpers

    private async Task<Subscription> Reactivate(int subscriptionId, CancellationToken ct)
        => await Call("restart the subscription", async () => (await _client.SubscriptionStatus.ReactivateSubscription(
            new ReactivateSubscriptionOperationRequest
            {
                SubscriptionId = subscriptionId,
                Body = new ReactivateSubscriptionRequest { Resume = Resume.Bool(true) }
            }, null, ct)).Subscription!);

    /// <summary>Reads a subscription and checks that it belongs to the caller; anyone else's is "not found".</summary>
    private async Task<Subscription> ReadOwnedAsync(string userName, int subscriptionId, CancellationToken ct)
    {
        Subscription sub;
        try
        {
            sub = (await _client.Subscriptions.ReadSubscription(new ReadSubscriptionRequest { SubscriptionId = subscriptionId }, null, ct)).Subscription!;
        }
        catch (Exception ex)
        {
            var translated = MaxioErrors.Translate(ex, "read the subscription");
            throw translated.StatusCode == 404 ? new BillingException(404, "Subscription not found.", ex) : translated;
        }

        if (!string.Equals(sub.Customer?.Reference, userName, StringComparison.OrdinalIgnoreCase))
            throw new BillingException(404, "Subscription not found.");
        return sub;
    }

    private async Task<Customer?> FindCustomerAsync(string userName, CancellationToken ct)
    {
        try
        {
            return (await _client.Customers.ReadCustomerByReference(new ReadCustomerByReferenceRequest { Reference = userName }, null, ct)).Customer;
        }
        catch (Exception ex)
        {
            var translated = MaxioErrors.Translate(ex, "look up the customer");
            if (translated.StatusCode == 404) return null;
            throw translated;
        }
    }

    /// <summary>A plan can only be subscribed to or switched to when the plan list offers it.</summary>
    private async Task<PlanDto> FindOfferedPlanAsync(string handle, CancellationToken ct)
    {
        var plans = await ListPlansAsync(ct);
        return plans.FirstOrDefault(p => string.Equals(p.Handle, handle.Trim(), StringComparison.OrdinalIgnoreCase))
               ?? throw new BillingException(404, $"No plan with handle '{handle}' is on offer.");
    }

    private async Task<CatalogBrand> FindCatalogBrandAsync(string? brand, int? brandId)
    {
        var all = await _brands.ListAsync();
        var match = brandId != null
            ? all.FirstOrDefault(b => b.Id == brandId)
            : all.FirstOrDefault(b => string.Equals(b.Brand, brand!.Trim(), StringComparison.OrdinalIgnoreCase));
        return match ?? throw Bad("The brand is not one of the shop's catalog brands.");
    }

    private async Task<int> FindOrCreateFamilyAsync(string brandName, CancellationToken ct)
    {
        var families = await Call("list brands", () => _client.ProductFamilies.ListProductFamilies(new ListProductFamiliesRequest(), null, ct));
        var found = families.Select(f => f.ProductFamily)
            .FirstOrDefault(f => f?.Id != null && f.ArchivedAt == null && string.Equals(f.Name, brandName, StringComparison.OrdinalIgnoreCase));
        if (found != null) return found.Id!.Value;

        var created = await Call("create the brand's plan family", () => _client.ProductFamilies.CreateProductFamily(
            new CreateProductFamilyOperationRequest
            {
                Body = new CreateProductFamilyRequest { ProductFamily = new CreateProductFamily { Name = brandName, Handle = Slug(brandName) } }
            }, null, ct));
        return created.ProductFamily!.Id!.Value;
    }

    private static async Task<T> Call<T>(string action, Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (Exception ex)
        {
            throw MaxioErrors.Translate(ex, action);
        }
    }

    private static BillingException Bad(string message) => new(400, message);

    private static string Slug(string text)
    {
        var sb = new StringBuilder();
        foreach (var ch in text.Trim().ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }

        return sb.ToString().Trim('-');
    }

    private static decimal ParseQuantity(Quantity1? q)
    {
        if (q == null) return 0;
        if (q.TryGetInt(out var i)) return i;
        return q.TryGetString(out var s) && decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    private static PlanDto ToPlan(Product p) => new()
    {
        Id = p.Id!.Value,
        Handle = p.Handle ?? string.Empty,
        Name = p.Name ?? string.Empty,
        Description = p.Description,
        MonthlyPrice = (p.PriceInCents ?? 0) / 100m,
        Interval = p.Interval ?? 1,
        IntervalUnit = p.IntervalUnit?.Value,
        Brand = p.ProductFamily?.Name
    };

    private static SubscriptionDto ToSubscription(Subscription s)
    {
        var balance = (s.BalanceInCents ?? 0) / 100m;
        return new SubscriptionDto
        {
            SubscriptionId = s.Id ?? 0,
            State = s.State?.Value ?? string.Empty,
            PlanId = s.Product?.Id,
            PlanHandle = s.Product?.Handle,
            PlanName = s.Product?.Name,
            Paid = balance <= 0,
            BalanceDue = balance > 0 ? balance : 0,
            Currency = s.Currency,
            NextBillingDate = s.CurrentPeriodEndsAt ?? s.NextAssessmentAt,
            CanceledAt = s.CanceledAt,
            CreatedAt = s.CreatedAt
        };
    }
}
