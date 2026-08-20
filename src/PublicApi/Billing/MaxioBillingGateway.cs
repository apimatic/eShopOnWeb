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
using MaxioAdvancedBilling.Models.AnyOf;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public sealed class MaxioBillingGateway : IMaxioBillingGateway
{
    private const int PageSize = 100;
    private const int MaximumPages = 20;
    private const string MeteredComponentHandle = "api-call";
    private static readonly TimeSpan TotalCallBudget = TimeSpan.FromSeconds(30);
    private static readonly HashSet<string> SupportedProducts = new(StringComparer.Ordinal)
    {
        "eshop-pro",
        "basic-plan"
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly IMaxioResponseContext _responseContext;
    private readonly IMaxioWriteGuard _writeGuard;

    public MaxioBillingGateway(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        IMaxioResponseContext responseContext,
        IMaxioWriteGuard writeGuard)
    {
        _client = client;
        _options = options.Value;
        _responseContext = responseContext;
        _writeGuard = writeGuard;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken) =>
        WithBudgetAsync(ListPlansCoreAsync, cancellationToken);

    public Task<UserSubscription?> FindSubscriptionAsync(
        string reference,
        CancellationToken cancellationToken) =>
        WithBudgetAsync(ct => FindSubscriptionCoreAsync(reference, ct), cancellationToken);

    public Task<UserSubscription> CreateSubscriptionAsync(
        BillingCustomer customer,
        string productHandle,
        string subscriptionReference,
        CancellationToken cancellationToken) =>
        WithBudgetAsync(
            ct => CreateSubscriptionCoreAsync(customer, productHandle, subscriptionReference, ct),
            cancellationToken);

    public Task<IReadOnlyList<UserSubscription>> ListSubscriptionsAsync(
        string customerReference,
        CancellationToken cancellationToken) =>
        WithBudgetAsync(ct => ListSubscriptionsCoreAsync(customerReference, ct), cancellationToken);

    private async Task<IReadOnlyList<SubscriptionPlan>> ListPlansCoreAsync(CancellationToken ct)
    {
        var plans = new List<SubscriptionPlan>();
        for (var page = 1; page <= MaximumPages; page++)
        {
            IReadOnlyList<ProductResponse> response;
            try
            {
                response = await CallAsync(
                    callCt => _client.ProductFamilies.ListProductsForProductFamily(
                        productFamilyId: $"handle:{_options.ProductFamilyHandle}",
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
                        ct: callCt),
                    ct);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                throw MapListProductsError(ex);
            }

            foreach (var envelope in response)
            {
                var product = envelope.Product;
                if (product.ArchivedAt is not null ||
                    product.Handle is null ||
                    !SupportedProducts.Contains(product.Handle) ||
                    product.Name is null ||
                    product.PriceInCents is null ||
                    product.Interval is null ||
                    product.IntervalUnit is null ||
                    !string.Equals(product.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
                {
                    continue;
                }

                plans.Add(new SubscriptionPlan(
                    product.Handle,
                    product.Name,
                    product.PriceInCents.Value,
                    product.Interval.Value,
                    product.IntervalUnit.Value));
            }

            if (response.Count < PageSize)
            {
                return plans.OrderBy(plan => plan.PriceInCents).ToArray();
            }
        }

        throw new MaxioProviderException(
            HttpStatusCode.BadGateway,
            "The billing catalog exceeded the supported pagination limit.");
    }

    private async Task<UserSubscription> CreateSubscriptionCoreAsync(
        BillingCustomer customer,
        string productHandle,
        string subscriptionReference,
        CancellationToken ct)
    {
        if (!SupportedProducts.Contains(productHandle))
        {
            throw new BillingValidationException("The selected subscription plan is not supported.");
        }

        var product = await ReadAndValidateProductAsync(productHandle, ct);
        var componentId = await ResolveMeteredComponentAsync(ct);
        var paymentCollectionMethod = await ResolveNonAutomaticCollectionMethodAsync(ct);
        await EnsureCustomerAsync(customer, ct);

        var existing = await FindSubscriptionCoreAsync(subscriptionReference, ct);
        if (existing is not null)
        {
            return existing;
        }

        var request = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = product.Handle,
                CustomerReference = customer.MaxioReference,
                Reference = subscriptionReference,
                PaymentCollectionMethod = paymentCollectionMethod,
                Components =
                [
                    new CreateSubscriptionComponent
                    {
                        ComponentId = ComponentId1.Int(componentId),
                        UnitBalance = 0
                    }
                ]
            }
        };

        try
        {
            using var writeScope = _writeGuard.BeginScope();
            var response = await CallAsync(
                callCt => _client.Subscriptions.CreateSubscription(request, ct: callCt),
                ct);
            return MapSubscription(response.Subscription, subscriptionReference);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            var reconciled = await FindSubscriptionCoreAsync(subscriptionReference, ct);
            if (reconciled is not null)
            {
                return reconciled;
            }

            throw MapCreateSubscriptionError(ex);
        }
        catch (Exception ex) when (ex is MaxioWriteReplayBlockedException or MaxioProviderException)
        {
            var reconciled = await FindSubscriptionCoreAsync(subscriptionReference, ct);
            if (reconciled is not null)
            {
                return reconciled;
            }

            throw ex is MaxioProviderException providerException
                ? providerException
                : new MaxioProviderException(
                    HttpStatusCode.ServiceUnavailable,
                    "The billing provider could not confirm the subscription outcome.",
                    ex);
        }
    }

    private async Task<Product> ReadAndValidateProductAsync(string productHandle, CancellationToken ct)
    {
        ProductResponse response;
        try
        {
            response = await CallAsync(
                callCt => _client.Products.ReadProductByHandle(productHandle, ct: callCt),
                ct);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            throw new BillingValidationException("The selected subscription plan does not exist.");
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError(ex.Error, ex);
        }

        var product = response.Product;
        if (product.Handle is null ||
            !string.Equals(product.Handle, productHandle, StringComparison.Ordinal) ||
            product.ArchivedAt is not null ||
            !string.Equals(product.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
        {
            throw new BillingValidationException("The selected subscription plan is not available.");
        }

        return product;
    }

    private async Task<int> ResolveMeteredComponentAsync(CancellationToken ct)
    {
        ComponentResponse response;
        try
        {
            response = await CallAsync(
                callCt => _client.Components.FindComponent(MeteredComponentHandle, ct: callCt),
                ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError(ex.Error, ex);
        }

        var component = response.Component;
        var supportedKind = component.Kind == ComponentKind.MeteredComponent ||
                            component.Kind == ComponentKind.EventBasedComponent;
        if (component.Id is null ||
            component.Archived == true ||
            !supportedKind ||
            !string.Equals(component.Handle, MeteredComponentHandle, StringComparison.Ordinal) ||
            !string.Equals(component.ProductFamilyHandle, _options.ProductFamilyHandle, StringComparison.Ordinal))
        {
            throw new MaxioProviderException(
                HttpStatusCode.BadGateway,
                "The configured billing component is unavailable or incompatible.");
        }

        return component.Id.Value;
    }

    private async Task<CollectionMethod> ResolveNonAutomaticCollectionMethodAsync(CancellationToken ct)
    {
        SiteResponse response;
        try
        {
            response = await CallAsync(
                callCt => _client.Sites.ReadSite(ct: callCt),
                ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError(ex.Error, ex);
        }

        return response.Site.RelationshipInvoicingEnabled switch
        {
            true => CollectionMethod.Remittance,
            false => CollectionMethod.Invoice,
            null => throw new MaxioProviderException(
                HttpStatusCode.BadGateway,
                "The billing provider did not report its invoicing architecture.")
        };
    }

    private async Task<Customer> EnsureCustomerAsync(BillingCustomer customer, CancellationToken ct)
    {
        var existing = await ReadCustomerAsync(customer.MaxioReference, ct);
        if (existing is not null)
        {
            return existing;
        }

        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.MaxioReference
            }
        };

        try
        {
            using var writeScope = _writeGuard.BeginScope();
            var response = await CallAsync(
                callCt => _client.Customers.CreateCustomer(request, ct: callCt),
                ct);
            return ValidateCustomer(response.Customer, customer.MaxioReference);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            var reconciled = await ReadCustomerAsync(customer.MaxioReference, ct);
            if (reconciled is not null)
            {
                return reconciled;
            }

            throw MapCreateCustomerError(ex);
        }
        catch (Exception ex) when (ex is MaxioWriteReplayBlockedException or MaxioProviderException)
        {
            var reconciled = await ReadCustomerAsync(customer.MaxioReference, ct);
            if (reconciled is not null)
            {
                return reconciled;
            }

            throw ex is MaxioProviderException providerException
                ? providerException
                : new MaxioProviderException(
                    HttpStatusCode.ServiceUnavailable,
                    "The billing provider could not confirm the customer outcome.",
                    ex);
        }
    }

    private async Task<Customer?> ReadCustomerAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await CallAsync(
                callCt => _client.Customers.ReadCustomerByReference(reference, ct: callCt),
                ct);
            return ValidateCustomer(response.Customer, reference);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError(ex.Error, ex);
        }
    }

    private async Task<UserSubscription?> FindSubscriptionCoreAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await CallAsync(
                callCt => _client.Subscriptions.FindSubscription(reference, ct: callCt),
                ct);
            return MapSubscription(response.Subscription, reference);
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

            throw new MaxioProviderException(
                HttpStatusCode.BadGateway,
                "The billing provider returned an unrecognized subscription lookup error.",
                ex);
        }
    }

    private async Task<IReadOnlyList<UserSubscription>> ListSubscriptionsCoreAsync(
        string customerReference,
        CancellationToken ct)
    {
        var customer = await ReadCustomerAsync(customerReference, ct);
        if (customer is null)
        {
            return Array.Empty<UserSubscription>();
        }

        if (customer.Id is null)
        {
            throw new MaxioProviderException(
                HttpStatusCode.BadGateway,
                "The billing provider returned a customer without an identifier.");
        }

        try
        {
            var responses = await CallAsync(
                callCt => _client.Customers.ListCustomerSubscriptions(customer.Id.Value, ct: callCt),
                ct);
            return responses
                .Where(response => response.Subscription is not null)
                .Select(response => MapSubscription(response.Subscription, response.Subscription!.Reference))
                .ToArray();
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError(ex.Error, ex);
        }
    }

    private async Task<T> WithBudgetAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TotalCallBudget);
        try
        {
            return await action(budget.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MaxioProviderException(
                HttpStatusCode.GatewayTimeout,
                "The billing provider did not respond before the request deadline.");
        }
    }

    private async Task<T> CallAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        using var responseScope = _responseContext.BeginScope();
        try
        {
            return await action(cancellationToken);
        }
        catch (JsonException ex)
        {
            var status = _responseContext.LastStatusCode;
            throw new MaxioProviderException(
                status is not null && (int)status.Value >= 400
                    ? MapBoundaryStatus(status.Value)
                    : HttpStatusCode.BadGateway,
                "The billing provider returned a response that could not be processed.",
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioProviderException(
                HttpStatusCode.ServiceUnavailable,
                "The billing provider is currently unreachable.",
                ex);
        }
    }

    private static Customer ValidateCustomer(Customer customer, string expectedReference)
    {
        if (customer.Id is null ||
            !string.Equals(customer.Reference, expectedReference, StringComparison.Ordinal))
        {
            throw new MaxioProviderException(
                HttpStatusCode.BadGateway,
                "The billing provider returned an invalid customer response.");
        }

        return customer;
    }

    private static UserSubscription MapSubscription(Subscription? subscription, string? expectedReference)
    {
        if (subscription is null ||
            subscription.Reference is null ||
            (expectedReference is not null &&
             !string.Equals(subscription.Reference, expectedReference, StringComparison.Ordinal)) ||
            subscription.Product?.Handle is null ||
            subscription.Product.Name is null ||
            subscription.State is null)
        {
            throw new MaxioProviderException(
                HttpStatusCode.BadGateway,
                "The billing provider returned an invalid subscription response.");
        }

        return new UserSubscription(
            subscription.Reference,
            subscription.Product.Handle,
            subscription.Product.Name,
            subscription.ProductPriceInCents,
            subscription.State.Value,
            subscription.NextAssessmentAt);
    }

    private static MaxioProviderException MapListProductsError(
        SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out _))
        {
            return new MaxioProviderException(
                HttpStatusCode.NotFound,
                "The configured billing product family was not found.",
                ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRawError(raw, ex);
        }

        return new MaxioProviderException(
            HttpStatusCode.BadGateway,
            "The billing provider returned an unrecognized catalog error.",
            ex);
    }

    private static MaxioProviderException MapCreateCustomerError(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out _))
        {
            return new MaxioProviderException(
                HttpStatusCode.UnprocessableEntity,
                "The billing provider rejected the customer profile.",
                ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRawError(raw, ex);
        }

        return new MaxioProviderException(
            HttpStatusCode.BadGateway,
            "The billing provider returned an unrecognized customer error.",
            ex);
    }

    private static MaxioProviderException MapCreateSubscriptionError(
        SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out _))
        {
            return new MaxioProviderException(
                HttpStatusCode.UnprocessableEntity,
                "The billing provider rejected the subscription.",
                ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRawError(raw, ex);
        }

        return new MaxioProviderException(
            HttpStatusCode.BadGateway,
            "The billing provider returned an unrecognized subscription error.",
            ex);
    }

    private static MaxioProviderException MapRawError(RawError raw, Exception exception) =>
        new(
            MapBoundaryStatus(raw.StatusCode),
            (int)raw.StatusCode is >= 400 and < 500
                ? "The billing provider rejected the request."
                : "The billing provider could not complete the request.",
            exception);

    private static HttpStatusCode MapBoundaryStatus(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => HttpStatusCode.GatewayTimeout,
        HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable => HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => HttpStatusCode.BadGateway,
        >= HttpStatusCode.InternalServerError => HttpStatusCode.BadGateway,
        _ => statusCode
    };
}
