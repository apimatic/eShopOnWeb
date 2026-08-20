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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MaxioBillingGateway : IMaxioBillingGateway
{
    private const int ProductPageSize = 100;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PlanCacheLifetime = TimeSpan.FromMinutes(5);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly IMemoryCache _cache;
    private readonly MaxioWriteGuard _writeGuard;
    private readonly ILogger<MaxioBillingGateway> _logger;

    public MaxioBillingGateway(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        IMemoryCache cache,
        MaxioWriteGuard writeGuard,
        ILogger<MaxioBillingGateway> logger)
    {
        _client = client;
        _options = options.Value;
        _cache = cache;
        _writeGuard = writeGuard;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var cacheKey = $"maxio-plans:{_options.ProductFamilyHandle}";
        var cached = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = PlanCacheLifetime;
            return await LoadPlansAsync(cancellationToken);
        });

        return cached ?? Array.Empty<SubscriptionPlanDto>();
    }

    public async Task<SubscriptionPlanDto> GetPlanAsync(string productHandle, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                ct => _client.Products.ReadProductByHandle(productHandle, ct: ct), cancellationToken);
            return MapAndValidatePlan(response.Product);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            throw BillingException.NotFound($"No subscribable plan has handle '{productHandle}'.");
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError(ex.Error, ex);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw BillingException.ProviderUnavailable(ex);
        }
    }

    public async Task<BillingCustomer> EnsureCustomerAsync(
        BillingCustomerProfile profile,
        CancellationToken cancellationToken)
    {
        var reference = $"eshop-user:{profile.StableUserId}";
        var existing = await ReadCustomerAsync(reference, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = profile.FirstName,
                LastName = profile.LastName,
                Email = profile.Email,
                Reference = reference
            }
        };

        try
        {
            var response = await BoundedAsync(
                ct => _client.Customers.CreateCustomer(request, ct: ct), cancellationToken);
            return MapCustomer(response.Customer, reference);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                var racedCustomer = await ReadCustomerAsync(reference, cancellationToken);
                if (racedCustomer != null)
                {
                    return racedCustomer;
                }

                throw new BillingException(HttpStatusCode.UnprocessableEntity, "Customer could not be created",
                    "The billing service rejected the customer profile.", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError(raw, ex);
            }

            throw BillingException.ProviderUnavailable(ex);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw BillingException.ProviderUnavailable(ex);
        }
    }

    public async Task<SubscriptionDto?> FindSubscriptionAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                ct => _client.Subscriptions.FindSubscription(reference: reference, ct: ct), cancellationToken);
            return response.Subscription == null ? null : MapSubscription(response.Subscription);
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError(raw, ex);
            }

            throw BillingException.ProviderUnavailable(ex);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw BillingException.ProviderUnavailable(ex);
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var request = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerReference = customerReference,
                Reference = subscriptionReference,
                PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Invoice
            }
        };

        using var writeScope = _writeGuard.Begin();
        try
        {
            var response = await BoundedAsync(
                ct => _client.Subscriptions.CreateSubscription(request, ct: ct), cancellationToken);
            if (response.Subscription == null)
            {
                throw BillingException.ProviderUnavailable();
            }

            return MapSubscription(response.Subscription);
        }
        catch (MaxioWriteReplayBlockedException ex)
        {
            _logger.LogWarning("Blocked an automatic transport retry of a Maxio subscription POST.");
            var recovered = await FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (recovered != null)
            {
                return recovered;
            }

            throw BillingException.UnknownOutcome(ex);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                var detail = errorList.Errors.Count > 0
                    ? string.Join(" ", errorList.Errors)
                    : "The billing service rejected the subscription.";
                throw new BillingException(HttpStatusCode.UnprocessableEntity, "Subscription rejected", detail, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError(raw, ex);
            }

            throw BillingException.ProviderUnavailable(ex);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw BillingException.ProviderUnavailable(ex);
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetCustomerSubscriptionsAsync(
        string customerReference,
        CancellationToken cancellationToken)
    {
        var customer = await ReadCustomerAsync(customerReference, cancellationToken);
        if (customer == null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        try
        {
            var responses = await BoundedAsync(
                ct => _client.Customers.ListCustomerSubscriptions(customer.Id, ct: ct), cancellationToken);
            return responses
                .Where(response => response.Subscription != null)
                .Select(response => MapSubscription(response.Subscription!))
                .ToArray();
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError(ex.Error, ex);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw BillingException.ProviderUnavailable(ex);
        }
    }

    private async Task<IReadOnlyList<SubscriptionPlanDto>> LoadPlansAsync(CancellationToken cancellationToken)
    {
        try
        {
            var families = await BoundedAsync(
                ct => _client.ProductFamilies.ListProductFamilies(
                    dateField: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    ct: ct), cancellationToken);

            var matches = families
                .Select(response => response.ProductFamily)
                .Where(family => family is { ArchivedAt: null, Id: not null } &&
                    string.Equals(family.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
                .ToArray();

            if (matches.Length != 1)
            {
                throw BillingException.ProviderUnavailable();
            }

            var familyId = matches[0]!.Id!.Value.ToString(CultureInfo.InvariantCulture);
            var plans = new List<SubscriptionPlanDto>();
            for (var page = 1; ; page++)
            {
                var responses = await BoundedAsync(
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
                        perPage: ProductPageSize,
                        ct: ct), cancellationToken);

                plans.AddRange(responses
                    .Select(response => response.Product)
                    .Where(product => product.ArchivedAt == null)
                    .Select(MapAndValidatePlan));

                if (responses.Count < ProductPageSize)
                {
                    break;
                }
            }

            return plans.OrderBy(plan => plan.PriceInCents).ToArray();
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError(ex.Error, ex);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out _))
            {
                throw BillingException.ProviderUnavailable(ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError(raw, ex);
            }

            throw BillingException.ProviderUnavailable(ex);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw BillingException.ProviderUnavailable(ex);
        }
    }

    private async Task<BillingCustomer?> ReadCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                ct => _client.Customers.ReadCustomerByReference(reference, ct: ct), cancellationToken);
            return MapCustomer(response.Customer, reference);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError(ex.Error, ex);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw BillingException.ProviderUnavailable(ex);
        }
    }

    private SubscriptionPlanDto MapAndValidatePlan(Product product)
    {
        if (product.ArchivedAt != null ||
            !string.Equals(product.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(product.Handle) ||
            string.IsNullOrWhiteSpace(product.Name) ||
            !product.PriceInCents.HasValue ||
            !product.Interval.HasValue ||
            product.IntervalUnit == null)
        {
            throw BillingException.ProviderUnavailable();
        }

        if (product.RequireCreditCard == true)
        {
            throw new BillingException(HttpStatusCode.UnprocessableEntity, "Payment method required",
                "This plan cannot be subscribed to through the cardless subscription flow.");
        }

        return new SubscriptionPlanDto(
            product.Handle,
            product.Name,
            product.PriceInCents.Value,
            product.Interval.Value,
            product.IntervalUnit.Value);
    }

    private static BillingCustomer MapCustomer(Customer customer, string expectedReference)
    {
        if (!customer.Id.HasValue ||
            !string.Equals(customer.Reference, expectedReference, StringComparison.Ordinal))
        {
            throw BillingException.ProviderUnavailable();
        }

        return new BillingCustomer(customer.Id.Value, expectedReference);
    }

    private static SubscriptionDto MapSubscription(Subscription subscription)
    {
        var product = subscription.Product;
        var price = subscription.ProductPriceInCents ?? product?.PriceInCents;
        if (!subscription.Id.HasValue ||
            subscription.State == null ||
            product == null ||
            string.IsNullOrWhiteSpace(product.Handle) ||
            string.IsNullOrWhiteSpace(product.Name) ||
            !price.HasValue)
        {
            throw BillingException.ProviderUnavailable();
        }

        return new SubscriptionDto(
            subscription.Id.Value,
            product.Handle,
            product.Name,
            price.Value,
            subscription.State.Value,
            subscription.NextAssessmentAt);
    }

    private static BillingException MapRawError(RawError error, Exception exception)
    {
        var statusCode = error.StatusCode;
        if ((int)statusCode >= 400 && (int)statusCode < 500 &&
            statusCode != HttpStatusCode.Unauthorized && statusCode != HttpStatusCode.Forbidden)
        {
            return new BillingException(statusCode, "Billing request rejected",
                "The billing service rejected the request.", exception);
        }

        return BillingException.ProviderUnavailable(exception);
    }

    private static bool IsInfrastructureFailure(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or JsonException;

    private static async Task<T> BoundedAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CallBudget);
        return await operation(timeout.Token);
    }
}
