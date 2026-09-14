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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioSubscriptionGateway : IMaxioSubscriptionGateway
{
    private const int ProductPageSize = 100;
    private static readonly TimeSpan TotalCallBudget = TimeSpan.FromSeconds(30);

    private readonly MaxioAdvancedBilling.MaxioAdvancedBillingClient _client;
    private readonly IMaxioAttemptContext _attemptContext;
    private readonly MaxioOptions _options;

    public MaxioSubscriptionGateway(
        MaxioAdvancedBilling.MaxioAdvancedBillingClient client,
        IMaxioAttemptContext attemptContext,
        IOptions<MaxioOptions> options)
    {
        _client = client;
        _attemptContext = attemptContext;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        using var budget = CreateBudget(cancellationToken);
        using var attempt = _attemptContext.Begin(allowSingleWrite: false);

        try
        {
            var families = await _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: budget.Token);

            var matches = families
                .Select(x => x.ProductFamily)
                .Where(x => x is not null && string.Equals(x.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
                .ToList();

            if (matches.Count != 1 || matches[0]!.Id is not int familyId)
            {
                throw new MaxioBillingException("The configured Maxio product family could not be resolved uniquely.");
            }

            var plans = new List<MaxioPlan>();
            for (var page = 1; ; page++)
            {
                var responses = await _client.ProductFamilies.ListProductsForProductFamily(
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
                    perPage: ProductPageSize,
                    ct: budget.Token);

                foreach (var response in responses)
                {
                    var product = response.Product;
                    if (product.ArchivedAt is not null || string.IsNullOrWhiteSpace(product.Handle) ||
                        string.IsNullOrWhiteSpace(product.Name) || product.PriceInCents is not long priceInCents)
                    {
                        continue;
                    }

                    plans.Add(new MaxioPlan(
                        product.Handle,
                        product.Name,
                        product.Description,
                        priceInCents,
                        product.Interval,
                        product.IntervalUnit?.Value,
                        product.RequireCreditCard ?? false));
                }

                if (responses.Count < ProductPageSize)
                {
                    break;
                }
            }

            return plans.OrderBy(x => x.PriceInCents).ThenBy(x => x.Handle, StringComparer.Ordinal).ToList();
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out _))
            {
                throw new MaxioBillingException("The configured Maxio product family was not found.", HttpStatusCode.NotFound, innerException: ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRawError(raw, "Maxio rejected the product catalog request.", ex);
            }

            throw new MaxioBillingException("Maxio rejected the product catalog request.", innerException: ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRawError(ex.Error, "Maxio rejected the product family request.", ex);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw TranslateInfrastructureFailure(ex, isWrite: false, cancellationToken);
        }
    }

    public async Task<MaxioCustomer> EnsureCustomerAsync(
        string reference,
        string firstName,
        string lastName,
        string email,
        CancellationToken cancellationToken)
    {
        var existing = await ReadCustomerAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        using var budget = CreateBudget(cancellationToken);
        using var attempt = _attemptContext.Begin(allowSingleWrite: true);
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference,
            },
        };

        try
        {
            var response = await _client.Customers.CreateCustomer(body, ct: budget.Token);
            return MapCustomer(response, reference);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                var racedCustomer = await ReadCustomerAsync(reference, cancellationToken);
                if (racedCustomer is not null)
                {
                    return racedCustomer;
                }

                throw new MaxioBillingException("Maxio rejected the customer details.", HttpStatusCode.UnprocessableEntity, innerException: ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRawError(raw, "Maxio rejected the customer request.", ex);
            }

            throw new MaxioBillingException("Maxio rejected the customer request.", innerException: ex);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw TranslateInfrastructureFailure(ex, isWrite: true, cancellationToken);
        }
    }

    public async Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        using var budget = CreateBudget(cancellationToken);
        using var attempt = _attemptContext.Begin(allowSingleWrite: false);

        try
        {
            var response = await _client.Subscriptions.FindSubscription(reference, ct: budget.Token);
            return response.Subscription is null ? null : MapSubscription(response.Subscription);
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out var notFound) && notFound.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRawError(raw, "Maxio rejected the subscription lookup.", ex);
            }

            throw new MaxioBillingException("Maxio rejected the subscription lookup.", innerException: ex);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw TranslateInfrastructureFailure(ex, isWrite: false, cancellationToken);
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        string customerReference,
        string subscriptionReference,
        string productHandle,
        CancellationToken cancellationToken)
    {
        using var budget = CreateBudget(cancellationToken);
        using var attempt = _attemptContext.Begin(allowSingleWrite: true);

        try
        {
            var site = await _client.Sites.ReadSite(ct: budget.Token);
            var collectionMethod = site.Site.RelationshipInvoicingEnabled switch
            {
                true => CollectionMethod.Remittance,
                false => CollectionMethod.Invoice,
                null => throw new MaxioBillingException("Maxio returned an incomplete site response."),
            };

            var body = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = productHandle,
                    CustomerReference = customerReference,
                    Reference = subscriptionReference,
                    PaymentCollectionMethod = collectionMethod,
                },
            };

            var response = await _client.Subscriptions.CreateSubscription(body, ct: budget.Token);
            if (response.Subscription is null)
            {
                throw new MaxioBillingException("Maxio returned an incomplete subscription response.", outcomeMayBeAmbiguous: true);
            }

            return MapSubscription(response.Subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var validation))
            {
                var detail = validation.Errors.FirstOrDefault();
                throw new MaxioBillingException(
                    string.IsNullOrWhiteSpace(detail) ? "Maxio rejected the subscription request." : detail,
                    HttpStatusCode.UnprocessableEntity,
                    innerException: ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRawError(raw, "Maxio rejected the subscription request.", ex);
            }

            throw new MaxioBillingException("Maxio rejected the subscription request.", innerException: ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRawError(ex.Error, "Maxio rejected the site request.", ex);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw TranslateInfrastructureFailure(ex, isWrite: true, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        string customerReference,
        CancellationToken cancellationToken)
    {
        var customer = await ReadCustomerAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        using var budget = CreateBudget(cancellationToken);
        using var attempt = _attemptContext.Begin(allowSingleWrite: false);
        try
        {
            var responses = await _client.Customers.ListCustomerSubscriptions(customer.Id, ct: budget.Token);
            return responses
                .Where(x => x.Subscription is not null)
                .Select(x => MapSubscription(x.Subscription!))
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRawError(ex.Error, "Maxio rejected the customer subscription request.", ex);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw TranslateInfrastructureFailure(ex, isWrite: false, cancellationToken);
        }
    }

    private async Task<MaxioCustomer?> ReadCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        using var budget = CreateBudget(cancellationToken);
        using var attempt = _attemptContext.Begin(allowSingleWrite: false);
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: budget.Token);
            return MapCustomer(response, reference);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRawError(ex.Error, "Maxio rejected the customer lookup.", ex);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw TranslateInfrastructureFailure(ex, isWrite: false, cancellationToken);
        }
    }

    private static MaxioCustomer MapCustomer(CustomerResponse response, string expectedReference)
    {
        var customer = response.Customer;
        if (customer.Id is not int id || string.IsNullOrWhiteSpace(customer.Reference) ||
            !string.Equals(customer.Reference, expectedReference, StringComparison.Ordinal))
        {
            throw new MaxioBillingException("Maxio returned an incomplete customer response.");
        }

        return new MaxioCustomer(id, customer.Reference);
    }

    private static MaxioSubscription MapSubscription(Subscription subscription)
    {
        var product = subscription.Product;
        if (subscription.Id is not int id || string.IsNullOrWhiteSpace(subscription.Reference) ||
            product is null || string.IsNullOrWhiteSpace(product.Handle) || string.IsNullOrWhiteSpace(product.Name) ||
            subscription.ProductPriceInCents is not long priceInCents)
        {
            throw new MaxioBillingException("Maxio returned an incomplete subscription response.");
        }

        return new MaxioSubscription(
            id,
            subscription.Reference,
            product.Handle,
            product.Name,
            priceInCents,
            subscription.Currency,
            subscription.State?.Value,
            subscription.NextAssessmentAt,
            product.Interval,
            product.IntervalUnit?.Value);
    }

    private static MaxioBillingException FromRawError(RawError raw, string message, Exception innerException) =>
        new(message, raw.StatusCode, innerException: innerException);

    private MaxioBillingException TranslateInfrastructureFailure(
        Exception exception,
        bool isWrite,
        CancellationToken callerToken)
    {
        if (exception is OperationCanceledException && callerToken.IsCancellationRequested)
        {
            throw exception;
        }

        if (exception is JsonException)
        {
            var status = _attemptContext.ResponseStatusCode;
            if (status is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError)
            {
                return new MaxioBillingException("Maxio rejected the request.", status, innerException: exception);
            }

            return new MaxioBillingException(
                "Maxio returned a response that could not be processed.",
                outcomeMayBeAmbiguous: isWrite,
                innerException: exception);
        }

        return new MaxioBillingException(
            isWrite
                ? "The Maxio write did not complete with a known outcome."
                : "Maxio is currently unavailable.",
            outcomeMayBeAmbiguous: isWrite,
            innerException: exception);
    }

    private static bool IsInfrastructureFailure(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or JsonException or MaxioWriteReplayBlockedException;

    private static CancellationTokenSource CreateBudget(CancellationToken cancellationToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(TotalCallBudget);
        return source;
    }
}
