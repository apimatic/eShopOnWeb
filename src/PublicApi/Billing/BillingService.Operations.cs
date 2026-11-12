using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.Exceptions;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public partial class BillingService
{
    private static readonly TimeSpan ClaimTtl = TimeSpan.FromMinutes(5);

    // ---------------------------------------------------------------- claims (app-owned store, PK = Key)

    /// <summary>Claim before the SDK call; the primary key refuses a second claimant.</summary>
    private async Task<BillingClaim> ClaimAsync(string key, string conflictMessage)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var claim = new BillingClaim { Key = key };
            try
            {
                _db.BillingClaims.Add(claim);
                await _db.SaveChangesAsync();
                return claim;
            }
            catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or ArgumentException)
            {
                _db.Entry(claim).State = EntityState.Detached;
                var existing = await _db.BillingClaims.AsNoTracking().FirstOrDefaultAsync(c => c.Key == key);
                if (existing != null && attempt == 0 && DateTimeOffset.UtcNow - existing.CreatedAt > ClaimTtl && existing.MaxioCustomerId == null)
                {
                    // abandoned by a crashed process: take it over
                    await ReleaseAsync(key);
                    continue;
                }
                throw new BillingException(409, conflictMessage, ex);
            }
        }
        throw new BillingException(409, conflictMessage);
    }

    private async Task ReleaseAsync(string key)
    {
        var row = await _db.BillingClaims.FirstOrDefaultAsync(c => c.Key == key);
        if (row != null)
        {
            _db.BillingClaims.Remove(row);
            await _db.SaveChangesAsync();
        }
    }

    private static bool IsUnknownOutcome(BillingException ex) => ex.InnerException is SdkConnectionException or ResponseDeserializationException;

    // ---------------------------------------------------------------- plans

    private async Task<List<Product>> OfferedProducts(CancellationToken ct)
    {
        var all = new List<Product>();
        for (var page = 1; page <= MAX_PAGES; page++)
        {
            var p = page;
            var batch = await Call(() => _client.Products.ListProducts(new ListProductsRequest { Page = p, PerPage = PER_PAGE }, cancellationToken: ct));
            all.AddRange(batch.Select(b => b.Product).Where(x => x != null && x.ArchivedAt == null)!);
            if (batch.Count < PER_PAGE) return all;
        }
        throw new BillingException(502, "Too many plans to read completely.");
    }

    private static PlanDto ToDto(Product p) =>
        new(p.Id, p.Handle, p.Name, p.PriceInCents.HasValue ? p.PriceInCents.Value / 100m : null, p.ProductFamily?.Name);

    public async Task<ListPlansResponse> ListPlans(CancellationToken ct)
    {
        using var cts = Budgeted(ct);
        var products = await OfferedProducts(cts.Token);
        return new ListPlansResponse(products.Select(ToDto).ToList());
    }

    // ---------------------------------------------------------------- customer link

    private async Task<int> EnsureCustomer(string username, CancellationToken ct)
    {
        var key = "customer:" + username.ToLowerInvariant();
        var row = await _db.BillingClaims.AsNoTracking().FirstOrDefaultAsync(c => c.Key == key);
        if (row?.MaxioCustomerId != null) return row.MaxioCustomerId.Value;

        var existing = await FindCustomerByReference(username, ct);
        if (existing != null)
        {
            await StoreCustomer(key, existing.Value);
            return existing.Value;
        }

        var user = await _users.FindByNameAsync(username);
        var email = user?.Email;
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new BillingException(422, "The account has no e-mail address, which billing requires.");
        }

        await ClaimAsync(key, "Billing setup for this account is already in progress.");
        try
        {
            var created = await Call(() => _client.Customers.CreateCustomer(new CreateCustomerOperationRequest
            {
                Body = new CreateCustomerRequest
                {
                    Customer = new CreateCustomer { FirstName = username, LastName = username, Email = email, Reference = username }
                }
            }, cancellationToken: ct));
            var id = created.Customer.Id ?? throw new BillingException(502, "The billing provider returned no customer id.");
            await StoreCustomer(key, id);
            return id;
        }
        catch (BillingException ex)
        {
            if (IsUnknownOutcome(ex))
            {
                var settled = await FindCustomerByReference(username, ct);
                if (settled != null)
                {
                    await StoreCustomer(key, settled.Value);
                    return settled.Value;
                }
            }
            await ReleaseAsync(key);
            throw;
        }
    }

    private async Task<int?> FindCustomerByReference(string username, CancellationToken ct)
    {
        try
        {
            var found = await Call(() => _client.Customers.ReadCustomerByReference(new ReadCustomerByReferenceRequest { Reference = username }, cancellationToken: ct));
            return found.Customer.Id;
        }
        catch (BillingException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    private async Task StoreCustomer(string key, int customerId)
    {
        var row = await _db.BillingClaims.FirstOrDefaultAsync(c => c.Key == key);
        if (row == null)
        {
            _db.BillingClaims.Add(new BillingClaim { Key = key, MaxioCustomerId = customerId });
        }
        else
        {
            row.MaxioCustomerId = customerId;
        }
        await _db.SaveChangesAsync();
    }

    // ---------------------------------------------------------------- subscriptions

    private async Task<Subscription> ReadOwned(string username, int subscriptionId, CancellationToken ct)
    {
        var sub = await ReadAny(subscriptionId, ct);
        if (!string.Equals(sub.Customer?.Reference, username, StringComparison.OrdinalIgnoreCase))
        {
            throw new BillingException(404, "Not found.");
        }
        return sub;
    }

    private async Task<Subscription> ReadAny(int subscriptionId, CancellationToken ct) =>
        (await Call(() => _client.Subscriptions.ReadSubscription(new ReadSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct))).Subscription
            ?? throw new BillingException(404, "Not found.");

    private async Task<bool> IsPaid(Subscription sub, CancellationToken ct)
    {
        if ((sub.BalanceInCents ?? 0) > 0) return false;
        for (var page = 1; page <= MAX_PAGES; page++)
        {
            var p = page;
            var result = await Call(() => _client.Invoices.ListInvoices(new ListInvoicesRequest { SubscriptionId = sub.Id, Page = p, PerPage = PER_PAGE }, cancellationToken: ct));
            foreach (var inv in result.Invoices)
            {
                if (inv.Status == InvoiceStatus.Draft || inv.Status == InvoiceStatus.Voided || inv.Status == InvoiceStatus.Canceled) continue;
                if (decimal.TryParse(inv.DueAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var due) && due > 0) return false;
            }
            if (result.Invoices.Count < PER_PAGE) return true;
        }
        throw new BillingException(502, "Too many invoices to read completely.");
    }

    private async Task<SubscriptionDto> ToDto(Subscription sub, CancellationToken ct) =>
        new(sub.Id ?? 0, sub.Product?.Handle, sub.Product?.Name, sub.State?.Value, await IsPaid(sub, ct), sub.CurrentPeriodEndsAt);

    public async Task<SubscriptionDto> Subscribe(string username, string? planHandle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(planHandle)) throw new BillingException(400, "planHandle is required.");
        using var cts = Budgeted(ct);
        var token = cts.Token;

        var offered = await OfferedProducts(token);
        var plan = offered.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal))
            ?? throw new BillingException(422, $"'{planHandle}' is not a plan that can be subscribed to.");
        var handle = plan.Handle!;

        var customerId = await EnsureCustomer(username, token);

        var key = $"subscribe:{username.ToLowerInvariant()}:{handle}";
        var claim = await ClaimAsync(key, "A subscription request for this plan is already in progress.");
        try
        {
            var created = await Call(() => _client.Subscriptions.CreateSubscription(new CreateSubscriptionOperationRequest
            {
                Body = new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = handle,
                        CustomerId = customerId,
                        PaymentCollectionMethod = CollectionMethod.Remittance,
                    }
                }
            }, cancellationToken: token));
            await ReleaseAsync(key);
            return await ToDto(created.Subscription ?? throw new BillingException(502, "The billing provider returned no subscription."), token);
        }
        catch (BillingException ex)
        {
            if (IsUnknownOutcome(ex))
            {
                var settled = await SettleSubscribe(customerId, handle, claim.CreatedAt, token);
                if (settled != null)
                {
                    await ReleaseAsync(key);
                    return await ToDto(settled, token);
                }
            }
            await ReleaseAsync(key);
            throw;
        }
    }

    private async Task<Subscription?> SettleSubscribe(int customerId, string handle, DateTimeOffset since, CancellationToken ct)
    {
        try
        {
            var subs = await Call(() => _client.Customers.ListCustomerSubscriptions(new ListCustomerSubscriptionsRequest { CustomerId = customerId }, cancellationToken: ct));
            return subs.Select(s => s.Subscription)
                .FirstOrDefault(s => s != null && s.Product?.Handle == handle && s.CreatedAt >= since.AddMinutes(-1));
        }
        catch (BillingException)
        {
            return null;
        }
    }

    public async Task<SubscriptionDto> GetSubscription(string username, int id, CancellationToken ct)
    {
        using var cts = Budgeted(ct);
        return await ToDto(await ReadOwned(username, id, cts.Token), cts.Token);
    }

    public async Task<SubscriptionDto> Pause(string username, int id, CancellationToken ct)
    {
        using var cts = Budgeted(ct);
        var sub = await ReadOwned(username, id, cts.Token);
        if (sub.State != SubscriptionState.Active) throw new BillingException(422, "Only an active subscription can be paused.");
        _logger.LogInformation("Pausing subscription {Id} for {User}", id, username);
        await Settle(() => _client.SubscriptionStatus.PauseSubscription(new PauseSubscriptionRequest { SubscriptionId = id }, cancellationToken: cts.Token),
            s => s.State == SubscriptionState.OnHold, id, cts.Token);
        return await ToDto(await ReadAny(id, cts.Token), cts.Token);
    }

    public async Task<SubscriptionDto> Resume(string username, int id, CancellationToken ct)
    {
        using var cts = Budgeted(ct);
        var sub = await ReadOwned(username, id, cts.Token);
        if (sub.State != SubscriptionState.OnHold) throw new BillingException(422, "Only a paused subscription can be resumed.");
        await Settle(() => _client.SubscriptionStatus.ResumeSubscription(new ResumeSubscriptionRequest { SubscriptionId = id }, cancellationToken: cts.Token),
            s => s.State != SubscriptionState.OnHold, id, cts.Token);
        return await ToDto(await ReadAny(id, cts.Token), cts.Token);
    }

    public async Task<SubscriptionDto> Cancel(string username, int id, CancellationToken ct)
    {
        using var cts = Budgeted(ct);
        var sub = await ReadOwned(username, id, cts.Token);
        if (sub.State == SubscriptionState.Canceled) throw new BillingException(422, "The subscription is already cancelled.");
        await Settle(() => _client.SubscriptionStatus.CancelSubscription(new CancelSubscriptionRequest { SubscriptionId = id }, cancellationToken: cts.Token),
            s => s.State == SubscriptionState.Canceled, id, cts.Token);
        return await ToDto(await ReadAny(id, cts.Token), cts.Token);
    }

    public async Task<SubscriptionDto> Restart(string username, int id, CancellationToken ct)
    {
        using var cts = Budgeted(ct);
        var sub = await ReadOwned(username, id, cts.Token);
        if (sub.State != SubscriptionState.Canceled) throw new BillingException(422, "Only a cancelled subscription can be restarted.");
        await Settle(() => _client.SubscriptionStatus.ReactivateSubscription(new ReactivateSubscriptionOperationRequest { SubscriptionId = id }, cancellationToken: cts.Token),
            s => s.State != SubscriptionState.Canceled, id, cts.Token);
        return await ToDto(await ReadAny(id, cts.Token), cts.Token);
    }

    public async Task<SubscriptionDto> Switch(string username, int id, string? planHandle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(planHandle)) throw new BillingException(400, "planHandle is required.");
        using var cts = Budgeted(ct);
        var token = cts.Token;
        var sub = await ReadOwned(username, id, token);
        var offered = await OfferedProducts(token);
        if (!offered.Any(p => p.Handle == planHandle)) throw new BillingException(422, $"'{planHandle}' is not a plan that can be subscribed to.");
        if (sub.Product?.Handle == planHandle) throw new BillingException(422, "The subscription is already on that plan.");

        Func<Subscription, bool> done = s => s.Product?.Handle == planHandle;
        if (sub.State == SubscriptionState.Active || sub.State == SubscriptionState.Trialing)
        {
            await Settle(() => _client.SubscriptionProducts.MigrateSubscriptionProduct(new MigrateSubscriptionProductRequest
            {
                SubscriptionId = id,
                Body = new SubscriptionProductMigrationRequest { Migration = new SubscriptionProductMigration { ProductHandle = planHandle } }
            }, cancellationToken: token), done, id, token);
        }
        else if (sub.State == SubscriptionState.Canceled)
        {
            await Settle(() => _client.Subscriptions.UpdateSubscription(new UpdateSubscriptionOperationRequest
            {
                SubscriptionId = id,
                Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { ProductHandle = planHandle } }
            }, cancellationToken: token), done, id, token);
        }
        else
        {
            throw new BillingException(422, $"A subscription in state '{sub.State}' cannot be switched.");
        }

        var after = await ReadAny(id, token);
        if (!done(after)) throw new BillingException(422, "The billing provider did not move the subscription to the new plan.");
        return await ToDto(after, token);
    }

    /// <summary>Runs a write. When its connection fails after Maxio may have acted, re-reads the subscription to settle the outcome.</summary>
    private async Task Settle(Func<Task<SubscriptionResponse>> write, Func<Subscription, bool> achieved, int subscriptionId, CancellationToken ct)
    {
        try
        {
            await Call(write);
        }
        catch (BillingException ex) when (IsUnknownOutcome(ex))
        {
            var now = await ReadAny(subscriptionId, ct);
            if (!achieved(now)) throw new BillingException(502, "The billing provider did not confirm the change; check the subscription and retry.", ex);
        }
    }

    // ---------------------------------------------------------------- usage

    public async Task<DayUsageResponse> UsageForDay(string username, int id, DateOnly date, CancellationToken ct)
    {
        using var cts = Budgeted(ct);
        var token = cts.Token;
        await ReadOwned(username, id, token);

        var comps = await Call(() => _client.SubscriptionComponents.ListSubscriptionComponents(new ListSubscriptionComponentsRequest { SubscriptionId = id }, cancellationToken: token));
        var metered = comps.Select(c => c.Component)
            .Where(c => c != null && c.Kind == ComponentKind.MeteredComponent && c.ComponentId != null)
            .ToList();

        var since = new DateTimeOffset(date.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var until = new DateTimeOffset(date.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var result = new List<ComponentUsageDto>();
        foreach (var c in metered)
        {
            var records = new List<Usage>();
            var complete = false;
            for (var page = 1; page <= MAX_PAGES && !complete; page++)
            {
                var p = page;
                var batch = await Call(() => _client.SubscriptionComponents.ListUsages(new ListUsagesRequest
                {
                    SubscriptionIdOrReference = id,
                    ComponentId = c!.ComponentId!.Value,
                    SinceDate = since,
                    UntilDate = until,
                    Page = p,
                    PerPage = PER_PAGE
                }, cancellationToken: token));
                records.AddRange(batch.Select(b => b.Usage));
                complete = batch.Count < PER_PAGE;
            }
            if (!complete) throw new BillingException(502, "Too many usage records to read completely.");

            var day = records.Where(u => u.CreatedAt != null && DateOnly.FromDateTime(u.CreatedAt.Value.DateTime) == date)
                .Select(u => new UsageRecordDto(u.Id, u.Memo, ReadQuantity(u.Quantity), u.CreatedAt))
                .ToList();
            result.Add(new ComponentUsageDto(c!.ComponentId, c.ComponentHandle, c.Name, day.Sum(r => r.Quantity), day));
        }
        return new DayUsageResponse(id, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), result);
    }

    private static decimal ReadQuantity(MaxioAdvancedBilling.Models.AnyOf.Quantity1? q)
    {
        if (q == null) return 0;
        if (q.TryGetInt(out var i)) return i;
        if (q.TryGetString(out var s) && decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) return d;
        return 0;
    }

    // ---------------------------------------------------------------- staff

    private static string Slug(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            sb.Append(char.IsLetterOrDigit(ch) && ch < 128 ? ch : '-');
        }
        return Regex.Replace(sb.ToString(), "-+", "-").Trim('-');
    }

    public async Task<PlanDto> CreatePlan(CreatePlanRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) throw new BillingException(400, "name is required.");
        if (request.MonthlyPrice is not > 0 || decimal.Round(request.MonthlyPrice.Value, 2) != request.MonthlyPrice.Value)
            throw new BillingException(400, "monthlyPrice must be positive with at most 2 decimals.");
        if (string.IsNullOrWhiteSpace(request.Brand)) throw new BillingException(400, "brand is required.");
        var handle = Slug(request.Name);
        if (handle.Length == 0) throw new BillingException(400, "name must contain letters or digits.");

        var brands = await _db.CatalogBrands.AsNoTracking().ToListAsync(ct);
        var brand = brands.FirstOrDefault(b => string.Equals(b.Brand, request.Brand.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new BillingException(422, $"'{request.Brand}' is not a brand in the catalog.");

        using var cts = Budgeted(ct);
        var token = cts.Token;
        var familyId = await EnsureFamily(brand, token);

        var key = "plan:" + handle;
        await ClaimAsync(key, "A plan with this name is being created or already exists.");
        try
        {
            var created = await Call(() => _client.Products.CreateProduct(new CreateProductRequest
            {
                ProductFamilyId = familyId.ToString(CultureInfo.InvariantCulture),
                Body = new CreateOrUpdateProductRequest
                {
                    Product = new CreateOrUpdateProduct
                    {
                        Name = request.Name.Trim(),
                        Handle = handle,
                        Description = request.Name.Trim(),
                        RequireCreditCard = false,
                        PriceInCents = (long)(request.MonthlyPrice.Value * 100m),
                        Interval = 1,
                        IntervalUnit = IntervalUnit.Month,
                    }
                }
            }, cancellationToken: token));
            await ReleaseAsync(key);
            return ToDto(created.Product);
        }
        catch (BillingException ex)
        {
            if (IsUnknownOutcome(ex))
            {
                try
                {
                    var found = await Call(() => _client.Products.ReadProductByHandle(new ReadProductByHandleRequest { ApiHandle = handle }, cancellationToken: token));
                    await ReleaseAsync(key);
                    return ToDto(found.Product);
                }
                catch (BillingException) { /* not found or unreadable */ }
            }
            await ReleaseAsync(key);
            throw;
        }
    }

    private async Task<int> EnsureFamily(CatalogBrand brand, CancellationToken ct)
    {
        var handle = $"brand-{brand.Id}";
        async Task<int?> Find()
        {
            var families = await Call(() => _client.ProductFamilies.ListProductFamilies(new ListProductFamiliesRequest(), cancellationToken: ct));
            return families.Select(f => f.ProductFamily).FirstOrDefault(f => f?.Handle == handle)?.Id;
        }
        var id = await Find();
        if (id != null) return id.Value;
        try
        {
            var created = await Call(() => _client.ProductFamilies.CreateProductFamily(new CreateProductFamilyOperationRequest
            {
                Body = new CreateProductFamilyRequest { ProductFamily = new CreateProductFamily { Name = brand.Brand, Handle = handle } }
            }, cancellationToken: ct));
            return created.ProductFamily?.Id ?? throw new BillingException(502, "The billing provider returned no product family.");
        }
        catch (BillingException ex) when (IsUnknownOutcome(ex) || ex.StatusCode == 422)
        {
            return await Find() ?? throw ex;
        }
    }

    public async Task<CurrencyPriceDto> AddCurrencyPrice(int planId, CurrencyPriceRequest request, CancellationToken ct)
    {
        var currency = request.Currency?.Trim().ToUpperInvariant();
        if (currency == null || !Regex.IsMatch(currency, "^[A-Z]{3}$")) throw new BillingException(400, "currency must be a 3-letter ISO code.");
        if (request.Price is not > 0) throw new BillingException(400, "price must be positive.");

        using var cts = Budgeted(ct);
        var token = cts.Token;
        var offered = await OfferedProducts(token);
        var plan = offered.FirstOrDefault(p => p.Id == planId) ?? throw new BillingException(404, "Plan not found.");
        var pricePointId = plan.DefaultProductPricePointId ?? throw new BillingException(422, "The plan has no default price point.");

        async Task<ProductPricePoint> ReadPoint() =>
            (await Call(() => _client.ProductPricePoints.ReadProductPricePoint(new ReadProductPricePointRequest
            {
                ProductId = planId,
                PricePointId = pricePointId,
                CurrencyPrices = true
            }, cancellationToken: token))).PricePoint;

        var point = await ReadPoint();
        var existing = point.CurrencyPrices?.FirstOrDefault(c => string.Equals(c.Currency, currency, StringComparison.OrdinalIgnoreCase));
        var price = request.Price.Value;

        if (existing?.Id != null)
        {
            await UpdateCurrency(pricePointId, existing.Id.Value, price, token);
            return new CurrencyPriceDto(planId, currency, price);
        }

        var key = $"currency:{planId}:{currency}";
        await ClaimAsync(key, "This currency price is already being set.");
        try
        {
            var created = await Call(() => _client.ProductPricePoints.CreateProductCurrencyPrices(new CreateProductCurrencyPricesOperationRequest
            {
                ProductPricePointId = pricePointId,
                Body = new CreateProductCurrencyPricesRequest
                {
                    CurrencyPrices = new List<CreateProductCurrencyPrice>
                    {
                        new() { Currency = currency, Price = (int)Math.Truncate(price), Role = CurrencyPriceRole.Baseline }
                    }
                }
            }, cancellationToken: token));
            if (price != Math.Truncate(price))
            {
                var id = created.CurrencyPrices.FirstOrDefault(c => string.Equals(c.Currency, currency, StringComparison.OrdinalIgnoreCase))?.Id;
                if (id != null) await UpdateCurrency(pricePointId, id.Value, price, token);
            }
            await ReleaseAsync(key);
            return new CurrencyPriceDto(planId, currency, price);
        }
        catch (BillingException ex)
        {
            if (IsUnknownOutcome(ex))
            {
                try
                {
                    var after = await ReadPoint();
                    var found = after.CurrencyPrices?.FirstOrDefault(c => string.Equals(c.Currency, currency, StringComparison.OrdinalIgnoreCase));
                    if (found?.Id != null)
                    {
                        if ((decimal?)found.Price != price) await UpdateCurrency(pricePointId, found.Id.Value, price, token);
                        await ReleaseAsync(key);
                        return new CurrencyPriceDto(planId, currency, price);
                    }
                }
                catch (BillingException) { /* unknown */ }
            }
            await ReleaseAsync(key);
            throw;
        }
    }

    private Task UpdateCurrency(int pricePointId, int currencyPriceId, decimal price, CancellationToken ct) =>
        Call(() => _client.ProductPricePoints.UpdateProductCurrencyPrices(new UpdateProductCurrencyPricesRequest
        {
            ProductPricePointId = pricePointId,
            Body = new UpdateCurrencyPricesRequest
            {
                CurrencyPrices = new List<UpdateCurrencyPrice> { new() { Id = currencyPriceId, Price = (double)price } }
            }
        }, cancellationToken: ct));

    public async Task<NextBillingDateDto> SetNextBillingDate(int id, DateOnly? date, CancellationToken ct)
    {
        if (date == null) throw new BillingException(400, "date is required (yyyy-MM-dd).");
        var at = new DateTimeOffset(date.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        if (at <= DateTimeOffset.UtcNow) throw new BillingException(422, "The next billing date must be in the future.");

        using var cts = Budgeted(ct);
        var token = cts.Token;
        var sub = await ReadAny(id, token);
        if (sub.State != SubscriptionState.Active) throw new BillingException(422, "Only an active subscription has a next billing date.");

        await Settle(() => _client.Subscriptions.UpdateSubscription(new UpdateSubscriptionOperationRequest
        {
            SubscriptionId = id,
            Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { NextBillingAt = at } }
        }, cancellationToken: token), s => s.CurrentPeriodEndsAt == at, id, token);

        return new NextBillingDateDto(id, at);
    }
}
