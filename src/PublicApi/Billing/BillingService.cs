using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
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
using Microsoft.Extensions.Logging;
using SdkProduct = MaxioAdvancedBilling.Models.Product;
using SdkSubscription = MaxioAdvancedBilling.Models.Subscription;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public interface IBillingService
{
    Task<IReadOnlyList<PlanDto>> ListPlansAsync(CancellationToken ct);
    Task<SubscriptionDto> SubscribeAsync(string user, string planHandle, CancellationToken ct);
    Task<SubscriptionDto> GetSubscriptionAsync(string user, int subscriptionId, CancellationToken ct);
    Task<SubscriptionDto> PauseAsync(string user, int subscriptionId, CancellationToken ct);
    Task<SubscriptionDto> ResumeAsync(string user, int subscriptionId, CancellationToken ct);
    Task<SubscriptionDto> CancelAsync(string user, int subscriptionId, CancellationToken ct);
    Task<SubscriptionDto> RestartAsync(string user, int subscriptionId, CancellationToken ct);
    Task<SubscriptionDto> SwitchPlanAsync(string user, int subscriptionId, string planHandle, CancellationToken ct);
    Task<UsageDto> GetUsageAsync(string user, int subscriptionId, DateOnly date, CancellationToken ct);
    Task<PlanDto> CreatePlanAsync(string name, decimal monthlyPrice, string brandName, CancellationToken ct);
    Task<CurrencyPriceDto> AddCurrencyPriceAsync(int planId, string currency, decimal price, CancellationToken ct);
    Task<NextBillingDateDto> SetNextBillingDateAsync(int subscriptionId, DateOnly date, CancellationToken ct);
}

/// <summary>
/// Subscription billing, with Maxio as the system of record. Every call goes to Maxio through the SDK;
/// nothing is cached or stubbed here.
/// </summary>
public class BillingService : IBillingService
{
    private const int PageSize = 200;
    private const string BillingCurrency = "USD";

    // In-process claims: one writer per shopper (subscribe) and one for plan/brand creation.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ShopperLocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim PlanCreationLock = new(1, 1);

    private readonly IMaxioClientProvider _clients;
    private readonly ILogger<BillingService> _logger;

    public BillingService(IMaxioClientProvider clients, ILogger<BillingService> logger)
    {
        _clients = clients;
        _logger = logger;
    }

    private MaxioAdvancedBillingClient Client => _clients.GetClient();

    // ---------------------------------------------------------------- plans

    public Task<IReadOnlyList<PlanDto>> ListPlansAsync(CancellationToken ct) =>
        Guard(async () => (IReadOnlyList<PlanDto>)(await ListOfferedProductsAsync(ct)).Select(ToPlan).ToList());

    /// <summary>The plans this application offers: every non-archived Maxio product.</summary>
    private async Task<List<SdkProduct>> ListOfferedProductsAsync(CancellationToken ct)
    {
        var all = new List<SdkProduct>();
        for (var page = 1; ; page++)
        {
            var batch = await Client.Products.ListProducts(new ListProductsRequest { Page = page, PerPage = PageSize, IncludeArchived = false }, cancellationToken: ct);
            // A plan is subscribed to by its handle, so a product without one is not on offer.
            all.AddRange(batch.Select(p => p.Product).Where(p => p.ArchivedAt == null && !string.IsNullOrEmpty(p.Handle)));
            if (batch.Count < PageSize) break;
        }
        return all;
    }

    private async Task<SdkProduct> RequireOfferedPlanAsync(string handle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(handle)) throw BillingException.BadRequest("A plan handle is required.");
        var plan = (await ListOfferedProductsAsync(ct)).FirstOrDefault(p => string.Equals(p.Handle, handle.Trim(), StringComparison.Ordinal));
        return plan ?? throw BillingException.NotFound($"Plan '{handle}' is not one of the plans on offer.");
    }

    private static PlanDto ToPlan(SdkProduct p) => new(
        p.Id ?? 0,
        p.Handle ?? string.Empty,
        p.Name ?? string.Empty,
        p.Description,
        p.ProductFamily?.Name,
        (p.PriceInCents ?? 0) / 100m,
        BillingCurrency,
        p.Interval ?? 1,
        p.IntervalUnit?.Value ?? "month");

    // ---------------------------------------------------------------- subscribe

    public Task<SubscriptionDto> SubscribeAsync(string user, string planHandle, CancellationToken ct) => Guard(async () =>
    {
        var plan = await RequireOfferedPlanAsync(planHandle, ct);

        var gate = ShopperLocks.GetOrAdd(user, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var customerId = await EnsureCustomerAsync(user, ct);

            // A shopper may hold several plans, but not the same plan twice at once.
            var existing = await ListCustomerSubscriptionsAsync(customerId, ct);
            if (existing.Any(s => s.Product?.Handle == plan.Handle && IsLive(s)))
            {
                throw BillingException.Conflict($"You already have a subscription to plan '{plan.Handle}'.");
            }

            var startedAt = DateTimeOffset.UtcNow.AddSeconds(-5);
            try
            {
                var created = await Client.Subscriptions.CreateSubscription(new CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = plan.Handle,
                            CustomerId = customerId,
                            // No card details: the shopper is invoiced.
                            PaymentCollectionMethod = CollectionMethod.Remittance
                        }
                    }
                }, cancellationToken: ct);
                return ToSubscription(created.Subscription!);
            }
            catch (SdkConnectionException ex)
            {
                // The write may have reached Maxio: settle the outcome by looking for it.
                _logger.LogWarning(ex, "Subscribe outcome unknown for {User}/{Plan}; reconciling.", user, plan.Handle);
                var found = (await ListCustomerSubscriptionsAsync(customerId, ct))
                    .FirstOrDefault(s => s.Product?.Handle == plan.Handle && IsLive(s) && s.CreatedAt >= startedAt);
                if (found != null) return ToSubscription(found);
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    });

    private static bool IsLive(SdkSubscription s) =>
        s.State != SubscriptionState.Canceled && s.State != SubscriptionState.Expired && s.State != SubscriptionState.FailedToCreate;

    private async Task<int> EnsureCustomerAsync(string user, CancellationToken ct)
    {
        try
        {
            var found = await Client.Customers.ReadCustomerByReference(new ReadCustomerByReferenceRequest { Reference = user }, cancellationToken: ct);
            if (found.Customer.Id is int id) return id;
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // first subscription of this shopper
        }

        var local = user.Contains('@') ? user[..user.IndexOf('@')] : user;
        var email = user.Contains('@') ? user : $"{user}@example.invalid";
        var created = await Client.Customers.CreateCustomer(new CreateCustomerOperationRequest
        {
            Body = new CreateCustomerRequest
            {
                Customer = new CreateCustomer { FirstName = local, LastName = "Shopper", Email = email, Reference = user }
            }
        }, cancellationToken: ct);
        return created.Customer.Id ?? throw new BillingException(502, "Maxio created the customer without an id.");
    }

    private async Task<List<SdkSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        var all = new List<SdkSubscription>();
        for (var page = 1; ; page++)
        {
            var batch = await Client.Subscriptions.ListSubscriptions(new ListSubscriptionsRequest { CustomerId = customerId, Page = page, PerPage = PageSize }, cancellationToken: ct);
            foreach (var item in batch)
            {
                if (item.Subscription != null) all.Add(item.Subscription);
            }
            if (batch.Count < PageSize) break;
        }
        return all;
    }

    // ---------------------------------------------------------------- read / ownership

    public Task<SubscriptionDto> GetSubscriptionAsync(string user, int subscriptionId, CancellationToken ct) =>
        Guard(async () => ToSubscription(await LoadOwnedAsync(user, subscriptionId, ct)));

    private async Task<SdkSubscription> LoadAsync(int subscriptionId, CancellationToken ct)
    {
        try
        {
            var response = await Client.Subscriptions.ReadSubscription(new ReadSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct);
            return response.Subscription ?? throw BillingException.NotFound("Subscription not found.");
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw BillingException.NotFound("Subscription not found.");
        }
    }

    /// <summary>Loads the subscription; one that is not the caller's looks exactly like one that does not exist.</summary>
    private async Task<SdkSubscription> LoadOwnedAsync(string user, int subscriptionId, CancellationToken ct)
    {
        var sub = await LoadAsync(subscriptionId, ct);
        if (!string.Equals(sub.Customer?.Reference, user, StringComparison.OrdinalIgnoreCase))
        {
            throw BillingException.NotFound("Subscription not found.");
        }
        return sub;
    }

    private static SubscriptionDto ToSubscription(SdkSubscription s)
    {
        var balance = s.BalanceInCents ?? 0;
        return new SubscriptionDto(
            s.Id ?? 0,
            s.State?.Value ?? "unknown",
            s.Product?.Handle,
            s.Product?.Name,
            s.Product?.Id,
            Paid: balance <= 0,
            BalanceDue: Math.Max(balance, 0) / 100m,
            s.CurrentPeriodEndsAt,
            s.CanceledAt);
    }

    // ---------------------------------------------------------------- status changes

    public Task<SubscriptionDto> PauseAsync(string user, int id, CancellationToken ct) => Guard(async () =>
    {
        var sub = await LoadOwnedAsync(user, id, ct);
        if (sub.State == SubscriptionState.OnHold) throw BillingException.Conflict("The subscription is already paused.");
        if (sub.State != SubscriptionState.Active && sub.State != SubscriptionState.Trialing)
            throw BillingException.Conflict($"A subscription in state '{sub.State?.Value}' cannot be paused.");
        return await MutateAsync(id, s => s.State == SubscriptionState.OnHold, async () =>
            (await Client.SubscriptionStatus.PauseSubscription(new PauseSubscriptionRequest { SubscriptionId = id }, cancellationToken: ct)).Subscription, ct);
    });

    public Task<SubscriptionDto> ResumeAsync(string user, int id, CancellationToken ct) => Guard(async () =>
    {
        var sub = await LoadOwnedAsync(user, id, ct);
        if (sub.State != SubscriptionState.OnHold) throw BillingException.Conflict("Only a paused subscription can be resumed.");
        return await MutateAsync(id, s => s.State != SubscriptionState.OnHold, async () =>
            (await Client.SubscriptionStatus.ResumeSubscription(new ResumeSubscriptionRequest { SubscriptionId = id }, cancellationToken: ct)).Subscription, ct);
    });

    public Task<SubscriptionDto> CancelAsync(string user, int id, CancellationToken ct) => Guard(async () =>
    {
        var sub = await LoadOwnedAsync(user, id, ct);
        if (sub.State == SubscriptionState.Canceled) throw BillingException.Conflict("The subscription is already canceled.");
        return await MutateAsync(id, s => s.State == SubscriptionState.Canceled, async () =>
            (await Client.SubscriptionStatus.CancelSubscription(new CancelSubscriptionRequest
            {
                SubscriptionId = id,
                Body = new CancellationRequest { Subscription = new CancellationOptions { CancellationMessage = "Canceled by the subscriber." } }
            }, cancellationToken: ct)).Subscription, ct);
    });

    public Task<SubscriptionDto> RestartAsync(string user, int id, CancellationToken ct) => Guard(async () =>
    {
        var sub = await LoadOwnedAsync(user, id, ct);
        if (sub.State != SubscriptionState.Canceled) throw BillingException.Conflict("Only a canceled subscription can be restarted.");
        return await MutateAsync(id, s => s.State != SubscriptionState.Canceled, () => ReactivateAsync(id, ct), ct);
    });

    private async Task<SdkSubscription?> ReactivateAsync(int id, CancellationToken ct) =>
        (await Client.SubscriptionStatus.ReactivateSubscription(new ReactivateSubscriptionOperationRequest { SubscriptionId = id }, cancellationToken: ct)).Subscription;

    /// <summary>
    /// Runs a state-changing call. If the connection fails after Maxio may have acted, the subscription is
    /// re-read and the call counts as done when it reached the wanted state.
    /// </summary>
    private async Task<SubscriptionDto> MutateAsync(int id, Func<SdkSubscription, bool> reached, Func<Task<SdkSubscription?>> call, CancellationToken ct)
    {
        try
        {
            var result = await call() ?? throw new BillingException(502, "Maxio returned no subscription.");
            return ToSubscription(result);
        }
        catch (SdkConnectionException ex)
        {
            _logger.LogWarning(ex, "Outcome unknown for a change on subscription {Id}; re-reading.", id);
            var current = await LoadAsync(id, ct);
            if (reached(current)) return ToSubscription(current);
            throw;
        }
    }

    // ---------------------------------------------------------------- switch plan

    public Task<SubscriptionDto> SwitchPlanAsync(string user, int id, string planHandle, CancellationToken ct) => Guard(async () =>
    {
        var sub = await LoadOwnedAsync(user, id, ct);
        var plan = await RequireOfferedPlanAsync(planHandle, ct);
        if (sub.Product?.Handle == plan.Handle) throw BillingException.Conflict($"The subscription is already on plan '{plan.Handle}'.");

        if (sub.State == SubscriptionState.Active || sub.State == SubscriptionState.Trialing)
        {
            return await MutateAsync(id, s => s.Product?.Handle == plan.Handle, async () =>
                (await Client.SubscriptionProducts.MigrateSubscriptionProduct(new MigrateSubscriptionProductRequest
                {
                    SubscriptionId = id,
                    Body = new SubscriptionProductMigrationRequest { Migration = new SubscriptionProductMigration { ProductHandle = plan.Handle } }
                }, cancellationToken: ct)).Subscription, ct);
        }

        // Maxio migrates only active/trialing subscriptions; for the others the product is changed in place.
        var previousHandle = sub.Product?.Handle;
        var changed = await MutateAsync(id, s => s.Product?.Handle == plan.Handle, () => ChangeProductAsync(id, plan.Handle!, ct), ct);

        if (sub.State == SubscriptionState.Canceled)
        {
            // Switching a canceled subscription puts the shopper on the new plan, so it is brought back to life.
            try
            {
                return await MutateAsync(id, s => s.State != SubscriptionState.Canceled, () => ReactivateAsync(id, ct), ct);
            }
            catch (Exception) when (previousHandle != null)
            {
                try { await ChangeProductAsync(id, previousHandle, ct); }
                catch (Exception undo) { _logger.LogError(undo, "Could not undo the plan change on subscription {Id}.", id); }
                throw;
            }
        }
        return changed;
    });

    private async Task<SdkSubscription?> ChangeProductAsync(int id, string handle, CancellationToken ct) =>
        (await Client.Subscriptions.UpdateSubscription(new UpdateSubscriptionOperationRequest
        {
            SubscriptionId = id,
            Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { ProductHandle = handle } }
        }, cancellationToken: ct)).Subscription;

    // ---------------------------------------------------------------- usage

    public Task<UsageDto> GetUsageAsync(string user, int id, DateOnly date, CancellationToken ct) => Guard(async () =>
    {
        await LoadOwnedAsync(user, id, ct);

        var components = (await Client.SubscriptionComponents.ListSubscriptionComponents(new ListSubscriptionComponentsRequest { SubscriptionId = id }, cancellationToken: ct))
            .Select(c => c.Component)
            .Where(c => c != null && c.Kind == ComponentKind.MeteredComponent && c.ComponentId != null)
            .Select(c => c!)
            .ToList();

        // Maxio's date filters are midnights; read a wider window and keep only the picked (site-local) day.
        var since = new DateTimeOffset(date.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var until = new DateTimeOffset(date.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var lines = new List<UsageComponentDto>();
        foreach (var component in components)
        {
            var componentId = component.ComponentId!.Value;
            decimal total = 0;
            for (var page = 1; ; page++)
            {
                var batch = await Client.SubscriptionComponents.ListUsages(new ListUsagesRequest
                {
                    SubscriptionIdOrReference = id,
                    ComponentId = componentId,
                    SinceDate = since,
                    UntilDate = until,
                    Page = page,
                    PerPage = PageSize
                }, cancellationToken: ct);
                foreach (var item in batch)
                {
                    // CreatedAt keeps the site's offset, so .Date is the site-local day.
                    if (item.Usage.CreatedAt is { } at && DateOnly.FromDateTime(at.Date) == date) total += ToDecimal(item.Usage.Quantity);
                }
                if (batch.Count < PageSize) break;
            }
            lines.Add(new UsageComponentDto(componentId, component.Name, component.UnitName, total));
        }
        return new UsageDto(id, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), lines.Sum(l => l.Quantity), lines);
    });

    private static decimal ToDecimal(Quantity1? q)
    {
        if (q == null) return 0;
        if (q.TryGetInt(out var i)) return i;
        if (q.TryGetString(out var s) && decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) return d;
        return 0;
    }

    // ---------------------------------------------------------------- staff: plans

    public Task<PlanDto> CreatePlanAsync(string name, decimal monthlyPrice, string brandName, CancellationToken ct) => Guard(async () =>
    {
        if (string.IsNullOrWhiteSpace(name)) throw BillingException.BadRequest("A plan name is required.");
        var cents = ToCents(monthlyPrice);

        await PlanCreationLock.WaitAsync(ct);
        try
        {
            var familyId = await EnsureBrandFamilyAsync(brandName, ct);
            var created = await Client.Products.CreateProduct(new CreateProductRequest
            {
                ProductFamilyId = familyId.ToString(CultureInfo.InvariantCulture),
                Body = new CreateOrUpdateProductRequest
                {
                    Product = new CreateOrUpdateProduct
                    {
                        Name = name.Trim(),
                        Handle = Slug(name),
                        Description = $"{name.Trim()} ({brandName})",
                        PriceInCents = cents,
                        Interval = 1,
                        IntervalUnit = IntervalUnit.Month,
                        // Shoppers subscribe without card details.
                        RequireCreditCard = false
                    }
                }
            }, cancellationToken: ct);
            return ToPlan(created.Product);
        }
        finally
        {
            PlanCreationLock.Release();
        }
    });

    private static long ToCents(decimal price)
    {
        if (price < 0) throw BillingException.BadRequest("The price cannot be negative.");
        var cents = price * 100m;
        if (cents != decimal.Truncate(cents)) throw BillingException.BadRequest("The price can have at most two decimal places.");
        return (long)cents;
    }

    /// <summary>A brand is a Maxio product family of the same name; it is created the first time a plan uses the brand.</summary>
    private async Task<int> EnsureBrandFamilyAsync(string brandName, CancellationToken ct)
    {
        var families = await Client.ProductFamilies.ListProductFamilies(new ListProductFamiliesRequest(), cancellationToken: ct);
        var match = families.Select(f => f.ProductFamily).FirstOrDefault(f => f != null && f.ArchivedAt == null &&
            string.Equals(f.Name, brandName, StringComparison.OrdinalIgnoreCase));
        if (match?.Id is int existing) return existing;

        var created = await Client.ProductFamilies.CreateProductFamily(new CreateProductFamilyOperationRequest
        {
            Body = new CreateProductFamilyRequest
            {
                ProductFamily = new CreateProductFamily { Name = brandName, Handle = Slug(brandName) }
            }
        }, cancellationToken: ct);
        return created.ProductFamily?.Id ?? throw new BillingException(502, "Maxio created the product family without an id.");
    }

    private static string Slug(string value)
    {
        var slug = Regex.Replace(value.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return slug.Length == 0 ? "brand" : slug;
    }

    public Task<CurrencyPriceDto> AddCurrencyPriceAsync(int planId, string currency, decimal price, CancellationToken ct) => Guard(async () =>
    {
        if (!Regex.IsMatch(currency ?? string.Empty, "^[A-Za-z]{3}$")) throw BillingException.BadRequest("The currency must be a three-letter ISO code.");
        if (price < 0) throw BillingException.BadRequest("The price cannot be negative.");
        // The SDK types a currency price as a whole number.
        if (price != decimal.Truncate(price)) throw BillingException.BadRequest("The price must be a whole number of currency units.");

        var plan = (await ListOfferedProductsAsync(ct)).FirstOrDefault(p => p.Id == planId)
            ?? throw BillingException.NotFound($"Plan {planId} is not one of the plans on offer.");
        if (plan.TrialPriceInCents != null || plan.InitialChargeInCents != null)
            throw BillingException.Conflict("This plan has a trial price or setup fee, which also needs prices in the new currency; that is not supported here.");
        var pricePointId = plan.DefaultProductPricePointId ?? plan.ProductPricePointId
            ?? throw new BillingException(502, "Maxio returned the plan without a price point.");

        var code = currency!.ToUpperInvariant();
        await Client.ProductPricePoints.CreateProductCurrencyPrices(new CreateProductCurrencyPricesOperationRequest
        {
            ProductPricePointId = pricePointId,
            Body = new CreateProductCurrencyPricesRequest
            {
                CurrencyPrices = new[] { new CreateProductCurrencyPrice { Currency = code, Price = (int)price, Role = CurrencyPriceRole.Baseline } }
            }
        }, cancellationToken: ct);
        return new CurrencyPriceDto(planId, code, price);
    });

    // ---------------------------------------------------------------- staff: next billing date

    public Task<NextBillingDateDto> SetNextBillingDateAsync(int id, DateOnly date, CancellationToken ct) => Guard(async () =>
    {
        var sub = await LoadAsync(id, ct);
        if (sub.State != SubscriptionState.Active && sub.State != SubscriptionState.Trialing)
            throw BillingException.Conflict($"A subscription in state '{sub.State?.Value}' is not being billed, so it has no next billing date to set.");

        // Maxio takes noon of the site's day for a date; express the date at noon in the site's own offset.
        var offset = sub.CurrentPeriodEndsAt?.Offset ?? TimeSpan.Zero;
        var at = new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), offset);

        var updated = await Client.Subscriptions.UpdateSubscription(new UpdateSubscriptionOperationRequest
        {
            SubscriptionId = id,
            Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { NextBillingAt = at } }
        }, cancellationToken: ct);

        // The response carries no next_billing_at; the new date shows as current_period_ends_at.
        var confirmed = updated.Subscription?.CurrentPeriodEndsAt;
        if (confirmed == null || DateOnly.FromDateTime(confirmed.Value.Date) != date)
            throw new BillingException(502, "Maxio did not confirm the new billing date.");
        return new NextBillingDateDto(id, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    });

    // ---------------------------------------------------------------- plumbing

    private async Task<T> Guard<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var translated = MaxioErrors.Translate(ex);
            if (translated.StatusCode >= 500) _logger.LogError(ex, "Billing request failed: {Reason}", translated.Message);
            else _logger.LogInformation("Billing request refused ({Status}): {Reason}", translated.StatusCode, translated.Message);
            if (ReferenceEquals(translated, ex)) throw;
            throw translated;
        }
    }
}
