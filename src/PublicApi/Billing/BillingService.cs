using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
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

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Subscription billing on top of Maxio (the billing system of record). Every call goes to Maxio through the SDK.
/// A shopper is identified to Maxio by a customer whose reference is the shopper's user name.
/// </summary>
public class BillingService : IBillingService
{
    private const int PAGE_SIZE = 200;
    private readonly MaxioAdvancedBillingClient _client;

    public BillingService(MaxioAdvancedBillingClient client)
    {
        _client = client;
    }

    // ---------------------------------------------------------------- shopper

    public async Task<IReadOnlyList<PlanDto>> ListPlansAsync(CancellationToken ct)
    {
        return await Call(async () =>
        {
            var plans = new List<PlanDto>();
            for (var page = 1; ; page++)
            {
                var batch = await _client.Products.ListProducts(
                    new ListProductsRequest { Page = page, PerPage = PAGE_SIZE, IncludeArchived = false },
                    cancellationToken: ct);
                plans.AddRange(batch.Select(p => ToPlan(p.Product)).Where(p => !p.Archived));
                if (batch.Count < PAGE_SIZE) break;
            }
            return (IReadOnlyList<PlanDto>)plans;
        });
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(string user, string? email, CancellationToken ct)
    {
        return await Call(async () =>
        {
            var customerId = await FindCustomerIdAsync(user, ct);
            if (customerId is null) return (IReadOnlyList<SubscriptionDto>)Array.Empty<SubscriptionDto>();
            var subs = await _client.Customers.ListCustomerSubscriptions(
                new ListCustomerSubscriptionsRequest { CustomerId = customerId.Value }, cancellationToken: ct);
            return (IReadOnlyList<SubscriptionDto>)subs
                .Where(s => s.Subscription is not null && IsOwner(s.Subscription, user))
                .Select(s => ToSubscription(s.Subscription!)).ToList();
        });
    }

    public async Task<SubscriptionDto> SubscribeAsync(string user, string? email, string planHandle, CancellationToken ct)
    {
        return await Call(async () =>
        {
            var plan = await ReadActivePlanAsync(planHandle, ct);
            var customerId = await FindCustomerIdAsync(user, ct) ?? await CreateCustomerAsync(user, email, ct);

            // Remittance collection: the shopper is invoiced, so no card details are needed.
            var created = await _client.Subscriptions.CreateSubscription(new CreateSubscriptionOperationRequest
            {
                Body = new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = plan.Handle,
                        CustomerId = customerId,
                        PaymentCollectionMethod = CollectionMethod.Remittance
                    }
                }
            }, cancellationToken: ct);
            return ToSubscription(created.Subscription!);
        });
    }

    public async Task<SubscriptionDto> GetSubscriptionAsync(string user, int subscriptionId, CancellationToken ct)
    {
        return await Call(async () => ToSubscription(await ReadOwnedAsync(user, subscriptionId, ct)));
    }

    public async Task<SubscriptionDto> PauseAsync(string user, int subscriptionId, CancellationToken ct)
    {
        return await Call(async () =>
        {
            var sub = await ReadOwnedAsync(user, subscriptionId, ct);
            if (sub.State == SubscriptionState.OnHold || sub.State == SubscriptionState.Paused)
                throw BillingException.Conflict("The subscription is already paused.");
            if (sub.State == SubscriptionState.Canceled || sub.State == SubscriptionState.Expired)
                throw BillingException.Conflict("A canceled or expired subscription cannot be paused.");
            var result = await _client.SubscriptionStatus.PauseSubscription(
                new PauseSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct);
            return ToSubscription(result.Subscription!);
        });
    }

    public async Task<SubscriptionDto> ResumeAsync(string user, int subscriptionId, CancellationToken ct)
    {
        return await Call(async () =>
        {
            var sub = await ReadOwnedAsync(user, subscriptionId, ct);
            if (sub.State != SubscriptionState.OnHold && sub.State != SubscriptionState.Paused)
                throw BillingException.Conflict("Only a paused subscription can be resumed.");
            var result = await _client.SubscriptionStatus.ResumeSubscription(
                new ResumeSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct);
            return ToSubscription(result.Subscription!);
        });
    }

    public async Task<SubscriptionDto> CancelAsync(string user, int subscriptionId, CancellationToken ct)
    {
        return await Call(async () =>
        {
            var sub = await ReadOwnedAsync(user, subscriptionId, ct);
            if (sub.State == SubscriptionState.Canceled)
                throw BillingException.Conflict("The subscription is already canceled.");
            if (sub.State == SubscriptionState.Expired)
                throw BillingException.Conflict("The subscription has expired and cannot be canceled.");
            var result = await _client.SubscriptionStatus.CancelSubscription(new CancelSubscriptionRequest
            {
                SubscriptionId = subscriptionId,
                Body = new CancellationRequest { Subscription = new CancellationOptions { CancellationMessage = "Canceled by the shopper" } }
            }, cancellationToken: ct);
            return ToSubscription(result.Subscription!);
        });
    }

    public async Task<SubscriptionDto> RestartAsync(string user, int subscriptionId, CancellationToken ct)
    {
        return await Call(async () =>
        {
            var sub = await ReadOwnedAsync(user, subscriptionId, ct);
            if (sub.State != SubscriptionState.Canceled)
                throw BillingException.Conflict("Only a canceled subscription can be restarted.");
            var result = await _client.SubscriptionStatus.ReactivateSubscription(
                new ReactivateSubscriptionOperationRequest { SubscriptionId = subscriptionId }, cancellationToken: ct);
            return ToSubscription(result.Subscription!);
        });
    }

    public async Task<SubscriptionDto> SwitchPlanAsync(string user, int subscriptionId, string planHandle, CancellationToken ct)
    {
        return await Call(async () =>
        {
            var sub = await ReadOwnedAsync(user, subscriptionId, ct);
            var plan = await ReadActivePlanAsync(planHandle, ct);
            if (sub.Product?.Id == plan.Id)
                throw BillingException.Conflict("The subscription is already on that plan.");

            // Maxio only migrates live subscriptions. A canceled, paused or otherwise inactive one has its
            // product changed in place, so it ends up on the new plan without being reactivated or charged.
            var migratable = sub.State == SubscriptionState.Active || sub.State == SubscriptionState.Trialing
                || sub.State == SubscriptionState.PastDue || sub.State == SubscriptionState.SoftFailure
                || sub.State == SubscriptionState.Assessing || sub.State == SubscriptionState.TrialEnded
                || sub.State == SubscriptionState.Unpaid;

            SubscriptionResponse result;
            if (migratable)
            {
                result = await _client.SubscriptionProducts.MigrateSubscriptionProduct(new MigrateSubscriptionProductRequest
                {
                    SubscriptionId = subscriptionId,
                    Body = new SubscriptionProductMigrationRequest
                    {
                        Migration = new SubscriptionProductMigration { ProductHandle = plan.Handle }
                    }
                }, cancellationToken: ct);
            }
            else
            {
                result = await _client.Subscriptions.UpdateSubscription(new UpdateSubscriptionOperationRequest
                {
                    SubscriptionId = subscriptionId,
                    Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { ProductHandle = plan.Handle } }
                }, cancellationToken: ct);
            }

            var updated = result.Subscription!;
            if (updated.Product?.Id != plan.Id)
                throw BillingException.Conflict("The billing system did not move the subscription to the new plan.");
            return ToSubscription(updated);
        });
    }

    public async Task<DailyUsageDto> GetUsageAsync(string user, int subscriptionId, DateOnly date, CancellationToken ct)
    {
        return await Call(async () =>
        {
            await ReadOwnedAsync(user, subscriptionId, ct);

            var components = await _client.SubscriptionComponents.ListSubscriptionComponents(
                new ListSubscriptionComponentsRequest { SubscriptionId = subscriptionId }, cancellationToken: ct);

            var result = new List<ComponentUsageDto>();
            foreach (var sc in components.Select(c => c.Component).Where(c => c?.ComponentId is not null))
            {
                if (sc!.Kind != ComponentKind.MeteredComponent && sc.Kind != ComponentKind.PrepaidUsageComponent) continue;
                var entries = new List<UsageEntryDto>();

                // Maxio treats since_date/until_date as midnights in its own time zone and the day edges are
                // easy to get wrong, so ask for a window around the day and keep only usage recorded on the
                // calendar day (in the site's time zone, as stamped on each usage).
                var from = new DateTimeOffset(date.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                var until = new DateTimeOffset(date.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                for (var page = 1; ; page++)
                {
                    var batch = await _client.SubscriptionComponents.ListUsages(new ListUsagesRequest
                    {
                        SubscriptionIdOrReference = subscriptionId,
                        ComponentId = sc.ComponentId!.Value,
                        SinceDate = from,
                        UntilDate = until,
                        Page = page,
                        PerPage = PAGE_SIZE
                    }, cancellationToken: ct);
                    foreach (var u in batch.Select(b => b.Usage))
                    {
                        if (u.CreatedAt is null || DateOnly.FromDateTime(u.CreatedAt.Value.DateTime) != date) continue;
                        entries.Add(new UsageEntryDto(u.Id, ParseQuantity(u.Quantity), u.Memo, u.CreatedAt));
                    }
                    if (batch.Count < PAGE_SIZE) break;
                }
                result.Add(new ComponentUsageDto(sc.ComponentId!.Value, sc.Name, sc.UnitName, entries.Sum(e => e.Quantity), entries));
            }
            return new DailyUsageDto(subscriptionId, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), result);
        });
    }

    // ------------------------------------------------------------------ staff

    public async Task<PlanDto> CreatePlanAsync(string name, decimal monthlyPrice, string brandName, string? handle, string? description, CancellationToken ct)
    {
        return await Call(async () =>
        {
            var cents = monthlyPrice * 100m;
            if (cents != decimal.Truncate(cents) || cents > int.MaxValue)
                throw BillingException.BadRequest("The monthly price must be a valid amount with at most two decimal places.");

            var planHandle = string.IsNullOrWhiteSpace(handle) ? Slug(name) : handle.Trim();
            if (planHandle.Length == 0)
                throw BillingException.BadRequest("A plan handle could not be derived from the name; provide a handle.");
            if (await TryReadProductByHandleAsync(planHandle, ct) is not null)
                throw BillingException.Conflict($"A plan with handle '{planHandle}' already exists.");

            var familyId = await FindOrCreateBrandFamilyAsync(brandName, ct);
            var created = await _client.Products.CreateProduct(new CreateProductRequest
            {
                ProductFamilyId = familyId.ToString(CultureInfo.InvariantCulture),
                Body = new CreateOrUpdateProductRequest
                {
                    Product = new CreateOrUpdateProduct
                    {
                        Name = name.Trim(),
                        Handle = planHandle,
                        Description = description ?? string.Empty,
                        PriceInCents = (long)cents,
                        Interval = 1,
                        IntervalUnit = IntervalUnit.Month,
                        RequireCreditCard = false
                    }
                }
            }, cancellationToken: ct);
            return ToPlan(created.Product);
        });
    }

    public async Task<CurrencyPriceDto> AddCurrencyPriceAsync(int planId, string currency, decimal price, CancellationToken ct)
    {
        return await Call(async () =>
        {
            if (price != decimal.Truncate(price) || price > int.MaxValue)
                throw BillingException.BadRequest("Currency prices must be whole amounts of the currency.");

            var product = (await _client.Products.ReadProduct(new ReadProductRequest { ProductId = planId }, cancellationToken: ct)).Product;
            if (product.ArchivedAt is not null)
                throw BillingException.Conflict("The plan is archived.");
            var pricePointId = product.DefaultProductPricePointId ?? product.ProductPricePointId
                ?? throw BillingException.Conflict("The plan has no price point to price.");

            var site = (await _client.Sites.ReadSite(cancellationToken: ct)).Site;
            if (string.Equals(site.Currency, currency, StringComparison.OrdinalIgnoreCase))
                throw BillingException.BadRequest($"{currency} is the shop's base currency; the plan's monthly price is already in it.");
            if (site.NonPrimaryCurrencies is null || !site.NonPrimaryCurrencies.Contains(currency, StringComparer.OrdinalIgnoreCase))
                throw BillingException.Unprocessable($"{currency} is not enabled in the billing system; enable it there before pricing plans in it.");

            var result = await _client.ProductPricePoints.CreateProductCurrencyPrices(new CreateProductCurrencyPricesOperationRequest
            {
                ProductPricePointId = pricePointId,
                Body = new CreateProductCurrencyPricesRequest
                {
                    CurrencyPrices = new[]
                    {
                        new CreateProductCurrencyPrice { Currency = currency, Price = (int)price, Role = CurrencyPriceRole.Baseline }
                    }
                }
            }, cancellationToken: ct);

            var created = result.CurrencyPrices.FirstOrDefault(c => string.Equals(c.Currency, currency, StringComparison.OrdinalIgnoreCase));
            return new CurrencyPriceDto(planId, currency, (decimal)(created?.Price ?? (double)price));
        });
    }

    public async Task<SubscriptionDto> SetNextBillingDateAsync(int subscriptionId, DateOnly date, CancellationToken ct)
    {
        return await Call(async () =>
        {
            var sub = (await _client.Subscriptions.ReadSubscription(
                new ReadSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct)).Subscription!;
            if (sub.State == SubscriptionState.Canceled || sub.State == SubscriptionState.Expired)
                throw BillingException.Conflict("A canceled or expired subscription is not billed.");
            if (sub.State == SubscriptionState.OnHold || sub.State == SubscriptionState.Paused)
                throw BillingException.Conflict("A paused subscription is not billed; resume it first.");

            // Keep the time of day (and the site's UTC offset) of the current billing moment, so the
            // subscriber is billed on the requested calendar date in the site's time zone.
            var reference = sub.NextAssessmentAt ?? sub.CurrentPeriodEndsAt ?? new DateTimeOffset(DateTime.UtcNow, TimeSpan.Zero);
            var target = new DateTimeOffset(date.ToDateTime(TimeOnly.FromDateTime(reference.DateTime)), reference.Offset);
            if (target <= DateTimeOffset.UtcNow)
                throw BillingException.BadRequest("The next billing date must be in the future.");

            var result = await _client.Subscriptions.UpdateSubscription(new UpdateSubscriptionOperationRequest
            {
                SubscriptionId = subscriptionId,
                Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { NextBillingAt = target } }
            }, cancellationToken: ct);
            return ToSubscription(result.Subscription!);
        });
    }

    // ---------------------------------------------------------------- helpers

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

    private static bool IsOwner(Subscription sub, string user) =>
        string.Equals(sub.Customer?.Reference, user, StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads a subscription and makes sure it belongs to the caller; someone else's looks like a missing one.</summary>
    private async Task<Subscription> ReadOwnedAsync(string user, int subscriptionId, CancellationToken ct)
    {
        Subscription? sub;
        try
        {
            sub = (await _client.Subscriptions.ReadSubscription(
                new ReadSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct)).Subscription;
        }
        catch (ApiException ex) when ((int)ex.StatusCode == 404)
        {
            throw BillingException.NotFound("Subscription not found.");
        }
        if (sub is null || !IsOwner(sub, user))
            throw BillingException.NotFound("Subscription not found.");
        return sub;
    }

    private async Task<int?> FindCustomerIdAsync(string user, CancellationToken ct)
    {
        try
        {
            var customer = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = user }, cancellationToken: ct);
            return customer.Customer.Id;
        }
        catch (ApiException ex) when ((int)ex.StatusCode == 404)
        {
            return null;
        }
    }

    private async Task<int> CreateCustomerAsync(string user, string? email, CancellationToken ct)
    {
        var address = string.IsNullOrWhiteSpace(email) ? user : email;
        var local = user.Contains('@') ? user.Substring(0, user.IndexOf('@')) : user;
        var created = await _client.Customers.CreateCustomer(new CreateCustomerOperationRequest
        {
            Body = new CreateCustomerRequest
            {
                Customer = new CreateCustomer { FirstName = local, LastName = "Shopper", Email = address, Reference = user }
            }
        }, cancellationToken: ct);
        return created.Customer.Id ?? throw new BillingException(HttpStatusCode.BadGateway, "The billing system returned no customer id.");
    }

    private async Task<Product?> TryReadProductByHandleAsync(string handle, CancellationToken ct)
    {
        try
        {
            return (await _client.Products.ReadProductByHandle(new ReadProductByHandleRequest { ApiHandle = handle }, cancellationToken: ct)).Product;
        }
        catch (ApiException ex) when ((int)ex.StatusCode == 404)
        {
            return null;
        }
    }

    private async Task<Product> ReadActivePlanAsync(string handle, CancellationToken ct)
    {
        var plan = await TryReadProductByHandleAsync(handle, ct);
        if (plan is null) throw BillingException.NotFound($"Plan '{handle}' not found.");
        if (plan.ArchivedAt is not null) throw BillingException.Conflict($"Plan '{handle}' is no longer available.");
        return plan;
    }

    private async Task<int> FindOrCreateBrandFamilyAsync(string brandName, CancellationToken ct)
    {
        async Task<int?> Find()
        {
            var families = await _client.ProductFamilies.ListProductFamilies(new ListProductFamiliesRequest(), cancellationToken: ct);
            return families.Select(f => f.ProductFamily)
                .FirstOrDefault(f => f?.ArchivedAt is null && string.Equals(f?.Name, brandName, StringComparison.OrdinalIgnoreCase))?.Id;
        }

        var existing = await Find();
        if (existing is not null) return existing.Value;
        try
        {
            var created = await _client.ProductFamilies.CreateProductFamily(new CreateProductFamilyOperationRequest
            {
                Body = new CreateProductFamilyRequest
                {
                    ProductFamily = new CreateProductFamily { Name = brandName, Handle = "brand-" + Slug(brandName), Description = "eShopOnWeb catalog brand" }
                }
            }, cancellationToken: ct);
            return created.ProductFamily?.Id ?? throw new BillingException(HttpStatusCode.BadGateway, "The billing system returned no product family.");
        }
        catch (ApiException)
        {
            // Lost a race with another request creating the same family.
            return await Find() ?? throw new BillingException(HttpStatusCode.BadGateway, "The brand could not be created in the billing system.");
        }
    }

    private static decimal ParseQuantity(MaxioAdvancedBilling.Models.AnyOf.Quantity1? q)
    {
        if (q is null) return 0m;
        if (q.TryGetInt(out var i)) return i;
        if (q.TryGetString(out var s) && decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
        return 0m;
    }

    internal static string Slug(string value)
    {
        var sb = new StringBuilder();
        foreach (var ch in value.Trim().ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(ch) && ch < 128 ? ch : '-');
        return Regex.Replace(sb.ToString(), "-+", "-").Trim('-');
    }

    private static PlanDto ToPlan(Product p) =>
        new(p.Id ?? 0, p.Name ?? string.Empty, p.Handle ?? string.Empty, p.Description,
            (p.PriceInCents ?? 0) / 100m, p.PriceInCents ?? 0, p.Interval, p.IntervalUnit?.Value,
            p.ProductFamily?.Name, p.ArchivedAt is not null);

    private static SubscriptionDto ToSubscription(Subscription s) =>
        new(s.Id ?? 0, s.State?.Value, (s.BalanceInCents ?? 0) <= 0, s.BalanceInCents ?? 0,
            s.Product is null ? null : ToPlan(s.Product), s.NextAssessmentAt, s.CanceledAt, s.CreatedAt, s.Currency);
}
