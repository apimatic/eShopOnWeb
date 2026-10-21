using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Recipes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>Site facts read once per process (knowledge/rules.md R3).</summary>
public sealed record MaxioSiteFacts(bool IsTest, bool RelationshipInvoicing, string? Currency, string? Subdomain)
{
    /// <summary>
    /// How a shopper who subscribes here pays. This shop captures no card, so the merchant bills the
    /// shopper by invoice: <c>remittance</c> on a Relationship Invoicing site, <c>invoice</c> on a legacy
    /// Statements site (R14, D3).
    /// </summary>
    public CollectionMethod InvoiceCollectionMethod =>
        RelationshipInvoicing ? CollectionMethod.Remittance : CollectionMethod.Invoice;
}

/// <summary>What a customer create ended in. <see cref="Customer"/> is null only when the outcome is unknown.</summary>
public sealed record CustomerCreateResult(Customer? Customer, bool OutcomeUnknown, bool Conflicted, string? Reason);

/// <summary>
/// The one class that calls the Maxio SDK. Every call goes through Maxio's limit of four requests in
/// flight per site; reads get a whole-call deadline; writes never get a deadline under 120 s and
/// never run under the caller's request-aborted token; a 429 pauses all calls for minutes.
/// </summary>
public class MaxioGateway
{
    /// <summary>The whole-call deadline of one read (one page of a list).</summary>
    public static readonly TimeSpan ReadBudget = TimeSpan.FromSeconds(30);

    /// <summary>How long the read-back after a write with an unknown outcome may take.</summary>
    public static readonly TimeSpan ReadBackBudget = TimeSpan.FromSeconds(30);

    /// <summary>How long every call pauses after Maxio answers 429 (knowledge/rules.md R10).</summary>
    public static readonly TimeSpan ThrottlePause = TimeSpan.FromMinutes(3);

    /// <summary>The page ceiling for the lists this shop reads (200 rows a page).</summary>
    public const int MaxPages = 10;

    private static readonly TimeSpan PlanCacheDuration = TimeSpan.FromMinutes(5);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _time;
    private readonly ILogger<MaxioGateway> _logger;
    private readonly SemaphoreSlim _siteLock = new(1, 1);
    private MaxioSiteFacts? _site;
    private long _pausedUntilUtcTicks;

    public MaxioGateway(MaxioAdvancedBillingClient client, MaxioOptions options, IMemoryCache cache,
        TimeProvider time, ILogger<MaxioGateway> logger)
    {
        _client = client;
        _options = options;
        _cache = cache;
        _time = time;
        _logger = logger;
    }

    private string Site => _options.RequestLimitKey;

    // ---------------------------------------------------------------- site

    /// <summary>The site's facts, read on first use and kept for the process (a failure is not kept).</summary>
    public virtual async Task<MaxioSiteFacts> GetSiteAsync(CancellationToken cancellationToken)
    {
        if (_site is { } known) return known;

        await _siteLock.WaitAsync(cancellationToken);
        try
        {
            if (_site is { } raced) return raced;

            var answer = await ReadAsync(token => _client.Sites.ReadSite(cancellationToken: token), cancellationToken);
            var site = answer.Site;
            if (site.RelationshipInvoicingEnabled is not bool relationshipInvoicing)
            {
                throw new SubscriptionBillingException(SubscriptionBillingError.ProviderUnavailable,
                    "The billing site did not report its invoicing architecture.");
            }

            _site = new MaxioSiteFacts(site.Test == true, relationshipInvoicing, site.Currency, site.Subdomain);
            _logger.LogInformation(
                "Maxio site {Subdomain}: test={IsTest}, relationshipInvoicing={RelationshipInvoicing}, currency={Currency}",
                _site.Subdomain, _site.IsTest, _site.RelationshipInvoicing, _site.Currency);
            return _site;
        }
        finally
        {
            _siteLock.Release();
        }
    }

    // ---------------------------------------------------------------- plans

    /// <summary>
    /// The configured family's plans, archived ones excluded, read to an empty page (R8) and cached
    /// for a few minutes. <see cref="PagedRows{T}.IsComplete"/> is false when the ceiling cut it short.
    /// </summary>
    public virtual async Task<PagedRows<Product>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var cacheKey = "maxio:plans:" + _options.ProductFamilyHandle;
        if (_cache.TryGetValue(cacheKey, out PagedRows<Product>? cached) && cached is not null)
            return cached;

        var familyId = "handle:" + _options.ProductFamilyHandle!.Trim();
        PagedRows<ProductResponse> rows;
        try
        {
            rows = await ListAll.PagesOrPartial(
                (page, perPage) => ReadAsync(
                    token => _client.ProductFamilies.ListProductsForProductFamily(
                        new ListProductsForProductFamilyRequest
                        {
                            ProductFamilyId = familyId,
                            Page = page,
                            PerPage = perPage,
                            IncludeArchived = false,
                        },
                        cancellationToken: token),
                    cancellationToken),
                MaxPages,
                key: row => row.Product.Id,
                cancellationToken: cancellationToken);
        }
        catch (ApiException<ListProductsForProductFamilyError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogError("Maxio product family {Family} was not found (404); check Maxio:ProductFamilyHandle",
                _options.ProductFamilyHandle);
            throw new SubscriptionBillingException(SubscriptionBillingError.ProviderUnavailable,
                "The configured subscription catalog was not found in the billing system.", innerException: ex);
        }

        var plans = rows.Items
            .Select(row => row.Product)
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .ToList();
        var result = new PagedRows<Product>(plans, rows.IsComplete);
        _cache.Set(cacheKey, result, PlanCacheDuration);
        return result;
    }

    // ---------------------------------------------------------------- customers

    /// <summary>The customer with this reference, or null on Maxio's 404 ("not there").</summary>
    public virtual Task<Customer?> FindCustomerAsync(string reference, CancellationToken cancellationToken) =>
        ReadAsync(token => SafeSignup.FindCustomer(_client, reference, token), cancellationToken);

    /// <summary>
    /// Creates the customer once, with this write's own <c>uniqueness_token</c> at the body root (R12).
    /// A 422 is settled by reading the reference back (a concurrent create won the race). A timeout, a
    /// dropped connection, a 5xx, a 408, a 409 or an unreadable 2xx is an unknown outcome (R11): settled
    /// by the same read-back, else reported as unknown — never as a failure.
    /// </summary>
    /// <exception cref="SubscriptionBillingException">Rejected (422 and nobody holds the reference) or throttled (429).</exception>
    public virtual async Task<CustomerCreateResult> CreateCustomerAsync(string reference, string email,
        string uniquenessToken, CancellationToken cancellationToken)
    {
        ThrowIfPaused();
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = FirstNameFrom(email),
                LastName = "eShop customer",
                Email = email,
                Reference = reference,
            },
        };
        // Beside "customer", where Maxio reads it. Not declared by the spec (docs/models.md).
        body.AdditionalProperties.Set("uniqueness_token", uniquenessToken);

        try
        {
            // A write: no deadline under 120 s and not the caller's token (an aborted browser request
            // must not abandon a create Maxio is working on). The client's own timeouts are 130 s.
            var created = await RequestLimit.Run(Site,
                () => _client.Customers.CreateCustomer(new CreateCustomerOperationRequest { Body = body }),
                cancellationToken);
            return new CustomerCreateResult(created.Customer, false, false, null);
        }
        catch (ApiException<CreateCustomerError> ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var reason = DescribeCustomerRejection(ex);
            // "Reference: has already been taken" is the race with another create: read it back (L2 step 4).
            var existing = await ReadBackCustomerAsync(reference);
            if (existing is not null)
                return new CustomerCreateResult(existing, false, false, null);

            _logger.LogWarning("Maxio rejected customer create for {Reference}: 422 {Reason}", reference, reason);
            throw new SubscriptionBillingException(SubscriptionBillingError.Rejected,
                "The billing system rejected the customer record.", innerException: ex);
        }
        catch (ApiException ex) when ((int)ex.StatusCode == 429)
        {
            throw Throttled(ex);
        }
        catch (Exception ex) when (MaxioErrors.OutcomeUnknown(ex))
        {
            var conflicted = ex is ApiException { StatusCode: HttpStatusCode.Conflict };
            _logger.LogWarning(ex, "Maxio customer create for {Reference} has an unknown outcome; reading back", reference);
            try
            {
                var found = await ReadBackCustomerAsync(reference);
                if (found is not null)
                    return new CustomerCreateResult(found, false, false, null);
            }
            catch (Exception readBack)
            {
                _logger.LogWarning(readBack, "Read-back of customer {Reference} failed", reference);
            }

            return new CustomerCreateResult(null, true, conflicted, MaxioErrors.Describe(ex));
        }
    }

    // ---------------------------------------------------------------- subscriptions

    /// <summary>
    /// A subscription the customer holds on the plan that leaves no room for a second one (L2 step 6, L3),
    /// skipping the one under this signup's own reference. Any failure is thrown, never read as "nothing held".
    /// </summary>
    public virtual Task<Subscription?> FindHeldSubscriptionAsync(Customer customer, string planHandle,
        string signupReference, CancellationToken cancellationToken)
    {
        ThrowIfPaused();
        return GuardThrottle(() => RequestLimit.Run(Site, async () =>
        {
            // Several pages, one at a time, in one slot: a deadline per walk of a few pages.
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(ReadBudget * 2);
            return await SafeSignup.FindHeldSubscription(_client, customer, planHandle, signupReference, deadline.Token);
        }, cancellationToken));
    }

    /// <summary>The subscription with this id, or null on Maxio's 404.</summary>
    public virtual Task<Subscription?> ReadSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken) =>
        ReadAsync(async token =>
        {
            try
            {
                var answer = await _client.Subscriptions.ReadSubscription(
                    new ReadSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: token);
                return answer.Subscription;
            }
            catch (ApiException<RawError> notThere) when (notThere.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }, cancellationToken);

    /// <summary>The subscription holding this reference, or null on Maxio's 404.</summary>
    public virtual Task<Subscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken) =>
        ReadAsync(async token =>
        {
            try
            {
                var answer = await _client.Subscriptions.FindSubscription(
                    new FindSubscriptionRequest { Reference = reference }, cancellationToken: token);
                return answer.Subscription;
            }
            catch (ApiException<FindSubscriptionError> notThere) when (notThere.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }, cancellationToken);

    /// <summary>
    /// Signs the customer up through <see cref="SafeSignup"/>: lookup by reference first, then one create
    /// with the body-root token and an explicit collection method, settled by reading back.
    /// </summary>
    /// <exception cref="ApiException{CreateSubscriptionError}">Maxio rejected the create (a 4xx other than 408, 409, 429).</exception>
    /// <exception cref="SubscriptionBillingException">Throttled (429).</exception>
    public virtual Task<SignupResult> SignUpAsync(string customerReference, string planHandle, string reference,
        string uniquenessToken, CollectionMethod collectionMethod)
    {
        ThrowIfPaused();
        var subscription = new CreateSubscription
        {
            ProductHandle = planHandle,
            CustomerReference = customerReference,
        };

        // CancellationToken.None: once sent, a create is never abandoned by the caller (R11).
        return GuardThrottle(() => RequestLimit.Run(Site, () => SafeSignup.SignUp(
            _client, subscription, reference, uniquenessToken, collectionMethod, ReadBackBudget,
            CancellationToken.None)));
    }

    /// <summary>All of the customer's subscriptions, read to an empty page (R8), ascending by id.</summary>
    public virtual async Task<PagedRows<Subscription>> ListCustomerSubscriptionsAsync(int customerId,
        CancellationToken cancellationToken)
    {
        var rows = await ListAll.PagesOrPartial(
            (page, perPage) => ReadAsync(
                token => _client.Subscriptions.ListSubscriptions(
                    new ListSubscriptionsRequest
                    {
                        CustomerId = customerId,
                        Page = page,
                        PerPage = perPage,
                        Sort = SubscriptionSort.Id,
                        Direction = SortingDirection.Asc,
                    },
                    cancellationToken: token),
                cancellationToken),
            MaxPages,
            key: row => row.Subscription?.Id,
            cancellationToken: cancellationToken);

        var subscriptions = rows.Items
            .Select(row => row.Subscription ?? throw new InvalidOperationException(
                "Maxio answered the subscription list with a row that has no subscription in it."))
            .ToList();
        return new PagedRows<Subscription>(subscriptions, rows.IsComplete);
    }

    // ---------------------------------------------------------------- plumbing

    /// <summary>One read: a slot of the request limit and a whole-call deadline linked to the caller's token.</summary>
    private async Task<T> ReadAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        ThrowIfPaused();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ReadBudget);
        return await GuardThrottle(() => RequestLimit.Run(Site, () => call(deadline.Token), deadline.Token));
    }

    private async Task<Customer?> ReadBackCustomerAsync(string reference)
    {
        // Its own timer: the read-back must not depend on a token that may already have fired.
        using var limit = new CancellationTokenSource(ReadBackBudget);
        return await FindCustomerAsync(reference, limit.Token);
    }

    private async Task<T> GuardThrottle<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (ApiException ex) when ((int)ex.StatusCode == 429)
        {
            throw Throttled(ex);
        }
    }

    private SubscriptionBillingException Throttled(Exception ex)
    {
        var until = _time.GetUtcNow() + ThrottlePause;
        Interlocked.Exchange(ref _pausedUntilUtcTicks, until.UtcTicks);
        _logger.LogWarning("Maxio answered 429; pausing all Maxio calls until {Until:o}", until);
        return new SubscriptionBillingException(SubscriptionBillingError.Throttled,
            "The billing system is busy. Try again in a few minutes.", ThrottlePause, ex);
    }

    private void ThrowIfPaused()
    {
        var until = Interlocked.Read(ref _pausedUntilUtcTicks);
        var now = _time.GetUtcNow().UtcTicks;
        if (until > now)
        {
            throw new SubscriptionBillingException(SubscriptionBillingError.Throttled,
                "The billing system is busy. Try again in a few minutes.", TimeSpan.FromTicks(until - now));
        }
    }

    private static string FirstNameFrom(string email)
    {
        var at = email.IndexOf('@');
        var local = at > 0 ? email[..at] : email;
        return string.IsNullOrWhiteSpace(local) ? "eShop" : local;
    }

    private static string DescribeCustomerRejection(ApiException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out var body) && body.Errors is { } errors)
        {
            if (errors.TryGetListOfString(out var list)) return string.Join("; ", list);
            if (errors.TryGetCustomerError(out var single) && single.Customer is { } text) return text;
        }

        return ex.Error.TryGetRawError(out var raw) ? raw.ReadAsString() : string.Empty;
    }
}

/// <summary>How a Maxio failure is classified (docs/error-handling.md, "Maxio: reading a rejection").</summary>
public static class MaxioErrors
{
    /// <summary>True when a write may or may not have happened (R11): settle it by reading back.</summary>
    public static bool OutcomeUnknown(Exception ex) => ex switch
    {
        // 409: the first copy arrived. 408 and 5xx: Maxio may have acted. 2xx: accepted, answer unreadable.
        ApiException api => (int)api.StatusCode is 408 or 409 or >= 500 or (>= 200 and < 300),
        SdkConnectionException => true,
        OperationCanceledException => true,
        _ => false,
    };

    /// <summary>The status and Maxio's own message, for our logs (never for callers).</summary>
    public static string Describe(Exception ex) => ex switch
    {
        ApiException<CreateSubscriptionError> typed when typed.Error.TryGetErrorListResponse1(out var list) =>
            $"{(int)typed.StatusCode}: {string.Join("; ", list.Errors)}",
        ApiException<CreateSubscriptionError> typed when typed.Error.TryGetRawError(out var raw) =>
            $"{(int)typed.StatusCode}: {raw.ReadAsString()}",
        ApiException<RawError> raw => $"{(int)raw.StatusCode}: {raw.Error.ReadAsString()}",
        ApiException api => $"{(int)api.StatusCode}: {api.Message}",
        _ => $"{ex.GetType().Name}: {ex.Message}",
    };
}
