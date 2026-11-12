using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.AnyOf;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.ProductPricePoints;
using MaxioAdvancedBilling.Requests.Products;
using MaxioAdvancedBilling.Requests.SubscriptionComponents;
using MaxioAdvancedBilling.Requests.SubscriptionInvoiceAccount;
using MaxioAdvancedBilling.Requests.Subscriptions;
using MaxioAdvancedBilling.Requests.SubscriptionStatus;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Subscription billing on top of Maxio (system of record). Every Maxio call goes through <see cref="Call{T}"/>,
/// which turns SDK failures into a <see cref="BillingException"/> carrying a caller-safe status and reason.
/// </summary>
public class BillingService
{
    private const int PAGE_SIZE = 200;
    private const int MAX_PAGES = 50;
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(60);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly BillingClaims _claims;
    private readonly IReadRepository<CatalogBrand> _brands;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ILogger<BillingService> _logger;

    public BillingService(MaxioAdvancedBillingClient client, BillingClaims claims, IReadRepository<CatalogBrand> brands,
        UserManager<ApplicationUser> users, ILogger<BillingService> logger)
    {
        _client = client;
        _claims = claims;
        _brands = brands;
        _users = users;
        _logger = logger;
    }

    // ---------------------------------------------------------------- plans

    public Task<ListPlansResponse> ListPlansAsync(CancellationToken ct) => Bounded(ct, async token =>
    {
        var (products, truncated) = await ReadPlansAsync(token);
        return new ListPlansResponse { Plans = products.Select(ToPlan).ToList(), Truncated = truncated };
    });

    // ---------------------------------------------------------------- subscribe

    public Task<SubscriptionDto> SubscribeAsync(string userName, string? planHandle, CancellationToken ct) => Bounded(ct, async token =>
    {
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new BillingException(400, "planHandle is required.");
        var plan = await RequirePlanAsync(planHandle, token);

        await using var claim = await _claims.ClaimAsync($"subscribe:{userName.ToLowerInvariant()}:{plan.Handle}",
            "A subscribe request for this plan is already in progress.");

        var site = (await Call(() => _client.Sites.ReadSite(cancellationToken: token))).Site;
        // No card is collected at sign-up: bill by invoice/remittance rather than automatic card payment.
        var collection = site.RelationshipInvoicingEnabled == true ? CollectionMethod.Remittance : CollectionMethod.Invoice;

        var customerId = await FindOrCreateCustomerAsync(userName, token);

        var reference = Guid.NewGuid().ToString("N");
        var request = new CreateSubscriptionOperationRequest
        {
            Body = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = plan.Handle,
                    PaymentCollectionMethod = collection,
                    Reference = reference
                }
            }
        };

        Subscription created;
        try
        {
            created = (await Call(() => _client.Subscriptions.CreateSubscription(request, cancellationToken: token))).Subscription!;
        }
        catch (BillingException ex) when (ex.StatusCode is 504 or 502)
        {
            // The write may have landed: settle by looking the subscription up by the reference we sent.
            var found = await TryFindByReferenceAsync(reference);
            if (found == null) throw;
            created = found;
        }

        _logger.LogInformation("Subscription {SubscriptionId} created for {User} on plan {Plan}", created.Id, userName, plan.Handle);
        return await ToSubscriptionDtoAsync(created, token);
    });

    private async Task<Subscription?> TryFindByReferenceAsync(string reference)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var found = await _client.Subscriptions.FindSubscription(new FindSubscriptionRequest { Reference = reference },
                cancellationToken: cts.Token);
            return found.Subscription;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<int> FindOrCreateCustomerAsync(string userName, CancellationToken token)
    {
        var existing = await TryReadCustomerAsync(userName, token);
        if (existing != null) return existing.Value;

        var user = await _users.FindByNameAsync(userName);
        var email = user?.Email;
        if (string.IsNullOrWhiteSpace(email))
            throw new BillingException(422, "Your account has no email address, so a billing customer cannot be created.");
        var local = email.Split('@')[0];

        IAsyncDisposable claim;
        try
        {
            claim = await _claims.ClaimAsync($"customer:{userName.ToLowerInvariant()}", "Your billing customer is being created.");
        }
        catch (BillingException ex) when (ex.StatusCode == 409)
        {
            // Another request is creating it: wait for it and re-read.
            for (var i = 0; i < 5; i++)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), token);
                var other = await TryReadCustomerAsync(userName, token);
                if (other != null) return other.Value;
            }
            throw;
        }

        await using (claim)
        {
            // Re-check under the claim.
            var again = await TryReadCustomerAsync(userName, token);
            if (again != null) return again.Value;
            try
            {
                var created = await Call(() => _client.Customers.CreateCustomer(new CreateCustomerOperationRequest
                {
                    Body = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = local,
                            LastName = "Shopper",
                            Email = email,
                            Reference = userName
                        }
                    }
                }, cancellationToken: token));
                return created.Customer!.Id!.Value;
            }
            catch (BillingException ex) when (ex.StatusCode == 504 || ex.StatusCode == 502)
            {
                var settled = await TryReadCustomerAsync(userName, token);
                if (settled != null) return settled.Value;
                throw;
            }
        }
    }

    private async Task<int?> TryReadCustomerAsync(string userName, CancellationToken token)
    {
        try
        {
            var found = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = userName }, cancellationToken: token);
            return found.Customer?.Id;
        }
        catch (ApiException ex) when ((int)ex.StatusCode == 404)
        {
            return null;
        }
        catch (Exception ex) when (ex is ApiException or SdkException)
        {
            throw Translate(ex);
        }
    }

    // ---------------------------------------------------------------- read / status

    public Task<SubscriptionDto> GetSubscriptionAsync(string userName, int subscriptionId, CancellationToken ct) => Bounded(ct, async token =>
    {
        var sub = await OwnedSubscriptionAsync(userName, subscriptionId, token);
        return await ToSubscriptionDtoAsync(sub, token);
    });

    public Task<SubscriptionDto> PauseAsync(string userName, int subscriptionId, CancellationToken ct) =>
        ChangeStatusAsync(userName, subscriptionId, ct, "pause",
            sub => sub.State == SubscriptionState.Active || sub.State == SubscriptionState.Trialing,
            "Only an active or trialing subscription can be paused.",
            token => Call(() => _client.SubscriptionStatus.PauseSubscription(
                new PauseSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: token)),
            sub => sub.State == SubscriptionState.OnHold || sub.State == SubscriptionState.Paused);

    public Task<SubscriptionDto> ResumeAsync(string userName, int subscriptionId, CancellationToken ct) =>
        ChangeStatusAsync(userName, subscriptionId, ct, "resume",
            sub => sub.State == SubscriptionState.OnHold || sub.State == SubscriptionState.Paused,
            "Only a paused subscription can be resumed.",
            token => Call(() => _client.SubscriptionStatus.ResumeSubscription(
                new ResumeSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: token)),
            sub => sub.State != SubscriptionState.OnHold && sub.State != SubscriptionState.Paused);

    public Task<SubscriptionDto> CancelAsync(string userName, int subscriptionId, CancellationToken ct) =>
        ChangeStatusAsync(userName, subscriptionId, ct, "cancel",
            sub => sub.State != SubscriptionState.Canceled && sub.State != SubscriptionState.Expired,
            "The subscription is already cancelled.",
            token => Call(() => _client.SubscriptionStatus.CancelSubscription(
                new CancelSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: token)),
            sub => sub.State == SubscriptionState.Canceled);

    public Task<SubscriptionDto> RestartAsync(string userName, int subscriptionId, CancellationToken ct) =>
        ChangeStatusAsync(userName, subscriptionId, ct, "restart",
            sub => sub.State == SubscriptionState.Canceled,
            "Only a cancelled subscription can be restarted.",
            token => Call(() => _client.SubscriptionStatus.ReactivateSubscription(
                new ReactivateSubscriptionOperationRequest
                {
                    SubscriptionId = subscriptionId,
                    Body = new ReactivateSubscriptionRequest { Resume = Resume.Bool(true) }
                }, cancellationToken: token)),
            sub => sub.State != SubscriptionState.Canceled);

    private Task<SubscriptionDto> ChangeStatusAsync(string userName, int subscriptionId, CancellationToken ct, string action,
        Func<Subscription, bool> allowed, string notAllowedReason,
        Func<CancellationToken, Task<SubscriptionResponse>> write, Func<Subscription, bool> landed) => Bounded(ct, async token =>
    {
        await using var claim = await _claims.ClaimAsync($"subscription:{subscriptionId}",
            "Another change to this subscription is in progress.");

        var sub = await OwnedSubscriptionAsync(userName, subscriptionId, token);
        if (!allowed(sub))
            throw new BillingException(409, $"{notAllowedReason} The subscription is {sub.State?.Value ?? "in an unknown state"}.");

        Subscription result;
        try
        {
            result = (await write(token)).Subscription!;
        }
        catch (BillingException ex) when (ex.StatusCode == 504 || ex.StatusCode == 502)
        {
            // Outcome unknown: re-read and see whether the change landed.
            var reread = await TryReadAsync(subscriptionId);
            if (reread == null || !landed(reread)) throw;
            result = reread;
        }

        _logger.LogInformation("Subscription {SubscriptionId} {Action} by {User}", subscriptionId, action, userName);
        return await ToSubscriptionDtoAsync(result, token);
    });

    // ---------------------------------------------------------------- switch

    public Task<SubscriptionDto> SwitchPlanAsync(string userName, int subscriptionId, string? planHandle, CancellationToken ct) => Bounded(ct, async token =>
    {
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new BillingException(400, "planHandle is required.");
        var plan = await RequirePlanAsync(planHandle, token);

        await using var claim = await _claims.ClaimAsync($"subscription:{subscriptionId}",
            "Another change to this subscription is in progress.");

        var sub = await OwnedSubscriptionAsync(userName, subscriptionId, token);
        if (string.Equals(sub.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase))
            throw new BillingException(409, "The subscription is already on that plan.");

        Subscription result;
        try
        {
            result = (await Call(() => _client.Subscriptions.UpdateSubscription(new UpdateSubscriptionOperationRequest
            {
                SubscriptionId = subscriptionId,
                Body = new UpdateSubscriptionRequest
                {
                    Subscription = new UpdateSubscription { ProductHandle = plan.Handle }
                }
            }, cancellationToken: token))).Subscription!;
        }
        catch (BillingException ex) when (ex.StatusCode == 504 || ex.StatusCode == 502)
        {
            var reread = await TryReadAsync(subscriptionId);
            if (reread == null || !string.Equals(reread.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase)) throw;
            result = reread;
        }

        // Make sure the shopper really is on the new plan now (a delayed change would not be).
        if (!string.Equals(result.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase))
        {
            var reread = await OwnedSubscriptionAsync(userName, subscriptionId, token);
            if (!string.Equals(reread.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase))
                throw new BillingException(409, "The plan change could not be applied to the subscription.");
            result = reread;
        }

        _logger.LogInformation("Subscription {SubscriptionId} switched to {Plan} by {User}", subscriptionId, plan.Handle, userName);
        return await ToSubscriptionDtoAsync(result, token);
    });

    // ---------------------------------------------------------------- usage

    public Task<UsageResponse> GetUsageAsync(string userName, int subscriptionId, string? date, CancellationToken ct) => Bounded(ct, async token =>
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            throw new BillingException(400, "date is required, formatted YYYY-MM-DD.");

        await OwnedSubscriptionAsync(userName, subscriptionId, token);

        var components = await Call(() => _client.SubscriptionComponents.ListSubscriptionComponents(
            new ListSubscriptionComponentsRequest { SubscriptionId = subscriptionId }, cancellationToken: token));

        var response = new UsageResponse { Date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        foreach (var item in components)
        {
            var component = item.Component;
            if (component?.ComponentId == null) continue;
            if (component.Kind != ComponentKind.MeteredComponent && component.Kind != ComponentKind.PrepaidUsageComponent) continue;

            var dto = new ComponentUsageDto
            {
                ComponentId = component.ComponentId.Value,
                ComponentHandle = component.ComponentHandle,
                Name = component.Name,
                UnitName = component.UnitName
            };

            // The site's time zone is not known here, so ask for a wider window and keep the rows whose own
            // (site-local) calendar date is the requested day.
            var since = new DateTimeOffset(day.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var until = new DateTimeOffset(day.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var page = 1;
            for (; page <= MAX_PAGES; page++)
            {
                var p = page;
                var usages = await Call(() => _client.SubscriptionComponents.ListUsages(new ListUsagesRequest
                {
                    SubscriptionIdOrReference = SubscriptionIdOrReference.Int(subscriptionId),
                    ComponentId = ComponentIdModel.Int(component.ComponentId.Value),
                    SinceDate = since,
                    UntilDate = until,
                    Page = p,
                    PerPage = PAGE_SIZE
                }, cancellationToken: token));

                foreach (var u in usages.Select(x => x.Usage).Where(x => x?.CreatedAt != null))
                {
                    if (DateOnly.FromDateTime(u!.CreatedAt!.Value.DateTime) != day) continue;
                    var quantity = ReadQuantity(u.Quantity);
                    dto.Entries.Add(new UsageEntryDto { Id = u.Id, CreatedAt = u.CreatedAt, Quantity = quantity, Memo = u.Memo });
                    dto.Quantity += quantity;
                }
                if (usages.Count < PAGE_SIZE) break;
            }
            if (page > MAX_PAGES) { dto.Truncated = true; response.Truncated = true; }
            response.Components.Add(dto);
        }
        return response;
    });

    private static decimal ReadQuantity(Quantity1? q)
    {
        if (q == null) return 0;
        if (q.TryGetInt(out var i)) return i;
        if (q.TryGetString(out var s) && decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return d;
        return 0;
    }

    // ---------------------------------------------------------------- admin: plans

    public Task<PlanDto> CreatePlanAsync(string? name, decimal monthlyPrice, string? brand, CancellationToken ct) => Bounded(ct, async token =>
    {
        if (string.IsNullOrWhiteSpace(name)) throw new BillingException(400, "name is required.");
        if (string.IsNullOrWhiteSpace(brand)) throw new BillingException(400, "brand is required.");
        if (monthlyPrice < 0) throw new BillingException(400, "monthlyPrice cannot be negative.");
        var cents = decimal.Round(monthlyPrice * 100m, 0, MidpointRounding.AwayFromZero);
        if (cents != monthlyPrice * 100m) throw new BillingException(400, "monthlyPrice can have at most two decimal places.");
        name = name.Trim();

        // The brand must be one of the shop's catalog brands.
        var catalogBrands = await _brands.ListAsync(token);
        var catalogBrand = catalogBrands.FirstOrDefault(b => string.Equals(b.Brand, brand.Trim(), StringComparison.OrdinalIgnoreCase));
        if (catalogBrand == null)
            throw new BillingException(422, $"Unknown brand '{brand.Trim()}'. Brands in the catalog: {string.Join(", ", catalogBrands.Select(b => b.Brand))}.");

        await using var claim = await _claims.ClaimAsync($"plan:{catalogBrand.Brand.ToLowerInvariant()}:{name.ToLowerInvariant()}",
            "A plan with this name and brand is already being created.");

        var handle = Slug(name);
        if (handle.Length == 0) throw new BillingException(400, "name must contain letters or digits.");

        var (existing, _) = await ReadPlansAsync(token);
        if (existing.Any(p => string.Equals(p.Handle, handle, StringComparison.OrdinalIgnoreCase)))
            throw new BillingException(409, $"A plan with handle '{handle}' already exists.");

        var familyId = await FindOrCreateFamilyAsync(catalogBrand.Brand, token);

        Product product;
        try
        {
            product = (await Call(() => _client.Products.CreateProduct(new CreateProductRequest
            {
                ProductFamilyId = familyId.ToString(CultureInfo.InvariantCulture),
                Body = new CreateOrUpdateProductRequest
                {
                    Product = new CreateOrUpdateProduct
                    {
                        Name = name,
                        Description = name,
                        PriceInCents = (long)cents,
                        Interval = 1,
                        IntervalUnit = IntervalUnit.Month,
                        Handle = handle
                    }
                }
            }, cancellationToken: token))).Product!;
        }
        catch (BillingException ex) when (ex.StatusCode == 504 || ex.StatusCode == 502)
        {
            var found = await TryReadProductByHandleAsync(handle);
            if (found == null) throw;
            product = found;
        }

        _logger.LogInformation("Plan {Handle} created in brand {Brand}", handle, catalogBrand.Brand);
        return ToPlan(product);
    });

    private async Task<Product?> TryReadProductByHandleAsync(string handle)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var found = await _client.Products.ReadProductByHandle(new ReadProductByHandleRequest { ApiHandle = handle },
                cancellationToken: cts.Token);
            return found.Product;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<int> FindOrCreateFamilyAsync(string brand, CancellationToken token)
    {
        async Task<int?> Find()
        {
            var families = await Call(() => _client.ProductFamilies.ListProductFamilies(new ListProductFamiliesRequest(), cancellationToken: token));
            return families.Select(f => f.ProductFamily)
                .FirstOrDefault(f => f != null && f.ArchivedAt == null && string.Equals(f.Name, brand, StringComparison.OrdinalIgnoreCase))?.Id;
        }

        var id = await Find();
        if (id != null) return id.Value;

        IAsyncDisposable claim;
        try
        {
            claim = await _claims.ClaimAsync($"family:{brand.ToLowerInvariant()}", "The brand's product family is being created.");
        }
        catch (BillingException ex) when (ex.StatusCode == 409)
        {
            for (var i = 0; i < 5; i++)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), token);
                id = await Find();
                if (id != null) return id.Value;
            }
            throw;
        }

        await using (claim)
        {
            id = await Find();
            if (id != null) return id.Value;
            try
            {
                var created = await Call(() => _client.ProductFamilies.CreateProductFamily(new CreateProductFamilyOperationRequest
                {
                    Body = new CreateProductFamilyRequest
                    {
                        ProductFamily = new CreateProductFamily { Name = brand, Handle = Slug(brand) }
                    }
                }, cancellationToken: token));
                return created.ProductFamily!.Id!.Value;
            }
            catch (BillingException ex) when (ex.StatusCode == 504 || ex.StatusCode == 502)
            {
                id = await Find();
                if (id != null) return id.Value;
                throw;
            }
        }
    }

    // ---------------------------------------------------------------- admin: currency prices

    public Task<PlanCurrencyPricesResponse> SetCurrencyPriceAsync(int planId, string? currency, decimal price, CancellationToken ct) => Bounded(ct, async token =>
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
            throw new BillingException(400, "currency must be a three-letter ISO code.");
        if (price <= 0) throw new BillingException(400, "price must be greater than zero.");
        currency = currency.Trim().ToUpperInvariant();

        var product = (await Call(() => _client.Products.ReadProduct(new ReadProductRequest { ProductId = planId }, cancellationToken: token))).Product
            ?? throw new BillingException(404, "Plan not found.");
        var pricePointId = product.DefaultProductPricePointId
            ?? throw new BillingException(422, "The plan has no price point to price in another currency.");

        var site = (await Call(() => _client.Sites.ReadSite(cancellationToken: token))).Site;
        if (string.Equals(site.Currency, currency, StringComparison.OrdinalIgnoreCase))
            throw new BillingException(422, $"{currency} is the shop's main currency; its price is the plan's monthly price.");
        var allowed = site.NonPrimaryCurrencies ?? Array.Empty<string>();
        if (!allowed.Contains(currency, StringComparer.OrdinalIgnoreCase))
            throw new BillingException(422, $"{currency} is not a currency this shop is set up for. Available: {string.Join(", ", allowed)}.");

        await using var claim = await _claims.ClaimAsync($"currency:{planId}:{currency}", "A price for this plan and currency is already being set.");

        Task<List<CurrencyPrice>> ReadPrices() => ReadCurrencyPricesAsync(planId, pricePointId, token);

        var current = await ReadPrices();
        var existing = current.FirstOrDefault(c => string.Equals(c.Currency, currency, StringComparison.OrdinalIgnoreCase));

        try
        {
            if (existing?.Id != null)
            {
                await Call(() => _client.ProductPricePoints.UpdateProductCurrencyPrices(new UpdateProductCurrencyPricesRequest
                {
                    ProductPricePointId = pricePointId,
                    Body = new UpdateCurrencyPricesRequest
                    {
                        CurrencyPrices = new[] { new UpdateCurrencyPrice { Id = existing.Id.Value, Price = (double)price } }
                    }
                }, cancellationToken: token));
            }
            else
            {
                var whole = (int)Math.Floor(price);
                await Call(() => _client.ProductPricePoints.CreateProductCurrencyPrices(new CreateProductCurrencyPricesOperationRequest
                {
                    ProductPricePointId = pricePointId,
                    Body = new CreateProductCurrencyPricesRequest
                    {
                        CurrencyPrices = new[] { new CreateProductCurrencyPrice { Currency = currency, Price = whole, Role = CurrencyPriceRole.Baseline } }
                    }
                }, cancellationToken: token));

                if (whole != price)
                {
                    // Creation only takes whole units; set the exact price on the record just created.
                    var created = (await ReadPrices()).First(c => string.Equals(c.Currency, currency, StringComparison.OrdinalIgnoreCase));
                    await Call(() => _client.ProductPricePoints.UpdateProductCurrencyPrices(new UpdateProductCurrencyPricesRequest
                    {
                        ProductPricePointId = pricePointId,
                        Body = new UpdateCurrencyPricesRequest
                        {
                            CurrencyPrices = new[] { new UpdateCurrencyPrice { Id = created.Id!.Value, Price = (double)price } }
                        }
                    }, cancellationToken: token));
                }
            }
        }
        catch (BillingException ex) when (ex.StatusCode == 504 || ex.StatusCode == 502)
        {
            var after = await ReadPrices();
            var match = after.FirstOrDefault(c => string.Equals(c.Currency, currency, StringComparison.OrdinalIgnoreCase));
            if (match?.Price == null || (decimal)match.Price.Value != price) throw;
        }

        var all = await ReadPrices();
        return new PlanCurrencyPricesResponse
        {
            PlanId = planId,
            CurrencyPrices = all.Where(c => c.Currency != null && c.Price != null)
                .Select(c => new CurrencyPriceDto { Currency = c.Currency, Price = (decimal)c.Price!.Value }).ToList()
        };
    });

    private async Task<List<CurrencyPrice>> ReadCurrencyPricesAsync(int planId, int pricePointId, CancellationToken token)
    {
        var pp = await Call(() => _client.ProductPricePoints.ReadProductPricePoint(new ReadProductPricePointRequest
        {
            ProductId = ProductIdModel.Int(planId),
            PricePointId = PricePointIdModel.Int(pricePointId),
            CurrencyPrices = true
        }, cancellationToken: token));
        return pp.PricePoint.CurrencyPrices?.ToList() ?? new List<CurrencyPrice>();
    }

    // ---------------------------------------------------------------- admin: next billing date

    public Task<SubscriptionDto> SetNextBillingDateAsync(int subscriptionId, string? date, CancellationToken ct) => Bounded(ct, async token =>
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            throw new BillingException(400, "date is required, formatted YYYY-MM-DD.");
        if (day <= DateOnly.FromDateTime(DateTime.UtcNow))
            throw new BillingException(422, "The next billing date must be in the future.");

        await using var claim = await _claims.ClaimAsync($"subscription:{subscriptionId}",
            "Another change to this subscription is in progress.");

        var sub = await ReadSubscriptionAsync(subscriptionId, token);
        if (sub.State == SubscriptionState.Canceled || sub.State == SubscriptionState.Expired)
            throw new BillingException(409, "A cancelled subscription is not billed.");

        // Noon UTC keeps the same calendar date for any site time zone from UTC-12 to UTC+11.
        var at = new DateTimeOffset(day.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        Subscription updated;
        try
        {
            updated = (await Call(() => _client.Subscriptions.UpdateSubscription(new UpdateSubscriptionOperationRequest
            {
                SubscriptionId = subscriptionId,
                Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { NextBillingAt = at } }
            }, cancellationToken: token))).Subscription!;
        }
        catch (BillingException ex) when (ex.StatusCode == 504 || ex.StatusCode == 502)
        {
            var reread = await TryReadAsync(subscriptionId);
            if (reread?.CurrentPeriodEndsAt == null || DateOnly.FromDateTime(reread.CurrentPeriodEndsAt.Value.DateTime) != day) throw;
            updated = reread;
        }

        _logger.LogInformation("Subscription {SubscriptionId} next billing date set to {Date}", subscriptionId, day);
        return await ToSubscriptionDtoAsync(updated, token);
    });

    // ---------------------------------------------------------------- helpers

    private async Task<(List<Product> Products, bool Truncated)> ReadPlansAsync(CancellationToken token)
    {
        var all = new List<Product>();
        for (var page = 1; page <= MAX_PAGES; page++)
        {
            var p = page;
            var products = await Call(() => _client.Products.ListProducts(new ListProductsRequest { Page = p, PerPage = PAGE_SIZE },
                cancellationToken: token));
            all.AddRange(products.Select(x => x.Product).Where(x => x != null && x.ArchivedAt == null)!);
            if (products.Count < PAGE_SIZE) return (all, false);
        }
        return (all, true);
    }

    /// <summary>The plan the shopper asked for, which must be one the shop offers (a non-archived product).</summary>
    private async Task<Product> RequirePlanAsync(string handle, CancellationToken token)
    {
        var (plans, truncated) = await ReadPlansAsync(token);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, handle.Trim(), StringComparison.OrdinalIgnoreCase));
        if (plan != null) return plan;
        if (truncated) throw new BillingException(503, "The plan catalog is too large to verify the plan right now.");
        throw new BillingException(422, $"'{handle}' is not a plan you can subscribe to.");
    }

    private async Task<Subscription> ReadSubscriptionAsync(int subscriptionId, CancellationToken token)
    {
        try
        {
            return (await Call(() => _client.Subscriptions.ReadSubscription(
                new ReadSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: token))).Subscription!;
        }
        catch (BillingException ex) when (ex.StatusCode == 404)
        {
            throw new BillingException(404, "Subscription not found.");
        }
    }

    private async Task<Subscription?> TryReadAsync(int subscriptionId)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            return (await _client.Subscriptions.ReadSubscription(new ReadSubscriptionRequest { SubscriptionId = subscriptionId },
                cancellationToken: cts.Token)).Subscription;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The subscription, if it belongs to the caller; otherwise the same 404 as a missing one.</summary>
    private async Task<Subscription> OwnedSubscriptionAsync(string userName, int subscriptionId, CancellationToken token)
    {
        var sub = await ReadSubscriptionAsync(subscriptionId, token);
        if (!string.Equals(sub.Customer?.Reference, userName, StringComparison.OrdinalIgnoreCase))
            throw new BillingException(404, "Subscription not found.");
        return sub;
    }

    private async Task<SubscriptionDto> ToSubscriptionDtoAsync(Subscription sub, CancellationToken token)
    {
        var openInvoices = 0L;
        if (sub.Id != null)
        {
            var balances = await Call(() => _client.SubscriptionInvoiceAccount.ReadAccountBalances(
                new ReadAccountBalancesRequest { SubscriptionId = sub.Id.Value }, cancellationToken: token));
            openInvoices = balances.OpenInvoices?.BalanceInCents ?? 0;
        }

        // The subscription balance already covers its open invoices; take the larger so nothing is counted twice.
        var owedCents = Math.Max(Math.Max(sub.BalanceInCents ?? 0, 0), Math.Max(openInvoices, 0));
        var behind = sub.State == SubscriptionState.PastDue || sub.State == SubscriptionState.Unpaid || sub.State == SubscriptionState.SoftFailure;
        return new SubscriptionDto
        {
            Id = sub.Id ?? 0,
            State = sub.State?.Value,
            PlanHandle = sub.Product?.Handle,
            PlanName = sub.Product?.Name,
            Paid = owedCents == 0 && !behind,
            BalanceDue = owedCents / 100m,
            CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt,
            CanceledAt = sub.CanceledAt
        };
    }

    private static PlanDto ToPlan(Product p) => new()
    {
        Id = p.Id ?? 0,
        Handle = p.Handle ?? string.Empty,
        Name = p.Name ?? string.Empty,
        Brand = p.ProductFamily?.Name,
        MonthlyPrice = (p.PriceInCents ?? 0) / 100m
    };

    private static string Slug(string value)
    {
        var sb = new StringBuilder();
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch)) sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }

    /// <summary>Runs the work under one total budget, linked to the caller's cancellation.</summary>
    private static async Task<T> Bounded<T>(CancellationToken ct, Func<CancellationToken, Task<T>> work)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Budget);
        try
        {
            return await work(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new BillingException(504, "The billing system did not answer in time.");
        }
    }

    // ---------------------------------------------------------------- error translation

    private static async Task<T> Call<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (Exception ex) when (ex is SdkException)
        {
            throw Translate(ex);
        }
    }

    private static BillingException Translate(Exception ex)
    {
        switch (ex)
        {
            case ResponseDeserializationException rd:
                return new BillingException(502, "The billing system returned a response that could not be processed.", rd);
            case ApiException api:
                var status = (int)api.StatusCode;
                var detail = Detail(api);
                if (status == 404) return new BillingException(404, "Not found in the billing system.", api);
                if (status is 401 or 403) return new BillingException(502, "The billing system is unavailable.", api);
                if (status == 429) return new BillingException(503, "The billing system is busy; try again shortly.", api);
                if (status == 409) return new BillingException(409, detail ?? "The billing system refused the request.", api);
                if (status is >= 400 and < 500) return new BillingException(422, detail ?? "The billing system refused the request.", api);
                return new BillingException(502, "The billing system is unavailable.", api);
            case SdkTimeoutException to:
                return new BillingException(504, "The billing system did not answer in time; the outcome is unknown.", to);
            case SdkConnectionException cx:
                return new BillingException(502, "The billing system could not be reached; the outcome is unknown.", cx);
            default:
                return new BillingException(502, "The billing system is unavailable.", ex);
        }
    }

    private static string? Detail(ApiException ex)
    {
        static string? Join(ErrorListResponse1? b) => b?.Errors is { Count: > 0 } ? string.Join(" ", b.Errors) : null;

        return ex switch
        {
            ApiException<RawError> r => Trim(SafeText(r.Error)),
            ApiException<CreateSubscriptionError> e => e.Error.TryGetErrorListResponse1(out var b) ? Join(b) : null,
            ApiException<UpdateSubscriptionError> e => e.Error.TryGetErrorListResponse1(out var b) ? Join(b) : null,
            ApiException<PauseSubscriptionError> e => e.Error.TryGetErrorListResponse1(out var b) ? Join(b) : null,
            ApiException<ResumeSubscriptionError> e => e.Error.TryGetErrorListResponse1(out var b) ? Join(b) : null,
            ApiException<ReactivateSubscriptionError> e => e.Error.TryGetErrorListResponse1(out var b) ? Join(b) : null,
            ApiException<CreateProductError> e => e.Error.TryGetErrorListResponse1(out var b) ? Join(b) : null,
            ApiException<CreateProductFamilyError> e => e.Error.TryGetErrorListResponse1(out var b) ? Join(b) : null,
            ApiException<CreateProductCurrencyPricesError> e => e.Error.TryGetErrorArrayMapResponse1(out var b) ? Json(b.Errors) : null,
            ApiException<UpdateProductCurrencyPricesError> e => e.Error.TryGetErrorArrayMapResponse1(out var b) ? Json(b.Errors) : null,
            _ => null
        };
    }

    private static string? Json(object? value) => value == null ? null : Trim(JsonSerializer.Serialize(value));

    private static string? SafeText(RawError raw)
    {
        try { return raw.ReadAsString(); } catch (Exception) { return null; }
    }

    private static string? Trim(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : (text.Length > 300 ? text[..300] : text);
}
