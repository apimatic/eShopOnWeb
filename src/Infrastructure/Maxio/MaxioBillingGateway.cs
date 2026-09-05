using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

internal sealed record MaxioCustomer(int Id);

public sealed class MaxioBillingGateway
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioWriteScope _writeScope;
    private readonly ILogger<MaxioBillingGateway> _logger;

    public MaxioBillingGateway(MaxioAdvancedBillingClient client, MaxioWriteScope writeScope, ILogger<MaxioBillingGateway> logger)
    {
        _client = client;
        _writeScope = writeScope;
        _logger = logger;
    }

    internal async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(string familyHandle, CancellationToken cancellationToken)
    {
        try
        {
            var families = await _client.ProductFamilies.ListProductFamilies(
                dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, ct: cancellationToken);
            var family = families.Select(x => x.ProductFamily).FirstOrDefault(x => x?.Handle == familyHandle && x.ArchivedAt is null);
            if (family?.Id is null)
            {
                throw new MaxioBillingException("The configured subscription product family is unavailable.", 503);
            }

            var products = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: family.Id.Value.ToString(), dateField: null, filter: null, startDate: null, endDate: null,
                startDatetime: null, endDatetime: null, includeArchived: false, include: null, page: 1, perPage: 100,
                ct: cancellationToken);

            return products
                .Select(x => x.Product)
                .Where(x => x is not null && x.ArchivedAt is null && !string.IsNullOrWhiteSpace(x.Handle) && !string.IsNullOrWhiteSpace(x.Name))
                .Select(x => new SubscriptionPlan(
                    x.Handle!,
                    x.Name!,
                    RequirePriceInCents(x.PriceInCents, "product.price_in_cents"),
                    RequireInterval(x.Interval, "product.interval"),
                    x.IntervalUnit?.Value ?? string.Empty))
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure(ex.Error.StatusCode, ex);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            throw ProviderFailure(ReadStatus(ex.Error), ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioBillingException("Maxio returned a response that could not be processed.", 502, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioBillingException("Maxio is unavailable.", 502, ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new MaxioBillingException("Maxio did not respond in time.", 504, ex);
        }
    }

    internal async Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: cancellationToken);
            if (response.Customer?.Id is not int id)
            {
                throw new MaxioBillingException("Maxio returned an incomplete customer response.", 502);
            }

            return new MaxioCustomer(id);
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure(ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioBillingException("Maxio returned a response that could not be processed.", 502, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioBillingException("Maxio is unavailable.", 502, ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new MaxioBillingException("Maxio did not respond in time.", 504, ex);
        }
    }

    internal async Task<MaxioCustomer> EnsureCustomerAsync(ShopperProfile shopper, string reference, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var response = await _client.Customers.CreateCustomer(new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = shopper.FirstName,
                    LastName = shopper.LastName,
                    Email = shopper.Email,
                    Reference = reference
                }
            }, ct: cancellationToken);

            if (response.Customer?.Id is not int id)
            {
                throw new MaxioBillingException("Maxio returned an incomplete customer response.", 502);
            }

            return new MaxioCustomer(id);
        }
        catch (SdkException<CreateCustomerError> ex) when (ex.Error.TryGetCustomerErrorResponse1(out _))
        {
            // A duplicate reference can be a concurrent create. Re-read before failing.
            var racedCustomer = await FindCustomerAsync(reference, cancellationToken);
            if (racedCustomer is not null)
            {
                return racedCustomer;
            }

            throw new MaxioBillingException("Maxio rejected the customer enrollment.", 422, ex);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            throw ProviderFailure(ReadStatus(ex.Error), ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioBillingException("Maxio returned a response that could not be processed.", 502, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioBillingException("Maxio is unavailable.", 502, ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new MaxioBillingException("Maxio did not respond in time.", 504, ex);
        }
    }

    internal async Task<BillingSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(reference, ct: cancellationToken);
            return MapSubscription(response.Subscription);
        }
        catch (SdkException<FindSubscriptionError> ex) when (ex.Error.TryGetNoContent(out _))
        {
            return null;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            throw ProviderFailure(ReadStatus(ex.Error), ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioBillingException("Maxio returned a response that could not be processed.", 502, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioBillingException("Maxio is unavailable.", 502, ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new MaxioBillingException("Maxio did not respond in time.", 504, ex);
        }
    }

    internal async Task<BillingSubscription> CreateSubscriptionAsync(int customerId, string planHandle, string reference, CancellationToken cancellationToken)
    {
        try
        {
            using var writeScope = _writeScope.Begin(reference);
            var response = await _client.Subscriptions.CreateSubscription(new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = planHandle,
                    Reference = reference,
                    PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Invoice
                }
            }, ct: cancellationToken);
            return MapSubscription(response.Subscription);
        }
        catch (MaxioWriteRetryBlockedException)
        {
            throw;
        }
        catch (SdkException<CreateSubscriptionError> ex) when (ex.Error.TryGetErrorListResponse1(out var error))
        {
            _logger.LogWarning("Maxio rejected a subscription enrollment: {Errors}", string.Join("; ", error.Errors.Take(5)));
            throw new MaxioBillingException("Maxio rejected the subscription enrollment.", 422, ex);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            throw ProviderFailure(ReadStatus(ex.Error), ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioBillingException("Maxio returned a response that could not be processed.", 502, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioBillingException("Maxio is unavailable.", 502, ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new MaxioBillingException("Maxio did not respond in time.", 504, ex);
        }
    }

    internal async Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Customers.ListCustomerSubscriptions(customerId, ct: cancellationToken);
            return response.Select(x => MapSubscription(x.Subscription)).ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure(ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioBillingException("Maxio returned a response that could not be processed.", 502, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioBillingException("Maxio is unavailable.", 502, ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new MaxioBillingException("Maxio did not respond in time.", 504, ex);
        }
    }

    private static BillingSubscription MapSubscription(Subscription? subscription)
    {
        if (subscription?.Id is not int id || string.IsNullOrWhiteSpace(subscription.Reference) || subscription.Product is null ||
            string.IsNullOrWhiteSpace(subscription.Product.Handle) || string.IsNullOrWhiteSpace(subscription.Product.Name) ||
            subscription.ProductPriceInCents is null || subscription.State is null)
        {
            throw new MaxioBillingException("Maxio returned an incomplete subscription response.", 502);
        }

        return new BillingSubscription(id, subscription.Reference, subscription.Product.Handle, subscription.Product.Name,
            RequirePriceInCents(subscription.ProductPriceInCents, "subscription.product_price_in_cents"), subscription.Currency,
            subscription.State.Value, subscription.NextAssessmentAt);
    }

    private static int RequirePriceInCents(long? priceInCents, string fieldName)
    {
        if (priceInCents is not long value || value < int.MinValue || value > int.MaxValue)
        {
            throw new MaxioBillingException($"Maxio returned an invalid {fieldName} value.", 502);
        }

        return (int)value;
    }

    private static int RequireInterval(int? interval, string fieldName)
    {
        if (interval is not int value)
        {
            throw new MaxioBillingException($"Maxio returned an incomplete {fieldName} value.", 502);
        }

        return value;
    }

    private static int ReadStatus(object error)
    {
        return error switch
        {
            CreateCustomerError createCustomerError when createCustomerError.TryGetRawError(out var raw) => (int)raw.StatusCode,
            CreateSubscriptionError createSubscriptionError when createSubscriptionError.TryGetRawError(out var raw) => (int)raw.StatusCode,
            FindSubscriptionError findSubscriptionError when findSubscriptionError.TryGetRawError(out var raw) => (int)raw.StatusCode,
            ListProductsForProductFamilyError productsError when productsError.TryGetRawError(out var raw) => (int)raw.StatusCode,
            _ => 502
        };
    }

    private static MaxioBillingException ProviderFailure(System.Net.HttpStatusCode statusCode, Exception innerException) =>
        new("Maxio could not process the request.", (int)statusCode, innerException);

    private static MaxioBillingException ProviderFailure(int statusCode, Exception innerException) =>
        new("Maxio could not process the request.", statusCode is >= 400 and <= 599 ? statusCode : 502, innerException);
}
