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
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioBillingGateway : ISubscriptionBillingGateway
{
    private const int PageSize = 100;
    private static readonly TimeSpan TotalCallBudget = TimeSpan.FromSeconds(25);
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _settings;

    public MaxioBillingGateway(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        int familyId;
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
                false,
                cancellationToken);

            var family = families
                .Select(response => response.ProductFamily)
                .SingleOrDefault(candidate =>
                    candidate?.Handle == _settings.ProductFamilyHandle
                    && candidate.ArchivedAt is null);
            familyId = family?.Id
                ?? throw new BillingRequestException("The configured subscription catalog was not found.", 404);
        }
        catch (SdkException<RawError> exception)
        {
            throw FromRawError(exception.Error, "Maxio could not return the product families.", false, exception);
        }

        var plans = new List<SubscriptionPlan>();
        for (var page = 1; page <= 100; page++)
        {
            IReadOnlyList<ProductResponse> responses;
            try
            {
                responses = await BoundedAsync(
                    ct => _client.ProductFamilies.ListProductsForProductFamily(
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
                        perPage: PageSize,
                        ct: ct),
                    false,
                    cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> exception)
            {
                if (exception.Error.TryGetString(out _))
                {
                    throw new BillingRequestException("The configured subscription catalog was not found.", 404);
                }

                if (exception.Error.TryGetRawError(out var raw))
                {
                    throw FromRawError(raw, "Maxio could not return the subscription plans.", false, exception);
                }

                throw new BillingProviderException("Maxio could not return the subscription plans.", innerException: exception);
            }

            plans.AddRange(responses
                .Select(response => response.Product)
                .Where(product => product.ArchivedAt is null)
                .Select(MapPlan));

            if (responses.Count < PageSize)
            {
                return plans;
            }
        }

        throw new BillingProviderException("Maxio returned too many subscription-plan pages to process safely.");
    }

    public async Task<SubscriptionPlan?> FindPlanAsync(
        string productHandle,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                ct => _client.Products.ReadProductByHandle(productHandle, ct: ct),
                false,
                cancellationToken);
            var product = response.Product;
            if (product.ArchivedAt is not null
                || product.ProductFamily?.Handle != _settings.ProductFamilyHandle)
            {
                return null;
            }

            return MapPlan(product);
        }
        catch (SdkException<RawError> exception) when (exception.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> exception)
        {
            throw FromRawError(exception.Error, "Maxio could not validate the subscription plan.", false, exception);
        }
    }

    public async Task<BillingCustomer?> FindCustomerAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                ct => _client.Customers.ReadCustomerByReference(reference, ct: ct),
                false,
                cancellationToken);
            return MapCustomer(response.Customer);
        }
        catch (SdkException<RawError> exception) when (exception.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> exception)
        {
            throw FromRawError(exception.Error, "Maxio could not look up the customer.", false, exception);
        }
    }

    public async Task<BillingCustomer> CreateCustomerAsync(
        BillingUser user,
        string reference,
        CancellationToken cancellationToken)
    {
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
            using var writeAttempt = MaxioWriteAttemptScope.Begin();
            var response = await BoundedAsync(
                ct => _client.Customers.CreateCustomer(request, ct: ct),
                true,
                cancellationToken);
            return MapCustomer(response.Customer);
        }
        catch (SdkException<CreateCustomerError> exception)
        {
            if (exception.Error.TryGetCustomerErrorResponse1(out _))
            {
                throw new BillingProviderException(
                    "Maxio rejected the customer profile.",
                    (int)HttpStatusCode.UnprocessableEntity,
                    innerException: exception);
            }

            if (exception.Error.TryGetRawError(out var raw))
            {
                throw FromRawError(raw, "Maxio could not create the customer.", true, exception);
            }

            throw new BillingProviderException(
                "Maxio could not create the customer.",
                outcomeMayBeUnknown: true,
                innerException: exception);
        }
    }

    public async Task<CustomerSubscription?> FindSubscriptionAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                ct => _client.Subscriptions.FindSubscription(reference, ct: ct),
                false,
                cancellationToken);
            return MapSubscription(response.Subscription);
        }
        catch (SdkException<FindSubscriptionError> exception)
        {
            if (exception.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (exception.Error.TryGetRawError(out var raw))
            {
                throw FromRawError(raw, "Maxio could not look up the subscription.", false, exception);
            }

            throw new BillingProviderException("Maxio could not look up the subscription.", innerException: exception);
        }
    }

    public async Task<CustomerSubscription> CreateSubscriptionAsync(
        string productHandle,
        int maxioCustomerId,
        string reference,
        CancellationToken cancellationToken)
    {
        var request = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = maxioCustomerId,
                PaymentCollectionMethod = CollectionMethod.Remittance,
                Reference = reference
            }
        };

        try
        {
            using var writeAttempt = MaxioWriteAttemptScope.Begin();
            var response = await BoundedAsync(
                ct => _client.Subscriptions.CreateSubscription(request, ct: ct),
                true,
                cancellationToken);
            return MapSubscription(response.Subscription);
        }
        catch (SdkException<CreateSubscriptionError> exception)
        {
            if (exception.Error.TryGetErrorListResponse1(out _))
            {
                throw new BillingProviderException(
                    "Maxio rejected the subscription request.",
                    (int)HttpStatusCode.UnprocessableEntity,
                    innerException: exception);
            }

            if (exception.Error.TryGetRawError(out var raw))
            {
                throw FromRawError(raw, "Maxio could not create the subscription.", true, exception);
            }

            throw new BillingProviderException(
                "Maxio could not create the subscription.",
                outcomeMayBeUnknown: true,
                innerException: exception);
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(
        int maxioCustomerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var responses = await BoundedAsync(
                ct => _client.Customers.ListCustomerSubscriptions(maxioCustomerId, ct: ct),
                false,
                cancellationToken);
            return responses.Select(response => MapSubscription(response.Subscription)).ToList();
        }
        catch (SdkException<RawError> exception)
        {
            throw FromRawError(exception.Error, "Maxio could not return the customer's subscriptions.", false, exception);
        }
    }

    private static SubscriptionPlan MapPlan(Product product)
    {
        if (product.Id is not int id
            || string.IsNullOrWhiteSpace(product.Name)
            || string.IsNullOrWhiteSpace(product.Handle)
            || product.PriceInCents is not long priceInCents
            || product.Interval is not int interval
            || product.IntervalUnit is null)
        {
            throw new BillingProviderException("Maxio returned an incomplete subscription plan.");
        }

        return new SubscriptionPlan(
            id,
            product.Name,
            product.Handle,
            product.Description,
            priceInCents,
            interval,
            product.IntervalUnit.Value);
    }

    private static BillingCustomer MapCustomer(Customer customer)
    {
        if (customer.Id is not int id || string.IsNullOrWhiteSpace(customer.Reference))
        {
            throw new BillingProviderException("Maxio returned an incomplete customer record.");
        }

        return new BillingCustomer(id, customer.Reference);
    }

    private static CustomerSubscription MapSubscription(Subscription? subscription)
    {
        if (subscription?.Id is not int id
            || subscription.Product is null
            || string.IsNullOrWhiteSpace(subscription.Product.Name)
            || string.IsNullOrWhiteSpace(subscription.Product.Handle)
            || subscription.ProductPriceInCents is not long priceInCents
            || subscription.State is null)
        {
            throw new BillingProviderException("Maxio returned an incomplete subscription record.");
        }

        return new CustomerSubscription(
            id,
            subscription.Reference,
            subscription.Product.Name,
            subscription.Product.Handle,
            priceInCents,
            subscription.State.Value,
            subscription.NextAssessmentAt);
    }

    private static BillingProviderException FromRawError(
        RawError raw,
        string safeMessage,
        bool isWrite,
        Exception exception)
    {
        return new BillingProviderException(
            safeMessage,
            (int)raw.StatusCode,
            isWrite && (int)raw.StatusCode >= 500,
            exception);
    }

    private static async Task<T> BoundedAsync<T>(
        Func<CancellationToken, Task<T>> call,
        bool isWrite,
        CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TotalCallBudget);

        try
        {
            return await call(budget.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MaxioRepeatedWriteBlockedException exception)
        {
            throw new BillingProviderException(
                "The Maxio write outcome is being reconciled.",
                outcomeMayBeUnknown: true,
                innerException: exception);
        }
        catch (TaskCanceledException exception)
        {
            throw new BillingProviderException(
                "Maxio did not respond before the request deadline.",
                outcomeMayBeUnknown: isWrite,
                innerException: exception);
        }
        catch (HttpRequestException exception)
        {
            throw new BillingProviderException(
                "Maxio is temporarily unreachable.",
                outcomeMayBeUnknown: isWrite,
                innerException: exception);
        }
        catch (JsonException exception)
        {
            throw new BillingProviderException(
                "Maxio returned a response that could not be processed.",
                outcomeMayBeUnknown: isWrite,
                innerException: exception);
        }
    }
}
