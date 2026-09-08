using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionServices;

public class MaxioSubscriptionService : ISubscriptionService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private static readonly SubscriptionState[] ClosedStates =
    {
        SubscriptionState.Canceled,
        SubscriptionState.Expired,
        SubscriptionState.FailedToCreate
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _subscribeGates =
        new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.Ordinal);

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, IOptions<MaxioOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);
        var products = await ListFamilyProductsAsync(familyId, cancellationToken);

        return products
            .Where(product => product.ArchivedAt == null)
            .Select(ToPlanDto)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(MaxioShopper shopper, string planHandle, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new MaxioProviderException(HttpStatusCode.BadRequest, "A subscription plan handle is required.");
        }

        var gate = _subscribeGates.GetOrAdd(shopper.Reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await SubscribeCoreAsync(shopper, planHandle, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetailsDto>> GetSubscriptionsAsync(MaxioShopper shopper, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var customer = await FindCustomerByReferenceAsync(shopper.Reference, cancellationToken);
        if (customer?.Id is not int customerId)
        {
            return Array.Empty<SubscriptionDetailsDto>();
        }

        var responses = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        var details = new List<SubscriptionDetailsDto>();
        foreach (var response in responses)
        {
            var subscription = response.Subscription;
            if (subscription?.Id == null)
            {
                continue;
            }

            var refreshed = await TryReadSubscriptionAsync(subscription.Id.Value, cancellationToken);
            if (refreshed != null)
            {
                details.Add(refreshed);
            }
        }

        return details;
    }

    private async Task<SubscribeResult> SubscribeCoreAsync(MaxioShopper shopper, string planHandle, CancellationToken cancellationToken)
    {
        var customer = await FindOrCreateCustomerAsync(shopper, cancellationToken);
        if (customer.Id is not int customerId)
        {
            throw new MaxioProviderException(HttpStatusCode.InternalServerError, "Maxio did not return a customer identifier.");
        }

        var existing = await FindOngoingSubscriptionForPlanAsync(customerId, planHandle, cancellationToken);
        if (existing?.Id is int existingId)
        {
            var refreshed = await TryReadSubscriptionAsync(existingId, cancellationToken);
            if (refreshed != null)
            {
                return new SubscribeResult { Subscription = refreshed, CreatedNew = false };
            }
        }

        return await CreateNewSubscriptionAsync(customer.Id.Value, shopper, planHandle, cancellationToken);
    }

    private async Task<SubscribeResult> CreateNewSubscriptionAsync(int customerId, MaxioShopper shopper, string planHandle, CancellationToken cancellationToken)
    {
        var request = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = planHandle,
                CustomerReference = shopper.Reference,
                PaymentCollectionMethod = CollectionMethod.Remittance
            }
        };

        try
        {
            var response = await ExecuteAsync(
                token => _client.Subscriptions.CreateSubscription(request, ct: token), cancellationToken);
            var subscription = response.Subscription;
            if (subscription == null)
            {
                throw new MaxioProviderException(HttpStatusCode.BadGateway, "Maxio returned an empty subscription response.");
            }

            return new SubscribeResult { Subscription = ToDetails(subscription), CreatedNew = true };
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var validation))
            {
                var message = validation.Errors is { Count: > 0 }
                    ? string.Join(" ", validation.Errors)
                    : "The billing provider rejected the subscription request.";
                throw new MaxioProviderException(HttpStatusCode.UnprocessableEntity, message);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Translate(raw, "creating the subscription");
            }

            throw new MaxioProviderException(HttpStatusCode.BadGateway, "The billing provider returned an unrecognised error response.");
        }
        catch (HttpRequestException ex)
        {
            var reconciled = await TryReconcileCreatedSubscriptionAsync(customerId, planHandle, cancellationToken);
            if (reconciled != null)
            {
                return reconciled;
            }

            throw new MaxioProviderException(
                HttpStatusCode.BadGateway,
                "The billing provider could not be reached; whether the subscription was created is unknown. Check your subscriptions before retrying.",
                ex);
        }
    }

    private async Task<SubscribeResult?> TryReconcileCreatedSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        try
        {
            var existing = await FindOngoingSubscriptionForPlanAsync(customerId, planHandle, cancellationToken);
            if (existing?.Id is int existingId)
            {
                var refreshed = await TryReadSubscriptionAsync(existingId, cancellationToken);
                if (refreshed != null)
                {
                    return new SubscribeResult { Subscription = refreshed, CreatedNew = true };
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
        }

        return null;
    }

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await ExecuteAsync(
                token => _client.ProductFamilies.ListProductFamilies(
                    dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, ct: token),
                cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex.Error, "locating the subscription product family");
        }

        var matched = families
            .Select(response => response.ProductFamily)
            .FirstOrDefault(candidate => candidate is not null &&
                string.Equals(candidate.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal));

        if (matched?.Id is not int familyId)
        {
            throw new MaxioConfigurationException(
                $"The configured Maxio product family '{_options.ProductFamilyHandle}' was not found.");
        }

        return familyId;
    }

    private async Task<IReadOnlyList<Product>> ListFamilyProductsAsync(int familyId, CancellationToken cancellationToken)
    {
        const int perPage = 100;
        var products = new List<Product>();
        var page = 1;

        while (true)
        {
            var responses = await FetchFamilyProductsPageAsync(familyId, page, perPage, cancellationToken);
            products.AddRange(responses.Select(response => response.Product));
            if (responses.Count < perPage)
            {
                break;
            }

            page++;
        }

        return products;
    }

    private async Task<IReadOnlyList<ProductResponse>> FetchFamilyProductsPageAsync(int familyId, int page, int perPage, CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteAsync(
                token => _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyId.ToString(CultureInfo.InvariantCulture),
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: page,
                    perPage: perPage,
                    ct: token),
                cancellationToken);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var _))
            {
                throw new MaxioProviderException(HttpStatusCode.NotFound, "The configured Maxio product family was not found on the site.");
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Translate(raw, "loading subscription plans");
            }

            throw new MaxioProviderException(HttpStatusCode.BadGateway, "The billing provider returned an unrecognised error response while loading subscription plans.");
        }
    }

    private async Task<Customer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await ExecuteAsync(
                token => _client.Customers.ReadCustomerByReference(reference, ct: token), cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex.Error, "looking up the shopper");
        }
    }

    private async Task<Customer> FindOrCreateCustomerAsync(MaxioShopper shopper, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerByReferenceAsync(shopper.Reference, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = shopper.FirstName,
                LastName = shopper.LastName,
                Email = shopper.Email,
                Reference = shopper.Reference
            }
        };

        try
        {
            var response = await ExecuteAsync(
                token => _client.Customers.CreateCustomer(request, ct: token), cancellationToken);
            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var _))
            {
                var created = await FindCustomerByReferenceAsync(shopper.Reference, cancellationToken);
                if (created != null)
                {
                    return created;
                }

                throw new MaxioProviderException(HttpStatusCode.UnprocessableEntity, "The billing provider rejected the shopper details.");
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Translate(raw, "creating the shopper");
            }

            throw new MaxioProviderException(HttpStatusCode.BadGateway, "The billing provider returned an unrecognised error response.");
        }
    }

    private async Task<Subscription?> FindOngoingSubscriptionForPlanAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        var responses = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return responses
            .Select(response => response.Subscription)
            .FirstOrDefault(subscription =>
                subscription != null &&
                IsOngoing(subscription.State) &&
                string.Equals(subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteAsync(
                token => _client.Customers.ListCustomerSubscriptions(customerId, ct: token), cancellationToken);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<SubscriptionResponse>();
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex.Error, "loading the shopper's subscriptions");
        }
    }

    private async Task<SubscriptionDetailsDto?> TryReadSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await ExecuteAsync(
                token => _client.Subscriptions.ReadSubscription(subscriptionId, include: null, ct: token), cancellationToken);
            var subscription = response.Subscription;
            return subscription == null ? null : ToDetails(subscription);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex.Error, "refreshing the subscription");
        }
    }

    private async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CallBudget);

        try
        {
            return await action(timeout.Token);
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioProviderException(HttpStatusCode.BadGateway, "The billing provider could not be reached.", ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioProviderException(HttpStatusCode.BadGateway, "The billing provider returned an unreadable response.", ex);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MaxioProviderException(HttpStatusCode.GatewayTimeout, "The billing provider did not respond within the allotted time.");
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new MaxioConfigurationException("Maxio:ApiKey is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.Subdomain))
        {
            throw new MaxioConfigurationException("Maxio:Subdomain is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new MaxioConfigurationException("Maxio:ProductFamilyHandle is not configured.");
        }
    }

    private static bool IsOngoing(SubscriptionState? state)
    {
        return state is not null && !ClosedStates.Contains(state);
    }

    private static SubscriptionPlanDto ToPlanDto(Product product)
    {
        return new SubscriptionPlanDto
        {
            Id = product.Id,
            Handle = product.Handle,
            Name = product.Name,
            PriceInCents = product.PriceInCents,
            Price = ToMajorUnits(product.PriceInCents),
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit?.Value
        };
    }

    private static SubscriptionDetailsDto ToDetails(Subscription subscription)
    {
        return new SubscriptionDetailsDto
        {
            Id = subscription.Id,
            PlanHandle = subscription.Product?.Handle,
            PlanName = subscription.Product?.Name,
            State = subscription.State?.Value,
            PriceInCents = subscription.ProductPriceInCents,
            Price = ToMajorUnits(subscription.ProductPriceInCents),
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextBillingDate = subscription.NextAssessmentAt,
            CreatedAt = subscription.CreatedAt
        };
    }

    private static decimal? ToMajorUnits(long? priceInCents)
    {
        return priceInCents.HasValue ? priceInCents.Value / 100m : null;
    }

    private static MaxioProviderException Translate(RawError raw, string context)
    {
        if (raw.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return new MaxioProviderException(HttpStatusCode.TooManyRequests, "The billing provider is receiving too many requests; please retry shortly.");
        }

        if ((int)raw.StatusCode >= 500)
        {
            return new MaxioProviderException(HttpStatusCode.BadGateway, $"The billing provider failed while {context}.");
        }

        if (raw.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new MaxioProviderException(raw.StatusCode, "The billing provider rejected this application's credentials.");
        }

        return new MaxioProviderException(raw.StatusCode, $"The billing provider rejected the request while {context}.");
    }
}
