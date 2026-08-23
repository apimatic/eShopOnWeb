using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionBillingService : ISubscriptionBillingService
{
    private const int CatalogPageSize = 100;
    private static readonly TimeSpan CatalogCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ContendingRequestWait = TimeSpan.FromSeconds(25);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly CatalogContext _db;
    private readonly IMemoryCache _cache;
    private readonly MaxioOptions _options;
    private readonly SubscriptionOperationLocks _locks;
    private readonly ILogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        CatalogContext db,
        IMemoryCache cache,
        IOptions<MaxioOptions> options,
        SubscriptionOperationLocks locks,
        ILogger<SubscriptionBillingService> logger)
    {
        _client = client;
        _db = db;
        _cache = cache;
        _options = options.Value;
        _locks = locks;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var catalog = await GetCatalogAsync(cancellationToken);
        return catalog.Plans;
    }

    public async Task<SubscribeResult> SubscribeAsync(
        BillingUser user,
        string productHandle,
        CancellationToken cancellationToken)
    {
        productHandle = productHandle?.Trim() ?? string.Empty;
        if (productHandle.Length == 0)
        {
            throw new SubscriptionBillingException(HttpStatusCode.BadRequest, "Invalid subscription", "A product handle is required.");
        }
        if (productHandle.Length > 255)
        {
            throw new SubscriptionBillingException(HttpStatusCode.BadRequest, "Invalid subscription", "The product handle is too long.");
        }

        if (string.IsNullOrWhiteSpace(user.Email) ||
            string.IsNullOrWhiteSpace(user.FirstName) ||
            string.IsNullOrWhiteSpace(user.LastName))
        {
            throw new SubscriptionBillingException(
                HttpStatusCode.UnprocessableEntity,
                "Incomplete billing profile",
                "First name, last name, and email are required before subscribing.");
        }

        var lockKey = $"{user.Id}\n{productHandle}";
        await using var operationLock = await _locks.AcquireAsync(lockKey, cancellationToken);

        var customerReference = BuildReference("eshop-cust", user.Id, 24);
        var subscriptionReference = BuildReference("eshop-sub", $"{user.Id}\n{productHandle}", 32);
        var prepared = await PrepareLinkAsync(
            user.Id,
            productHandle,
            customerReference,
            subscriptionReference,
            cancellationToken);

        if (!prepared.IsOwner)
        {
            if (prepared.Link.Status == SubscriptionBillingStatus.Pending)
            {
                prepared = await WaitForOwnerAsync(prepared.Link.Id, cancellationToken);
            }

            var existing = await FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                await ConfirmLinkAsync(prepared.Link.Id, existing, cancellationToken);
                return new SubscribeResult(MapSubscription(existing), false);
            }

            if (prepared.Link.Status == SubscriptionBillingStatus.Unknown)
            {
                throw new SubscriptionBillingException(
                    HttpStatusCode.ServiceUnavailable,
                    "Subscription status pending",
                    "A previous enrollment has an unknown outcome. No duplicate was created; try again shortly.");
            }

            prepared = await PrepareLinkAsync(
                user.Id,
                productHandle,
                customerReference,
                subscriptionReference,
                cancellationToken);
            if (!prepared.IsOwner)
            {
                throw new SubscriptionBillingException(
                    HttpStatusCode.Conflict,
                    "Subscription in progress",
                    "This subscription is already being processed. Try again shortly.");
            }
        }

        var alreadyExists = await FindSubscriptionAsync(subscriptionReference, cancellationToken);
        if (alreadyExists is not null)
        {
            await ConfirmLinkAsync(prepared.Link.Id, alreadyExists, cancellationToken);
            return new SubscribeResult(MapSubscription(alreadyExists), false);
        }

        var catalog = await GetCatalogAsync(cancellationToken);
        if (!catalog.Plans.Any(x => string.Equals(x.Handle, productHandle, StringComparison.Ordinal)))
        {
            await MarkLinkFailedAsync(prepared.Link.Id, cancellationToken);
            throw new SubscriptionBillingException(HttpStatusCode.NotFound, "Plan not found", "The requested subscription plan is not available.");
        }

        _ = await EnsureCustomerAsync(user, customerReference, cancellationToken);

        try
        {
            var created = await CreateSubscriptionAsync(
                productHandle,
                customerReference,
                subscriptionReference,
                catalog.PaymentCollectionMethod,
                cancellationToken);
            await ConfirmLinkAsync(prepared.Link.Id, created, cancellationToken);
            return new SubscribeResult(MapSubscription(created), true);
        }
        catch (Exception original) when (original is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var reconciled = await FindSubscriptionAsync(subscriptionReference, cancellationToken);
                if (reconciled is not null)
                {
                    await ConfirmLinkAsync(prepared.Link.Id, reconciled, cancellationToken);
                    return new SubscribeResult(MapSubscription(reconciled), false);
                }
            }
            catch (Exception reconciliationError) when (reconciliationError is not OperationCanceledException)
            {
                _logger.LogWarning(reconciliationError, "Could not reconcile Maxio subscription {SubscriptionReference}", subscriptionReference);
            }

            if (original is SubscriptionBillingException billingError &&
                (int)billingError.StatusCode is >= 400 and < 500)
            {
                await MarkLinkFailedAsync(prepared.Link.Id, cancellationToken);
                throw;
            }

            await MarkLinkUnknownAsync(prepared.Link.Id, cancellationToken);
            throw new SubscriptionBillingException(
                HttpStatusCode.ServiceUnavailable,
                "Subscription status pending",
                "Maxio did not confirm the enrollment outcome. No additional enrollment will be attempted until it is reconciled.",
                original);
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var customerReference = BuildReference("eshop-cust", userId, 24);
        var customer = await ReadCustomerAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        if (customer.Id is null)
        {
            throw IncompleteProviderData("customer.id");
        }

        var catalog = await GetCatalogAsync(cancellationToken);
        var familyHandles = new HashSet<string>(catalog.Plans.Select(x => x.Handle), StringComparer.Ordinal);
        var responses = await ListCustomerSubscriptionsAsync(customer.Id.Value, cancellationToken);
        var result = new List<SubscriptionDto>();

        foreach (var response in responses)
        {
            var subscription = response.Subscription;
            var handle = subscription?.Product?.Handle;
            if (subscription is null || handle is null || !familyHandles.Contains(handle))
            {
                continue;
            }

            result.Add(MapSubscription(subscription));
            await ReconcileLocalLinkAsync(userId, handle, customerReference, subscription, cancellationToken);
        }

        return result;
    }

    private async Task<CatalogSnapshot> GetCatalogAsync(CancellationToken cancellationToken)
    {
        var cacheKey = $"maxio-catalog:{_options.ProductFamilyHandle}";
        if (_cache.TryGetValue(cacheKey, out CatalogSnapshot? cached) && cached is not null)
        {
            return cached;
        }

        var loaded = await LoadCatalogAsync(cancellationToken);
        _cache.Set(cacheKey, loaded, CatalogCacheDuration);
        return loaded;
    }

    private async Task<CatalogSnapshot> LoadCatalogAsync(CancellationToken cancellationToken)
    {
        var families = await ListProductFamiliesAsync(cancellationToken);
        var family = families
            .Select(x => x.ProductFamily)
            .FirstOrDefault(x => x is not null && string.Equals(x.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal));

        if (family?.Id is null)
        {
            throw new SubscriptionBillingException(
                HttpStatusCode.ServiceUnavailable,
                "Billing catalog unavailable",
                "The configured Maxio product family could not be resolved.");
        }

        var site = await ReadSiteAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(site.Currency))
        {
            throw IncompleteProviderData("site.currency");
        }

        var products = new List<Product>();
        for (var page = 1; ; page++)
        {
            var responses = await ListProductsAsync(family.Id.Value.ToString(), page, cancellationToken);
            products.AddRange(responses.Select(x => x.Product).Where(x => x.ArchivedAt is null));
            if (responses.Count < CatalogPageSize)
            {
                break;
            }
        }

        var paymentCollectionMethod = site.RelationshipInvoicingEnabled switch
        {
            true => CollectionMethod.Remittance,
            false => CollectionMethod.Invoice,
            null => throw IncompleteProviderData("site.relationship_invoicing_enabled")
        };
        var plans = products.Select(product => MapPlan(product, site.Currency)).ToArray();
        return new CatalogSnapshot(plans, paymentCollectionMethod);
    }

    private static SubscriptionPlanDto MapPlan(Product product, string currency)
    {
        if (string.IsNullOrWhiteSpace(product.Handle)) throw IncompleteProviderData("product.handle");
        if (string.IsNullOrWhiteSpace(product.Name)) throw IncompleteProviderData("product.name");
        if (product.PriceInCents is null) throw IncompleteProviderData("product.price_in_cents");
        if (product.Interval is null) throw IncompleteProviderData("product.interval");
        if (product.IntervalUnit is null) throw IncompleteProviderData("product.interval_unit");

        return new SubscriptionPlanDto(
            product.Handle,
            product.Name,
            product.Description,
            product.PriceInCents.Value,
            product.Interval.Value,
            product.IntervalUnit.Value,
            currency);
    }

    private static SubscriptionDto MapSubscription(Subscription subscription)
    {
        if (subscription.Id is null) throw IncompleteProviderData("subscription.id");
        if (string.IsNullOrWhiteSpace(subscription.Reference)) throw IncompleteProviderData("subscription.reference");
        if (string.IsNullOrWhiteSpace(subscription.Product?.Handle)) throw IncompleteProviderData("subscription.product.handle");
        if (string.IsNullOrWhiteSpace(subscription.Product.Name)) throw IncompleteProviderData("subscription.product.name");
        if (subscription.ProductPriceInCents is null) throw IncompleteProviderData("subscription.product_price_in_cents");
        if (string.IsNullOrWhiteSpace(subscription.Currency)) throw IncompleteProviderData("subscription.currency");
        if (subscription.State is null) throw IncompleteProviderData("subscription.state");

        return new SubscriptionDto(
            subscription.Id.Value,
            subscription.Reference,
            subscription.Product.Handle,
            subscription.Product.Name,
            subscription.ProductPriceInCents.Value,
            subscription.Currency,
            subscription.State.Value,
            subscription.NextAssessmentAt,
            subscription.CurrentPeriodEndsAt);
    }

    private async Task<Customer> EnsureCustomerAsync(BillingUser user, string reference, CancellationToken cancellationToken)
    {
        var existing = await ReadCustomerAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            using var writeScope = MaxioWriteOnceScope.Begin();
            var response = await BoundedAsync(
                ct => _client.Customers.CreateCustomer(
                    body: new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = user.FirstName,
                            LastName = user.LastName,
                            Email = user.Email,
                            Reference = reference
                        }
                    },
                    ct: ct),
                cancellationToken);
            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                var raced = await ReadCustomerAsync(reference, cancellationToken);
                if (raced is not null) return raced;
                throw new SubscriptionBillingException(HttpStatusCode.UnprocessableEntity, "Customer rejected", "Maxio rejected the billing customer profile.", ex);
            }

            if (ex.Error.TryGetRawError(out var raw)) throw MapRawError(raw, "create customer", ex);
            throw ProviderUnavailable("create customer", ex);
        }
        catch (MaxioWriteRetryBlockedException ex)
        {
            var reconciled = await ReadCustomerAsync(reference, cancellationToken);
            if (reconciled is not null) return reconciled;
            throw ProviderUnavailable("create customer", ex);
        }
        catch (JsonException ex)
        {
            throw ProviderUnavailable("create customer", ex);
        }
        catch (HttpRequestException ex)
        {
            throw ProviderUnavailable("create customer", ex);
        }
    }

    private async Task<Subscription> CreateSubscriptionAsync(
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CollectionMethod paymentCollectionMethod,
        CancellationToken cancellationToken)
    {
        try
        {
            using var writeScope = MaxioWriteOnceScope.Begin();
            var response = await BoundedAsync(
                ct => _client.Subscriptions.CreateSubscription(
                    body: new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = productHandle,
                            CustomerReference = customerReference,
                            Reference = subscriptionReference,
                            PaymentCollectionMethod = paymentCollectionMethod
                        }
                    },
                    ct: ct),
                cancellationToken);

            return response.Subscription ?? throw IncompleteProviderData("subscription");
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out _))
            {
                throw new SubscriptionBillingException(HttpStatusCode.UnprocessableEntity, "Subscription rejected", "Maxio rejected the subscription request.", ex);
            }

            if (ex.Error.TryGetRawError(out var raw)) throw MapRawError(raw, "create subscription", ex);
            throw ProviderUnavailable("create subscription", ex);
        }
        catch (JsonException ex)
        {
            throw ProviderUnavailable("create subscription", ex);
        }
        catch (HttpRequestException ex)
        {
            throw ProviderUnavailable("create subscription", ex);
        }
    }

    private async Task<Customer?> ReadCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(ct => _client.Customers.ReadCustomerByReference(reference, ct: ct), cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError(ex.Error, "read customer", ex);
        }
        catch (JsonException ex)
        {
            throw ProviderUnavailable("read customer", ex);
        }
        catch (HttpRequestException ex)
        {
            throw ProviderUnavailable("read customer", ex);
        }
    }

    private async Task<Subscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(ct => _client.Subscriptions.FindSubscription(reference: reference, ct: ct), cancellationToken);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _)) return null;
            if (ex.Error.TryGetRawError(out var raw)) throw MapRawError(raw, "find subscription", ex);
            throw ProviderUnavailable("find subscription", ex);
        }
        catch (JsonException ex)
        {
            throw ProviderUnavailable("find subscription", ex);
        }
        catch (HttpRequestException ex)
        {
            throw ProviderUnavailable("find subscription", ex);
        }
    }

    private async Task<IReadOnlyList<ProductFamilyResponse>> ListProductFamiliesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await BoundedAsync(
                ct => _client.ProductFamilies.ListProductFamilies(
                    dateField: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    ct: ct),
                cancellationToken);
        }
        catch (SdkException<RawError> ex) { throw MapRawError(ex.Error, "list product families", ex); }
        catch (JsonException ex) { throw ProviderUnavailable("list product families", ex); }
        catch (HttpRequestException ex) { throw ProviderUnavailable("list product families", ex); }
    }

    private async Task<IReadOnlyList<ProductResponse>> ListProductsAsync(string familyId, int page, CancellationToken cancellationToken)
    {
        try
        {
            return await BoundedAsync(
                ct => _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyId,
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: page,
                    perPage: CatalogPageSize,
                    ct: ct),
                cancellationToken);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out _))
            {
                throw new SubscriptionBillingException(HttpStatusCode.ServiceUnavailable, "Billing catalog unavailable", "The configured Maxio product family could not be loaded.", ex);
            }
            if (ex.Error.TryGetRawError(out var raw)) throw MapRawError(raw, "list products", ex);
            throw ProviderUnavailable("list products", ex);
        }
        catch (JsonException ex) { throw ProviderUnavailable("list products", ex); }
        catch (HttpRequestException ex) { throw ProviderUnavailable("list products", ex); }
    }

    private async Task<Site> ReadSiteAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(ct => _client.Sites.ReadSite(ct: ct), cancellationToken);
            return response.Site;
        }
        catch (SdkException<RawError> ex) { throw MapRawError(ex.Error, "read site", ex); }
        catch (JsonException ex) { throw ProviderUnavailable("read site", ex); }
        catch (HttpRequestException ex) { throw ProviderUnavailable("read site", ex); }
    }

    private async Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        try
        {
            return await BoundedAsync(ct => _client.Customers.ListCustomerSubscriptions(customerId, ct: ct), cancellationToken);
        }
        catch (SdkException<RawError> ex) { throw MapRawError(ex.Error, "list customer subscriptions", ex); }
        catch (JsonException ex) { throw ProviderUnavailable("list customer subscriptions", ex); }
        catch (HttpRequestException ex) { throw ProviderUnavailable("list customer subscriptions", ex); }
    }

    private async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SubscriptionBillingException(HttpStatusCode.GatewayTimeout, "Billing provider timeout", "Maxio did not respond before the request deadline.", ex);
        }
    }

    private async Task<PreparedLink> PrepareLinkAsync(
        string userId,
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        _db.ChangeTracker.Clear();
        var now = DateTimeOffset.UtcNow;
        var leaseToken = Guid.NewGuid().ToString("N");
        var link = await _db.SubscriptionBillingLinks
            .SingleOrDefaultAsync(x => x.UserId == userId && x.ProductHandle == productHandle, cancellationToken);

        if (link is null)
        {
            link = new SubscriptionBillingLink(userId, productHandle, customerReference, subscriptionReference, leaseToken, now, now.Add(LeaseDuration));
            _db.SubscriptionBillingLinks.Add(link);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                return new PreparedLink(link, true);
            }
            catch (DbUpdateException ex)
            {
                _db.Entry(link).State = EntityState.Detached;
                link = await _db.SubscriptionBillingLinks
                    .SingleOrDefaultAsync(x => x.UserId == userId && x.ProductHandle == productHandle, cancellationToken);
                if (link is null)
                {
                    throw new SubscriptionBillingException(
                        HttpStatusCode.InternalServerError,
                        "Subscription persistence failed",
                        "The subscription request could not be recorded.",
                        ex);
                }
            }
        }

        if (link.Status is SubscriptionBillingStatus.Confirmed or SubscriptionBillingStatus.Unknown)
        {
            return new PreparedLink(link, false);
        }

        if (link.Status == SubscriptionBillingStatus.Pending && link.LeaseExpiresAt > now)
        {
            return new PreparedLink(link, false);
        }

        link.Claim(leaseToken, now, now.Add(LeaseDuration));
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return new PreparedLink(link, true);
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            var winner = await _db.SubscriptionBillingLinks
                .SingleAsync(x => x.UserId == userId && x.ProductHandle == productHandle, cancellationToken);
            return new PreparedLink(winner, false);
        }
    }

    private async Task<PreparedLink> WaitForOwnerAsync(int linkId, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(ContendingRequestWait);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            _db.ChangeTracker.Clear();
            var link = await _db.SubscriptionBillingLinks.AsNoTracking().SingleAsync(x => x.Id == linkId, cancellationToken);
            if (link.Status != SubscriptionBillingStatus.Pending || link.LeaseExpiresAt <= DateTimeOffset.UtcNow)
            {
                return new PreparedLink(link, false);
            }
        }

        throw new SubscriptionBillingException(HttpStatusCode.Conflict, "Subscription in progress", "This subscription is already being processed. Try again shortly.");
    }

    private async Task ConfirmLinkAsync(int linkId, Subscription subscription, CancellationToken cancellationToken)
    {
        if (subscription.Id is null) throw IncompleteProviderData("subscription.id");
        if (subscription.Customer?.Id is null) throw IncompleteProviderData("subscription.customer.id");
        _db.ChangeTracker.Clear();
        var link = await _db.SubscriptionBillingLinks.SingleAsync(x => x.Id == linkId, cancellationToken);
        link.Confirm(subscription.Customer.Id.Value, subscription.Id.Value, DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkLinkFailedAsync(int linkId, CancellationToken cancellationToken)
    {
        _db.ChangeTracker.Clear();
        var link = await _db.SubscriptionBillingLinks.SingleAsync(x => x.Id == linkId, cancellationToken);
        link.MarkFailed(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkLinkUnknownAsync(int linkId, CancellationToken cancellationToken)
    {
        _db.ChangeTracker.Clear();
        var link = await _db.SubscriptionBillingLinks.SingleAsync(x => x.Id == linkId, cancellationToken);
        link.MarkUnknown(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task ReconcileLocalLinkAsync(
        string userId,
        string productHandle,
        string customerReference,
        Subscription subscription,
        CancellationToken cancellationToken)
    {
        if (subscription.Id is null || subscription.Customer?.Id is null || string.IsNullOrWhiteSpace(subscription.Reference))
        {
            return;
        }

        _db.ChangeTracker.Clear();
        var link = await _db.SubscriptionBillingLinks
            .SingleOrDefaultAsync(x => x.UserId == userId && x.ProductHandle == productHandle, cancellationToken);
        if (link is null)
        {
            var now = DateTimeOffset.UtcNow;
            link = new SubscriptionBillingLink(userId, productHandle, customerReference, subscription.Reference, Guid.NewGuid().ToString("N"), now, now);
            _db.SubscriptionBillingLinks.Add(link);
        }
        link.Confirm(subscription.Customer.Id.Value, subscription.Id.Value, DateTimeOffset.UtcNow);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogDebug(ex, "A concurrent request already reconciled subscription {SubscriptionReference}", subscription.Reference);
        }
    }

    private static string BuildReference(string prefix, string input, int hashCharacters)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
        return $"{prefix}-{hash[..hashCharacters]}";
    }

    private static SubscriptionBillingException MapRawError(RawError raw, string operation, Exception inner)
    {
        if (raw.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new SubscriptionBillingException(HttpStatusCode.BadGateway, "Billing provider authentication failed", "The billing provider credentials were rejected.", inner);
        }

        if ((int)raw.StatusCode is >= 400 and < 500)
        {
            return new SubscriptionBillingException(raw.StatusCode, "Billing request rejected", "Maxio rejected the billing request.", inner);
        }

        return ProviderUnavailable(operation, inner);
    }

    private static SubscriptionBillingException ProviderUnavailable(string operation, Exception inner) =>
        new(HttpStatusCode.BadGateway, "Billing provider unavailable", $"Maxio could not complete the {operation} operation.", inner);

    private static SubscriptionBillingException IncompleteProviderData(string field) =>
        new(HttpStatusCode.UnprocessableEntity, "Billing data incomplete", $"Maxio returned an incomplete value for {field}.");

    private sealed record CatalogSnapshot(
        IReadOnlyList<SubscriptionPlanDto> Plans,
        CollectionMethod PaymentCollectionMethod);
    private sealed record PreparedLink(SubscriptionBillingLink Link, bool IsOwner);
}
