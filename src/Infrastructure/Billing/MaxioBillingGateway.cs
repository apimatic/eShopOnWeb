using System;
using System.Collections.Generic;
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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AppBillingCustomer = Microsoft.eShopWeb.ApplicationCore.Models.BillingCustomer;
using AppSubscription = Microsoft.eShopWeb.ApplicationCore.Models.BillingSubscription;
using MaxioSubscription = MaxioAdvancedBilling.Models.Subscription;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioBillingGateway : ISubscriptionBillingGateway
{
    private const int PageSize = 100;
    private static readonly TimeSpan TotalCallBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReconciliationBudget = TimeSpan.FromSeconds(10);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _settings;
    private readonly ILogger<MaxioBillingGateway> _logger;

    public MaxioBillingGateway(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> settings,
        ILogger<MaxioBillingGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await BoundedAsync(token => _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: token), cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure("Maxio could not return subscription plan families.", ex.Error, ex);
        }
        catch (Exception ex) when (IsReadFailure(ex, cancellationToken))
        {
            throw ReadTransportFailure(ex, cancellationToken);
        }

        var family = families
            .Select(response => response.ProductFamily)
            .SingleOrDefault(candidate => candidate is not null &&
                string.Equals(candidate.Handle, _settings.ProductFamilyHandle, StringComparison.Ordinal));
        if (family?.Id is not int familyId)
        {
            throw new BillingProviderException("The configured Maxio product family was not found.", 404);
        }

        var plans = new List<SubscriptionPlan>();
        for (var page = 1; ; page++)
        {
            IReadOnlyList<ProductResponse> products;
            try
            {
                products = await BoundedAsync(token => _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyId.ToString(System.Globalization.CultureInfo.InvariantCulture),
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
                    ct: token), cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out _))
                {
                    throw new BillingProviderException("The configured Maxio product family was not found.", 404, innerException: ex);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw ProviderFailure("Maxio could not return subscription plans.", raw, ex);
                }

                throw new BillingProviderException("Maxio could not return subscription plans.", innerException: ex);
            }
            catch (Exception ex) when (IsReadFailure(ex, cancellationToken))
            {
                throw ReadTransportFailure(ex, cancellationToken);
            }

            foreach (var productResponse in products)
            {
                var product = productResponse.Product;
                if (product.ArchivedAt is not null)
                {
                    continue;
                }

                if (product.Handle is null || product.Name is null || product.PriceInCents is not long price ||
                    product.Interval is not int interval || product.IntervalUnit is null)
                {
                    throw MalformedResponse();
                }

                plans.Add(new SubscriptionPlan(
                    product.Handle,
                    product.Name,
                    price,
                    interval,
                    product.IntervalUnit.Value));
            }

            if (products.Count < PageSize)
            {
                return plans;
            }
        }
    }

    public async Task<AppBillingCustomer?> FindCustomerAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                token => _client.Customers.ReadCustomerByReference(reference, ct: token),
                cancellationToken);
            return MapCustomer(response);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure("Maxio could not read the customer.", ex.Error, ex);
        }
        catch (Exception ex) when (IsReadFailure(ex, cancellationToken))
        {
            throw ReadTransportFailure(ex, cancellationToken);
        }
    }

    public async Task<AppBillingCustomer> EnsureCustomerAsync(
        BillingUser user,
        string reference,
        CancellationToken cancellationToken)
    {
        var existing = await FindCustomerAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                Reference = reference
            }
        };

        try
        {
            using var writeScope = MaxioWriteOnceScope.Begin();
            var response = await BoundedAsync(
                token => _client.Customers.CreateCustomer(body: request, ct: token),
                cancellationToken);
            return MapCustomer(response);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                var winner = await ReconcileCustomerAsync(reference);
                if (winner is not null)
                {
                    return winner;
                }

                throw new BillingProviderException("Maxio rejected the customer profile.", 422, innerException: ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ProviderFailure("Maxio could not create the customer.", raw, ex);
            }

            throw new BillingProviderException("Maxio could not create the customer.", innerException: ex);
        }
        catch (Exception ex) when (IsAmbiguousWriteFailure(ex))
        {
            var winner = await ReconcileCustomerAsync(reference);
            if (winner is not null)
            {
                return winner;
            }

            throw new BillingProviderException(
                "The Maxio customer request could not be confirmed.",
                outcomeMayBeUnknown: true,
                innerException: ex);
        }
    }

    public async Task<AppSubscription?> FindSubscriptionAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                token => _client.Subscriptions.FindSubscription(reference: reference, ct: token),
                cancellationToken);
            return response.Subscription is null ? throw MalformedResponse() : MapSubscription(response.Subscription);
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

                throw ProviderFailure("Maxio could not find the subscription.", raw, ex);
            }

            throw new BillingProviderException("Maxio could not find the subscription.", innerException: ex);
        }
        catch (Exception ex) when (IsReadFailure(ex, cancellationToken))
        {
            throw ReadTransportFailure(ex, cancellationToken);
        }
    }

    public async Task<AppSubscription> CreateSubscriptionAsync(
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
                PaymentCollectionMethod = CollectionMethod.Remittance
            }
        };

        try
        {
            using var writeScope = MaxioWriteOnceScope.Begin();
            var response = await BoundedAsync(
                token => _client.Subscriptions.CreateSubscription(body: request, ct: token),
                cancellationToken);
            return response.Subscription is null ? throw MalformedResponse() : MapSubscription(response.Subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var validationError))
            {
                _logger.LogWarning(
                    "Maxio rejected the subscription request with validation errors: {ValidationErrors}",
                    FormatValidationErrors(validationError.Errors));
                throw new BillingProviderException("Maxio rejected the subscription request.", 422, innerException: ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ProviderFailure("Maxio could not create the subscription.", raw, ex);
            }

            throw new BillingProviderException("Maxio could not create the subscription.", innerException: ex);
        }
        catch (Exception ex) when (IsAmbiguousWriteFailure(ex))
        {
            var reconciled = await ReconcileSubscriptionAsync(subscriptionReference);
            if (reconciled is not null)
            {
                return reconciled;
            }

            throw new BillingProviderException(
                "The Maxio subscription request could not be confirmed.",
                outcomeMayBeUnknown: true,
                innerException: ex);
        }
    }

    public async Task<AppSubscription> ReadSubscriptionAsync(
        int subscriptionId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                token => _client.Subscriptions.ReadSubscription(subscriptionId, include: null, ct: token),
                cancellationToken);
            return response.Subscription is null ? throw MalformedResponse() : MapSubscription(response.Subscription);
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure("Maxio could not read the subscription.", ex.Error, ex);
        }
        catch (Exception ex) when (IsReadFailure(ex, cancellationToken))
        {
            throw ReadTransportFailure(ex, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<AppSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var responses = await BoundedAsync(
                token => _client.Customers.ListCustomerSubscriptions(customerId, ct: token),
                cancellationToken);
            return responses.Select(response => response.Subscription is null
                    ? throw MalformedResponse()
                    : MapSubscription(response.Subscription))
                .ToArray();
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure("Maxio could not return customer subscriptions.", ex.Error, ex);
        }
        catch (Exception ex) when (IsReadFailure(ex, cancellationToken))
        {
            throw ReadTransportFailure(ex, cancellationToken);
        }
    }

    private async Task<AppBillingCustomer?> ReconcileCustomerAsync(string reference)
    {
        using var timeout = new CancellationTokenSource(ReconciliationBudget);
        try
        {
            return await FindCustomerAsync(reference, timeout.Token);
        }
        catch (Exception ex) when (ex is BillingProviderException or OperationCanceledException)
        {
            _logger.LogWarning("Could not reconcile Maxio customer reference after an ambiguous write.");
            return null;
        }
    }

    private async Task<AppSubscription?> ReconcileSubscriptionAsync(string reference)
    {
        using var timeout = new CancellationTokenSource(ReconciliationBudget);
        try
        {
            return await FindSubscriptionAsync(reference, timeout.Token);
        }
        catch (Exception ex) when (ex is BillingProviderException or OperationCanceledException)
        {
            _logger.LogWarning("Could not reconcile Maxio subscription reference after an ambiguous write.");
            return null;
        }
    }

    private static AppBillingCustomer MapCustomer(CustomerResponse response)
    {
        var customer = response.Customer;
        return customer.Id is int id && customer.Reference is string reference
            ? new AppBillingCustomer(id, reference)
            : throw MalformedResponse();
    }

    private static AppSubscription MapSubscription(MaxioSubscription subscription)
    {
        var product = subscription.Product;
        if (subscription.Id is not int id || subscription.Reference is not string reference ||
            subscription.ProductPriceInCents is not long price || subscription.State is null ||
            product?.Handle is not string productHandle || product.Name is not string productName ||
            product.Interval is not int interval || product.IntervalUnit is null)
        {
            throw MalformedResponse();
        }

        return new AppSubscription(
            id,
            reference,
            productHandle,
            productName,
            price,
            interval,
            product.IntervalUnit.Value,
            subscription.State.Value,
            subscription.NextAssessmentAt);
    }

    private static async Task<T> BoundedAsync<T>(
        Func<CancellationToken, Task<T>> call,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TotalCallBudget);
        return await call(timeout.Token);
    }

    private BillingProviderException ProviderFailure(string message, RawError raw, Exception innerException)
    {
        var statusCode = (int)raw.StatusCode;
        _logger.LogWarning("Maxio returned HTTP {StatusCode}.", statusCode);
        return new BillingProviderException(message, statusCode, innerException: innerException);
    }

    private static string FormatValidationErrors(IReadOnlyList<string> errors)
    {
        const int maxLogLength = 2_000;
        var message = errors.Count == 0
            ? "(empty error list)"
            : string.Join(" | ", errors.Select(error => error.Replace('\r', ' ').Replace('\n', ' ').Trim()));

        return message.Length <= maxLogLength ? message : message[..maxLogLength];
    }

    private static BillingProviderException ReadTransportFailure(
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            throw exception;
        }

        return exception is JsonException
            ? MalformedResponse(exception)
            : new BillingProviderException("Maxio is temporarily unavailable.", 504, innerException: exception);
    }

    private static bool IsReadFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or JsonException or TaskCanceledException or OperationCanceledException;

    private static bool IsAmbiguousWriteFailure(Exception exception) =>
        exception is HttpRequestException or JsonException or TaskCanceledException or OperationCanceledException or
            MaxioWriteAlreadyAttemptedException;

    private static BillingProviderException MalformedResponse(Exception? innerException = null) =>
        new("Maxio returned a response that could not be processed.", 502, innerException: innerException);
}
