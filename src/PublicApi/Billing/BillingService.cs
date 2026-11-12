using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.ProductPricePoints;
using MaxioAdvancedBilling.Requests.Products;
using MaxioAdvancedBilling.Requests.SubscriptionComponents;
using MaxioAdvancedBilling.Requests.SubscriptionInvoiceAccount;
using MaxioAdvancedBilling.Requests.SubscriptionProducts;
using MaxioAdvancedBilling.Requests.Subscriptions;
using MaxioAdvancedBilling.Requests.SubscriptionStatus;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Subscription billing on top of Maxio. Maxio is the system of record: nothing about a subscription is stored
/// here except short-lived claims that stop a write from being made twice at once.
/// Every public method is bounded by one total budget that also ends when the caller disconnects.
/// </summary>
public class BillingService
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(60);
    private const int PerPage = 200;
    private const int MaxPages = 25;
    private const string FamilyHandlePrefix = "eshop-brand-";

    private readonly MaxioGateway _maxio;
    private readonly MaxioSiteCache _site;
    private readonly ClaimStore _claims;
    private readonly IRepository<CatalogBrand> _brands;
    private readonly ILogger<BillingService> _logger;

    public BillingService(MaxioGateway maxio, MaxioSiteCache site, ClaimStore claims,
        IRepository<CatalogBrand> brands, ILogger<BillingService> logger)
    {
        _maxio = maxio;
        _site = site;
        _claims = claims;
        _brands = brands;
        _logger = logger;
    }

    private static CancellationTokenSource Bound(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Budget);
        return cts;
    }

    // ------------------------------------------------------------------ plans (shopper)

    public async Task<ListPlansResponse> ListPlansAsync(CancellationToken requestCt)
    {
        using var cts = Bound(requestCt);
        var (plans, truncated) = await OfferedPlansAsync(cts.Token);
        return new ListPlansResponse { Plans = plans, Truncated = truncated };
    }

    /// <summary>Plans this shop offers: live Maxio products whose family is one of the catalog brands.</summary>
    private async Task<(List<PlanDto> Plans, bool Truncated)> OfferedPlansAsync(CancellationToken ct)
    {
        var brands = await _brands.ListAsync(ct);
        var plans = new List<PlanDto>();
        var truncated = true;
        for (var page = 1; page <= MaxPages; page++)
        {
            var batch = await _maxio.ReadAsync(c => c.Products.ListProducts(
                new ListProductsRequest { Page = page, PerPage = PerPage }, cancellationToken: ct));
            foreach (var item in batch)
            {
                var p = item.Product;
                if (p.ArchivedAt != null || p.Id == null || string.IsNullOrEmpty(p.Handle)) continue;
                var brand = BrandFor(p.ProductFamily, brands);
                if (brand == null) continue;
                plans.Add(new PlanDto
                {
                    Id = p.Id.Value,
                    Handle = p.Handle,
                    Name = p.Name ?? p.Handle,
                    Description = p.Description,
                    Price = (p.PriceInCents ?? 0) / 100m,
                    Interval = p.Interval != null ? $"{p.Interval} {p.IntervalUnit?.Value}" : string.Empty,
                    BrandId = brand.Id,
                    Brand = brand.Brand,
                });
            }
            if (batch.Count < PerPage) { truncated = false; break; }
        }
        return (plans, truncated);
    }

    private static CatalogBrand? BrandFor(ProductFamily? family, IEnumerable<CatalogBrand> brands)
    {
        if (family == null) return null;
        return brands.FirstOrDefault(b =>
            string.Equals(family.Handle, FamilyHandlePrefix + b.Id, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(family.Name, b.Brand, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<PlanDto> RequireOfferedPlanAsync(string? handle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(handle)) throw BillingException.BadRequest("planHandle is required.");
        var (plans, truncated) = await OfferedPlansAsync(ct);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, handle.Trim(), StringComparison.OrdinalIgnoreCase));
        if (plan != null) return plan;
        if (truncated) throw new BillingException(503, "The plan list is too long to verify the plan right now; try again later.");
        throw BillingException.NotFound($"Plan '{handle}' is not one of the plans on offer.");
    }

    // ------------------------------------------------------------------ subscribe

    public async Task<SubscriptionDto> SubscribeAsync(string user, string? planHandle, CancellationToken requestCt)
    {
        using var cts = Bound(requestCt);
        var ct = cts.Token;
        var plan = await RequireOfferedPlanAsync(planHandle, ct);
        var site = await _site.GetAsync(_maxio, ct);
        var customerId = await EnsureCustomerAsync(user, ct);

        // Claim first, then call Maxio: a second concurrent subscribe to the same plan is refused by the claim store.
        var claimKey = $"sub:{user.ToLowerInvariant()}:{plan.Handle.ToLowerInvariant()}";
        if (!await _claims.TryClaimAsync(claimKey, ct))
            throw BillingException.Conflict("A subscription to this plan is already being created.");
        try
        {
            var reference = Guid.NewGuid().ToString("N");
            var created = await _maxio.WriteAsync(
                c => c.Subscriptions.CreateSubscription(new CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = plan.Handle,
                            CustomerId = customerId,
                            // No card: invoice-style collection only.
                            PaymentCollectionMethod = site.RelationshipInvoicing ? CollectionMethod.Remittance : CollectionMethod.Invoice,
                            Reference = reference,
                        }
                    }
                }, cancellationToken: ct),
                async () => await FindByReferenceAsync(reference, ct));
            var sub = created.Subscription ?? throw new BillingException(502, "Billing provider returned no subscription.");
            return await ToDtoAsync(sub, ct);
        }
        finally
        {
            await _claims.ReleaseAsync(claimKey);
        }
    }

    private async Task<SubscriptionResponse?> FindByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            return await _maxio.ReadAsync(c => c.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = reference }, cancellationToken: ct));
        }
        catch (BillingException e) when (e.StatusCode == 404)
        {
            return null;
        }
    }

    private async Task<int> EnsureCustomerAsync(string user, CancellationToken ct)
    {
        var existing = await FindCustomerAsync(user, ct);
        if (existing != null) return existing.Value;

        var claimKey = $"customer:{user.ToLowerInvariant()}";
        if (!await _claims.TryClaimAsync(claimKey, ct))
        {
            // Another request is creating this shopper's customer; use theirs once it exists.
            existing = await FindCustomerAsync(user, ct);
            return existing ?? throw BillingException.Conflict("Your billing profile is being set up; try again in a moment.");
        }
        try
        {
            var (first, last) = NamesFrom(user);
            try
            {
                var created = await _maxio.WriteAsync(
                    c => c.Customers.CreateCustomer(new CreateCustomerOperationRequest
                    {
                        Body = new CreateCustomerRequest
                        {
                            Customer = new CreateCustomer { FirstName = first, LastName = last, Email = user, Reference = user }
                        }
                    }, cancellationToken: ct),
                    async () =>
                    {
                        var id = await FindCustomerAsync(user, ct);
                        return id == null ? null : new CustomerResponse { Customer = new Customer { Id = id, Reference = user } };
                    });
                return created.Customer.Id ?? throw new BillingException(502, "Billing provider returned no customer id.");
            }
            catch (BillingException e) when (e.StatusCode == 422)
            {
                // Reference already taken: the customer exists (made by an earlier request).
                existing = await FindCustomerAsync(user, ct);
                if (existing != null) return existing.Value;
                throw;
            }
        }
        finally
        {
            await _claims.ReleaseAsync(claimKey);
        }
    }

    private async Task<int?> FindCustomerAsync(string user, CancellationToken ct)
    {
        try
        {
            var resp = await _maxio.ReadAsync(c => c.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = user }, cancellationToken: ct));
            return resp.Customer.Id;
        }
        catch (BillingException e) when (e.StatusCode == 404)
        {
            return null;
        }
    }

    private static (string First, string Last) NamesFrom(string user)
    {
        var local = user.Split('@')[0];
        var parts = local.Split(new[] { '.', '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
        var first = parts.Length > 0 ? Cap(parts[0]) : "Shopper";
        var last = parts.Length > 1 ? Cap(string.Join(" ", parts.Skip(1))) : "Customer";
        return (first, last);
    }

    // ------------------------------------------------------------------ read / ownership

    /// <summary>Reads a subscription and refuses it unless it belongs to <paramref name="user"/>. A subscription
    /// that exists but belongs to somebody else is reported exactly like a missing one.</summary>
    private async Task<Subscription> LoadOwnedAsync(string user, int subscriptionId, CancellationToken ct)
    {
        Subscription? sub;
        try
        {
            sub = (await _maxio.ReadAsync(c => c.Subscriptions.ReadSubscription(
                new ReadSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct))).Subscription;
        }
        catch (BillingException e) when (e.StatusCode == 404)
        {
            throw BillingException.NotFound("Subscription not found.");
        }
        if (sub == null || !string.Equals(sub.Customer?.Reference, user, StringComparison.OrdinalIgnoreCase))
            throw BillingException.NotFound("Subscription not found.");
        return sub;
    }

    private async Task<Subscription?> ReReadAsync(int subscriptionId, CancellationToken ct) =>
        (await _maxio.ReadAsync(c => c.Subscriptions.ReadSubscription(
            new ReadSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct))).Subscription;

    public async Task<SubscriptionDto> GetSubscriptionAsync(string user, int subscriptionId, CancellationToken requestCt)
    {
        using var cts = Bound(requestCt);
        var sub = await LoadOwnedAsync(user, subscriptionId, cts.Token);
        return await ToDtoAsync(sub, cts.Token);
    }

    private async Task<SubscriptionDto> ToDtoAsync(Subscription sub, CancellationToken ct) => new()
    {
        Id = sub.Id ?? 0,
        State = sub.State?.Value,
        PlanHandle = sub.Product?.Handle,
        PlanName = sub.Product?.Name,
        Paid = await IsPaidAsync(sub, ct),
        BalanceInCents = sub.BalanceInCents ?? 0,
        Currency = sub.Currency,
        PaymentCollectionMethod = sub.PaymentCollectionMethod?.Value,
        NextBillingAt = sub.NextAssessmentAt,
        CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt,
    };

    /// <summary>Paid = the shopper owes nothing. Anything that cannot be decided is an error, never a guessed "true".</summary>
    private async Task<bool> IsPaidAsync(Subscription sub, CancellationToken ct)
    {
        if (sub.BalanceInCents == null)
            throw new BillingException(502, "Billing provider did not report the subscription balance.");
        if (sub.BalanceInCents > 0) return false;

        var site = await _site.GetAsync(_maxio, ct);
        if (!site.RelationshipInvoicing) return true;

        var balances = await _maxio.ReadAsync(c => c.SubscriptionInvoiceAccount.ReadAccountBalances(
            new ReadAccountBalancesRequest { SubscriptionId = sub.Id!.Value }, cancellationToken: ct));
        var open = balances.OpenInvoices?.BalanceInCents
            ?? throw new BillingException(502, "Billing provider did not report the open invoice balance.");
        return open <= 0;
    }

    // ------------------------------------------------------------------ pause / resume / cancel / restart

    public Task<SubscriptionDto> PauseAsync(string user, int id, CancellationToken requestCt) =>
        ChangeStateAsync(user, id, requestCt,
            guard: sub =>
            {
                if (sub.State != SubscriptionState.Active && sub.State != SubscriptionState.Trialing)
                    throw BillingException.Conflict($"A subscription that is '{sub.State?.Value}' cannot be paused; only active or trialing ones can.");
                if (sub.NextAssessmentAt != null && sub.NextAssessmentAt.Value - DateTimeOffset.UtcNow < TimeSpan.FromHours(24))
                    throw BillingException.Conflict("The subscription is due to be billed within 24 hours and cannot be paused until after that.");
            },
            call: (c, ct) => c.SubscriptionStatus.PauseSubscription(new PauseSubscriptionRequest { SubscriptionId = id }, cancellationToken: ct),
            reached: sub => sub.State == SubscriptionState.OnHold);

    public Task<SubscriptionDto> ResumeAsync(string user, int id, CancellationToken requestCt) =>
        ChangeStateAsync(user, id, requestCt,
            guard: sub =>
            {
                if (sub.State != SubscriptionState.OnHold)
                    throw BillingException.Conflict($"A subscription that is '{sub.State?.Value}' cannot be resumed; only paused (on hold) ones can.");
            },
            call: (c, ct) => c.SubscriptionStatus.ResumeSubscription(new ResumeSubscriptionRequest { SubscriptionId = id }, cancellationToken: ct),
            reached: sub => sub.State != SubscriptionState.OnHold);

    public Task<SubscriptionDto> CancelAsync(string user, int id, CancellationToken requestCt) =>
        ChangeStateAsync(user, id, requestCt,
            guard: sub =>
            {
                if (sub.State == SubscriptionState.Canceled)
                    throw BillingException.Conflict("The subscription is already cancelled.");
            },
            call: (c, ct) => c.SubscriptionStatus.CancelSubscription(new CancelSubscriptionRequest
            {
                SubscriptionId = id,
                Body = new CancellationRequest { Subscription = new CancellationOptions() },
            }, cancellationToken: ct),
            reached: sub => sub.State == SubscriptionState.Canceled);

    public Task<SubscriptionDto> RestartAsync(string user, int id, CancellationToken requestCt) =>
        ChangeStateAsync(user, id, requestCt,
            guard: sub =>
            {
                if (sub.State != SubscriptionState.Canceled)
                    throw BillingException.Conflict($"Only a cancelled subscription can be restarted; this one is '{sub.State?.Value}'.");
            },
            call: (c, ct) => c.SubscriptionStatus.ReactivateSubscription(new ReactivateSubscriptionOperationRequest { SubscriptionId = id }, cancellationToken: ct),
            reached: sub => sub.State != SubscriptionState.Canceled);

    private async Task<SubscriptionDto> ChangeStateAsync(string user, int id, CancellationToken requestCt,
        Action<Subscription> guard,
        Func<MaxioAdvancedBilling.MaxioAdvancedBillingClient, CancellationToken, Task<SubscriptionResponse>> call,
        Func<Subscription, bool> reached)
    {
        using var cts = Bound(requestCt);
        var ct = cts.Token;
        var current = await LoadOwnedAsync(user, id, ct);
        guard(current);
        var response = await _maxio.WriteAsync(c => call(c, ct), async () =>
        {
            var now = await ReReadAsync(id, ct);
            return now != null && reached(now) ? new SubscriptionResponse { Subscription = now } : null;
        });
        return await ToDtoAsync(response.Subscription ?? throw new BillingException(502, "Billing provider returned no subscription."), ct);
    }

    // ------------------------------------------------------------------ switch plan

    public async Task<SubscriptionDto> SwitchPlanAsync(string user, int id, string? planHandle, CancellationToken requestCt)
    {
        using var cts = Bound(requestCt);
        var ct = cts.Token;
        var current = await LoadOwnedAsync(user, id, ct);
        var plan = await RequireOfferedPlanAsync(planHandle, ct);
        if (string.Equals(current.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase))
            throw BillingException.Conflict("The subscription is already on that plan.");

        async Task<SubscriptionResponse?> Settle()
        {
            var now = await ReReadAsync(id, ct);
            return now != null && string.Equals(now.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase)
                ? new SubscriptionResponse { Subscription = now } : null;
        }

        SubscriptionResponse response;
        if (current.State == SubscriptionState.Active || current.State == SubscriptionState.Trialing)
        {
            response = await _maxio.WriteAsync(c => c.SubscriptionProducts.MigrateSubscriptionProduct(
                new MigrateSubscriptionProductRequest
                {
                    SubscriptionId = id,
                    Body = new SubscriptionProductMigrationRequest
                    {
                        Migration = new SubscriptionProductMigration { ProductHandle = plan.Handle }
                    }
                }, cancellationToken: ct), Settle);
        }
        else
        {
            // Migration is only for live subscriptions; every other state (cancelled included) takes a product change.
            response = await _maxio.WriteAsync(c => c.Subscriptions.UpdateSubscription(
                new UpdateSubscriptionOperationRequest
                {
                    SubscriptionId = id,
                    Body = new UpdateSubscriptionRequest
                    {
                        Subscription = new UpdateSubscription { ProductHandle = plan.Handle }
                    }
                }, cancellationToken: ct), Settle);
        }
        // The answer to a switch must show the new plan; confirm it from Maxio rather than from the call's echo.
        var after = await ReReadAsync(id, ct) ?? response.Subscription;
        if (after == null || !string.Equals(after.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase))
            throw new BillingException(502, "Billing provider accepted the switch but the subscription is not on the new plan.");
        return await ToDtoAsync(after, ct);
    }

    // ------------------------------------------------------------------ usage for a day

    public async Task<UsageResponse> GetUsageAsync(string user, int id, string? date, CancellationToken requestCt)
    {
        if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            throw BillingException.BadRequest("date is required in the form YYYY-MM-DD.");

        using var cts = Bound(requestCt);
        var ct = cts.Token;
        await LoadOwnedAsync(user, id, ct);

        var components = await _maxio.ReadAsync(c => c.SubscriptionComponents.ListSubscriptionComponents(
            new ListSubscriptionComponentsRequest { SubscriptionId = id }, cancellationToken: ct));

        var result = new UsageResponse { SubscriptionId = id, Date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        var since = new DateTimeOffset(day, TimeSpan.Zero);
        foreach (var item in components)
        {
            var comp = item.Component;
            if (comp?.ComponentId == null || comp.Kind != ComponentKind.MeteredComponent) continue;
            var componentId = comp.ComponentId.Value;
            var dto = new ComponentUsageDto { ComponentId = componentId, Handle = comp.ComponentHandle, Name = comp.Name };
            var complete = false;
            for (var page = 1; page <= MaxPages; page++)
            {
                var batch = await _maxio.ReadAsync(c => c.SubscriptionComponents.ListUsages(new ListUsagesRequest
                {
                    SubscriptionIdOrReference = id,
                    ComponentId = componentId,
                    SinceDate = since,
                    UntilDate = since.AddDays(1),
                    Page = page,
                    PerPage = PerPage,
                }, cancellationToken: ct));
                foreach (var u in batch.Select(b => b.Usage))
                {
                    // Maxio stamps usage in the site's time zone; the day asked for is that calendar day.
                    if (u.CreatedAt == null || u.CreatedAt.Value.Date != day.Date) continue;
                    var qty = QuantityOf(u.Quantity);
                    dto.Records.Add(new UsageRecordDto { Id = u.Id, Quantity = qty, Memo = u.Memo, RecordedAt = u.CreatedAt });
                    dto.TotalQuantity += qty;
                }
                if (batch.Count < PerPage) { complete = true; break; }
            }
            if (!complete) result.Truncated = true;
            result.Components.Add(dto);
        }
        return result;
    }

    private static decimal QuantityOf(MaxioAdvancedBilling.Models.AnyOf.Quantity1? q)
    {
        if (q == null) return 0;
        if (q.TryGetInt(out var i)) return i;
        if (q.TryGetString(out var s) && decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) return d;
        throw new BillingException(502, "Billing provider returned a usage quantity that could not be read.");
    }

    // ------------------------------------------------------------------ staff: plans

    public async Task<PlanDto> CreatePlanAsync(CreatePlanRequest request, CancellationToken requestCt)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) throw BillingException.BadRequest("name is required.");
        if (request.Price <= 0) throw BillingException.BadRequest("price must be greater than zero.");
        var cents = request.Price * 100m;
        if (cents != decimal.Truncate(cents) || cents > int.MaxValue)
            throw BillingException.BadRequest("price must be a whole number of cents.");
        var brand = await _brands.GetByIdAsync(request.BrandId, requestCt)
            ?? throw BillingException.BadRequest($"Brand {request.BrandId} is not one of the shop's catalog brands.");

        using var cts = Bound(requestCt);
        var ct = cts.Token;
        var handle = Slug(request.Name);
        if (handle.Length == 0) throw BillingException.BadRequest("name must contain letters or digits.");

        var claimKey = $"plan:{handle}";
        if (!await _claims.TryClaimAsync(claimKey, ct))
            throw BillingException.Conflict("A plan with that name is already being created.");
        try
        {
            if (await ProductByHandleAsync(handle, ct) != null)
                throw BillingException.Conflict($"A plan with handle '{handle}' already exists.");
            var familyId = await EnsureFamilyAsync(brand, ct);
            var created = await _maxio.WriteAsync(
                c => c.Products.CreateProduct(new CreateProductRequest
                {
                    ProductFamilyId = familyId.ToString(CultureInfo.InvariantCulture),
                    Body = new CreateOrUpdateProductRequest
                    {
                        Product = new CreateOrUpdateProduct
                        {
                            Name = request.Name.Trim(),
                            Handle = handle,
                            Description = request.Name.Trim(),
                            PriceInCents = (long)cents,
                            Interval = 1,
                            IntervalUnit = IntervalUnit.Month,
                        }
                    }
                }, cancellationToken: ct),
                async () =>
                {
                    var p = await ProductByHandleAsync(handle, ct);
                    return p == null ? null : new ProductResponse { Product = p };
                });
            var product = created.Product;
            return new PlanDto
            {
                Id = product.Id ?? 0,
                Handle = product.Handle ?? handle,
                Name = product.Name ?? request.Name.Trim(),
                Description = product.Description,
                Price = (product.PriceInCents ?? (long)cents) / 100m,
                Interval = "1 month",
                BrandId = brand.Id,
                Brand = brand.Brand,
            };
        }
        finally
        {
            await _claims.ReleaseAsync(claimKey);
        }
    }

    private async Task<Product?> ProductByHandleAsync(string handle, CancellationToken ct)
    {
        try
        {
            return (await _maxio.ReadAsync(c => c.Products.ReadProductByHandle(
                new ReadProductByHandleRequest { ApiHandle = handle }, cancellationToken: ct))).Product;
        }
        catch (BillingException e) when (e.StatusCode == 404)
        {
            return null;
        }
    }

    /// <summary>A brand is a Maxio product family; make the family on first use.</summary>
    private async Task<int> EnsureFamilyAsync(CatalogBrand brand, CancellationToken ct)
    {
        var handle = FamilyHandlePrefix + brand.Id;
        async Task<int?> Find()
        {
            var families = await _maxio.ReadAsync(c => c.ProductFamilies.ListProductFamilies(new ListProductFamiliesRequest(), cancellationToken: ct));
            var live = families.Select(f => f.ProductFamily).Where(f => f != null && f.ArchivedAt == null).ToList();
            // Prefer the family this app made for the brand over one that merely shares its name.
            return (live.FirstOrDefault(f => string.Equals(f!.Handle, handle, StringComparison.OrdinalIgnoreCase))
                ?? live.FirstOrDefault(f => string.Equals(f!.Name, brand.Brand, StringComparison.OrdinalIgnoreCase)))?.Id;
        }

        var existing = await Find();
        if (existing != null) return existing.Value;

        var claimKey = $"family:{brand.Id}";
        if (!await _claims.TryClaimAsync(claimKey, ct))
        {
            existing = await Find();
            return existing ?? throw BillingException.Conflict("The brand is being set up; try again in a moment.");
        }
        try
        {
            var created = await _maxio.WriteAsync(
                c => c.ProductFamilies.CreateProductFamily(new CreateProductFamilyOperationRequest
                {
                    Body = new CreateProductFamilyRequest
                    {
                        ProductFamily = new CreateProductFamily { Name = brand.Brand, Handle = handle }
                    }
                }, cancellationToken: ct),
                async () =>
                {
                    var id = await Find();
                    return id == null ? null : new ProductFamilyResponse { ProductFamily = new ProductFamily { Id = id, Handle = handle, Name = brand.Brand } };
                });
            return created.ProductFamily?.Id ?? throw new BillingException(502, "Billing provider returned no product family.");
        }
        finally
        {
            await _claims.ReleaseAsync(claimKey);
        }
    }

    private static string Slug(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name.Trim().ToLowerInvariant())
            sb.Append(char.IsAsciiLetterOrDigit(ch) ? ch : '-');
        return Regex.Replace(sb.ToString(), "-{2,}", "-").Trim('-');
    }

    // ------------------------------------------------------------------ staff: currency price

    public async Task<CurrencyPriceDto> SetCurrencyPriceAsync(int planId, CurrencyPriceRequest request, CancellationToken requestCt)
    {
        if (string.IsNullOrWhiteSpace(request.Currency)) throw BillingException.BadRequest("currency is required.");
        if (request.Price <= 0) throw BillingException.BadRequest("price must be greater than zero.");
        var currency = request.Currency.Trim().ToUpperInvariant();

        using var cts = Bound(requestCt);
        var ct = cts.Token;

        Product product;
        try
        {
            product = (await _maxio.ReadAsync(c => c.Products.ReadProduct(new ReadProductRequest { ProductId = planId }, cancellationToken: ct))).Product;
        }
        catch (BillingException e) when (e.StatusCode == 404)
        {
            throw BillingException.NotFound($"Plan {planId} not found.");
        }
        var brands = await _brands.ListAsync(ct);
        if (BrandFor(product.ProductFamily, brands) == null)
            throw BillingException.NotFound($"Plan {planId} not found.");

        var site = await _site.GetAsync(_maxio, ct);
        if (!site.NonPrimaryCurrencies.Contains(currency))
        {
            site = await _site.GetAsync(_maxio, ct, forceRefresh: true);
            if (!site.NonPrimaryCurrencies.Contains(currency))
                throw BillingException.BadRequest(site.NonPrimaryCurrencies.Count == 0
                    ? "The billing site has no other currencies defined."
                    : $"Currency '{currency}' is not defined on the billing site; defined: {string.Join(", ", site.NonPrimaryCurrencies)}.");
        }

        var pricePointId = product.DefaultProductPricePointId
            ?? throw new BillingException(502, "Billing provider returned no default price point for the plan.");

        async Task<IReadOnlyList<CurrencyPrice>> ReadPrices() =>
            (await _maxio.ReadAsync(c => c.ProductPricePoints.ReadProductPricePoint(new ReadProductPricePointRequest
            {
                ProductId = planId,
                PricePointId = pricePointId,
                CurrencyPrices = true,
            }, cancellationToken: ct))).PricePoint.CurrencyPrices ?? new List<CurrencyPrice>();

        var existing = (await ReadPrices()).FirstOrDefault(p => string.Equals(p.Currency, currency, StringComparison.OrdinalIgnoreCase));

        async Task<CurrencyPricesResponse?> Settle()
        {
            var found = (await ReadPrices()).Where(p => string.Equals(p.Currency, currency, StringComparison.OrdinalIgnoreCase)).ToList();
            return found.Count == 0 || (decimal?)found[0].Price != request.Price ? null : new CurrencyPricesResponse { CurrencyPrices = found };
        }

        if (existing?.Id != null)
        {
            await _maxio.WriteAsync(c => c.ProductPricePoints.UpdateProductCurrencyPrices(new UpdateProductCurrencyPricesRequest
            {
                ProductPricePointId = pricePointId,
                Body = new UpdateCurrencyPricesRequest
                {
                    CurrencyPrices = new[] { new UpdateCurrencyPrice { Id = existing.Id.Value, Price = (double)request.Price } }
                }
            }, cancellationToken: ct), Settle);
        }
        else
        {
            // The create call takes a whole-number price; a fractional one is created at the rounded value, then set exactly.
            await _maxio.WriteAsync(c => c.ProductPricePoints.CreateProductCurrencyPrices(new CreateProductCurrencyPricesOperationRequest
            {
                ProductPricePointId = pricePointId,
                Body = new CreateProductCurrencyPricesRequest
                {
                    CurrencyPrices = new[]
                    {
                        new CreateProductCurrencyPrice
                        {
                            Currency = currency,
                            Price = (int)Math.Round(request.Price, MidpointRounding.AwayFromZero),
                            Role = CurrencyPriceRole.Baseline,
                        }
                    }
                }
            }, cancellationToken: ct), async () =>
            {
                // Settled when a price in this currency exists, whatever its (whole-number) value.
                var found = (await ReadPrices()).Where(p => string.Equals(p.Currency, currency, StringComparison.OrdinalIgnoreCase)).ToList();
                return found.Count == 0 ? null : new CurrencyPricesResponse { CurrencyPrices = found };
            });
            if (request.Price != decimal.Truncate(request.Price))
            {
                var created = (await ReadPrices()).First(p => string.Equals(p.Currency, currency, StringComparison.OrdinalIgnoreCase));
                await _maxio.WriteAsync(c => c.ProductPricePoints.UpdateProductCurrencyPrices(new UpdateProductCurrencyPricesRequest
                {
                    ProductPricePointId = pricePointId,
                    Body = new UpdateCurrencyPricesRequest
                    {
                        CurrencyPrices = new[] { new UpdateCurrencyPrice { Id = created.Id!.Value, Price = (double)request.Price } }
                    }
                }, cancellationToken: ct), Settle);
            }
        }

        // Verify what Maxio now holds equals what staff asked for.
        var final = (await ReadPrices()).FirstOrDefault(p => string.Equals(p.Currency, currency, StringComparison.OrdinalIgnoreCase));
        if (final?.Price == null || (decimal)final.Price.Value != request.Price)
            throw new BillingException(502, $"Billing provider stored a different price ({final?.FormattedPrice ?? "none"}) than requested.");
        return new CurrencyPriceDto { PlanId = planId, Currency = currency, Price = (decimal)final.Price.Value, FormattedPrice = final.FormattedPrice };
    }

    // ------------------------------------------------------------------ staff: next billing date

    public async Task<SubscriptionDto> SetNextBillingDateAsync(int id, string? date, CancellationToken requestCt)
    {
        if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            throw BillingException.BadRequest("date is required in the form YYYY-MM-DD.");

        using var cts = Bound(requestCt);
        var ct = cts.Token;

        Subscription sub;
        try
        {
            sub = (await ReReadAsync(id, ct)) ?? throw BillingException.NotFound("Subscription not found.");
        }
        catch (BillingException e) when (e.StatusCode == 404)
        {
            throw BillingException.NotFound("Subscription not found.");
        }

        // Billing only happens on a date if the subscription still renews.
        if (sub.State != SubscriptionState.Active && sub.State != SubscriptionState.Trialing)
            throw BillingException.Conflict($"A subscription that is '{sub.State?.Value}' is not billed on a schedule; its next billing date cannot be set.");
        var offset = sub.NextAssessmentAt?.Offset ?? sub.CurrentPeriodEndsAt?.Offset ?? TimeSpan.Zero;
        var today = DateTimeOffset.UtcNow.ToOffset(offset).Date;
        if (day.Date <= today)
            throw BillingException.BadRequest("The next billing date must be in the future.");
        if (sub.ExpiresAt != null && sub.ExpiresAt.Value.Date <= day.Date)
            throw BillingException.Conflict("The subscription expires before that date, so it would never be billed on it.");

        // Noon UTC on the date lands on the same calendar day in every site time zone from UTC-12 to UTC+11.
        var target = new DateTimeOffset(day.Date.AddHours(12), TimeSpan.Zero);
        async Task<SubscriptionResponse?> Settle()
        {
            var now = await ReReadAsync(id, ct);
            return now != null && MatchesDate(now, day) ? new SubscriptionResponse { Subscription = now } : null;
        }

        await _maxio.WriteAsync(c => c.Subscriptions.UpdateSubscription(new UpdateSubscriptionOperationRequest
        {
            SubscriptionId = id,
            Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { NextBillingAt = target } }
        }, cancellationToken: ct), Settle);

        // The update's own answer does not carry the new date; read it back and confirm.
        var after = await ReReadAsync(id, ct) ?? throw new BillingException(502, "Billing provider returned no subscription.");
        if (!MatchesDate(after, day))
            throw new BillingException(502, "Billing provider accepted the change but the subscription's next billing date is not the requested one.");
        return await ToDtoAsync(after, ct);
    }

    private static bool MatchesDate(Subscription sub, DateTime day) =>
        sub.NextAssessmentAt?.Date == day.Date || sub.CurrentPeriodEndsAt?.Date == day.Date;
}
