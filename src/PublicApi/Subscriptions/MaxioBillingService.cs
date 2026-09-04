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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioBillingService : IMaxioBillingService
{
    public const string HttpClientName = "MaxioAdvancedBilling";

    private const int PageSize = 200;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly SubscriptionRequestCoordinator _coordinator;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        SubscriptionRequestCoordinator coordinator,
        ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _options = options.Value;
        _coordinator = coordinator;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        Required(_options.ApiKey, "Maxio billing credentials are not configured.");
        Required(_options.Subdomain, "Maxio billing site is not configured.");
        var familyHandle = Required(_options.ProductFamilyHandle, "Maxio product family is not configured.");
        var plans = new List<SubscriptionPlanDto>();

        try
        {
            for (var page = 1; ; page++)
            {
                var products = await CallAsync(ct => _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: "handle:" + familyHandle,
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

                foreach (var productResponse in products)
                {
                    var product = productResponse.Product;
                    if (string.IsNullOrWhiteSpace(product.Handle))
                    {
                        continue;
                    }

                    plans.Add(new SubscriptionPlanDto(
                        product.Handle,
                        product.Name,
                        product.PriceInCents,
                        ToPrice(product.PriceInCents),
                        product.Interval,
                        product.IntervalUnit?.Value,
                        product.ProductPricePointHandle,
                        product.ProductPricePointName));
                }

                if (products.Count < PageSize)
                {
                    break;
                }
            }
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out _))
            {
                throw new MaxioBillingException((int)HttpStatusCode.NotFound, "The configured Maxio product family was not found.", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, "Maxio plan lookup failed.", ex);
            }

            throw new MaxioBillingException((int)HttpStatusCode.BadGateway, "Maxio plan lookup failed.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioBillingException((int)HttpStatusCode.BadGateway, "Maxio billing is temporarily unavailable.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MaxioBillingException((int)HttpStatusCode.GatewayTimeout, "Maxio billing did not respond in time.", ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioBillingException((int)HttpStatusCode.BadGateway, "Maxio returned a response that could not be processed.", ex);
        }

        return plans;
    }

    public async Task<SubscriptionDto> SubscribeAsync(string identity, string? planHandle, CancellationToken cancellationToken)
    {
        var email = RequiredIdentity(identity);
        var requestedHandle = Required(planHandle, "A plan handle is required.");
        var plans = await GetPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(x => string.Equals(x.Handle, requestedHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new MaxioBillingException((int)HttpStatusCode.BadRequest, "The requested subscription plan is not available.");
        }

        var normalizedIdentity = email.ToUpperInvariant();
        var customerReference = StableReference("eshop-user", normalizedIdentity);
        var subscriptionReference = StableReference("eshop-subscription", normalizedIdentity + "|" + plan.Handle.ToUpperInvariant());
        using var lease = await _coordinator.AcquireAsync(subscriptionReference, cancellationToken);

        try
        {
            var existing = await FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                return ToSubscriptionDto(existing);
            }

            var customer = await EnsureCustomerAsync(email, customerReference, cancellationToken);
            var response = await CallAsync(ct => _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = plan.Handle,
                        CustomerReference = customerReference,
                        Reference = subscriptionReference,
                        PaymentCollectionMethod = CollectionMethod.Remittance
                    }
                }, ct), cancellationToken);

            var subscription = response.Subscription;
            if (subscription is null)
            {
                throw new MaxioBillingException((int)HttpStatusCode.BadGateway, "Maxio returned no subscription.");
            }

            return ToSubscriptionDto(subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var details))
            {
                _logger.LogWarning("Maxio rejected subscription request: {Errors}",
                    details.Errors is null ? "No validation details" : string.Join("; ", details.Errors));
                var existing = await FindSubscriptionAsync(subscriptionReference, cancellationToken);
                if (existing is not null)
                {
                    return ToSubscriptionDto(existing);
                }

                throw new MaxioBillingException((int)HttpStatusCode.UnprocessableEntity, "Maxio rejected the subscription request.", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, "Maxio rejected the subscription request.", ex);
            }

            throw new MaxioBillingException((int)HttpStatusCode.BadGateway, "Maxio subscription creation failed.", ex);
        }
        catch (HttpRequestException ex)
        {
            var existing = await FindSubscriptionAfterUnknownOutcomeAsync(subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                return ToSubscriptionDto(existing);
            }

            throw new MaxioBillingException((int)HttpStatusCode.BadGateway, "Maxio billing is temporarily unavailable; subscription status is not yet confirmed.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            var existing = await FindSubscriptionAfterUnknownOutcomeAsync(subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                return ToSubscriptionDto(existing);
            }

            throw new MaxioBillingException((int)HttpStatusCode.GatewayTimeout, "Maxio billing did not respond; subscription status is not yet confirmed.", ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioBillingException((int)HttpStatusCode.BadGateway, "Maxio returned a response that could not be processed.", ex);
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(string identity, CancellationToken cancellationToken)
    {
        var email = RequiredIdentity(identity);
        var customerReference = StableReference("eshop-user", email.ToUpperInvariant());
        try
        {
            var customer = await FindCustomerAsync(customerReference, cancellationToken);
            if (customer is null || customer.Id is null)
            {
                return Array.Empty<SubscriptionDto>();
            }

            var responses = await CallAsync(ct => _client.Customers.ListCustomerSubscriptions(customer.Id.Value, ct), cancellationToken);
            var subscriptions = new List<SubscriptionDto>(responses.Count);
            foreach (var response in responses)
            {
                if (response.Subscription is null)
                {
                    throw new MaxioBillingException((int)HttpStatusCode.BadGateway, "Maxio returned an invalid subscription response.");
                }

                subscriptions.Add(ToSubscriptionDto(response.Subscription));
            }

            return subscriptions;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error, "Maxio subscription lookup failed.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioBillingException((int)HttpStatusCode.BadGateway, "Maxio billing is temporarily unavailable.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MaxioBillingException((int)HttpStatusCode.GatewayTimeout, "Maxio billing did not respond in time.", ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioBillingException((int)HttpStatusCode.BadGateway, "Maxio returned a response that could not be processed.", ex);
        }
    }

    private async Task<Customer> EnsureCustomerAsync(string email, string reference, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var response = await CallAsync(ct => _client.Customers.CreateCustomer(
                new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = "eShopOnWeb",
                        LastName = "Customer",
                        Email = email,
                        Reference = reference
                    }
                }, ct), cancellationToken);

            if (response.Customer is null || response.Customer.Id is null)
            {
                throw new MaxioBillingException((int)HttpStatusCode.BadGateway, "Maxio returned an invalid customer response.");
            }

            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                existing = await FindCustomerAsync(reference, cancellationToken);
                if (existing is not null)
                {
                    return existing;
                }

                throw new MaxioBillingException((int)HttpStatusCode.UnprocessableEntity, "Maxio rejected the customer request.", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, "Maxio rejected the customer request.", ex);
            }

            throw new MaxioBillingException((int)HttpStatusCode.BadGateway, "Maxio customer creation failed.", ex);
        }
        catch (HttpRequestException ex)
        {
            existing = await FindCustomerAfterUnknownOutcomeAsync(reference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            throw new MaxioBillingException((int)HttpStatusCode.BadGateway, "Maxio billing is temporarily unavailable; customer status is not yet confirmed.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            existing = await FindCustomerAfterUnknownOutcomeAsync(reference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            throw new MaxioBillingException((int)HttpStatusCode.GatewayTimeout, "Maxio billing did not respond; customer status is not yet confirmed.", ex);
        }
    }

    private async Task<Customer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await CallAsync(ct => _client.Customers.ReadCustomerByReference(reference, ct), cancellationToken);
            if (response.Customer is null || response.Customer.Id is null)
            {
                throw new MaxioBillingException((int)HttpStatusCode.BadGateway, "Maxio returned an invalid customer response.");
            }

            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error, "Maxio customer lookup failed.", ex);
        }
    }

    private async Task<Subscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await CallAsync(ct => _client.Subscriptions.FindSubscription(reference: reference, ct: ct), cancellationToken);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, "Maxio subscription lookup failed.", ex);
            }

            throw new MaxioBillingException((int)HttpStatusCode.BadGateway, "Maxio subscription lookup failed.", ex);
        }
    }

    private async Task<Customer?> FindCustomerAfterUnknownOutcomeAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            return await FindCustomerAsync(reference, cancellationToken);
        }
        catch (MaxioBillingException)
        {
            return null;
        }
    }

    private async Task<Subscription?> FindSubscriptionAfterUnknownOutcomeAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            return await FindSubscriptionAsync(reference, cancellationToken);
        }
        catch (MaxioBillingException)
        {
            return null;
        }
    }

    private static SubscriptionDto ToSubscriptionDto(Subscription subscription)
    {
        var product = subscription.Product;
        var priceInCents = subscription.ProductPriceInCents ?? product?.PriceInCents;
        return new SubscriptionDto(
            subscription.Reference,
            product?.Handle,
            product?.Name,
            priceInCents,
            ToPrice(priceInCents),
            product?.Interval,
            product?.IntervalUnit?.Value,
            subscription.State?.Value,
            subscription.NextAssessmentAt);
    }

    private static decimal? ToPrice(long? priceInCents) => priceInCents / 100m;

    private async Task<T> CallAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CallBudget);
        return await call(timeout.Token);
    }

    private static string Required(string? value, string message) =>
        string.IsNullOrWhiteSpace(value) ? throw new MaxioBillingException((int)HttpStatusCode.ServiceUnavailable, message) : value.Trim();

    private static string RequiredIdentity(string identity)
    {
        var value = identity.Trim();
        if (string.IsNullOrWhiteSpace(value) || !value.Contains('@', StringComparison.Ordinal))
        {
            throw new MaxioBillingException((int)HttpStatusCode.Unauthorized, "The authenticated user does not have a usable billing identity.");
        }

        return value.ToLowerInvariant();
    }

    private static string StableReference(string prefix, string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return prefix + "-" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static MaxioBillingException FromRaw(RawError error, string message, Exception inner) =>
        new((int)error.StatusCode, message, inner);
}
