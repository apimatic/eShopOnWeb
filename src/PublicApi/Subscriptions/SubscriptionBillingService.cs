using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System;
using System.Linq;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionBillingService
{
    private const int ProductsPerPage = 200;
    private static readonly TimeSpan ProviderCallBudget = TimeSpan.FromSeconds(30);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _settings;
    private readonly ILogger<SubscriptionBillingService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _customerLocks = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _subscriptionLocks = new();

    public SubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> settings,
        ILogger<SubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var plans = new List<SubscriptionPlanDto>();

        for (var page = 1; ; page++)
        {
            IReadOnlyList<ProductResponse> pageItems;
            try
            {
                pageItems = await Bounded(token => _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: $"handle:{_settings.ProductFamilyHandle}",
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
                    ct: token), cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                _logger.LogWarning(ex, "Maxio rejected the subscription plan lookup.");
                if (ex.Error.TryGetString(out _))
                {
                    throw new SubscriptionBillingException(HttpStatusCode.BadGateway,
                        "Subscription plans could not be loaded.", ex);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw ProviderFailure(raw.StatusCode, "Subscription plans could not be loaded.", ex);
                }

                throw new SubscriptionBillingException(HttpStatusCode.BadGateway,
                    "Subscription plans could not be loaded.", ex);
            }
            catch (JsonException ex)
            {
                throw new SubscriptionBillingException(HttpStatusCode.BadGateway,
                    "Subscription plans could not be processed.", ex);
            }
            catch (HttpRequestException ex)
            {
                throw new SubscriptionBillingException(HttpStatusCode.BadGateway,
                    "The subscription provider is unavailable.", ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new SubscriptionBillingException(HttpStatusCode.GatewayTimeout,
                    "The subscription provider did not respond in time.", ex);
            }

            foreach (var item in pageItems)
            {
                var product = item.Product;
                if (product.ArchivedAt is not null || string.IsNullOrWhiteSpace(product.Handle))
                {
                    continue;
                }

                plans.Add(new SubscriptionPlanDto(
                    product.Handle,
                    product.Name ?? product.Handle,
                    product.PriceInCents,
                    product.Interval,
                    product.IntervalUnit?.Value,
                    product.ProductPricePointHandle,
                    product.ProductPricePointName)
                {
                    ProductId = product.Id,
                    PricePointId = product.ProductPricePointId
                });
            }

            if (pageItems.Count < ProductsPerPage)
            {
                break;
            }
        }

        return plans;
    }

    public async Task<SubscriptionDto> SubscribeAsync(
        CurrentUserIdentity user,
        string planHandle,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new SubscriptionBillingException(HttpStatusCode.BadRequest,
                "A plan handle is required.");
        }

        var plan = (await GetPlansAsync(cancellationToken))
            .FirstOrDefault(candidate => string.Equals(candidate.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new SubscriptionBillingException(HttpStatusCode.BadRequest,
                "The selected subscription plan is not available.");
        }

        var customer = await EnsureCustomerAsync(user, cancellationToken);
        var subscription = await EnsureSubscriptionAsync(customer, plan, user, cancellationToken);
        return MapSubscription(subscription);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(
        CurrentUserIdentity user,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var customer = await EnsureCustomerAsync(user, cancellationToken);
        IReadOnlyList<SubscriptionResponse> responses;
        try
        {
            responses = await Bounded(token => _client.Customers.ListCustomerSubscriptions(customer.Id!.Value, token), cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure(ex.Error.StatusCode, "Your subscriptions could not be loaded.", ex);
        }
        catch (JsonException ex)
        {
            throw new SubscriptionBillingException(HttpStatusCode.BadGateway,
                "Your subscriptions could not be processed.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new SubscriptionBillingException(HttpStatusCode.BadGateway,
                "The subscription provider is unavailable.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SubscriptionBillingException(HttpStatusCode.GatewayTimeout,
                "The subscription provider did not respond in time.", ex);
        }

        return responses
            .Where(response => response.Subscription is not null)
            .Select(response => MapSubscription(response.Subscription!))
            .ToArray();
    }

    private async Task<Customer> EnsureCustomerAsync(CurrentUserIdentity user, CancellationToken cancellationToken)
    {
        var gate = _customerLocks.GetOrAdd(user.Reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await ReadCustomerAsync(user.Reference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            try
            {
                var response = await Bounded(token => _client.Customers.CreateCustomer(
                    body: new CreateCustomerRequest
                    {
                            Customer = new CreateCustomer
                        {
                            FirstName = user.FirstName,
                            LastName = user.LastName,
                            Email = user.Email,
                            Reference = user.Reference
                        }
                    },
                    ct: token), cancellationToken);
                return RequireCustomer(response.Customer);
            }
            catch (SdkException<CreateCustomerError> ex)
            {
                var reconciled = await ReconcileCustomerAsync(user.Reference, cancellationToken);
                if (reconciled is not null)
                {
                    return reconciled;
                }

                _logger.LogWarning(ex, "Maxio rejected customer creation for a user reference.");
                if (ex.Error.TryGetCustomerErrorResponse1(out _))
                {
                    throw new SubscriptionBillingException(HttpStatusCode.UnprocessableEntity,
                        "The subscription customer could not be created.", ex);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw ProviderFailure(raw.StatusCode, "The subscription customer could not be created.", ex);
                }

                throw new SubscriptionBillingException(HttpStatusCode.BadGateway,
                    "The subscription customer could not be created.", ex);
            }
            catch (JsonException ex)
            {
                var reconciled = await ReconcileCustomerAsync(user.Reference, cancellationToken);
                if (reconciled is not null)
                {
                    return reconciled;
                }

                throw new SubscriptionBillingException(HttpStatusCode.BadGateway,
                    "The subscription customer response could not be processed.", ex);
            }
            catch (HttpRequestException ex)
            {
                var reconciled = await ReconcileCustomerAsync(user.Reference, cancellationToken);
                if (reconciled is not null)
                {
                    return reconciled;
                }

                throw new SubscriptionBillingException(HttpStatusCode.BadGateway,
                    "The subscription provider is unavailable; customer enrollment is unknown.", ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new SubscriptionBillingException(HttpStatusCode.GatewayTimeout,
                    "The subscription provider did not respond in time; customer enrollment is unknown.", ex);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<Customer?> ReadCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(token => _client.Customers.ReadCustomerByReference(reference, token), cancellationToken);
            return RequireCustomer(response.Customer);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure(ex.Error.StatusCode, "The subscription customer could not be loaded.", ex);
        }
        catch (JsonException ex)
        {
            throw new SubscriptionBillingException(HttpStatusCode.BadGateway,
                "The subscription customer response could not be processed.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new SubscriptionBillingException(HttpStatusCode.BadGateway,
                "The subscription provider is unavailable.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SubscriptionBillingException(HttpStatusCode.GatewayTimeout,
                "The subscription provider did not respond in time.", ex);
        }
    }

    private async Task<Customer?> ReconcileCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            return await ReadCustomerAsync(reference, cancellationToken);
        }
        catch (SubscriptionBillingException)
        {
            return null;
        }
    }

    private async Task<Subscription> EnsureSubscriptionAsync(
        Customer customer,
        SubscriptionPlanDto plan,
        CurrentUserIdentity user,
        CancellationToken cancellationToken)
    {
        var reference = $"eshop-sub:{user.Reference[10..]}:{plan.Handle}";
        var gate = _subscriptionLocks.GetOrAdd(reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await FindSubscriptionAsync(reference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            var customerSubscriptions = await ListCustomerSubscriptionsAsync(customer.Id!.Value, cancellationToken);
            var existingForPlan = customerSubscriptions
                .Select(response => response.Subscription)
                .FirstOrDefault(subscription =>
                    subscription is not null &&
                    (string.Equals(subscription.Reference, reference, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(subscription.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase)));
            if (existingForPlan is not null)
            {
                return existingForPlan;
            }

            try
            {
                if (plan.ProductId is not int productId)
                {
                    throw new SubscriptionBillingException(HttpStatusCode.BadGateway,
                        "The selected subscription plan did not include a provider identifier.");
                }

                var body = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
                {
                    Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                    {
                        CustomerId = customer.Id,
                        ProductId = productId,
                        ProductPricePointId = plan.PricePointId,
                        PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Invoice,
                        Reference = reference
                    }
                };
                var response = await Bounded(token => _client.Subscriptions.CreateSubscription(body, token), cancellationToken);
                return RequireSubscription(response.Subscription);
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                var reconciled = await ReconcileSubscriptionAsync(reference, customer.Id.Value, plan.Handle, cancellationToken);
                if (reconciled is not null)
                {
                    return reconciled;
                }

                _logger.LogWarning(ex, "Maxio rejected subscription creation for a user reference.");
                if (ex.Error.TryGetErrorListResponse1(out var details))
                {
                    _logger.LogWarning("Maxio subscription validation errors: {Errors}",
                        string.Join("; ", details.Errors));
                    throw new SubscriptionBillingException(HttpStatusCode.UnprocessableEntity,
                        "The subscription could not be created.", ex);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw ProviderFailure(raw.StatusCode, "The subscription could not be created.", ex);
                }

                throw new SubscriptionBillingException(HttpStatusCode.BadGateway,
                    "The subscription could not be created.", ex);
            }
            catch (JsonException ex)
            {
                var reconciled = await ReconcileSubscriptionAsync(reference, customer.Id.Value, plan.Handle, cancellationToken);
                if (reconciled is not null)
                {
                    return reconciled;
                }

                throw new SubscriptionBillingException(HttpStatusCode.BadGateway,
                    "The subscription response could not be processed; enrollment is unknown.", ex);
            }
            catch (HttpRequestException ex)
            {
                var reconciled = await ReconcileSubscriptionAsync(reference, customer.Id.Value, plan.Handle, cancellationToken);
                if (reconciled is not null)
                {
                    return reconciled;
                }

                throw new SubscriptionBillingException(HttpStatusCode.BadGateway,
                    "The subscription provider is unavailable; enrollment is unknown.", ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new SubscriptionBillingException(HttpStatusCode.GatewayTimeout,
                    "The subscription provider did not respond in time; enrollment is unknown.", ex);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<Subscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(token => _client.Subscriptions.FindSubscription(reference: reference, ct: token), cancellationToken);
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
                if (raw.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                throw ProviderFailure(raw.StatusCode, "The subscription could not be loaded.", ex);
            }

            throw new SubscriptionBillingException(HttpStatusCode.BadGateway,
                "The subscription could not be loaded.", ex);
        }
        catch (JsonException ex)
        {
            throw new SubscriptionBillingException(HttpStatusCode.BadGateway,
                "The subscription response could not be processed.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new SubscriptionBillingException(HttpStatusCode.BadGateway,
                "The subscription provider is unavailable.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SubscriptionBillingException(HttpStatusCode.GatewayTimeout,
                "The subscription provider did not respond in time.", ex);
        }
    }

    private async Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await Bounded(token => _client.Customers.ListCustomerSubscriptions(customerId, token), cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure(ex.Error.StatusCode, "The customer's subscriptions could not be loaded.", ex);
        }
        catch (JsonException ex)
        {
            throw new SubscriptionBillingException(HttpStatusCode.BadGateway,
                "The customer's subscriptions could not be processed.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new SubscriptionBillingException(HttpStatusCode.BadGateway,
                "The subscription provider is unavailable.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SubscriptionBillingException(HttpStatusCode.GatewayTimeout,
                "The subscription provider did not respond in time.", ex);
        }
    }

    private async Task<Subscription?> ReconcileSubscriptionAsync(
        string reference,
        int customerId,
        string planHandle,
        CancellationToken cancellationToken)
    {
        try
        {
            var byReference = await FindSubscriptionAsync(reference, cancellationToken);
            if (byReference is not null)
            {
                return byReference;
            }

            var responses = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);
            return responses
                .Select(response => response.Subscription)
                .FirstOrDefault(subscription => subscription is not null &&
                    (string.Equals(subscription.Reference, reference, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase)));
        }
        catch (SubscriptionBillingException)
        {
            return null;
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> operation, CancellationToken requestCancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(requestCancellationToken);
        timeout.CancelAfter(ProviderCallBudget);
        return await operation(timeout.Token);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey) ||
            string.IsNullOrWhiteSpace(_settings.Subdomain) ||
            string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            throw new SubscriptionBillingException(HttpStatusCode.InternalServerError,
                "Subscription billing is not configured.");
        }
    }

    private static Customer RequireCustomer(Customer? customer)
    {
        if (customer?.Id is not int id)
        {
            throw new SubscriptionBillingException(HttpStatusCode.BadGateway,
                "The subscription provider returned an invalid customer.");
        }

        return customer;
    }

    private static Subscription RequireSubscription(Subscription? subscription)
    {
        if (subscription?.Id is not int)
        {
            throw new SubscriptionBillingException(HttpStatusCode.BadGateway,
                "The subscription provider returned an invalid subscription.");
        }

        return subscription;
    }

    private static SubscriptionDto MapSubscription(Subscription subscription)
    {
        return new SubscriptionDto(
            subscription.Id!.Value,
            subscription.Reference,
            subscription.Product?.Handle,
            subscription.Product?.Name,
            subscription.ProductPriceInCents ?? subscription.CurrentBillingAmountInCents ?? subscription.Product?.PriceInCents,
            subscription.CurrentBillingAmountInCents,
            subscription.Currency,
            subscription.State?.Value,
            subscription.CurrentPeriodEndsAt,
            subscription.NextAssessmentAt);
    }

    private static SubscriptionBillingException ProviderFailure(
        HttpStatusCode statusCode,
        string message,
        Exception innerException) =>
        new(statusCode == HttpStatusCode.NotFound ? HttpStatusCode.BadGateway : statusCode, message, innerException);
}
