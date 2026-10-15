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
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingGateway"/> over the Maxio Advanced Billing SDK. Every call is bounded
/// by <see cref="MaxioSettings.RequestTimeoutSeconds"/>, every SDK failure is translated here, and the
/// two writes settle a transport failure by re-reading the provider before reporting anything.
/// </summary>
public class MaxioSubscriptionBillingGateway : ISubscriptionBillingGateway
{
    public const int PlanPageSize = 200;
    public const int MaxPlanPages = 5;

    private const string PlanCacheKey = "maxio:plan-catalog";
    private const string Unavailable = "The billing provider is currently unavailable. Please try again later.";

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioSubscriptionBillingGateway> _logger;
    private readonly TimeSpan _budget;
    private readonly CollectionMethod _collectionMethod;

    public MaxioSubscriptionBillingGateway(MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        IMemoryCache cache,
        ILogger<MaxioSubscriptionBillingGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _cache = cache;
        _logger = logger;
        _budget = TimeSpan.FromSeconds(_settings.RequestTimeoutSeconds);
        // Validated at startup, so the value is always a declared member.
        _collectionMethod = CollectionMethod.TryGetKnownValue(_settings.PaymentCollectionMethod.Trim(), out var method)
            ? method
            : CollectionMethod.Remittance;
    }

    public string CatalogKey => _settings.ProductFamilyHandle!;

    public async Task<PlanCatalog> ListPlansAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(PlanCacheKey, out PlanCatalog? cached) && cached is not null)
        {
            return cached;
        }

        const string operation = "ListProductsForProductFamily";
        var plans = new List<SubscriptionPlan>();
        var isComplete = false;

        // The operation is a plain list (no SDK pagination): drive page/per_page ourselves, stop on a
        // short page, and never walk more than MaxPlanPages whatever the provider returns.
        for (var page = 1; page <= MaxPlanPages; page++)
        {
            IReadOnlyList<ProductResponse> batch;
            try
            {
                batch = await ReadAsync(operation, token => _client.ProductFamilies.ListProductsForProductFamily(
                    new ListProductsForProductFamilyRequest
                    {
                        ProductFamilyId = $"handle:{_settings.ProductFamilyHandle}",
                        Page = page,
                        PerPage = PlanPageSize
                    },
                    cancellationToken: token), cancellationToken);
            }
            catch (ApiException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out _))
                {
                    _logger.LogError("Maxio product family configured in Maxio:ProductFamilyHandle was not found (HTTP {StatusCode}).", (int)ex.StatusCode);
                    throw new BillingUnavailableException("The subscription plan catalog is not available.", ex);
                }
                if (ex.Error.TryGetRawError(out _))
                {
                    throw UnavailableFrom(operation, ex.StatusCode, ex);
                }
                throw UnavailableFrom(operation, ex.StatusCode, ex);
            }

            foreach (var product in batch.Select(item => item?.Product))
            {
                if (product?.Handle is null || product.ArchivedAt is not null)
                {
                    continue;
                }
                plans.Add(new SubscriptionPlan(
                    product.Handle,
                    product.Name ?? product.Handle,
                    product.Description,
                    product.PriceInCents,
                    product.Interval,
                    product.IntervalUnit?.Value));
            }

            if (batch.Count < PlanPageSize)
            {
                isComplete = true;
                break;
            }
        }

        if (!isComplete)
        {
            _logger.LogWarning("Plan catalog truncated at {MaxPages} pages of {PageSize}.", MaxPlanPages, PlanPageSize);
        }

        var defaultPlan = string.IsNullOrWhiteSpace(_settings.DefaultPlanHandle) ? null : _settings.DefaultPlanHandle.Trim();
        var catalog = new PlanCatalog(plans, isComplete,
            defaultPlan is not null && plans.Any(p => p.Handle == defaultPlan) ? defaultPlan : null);

        if (_settings.PlanCacheSeconds > 0)
        {
            _cache.Set(PlanCacheKey, catalog, TimeSpan.FromSeconds(_settings.PlanCacheSeconds));
        }
        _logger.LogInformation("Loaded {PlanCount} subscription plans (complete: {IsComplete}).", plans.Count, isComplete);
        return catalog;
    }

    public async Task<int?> FindCustomerIdAsync(string customerReference, CancellationToken cancellationToken)
    {
        const string operation = "ReadCustomerByReference";
        CustomerResponse response;
        try
        {
            response = await ReadAsync(operation, token => _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = customerReference },
                cancellationToken: token), cancellationToken);
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (ApiException<RawError> ex)
        {
            throw UnavailableFrom(operation, ex.StatusCode, ex);
        }

        // A 2xx without an id is not "no customer" — it gates a create, so treat it as a failure.
        return response.Customer?.Id ?? throw UnavailableFrom(operation, null, null, "customer response carried no id");
    }

    public async Task<int> EnsureCustomerAsync(BillingCustomer customer, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerIdAsync(customer.Reference, cancellationToken);
        return existing ?? await CreateCustomerAsync(customer, cancellationToken);
    }

    private async Task<int> CreateCustomerAsync(BillingCustomer customer, CancellationToken cancellationToken)
    {
        const string operation = "CreateCustomer";
        try
        {
            var response = await Bounded(token => _client.Customers.CreateCustomer(
                new CreateCustomerOperationRequest
                {
                    Body = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = customer.FirstName,
                            LastName = customer.LastName,
                            Email = customer.Email,
                            Reference = customer.Reference
                        }
                    }
                },
                cancellationToken: token), cancellationToken);

            if (response.Customer?.Id is int id)
            {
                _logger.LogInformation("Created Maxio customer {CustomerId}.", id);
                return id;
            }
            return await SettleCustomerAsync(customer.Reference, null, "create returned no customer id");
        }
        catch (ApiException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var body))
            {
                // One customer per reference: a concurrent creator surfaces here. Re-read before rejecting.
                var raced = await FindCustomerIdAsync(customer.Reference, cancellationToken);
                if (raced is int id)
                {
                    return id;
                }
                var messages = DescribeCustomerErrors(body);
                _logger.LogWarning("Maxio rejected customer creation (HTTP {StatusCode}): {Errors}", (int)ex.StatusCode, messages);
                throw new BillingRejectedException($"The billing provider rejected the customer: {messages}", ex);
            }
            if (ex.Error.TryGetRawError(out _))
            {
                throw UnavailableFrom(operation, ex.StatusCode, ex);
            }
            throw UnavailableFrom(operation, ex.StatusCode, ex);
        }
        catch (ResponseDeserializationException ex) when (IsSuccess(ex.StatusCode))
        {
            return await SettleCustomerAsync(customer.Reference, ex, "unreadable success response");
        }
        catch (ResponseDeserializationException ex)
        {
            throw RejectedOrUnavailable(operation, ex);
        }
        catch (SdkConnectionException ex)
        {
            return await SettleCustomerAsync(customer.Reference, ex, ex.GetType().Name);
        }
        catch (TimeoutException ex)
        {
            return await SettleCustomerAsync(customer.Reference, ex, "request budget exceeded");
        }
        catch (AuthSchemeException ex)
        {
            throw UnavailableFrom(operation, null, ex);
        }
    }

    private async Task<int> SettleCustomerAsync(string reference, Exception? cause, string reason)
    {
        _logger.LogWarning("CreateCustomer outcome unknown ({Reason}); re-reading by reference.", reason);
        try
        {
            using var settle = new CancellationTokenSource(_budget);
            if (await FindCustomerIdAsync(reference, settle.Token) is int id)
            {
                return id;
            }
        }
        catch (Exception ex) when (ex is BillingUnavailableException or OperationCanceledException)
        {
            _logger.LogWarning("Re-reading the customer failed: {Reason}", ex.Message);
        }
        throw new BillingOutcomeUnknownException("The customer could not be confirmed at the billing provider.", cause);
    }

    public async Task<BillingSubscription?> FindSubscriptionByReferenceAsync(string subscriptionReference, CancellationToken cancellationToken)
    {
        const string operation = "FindSubscription";
        SubscriptionResponse response;
        try
        {
            response = await ReadAsync(operation, token => _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = subscriptionReference },
                cancellationToken: token), cancellationToken);
        }
        catch (ApiException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }
            if (ex.Error.TryGetRawError(out _))
            {
                throw UnavailableFrom(operation, ex.StatusCode, ex);
            }
            throw UnavailableFrom(operation, ex.StatusCode, ex);
        }

        // Only a 404 means "absent"; a 2xx without a subscription is a failure, since this read gates a create.
        return response.Subscription is { } subscription
            ? Map(subscription)
            : throw UnavailableFrom(operation, null, null, "lookup response carried no subscription");
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(int customerId, string planHandle, string subscriptionReference,
        CancellationToken cancellationToken)
    {
        const string operation = "CreateSubscription";
        try
        {
            var response = await Bounded(token => _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = planHandle,
                            CustomerId = customerId,
                            Reference = subscriptionReference,
                            PaymentCollectionMethod = _collectionMethod
                        }
                    }
                },
                cancellationToken: token), cancellationToken);

            if (response.Subscription is { } subscription)
            {
                return Map(subscription);
            }
            return await SettleSubscriptionAsync(subscriptionReference, null, "create returned no subscription");
        }
        catch (ApiException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var body))
            {
                // If the provider refused because this reference already exists, that subscription is ours.
                var existing = await FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
                if (existing is not null)
                {
                    return existing;
                }
                var messages = string.Join("; ", body.Errors ?? Array.Empty<string>());
                _logger.LogWarning("Maxio rejected subscription creation on plan {PlanHandle} (HTTP {StatusCode}): {Errors}",
                    planHandle, (int)ex.StatusCode, messages);
                throw new BillingRejectedException($"The billing provider rejected the subscription: {messages}", ex);
            }
            if (ex.Error.TryGetRawError(out _))
            {
                throw UnavailableFrom(operation, ex.StatusCode, ex);
            }
            throw UnavailableFrom(operation, ex.StatusCode, ex);
        }
        catch (ResponseDeserializationException ex) when (IsSuccess(ex.StatusCode))
        {
            return await SettleSubscriptionAsync(subscriptionReference, ex, "unreadable success response");
        }
        catch (ResponseDeserializationException ex)
        {
            throw RejectedOrUnavailable(operation, ex);
        }
        catch (SdkConnectionException ex)
        {
            return await SettleSubscriptionAsync(subscriptionReference, ex, ex.GetType().Name);
        }
        catch (TimeoutException ex)
        {
            return await SettleSubscriptionAsync(subscriptionReference, ex, "request budget exceeded");
        }
        catch (AuthSchemeException ex)
        {
            throw UnavailableFrom(operation, null, ex);
        }
    }

    private async Task<BillingSubscription> SettleSubscriptionAsync(string reference, Exception? cause, string reason)
    {
        _logger.LogWarning("CreateSubscription outcome unknown ({Reason}); re-reading by reference.", reason);
        try
        {
            using var settle = new CancellationTokenSource(_budget);
            if (await FindSubscriptionByReferenceAsync(reference, settle.Token) is { } found)
            {
                return found;
            }
        }
        catch (Exception ex) when (ex is BillingUnavailableException or OperationCanceledException)
        {
            _logger.LogWarning("Re-reading the subscription failed: {Reason}", ex.Message);
        }
        throw new BillingOutcomeUnknownException("The subscription could not be confirmed at the billing provider.", cause);
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListSubscriptionsForCustomerAsync(int customerId, CancellationToken cancellationToken)
    {
        const string operation = "ListCustomerSubscriptions";
        IReadOnlyList<SubscriptionResponse> response;
        try
        {
            response = await ReadAsync(operation, token => _client.Customers.ListCustomerSubscriptions(
                new ListCustomerSubscriptionsRequest { CustomerId = customerId },
                cancellationToken: token), cancellationToken);
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<BillingSubscription>();
        }
        catch (ApiException<RawError> ex)
        {
            throw UnavailableFrom(operation, ex.StatusCode, ex);
        }

        return response
            .Select(item => item?.Subscription)
            .Where(subscription => subscription is not null)
            .Select(subscription => Map(subscription!))
            .ToList();
    }

    private static BillingSubscription Map(Subscription subscription) => new(
        subscription.Id ?? 0,
        subscription.Reference,
        subscription.Product?.Handle,
        subscription.Product?.Name,
        subscription.State?.Value ?? "unknown",
        subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
        subscription.Product?.Interval,
        subscription.Product?.IntervalUnit?.Value,
        // next_assessment_at is when the next charge is attempted; it tracks current_period_ends_at
        // except while a failed renewal is being retried.
        subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        subscription.CurrentPeriodEndsAt,
        subscription.ActivatedAt,
        subscription.CreatedAt);

    /// <summary>
    /// A read: bounded, with the transport and shape failures shared by every read translated here.
    /// Typed <c>ApiException&lt;TError&gt;</c> passes through to the caller's per-operation catch.
    /// </summary>
    private async Task<T> ReadAsync<T>(string operation, Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        try
        {
            return await Bounded(call, cancellationToken);
        }
        catch (ResponseDeserializationException ex)
        {
            throw UnavailableFrom(operation, ex.StatusCode, ex);
        }
        catch (SdkConnectionException ex)
        {
            throw UnavailableFrom(operation, null, ex);
        }
        catch (AuthSchemeException ex)
        {
            throw UnavailableFrom(operation, null, ex);
        }
        catch (TimeoutException ex)
        {
            throw UnavailableFrom(operation, null, ex);
        }
    }

    /// <summary>
    /// The one home of the total call budget. Linked to the caller's token, so an abandoned request
    /// stops its outbound work; our own budget expiring surfaces as <see cref="TimeoutException"/>.
    /// </summary>
    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_budget);
        try
        {
            return await call(cts.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"The billing call did not complete within {_budget.TotalSeconds:0}s.", ex);
        }
    }

    private BillingUnavailableException UnavailableFrom(string operation, HttpStatusCode? status, Exception? cause, string? detail = null)
    {
        // The SDK's message names the call (method + URL, query stripped) but never the body or credentials.
        _logger.LogError(cause, "Maxio {Operation} failed (HTTP {StatusCode}): {Detail}",
            operation, status is null ? "none" : ((int)status).ToString(), detail ?? cause?.Message ?? "no detail");
        return new BillingUnavailableException(Unavailable, cause);
    }

    private Exception RejectedOrUnavailable(string operation, ResponseDeserializationException ex)
    {
        // An unreadable error body: the provider still refused. Keep a 4xx a rejection so callers do not retry it.
        if ((int)ex.StatusCode is >= 400 and < 500
            && ex.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests))
        {
            _logger.LogWarning("Maxio {Operation} refused with an unreadable body (HTTP {StatusCode}).", operation, (int)ex.StatusCode);
            return new BillingRejectedException("The billing provider rejected the request.", ex);
        }
        return UnavailableFrom(operation, ex.StatusCode, ex);
    }

    private static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and < 300;

    private static string DescribeCustomerErrors(CustomerErrorResponse1 body)
    {
        if (body.Errors is { } errors)
        {
            if (errors.TryGetListOfString(out var list) && list.Count > 0)
            {
                return string.Join("; ", list);
            }
            if (errors.TryGetCustomerError(out var customerError) && !string.IsNullOrWhiteSpace(customerError.Customer))
            {
                return customerError.Customer;
            }
        }
        return "validation failed";
    }
}
