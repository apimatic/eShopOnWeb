using System;
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
using MaxioAdvancedBilling.Errors;
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
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using MaxioSubscription = MaxioAdvancedBilling.Models.Subscription;
using MaxioProduct = MaxioAdvancedBilling.Models.Product;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Subscription billing on top of Maxio (the system of record). Every shopper operation is scoped to the
/// caller's own Maxio customer, whose reference is the caller's JWT name.
/// </summary>
public class MaxioBilling
{
    private const int PageSize = 100;
    private static readonly Regex CurrencyRegex = new("^[A-Za-z]{3}$", RegexOptions.Compiled);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<MaxioBilling> _logger;

    public MaxioBilling(MaxioAdvancedBillingClient client, UserManager<ApplicationUser> userManager, ILogger<MaxioBilling> logger)
    {
        _client = client;
        _userManager = userManager;
        _logger = logger;
    }

    // ---------------------------------------------------------------- plans

    public async Task<IReadOnlyList<PlanDto>> ListPlansAsync(CancellationToken ct)
    {
        var products = await ListAllProductsAsync(ct);
        return products.Select(ToPlan).ToList();
    }

    private async Task<List<MaxioProduct>> ListAllProductsAsync(CancellationToken ct)
    {
        var all = new List<MaxioProduct>();
        for (var page = 1; ; page++)
        {
            var batch = await RunRaw("list plans", () => _client.Products.ListProducts(
                new ListProductsRequest { Page = page, PerPage = PageSize, IncludeArchived = false }, null, ct));
            all.AddRange(batch.Select(p => p.Product));
            if (batch.Count < PageSize)
            {
                break;
            }
        }

        // Only live plans with a handle are offered.
        return all.Where(p => p.ArchivedAt == null && p.Id != null && !string.IsNullOrEmpty(p.Handle)).ToList();
    }

    private async Task<MaxioProduct> RequireOfferedPlanAsync(string? handle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw BillingException.Invalid("A plan handle is required.");
        }

        var plan = (await ListAllProductsAsync(ct)).FirstOrDefault(p => string.Equals(p.Handle, handle.Trim(), StringComparison.Ordinal));
        return plan ?? throw BillingException.NotFound($"Plan '{handle}' is not one of the plans on offer.");
    }

    // ---------------------------------------------------------- subscribe

    public async Task<SubscriptionDto> SubscribeAsync(string userName, string? planHandle, CancellationToken ct)
    {
        var plan = await RequireOfferedPlanAsync(planHandle, ct);
        var customerId = await EnsureCustomerAsync(userName, ct);
        var started = DateTimeOffset.UtcNow.AddMinutes(-1);

        MaxioSubscription created;
        try
        {
            var response = await RunA<SubscriptionResponse, CreateSubscriptionError>("create subscription",
                () => _client.Subscriptions.CreateSubscription(new CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = plan.Handle,
                            CustomerId = customerId,
                            // No card details: the subscription is billed by invoice (remittance).
                            PaymentCollectionMethod = CollectionMethod.Remittance
                        }
                    }
                }, null, ct), e => e.TryGetErrorListResponse1(out var errors) ? string.Join("; ", errors.Errors) : Raw(e));
            created = response.Subscription ?? throw new BillingException(HttpStatusCode.BadGateway, "Billing system returned no subscription.");
        }
        catch (BillingException ex) when (ex.StatusCode == HttpStatusCode.GatewayTimeout || ex.InnerException is SdkConnectionException)
        {
            // Unknown outcome: Maxio may have created the subscription. Settle it by looking for it.
            var found = await FindRecentSubscriptionAsync(customerId, plan.Handle!, started, ct);
            if (found == null)
            {
                throw;
            }

            created = found;
        }

        return await ToDtoAsync(created, ct);
    }

    private async Task<MaxioSubscription?> FindRecentSubscriptionAsync(int customerId, string handle, DateTimeOffset since, CancellationToken ct)
    {
        try
        {
            var subs = await _client.Customers.ListCustomerSubscriptions(new ListCustomerSubscriptionsRequest { CustomerId = customerId }, null, ct);
            return subs.Select(s => s.Subscription)
                .Where(s => s != null && s.Product?.Handle == handle && s.CreatedAt >= since)
                .OrderByDescending(s => s!.CreatedAt)
                .FirstOrDefault();
        }
        catch (SdkException ex)
        {
            _logger.LogWarning(ex, "Could not settle the outcome of a subscribe call");
            return null;
        }
    }

    private async Task<int> EnsureCustomerAsync(string userName, CancellationToken ct)
    {
        var existing = await FindCustomerIdAsync(userName, ct);
        if (existing != null)
        {
            return existing.Value;
        }

        var user = await _userManager.FindByNameAsync(userName);
        var email = !string.IsNullOrWhiteSpace(user?.Email) ? user!.Email! : userName;
        var first = email.Contains('@') ? email.Substring(0, email.IndexOf('@')) : email;
        try
        {
            var response = await RunA<CustomerResponse, CreateCustomerError>("create customer",
                () => _client.Customers.CreateCustomer(new CreateCustomerOperationRequest
                {
                    Body = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer { FirstName = first, LastName = "Shopper", Email = email, Reference = userName }
                    }
                }, null, ct),
                e => e.TryGetCustomerErrorResponse1(out var errors) ? $"customer rejected: {errors.Errors}" : Raw(e));
            return response.Customer.Id ?? throw new BillingException(HttpStatusCode.BadGateway, "Billing system returned a customer without an id.");
        }
        catch (BillingException)
        {
            // A concurrent request may have created the customer first.
            var raced = await FindCustomerIdAsync(userName, ct);
            if (raced != null)
            {
                return raced.Value;
            }

            throw;
        }
    }

    private async Task<int?> FindCustomerIdAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await RunRaw("read customer", () => _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference }, null, ct));
            return response.Customer.Id;
        }
        catch (BillingException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    // -------------------------------------------------------------- read

    public async Task<SubscriptionDto> GetAsync(string userName, int id, CancellationToken ct)
    {
        var sub = await GetOwnedAsync(userName, id, ct);
        return await ToDtoAsync(sub, ct);
    }

    private async Task<MaxioSubscription> GetOwnedAsync(string userName, int id, CancellationToken ct)
    {
        var sub = await GetAsync(id, ct);
        if (!string.Equals(sub.Customer?.Reference, userName, StringComparison.Ordinal))
        {
            // Do not reveal that the subscription exists.
            throw BillingException.NotFound("Subscription not found.");
        }

        return sub;
    }

    private async Task<MaxioSubscription> GetAsync(int id, CancellationToken ct)
    {
        var response = await RunRaw("read subscription", () => _client.Subscriptions.ReadSubscription(
            new ReadSubscriptionRequest { SubscriptionId = id }, null, ct));
        return response.Subscription ?? throw BillingException.NotFound("Subscription not found.");
    }

    // ------------------------------------------------- pause / resume

    public async Task<SubscriptionDto> PauseAsync(string userName, int id, CancellationToken ct)
    {
        var sub = await GetOwnedAsync(userName, id, ct);
        if (!(sub.State == SubscriptionState.Active || sub.State == SubscriptionState.Trialing))
        {
            throw BillingException.Conflict($"A subscription in state '{StateName(sub)}' cannot be paused.");
        }

        await RunA<SubscriptionResponse, PauseSubscriptionError>("pause subscription",
            () => _client.SubscriptionStatus.PauseSubscription(new PauseSubscriptionRequest { SubscriptionId = id }, null, ct), e => e.TryGetErrorListResponse1(out var errors) ? string.Join("; ", errors.Errors) : Raw(e));
        return await ToDtoAsync(await GetAsync(id, ct), ct);
    }

    public async Task<SubscriptionDto> ResumeAsync(string userName, int id, CancellationToken ct)
    {
        var sub = await GetOwnedAsync(userName, id, ct);
        if (!(sub.State == SubscriptionState.OnHold || sub.State == SubscriptionState.Paused))
        {
            throw BillingException.Conflict($"A subscription in state '{StateName(sub)}' is not paused.");
        }

        await RunA<SubscriptionResponse, ResumeSubscriptionError>("resume subscription",
            () => _client.SubscriptionStatus.ResumeSubscription(new ResumeSubscriptionRequest { SubscriptionId = id }, null, ct), e => e.TryGetErrorListResponse1(out var errors) ? string.Join("; ", errors.Errors) : Raw(e));
        return await ToDtoAsync(await GetAsync(id, ct), ct);
    }

    // ------------------------------------------------ cancel / restart

    public async Task<SubscriptionDto> CancelAsync(string userName, int id, CancellationToken ct)
    {
        var sub = await GetOwnedAsync(userName, id, ct);
        if (sub.State == SubscriptionState.Canceled || sub.State == SubscriptionState.Expired || sub.State == SubscriptionState.FailedToCreate)
        {
            throw BillingException.Conflict($"A subscription in state '{StateName(sub)}' cannot be cancelled.");
        }

        await CancelNowAsync(id, ct);
        return await ToDtoAsync(await GetAsync(id, ct), ct);
    }

    private async Task CancelNowAsync(int id, CancellationToken ct)
    {
        await RunA<SubscriptionResponse, CancelSubscriptionError>("cancel subscription",
            () => _client.SubscriptionStatus.CancelSubscription(new CancelSubscriptionRequest { SubscriptionId = id }, null, ct),
            e => e.TryGetCancelSubscriptionErrorResponse(out var errors) && errors.TryGetErrorListResponse1(out var list)
                ? string.Join("; ", list.Errors)
                : Raw(e));
    }

    public async Task<SubscriptionDto> RestartAsync(string userName, int id, CancellationToken ct)
    {
        var sub = await GetOwnedAsync(userName, id, ct);
        if (sub.State != SubscriptionState.Canceled)
        {
            throw BillingException.Conflict($"Only a cancelled subscription can be restarted; this one is '{StateName(sub)}'.");
        }

        await ReactivateAsync(id, ct);
        return await ToDtoAsync(await GetAsync(id, ct), ct);
    }

    private async Task ReactivateAsync(int id, CancellationToken ct)
    {
        await RunA<SubscriptionResponse, ReactivateSubscriptionError>("restart subscription",
            () => _client.SubscriptionStatus.ReactivateSubscription(new ReactivateSubscriptionOperationRequest { SubscriptionId = id }, null, ct), e => e.TryGetErrorListResponse1(out var errors) ? string.Join("; ", errors.Errors) : Raw(e));
    }

    // ------------------------------------------------------ switch plan

    public async Task<SubscriptionDto> SwitchAsync(string userName, int id, string? planHandle, CancellationToken ct)
    {
        var plan = await RequireOfferedPlanAsync(planHandle, ct);
        var sub = await GetOwnedAsync(userName, id, ct);

        var wasCanceled = sub.State == SubscriptionState.Canceled;
        if (!wasCanceled && !(sub.State == SubscriptionState.Active || sub.State == SubscriptionState.Trialing))
        {
            throw BillingException.Conflict($"A subscription in state '{StateName(sub)}' cannot be switched to another plan.");
        }

        if (string.Equals(sub.Product?.Handle, plan.Handle, StringComparison.Ordinal))
        {
            throw BillingException.Conflict("The subscription is already on that plan.");
        }

        if (wasCanceled)
        {
            // Maxio only migrates active or trialing subscriptions, so a cancelled one is restarted first.
            await ReactivateAsync(id, ct);
        }

        try
        {
            await RunA<SubscriptionResponse, MigrateSubscriptionProductError>("switch plan",
                () => _client.SubscriptionProducts.MigrateSubscriptionProduct(new MigrateSubscriptionProductRequest
                {
                    SubscriptionId = id,
                    Body = new SubscriptionProductMigrationRequest
                    {
                        Migration = new SubscriptionProductMigration { ProductHandle = plan.Handle }
                    }
                }, null, ct), e => e.TryGetErrorListResponse1(out var errors) ? string.Join("; ", errors.Errors) : Raw(e));
        }
        catch (BillingException) when (wasCanceled)
        {
            // Put the subscription back the way it was: cancelled, on its old plan.
            try
            {
                await CancelNowAsync(id, ct);
            }
            catch (BillingException ex)
            {
                _logger.LogError(ex, "Could not re-cancel subscription {SubscriptionId} after a failed plan switch", id);
            }

            throw;
        }

        return await ToDtoAsync(await GetAsync(id, ct), ct);
    }

    // ------------------------------------------------------------ usage

    public async Task<UsageDayDto> GetUsageAsync(string userName, int id, DateOnly day, CancellationToken ct)
    {
        await GetOwnedAsync(userName, id, ct);

        var components = await RunRaw("list subscription components", () => _client.SubscriptionComponents.ListSubscriptionComponents(
            new ListSubscriptionComponentsRequest { SubscriptionId = id }, null, ct));

        var since = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var until = since.AddDays(1);

        var result = new List<ComponentUsageDto>();
        foreach (var component in components.Select(c => c.Component).Where(c => c != null && c.Kind == ComponentKind.MeteredComponent && c.ComponentId != null))
        {
            var componentId = component!.ComponentId!.Value;
            var entries = new List<UsageEntryDto>();
            for (var page = 1; ; page++)
            {
                var batch = await RunRaw("list usages", () => _client.SubscriptionComponents.ListUsages(new ListUsagesRequest
                {
                    SubscriptionIdOrReference = id,
                    ComponentId = componentId,
                    SinceDate = since,
                    UntilDate = until,
                    Page = page,
                    PerPage = PageSize
                }, null, ct));
                entries.AddRange(batch.Select(u => new UsageEntryDto(u.Usage.Id, u.Usage.CreatedAt, QuantityOf(u.Usage.Quantity), u.Usage.Memo)));
                if (batch.Count < PageSize)
                {
                    break;
                }
            }

            result.Add(new ComponentUsageDto(componentId, component.Name, component.UnitName, entries.Sum(e => e.Quantity), entries));
        }

        return new UsageDayDto(id, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), result);
    }

    private static decimal QuantityOf(MaxioAdvancedBilling.Models.AnyOf.Quantity1? quantity)
    {
        if (quantity == null)
        {
            return 0m;
        }

        return quantity.Match(
            i => (decimal)i,
            s => decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m,
            _ => 0m);
    }

    // -------------------------------------------------------- admin: plans

    public async Task<PlanDto> CreatePlanAsync(string name, decimal monthlyPrice, string brand, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw BillingException.Invalid("A plan name is required.");
        }

        var cents = monthlyPrice * 100m;
        if (monthlyPrice < 0 || monthlyPrice > 10_000_000m || cents != decimal.Truncate(cents))
        {
            throw BillingException.Invalid("The monthly price must be between 0 and 10000000 with at most two decimals.");
        }

        name = name.Trim();
        var existing = await ListAllProductsAsync(ct);
        if (existing.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw BillingException.Conflict($"A plan named '{name}' already exists.");
        }

        var familyId = await EnsureFamilyAsync(brand, ct);
        var response = await RunA<ProductResponse, CreateProductError>("create plan",
            () => _client.Products.CreateProduct(new CreateProductRequest
            {
                ProductFamilyId = familyId.ToString(CultureInfo.InvariantCulture),
                Body = new CreateOrUpdateProductRequest
                {
                    Product = new CreateOrUpdateProduct
                    {
                        Name = name,
                        // Maxio does not generate a handle on its own, and subscribers address a plan by handle.
                        Handle = MakeHandle(name),
                        Description = name,
                        PriceInCents = (long)cents,
                        Interval = 1,
                        IntervalUnit = IntervalUnit.Month
                    }
                }
            }, null, ct), e => e.TryGetErrorListResponse1(out var errors) ? string.Join("; ", errors.Errors) : Raw(e));
        return ToPlan(response.Product);
    }

    private static string MakeHandle(string name)
    {
        var slug = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (slug.Length > 40)
        {
            slug = slug.Substring(0, 40).Trim('-');
        }

        return $"{(slug.Length == 0 ? "plan" : slug)}-{Guid.NewGuid().ToString("N").Substring(0, 6)}";
    }

    private async Task<int> EnsureFamilyAsync(string brand, CancellationToken ct)
    {
        var family = await FindFamilyAsync(brand, ct);
        if (family != null)
        {
            return family.Value;
        }

        try
        {
            var created = await RunA<ProductFamilyResponse, CreateProductFamilyError>("create plan brand",
                () => _client.ProductFamilies.CreateProductFamily(new CreateProductFamilyOperationRequest
                {
                    Body = new CreateProductFamilyRequest { ProductFamily = new CreateProductFamily { Name = brand } }
                }, null, ct), e => e.TryGetErrorListResponse1(out var errors) ? string.Join("; ", errors.Errors) : Raw(e));
            return created.ProductFamily?.Id ?? throw new BillingException(HttpStatusCode.BadGateway, "Billing system returned a brand without an id.");
        }
        catch (BillingException)
        {
            var raced = await FindFamilyAsync(brand, ct);
            if (raced != null)
            {
                return raced.Value;
            }

            throw;
        }
    }

    private async Task<int?> FindFamilyAsync(string brand, CancellationToken ct)
    {
        var families = await RunRaw("list plan brands", () => _client.ProductFamilies.ListProductFamilies(new ListProductFamiliesRequest(), null, ct));
        return families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f != null && f.ArchivedAt == null && f.Id != null && string.Equals(f.Name, brand, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    public async Task<CurrencyPriceDto> AddCurrencyPriceAsync(int planId, string? currency, decimal price, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(currency) || !CurrencyRegex.IsMatch(currency.Trim()))
        {
            throw BillingException.Invalid("The currency must be a three-letter ISO code.");
        }

        if (price <= 0 || price != decimal.Truncate(price) || price > int.MaxValue)
        {
            throw BillingException.Invalid("The price must be a positive whole number of currency units.");
        }

        currency = currency.Trim().ToUpperInvariant();
        var product = (await RunRaw("read plan", () => _client.Products.ReadProduct(new ReadProductRequest { ProductId = planId }, null, ct))).Product;
        if (product.ArchivedAt != null)
        {
            throw BillingException.NotFound("Plan not found.");
        }

        if (product.DefaultProductPricePointId == null)
        {
            throw BillingException.Conflict("The plan has no default price point to price.");
        }

        if (product.InitialChargeInCents > 0 || product.TrialPriceInCents > 0 || product.TrialInterval > 0)
        {
            throw BillingException.Invalid("Plans with a set-up fee or a trial cannot be priced in another currency here.");
        }

        var response = await RunA<CurrencyPricesResponse, CreateProductCurrencyPricesError>("price plan in currency",
            () => _client.ProductPricePoints.CreateProductCurrencyPrices(new CreateProductCurrencyPricesOperationRequest
            {
                ProductPricePointId = product.DefaultProductPricePointId.Value,
                Body = new CreateProductCurrencyPricesRequest
                {
                    CurrencyPrices = new[]
                    {
                        new CreateProductCurrencyPrice { Currency = currency, Price = (int)price, Role = CurrencyPriceRole.Baseline }
                    }
                }
            }, null, ct),
            e => e.TryGetErrorArrayMapResponse1(out var errors) && errors.Errors != null
                ? string.Join("; ", errors.Errors.Select(kv => $"{kv.Key}: {kv.Value}"))
                : Raw(e));

        var created = response.CurrencyPrices.FirstOrDefault(c => string.Equals(c.Currency, currency, StringComparison.OrdinalIgnoreCase));
        return new CurrencyPriceDto(planId, currency, created?.Price != null ? (decimal)created.Price.Value : price, "baseline");
    }

    // ------------------------------------------------ admin: next billing

    public async Task<SubscriptionDto> SetNextBillingDateAsync(int id, DateTimeOffset when, CancellationToken ct)
    {
        if (when <= DateTimeOffset.UtcNow)
        {
            throw BillingException.Invalid("The next billing date must be in the future.");
        }

        var sub = await GetAsync(id, ct);
        if (!(sub.State == SubscriptionState.Active || sub.State == SubscriptionState.Trialing))
        {
            throw BillingException.Conflict($"A subscription in state '{StateName(sub)}' is not billed, so it has no next billing date to set.");
        }

        await RunA<SubscriptionResponse, UpdateSubscriptionError>("set next billing date",
            () => _client.Subscriptions.UpdateSubscription(new UpdateSubscriptionOperationRequest
            {
                SubscriptionId = id,
                Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { NextBillingAt = when } }
            }, null, ct), e => e.TryGetErrorListResponse1(out var errors) ? string.Join("; ", errors.Errors) : Raw(e));

        // The update response does not echo the date; read the subscription back.
        return await ToDtoAsync(await GetAsync(id, ct), ct);
    }

    // ------------------------------------------------------------ mapping

    private static string StateName(MaxioSubscription sub) => sub.State?.Value ?? "unknown";

    private static PlanDto ToPlan(MaxioProduct p)
    {
        var cents = p.PriceInCents ?? 0;
        return new PlanDto(p.Id ?? 0, p.Handle ?? string.Empty, p.Name ?? string.Empty, p.Description, p.ProductFamily?.Name, cents / 100m, cents);
    }

    private async Task<SubscriptionDto> ToDtoAsync(MaxioSubscription sub, CancellationToken ct)
    {
        var balance = sub.BalanceInCents ?? 0;
        return new SubscriptionDto(
            sub.Id ?? 0,
            sub.State?.Value,
            await IsPaidAsync(sub, ct),
            balance,
            sub.Currency,
            sub.Product == null ? null : ToPlan(sub.Product),
            sub.State == SubscriptionState.Canceled ? null : sub.CurrentPeriodEndsAt,
            sub.CreatedAt,
            sub.CanceledAt);
    }

    /// <summary>
    /// Paid means nothing is owed: no positive balance, no dunning state, and no invoice with money still due.
    /// </summary>
    private async Task<bool> IsPaidAsync(MaxioSubscription sub, CancellationToken ct)
    {
        if ((sub.BalanceInCents ?? 0) > 0
            || sub.State == SubscriptionState.PastDue
            || sub.State == SubscriptionState.Unpaid
            || sub.State == SubscriptionState.SoftFailure)
        {
            return false;
        }

        if (sub.Id == null)
        {
            return true;
        }

        for (var page = 1; ; page++)
        {
            var batch = await RunRaw("list invoices", () => _client.Invoices.ListInvoices(
                new ListInvoicesRequest { SubscriptionId = sub.Id, Page = page, PerPage = PageSize }, null, ct));
            foreach (var invoice in batch.Invoices)
            {
                if (invoice.Status == InvoiceStatus.Draft || invoice.Status == InvoiceStatus.Voided
                    || invoice.Status == InvoiceStatus.Canceled || invoice.Status == InvoiceStatus.Paid)
                {
                    continue;
                }

                if (!decimal.TryParse(invoice.DueAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var due))
                {
                    throw new BillingException(HttpStatusCode.BadGateway, "Billing system returned an invoice with an unreadable amount due.");
                }

                if (due > 0)
                {
                    return false;
                }
            }

            if (batch.Invoices.Count < PageSize)
            {
                return true;
            }
        }
    }

    // ----------------------------------------------------- error mapping

    private static string? Raw(ApiError e) => e.TryGetRawError(out var raw) ? Trim(raw.ReadAsString()) : null;

    private static string? Trim(string? s) => s is { Length: > 300 } ? s.Substring(0, 300) : s;

    private Task<T> RunRaw<T>(string what, Func<Task<T>> call) =>
        Run<T, RawError>(what, call, e => Trim(e.ReadAsString()));

    private Task<T> RunA<T, TError>(string what, Func<Task<T>> call, Func<TError, string?> describe) where TError : ApiError =>
        Run(what, call, describe);

    private async Task<T> Run<T, TError>(string what, Func<Task<T>> call, Func<TError, string?> describe)
    {
        try
        {
            return await call();
        }
        catch (ApiException<TError> ex)
        {
            string? reason = null;
            try
            {
                reason = describe(ex.Error);
            }
            catch (Exception)
            {
                // best effort only
            }

            throw Translate(ex.StatusCode, reason, what, ex);
        }
        catch (SdkTimeoutException ex)
        {
            _logger.LogWarning(ex, "Maxio call timed out: {Operation}", what);
            throw new BillingException(HttpStatusCode.GatewayTimeout, $"The billing system did not answer in time ({what}).", ex);
        }
        catch (SdkConnectionException ex)
        {
            _logger.LogWarning(ex, "Maxio call failed to connect: {Operation}", what);
            throw new BillingException(HttpStatusCode.BadGateway, $"The billing system could not be reached ({what}).", ex);
        }
        catch (ApiException ex)
        {
            // e.g. ResponseDeserializationException or an error type other than the declared one
            throw Translate(ex.StatusCode, null, what, ex);
        }
        catch (SdkException ex)
        {
            _logger.LogError(ex, "Maxio call failed: {Operation}", what);
            throw new BillingException(HttpStatusCode.BadGateway, $"The billing system call failed ({what}).", ex);
        }
    }

    private BillingException Translate(HttpStatusCode status, string? reason, string what, Exception inner)
    {
        var code = (int)status;
        _logger.LogWarning("Maxio answered {Status} for {Operation}", code, what);
        var suffix = string.IsNullOrWhiteSpace(reason) ? string.Empty : $": {reason}";
        if (code == 404)
        {
            return new BillingException(HttpStatusCode.NotFound, $"Not found in the billing system ({what}){suffix}", inner);
        }

        if (code is 400 or 409 or 422)
        {
            return new BillingException(HttpStatusCode.UnprocessableEntity, $"The billing system rejected the request ({what}){suffix}", inner);
        }

        if (code is 401 or 403)
        {
            return new BillingException(HttpStatusCode.BadGateway, $"The billing system refused this application's credentials ({what}).", inner);
        }

        if (code == 429)
        {
            return new BillingException(HttpStatusCode.ServiceUnavailable, "The billing system is rate limiting requests; try again shortly.", inner);
        }

        return new BillingException(HttpStatusCode.BadGateway, $"The billing system failed ({what}, status {code}).", inner);
    }
}
