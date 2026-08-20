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
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class MaxioBillingGateway : IMaxioBillingGateway
{
    private const int PageSize = 20;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(25);
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly MaxioWriteScopeAccessor _writeScope;

    public MaxioBillingGateway(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        MaxioWriteScopeAccessor writeScope)
    {
        _client = client;
        _options = options.Value;
        _writeScope = writeScope;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);
        var products = new List<SubscriptionPlan>();

        for (var page = 1; ; page++)
        {
            IReadOnlyList<ProductResponse> response;
            try
            {
                response = await BoundedAsync(ct => _client.ProductFamilies.ListProductsForProductFamily(
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
                    ct: ct), cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out _))
                {
                    throw new BillingProviderException(BillingFailureKind.NotFound, "The configured Maxio product family was not found.", 404, ex);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw Translate(raw, "Maxio could not list subscription plans.", ex);
                }

                throw UnreadableError("Maxio could not list subscription plans.", ex);
            }
            catch (Exception ex) when (IsBoundaryException(ex))
            {
                throw TranslateBoundary("Maxio could not list subscription plans.", ex, cancellationToken);
            }

            foreach (var envelope in response)
            {
                if (TryMapPlan(envelope.Product, out var plan))
                {
                    products.Add(plan);
                }
            }

            if (response.Count < PageSize)
            {
                break;
            }
        }

        return products;
    }

    public async Task<SubscriptionPlan> GetPlanAsync(string productHandle, CancellationToken cancellationToken)
    {
        ProductResponse response;
        try
        {
            response = await BoundedAsync(ct => _client.Products.ReadProductByHandle(productHandle, ct: ct), cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex.Error, "Maxio could not read the requested subscription plan.", ex);
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            throw TranslateBoundary("Maxio could not read the requested subscription plan.", ex, cancellationToken);
        }

        var product = response.Product;
        if (product.ArchivedAt is not null ||
            !string.Equals(product.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal) ||
            !TryMapPlan(product, out var plan))
        {
            throw new BillingProviderException(BillingFailureKind.InvalidRequest, "The requested subscription plan is not available.", 422);
        }

        return plan;
    }

    public async Task EnsureCustomerAsync(BillingCustomerProfile profile, CancellationToken cancellationToken)
    {
        var existing = await TryReadCustomerAsync(profile.UserId, cancellationToken);
        if (existing is not null)
        {
            RequireCustomer(existing, profile.UserId);
            return;
        }

        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = profile.FirstName,
                LastName = profile.LastName,
                Email = profile.Email,
                Reference = profile.UserId
            }
        };

        try
        {
            using (_writeScope.Begin())
            {
                var response = await BoundedAsync(ct => _client.Customers.CreateCustomer(request, ct: ct), cancellationToken);
                RequireCustomer(response.Customer, profile.UserId);
            }
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                var racedCustomer = await TryReadCustomerAsync(profile.UserId, cancellationToken);
                if (racedCustomer is not null)
                {
                    RequireCustomer(racedCustomer, profile.UserId);
                    return;
                }

                throw new BillingProviderException(BillingFailureKind.InvalidRequest, "Maxio rejected the customer profile.", 422, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Translate(raw, "Maxio could not create the billing customer.", ex);
            }

            throw UnreadableError("Maxio could not create the billing customer.", ex);
        }
        catch (MaxioWriteReplayPreventedException ex)
        {
            var recovered = await TryReadCustomerAsync(profile.UserId, cancellationToken);
            if (recovered is not null)
            {
                RequireCustomer(recovered, profile.UserId);
                return;
            }

            throw new BillingProviderException(BillingFailureKind.Indeterminate, "The billing customer creation outcome is still being reconciled.", null, ex);
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            throw TranslateBoundary("Maxio could not create the billing customer.", ex, cancellationToken);
        }
    }

    public Task<SubscriptionDetails?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken) =>
        TryFindSubscriptionAsync(reference, cancellationToken);

    public async Task<SubscriptionDetails> CreateSubscriptionAsync(
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var existing = await TryFindSubscriptionAsync(subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var paymentCollectionMethod = await ResolveNoCardCollectionMethodAsync(cancellationToken);
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

        try
        {
            using (_writeScope.Begin())
            {
                var response = await BoundedAsync(ct => _client.Subscriptions.CreateSubscription(request, ct: ct), cancellationToken);
                return MapSubscription(response.Subscription);
            }
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out _))
            {
                throw new BillingProviderException(BillingFailureKind.InvalidRequest, "Maxio rejected the subscription request.", 422, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Translate(raw, "Maxio could not create the subscription.", ex);
            }

            throw UnreadableError("Maxio could not create the subscription.", ex);
        }
        catch (Exception ex) when (ex is MaxioWriteReplayPreventedException or HttpRequestException or TaskCanceledException or JsonException)
        {
            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            var recovered = await TryFindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (recovered is not null)
            {
                return recovered;
            }

            throw new BillingProviderException(BillingFailureKind.Indeterminate, "The subscription creation outcome is still being reconciled.", null, ex);
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsAsync(string customerReference, CancellationToken cancellationToken)
    {
        var customer = await TryReadCustomerAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        if (customer.Id is null)
        {
            throw new BillingProviderException(BillingFailureKind.Unavailable, "Maxio returned an incomplete billing customer.");
        }

        IReadOnlyList<SubscriptionResponse> response;
        try
        {
            response = await BoundedAsync(ct => _client.Customers.ListCustomerSubscriptions(customer.Id.Value, ct: ct), cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex.Error, "Maxio could not list customer subscriptions.", ex);
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            throw TranslateBoundary("Maxio could not list customer subscriptions.", ex, cancellationToken);
        }

        var subscriptions = new List<SubscriptionDetails>();
        foreach (var envelope in response)
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(MapSubscription(envelope.Subscription));
            }
        }

        return subscriptions;
    }

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ProductFamilyResponse> response;
        try
        {
            response = await BoundedAsync(ct => _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: ct), cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex.Error, "Maxio could not resolve the configured product family.", ex);
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            throw TranslateBoundary("Maxio could not resolve the configured product family.", ex, cancellationToken);
        }

        var matches = response
            .Select(item => item.ProductFamily)
            .Where(family => family is not null && family.ArchivedAt is null && string.Equals(family.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
            .ToArray();

        if (matches.Length != 1 || matches[0]!.Id is null)
        {
            throw new BillingProviderException(BillingFailureKind.Misconfigured, "The configured Maxio product family is missing or ambiguous.");
        }

        return matches[0]!.Id!.Value;
    }

    private async Task<Customer?> TryReadCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(ct => _client.Customers.ReadCustomerByReference(reference, ct: ct), cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex.Error, "Maxio could not read the billing customer.", ex);
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            throw TranslateBoundary("Maxio could not read the billing customer.", ex, cancellationToken);
        }
    }

    private async Task<CollectionMethod> ResolveNoCardCollectionMethodAsync(CancellationToken cancellationToken)
    {
        SiteResponse response;
        try
        {
            response = await BoundedAsync(ct => _client.Sites.ReadSite(ct: ct), cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex.Error, "Maxio could not determine the site's payment collection method.", ex);
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            throw TranslateBoundary("Maxio could not determine the site's payment collection method.", ex, cancellationToken);
        }

        if (response.Site.RelationshipInvoicingEnabled is null)
        {
            throw new BillingProviderException(
                BillingFailureKind.Unavailable,
                "Maxio returned incomplete site invoicing settings.");
        }

        return response.Site.RelationshipInvoicingEnabled.Value
            ? CollectionMethod.Remittance
            : CollectionMethod.Invoice;
    }

    private async Task<SubscriptionDetails?> TryFindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(ct => _client.Subscriptions.FindSubscription(reference, ct: ct), cancellationToken);
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
                throw Translate(raw, "Maxio could not find the subscription.", ex);
            }

            throw UnreadableError("Maxio could not find the subscription.", ex);
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            throw TranslateBoundary("Maxio could not find the subscription.", ex, cancellationToken);
        }
    }

    private static bool TryMapPlan(Product product, out SubscriptionPlan plan)
    {
        if (product.ArchivedAt is null &&
            !string.IsNullOrWhiteSpace(product.Handle) &&
            !string.IsNullOrWhiteSpace(product.Name) &&
            product.PriceInCents is not null &&
            product.Interval is not null &&
            product.IntervalUnit is not null)
        {
            plan = new SubscriptionPlan(
                product.Handle,
                product.Name,
                product.Description,
                product.PriceInCents.Value,
                product.Interval.Value,
                product.IntervalUnit.Value);
            return true;
        }

        plan = null!;
        return false;
    }

    private static SubscriptionDetails MapSubscription(Subscription? subscription)
    {
        var product = subscription?.Product;
        var price = subscription?.ProductPriceInCents ?? product?.PriceInCents;
        if (subscription?.Id is null ||
            string.IsNullOrWhiteSpace(subscription.Reference) ||
            product is null ||
            string.IsNullOrWhiteSpace(product.Handle) ||
            string.IsNullOrWhiteSpace(product.Name) ||
            price is null ||
            subscription.State is null ||
            string.IsNullOrWhiteSpace(subscription.Currency))
        {
            throw new BillingProviderException(BillingFailureKind.Unavailable, "Maxio returned an incomplete subscription.");
        }

        return new SubscriptionDetails(
            subscription.Id.Value,
            subscription.Reference,
            product.Handle,
            product.Name,
            price.Value,
            subscription.Currency,
            subscription.State.Value,
            subscription.NextAssessmentAt,
            product.Interval,
            product.IntervalUnit?.Value);
    }

    private static void RequireCustomer(Customer customer, string expectedReference)
    {
        if (customer.Id is null || !string.Equals(customer.Reference, expectedReference, StringComparison.Ordinal))
        {
            throw new BillingProviderException(BillingFailureKind.Unavailable, "Maxio returned an incomplete billing customer.");
        }
    }

    private static async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CallBudget);
        return await call(timeout.Token);
    }

    private static bool IsBoundaryException(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or JsonException;

    private static BillingProviderException TranslateBoundary(string message, Exception exception, CancellationToken callerToken)
    {
        if (exception is OperationCanceledException && callerToken.IsCancellationRequested)
        {
            throw exception;
        }

        return new BillingProviderException(BillingFailureKind.Unavailable, message, null, exception);
    }

    private static BillingProviderException Translate(RawError error, string message, Exception exception)
    {
        var status = (int)error.StatusCode;
        var kind = status switch
        {
            404 => BillingFailureKind.NotFound,
            >= 400 and < 500 => BillingFailureKind.InvalidRequest,
            _ => BillingFailureKind.Unavailable
        };
        return new BillingProviderException(kind, message, status, exception);
    }

    private static BillingProviderException UnreadableError(string message, Exception exception) =>
        new(BillingFailureKind.Unavailable, message, null, exception);

}
