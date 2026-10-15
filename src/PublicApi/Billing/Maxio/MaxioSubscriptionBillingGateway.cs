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
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing.Maxio;

/// <summary>
/// The only class that talks to Maxio Advanced Billing. Every call runs under one total budget,
/// every SDK failure is translated into <see cref="BillingProviderException"/>, and an ambiguous write
/// is settled by re-reading its reference inside the write's own catch.
/// </summary>
public class MaxioSubscriptionBillingGateway : ISubscriptionBillingGateway
{
    /// <summary>Total budget for one gateway call, all attempts and backoff included.</summary>
    internal static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(20);

    /// <summary>Page size for the plan list (the provider's maximum) and the most pages walked.</summary>
    public const int PlanPageSize = 200;
    public const int MaxPlanPages = 5;

    private static readonly TimeSpan PlanCacheDuration = TimeSpan.FromSeconds(60);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioSubscriptionBillingGateway> _logger;

    public MaxioSubscriptionBillingGateway(MaxioAdvancedBillingClient client, IOptions<MaxioSettings> settings,
        IMemoryCache cache, ILogger<MaxioSubscriptionBillingGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<SubscriptionPlanCatalog> GetPlanCatalogAsync(CancellationToken cancellationToken)
    {
        var cacheKey = "maxio:plans:" + _settings.ProductFamilyHandle;
        if (_cache.TryGetValue(cacheKey, out SubscriptionPlanCatalog? cached) && cached is not null)
        {
            return cached;
        }

        var catalog = await ReadAsync("ListProductsForProductFamily", ReadPlanCatalogAsync, cancellationToken);
        _cache.Set(cacheKey, catalog, PlanCacheDuration);
        return catalog;
    }

    private async Task<SubscriptionPlanCatalog> ReadPlanCatalogAsync(CancellationToken budget)
    {
        var plans = new List<SubscriptionPlan>();
        var truncated = false;
        for (var page = 1; page <= MaxPlanPages; page++)
        {
            IReadOnlyList<ProductResponse> items;
            try
            {
                items = await _client.ProductFamilies.ListProductsForProductFamily(
                    new ListProductsForProductFamilyRequest
                    {
                        ProductFamilyId = "handle:" + _settings.ProductFamilyHandle,
                        Page = page,
                        PerPage = PlanPageSize
                    },
                    cancellationToken: budget);
            }
            catch (ApiException<ListProductsForProductFamilyError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogError("Maxio product family configured as Maxio:ProductFamilyHandle was not found (404).");
                throw new BillingProviderException(BillingFailureKind.Misconfigured,
                    "Subscription plans are not available right now.", (int)ex.StatusCode, innerException: ex);
            }

            foreach (var product in items.Select(i => i.Product))
            {
                if (product is null || product.ArchivedAt is not null || string.IsNullOrWhiteSpace(product.Handle))
                {
                    continue;
                }
                plans.Add(new SubscriptionPlan(
                    product.Handle,
                    string.IsNullOrWhiteSpace(product.Name) ? product.Handle : product.Name,
                    product.Description,
                    product.PriceInCents ?? 0,
                    product.Interval,
                    product.IntervalUnit?.Value));
            }

            if (items.Count < PlanPageSize)
            {
                return new SubscriptionPlanCatalog(plans, Truncated: false);
            }
            truncated = page == MaxPlanPages;
        }

        _logger.LogWarning("Maxio plan list hit the {MaxPages}-page cap; the catalog returned is incomplete.", MaxPlanPages);
        return new SubscriptionPlanCatalog(plans, truncated);
    }

    public Task<int?> FindCustomerIdAsync(string customerReference, CancellationToken cancellationToken) =>
        ReadAsync("ReadCustomerByReference", budget => FindCustomerIdCoreAsync(customerReference, budget), cancellationToken);

    private async Task<int?> FindCustomerIdCoreAsync(string customerReference, CancellationToken budget)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = customerReference },
                cancellationToken: budget);
            // A found customer without an id is unreadable, never "absent": absence would trigger a duplicate create.
            return response.Customer?.Id ?? throw new BillingProviderException(BillingFailureKind.Unavailable,
                "The billing provider returned a response that could not be processed.");
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<int> CreateCustomerAsync(string customerReference, BillingSubscriber subscriber, CancellationToken cancellationToken)
    {
        var request = new CreateCustomerOperationRequest
        {
            Body = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = FirstNameOf(subscriber),
                    LastName = "eShopOnWeb",
                    Email = subscriber.Email,
                    Reference = customerReference
                }
            }
        };

        try
        {
            var response = await Bounded(budget => _client.Customers.CreateCustomer(request, cancellationToken: budget), cancellationToken);
            if (response.Customer?.Id is int id)
            {
                _logger.LogInformation("Maxio customer {CustomerId} created for reference {CustomerReference}.", id, customerReference);
                return id;
            }
            // A 2xx we cannot read an id from: the customer may exist. Settle below.
            throw new BillingOutcomeUnknownException(customerReference);
        }
        catch (ApiException<CreateCustomerError> ex) when ((int)ex.StatusCode < 500)
        {
            var errors = ReadErrors(ex.Error);
            if (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                // The reference may already be taken by a concurrent create: that customer is ours.
                var existing = await SettleAsync("ReadCustomerByReference", budget => FindCustomerIdCoreAsync(customerReference, budget));
                if (existing is int existingId)
                {
                    return existingId;
                }
            }
            throw Rejected("CreateCustomer", ex, errors);
        }
        catch (Exception ex) when (IsOutcomeUnknown(ex))
        {
            _logger.LogWarning(ex, "Maxio CreateCustomer outcome unknown for reference {CustomerReference}; re-reading by reference.", customerReference);
            var settled = await SettleAsync("ReadCustomerByReference", budget => FindCustomerIdCoreAsync(customerReference, budget));
            return settled ?? throw new BillingOutcomeUnknownException(customerReference, ex);
        }
        catch (SdkException ex)
        {
            throw Translate("CreateCustomer", ex);
        }
    }

    public Task<IReadOnlyList<BillingSubscription>> ListSubscriptionsAsync(int customerId, CancellationToken cancellationToken) =>
        ReadAsync<IReadOnlyList<BillingSubscription>>("ListCustomerSubscriptions", async budget =>
        {
            var items = await _client.Customers.ListCustomerSubscriptions(
                new ListCustomerSubscriptionsRequest { CustomerId = customerId },
                cancellationToken: budget);
            return items
                .Select(i => i.Subscription)
                .Where(s => s?.Id is not null)
                .Select(s => ToBillingSubscription(s!))
                .ToList();
        }, cancellationToken);

    public async Task<BillingSubscription> CreateSubscriptionAsync(int customerId, string planHandle, string subscriptionReference, CancellationToken cancellationToken)
    {
        var request = new CreateSubscriptionOperationRequest
        {
            Body = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = planHandle,
                    CustomerId = customerId,
                    Reference = subscriptionReference,
                    // eShopOnWeb captures no payment method, so the provider's default (automatic)
                    // collection is refused with "No payment method was on file"; remittance invoices
                    // the subscriber instead.
                    PaymentCollectionMethod = CollectionMethod.Remittance
                }
            }
        };

        try
        {
            var response = await Bounded(budget => _client.Subscriptions.CreateSubscription(request, cancellationToken: budget), cancellationToken);
            if (response.Subscription is { Id: not null } subscription)
            {
                return ToBillingSubscription(subscription);
            }
            // A 2xx without a readable subscription: it may exist. Settle below.
            throw new BillingOutcomeUnknownException(subscriptionReference);
        }
        catch (ApiException<CreateSubscriptionError> ex) when ((int)ex.StatusCode < 500)
        {
            var errors = ex.Error.TryGetErrorListResponse1(out var errorList) && errorList.Errors is not null
                ? errorList.Errors
                : Array.Empty<string>();
            throw Rejected("CreateSubscription", ex, errors);
        }
        catch (Exception ex) when (IsOutcomeUnknown(ex))
        {
            _logger.LogWarning(ex, "Maxio CreateSubscription outcome unknown for reference {SubscriptionReference}; re-reading by reference.", subscriptionReference);
            var settled = await SettleAsync("FindSubscription", budget => FindSubscriptionCoreAsync(subscriptionReference, budget));
            return settled ?? throw new BillingOutcomeUnknownException(subscriptionReference, ex);
        }
        catch (SdkException ex)
        {
            throw Translate("CreateSubscription", ex);
        }
    }

    public Task<BillingSubscription?> FindSubscriptionAsync(string subscriptionReference, CancellationToken cancellationToken) =>
        ReadAsync("FindSubscription", budget => FindSubscriptionCoreAsync(subscriptionReference, budget), cancellationToken);

    private async Task<BillingSubscription?> FindSubscriptionCoreAsync(string subscriptionReference, CancellationToken budget)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = subscriptionReference },
                cancellationToken: budget);
            // Only a 404 means "no such subscription"; an unreadable 200 must not lead to a second create.
            return response.Subscription is { Id: not null } subscription
                ? ToBillingSubscription(subscription)
                : throw new BillingProviderException(BillingFailureKind.Unavailable,
                    "The billing provider returned a response that could not be processed.");
        }
        catch (ApiException<FindSubscriptionError> ex) when (ex.StatusCode == HttpStatusCode.NotFound || ex.Error.TryGetNoContent(out _))
        {
            return null;
        }
    }

    /// <summary>
    /// Re-reads provider state after an ambiguous write. Runs on its own budget, not linked to the
    /// caller's token, so a disconnected caller does not abandon settlement.
    /// Returns default when the read itself fails: the outcome then stays unknown.
    /// </summary>
    private async Task<T?> SettleAsync<T>(string operation, Func<CancellationToken, Task<T?>> read)
    {
        try
        {
            return await Bounded(read, CancellationToken.None);
        }
        catch (Exception ex) when (ex is SdkException or OperationCanceledException or BillingProviderException)
        {
            _logger.LogWarning(ex, "Maxio {Operation} could not settle an unknown outcome.", operation);
            return default;
        }
    }

    /// <summary>Runs a read under the call budget and translates every SDK failure.</summary>
    private async Task<T> ReadAsync<T>(string operation, Func<CancellationToken, Task<T>> read, CancellationToken cancellationToken)
    {
        try
        {
            return await Bounded(read, cancellationToken);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError("Maxio {Operation} exceeded its {Budget}s budget.", operation, CallBudget.TotalSeconds);
            throw new BillingProviderException(BillingFailureKind.Unavailable,
                "The billing provider did not respond in time. Please retry.", innerException: ex);
        }
        catch (SdkException ex)
        {
            throw Translate(operation, ex);
        }
    }

    /// <summary>The one home of the total call budget: every operation goes through here.</summary>
    private static async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    /// <summary>
    /// After a write, these leave the outcome unknown: no usable response (connection, timeout,
    /// cancellation), an unreadable success body, or a provider 5xx.
    /// </summary>
    private static bool IsOutcomeUnknown(Exception ex) => ex switch
    {
        BillingOutcomeUnknownException => true,
        OperationCanceledException => true,
        SdkConnectionException => true, // includes SdkTimeoutException
        ApiException api => (int)api.StatusCode >= 500 || ((int)api.StatusCode is >= 200 and < 300),
        _ => false
    };

    private BillingProviderException Rejected(string operation, ApiException ex, IReadOnlyList<string> errors)
    {
        var status = (int)ex.StatusCode;
        if (status is 401 or 403 or 404)
        {
            return Translate(operation, ex);
        }
        _logger.LogWarning("Maxio {Operation} rejected the request with {Status}: {Errors}", operation, status, string.Join("; ", errors));
        var message = errors.Count > 0 ? string.Join(" ", errors) : "The billing provider rejected the request.";
        return new BillingProviderException(BillingFailureKind.Rejected, message, status, errors, ex);
    }

    /// <summary>One mapping from SDK failure to the app's failure kinds, shared by every operation.</summary>
    private BillingProviderException Translate(string operation, SdkException ex)
    {
        switch (ex)
        {
            case AuthSchemeException:
                _logger.LogError(ex, "Maxio {Operation}: credentials could not be applied.", operation);
                return new BillingProviderException(BillingFailureKind.Misconfigured, "Billing is not available right now.", innerException: ex);

            case SdkConnectionException:
                _logger.LogError(ex, "Maxio {Operation}: no response from the provider.", operation);
                return new BillingProviderException(BillingFailureKind.Unavailable, "The billing provider is unreachable. Please retry.", innerException: ex);

            case ResponseDeserializationException rde:
                _logger.LogError(ex, "Maxio {Operation}: unreadable {Status} response.", operation, (int)rde.StatusCode);
                return new BillingProviderException(BillingFailureKind.Unavailable, "The billing provider returned a response that could not be processed.", (int)rde.StatusCode, innerException: ex);

            case ApiException api:
                var status = (int)api.StatusCode;
                if (status is 401 or 403 or 404)
                {
                    // Our key, site or catalog is wrong — not the caller's fault.
                    _logger.LogError("Maxio {Operation} returned {Status}: check Maxio:ApiKey / Maxio:Subdomain / Maxio:BaseUrl.", operation, status);
                    return new BillingProviderException(BillingFailureKind.Misconfigured, "Billing is not available right now.", status, innerException: ex);
                }
                if (status is 400 or 409 or 422)
                {
                    _logger.LogWarning("Maxio {Operation} rejected the request with {Status}.", operation, status);
                    return new BillingProviderException(BillingFailureKind.Rejected, "The billing provider rejected the request.", status, innerException: ex);
                }
                _logger.LogError("Maxio {Operation} failed with {Status}.", operation, status);
                return new BillingProviderException(BillingFailureKind.Unavailable, "The billing provider is unavailable. Please retry.", status, innerException: ex);

            default:
                _logger.LogError(ex, "Maxio {Operation} failed.", operation);
                return new BillingProviderException(BillingFailureKind.Unavailable, "The billing provider is unavailable. Please retry.", innerException: ex);
        }
    }

    private static IReadOnlyList<string> ReadErrors(CreateCustomerError error)
    {
        if (error.TryGetCustomerErrorResponse1(out var body) && body.Errors is not null)
        {
            if (body.Errors.TryGetListOfString(out var list))
            {
                return list;
            }
            if (body.Errors.TryGetCustomerError(out var customerError) && !string.IsNullOrWhiteSpace(customerError.Customer))
            {
                return new[] { customerError.Customer };
            }
        }
        return Array.Empty<string>();
    }

    private static BillingSubscription ToBillingSubscription(Subscription subscription)
    {
        var state = subscription.State;
        var isLive = state is not null
            && state != SubscriptionState.Canceled
            && state != SubscriptionState.Expired
            && state != SubscriptionState.FailedToCreate;

        return new BillingSubscription(
            subscription.Id!.Value,
            subscription.Product?.Handle,
            subscription.Product?.Name,
            subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
            subscription.Currency,
            state?.Value ?? "unknown",
            isLive,
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            subscription.CurrentPeriodEndsAt,
            subscription.CreatedAt,
            subscription.Reference);
    }

    private static string FirstNameOf(BillingSubscriber subscriber)
    {
        var at = subscriber.Email.IndexOf('@');
        var localPart = at > 0 ? subscriber.Email[..at] : subscriber.UserName;
        return string.IsNullOrWhiteSpace(localPart) ? "Customer" : localPart;
    }
}
