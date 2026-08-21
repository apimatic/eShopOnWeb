using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioBillingGateway : IMaxioBillingGateway
{
    private const int PageSize = 20;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PlanCacheDuration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan SiteCapabilityCacheDuration = TimeSpan.FromMinutes(5);
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioBillingGateway> _logger;

    public MaxioBillingGateway(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        IMemoryCache cache,
        ILogger<MaxioBillingGateway> logger)
    {
        _client = client;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var cacheKey = $"maxio-plans:{_options.ProductFamilyHandle}";
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<SubscriptionPlan>? cached) && cached is not null)
        {
            return cached;
        }

        var plans = await ExecuteAsync(async ct =>
        {
            IReadOnlyList<ProductFamilyResponse> families;
            try
            {
                families = await _client.ProductFamilies.ListProductFamilies(
                    dateField: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    ct: ct);
            }
            catch (SdkException<RawError> exception)
            {
                throw FromRawError(exception.Error, "catalog_unavailable", exception);
            }

            var matches = families
                .Select(response => response.ProductFamily)
                .Where(family => family is not null &&
                    family.ArchivedAt is null &&
                    string.Equals(family.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
                .ToList();

            if (matches.Count != 1 || matches[0]!.Id is null)
            {
                throw new BillingProviderException(
                    HttpStatusCode.BadGateway,
                    "catalog_misconfigured",
                    "The subscription catalog is not configured correctly.",
                    outcomeUnknown: false);
            }

            var familyId = matches[0]!.Id!.Value.ToString(CultureInfo.InvariantCulture);
            var allProducts = new List<ProductResponse>();
            for (var page = 1; ; page++)
            {
                IReadOnlyList<ProductResponse> products;
                try
                {
                    products = await _client.ProductFamilies.ListProductsForProductFamily(
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
                        perPage: PageSize,
                        ct: ct);
                }
                catch (SdkException<ListProductsForProductFamilyError> exception)
                {
                    if (exception.Error.TryGetString(out _))
                    {
                        throw new BillingProviderException(
                            HttpStatusCode.BadGateway,
                            "catalog_not_found",
                            "The configured subscription catalog could not be found.",
                            outcomeUnknown: false,
                            exception);
                    }

                    if (exception.Error.TryGetRawError(out var raw))
                    {
                        throw FromRawError(raw, "catalog_unavailable", exception);
                    }

                    throw UnexpectedProviderError("catalog_unavailable", exception);
                }

                allProducts.AddRange(products);
                if (products.Count < PageSize)
                {
                    break;
                }
            }

            return (IReadOnlyList<SubscriptionPlan>)allProducts
                .Select(response => response.Product)
                .Where(product => product.ArchivedAt is null &&
                    !string.IsNullOrWhiteSpace(product.Handle) &&
                    !string.IsNullOrWhiteSpace(product.Name) &&
                    product.PriceInCents.HasValue)
                .Select(product => new SubscriptionPlan(
                    product.Handle!,
                    product.ProductPricePointHandle,
                    product.Name!,
                    product.PriceInCents!.Value,
                    product.Interval,
                    product.IntervalUnit?.Value))
                .OrderBy(plan => plan.PriceInCents)
                .ThenBy(plan => plan.ProductHandle, StringComparer.Ordinal)
                .ToList();
        }, isWrite: false, cancellationToken);

        _cache.Set(cacheKey, plans, PlanCacheDuration);
        return plans;
    }

    public Task<BillingCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken) =>
        ExecuteAsync(async ct =>
        {
            try
            {
                var response = await _client.Customers.ReadCustomerByReference(reference, ct: ct);
                return MapCustomer(response.Customer, reference);
            }
            catch (SdkException<RawError> exception) when (exception.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            catch (SdkException<RawError> exception)
            {
                throw FromRawError(exception.Error, "customer_lookup_failed", exception);
            }
        }, isWrite: false, cancellationToken);

    public Task<BillingCustomer> CreateCustomerAsync(
        CreateBillingCustomer customer,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async ct =>
        {
            var request = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = customer.FirstName,
                    LastName = customer.LastName,
                    Email = customer.Email,
                    Reference = customer.Reference
                }
            };

            try
            {
                using var writeScope = MaxioWriteGuard.Begin();
                var response = await _client.Customers.CreateCustomer(request, ct: ct);
                return MapCustomer(response.Customer, customer.Reference);
            }
            catch (SdkException<CreateCustomerError> exception)
            {
                if (exception.Error.TryGetCustomerErrorResponse1(out _))
                {
                    throw new BillingProviderException(
                        HttpStatusCode.UnprocessableEntity,
                        "customer_rejected",
                        "Maxio could not create the billing customer.",
                        outcomeUnknown: false,
                        exception);
                }

                if (exception.Error.TryGetRawError(out var raw))
                {
                    throw FromRawError(raw, "customer_create_failed", exception);
                }

                throw UnexpectedProviderError("customer_create_failed", exception, outcomeUnknown: true);
            }
        }, isWrite: true, cancellationToken);

    public Task<SubscriptionDetails?> FindSubscriptionAsync(
        string reference,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async ct =>
        {
            try
            {
                var response = await _client.Subscriptions.FindSubscription(reference, ct: ct);
                return response.Subscription is null ? null : MapSubscription(response.Subscription);
            }
            catch (SdkException<FindSubscriptionError> exception)
            {
                if (exception.Error.TryGetNoContent(out _))
                {
                    return null;
                }

                if (exception.Error.TryGetRawError(out var raw))
                {
                    throw FromRawError(raw, "subscription_lookup_failed", exception);
                }

                throw UnexpectedProviderError("subscription_lookup_failed", exception);
            }
        }, isWrite: false, cancellationToken);

    public Task<SubscriptionDetails> CreateSubscriptionAsync(
        CreateBillingSubscription subscription,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async ct =>
        {
            var paymentCollectionMethod = await GetNoStoredPaymentCollectionMethodAsync(ct);
            var request = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = subscription.ProductHandle,
                    ProductPricePointHandle = string.IsNullOrEmpty(subscription.PricePointHandle)
                        ? null
                        : subscription.PricePointHandle,
                    PaymentCollectionMethod = paymentCollectionMethod,
                    CustomerReference = subscription.CustomerReference,
                    Reference = subscription.Reference
                }
            };

            try
            {
                using var writeScope = MaxioWriteGuard.Begin();
                var response = await _client.Subscriptions.CreateSubscription(request, ct: ct);
                if (response.Subscription is null)
                {
                    throw MalformedProviderResponse("subscription_create_malformed");
                }

                return MapSubscription(response.Subscription);
            }
            catch (SdkException<CreateSubscriptionError> exception)
            {
                if (exception.Error.TryGetErrorListResponse1(out var errorResponse))
                {
                    var providerErrors = SanitizeProviderErrors(errorResponse.Errors);
                    _logger.LogWarning(
                        "Maxio rejected a subscription create request with HTTP 422. Provider errors: {ProviderErrors}",
                        providerErrors.Count == 0 ? "<none>" : string.Join(" | ", providerErrors));
                    throw new BillingProviderException(
                        HttpStatusCode.UnprocessableEntity,
                        "subscription_rejected",
                        "Maxio rejected the subscription request.",
                        outcomeUnknown: false,
                        exception);
                }

                if (exception.Error.TryGetRawError(out var raw))
                {
                    throw FromRawError(raw, "subscription_create_failed", exception);
                }

                throw UnexpectedProviderError("subscription_create_failed", exception, outcomeUnknown: true);
            }
        }, isWrite: true, cancellationToken);

    private async Task<CollectionMethod> GetNoStoredPaymentCollectionMethodAsync(
        CancellationToken cancellationToken)
    {
        const string cacheKey = "maxio-site:no-stored-payment-collection-method";
        if (_cache.TryGetValue(cacheKey, out CollectionMethod? cached) && cached is not null)
        {
            return cached;
        }

        SiteResponse response;
        try
        {
            response = await _client.Sites.ReadSite(ct: cancellationToken);
        }
        catch (SdkException<RawError> exception)
        {
            throw FromRawError(exception.Error, "site_capabilities_unavailable", exception);
        }

        var collectionMethod = response.Site.RelationshipInvoicingEnabled switch
        {
            true => CollectionMethod.Remittance,
            false => CollectionMethod.Invoice,
            null => throw new BillingProviderException(
                HttpStatusCode.BadGateway,
                "site_billing_architecture_unknown",
                "Maxio did not identify the site's billing architecture.",
                outcomeUnknown: false)
        };

        _cache.Set(cacheKey, collectionMethod, SiteCapabilityCacheDuration);
        return collectionMethod;
    }

    public Task<SubscriptionDetails> ReadSubscriptionAsync(
        int subscriptionId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async ct =>
        {
            try
            {
                var response = await _client.Subscriptions.ReadSubscription(
                    subscriptionId,
                    include: null,
                    ct: ct);
                if (response.Subscription is null)
                {
                    throw MalformedProviderResponse("subscription_read_malformed");
                }

                return MapSubscription(response.Subscription);
            }
            catch (SdkException<RawError> exception)
            {
                throw FromRawError(exception.Error, "subscription_read_failed", exception);
            }
        }, isWrite: false, cancellationToken);

    public Task<IReadOnlyList<SubscriptionDetails>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async ct =>
        {
            try
            {
                var responses = await _client.Customers.ListCustomerSubscriptions(customerId, ct: ct);
                return (IReadOnlyList<SubscriptionDetails>)responses
                    .Where(response => response.Subscription is not null)
                    .Select(response => MapSubscription(response.Subscription!))
                    .ToList();
            }
            catch (SdkException<RawError> exception)
            {
                throw FromRawError(exception.Error, "subscription_list_failed", exception);
            }
        }, isWrite: false, cancellationToken);

    private async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        bool isWrite,
        CancellationToken cancellationToken)
    {
        using var callContext = MaxioCallContext.Begin();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(CallBudget);

        try
        {
            return await action(budget.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MaxioWriteReplayBlockedException exception)
        {
            _logger.LogWarning("Blocked an automatic retry of a Maxio write.");
            throw new BillingProviderException(
                HttpStatusCode.ServiceUnavailable,
                "provider_write_outcome_unknown",
                "The billing request outcome is being reconciled. Please retry shortly.",
                outcomeUnknown: true,
                exception);
        }
        catch (TaskCanceledException exception)
        {
            throw new BillingProviderException(
                HttpStatusCode.GatewayTimeout,
                "provider_timeout",
                "Maxio did not respond before the billing request timed out.",
                outcomeUnknown: isWrite,
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new BillingProviderException(
                HttpStatusCode.ServiceUnavailable,
                "provider_unavailable",
                "Maxio is temporarily unavailable.",
                outcomeUnknown: isWrite,
                exception);
        }
        catch (JsonException exception)
        {
            var statusCode = MaxioCallContext.LastStatusCode;
            if (statusCode is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError)
            {
                throw new BillingProviderException(
                    statusCode.Value,
                    "provider_rejected_unreadable",
                    "Maxio rejected the request, but its response could not be processed.",
                    outcomeUnknown: false,
                    exception);
            }

            throw new BillingProviderException(
                HttpStatusCode.BadGateway,
                "provider_response_invalid",
                "Maxio returned a response that could not be processed.",
                outcomeUnknown: isWrite,
                exception);
        }
    }

    private static BillingCustomer MapCustomer(Customer customer, string expectedReference)
    {
        if (customer.Id is null ||
            !string.Equals(customer.Reference, expectedReference, StringComparison.Ordinal))
        {
            throw MalformedProviderResponse("customer_response_invalid");
        }

        return new BillingCustomer(
            customer.Id.Value,
            customer.Reference!,
            customer.FirstName,
            customer.LastName,
            customer.Email);
    }

    private static SubscriptionDetails MapSubscription(Subscription subscription)
    {
        if (subscription.Id is null || string.IsNullOrWhiteSpace(subscription.Product?.Handle))
        {
            throw MalformedProviderResponse("subscription_response_invalid");
        }

        return new SubscriptionDetails(
            subscription.Id.Value,
            subscription.Product.Handle!,
            subscription.Product.Name,
            subscription.Product.ProductPricePointHandle,
            subscription.ProductPriceInCents,
            subscription.Currency,
            subscription.State?.Value,
            subscription.NextAssessmentAt,
            subscription.Customer?.Id,
            subscription.Customer?.Reference,
            subscription.Reference);
    }

    private static BillingProviderException FromRawError(
        RawError raw,
        string code,
        Exception exception)
    {
        var providerStatus = raw.StatusCode;
        var publicStatus = providerStatus switch
        {
            HttpStatusCode.BadRequest or HttpStatusCode.Conflict or
                HttpStatusCode.UnprocessableEntity => providerStatus,
            HttpStatusCode.TooManyRequests => HttpStatusCode.ServiceUnavailable,
            _ => HttpStatusCode.BadGateway
        };

        return new BillingProviderException(
            publicStatus,
            code,
            publicStatus == HttpStatusCode.ServiceUnavailable
                ? "Maxio is temporarily unavailable."
                : "The billing operation could not be completed.",
            outcomeUnknown: false,
            exception);
    }

    private static BillingProviderException UnexpectedProviderError(
        string code,
        Exception exception,
        bool outcomeUnknown = false) =>
        new(
            HttpStatusCode.BadGateway,
            code,
            "Maxio returned an unexpected error response.",
            outcomeUnknown,
            exception);

    private static BillingProviderException MalformedProviderResponse(string code) =>
        new(
            HttpStatusCode.BadGateway,
            code,
            "Maxio returned an incomplete response.",
            outcomeUnknown: false);

    private static IReadOnlyList<string> SanitizeProviderErrors(IReadOnlyList<string> errors) =>
        errors
            .Where(error => !string.IsNullOrWhiteSpace(error))
            .Take(5)
            .Select(error =>
            {
                var normalized = string.Join(
                    ' ',
                    error.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                return normalized.Length <= 256 ? normalized : normalized[..256];
            })
            .ToList();
}
