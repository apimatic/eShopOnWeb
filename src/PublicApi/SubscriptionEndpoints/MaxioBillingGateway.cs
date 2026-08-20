using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal sealed class MaxioBillingGateway : IMaxioBillingGateway
{
    internal const string ReadClientName = "Maxio.Read";
    internal const string WriteClientName = "Maxio.Write";
    private static readonly TimeSpan TotalCallBudget = TimeSpan.FromSeconds(30);
    private const int PageSize = 20;

    private readonly MaxioAdvancedBillingClient _readClient;
    private readonly MaxioAdvancedBillingClient _writeClient;
    private readonly MaxioOptions _options;

    public MaxioBillingGateway(IHttpClientFactory httpClientFactory, IOptions<MaxioOptions> options)
    {
        _options = options.Value;
        _readClient = CreateClient(httpClientFactory.CreateClient(ReadClientName), _options);
        _writeClient = CreateClient(httpClientFactory.CreateClient(WriteClientName), _options);
    }

    public Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken) =>
        ExecuteReadAsync(async ct =>
        {
            IReadOnlyList<ProductFamilyResponse> families;
            try
            {
                families = await _readClient.ProductFamilies.ListProductFamilies(
                    dateField: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    ct: ct);
            }
            catch (SdkException<RawError> ex)
            {
                throw DependencyFromRaw(ex.Error, ex);
            }

            var matches = families
                .Select(x => x.ProductFamily)
                .Where(x => x is not null && x.ArchivedAt is null &&
                            string.Equals(x.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
                .ToList();

            if (matches.Count != 1 || matches[0]!.Id is null)
            {
                throw new MaxioDependencyException("The configured Maxio product family could not be resolved uniquely.");
            }

            var familyId = matches[0]!.Id!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var plans = new Dictionary<string, SubscriptionPlanDto>(StringComparer.Ordinal);

            for (var page = 1; ; page++)
            {
                IReadOnlyList<ProductResponse> response;
                try
                {
                    response = await _readClient.ProductFamilies.ListProductsForProductFamily(
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
                catch (SdkException<ListProductsForProductFamilyError> ex)
                {
                    if (ex.Error.TryGetString(out _))
                    {
                        throw new MaxioDependencyException("The configured Maxio product family is unavailable.", ex);
                    }

                    if (ex.Error.TryGetRawError(out var raw))
                    {
                        throw DependencyFromRaw(raw, ex);
                    }

                    throw new MaxioDependencyException("Maxio rejected the product catalog request.", ex);
                }

                foreach (var item in response)
                {
                    var product = item.Product;
                    if (product.ArchivedAt is not null || string.IsNullOrWhiteSpace(product.Handle))
                    {
                        continue;
                    }

                    plans[product.Handle] = new SubscriptionPlanDto(
                        product.Handle,
                        product.Name ?? product.Handle,
                        product.Description,
                        product.PriceInCents,
                        product.Interval,
                        product.IntervalUnit?.Value);
                }

                if (response.Count < PageSize)
                {
                    break;
                }
            }

            return (IReadOnlyList<SubscriptionPlanDto>)plans.Values.OrderBy(x => x.PriceInCents).ToList();
        }, cancellationToken);

    public Task<MaxioProduct?> FindProductAsync(string productHandle, CancellationToken cancellationToken) =>
        ExecuteReadAsync(async ct =>
        {
            try
            {
                var response = await _readClient.Products.ReadProductByHandle(productHandle, ct: ct);
                var product = response.Product;
                return new MaxioProduct(
                    product.Id ?? throw MalformedResponse(),
                    product.Handle ?? throw MalformedResponse(),
                    product.Name ?? product.Handle ?? throw MalformedResponse(),
                    product.Description,
                    product.PriceInCents,
                    product.Interval,
                    product.IntervalUnit?.Value,
                    product.ProductFamily?.Handle ?? string.Empty,
                    product.ArchivedAt is not null);
            }
            catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            catch (SdkException<RawError> ex)
            {
                throw DependencyFromRaw(ex.Error, ex);
            }
        }, cancellationToken);

    public Task<MaxioCustomer?> FindCustomerAsync(string customerReference, CancellationToken cancellationToken) =>
        ExecuteReadAsync(async ct =>
        {
            try
            {
                var response = await _readClient.Customers.ReadCustomerByReference(customerReference, ct: ct);
                return new MaxioCustomer(
                    response.Customer.Id ?? throw MalformedResponse(),
                    response.Customer.Reference ?? customerReference);
            }
            catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            catch (SdkException<RawError> ex)
            {
                throw DependencyFromRaw(ex.Error, ex);
            }
        }, cancellationToken);

    public Task<MaxioCustomer> CreateCustomerAsync(
        BillingUser user,
        string customerReference,
        CancellationToken cancellationToken) =>
        ExecuteReadAsync(async ct =>
        {
            try
            {
                var response = await _readClient.Customers.CreateCustomer(
                    new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = user.FirstName,
                            LastName = user.LastName,
                            Email = user.Email,
                            Reference = customerReference
                        }
                    },
                    ct: ct);

                return new MaxioCustomer(
                    response.Customer.Id ?? throw MalformedResponse(),
                    response.Customer.Reference ?? customerReference);
            }
            catch (SdkException<CreateCustomerError> ex)
            {
                if (ex.Error.TryGetCustomerErrorResponse1(out _))
                {
                    throw new MaxioValidationException("Maxio rejected the customer request.", ex);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw DependencyFromRaw(raw, ex);
                }

                throw new MaxioDependencyException("Maxio rejected the customer request.", ex);
            }
        }, cancellationToken);

    public Task<NoCardPaymentCollectionMethod> ResolveNoCardPaymentCollectionMethodAsync(
        CancellationToken cancellationToken) =>
        ExecuteReadAsync(async ct =>
        {
            try
            {
                var response = await _readClient.Sites.ReadSite(ct: ct);
                return response.Site.RelationshipInvoicingEnabled == true
                    ? NoCardPaymentCollectionMethod.Remittance
                    : NoCardPaymentCollectionMethod.Invoice;
            }
            catch (SdkException<RawError> ex)
            {
                throw DependencyFromRaw(ex.Error, ex);
            }
        }, cancellationToken);

    public Task<SubscriptionDto?> FindSubscriptionAsync(
        string subscriptionReference,
        CancellationToken cancellationToken) =>
        ExecuteReadAsync(async ct =>
        {
            try
            {
                var response = await _readClient.Subscriptions.FindSubscription(subscriptionReference, ct: ct);
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
                    throw DependencyFromRaw(raw, ex);
                }

                throw new MaxioDependencyException("Maxio could not resolve the subscription.", ex);
            }
        }, cancellationToken);

    public Task<SubscriptionDto> CreateSubscriptionAsync(
        string productHandle,
        string customerReference,
        string subscriptionReference,
        NoCardPaymentCollectionMethod paymentCollectionMethod,
        CancellationToken cancellationToken) =>
        ExecuteWriteAsync(async ct =>
        {
            try
            {
                var response = await _writeClient.Subscriptions.CreateSubscription(
                    new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = productHandle,
                            CustomerReference = customerReference,
                            Reference = subscriptionReference,
                            PaymentCollectionMethod = paymentCollectionMethod switch
                            {
                                NoCardPaymentCollectionMethod.Remittance => CollectionMethod.Remittance,
                                NoCardPaymentCollectionMethod.Invoice => CollectionMethod.Invoice,
                                _ => throw new ArgumentOutOfRangeException(nameof(paymentCollectionMethod))
                            }
                        }
                    },
                    ct: ct);

                return response.Subscription is null
                    ? throw MalformedResponse()
                    : MapSubscription(response.Subscription);
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                if (ex.Error.TryGetErrorListResponse1(out _))
                {
                    throw new MaxioValidationException("Maxio rejected the subscription request.", ex);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw DependencyFromRaw(raw, ex);
                }

                throw new MaxioDependencyException("Maxio rejected the subscription request.", ex);
            }
        }, cancellationToken);

    public Task<IReadOnlyList<SubscriptionDto>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken) =>
        ExecuteReadAsync(async ct =>
        {
            try
            {
                var response = await _readClient.Customers.ListCustomerSubscriptions(customerId, ct: ct);
                return (IReadOnlyList<SubscriptionDto>)response
                    .Where(x => x.Subscription?.Product?.ProductFamily?.Handle == _options.ProductFamilyHandle)
                    .Select(x => MapSubscription(x.Subscription!))
                    .ToList();
            }
            catch (SdkException<RawError> ex)
            {
                throw DependencyFromRaw(ex.Error, ex);
            }
        }, cancellationToken);

    private static MaxioAdvancedBillingClient CreateClient(HttpClient httpClient, MaxioOptions configuration)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(10) },
            BasicAuth = new BasicAuthCredentials
            {
                Username = configuration.ApiKey,
                Password = "x"
            }
        };

        if (configuration.BaseUrl is not null)
        {
            options.Server.Production.Us.BaseUrl = configuration.BaseUrl;
        }
        else
        {
            options.Server.Production.Us.Site = configuration.Subdomain;
        }

        return new MaxioAdvancedBillingClient(httpClient, options);
    }

    private static SubscriptionDto MapSubscription(Subscription subscription)
    {
        var product = subscription.Product ?? throw MalformedResponse();
        return new SubscriptionDto(
            subscription.Id ?? throw MalformedResponse(),
            product.Handle ?? throw MalformedResponse(),
            product.Name ?? product.Handle ?? throw MalformedResponse(),
            subscription.ProductPriceInCents ?? product.PriceInCents,
            subscription.Currency,
            product.Interval,
            product.IntervalUnit?.Value,
            subscription.State?.Value,
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);
    }

    private static MaxioDependencyException MalformedResponse() =>
        new("Maxio returned a response that could not be processed.");

    private static MaxioDependencyException DependencyFromRaw(RawError raw, Exception innerException)
    {
        var message = raw.StatusCode == HttpStatusCode.TooManyRequests
            ? "Maxio is temporarily throttling requests."
            : "Maxio could not complete the billing request.";
        return new MaxioDependencyException(message, innerException);
    }

    private static async Task<T> ExecuteReadAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        using var scope = MaxioCallScope.Begin(enforceSingleSend: false);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TotalCallBudget);

        try
        {
            return await operation(budget.Token);
        }
        catch (JsonException ex)
        {
            throw FromJsonFailure(scope.State.LastStatusCode, isWrite: false, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioDependencyException("Maxio is unavailable.", ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MaxioDependencyException("The Maxio request timed out.", ex);
        }
    }

    private static async Task<T> ExecuteWriteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        using var scope = MaxioCallScope.Begin(enforceSingleSend: true);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TotalCallBudget);

        try
        {
            return await operation(budget.Token);
        }
        catch (DuplicateSendBlockedException ex)
        {
            throw new MaxioUnknownOutcomeException("The subscription create outcome is unknown.", ex);
        }
        catch (JsonException ex)
        {
            throw FromJsonFailure(scope.State.LastStatusCode, isWrite: true, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            throw new MaxioUnknownOutcomeException("The subscription create outcome is unknown.", ex);
        }
    }

    private static Exception FromJsonFailure(HttpStatusCode? statusCode, bool isWrite, JsonException exception)
    {
        if (statusCode is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError)
        {
            return new MaxioValidationException("Maxio rejected a request that could not be processed.", exception);
        }

        if (isWrite && statusCode is >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices)
        {
            return new MaxioUnknownOutcomeException("The subscription create outcome is unknown.", exception);
        }

        return new MaxioDependencyException("Maxio returned a response that could not be processed.", exception);
    }
}
