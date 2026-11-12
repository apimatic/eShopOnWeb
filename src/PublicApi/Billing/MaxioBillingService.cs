using System;
using System.Collections.Concurrent;
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
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
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
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Subscription billing on top of Maxio (the billing system of record).
/// Every failure surfaces as a <see cref="BillingException"/> carrying an HTTP status and a reason.
/// </summary>
public class MaxioBillingService
{
    private const int PageSize = 200;
    private const int MaxPages = 50;
    private const string FamilySuffix = " plans";

    // In-process claims serialising subscribe per shopper (see maxio-advanced-billing-plan.md, DUPLICATE CLAIMS).
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeClaims = new();

    private readonly MaxioAdvancedBillingClient _client;
    private readonly IReadRepository<CatalogBrand> _brands;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(MaxioAdvancedBillingClient client, IReadRepository<CatalogBrand> brands, ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _brands = brands;
        _logger = logger;
    }

    // ---------------------------------------------------------------- plans

    /// <summary>Plans a shopper can subscribe to: live products in the family of a catalog brand.</summary>
    public async Task<IReadOnlyList<PlanDto>> ListPlansAsync(CancellationToken ct)
    {
        var brandNames = await BrandNamesAsync();
        var plans = new List<PlanDto>();
        foreach (var p in await ListAllProductsAsync(ct))
        {
            if (p.ArchivedAt != null) continue;
            var brand = BrandOf(p, brandNames);
            if (brand == null) continue;
            plans.Add(ToPlan(p, brand));
        }
        return plans;
    }

    private async Task<PlanDto> FindOfferedPlanAsync(string? handle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw BillingException.BadRequest("A plan handle is required.");
        }
        var plans = await ListPlansAsync(ct);
        return plans.FirstOrDefault(p => string.Equals(p.Handle, handle.Trim(), StringComparison.Ordinal))
            ?? throw BillingException.NotFound($"Plan '{handle}' is not one of the plans on offer.");
    }

    private async Task<List<MaxioAdvancedBilling.Models.Product>> ListAllProductsAsync(CancellationToken ct)
    {
        var all = new List<MaxioAdvancedBilling.Models.Product>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var batch = await Execute(() => _client.Products.ListProducts(
                new ListProductsRequest { Page = page, PerPage = PageSize, IncludeArchived = false }, cancellationToken: ct));
            all.AddRange(batch.Select(b => b.Product));
            if (batch.Count < PageSize) return all;
        }
        // Never silently truncate: the caller would see a partial plan list.
        throw new BillingException(502, "The billing system holds more plans than can be listed.");
    }

    private async Task<List<string>> BrandNamesAsync()
    {
        var brands = await _brands.ListAsync();
        return brands.Select(b => b.Brand).ToList();
    }

    private static string? BrandOf(MaxioAdvancedBilling.Models.Product p, IEnumerable<string> brandNames)
    {
        var family = p.ProductFamily?.Name;
        if (family == null) return null;
        return brandNames.FirstOrDefault(b => string.Equals(family.Trim(), b + FamilySuffix, StringComparison.OrdinalIgnoreCase));
    }

    private static PlanDto ToPlan(MaxioAdvancedBilling.Models.Product p, string brand) => new()
    {
        Id = p.Id ?? 0,
        Handle = p.Handle ?? "",
        Name = p.Name ?? "",
        Description = p.Description,
        Brand = brand,
        PriceInCents = p.PriceInCents ?? 0,
        Price = (p.PriceInCents ?? 0) / 100m,
        Interval = p.Interval ?? 1,
        IntervalUnit = p.IntervalUnit?.Value ?? "month",
        PricePointId = p.ProductPricePointId ?? p.DefaultProductPricePointId,
    };

    // ------------------------------------------------------------ subscribe

    public async Task<SubscriptionDto> SubscribeAsync(string userName, string? planHandle, CancellationToken ct)
    {
        var plan = await FindOfferedPlanAsync(planHandle, ct);

        // Claim first, then call Maxio: a second request from the same shopper waits here and
        // then sees the first one's subscription instead of creating another.
        var claim = SubscribeClaims.GetOrAdd(userName.ToLowerInvariant(), _ => new SemaphoreSlim(1, 1));
        await claim.WaitAsync(ct);
        try
        {
            var customer = await EnsureCustomerAsync(userName, ct);
            var existing = await LiveSubscriptionsOfAsync(customer.Id!.Value, ct);
            var already = existing.FirstOrDefault(s => s.Product?.Handle == plan.Handle);
            if (already != null)
            {
                throw BillingException.Conflict($"You already have a live subscription (#{already.Id}) to plan '{plan.Handle}'.");
            }

            try
            {
                var created = await Execute(() => _client.Subscriptions.CreateSubscription(
                    new CreateSubscriptionOperationRequest
                    {
                        Body = new CreateSubscriptionRequest
                        {
                            Subscription = new CreateSubscription
                            {
                                ProductHandle = plan.Handle,
                                CustomerId = customer.Id,
                                // No card details: the subscriber is invoiced and pays later.
                                PaymentCollectionMethod = CollectionMethod.Remittance,
                            },
                        },
                    }, cancellationToken: ct), ListReason<CreateSubscriptionError>(e => e.TryGetErrorListResponse1(out var v) ? v : null));
                return ToSubscription(created.Subscription ?? throw new BillingException(502, "The billing system returned no subscription."));
            }
            catch (BillingException be) when (be.OutcomeUnknown)
            {
                // The connection failed after Maxio may have acted: settle by looking the subscription up.
                var settled = (await LiveSubscriptionsOfAsync(customer.Id!.Value, ct)).FirstOrDefault(s => s.Product?.Handle == plan.Handle);
                if (settled != null) return ToSubscription(settled);
                throw;
            }
        }
        finally
        {
            claim.Release();
        }
    }

    private async Task<Customer> EnsureCustomerAsync(string userName, CancellationToken ct)
    {
        var reference = ReferenceOf(userName);
        try
        {
            var found = await _client.Customers.ReadCustomerByReference(new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: ct);
            if (found.Customer.Id != null) return found.Customer;
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // not yet a Maxio customer — create below
        }
        catch (Exception ex) when (IsMaxioFailure(ex))
        {
            throw Translate(ex, null, false);
        }

        var email = userName.Contains('@') ? userName : userName + "@example.invalid";
        var local = email.Split('@')[0];
        var created = await Execute(() => _client.Customers.CreateCustomer(new CreateCustomerOperationRequest
        {
            Body = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = string.IsNullOrWhiteSpace(local) ? "Shopper" : local,
                    LastName = "eShopOnWeb",
                    Email = email,
                    Reference = reference,
                },
            },
        }, cancellationToken: ct), null);
        return created.Customer;
    }

    private async Task<List<MaxioAdvancedBilling.Models.Subscription>> LiveSubscriptionsOfAsync(int customerId, CancellationToken ct)
    {
        var all = await Execute(() => _client.Customers.ListCustomerSubscriptions(new ListCustomerSubscriptionsRequest { CustomerId = customerId }, cancellationToken: ct));
        return all.Select(s => s.Subscription)
            .Where(s => s != null && s.State != SubscriptionState.Canceled && s.State != SubscriptionState.Expired)
            .Select(s => s!).ToList();
    }

    private static string ReferenceOf(string userName) => userName.Trim();

    // ------------------------------------------------- read / own subscription

    public async Task<SubscriptionDto> GetSubscriptionAsync(string userName, int subscriptionId, CancellationToken ct)
        => ToSubscription(await OwnedAsync(userName, subscriptionId, ct));

    private async Task<MaxioAdvancedBilling.Models.Subscription> OwnedAsync(string userName, int subscriptionId, CancellationToken ct)
    {
        var sub = await ReadAsync(subscriptionId, ct);
        if (!string.Equals(sub.Customer?.Reference, ReferenceOf(userName), StringComparison.OrdinalIgnoreCase))
        {
            // Not the caller's: indistinguishable from a missing subscription.
            throw BillingException.NotFound($"Subscription {subscriptionId} was not found.");
        }
        return sub;
    }

    private async Task<MaxioAdvancedBilling.Models.Subscription> ReadAsync(int subscriptionId, CancellationToken ct)
    {
        var res = await Execute(() => _client.Subscriptions.ReadSubscription(new ReadSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct), null, notFound: $"Subscription {subscriptionId} was not found.");
        return res.Subscription ?? throw BillingException.NotFound($"Subscription {subscriptionId} was not found.");
    }

    // ----------------------------------------------------- status transitions

    public async Task<SubscriptionDto> PauseAsync(string userName, int id, CancellationToken ct)
    {
        var sub = await OwnedAsync(userName, id, ct);
        if (sub.State == SubscriptionState.OnHold || sub.State == SubscriptionState.Paused) throw BillingException.Conflict("The subscription is already paused.");
        RequireNot(sub, "pause", SubscriptionState.Canceled, SubscriptionState.Expired);
        return await WriteAsync(id, ct, () => _client.SubscriptionStatus.PauseSubscription(new PauseSubscriptionRequest { SubscriptionId = id }, cancellationToken: ct),
            ListReason<PauseSubscriptionError>(e => e.TryGetErrorListResponse1(out var v) ? v : null),
            s => s.State == SubscriptionState.OnHold || s.State == SubscriptionState.Paused);
    }

    public async Task<SubscriptionDto> ResumeAsync(string userName, int id, CancellationToken ct)
    {
        var sub = await OwnedAsync(userName, id, ct);
        if (sub.State != SubscriptionState.OnHold && sub.State != SubscriptionState.Paused) throw BillingException.Conflict($"Only a paused subscription can be resumed; this one is {sub.State?.Value}.");
        return await WriteAsync(id, ct, () => _client.SubscriptionStatus.ResumeSubscription(new ResumeSubscriptionRequest { SubscriptionId = id }, cancellationToken: ct),
            ListReason<ResumeSubscriptionError>(e => e.TryGetErrorListResponse1(out var v) ? v : null),
            s => s.State != SubscriptionState.OnHold && s.State != SubscriptionState.Paused);
    }

    public async Task<SubscriptionDto> CancelAsync(string userName, int id, CancellationToken ct)
    {
        var sub = await OwnedAsync(userName, id, ct);
        if (sub.State == SubscriptionState.Canceled) throw BillingException.Conflict("The subscription is already cancelled.");
        RequireNot(sub, "cancel", SubscriptionState.Expired);
        return await WriteAsync(id, ct, () => _client.SubscriptionStatus.CancelSubscription(
                new CancelSubscriptionRequest { SubscriptionId = id, Body = new CancellationRequest { Subscription = new CancellationOptions { CancellationMessage = "Cancelled by the subscriber" } } }, cancellationToken: ct),
            ListReason<CancelSubscriptionError>(e => e.TryGetCancelSubscriptionErrorResponse(out var v) && v.TryGetErrorListResponse1(out var l) ? l : null),
            s => s.State == SubscriptionState.Canceled);
    }

    public async Task<SubscriptionDto> RestartAsync(string userName, int id, CancellationToken ct)
    {
        var sub = await OwnedAsync(userName, id, ct);
        if (sub.State != SubscriptionState.Canceled) throw BillingException.Conflict($"Only a cancelled subscription can be restarted; this one is {sub.State?.Value}.");
        return await WriteAsync(id, ct, () => _client.SubscriptionStatus.ReactivateSubscription(new ReactivateSubscriptionOperationRequest { SubscriptionId = id }, cancellationToken: ct),
            ListReason<ReactivateSubscriptionError>(e => e.TryGetErrorListResponse1(out var v) ? v : null),
            s => s.State != SubscriptionState.Canceled);
    }

    public async Task<SubscriptionDto> SwitchAsync(string userName, int id, string? planHandle, CancellationToken ct)
    {
        var plan = await FindOfferedPlanAsync(planHandle, ct);
        var sub = await OwnedAsync(userName, id, ct);
        if (sub.Product?.Handle == plan.Handle) throw BillingException.Conflict($"The subscription is already on plan '{plan.Handle}'.");
        RequireNot(sub, "switch", SubscriptionState.Expired);

        Func<Task<SubscriptionResponse>> write;
        Func<ApiException, string?> reason;
        if (sub.State == SubscriptionState.Canceled)
        {
            // Maxio's product migration refuses a cancelled subscription, but a subscription update may
            // re-point it at another product (it stays cancelled until restarted).
            write = () => _client.Subscriptions.UpdateSubscription(new UpdateSubscriptionOperationRequest
            {
                SubscriptionId = id,
                Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { ProductHandle = plan.Handle } },
            }, cancellationToken: ct);
            reason = ListReason<UpdateSubscriptionError>(e => e.TryGetErrorListResponse1(out var v) ? v : null);
        }
        else
        {
            write = () => _client.SubscriptionProducts.MigrateSubscriptionProduct(new MigrateSubscriptionProductRequest
            {
                SubscriptionId = id,
                Body = new SubscriptionProductMigrationRequest { Migration = new SubscriptionProductMigration { ProductHandle = plan.Handle } },
            }, cancellationToken: ct);
            reason = ListReason<MigrateSubscriptionProductError>(e => e.TryGetErrorListResponse1(out var v) ? v : null);
        }

        var dto = await WriteAsync(id, ct, write, reason, s => s.Product?.Handle == plan.Handle);
        if (dto.Plan?.Handle != plan.Handle)
        {
            throw new BillingException(502, "The billing system did not move the subscription to the new plan.");
        }
        return dto;
    }

    private static void RequireNot(MaxioAdvancedBilling.Models.Subscription sub, string verb, params SubscriptionState[] blocked)
    {
        if (blocked.Contains(sub.State!))
        {
            throw BillingException.Conflict($"Cannot {verb} a subscription that is {sub.State?.Value}.");
        }
    }

    /// <summary>
    /// Runs a subscription write. If the connection fails after Maxio may have acted, the outcome is
    /// settled by re-reading the subscription before answering.
    /// </summary>
    private async Task<SubscriptionDto> WriteAsync(int id, CancellationToken ct, Func<Task<SubscriptionResponse>> write,
        Func<ApiException, string?> reason, Func<MaxioAdvancedBilling.Models.Subscription, bool> achieved)
    {
        try
        {
            var res = await Execute(write, reason, conflictOnRejection: true, notFound: $"Subscription {id} was not found.");
            return ToSubscription(res.Subscription ?? await ReadAsync(id, ct));
        }
        catch (BillingException be) when (be.OutcomeUnknown)
        {
            var current = await ReadAsync(id, ct);
            if (achieved(current)) return ToSubscription(current);
            throw;
        }
    }

    // ---------------------------------------------------------------- usage

    public async Task<DailyUsageDto> UsageForDayAsync(string userName, int id, DateTime day, CancellationToken ct)
    {
        await OwnedAsync(userName, id, ct);
        var components = await Execute(() => _client.SubscriptionComponents.ListSubscriptionComponents(
            new ListSubscriptionComponentsRequest { SubscriptionId = id }, cancellationToken: ct), null, notFound: $"Subscription {id} was not found.");

        var result = new DailyUsageDto { SubscriptionId = id, Date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        // Maxio's since/until are midnight-inclusive and read in the site's time zone, so ask for a
        // window a day wider on each side and keep exactly the entries stamped on the chosen day.
        var since = new DateTimeOffset(DateTime.SpecifyKind(day.Date.AddDays(-1), DateTimeKind.Utc));
        var until = new DateTimeOffset(DateTime.SpecifyKind(day.Date.AddDays(2), DateTimeKind.Utc));

        foreach (var c in components.Select(r => r.Component).Where(c => c.ComponentId != null
                     && (c.Kind == ComponentKind.MeteredComponent || c.Kind == ComponentKind.PrepaidUsageComponent)))
        {
            var entry = new ComponentUsageDto { ComponentId = c.ComponentId, Name = c.Name, UnitName = c.UnitName };
            for (var page = 1; ; page++)
            {
                if (page > MaxPages) throw new BillingException(502, "The usage history is too long to read.");
                var usages = await Execute(() => _client.SubscriptionComponents.ListUsages(new ListUsagesRequest
                {
                    SubscriptionIdOrReference = id,
                    ComponentId = c.ComponentId!.Value,
                    SinceDate = since,
                    UntilDate = until,
                    Page = page,
                    PerPage = PageSize,
                }, cancellationToken: ct));
                foreach (var u in usages.Select(x => x.Usage))
                {
                    if (u.CreatedAt == null || u.CreatedAt.Value.Date != day.Date) continue;
                    var q = QuantityOf(u.Quantity);
                    entry.Usages.Add(new UsageEntryDto { Id = u.Id, Quantity = q, Memo = u.Memo, CreatedAt = u.CreatedAt });
                    entry.TotalQuantity += q;
                }
                if (usages.Count < PageSize) break;
            }
            result.TotalQuantity += entry.TotalQuantity;
            result.Components.Add(entry);
        }
        return result;
    }

    private static decimal QuantityOf(MaxioAdvancedBilling.Models.AnyOf.Quantity1? q)
    {
        if (q == null) return 0;
        if (q.TryGetInt(out var i)) return i;
        if (q.TryGetString(out var s) && decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return d;
        return 0;
    }

    // ---------------------------------------------------------------- staff

    public async Task<PlanDto> CreatePlanAsync(string? name, decimal? price, string? brandName, int? brandId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) throw BillingException.BadRequest("A plan name is required.");
        if (price == null || price < 0) throw BillingException.BadRequest("A monthly price of zero or more is required.");
        if (price != decimal.Round(price.Value, 2)) throw BillingException.BadRequest("The monthly price can have at most two decimal places.");

        var brands = await _brands.ListAsync();
        CatalogBrand? brand = null;
        if (brandId != null) brand = brands.FirstOrDefault(b => b.Id == brandId);
        else if (!string.IsNullOrWhiteSpace(brandName)) brand = brands.FirstOrDefault(b => string.Equals(b.Brand, brandName.Trim(), StringComparison.OrdinalIgnoreCase));
        else throw BillingException.BadRequest("A brand is required.");
        if (brand == null) throw BillingException.BadRequest("The brand is not one of the brands in the shop's catalog.");

        var family = await EnsureFamilyAsync(brand.Brand, ct);
        var created = await Execute(() => _client.Products.CreateProduct(new CreateProductRequest
        {
            ProductFamilyId = family.ToString(CultureInfo.InvariantCulture),
            Body = new CreateOrUpdateProductRequest
            {
                Product = new CreateOrUpdateProduct
                {
                    Name = name.Trim(),
                    Handle = Slug(name),
                    Description = name.Trim(),
                    PriceInCents = (long)decimal.Round(price.Value * 100m),
                    Interval = 1,
                    IntervalUnit = IntervalUnit.Month,
                },
            },
        }, cancellationToken: ct), ListReason<CreateProductError>(e => e.TryGetErrorListResponse1(out var v) ? v : null), conflictOnRejection: true);
        return ToPlan(created.Product, brand.Brand);
    }

    private async Task<int> EnsureFamilyAsync(string brand, CancellationToken ct)
    {
        var wanted = brand + FamilySuffix;
        var families = await Execute(() => _client.ProductFamilies.ListProductFamilies(new ListProductFamiliesRequest(), cancellationToken: ct));
        var match = families.Select(f => f.ProductFamily).FirstOrDefault(f => f != null && f.ArchivedAt == null
            && string.Equals(f.Name?.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
        if (match?.Id != null) return match.Id.Value;

        var created = await Execute(() => _client.ProductFamilies.CreateProductFamily(new CreateProductFamilyOperationRequest
        {
            Body = new CreateProductFamilyRequest { ProductFamily = new CreateProductFamily { Name = wanted, Handle = Slug(wanted) } },
        }, cancellationToken: ct), ListReason<CreateProductFamilyError>(e => e.TryGetErrorListResponse1(out var v) ? v : null), conflictOnRejection: true);
        return created.ProductFamily?.Id ?? throw new BillingException(502, "The billing system returned no product family.");
    }

    public async Task<IReadOnlyList<CurrencyPriceDto>> AddCurrencyPriceAsync(int planId, string? currency, decimal? price, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(currency) || !Regex.IsMatch(currency.Trim(), "^[A-Za-z]{3}$")) throw BillingException.BadRequest("A three-letter currency code is required.");
        if (price == null || price <= 0) throw BillingException.BadRequest("A price greater than zero is required.");
        if (price != decimal.Truncate(price.Value)) throw BillingException.BadRequest("The currency price must be a whole number of currency units.");

        var plan = (await ListPlansAsync(ct)).FirstOrDefault(p => p.Id == planId)
            ?? throw BillingException.NotFound($"Plan {planId} was not found.");
        if (plan.PricePointId == null) throw BillingException.Conflict("The plan has no price point to price in another currency.");

        var res = await Execute(() => _client.ProductPricePoints.CreateProductCurrencyPrices(new CreateProductCurrencyPricesOperationRequest
        {
            ProductPricePointId = plan.PricePointId.Value,
            Body = new CreateProductCurrencyPricesRequest
            {
                CurrencyPrices = new[]
                {
                    new CreateProductCurrencyPrice { Currency = currency.Trim().ToUpperInvariant(), Price = (int)price.Value, Role = CurrencyPriceRole.Baseline },
                },
            },
        }, cancellationToken: ct), e => e is ApiException<CreateProductCurrencyPricesError> t
            ? (t.Error.TryGetErrorArrayMapResponse1(out var m) && m.Errors != null ? string.Join("; ", m.Errors.Select(kv => $"{kv.Key}: {FlattenErrors(kv.Value)}")) : null)
            : null, conflictOnRejection: false);
        return res.CurrencyPrices.Select(c => new CurrencyPriceDto { Currency = c.Currency, Price = c.Price, Role = c.Role?.Value }).ToList();
    }

    public async Task<SubscriptionDto> SetNextBillingDateAsync(int id, DateTime date, CancellationToken ct)
    {
        var sub = await ReadAsync(id, ct);
        if (sub.State == SubscriptionState.Canceled || sub.State == SubscriptionState.Expired || sub.State == SubscriptionState.Pending || sub.State == SubscriptionState.FailedToCreate)
        {
            throw BillingException.Conflict($"The next billing date cannot be set on a {sub.State?.Value} subscription.");
        }
        // Keep Maxio's own time-of-day and site offset so the date lands on the right calendar day.
        var anchor = sub.NextAssessmentAt ?? sub.CreatedAt ?? new DateTimeOffset(DateTime.UtcNow);
        var target = new DateTimeOffset(date.Date + anchor.TimeOfDay, anchor.Offset);
        if (target <= DateTimeOffset.UtcNow) throw BillingException.BadRequest("The next billing date must be in the future.");

        return await WriteAsync(id, ct, () => _client.Subscriptions.UpdateSubscription(new UpdateSubscriptionOperationRequest
        {
            SubscriptionId = id,
            Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { NextBillingAt = target } },
        }, cancellationToken: ct), ListReason<UpdateSubscriptionError>(e => e.TryGetErrorListResponse1(out var v) ? v : null),
            s => s.NextAssessmentAt?.Date == date.Date);
    }

    // -------------------------------------------------------------- mapping

    private static SubscriptionDto ToSubscription(MaxioAdvancedBilling.Models.Subscription s)
    {
        var balance = s.BalanceInCents ?? 0;
        return new SubscriptionDto
        {
            Id = s.Id ?? 0,
            State = s.State?.Value ?? "",
            // Paid: the subscriber owes nothing on it.
            Paid = balance <= 0,
            BalanceInCents = balance,
            Balance = balance / 100m,
            Plan = s.Product == null ? null : new PlanSummaryDto { Id = s.Product.Id, Handle = s.Product.Handle, Name = s.Product.Name, PriceInCents = s.Product.PriceInCents },
            NextBillingAt = s.NextAssessmentAt,
            CanceledAt = s.CanceledAt,
            CreatedAt = s.CreatedAt,
        };
    }

    private static string Slug(string text)
    {
        var sb = new StringBuilder();
        foreach (var ch in text.Trim().ToLowerInvariant())
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9')) sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "plan" : slug;
    }

    // ------------------------------------------------------ error boundary

    private static Func<ApiException, string?> ListReason<TError>(Func<TError, ErrorListResponse1?> pick) where TError : ApiError
        => ex => ex is ApiException<TError> t ? (pick(t.Error) is { } l ? string.Join("; ", l.Errors) : null) : null;

    private static string FlattenErrors(object value) =>
        value is System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Array } arr
            ? string.Join(", ", arr.EnumerateArray().Select(x => x.ToString()).Distinct())
            : value.ToString() ?? "";

    private static bool IsMaxioFailure(Exception ex) =>
        ex is ApiException or SdkTimeoutException or SdkConnectionException or AuthSchemeException;

    private async Task<T> Execute<T>(Func<Task<T>> call, Func<ApiException, string?>? reason = null,
        bool conflictOnRejection = false, string? notFound = null)
    {
        try
        {
            return await call();
        }
        catch (Exception ex) when (IsMaxioFailure(ex))
        {
            throw Translate(ex, reason, conflictOnRejection, notFound);
        }
    }

    private BillingException Translate(Exception ex, Func<ApiException, string?>? reason, bool conflictOnRejection, string? notFound = null)
    {
        switch (ex)
        {
            case ApiException api:
                var detail = reason?.Invoke(api) ?? (api is ApiException<RawError> raw ? Truncate(raw.Error.ReadAsString()) : null);
                var status = (int)api.StatusCode;
                _logger.LogWarning("Maxio answered {Status}: {Detail}", status, detail);
                if (status == 404) return new BillingException(404, notFound ?? "Not found in the billing system.", ex);
                if (status == 409) return new BillingException(409, detail ?? "Conflict in the billing system.", ex);
                if (status is 400 or 422) return new BillingException(conflictOnRejection ? 409 : 422, detail ?? "The billing system rejected the request.", ex);
                // 401/403: our credentials; 429: our quota — not the caller's doing.
                if (status == 429) return new BillingException(503, "The billing system is temporarily unavailable.", ex);
                return new BillingException(502, "The billing system could not process the request.", ex);
            case SdkTimeoutException:
                _logger.LogError(ex, "Maxio timed out");
                return new BillingException(504, "The billing system did not answer in time.", ex, outcomeUnknown: true);
            case SdkConnectionException:
                _logger.LogError(ex, "Maxio unreachable");
                return new BillingException(502, "The billing system is unreachable.", ex, outcomeUnknown: true);
            default:
                _logger.LogError(ex, "Maxio credentials missing or rejected");
                return new BillingException(502, "The billing system is not available.", ex);
        }
    }

    private static string? Truncate(string? s) => s is { Length: > 300 } ? s[..300] : s;
}
