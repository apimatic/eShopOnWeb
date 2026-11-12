using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
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

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Subscription billing, with Maxio as the system of record. Every call goes to Maxio through the SDK.
/// </summary>
public class MaxioBillingService
{
    public const string BRAND_FAMILY_HANDLE_PREFIX = "catalog-brand-";
    private const int PAGE_SIZE = 200;
    private const int MAX_PAGES = 50;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(MaxioAdvancedBillingClient client, ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _logger = logger;
    }

    // ---------------------------------------------------------------- plans

    /// <summary>The plans this application offers: live plans a shopper can take without card details.</summary>
    public async Task<IReadOnlyList<PlanDto>> ListPlansAsync(CancellationToken ct)
    {
        var plans = new List<PlanDto>();
        for (var page = 1; ; page++)
        {
            if (page > MAX_PAGES)
                throw new BillingException(HttpStatusCode.BadGateway, "The plan list is too long to be returned completely.");
            var p = page;
            var batch = await CallAsync(() => _client.Products.ListProducts(
                new ListProductsRequest { Page = p, PerPage = PAGE_SIZE }, cancellationToken: ct));
            plans.AddRange(batch
                .Select(r => r.Product)
                .Where(x => x.ArchivedAt is null && x.RequireCreditCard != true && !string.IsNullOrEmpty(x.Handle))
                .Select(ToPlan));
            if (batch.Count < PAGE_SIZE) break;
        }
        return plans;
    }

    private async Task<PlanDto> FindOfferedPlanAsync(string? handle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(handle))
            throw new BillingException(HttpStatusCode.BadRequest, "A plan handle is required.");
        var plans = await ListPlansAsync(ct);
        return plans.FirstOrDefault(p => string.Equals(p.Handle, handle.Trim(), StringComparison.Ordinal))
               ?? throw new BillingException(HttpStatusCode.UnprocessableEntity,
                   $"'{handle}' is not a plan that can be subscribed to.");
    }

    private static PlanDto ToPlan(Product p)
    {
        var plan = new PlanDto
        {
            Id = p.Id ?? 0,
            Handle = p.Handle ?? "",
            Name = p.Name ?? "",
            Description = p.Description,
            Price = (p.PriceInCents ?? 0) / 100m,
            Interval = p.Interval ?? 1,
            IntervalUnit = p.IntervalUnit?.Value,
            Brand = p.ProductFamily?.Name,
        };
        var familyHandle = p.ProductFamily?.Handle;
        if (familyHandle is not null && familyHandle.StartsWith(BRAND_FAMILY_HANDLE_PREFIX, StringComparison.Ordinal)
            && int.TryParse(familyHandle[BRAND_FAMILY_HANDLE_PREFIX.Length..], out var brandId))
            plan.BrandId = brandId;
        return plan;
    }

    // ---------------------------------------------------------------- shopper subscriptions

    public async Task<SubscriptionDto> SubscribeAsync(string userName, string? planHandle, CancellationToken ct)
    {
        var plan = await FindOfferedPlanAsync(planHandle, ct);
        var customerId = await EnsureCustomerAsync(userName, ct);
        var collection = await CardFreeCollectionMethodAsync(ct);

        var response = await CallAsync(() => _client.Subscriptions.CreateSubscription(
            new CreateSubscriptionOperationRequest
            {
                Body = new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = plan.Handle,
                        CustomerId = customerId,
                        PaymentCollectionMethod = collection,
                    },
                },
            }, cancellationToken: ct), isWrite: true);
        return ToDto(response.Subscription ?? throw new BillingException(HttpStatusCode.BadGateway,
            "The billing system returned no subscription."));
    }

    public async Task<SubscriptionDto> GetAsync(string userName, int subscriptionId, CancellationToken ct) =>
        ToDto(await GetOwnedAsync(userName, subscriptionId, ct));

    public Task<SubscriptionDto> PauseAsync(string userName, int id, CancellationToken ct) =>
        ChangeStateAsync(userName, id, ct,
            allowed: s => s == SubscriptionState.Active || s == SubscriptionState.Trialing,
            refusal: "Only an active subscription can be paused",
            call: () => _client.SubscriptionStatus.PauseSubscription(
                new PauseSubscriptionRequest { SubscriptionId = id }, cancellationToken: ct),
            achieved: s => s == SubscriptionState.OnHold);

    public Task<SubscriptionDto> ResumeAsync(string userName, int id, CancellationToken ct) =>
        ChangeStateAsync(userName, id, ct,
            allowed: s => s == SubscriptionState.OnHold,
            refusal: "Only a paused subscription can be resumed",
            call: () => _client.SubscriptionStatus.ResumeSubscription(
                new ResumeSubscriptionRequest { SubscriptionId = id }, cancellationToken: ct),
            achieved: s => s != SubscriptionState.OnHold);

    public Task<SubscriptionDto> CancelAsync(string userName, int id, CancellationToken ct) =>
        ChangeStateAsync(userName, id, ct,
            allowed: s => s != SubscriptionState.Canceled,
            refusal: "The subscription is already cancelled",
            call: () => _client.SubscriptionStatus.CancelSubscription(
                new CancelSubscriptionRequest { SubscriptionId = id }, cancellationToken: ct),
            achieved: s => s == SubscriptionState.Canceled);

    public Task<SubscriptionDto> RestartAsync(string userName, int id, CancellationToken ct) =>
        ChangeStateAsync(userName, id, ct,
            allowed: s => s == SubscriptionState.Canceled,
            refusal: "Only a cancelled subscription can be restarted",
            call: () => _client.SubscriptionStatus.ReactivateSubscription(
                new ReactivateSubscriptionOperationRequest
                {
                    SubscriptionId = id,
                    // keep the original billing period when it can still be resumed
                    Body = new ReactivateSubscriptionRequest { Resume = true },
                }, cancellationToken: ct),
            achieved: s => s != SubscriptionState.Canceled);

    public async Task<SubscriptionDto> SwitchPlanAsync(string userName, int id, string? planHandle, CancellationToken ct)
    {
        var plan = await FindOfferedPlanAsync(planHandle, ct);
        var current = await GetOwnedAsync(userName, id, ct);
        if (string.Equals(current.Product?.Handle, plan.Handle, StringComparison.Ordinal))
            throw new BillingException(HttpStatusCode.Conflict, $"The subscription is already on plan '{plan.Handle}'.");

        var state = current.State;
        Func<Task<SubscriptionResponse>> call = state == SubscriptionState.Active || state == SubscriptionState.Trialing
            // Maxio only migrates active or trialing subscriptions
            ? () => _client.SubscriptionProducts.MigrateSubscriptionProduct(
                new MigrateSubscriptionProductRequest
                {
                    SubscriptionId = id,
                    Body = new SubscriptionProductMigrationRequest
                    {
                        Migration = new SubscriptionProductMigration { ProductHandle = plan.Handle },
                    },
                }, cancellationToken: ct)
            // a cancelled, paused or unpaid subscription has its product changed in place
            : () => _client.Subscriptions.UpdateSubscription(
                new UpdateSubscriptionOperationRequest
                {
                    SubscriptionId = id,
                    Body = new UpdateSubscriptionRequest
                    {
                        Subscription = new UpdateSubscription { ProductHandle = plan.Handle },
                    },
                }, cancellationToken: ct);

        var response = await WriteAsync(id, call, s => s.Product?.Handle == plan.Handle, ct);
        var updated = response.Subscription;
        if (updated?.Product?.Handle != plan.Handle)
        {
            // do not trust an echo that does not show the new plan: read it back
            updated = (await CallAsync(() => _client.Subscriptions.ReadSubscription(
                new ReadSubscriptionRequest { SubscriptionId = id }, cancellationToken: ct))).Subscription;
        }
        if (updated?.Product?.Handle != plan.Handle)
            throw new BillingException(HttpStatusCode.Conflict,
                $"The billing system did not move the subscription to plan '{plan.Handle}'.");
        return ToDto(updated);
    }

    // ---------------------------------------------------------------- usage

    public async Task<UsageDto> UsageForDayAsync(string userName, int id, DateOnly day, CancellationToken ct)
    {
        await GetOwnedAsync(userName, id, ct);
        var components = await CallAsync(() => _client.SubscriptionComponents.ListSubscriptionComponents(
            new ListSubscriptionComponentsRequest { SubscriptionId = id }, cancellationToken: ct));

        var result = new UsageDto { SubscriptionId = id, Date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        // widen the window by a day on both sides, then keep the usages created on the requested day in the
        // site's own time (the offset Maxio reports), so the answer does not depend on the site's zone
        var since = new DateTimeOffset(day.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var until = new DateTimeOffset(day.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var metered = components.Select(c => c.Component)
            .Where(c => c is not null && c.Kind == ComponentKind.MeteredComponent && c.ComponentId is not null)
            .Select(c => c!)
            .ToList();
        foreach (var component in metered)
        {
            var componentId = component.ComponentId!.Value;
            decimal quantity = 0;
            for (var page = 1; ; page++)
            {
                if (page > MAX_PAGES)
                    throw new BillingException(HttpStatusCode.BadGateway, "The usage list is too long to be summed completely.");
                var p = page;
                var usages = await CallAsync(() => _client.SubscriptionComponents.ListUsages(
                    new ListUsagesRequest
                    {
                        SubscriptionIdOrReference = id,
                        ComponentId = componentId,
                        SinceDate = since,
                        UntilDate = until,
                        Page = p,
                        PerPage = PAGE_SIZE,
                    }, cancellationToken: ct));
                foreach (var usage in usages.Select(u => u.Usage))
                {
                    if (usage.CreatedAt is { } created && DateOnly.FromDateTime(created.DateTime) == day)
                        quantity += ReadQuantity(usage.Quantity);
                }
                if (usages.Count < PAGE_SIZE) break;
            }
            result.Components.Add(new UsageComponentDto
            {
                ComponentId = componentId,
                Name = component.Name,
                UnitName = component.UnitName,
                Quantity = quantity,
            });
        }
        result.TotalQuantity = result.Components.Sum(c => c.Quantity);
        return result;
    }

    private static decimal ReadQuantity(Quantity1? quantity)
    {
        if (quantity is null) return 0;
        if (quantity.TryGetInt(out var whole)) return whole;
        if (quantity.TryGetString(out var text)
            && decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return 0;
    }

    // ---------------------------------------------------------------- staff

    public async Task<PlanDto> CreatePlanAsync(string? name, decimal price, int brandId, string brandName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new BillingException(HttpStatusCode.BadRequest, "A plan name is required.");
        if (name.Trim().Length > 255)
            throw new BillingException(HttpStatusCode.BadRequest, "The plan name is too long (255 characters at most).");
        if (price < 0 || decimal.Round(price, 2) != price)
            throw new BillingException(HttpStatusCode.BadRequest,
                "The monthly price must not be negative and has at most two decimals.");
        var cents = decimal.ToInt64(price * 100);

        var familyId = await EnsureBrandFamilyAsync(brandId, brandName, ct);
        ProductResponse response;
        try
        {
            response = await CreateProductAsync(familyId, name, cents, ct);
        }
        catch (BillingException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity
                                          && ex.Message.Contains("has been taken", StringComparison.OrdinalIgnoreCase))
        {
            throw new BillingException(HttpStatusCode.Conflict, $"A plan named '{name.Trim()}' already exists.", ex);
        }
        var plan = ToPlan(response.Product);
        plan.BrandId ??= brandId;
        plan.Brand ??= brandName;
        return plan;
    }

    private Task<ProductResponse> CreateProductAsync(int familyId, string name, long cents, CancellationToken ct) =>
        CallAsync(() => _client.Products.CreateProduct(
            new CreateProductRequest
            {
                ProductFamilyId = familyId.ToString(CultureInfo.InvariantCulture),
                Body = new CreateOrUpdateProductRequest
                {
                    Product = new CreateOrUpdateProduct
                    {
                        Name = name.Trim(),
                        Handle = Slug(name),
                        Description = name.Trim(),
                        PriceInCents = cents,
                        Interval = 1,
                        IntervalUnit = IntervalUnit.Month,
                    },
                },
            }, cancellationToken: ct), isWrite: true);

    public async Task<CurrencyPriceDto> AddCurrencyPriceAsync(int planId, string? currency, decimal price, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3 || !currency.Trim().All(char.IsLetter))
            throw new BillingException(HttpStatusCode.BadRequest, "The currency must be a three-letter code such as EUR.");
        if (price < 0 || price != decimal.Truncate(price) || price > int.MaxValue)
            throw new BillingException(HttpStatusCode.BadRequest,
                "The price must be a whole, non-negative number of currency units.");
        var code = currency.Trim().ToUpperInvariant();

        var product = (await CallAsync(() => _client.Products.ReadProduct(
            new ReadProductRequest { ProductId = planId }, cancellationToken: ct))).Product;
        var pricePointId = product.DefaultProductPricePointId ?? product.ProductPricePointId
            ?? throw new BillingException(HttpStatusCode.UnprocessableEntity, "The plan has no price point to price.");

        var site = (await CallAsync(() => _client.Sites.ReadSite(cancellationToken: ct))).Site;
        if (string.Equals(site.Currency, code, StringComparison.OrdinalIgnoreCase))
            throw new BillingException(HttpStatusCode.UnprocessableEntity,
                $"{code} is the billing site's own currency; the plan's monthly price already applies in it.");
        if (site.NonPrimaryCurrencies is { } enabled && !enabled.Contains(code, StringComparer.OrdinalIgnoreCase))
            throw new BillingException(HttpStatusCode.UnprocessableEntity,
                $"Currency {code} is not enabled on the billing site (enabled besides {site.Currency}: "
                + (enabled.Count == 0 ? "none" : string.Join(", ", enabled)) + ").");

        await CallAsync(() => _client.ProductPricePoints.CreateProductCurrencyPrices(
            new CreateProductCurrencyPricesOperationRequest
            {
                ProductPricePointId = pricePointId,
                Body = new CreateProductCurrencyPricesRequest
                {
                    CurrencyPrices =
                    [
                        new CreateProductCurrencyPrice { Currency = code, Price = (int)price, Role = CurrencyPriceRole.Baseline },
                    ],
                },
            }, cancellationToken: ct), isWrite: true);
        return new CurrencyPriceDto { PlanId = planId, Currency = code, Price = price };
    }

    public async Task<SubscriptionDto> SetNextBillingDateAsync(int id, DateOnly date, CancellationToken ct)
    {
        var current = (await CallAsync(() => _client.Subscriptions.ReadSubscription(
            new ReadSubscriptionRequest { SubscriptionId = id }, cancellationToken: ct))).Subscription
            ?? throw new BillingException(HttpStatusCode.NotFound, "Subscription not found.");

        var state = current.State;
        if (!(state == SubscriptionState.Active || state == SubscriptionState.Trialing
              || state == SubscriptionState.PastDue || state == SubscriptionState.SoftFailure))
            throw new BillingException(HttpStatusCode.Conflict,
                $"A subscription that is {state?.Value ?? "in an unknown state"} is not billed, so it has no billing date to set.");

        // dates are the site's calendar days: use noon in the site's own offset (taken from Maxio's own timestamps)
        var offset = current.CurrentPeriodEndsAt?.Offset ?? current.CreatedAt?.Offset ?? TimeSpan.Zero;
        var target = new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), offset);
        if (target <= DateTimeOffset.UtcNow.AddHours(24))
            throw new BillingException(HttpStatusCode.UnprocessableEntity,
                "The next billing date must be at least a day in the future.");

        var response = await WriteAsync(id, () => _client.Subscriptions.UpdateSubscription(
            new UpdateSubscriptionOperationRequest
            {
                SubscriptionId = id,
                Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { NextBillingAt = target } },
            }, cancellationToken: ct),
            s => s.CurrentPeriodEndsAt is { } end && DateOnly.FromDateTime(end.DateTime) == date, ct);

        // the update's answer carries the date as current_period_ends_at; confirm from a fresh read
        var fresh = (await CallAsync(() => _client.Subscriptions.ReadSubscription(
            new ReadSubscriptionRequest { SubscriptionId = id }, cancellationToken: ct))).Subscription ?? response.Subscription;
        if (fresh?.CurrentPeriodEndsAt is not { } ends || DateOnly.FromDateTime(ends.DateTime) != date)
            throw new BillingException(HttpStatusCode.BadGateway,
                "The billing system did not apply the requested next billing date.");
        return ToDto(fresh);
    }

    // ---------------------------------------------------------------- internals

    private async Task<Subscription> GetOwnedAsync(string userName, int id, CancellationToken ct)
    {
        Subscription? subscription;
        try
        {
            subscription = (await CallAsync(() => _client.Subscriptions.ReadSubscription(
                new ReadSubscriptionRequest { SubscriptionId = id }, cancellationToken: ct))).Subscription;
        }
        catch (BillingException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new BillingException(HttpStatusCode.NotFound, "Subscription not found.", ex);
        }
        // someone else's subscription is reported exactly like one that does not exist
        if (subscription is null
            || !string.Equals(subscription.Customer?.Reference, userName, StringComparison.OrdinalIgnoreCase))
            throw new BillingException(HttpStatusCode.NotFound, "Subscription not found.");
        return subscription;
    }

    private async Task<SubscriptionDto> ChangeStateAsync(string userName, int id, CancellationToken ct,
        Func<SubscriptionState?, bool> allowed, string refusal,
        Func<Task<SubscriptionResponse>> call, Func<SubscriptionState?, bool> achieved)
    {
        var current = await GetOwnedAsync(userName, id, ct);
        if (!allowed(current.State))
            throw new BillingException(HttpStatusCode.Conflict, $"{refusal} (it is {current.State?.Value}).");
        var response = await WriteAsync(id, call, s => achieved(s.State), ct);
        return ToDto(response.Subscription ?? current);
    }

    /// <summary>
    /// A write whose answer was lost (timeout, dropped connection) may still have been applied:
    /// settle it by reading the subscription back before reporting anything.
    /// </summary>
    private async Task<SubscriptionResponse> WriteAsync(int id, Func<Task<SubscriptionResponse>> call,
        Func<Subscription, bool> achieved, CancellationToken ct)
    {
        try
        {
            return await CallAsync(call, isWrite: true);
        }
        catch (BillingException ex) when (ex.OutcomeUnknown)
        {
            _logger.LogWarning(ex, "Outcome of a write on subscription {Id} unknown; reading it back", id);
            var read = await CallAsync(() => _client.Subscriptions.ReadSubscription(
                new ReadSubscriptionRequest { SubscriptionId = id }, cancellationToken: ct));
            if (read.Subscription is { } s && achieved(s))
                return read;
            throw new BillingException(HttpStatusCode.GatewayTimeout,
                "The billing system did not confirm the change; it has not been applied.", ex);
        }
    }

    private async Task<int> EnsureCustomerAsync(string userName, CancellationToken ct)
    {
        var existing = await FindCustomerAsync(userName, ct);
        if (existing is not null) return existing.Value;

        var local = userName.Contains('@') ? userName[..userName.IndexOf('@')] : userName;
        try
        {
            var created = await CallAsync(() => _client.Customers.CreateCustomer(
                new CreateCustomerOperationRequest
                {
                    Body = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = string.IsNullOrWhiteSpace(local) ? "Shopper" : local,
                            LastName = "eShopOnWeb",
                            Email = userName.Contains('@') ? userName : local + "@shop.invalid",
                            Reference = userName,
                        },
                    },
                }, cancellationToken: ct), isWrite: true);
            return created.Customer.Id ?? throw new BillingException(HttpStatusCode.BadGateway,
                "The billing system returned no customer id.");
        }
        catch (BillingException)
        {
            // two first requests racing for the same shopper: the loser finds the winner's customer
            var raced = await FindCustomerAsync(userName, ct);
            if (raced is not null) return raced.Value;
            throw;
        }
    }

    private async Task<int?> FindCustomerAsync(string userName, CancellationToken ct)
    {
        try
        {
            var found = await CallAsync(() => _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = userName }, cancellationToken: ct));
            return found.Customer.Id;
        }
        catch (BillingException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>Collection without a card: remittance on Relationship Invoicing sites, invoice otherwise.</summary>
    private async Task<CollectionMethod> CardFreeCollectionMethodAsync(CancellationToken ct)
    {
        var site = (await CallAsync(() => _client.Sites.ReadSite(cancellationToken: ct))).Site;
        return site.RelationshipInvoicingEnabled == true ? CollectionMethod.Remittance : CollectionMethod.Invoice;
    }

    private async Task<int> EnsureBrandFamilyAsync(int brandId, string brandName, CancellationToken ct)
    {
        var handle = BRAND_FAMILY_HANDLE_PREFIX + brandId.ToString(CultureInfo.InvariantCulture);
        var families = await CallAsync(() => _client.ProductFamilies.ListProductFamilies(
            new ListProductFamiliesRequest(), cancellationToken: ct));
        var family = families.Select(f => f.ProductFamily).FirstOrDefault(f => f?.Handle == handle);
        if (family?.Id is { } existing) return existing;

        try
        {
            var created = await CallAsync(() => _client.ProductFamilies.CreateProductFamily(
                new CreateProductFamilyOperationRequest
                {
                    Body = new CreateProductFamilyRequest
                    {
                        ProductFamily = new CreateProductFamily { Name = brandName, Handle = handle },
                    },
                }, cancellationToken: ct), isWrite: true);
            return created.ProductFamily?.Id ?? throw new BillingException(HttpStatusCode.BadGateway,
                "The billing system returned no product family.");
        }
        catch (BillingException)
        {
            families = await CallAsync(() => _client.ProductFamilies.ListProductFamilies(
                new ListProductFamiliesRequest(), cancellationToken: ct));
            var raced = families.Select(f => f.ProductFamily).FirstOrDefault(f => f?.Handle == handle);
            if (raced?.Id is { } id) return id;
            throw;
        }
    }

    private async Task<T> CallAsync<T>(Func<Task<T>> call, bool isWrite = false)
    {
        try
        {
            return await call();
        }
        catch (BillingException)
        {
            throw;
        }
        catch (SdkException ex)
        {
            var translated = MaxioErrors.Translate(ex, isWrite);
            _logger.LogWarning("Maxio call failed: {Status} {Method} {Path}", (int)translated.StatusCode,
                ex.Method, ex.RequestUri.AbsolutePath);
            throw translated;
        }
    }

    private static SubscriptionDto ToDto(Subscription s)
    {
        var balance = (s.BalanceInCents ?? 0) / 100m;
        return new SubscriptionDto
        {
            Id = s.Id ?? 0,
            State = s.State?.Value,
            PlanHandle = s.Product?.Handle,
            PlanName = s.Product?.Name,
            PlanId = s.Product?.Id,
            Price = s.ProductPriceInCents is { } cents ? cents / 100m : null,
            Paid = balance <= 0,
            Balance = balance,
            NextBillingAt = s.CurrentPeriodEndsAt,
            CreatedAt = s.CreatedAt,
            CanceledAt = s.CanceledAt,
            PaymentCollectionMethod = s.PaymentCollectionMethod?.Value,
        };
    }

    private static string Slug(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "plan-" + Guid.NewGuid().ToString("N")[..8] : slug;
    }
}
