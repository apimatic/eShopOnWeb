using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public sealed class MaxioBillingGateway : IMaxioBillingGateway
{
    private const int PageSize = 100;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan CatalogCacheDuration = TimeSpan.FromMinutes(5);
    private readonly MaxioAdvancedBilling.MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly MaxioWriteOnceCoordinator _writeOnce;
    private readonly IMemoryCache _cache;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<MaxioBillingGateway> _logger;
    private readonly SemaphoreSlim _catalogLock = new(1, 1);

    public MaxioBillingGateway(
        MaxioAdvancedBilling.MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        MaxioWriteOnceCoordinator writeOnce,
        IMemoryCache cache,
        IHostEnvironment environment,
        ILogger<MaxioBillingGateway> logger)
    {
        _client = client;
        _options = options.Value;
        _writeOnce = writeOnce;
        _cache = cache;
        _environment = environment;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        const string cacheKey = "maxio:subscription-plans:v1";
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<BillingPlan>? cached) && cached is not null)
        {
            return cached;
        }

        await _catalogLock.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(cacheKey, out cached) && cached is not null)
            {
                return cached;
            }

            var site = await ReadSiteAsync(cancellationToken);
            var families = await BoundedAsync(ct => _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: ct), cancellationToken);
            var matching = families
                .Select(x => x.ProductFamily)
                .Where(x => x is not null && x.ArchivedAt is null && string.Equals(x.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
                .ToList();
            if (matching.Count != 1 || matching[0]!.Id is null)
            {
                throw ConfigurationFailure("The configured Maxio product family could not be resolved uniquely.");
            }

            var products = new List<Product>();
            for (var page = 1; ; page++)
            {
                IReadOnlyList<ProductResponse> response;
                try
                {
                    response = await BoundedAsync(ct => _client.ProductFamilies.ListProductsForProductFamily(
                        productFamilyId: matching[0]!.Id!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
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
                        ct: ct), cancellationToken);
                }
                catch (SdkException<ListProductsForProductFamilyError> ex)
                {
                    throw TranslateListProductsError(ex);
                }

                products.AddRange(response.Select(x => x.Product));
                if (response.Count < PageSize)
                {
                    break;
                }
            }

            var currency = Require(site.Currency, "site currency");
            var result = products
                .Where(IsAvailableWithoutPaymentMethod)
                .Select(p => new BillingPlan(
                    Require(p.Handle, "product handle"),
                    Require(p.Name, "product name"),
                    p.Description,
                    Require(p.PriceInCents, "product price"),
                    currency,
                    Require(p.Interval, "product interval"),
                    Require(p.IntervalUnit, "product interval unit").Value))
                .OrderBy(x => x.PriceInCents)
                .ToArray();
            _cache.Set(cacheKey, result, CatalogCacheDuration);
            return result;
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex.Error, "list subscription plans");
        }
        catch (JsonException ex)
        {
            throw MalformedResponse(ex);
        }
        finally
        {
            _catalogLock.Release();
        }
    }

    public async Task<BillingProduct?> FindProductAsync(string productHandle, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(ct => _client.Products.ReadProductByHandle(productHandle, ct: ct), cancellationToken);
            var product = response.Product;
            if (product.ArchivedAt is not null ||
                !string.Equals(product.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
            {
                return null;
            }
            return new BillingProduct(
                Require(product.Handle, "product handle"),
                Require(product.Name, "product name"),
                Require(product.PriceInCents, "product price"),
                Require(product.Interval, "product interval"),
                Require(product.IntervalUnit, "product interval unit").Value,
                RequiresPaymentMethod(product));
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex.Error, "read product");
        }
        catch (JsonException ex)
        {
            throw MalformedResponse(ex);
        }
        catch (Exception ex) when (IsTransport(ex, cancellationToken))
        {
            throw TransportFailure(ex);
        }
    }

    public async Task<BillingSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(ct => _client.Subscriptions.FindSubscription(reference, ct: ct), cancellationToken);
            return response.Subscription is null ? null : MapSubscription(response.Subscription);
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRawError(raw, "find subscription");
            }
            throw ProviderFailure("find_subscription_failed", "Maxio could not complete the subscription lookup.", HttpStatusCode.BadGateway, ex);
        }
        catch (JsonException ex)
        {
            throw MalformedResponse(ex);
        }
        catch (Exception ex) when (IsTransport(ex, cancellationToken))
        {
            throw TransportFailure(ex);
        }
    }

    public async Task<BillingCustomer> EnsureCustomerAsync(BillingCustomerProfile profile, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerAsync(profile.Reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            using var scope = _writeOnce.Begin();
            var response = await BoundedAsync(ct => _client.Customers.CreateCustomer(new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = profile.FirstName,
                    LastName = profile.LastName,
                    Email = profile.Email,
                    Reference = profile.Reference
                }
            }, ct: ct), cancellationToken);
            return MapCustomer(response.Customer);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _) || ex.Error.TryGetRawError(out _))
            {
                existing = await FindCustomerAsync(profile.Reference, cancellationToken);
                if (existing is not null)
                {
                    return existing;
                }
            }
            throw ProviderFailure("customer_rejected", "Maxio rejected the customer profile.", HttpStatusCode.UnprocessableEntity, ex);
        }
        catch (Exception ex) when (ex is MaxioWriteAlreadyAttemptedException || IsTransport(ex, cancellationToken) || ex is JsonException)
        {
            existing = await FindCustomerAsync(profile.Reference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }
            throw ProviderFailure("customer_outcome_unknown", "The customer request outcome could not be confirmed.", HttpStatusCode.ServiceUnavailable, ex);
        }
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var existing = await FindSubscriptionAsync(subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var site = await ReadSiteAsync(cancellationToken);
        var paymentCollectionMethod = site.RelationshipInvoicingEnabled == true
            ? CollectionMethod.Remittance
            : CollectionMethod.Invoice;

        try
        {
            using var scope = _writeOnce.Begin();
            var response = await BoundedAsync(ct => _client.Subscriptions.CreateSubscription(new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = productHandle,
                    CustomerReference = customerReference,
                    Reference = subscriptionReference,
                    PaymentCollectionMethod = paymentCollectionMethod
                }
            }, ct: ct), cancellationToken);
            return MapSubscription(response.Subscription ?? throw new JsonException("Missing subscription envelope."));
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw ProviderFailure("subscription_rejected", SafeValidationMessage(errors.Errors), HttpStatusCode.UnprocessableEntity, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRawError(raw, "create subscription");
            }
            throw ProviderFailure("subscription_rejected", "Maxio rejected the subscription request.", HttpStatusCode.UnprocessableEntity, ex);
        }
        catch (Exception ex) when (ex is MaxioWriteAlreadyAttemptedException || IsTransport(ex, cancellationToken) || ex is JsonException)
        {
            existing = await FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }
            throw ProviderFailure("subscription_outcome_unknown", "The subscription request outcome could not be confirmed.", HttpStatusCode.ServiceUnavailable, ex);
        }
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(
        string customerReference,
        string ownedReferencePrefix,
        CancellationToken cancellationToken)
    {
        var customer = await FindCustomerAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<BillingSubscription>();
        }
        try
        {
            var response = await BoundedAsync(ct => _client.Customers.ListCustomerSubscriptions(customer.Id, ct: ct), cancellationToken);
            return response
                .Where(x => x.Subscription?.Reference?.StartsWith(ownedReferencePrefix, StringComparison.Ordinal) == true)
                .Select(x => MapSubscription(x.Subscription!))
                .ToArray();
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex.Error, "list customer subscriptions");
        }
        catch (JsonException ex)
        {
            throw MalformedResponse(ex);
        }
        catch (Exception ex) when (IsTransport(ex, cancellationToken))
        {
            throw TransportFailure(ex);
        }
    }

    public async Task CheckHealthAsync(CancellationToken cancellationToken) => _ = await ReadSiteAsync(cancellationToken);

    private async Task<Site> ReadSiteAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(ct => _client.Sites.ReadSite(ct: ct), cancellationToken);
            if (!_environment.IsProduction() && response.Site.Test != true)
            {
                throw ConfigurationFailure("Development is configured against a non-test Maxio site.");
            }
            return response.Site;
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex.Error, "read site");
        }
        catch (JsonException ex)
        {
            throw MalformedResponse(ex);
        }
        catch (Exception ex) when (IsTransport(ex, cancellationToken))
        {
            throw TransportFailure(ex);
        }
    }

    private async Task<BillingCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(ct => _client.Customers.ReadCustomerByReference(reference, ct: ct), cancellationToken);
            return MapCustomer(response.Customer);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex.Error, "read customer");
        }
        catch (JsonException ex)
        {
            throw MalformedResponse(ex);
        }
        catch (Exception ex) when (IsTransport(ex, cancellationToken))
        {
            throw TransportFailure(ex);
        }
    }

    private async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return await operation(cts.Token);
    }

    private static bool IsAvailableWithoutPaymentMethod(Product? product) =>
        product is not null && product.ArchivedAt is null && !RequiresPaymentMethod(product) &&
        !string.IsNullOrWhiteSpace(product.Handle) && !string.IsNullOrWhiteSpace(product.Name) &&
        product.PriceInCents is not null && product.Interval is not null && product.IntervalUnit is not null;

    // RequestCreditCard is deprecated and only applies to legacy hosted signup pages.
    // RequireCreditCard controls whether API signup requires a payment profile.
    private static bool RequiresPaymentMethod(Product product) => product.RequireCreditCard == true;

    private static BillingCustomer MapCustomer(Customer customer) =>
        new(Require(customer.Id, "customer ID"), Require(customer.Reference, "customer reference"));

    private static BillingSubscription MapSubscription(Subscription subscription)
    {
        var product = subscription.Product ?? throw new JsonException("Missing product in subscription response.");
        return new BillingSubscription(
            Require(subscription.Id, "subscription ID"),
            Require(subscription.Reference, "subscription reference"),
            Require(product.Handle, "subscription product handle"),
            Require(product.Name, "subscription product name"),
            Require(subscription.ProductPriceInCents, "subscription product price"),
            Require(subscription.Currency, "subscription currency"),
            Require(subscription.State, "subscription state").Value,
            subscription.NextAssessmentAt,
            subscription.CurrentPeriodEndsAt);
    }

    private static string SafeValidationMessage(IReadOnlyList<string> errors) =>
        errors.Count == 0 ? "Maxio rejected the subscription request." : string.Join(" ", errors.Take(3).Select(Sanitize));

    private static string Sanitize(string message) => message.Length <= 300 ? message : message[..300];

    private static T Require<T>(T? value, string field) where T : class =>
        value ?? throw new JsonException($"Missing {field}.");

    private static T Require<T>(T? value, string field) where T : struct =>
        value ?? throw new JsonException($"Missing {field}.");

    private static BillingException TranslateListProductsError(SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out _))
        {
            return ProviderFailure("catalog_not_found", "The configured Maxio catalog was not found.", HttpStatusCode.BadGateway, ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return TranslateRawError(raw, "list products");
        }
        return ProviderFailure("catalog_failed", "Maxio could not return the subscription catalog.", HttpStatusCode.BadGateway, ex);
    }

    private static BillingException TranslateRawError(RawError raw, string operation)
    {
        var status = raw.StatusCode;
        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return ProviderFailure("maxio_authentication_failed", "Maxio authentication failed.", HttpStatusCode.ServiceUnavailable);
        }
        if (status == HttpStatusCode.TooManyRequests)
        {
            return ProviderFailure("maxio_throttled", "Maxio is temporarily rate limiting requests.", HttpStatusCode.ServiceUnavailable);
        }
        var publicStatus = (int)status >= 400 && (int)status < 500 ? status : HttpStatusCode.BadGateway;
        return ProviderFailure("maxio_request_failed", $"Maxio could not {operation}.", publicStatus);
    }

    private static BillingException TransportFailure(Exception exception) =>
        ProviderFailure("maxio_unavailable", "Maxio is temporarily unavailable.", HttpStatusCode.ServiceUnavailable, exception);

    private static BillingException MalformedResponse(Exception exception) =>
        ProviderFailure("maxio_malformed_response", "Maxio returned a response that could not be processed.", HttpStatusCode.BadGateway, exception);

    private static BillingException ConfigurationFailure(string message) =>
        ProviderFailure("maxio_configuration_invalid", message, HttpStatusCode.ServiceUnavailable);

    private static BillingException ProviderFailure(string code, string message, HttpStatusCode status, Exception? exception = null) =>
        new(code, message, status, exception);

    private static bool IsTransport(Exception exception, CancellationToken callerToken) =>
        exception is HttpRequestException || (exception is TaskCanceledException && !callerToken.IsCancellationRequested);
}
