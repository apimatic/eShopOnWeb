using System;
using System.Collections.Generic;
using System.Globalization;
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
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public sealed class MaxioBillingGateway : IMaxioBillingGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(25);
    private readonly MaxioAdvancedBilling.MaxioAdvancedBillingClient _client;
    private readonly ILogger<MaxioBillingGateway> _logger;
    private readonly MaxioOptions _options;

    public MaxioBillingGateway(
        MaxioAdvancedBilling.MaxioAdvancedBillingClient client,
        ILogger<MaxioBillingGateway> logger,
        IOptions<MaxioOptions> options)
    {
        _client = client;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
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
                    ct: ct),
                cancellationToken);

            var family = families
                .Select(x => x.ProductFamily)
                .SingleOrDefault(x => string.Equals(x?.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal));

            if (family?.Id is null)
            {
                throw new SubscriptionBillingException(
                    HttpStatusCode.BadGateway,
                    "The configured Maxio product family could not be resolved.");
            }

            const int perPage = 100;
            var page = 1;
            var products = new List<Product>();

            while (true)
            {
                var response = await BoundedAsync(
                    ct => _client.ProductFamilies.ListProductsForProductFamily(
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
                        perPage: perPage,
                        ct: ct),
                    cancellationToken);

                products.AddRange(response.Select(x => x.Product).Where(x => x.ArchivedAt is null));
                if (response.Count < perPage) break;
                page++;
            }

            return products.Select(ToPlan).OrderBy(x => x.PriceInCents).ToArray();
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out _))
            {
                throw ProviderFailure(HttpStatusCode.NotFound, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ProviderFailure(raw.StatusCode, ex);
            }
            throw ProviderFailure(null, ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure(ex.Error.StatusCode, ex);
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            throw BoundaryFailure(ex);
        }
    }

    public async Task<MaxioCustomer?> FindCustomerAsync(string customerReference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                ct => _client.Customers.ReadCustomerByReference(customerReference, ct: ct),
                cancellationToken);
            var customer = response.Customer;
            if (customer.Id is null)
            {
                throw new SubscriptionBillingException(HttpStatusCode.BadGateway, "Maxio returned an incomplete customer response.");
            }
            return new MaxioCustomer(customer.Id.Value, customer.Reference ?? customerReference);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure(ex.Error.StatusCode, ex);
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            throw BoundaryFailure(ex);
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        BillingUser user,
        string customerReference,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    Email = user.Email,
                    Reference = customerReference
                }
            };

            using var writeScope = WriteOnceHandler.BeginScope();
            var response = await BoundedAsync(
                ct => _client.Customers.CreateCustomer(body: request, ct: ct),
                cancellationToken);
            var customer = response.Customer;
            if (customer.Id is null)
            {
                throw new SubscriptionBillingException(HttpStatusCode.BadGateway, "Maxio returned an incomplete customer response.");
            }
            return new MaxioCustomer(customer.Id.Value, customer.Reference ?? customerReference);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            var reconciled = await FindCustomerAsync(customerReference, cancellationToken);
            if (reconciled is not null) return reconciled;

            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                throw ProviderFailure(HttpStatusCode.UnprocessableEntity, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ProviderFailure(raw.StatusCode, ex);
            }
            throw ProviderFailure(null, ex);
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            var reconciled = await FindCustomerAsync(customerReference, cancellationToken);
            return reconciled ?? throw BoundaryFailure(ex);
        }
    }

    public async Task<SubscriptionDetails?> FindSubscriptionAsync(
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                ct => _client.Subscriptions.FindSubscription(reference: subscriptionReference, ct: ct),
                cancellationToken);
            return response.Subscription is null ? null : ToSubscription(response.Subscription);
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _)) return null;
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ProviderFailure(raw.StatusCode, ex);
            }
            throw ProviderFailure(null, ex);
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            throw BoundaryFailure(ex);
        }
    }

    public async Task<SubscriptionDetails> CreateSubscriptionAsync(
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        try
        {
            var siteResponse = await BoundedAsync(
                ct => _client.Sites.ReadSite(ct: ct),
                cancellationToken);
            var paymentCollectionMethod = siteResponse.Site.RelationshipInvoicingEnabled switch
            {
                true => CollectionMethod.Remittance,
                false => CollectionMethod.Invoice,
                null => throw new SubscriptionBillingException(
                    HttpStatusCode.BadGateway,
                    "Maxio returned an incomplete site response.")
            };

            var request = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = productHandle,
                    CustomerReference = customerReference,
                    Reference = subscriptionReference,
                    PaymentCollectionMethod = paymentCollectionMethod
                }
            };

            using var writeScope = WriteOnceHandler.BeginScope();
            var response = await BoundedAsync(
                ct => _client.Subscriptions.CreateSubscription(body: request, ct: ct),
                cancellationToken);
            return response.Subscription is null
                ? throw new SubscriptionBillingException(HttpStatusCode.BadGateway, "Maxio returned an incomplete subscription response.")
                : ToSubscription(response.Subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var validation))
            {
                _logger.LogWarning(
                    "Maxio rejected subscription creation with validation errors: {ValidationErrors}",
                    string.Join(" | ", validation.Errors));
                throw ProviderFailure(HttpStatusCode.UnprocessableEntity, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ProviderFailure(raw.StatusCode, ex);
            }
            throw ProviderFailure(null, ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure(ex.Error.StatusCode, ex);
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            throw BoundaryFailure(ex);
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                ct => _client.Customers.ListCustomerSubscriptions(customerId, ct: ct),
                cancellationToken);
            return response
                .Where(x => x.Subscription is not null)
                .Select(x => ToSubscription(x.Subscription!))
                .ToArray();
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure(ex.Error.StatusCode, ex);
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            throw BoundaryFailure(ex);
        }
    }

    private static SubscriptionPlan ToPlan(Product product)
    {
        if (product.Handle is null || product.Name is null || product.PriceInCents is null ||
            product.Interval is null || product.IntervalUnit is null)
        {
            throw new SubscriptionBillingException(HttpStatusCode.BadGateway, "Maxio returned an incomplete product response.");
        }

        return new SubscriptionPlan(
            product.Handle,
            product.Name,
            product.Description,
            product.PriceInCents.Value,
            product.Interval.Value,
            product.IntervalUnit.Value);
    }

    private static SubscriptionDetails ToSubscription(Subscription subscription)
    {
        if (subscription.Id is null || subscription.Product?.Handle is null || subscription.Product.Name is null)
        {
            throw new SubscriptionBillingException(HttpStatusCode.BadGateway, "Maxio returned an incomplete subscription response.");
        }

        var price = subscription.ProductPriceInCents ?? subscription.Product.PriceInCents;
        if (price is null)
        {
            throw new SubscriptionBillingException(HttpStatusCode.BadGateway, "Maxio returned a subscription without a price.");
        }

        return new SubscriptionDetails(
            subscription.Id.Value,
            subscription.Product.Handle,
            subscription.Product.Name,
            price.Value,
            subscription.State?.Value ?? "unknown",
            subscription.NextAssessmentAt,
            subscription.Currency);
    }

    private static async Task<T> BoundedAsync<T>(
        Func<CancellationToken, Task<T>> call,
        CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(CallBudget);
        return await call(budget.Token);
    }

    private static bool IsBoundaryException(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or JsonException or
            Polly.Timeout.TimeoutRejectedException or MaxioWriteReplayBlockedException;

    private static SubscriptionBillingException BoundaryFailure(Exception exception) => exception switch
    {
        Polly.Timeout.TimeoutRejectedException => new SubscriptionBillingException(HttpStatusCode.GatewayTimeout, "Maxio did not respond within the allowed time.", exception),
        TaskCanceledException => new SubscriptionBillingException(HttpStatusCode.GatewayTimeout, "Maxio did not respond within the allowed time.", exception),
        JsonException => new SubscriptionBillingException(HttpStatusCode.BadGateway, "Maxio returned a response that could not be processed.", exception),
        _ => new SubscriptionBillingException(HttpStatusCode.ServiceUnavailable, "Maxio is temporarily unavailable.", exception)
    };

    private static SubscriptionBillingException ProviderFailure(HttpStatusCode? providerStatus, Exception exception)
    {
        var status = providerStatus is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError
            ? providerStatus.Value
            : HttpStatusCode.BadGateway;
        return new SubscriptionBillingException(status, "Maxio rejected the billing request.", exception);
    }
}
