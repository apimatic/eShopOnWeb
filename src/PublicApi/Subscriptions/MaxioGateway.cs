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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioGateway(
    MaxioAdvancedBillingClient client,
    IOptions<MaxioOptions> settings,
    IMemoryCache cache,
    MaxioWriteOnceScope writeOnceScope) : IMaxioGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PlanCacheDuration = TimeSpan.FromMinutes(2);
    private const int ProductsPerPage = 100;
    private readonly MaxioOptions _settings = settings.Value;

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var cacheKey = $"maxio-plans:{_settings.ProductFamilyHandle}";
        if (cache.TryGetValue(cacheKey, out IReadOnlyList<SubscriptionPlanDto>? cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            var families = await BoundedAsync(
                ct => client.ProductFamilies.ListProductFamilies(
                    dateField: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    ct: ct),
                cancellationToken);

            var family = families
                .Select(response => response.ProductFamily)
                .FirstOrDefault(candidate =>
                    candidate?.ArchivedAt is null &&
                    string.Equals(candidate?.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

            if (family?.Id is null)
            {
                throw new MaxioIntegrationException(
                    503,
                    "catalog_not_configured",
                    "The configured subscription catalog is unavailable.");
            }

            var plans = new List<SubscriptionPlanDto>();
            for (var page = 1; ; page++)
            {
                var products = await BoundedAsync(
                    ct => client.ProductFamilies.ListProductsForProductFamily(
                        productFamilyId: family.Id.Value.ToString(CultureInfo.InvariantCulture),
                        dateField: null,
                        filter: null,
                        startDate: null,
                        endDate: null,
                        startDatetime: null,
                        endDatetime: null,
                        includeArchived: false,
                        include: null,
                        page: page,
                        perPage: ProductsPerPage,
                        ct: ct),
                    cancellationToken);

                plans.AddRange(products
                    .Select(response => response.Product)
                    .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
                    .Select(ToPlan));

                if (products.Count < ProductsPerPage)
                {
                    break;
                }
            }

            var result = plans.OrderBy(plan => plan.PriceInCents).ThenBy(plan => plan.Name).ToArray();
            cache.Set(cacheKey, result, PlanCacheDuration);
            return result;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error, "catalog_unavailable", "The subscription catalog could not be loaded.", ex);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out _))
            {
                throw new MaxioIntegrationException(404, "catalog_not_found", "The subscription catalog was not found.", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, "catalog_unavailable", "The subscription catalog could not be loaded.", ex);
            }

            throw ProviderResponseError(ex);
        }
        catch (Exception ex) when (IsProviderBoundaryFailure(ex))
        {
            throw ProviderUnavailable(ex);
        }
    }

    public async Task<SubscriptionPlanDto> GetPlanAsync(string productHandle, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                ct => client.Products.ReadProductByHandle(productHandle, ct: ct),
                cancellationToken);
            var product = response.Product;

            if (product.ArchivedAt is not null ||
                !string.Equals(product.ProductFamily?.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
            {
                throw new MaxioIntegrationException(404, "plan_not_found", "The requested subscription plan was not found.");
            }

            return ToPlan(product);
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                throw new MaxioIntegrationException(404, "plan_not_found", "The requested subscription plan was not found.", ex);
            }

            throw FromRaw(ex.Error, "plan_lookup_failed", "The subscription plan could not be loaded.", ex);
        }
        catch (Exception ex) when (IsProviderBoundaryFailure(ex))
        {
            throw ProviderUnavailable(ex);
        }
    }

    public async Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                ct => client.Customers.ReadCustomerByReference(reference, ct: ct),
                cancellationToken);
            var customer = response.Customer;
            if (customer.Id is null || !string.Equals(customer.Reference, reference, StringComparison.Ordinal))
            {
                throw new MaxioIntegrationException(502, "invalid_customer_response", "Maxio returned an invalid customer response.");
            }

            return new MaxioCustomer(customer.Id.Value, customer.Reference!);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error, "customer_lookup_failed", "The billing customer could not be loaded.", ex);
        }
        catch (Exception ex) when (IsProviderBoundaryFailure(ex))
        {
            throw ProviderUnavailable(ex);
        }
    }

    public async Task<MaxioCustomer> EnsureCustomerAsync(BillingUser user, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerAsync(user.Id, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            using var writeScope = writeOnceScope.Begin();
            var response = await BoundedAsync(
                ct => client.Customers.CreateCustomer(
                    new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = user.FirstName,
                            LastName = user.LastName,
                            Email = user.Email,
                            Reference = user.Id
                        }
                    },
                    ct: ct),
                cancellationToken);

            var customer = response.Customer;
            if (customer.Id is null || !string.Equals(customer.Reference, user.Id, StringComparison.Ordinal))
            {
                throw new JsonException("Maxio customer response was missing required identity fields.");
            }

            return new MaxioCustomer(customer.Id.Value, customer.Reference!);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                var racedCustomer = await FindCustomerAsync(user.Id, cancellationToken);
                if (racedCustomer is not null)
                {
                    return racedCustomer;
                }

                throw new MaxioIntegrationException(422, "customer_rejected", "Maxio rejected the billing customer profile.", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, "customer_create_failed", "The billing customer could not be created.", ex);
            }

            throw ProviderResponseError(ex);
        }
        catch (Exception ex) when (IsAmbiguousWriteFailure(ex))
        {
            var reconciled = await FindCustomerAsync(user.Id, cancellationToken);
            if (reconciled is not null)
            {
                return reconciled;
            }

            throw new MaxioAmbiguousWriteException(ex);
        }
    }

    public async Task<SubscriptionDto?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                ct => client.Subscriptions.FindSubscription(reference, ct: ct),
                cancellationToken);
            return response.Subscription is null ? null : ToSubscription(response.Subscription);
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, "subscription_lookup_failed", "The subscription could not be loaded.", ex);
            }

            throw ProviderResponseError(ex);
        }
        catch (Exception ex) when (IsProviderBoundaryFailure(ex))
        {
            throw ProviderUnavailable(ex);
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var responses = await BoundedAsync(
                ct => client.Customers.ListCustomerSubscriptions(customerId, ct: ct),
                cancellationToken);
            return responses
                .Select(response => response.Subscription)
                .Where(subscription =>
                    subscription is not null &&
                    string.Equals(subscription.Product?.ProductFamily?.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
                .Select(subscription => ToSubscription(subscription!))
                .ToArray();
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error, "subscriptions_lookup_failed", "Subscriptions could not be loaded.", ex);
        }
        catch (Exception ex) when (IsProviderBoundaryFailure(ex))
        {
            throw ProviderUnavailable(ex);
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(
        string customerReference,
        string productHandle,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        try
        {
            using var writeScope = writeOnceScope.Begin();
            var response = await BoundedAsync(
                ct => client.Subscriptions.CreateSubscription(
                    new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = productHandle,
                            PaymentCollectionMethod = CollectionMethod.Remittance,
                            CustomerReference = customerReference,
                            Reference = subscriptionReference
                        }
                    },
                    ct: ct),
                cancellationToken);

            if (response.Subscription is null)
            {
                throw new JsonException("Maxio subscription response was missing the subscription.");
            }

            return ToSubscription(response.Subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out _))
            {
                throw new MaxioIntegrationException(422, "subscription_rejected", "Maxio rejected the subscription.", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, "subscription_create_failed", "The subscription could not be created.", ex);
            }

            throw ProviderResponseError(ex);
        }
        catch (Exception ex) when (IsAmbiguousWriteFailure(ex))
        {
            throw new MaxioAmbiguousWriteException(ex);
        }
    }

    private static SubscriptionPlanDto ToPlan(Product product) => new(
        product.Handle ?? string.Empty,
        product.Name ?? product.Handle ?? "Subscription plan",
        product.Description,
        product.PriceInCents,
        product.Interval,
        product.IntervalUnit?.Value);

    private static SubscriptionDto ToSubscription(Subscription subscription) => new(
        subscription.Id,
        subscription.Product?.Handle ?? string.Empty,
        subscription.Product?.Name ?? subscription.Product?.Handle ?? "Subscription plan",
        subscription.ProductPriceInCents,
        subscription.State?.Value,
        subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);

    private static async Task<T> BoundedAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(CallBudget);
        return await operation(budget.Token);
    }

    private static bool IsProviderBoundaryFailure(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or JsonException or MaxioWriteRetryBlockedException;

    private static bool IsAmbiguousWriteFailure(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or JsonException or MaxioWriteRetryBlockedException;

    private static MaxioIntegrationException ProviderUnavailable(Exception exception) =>
        new(503, "provider_unavailable", "The billing provider is temporarily unavailable.", exception);

    private static MaxioIntegrationException ProviderResponseError(Exception exception) =>
        new(502, "provider_response_invalid", "The billing provider returned a response that could not be processed.", exception);

    private static MaxioIntegrationException FromRaw(
        RawError raw,
        string code,
        string safeMessage,
        Exception exception)
    {
        var statusCode = (int)raw.StatusCode;
        if (statusCode < 400 || statusCode > 599)
        {
            statusCode = 502;
        }

        return new MaxioIntegrationException(statusCode, code, safeMessage, exception);
    }
}
