using System;
using System.Collections.Generic;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(25);
    private const int ProductsPageSize = 200;
    private const string CustomerReferencePrefix = "eshop:";

    private readonly MaxioAdvancedBillingClient _client;
    private readonly IOptions<MaxioOptions> _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    public Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct = default)
        => GuardAsync(ListPlansCoreAsync, ct);

    public Task<SubscriptionEnrollmentResult> SubscribeAsync(string userName, string planHandle, CancellationToken ct = default)
        => GuardAsync(token => SubscribeCoreAsync(userName, planHandle, token), ct);

    public Task<IReadOnlyList<SubscriptionDto>> ListUserSubscriptionsAsync(string userName, CancellationToken ct = default)
        => GuardAsync(token => ListUserSubscriptionsCoreAsync(userName, token), ct);

    private async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansCoreAsync(CancellationToken token)
    {
        var products = await ListFamilyProductsAsync(token);
        var currency = await ReadSiteCurrencyAsync(token);

        var plans = new List<SubscriptionPlanDto>(products.Count);
        foreach (var productResponse in products)
        {
            var product = productResponse.Product;
            if (product is null)
            {
                continue;
            }

            plans.Add(new SubscriptionPlanDto
            {
                Handle = product.Handle,
                Name = product.Name,
                Price = ToMoney(product.PriceInCents),
                Currency = currency,
                Interval = product.Interval ?? 0,
                IntervalUnit = product.IntervalUnit?.Value
            });
        }

        return plans;
    }

    private async Task<SubscriptionEnrollmentResult> SubscribeCoreAsync(string userName, string planHandle, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new MaxioApiException("The authenticated user could not be identified.", HttpStatusCode.BadRequest);
        }

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new MaxioApiException("A plan handle is required to subscribe.", HttpStatusCode.BadRequest);
        }

        var customerReference = BuildCustomerReference(userName);
        var subscriptionReference = BuildSubscriptionReference(userName, planHandle);

        var existing = await FindSubscriptionByReferenceAsync(subscriptionReference, token);
        if (existing is not null)
        {
            return new SubscriptionEnrollmentResult { Subscription = ToDto(existing), Created = false };
        }

        var customer = await EnsureCustomerAsync(customerReference, userName, token);
        if (customer is null)
        {
            throw new MaxioApiException("The billing provider could not establish the customer.", HttpStatusCode.BadGateway);
        }

        Subscription created;
        try
        {
            var response = await _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = planHandle,
                        CustomerReference = customerReference,
                        Reference = subscriptionReference,
                        PaymentCollectionMethod = CollectionMethod.Remittance
                    }
                },
                token);

            created = response.Subscription
                ?? throw new MaxioApiException("The billing provider did not return the created subscription.", HttpStatusCode.BadGateway);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            return await ReconcileCreateSubscriptionAsync(ex, subscriptionReference, planHandle, token);
        }
        catch (JsonException ex)
        {
            var reconciled = await FindSubscriptionByReferenceAsync(subscriptionReference, token);
            if (reconciled is not null)
            {
                _logger.LogWarning(ex, "Subscription {Reference} create outcome was ambiguous; recovered an existing subscription.", subscriptionReference);
                return new SubscriptionEnrollmentResult { Subscription = ToDto(reconciled), Created = false };
            }

            throw new MaxioApiException("The billing provider returned a response that could not be processed.", HttpStatusCode.BadGateway, ex);
        }

        return new SubscriptionEnrollmentResult { Subscription = ToDto(created), Created = true };
    }

    private async Task<IReadOnlyList<SubscriptionDto>> ListUserSubscriptionsCoreAsync(string userName, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new MaxioApiException("The authenticated user could not be identified.", HttpStatusCode.BadRequest);
        }

        var customer = await FindCustomerByReferenceAsync(BuildCustomerReference(userName), token);
        if (customer is null || customer.Id is not int customerId)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var responses = await _client.Customers.ListCustomerSubscriptions(customerId, token);

        var subscriptions = new List<SubscriptionDto>(responses.Count);
        foreach (var response in responses)
        {
            if (response.Subscription is not null)
            {
                subscriptions.Add(ToDto(response.Subscription));
            }
        }

        return subscriptions;
    }

    private async Task<SubscriptionEnrollmentResult> ReconcileCreateSubscriptionAsync(
        SdkException<CreateSubscriptionError> exception,
        string subscriptionReference,
        string planHandle,
        CancellationToken token)
    {
        var reconciled = await FindSubscriptionByReferenceAsync(subscriptionReference, token);
        if (reconciled is not null)
        {
            _logger.LogWarning(exception, "Subscription {Reference} create was rejected as a duplicate; recovered the existing subscription.", subscriptionReference);
            return new SubscriptionEnrollmentResult { Subscription = ToDto(reconciled), Created = false };
        }

        if (exception.Error.TryGetRawError(out var raw))
        {
            throw MapRaw(raw);
        }

        if (exception.Error.TryGetErrorListResponse1(out var errorList) && errorList?.Errors is { Count: > 0 })
        {
            throw new MaxioApiException(
                "The billing provider rejected the subscription request: " + string.Join(" | ", errorList.Errors),
                HttpStatusCode.UnprocessableEntity,
                exception);
        }

        throw new MaxioApiException(
            $"The billing provider rejected the subscription request for plan '{planHandle}'.",
            HttpStatusCode.UnprocessableEntity,
            exception);
    }

    private async Task<Customer> EnsureCustomerAsync(string customerReference, string userName, CancellationToken token)
    {
        var existing = await FindCustomerByReferenceAsync(customerReference, token);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitUserName(userName);

        try
        {
            var response = await _client.Customers.CreateCustomer(
                new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = userName,
                        Reference = customerReference
                    }
                },
                token);

            return response.Customer
                ?? throw new MaxioApiException("The billing provider did not return the created customer.", HttpStatusCode.BadGateway);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            var reconciled = await FindCustomerByReferenceAsync(customerReference, token);
            if (reconciled is not null)
            {
                _logger.LogWarning(ex, "Customer {Reference} create was rejected as a duplicate; recovered the existing customer.", customerReference);
                return reconciled;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRaw(raw);
            }

            throw new MaxioApiException("The billing provider rejected the customer request.", HttpStatusCode.UnprocessableEntity, ex);
        }
        catch (JsonException ex)
        {
            var reconciled = await FindCustomerByReferenceAsync(customerReference, token);
            if (reconciled is not null)
            {
                _logger.LogWarning(ex, "Customer {Reference} create outcome was ambiguous; recovered the existing customer.", customerReference);
                return reconciled;
            }

            throw new MaxioApiException("The billing provider returned a response that could not be processed.", HttpStatusCode.BadGateway, ex);
        }
    }

    private async Task<List<ProductResponse>> ListFamilyProductsAsync(CancellationToken token)
    {
        var familyHandle = _options.Value.ProductFamilyHandle;
        var results = new List<ProductResponse>();
        var page = 1;

        while (true)
        {
            try
            {
                var batch = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: "handle:" + familyHandle,
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: null,
                    include: null,
                    page: page,
                    perPage: ProductsPageSize,
                    ct: token);

                results.AddRange(batch);
                if (batch.Count < ProductsPageSize)
                {
                    return results;
                }

                page++;
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out _))
                {
                    throw new MaxioApiException("The configured billing product family could not be found.", HttpStatusCode.BadGateway, ex);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw MapRaw(raw);
                }

                throw new MaxioApiException("The billing provider could not list the configured product family.", HttpStatusCode.BadGateway, ex);
            }
        }
    }

    private async Task<string?> ReadSiteCurrencyAsync(CancellationToken token)
    {
        var response = await _client.Sites.ReadSite(token);
        return response.Site?.Currency;
    }

    private async Task<Customer?> FindCustomerByReferenceAsync(string reference, CancellationToken token)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, token);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<Subscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken token)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(reference: reference, ct: token);
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
                throw MapRaw(raw);
            }

            throw new MaxioApiException("The billing provider could not look up the subscription.", HttpStatusCode.BadGateway, ex);
        }
    }

    private async Task<T> GuardAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        if (!_options.Value.IsConfigured)
        {
            throw new MaxioConfigurationException(
                "Maxio billing is not configured. Set Maxio:ApiKey, Maxio:Subdomain or Maxio:BaseUrl, and Maxio:ProductFamilyHandle.");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallTimeout);

        try
        {
            return await operation(cts.Token);
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRaw(ex.Error);
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioApiException("The billing provider could not be reached.", HttpStatusCode.BadGateway, ex);
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            throw new MaxioApiException("The billing provider did not respond in time.", HttpStatusCode.GatewayTimeout, ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioApiException("The billing provider returned a response that could not be processed.", HttpStatusCode.BadGateway, ex);
        }
    }

    private static SubscriptionDto ToDto(Subscription subscription) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        State = subscription.State?.Value,
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        Price = ToMoney(subscription.ProductPriceInCents),
        Currency = subscription.Currency,
        Interval = subscription.Product?.Interval,
        IntervalUnit = subscription.Product?.IntervalUnit?.Value,
        NextBillingDate = subscription.CurrentPeriodEndsAt,
        CreatedAt = subscription.CreatedAt
    };

    private static MaxioApiException MapRaw(RawError raw)
    {
        var statusCode = raw.StatusCode;
        var numericStatus = (int)statusCode;

        var isClientError = numericStatus is >= 400 and < 500
            && statusCode is not HttpStatusCode.Unauthorized and not HttpStatusCode.Forbidden;

        return isClientError
            ? new MaxioApiException("The billing provider rejected the request.", statusCode)
            : new MaxioApiException("The billing provider reported an error or is unavailable.", HttpStatusCode.BadGateway);
    }

    private static decimal ToMoney(long? priceInCents)
        => priceInCents.HasValue ? Math.Round(priceInCents.Value / 100m, 2) : 0m;

    private static string BuildCustomerReference(string userName) => CustomerReferencePrefix + userName;

    private static string BuildSubscriptionReference(string userName, string planHandle)
        => $"{CustomerReferencePrefix}{userName}:{planHandle}";

    private static (string FirstName, string LastName) SplitUserName(string userName)
    {
        var atIndex = userName.IndexOf('@');
        if (atIndex > 0)
        {
            return (userName[..atIndex], userName[(atIndex + 1)..]);
        }

        return (userName, "User");
    }
}
