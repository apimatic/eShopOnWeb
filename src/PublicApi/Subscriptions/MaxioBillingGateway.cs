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
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioBillingGateway : IMaxioBillingGateway
{
    private const int PageSize = 100;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
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

    public async Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        try
        {
            var families = await ExecuteAsync(
                ct => _client.ProductFamilies.ListProductFamilies(
                    dateField: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    ct: ct),
                guardWrite: false,
                cancellationToken);

            var family = families
                .Select(x => x.ProductFamily)
                .SingleOrDefault(x => x is not null &&
                    x.ArchivedAt is null &&
                    string.Equals(x.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal));

            if (family?.Id is not int familyId)
            {
                throw new SubscriptionBillingException(
                    StatusCodes.Status404NotFound,
                    "The configured subscription product family was not found.");
            }

            var plans = new List<BillingPlan>();
            for (var page = 1; ; page++)
            {
                var responses = await ExecuteAsync(
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
                    guardWrite: false,
                    cancellationToken);

                plans.AddRange(responses.Select(x => MapPlan(x.Product)));
                if (responses.Count < PageSize)
                {
                    break;
                }
            }

            return plans;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRawError(ex.Error, ex);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out _))
            {
                throw new SubscriptionBillingException(
                    StatusCodes.Status404NotFound,
                    "The configured subscription product family was not found.",
                    ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRawError(raw, ex);
            }

            throw ProviderContractFailure(ex);
        }
    }

    public async Task<BillingCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await ExecuteAsync(
                ct => _client.Customers.ReadCustomerByReference(reference, ct: ct),
                guardWrite: false,
                cancellationToken);
            return MapCustomer(response.Customer);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRawError(ex.Error, ex);
        }
    }

    public async Task<BillingCustomer> EnsureCustomerAsync(
        string reference,
        string firstName,
        string lastName,
        string email,
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
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        try
        {
            var response = await ExecuteAsync(
                ct => _client.Customers.CreateCustomer(request, ct: ct),
                guardWrite: true,
                cancellationToken);
            return MapCustomer(response.Customer);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                var winner = await FindCustomerAsync(reference, cancellationToken);
                if (winner is not null)
                {
                    return winner;
                }

                throw new SubscriptionBillingException(
                    StatusCodes.Status422UnprocessableEntity,
                    "Maxio rejected the customer details.",
                    ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRawError(raw, ex);
            }

            throw ProviderContractFailure(ex);
        }
        catch (SubscriptionBillingException ex) when (ex.OutcomeUnknown)
        {
            var winner = await FindCustomerAsync(reference, cancellationToken);
            if (winner is not null)
            {
                return winner;
            }

            throw;
        }
    }

    public async Task<BillingSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await ExecuteAsync(
                ct => _client.Subscriptions.FindSubscription(reference, ct: ct),
                guardWrite: false,
                cancellationToken);
            return response.Subscription is null ? throw ProviderContractFailure() : MapSubscription(response.Subscription);
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out var notFound) && notFound.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRawError(raw, ex);
            }

            throw ProviderContractFailure(ex);
        }
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(
        string productHandle,
        string customerReference,
        string reference,
        CancellationToken cancellationToken)
    {
        var request = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerReference = customerReference,
                Reference = reference,
                PaymentCollectionMethod = CollectionMethod.Remittance
            }
        };

        try
        {
            var response = await ExecuteAsync(
                ct => _client.Subscriptions.CreateSubscription(request, ct: ct),
                guardWrite: true,
                cancellationToken);
            return response.Subscription is null ? throw ProviderContractFailure() : MapSubscription(response.Subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var error))
            {
                _logger.LogWarning(
                    "Maxio rejected subscription creation for product {ProductHandle}: {ProviderErrors}",
                    productHandle,
                    FormatProviderErrors(error.Errors));

                throw new SubscriptionBillingException(
                    StatusCodes.Status422UnprocessableEntity,
                    "Maxio rejected the subscription request.",
                    ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRawError(raw, ex);
            }

            throw ProviderContractFailure(ex);
        }
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var responses = await ExecuteAsync(
                ct => _client.Customers.ListCustomerSubscriptions(customerId, ct: ct),
                guardWrite: false,
                cancellationToken);

            return responses.Select(x => x.Subscription is null
                    ? throw ProviderContractFailure()
                    : MapSubscription(x.Subscription))
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRawError(ex.Error, ex);
        }
    }

    private static BillingPlan MapPlan(Product product)
    {
        if (string.IsNullOrWhiteSpace(product.Handle) ||
            string.IsNullOrWhiteSpace(product.Name) ||
            product.PriceInCents is not long priceInCents ||
            product.Interval is not int interval ||
            product.IntervalUnit is null)
        {
            throw ProviderContractFailure();
        }

        return new BillingPlan(
            product.Id,
            product.Handle,
            product.Name,
            product.Description,
            priceInCents,
            interval,
            product.IntervalUnit.Value,
            product.RequireCreditCard ?? false);
    }

    private static BillingCustomer MapCustomer(Customer customer)
    {
        if (customer.Id is not int id || string.IsNullOrWhiteSpace(customer.Reference))
        {
            throw ProviderContractFailure();
        }

        return new BillingCustomer(id, customer.Reference);
    }

    private static BillingSubscription MapSubscription(Subscription subscription)
    {
        if (subscription.Id is not int id ||
            subscription.Product is null ||
            string.IsNullOrWhiteSpace(subscription.Product.Handle) ||
            string.IsNullOrWhiteSpace(subscription.Product.Name) ||
            subscription.ProductPriceInCents is not long priceInCents ||
            subscription.State is null)
        {
            throw ProviderContractFailure();
        }

        return new BillingSubscription(
            id,
            subscription.Product.Handle,
            subscription.Product.Name,
            priceInCents,
            subscription.State.Value,
            subscription.NextAssessmentAt,
            subscription.CurrentPeriodEndsAt,
            subscription.Currency,
            subscription.Reference);
    }

    private static SubscriptionBillingException FromRawError(RawError raw, Exception innerException)
    {
        var statusCode = (int)raw.StatusCode;
        return statusCode switch
        {
            StatusCodes.Status400BadRequest or
            StatusCodes.Status404NotFound or
            StatusCodes.Status409Conflict or
            StatusCodes.Status422UnprocessableEntity => new SubscriptionBillingException(
                statusCode,
                "Maxio rejected the billing request.",
                innerException),
            StatusCodes.Status429TooManyRequests => new SubscriptionBillingException(
                StatusCodes.Status503ServiceUnavailable,
                "Subscription billing is temporarily unavailable.",
                innerException),
            _ => new SubscriptionBillingException(
                StatusCodes.Status502BadGateway,
                "Subscription billing could not be completed.",
                innerException)
        };
    }

    private static SubscriptionBillingException ProviderContractFailure(Exception? innerException = null) =>
        new(
            StatusCodes.Status502BadGateway,
            "Maxio returned a response that could not be processed.",
            innerException);

    private static string FormatProviderErrors(IReadOnlyList<string> errors)
    {
        const int maximumErrors = 10;
        const int maximumErrorLength = 300;

        var sanitized = errors
            .Take(maximumErrors)
            .Select(error => new string(error
                .Take(maximumErrorLength)
                .Select(character => char.IsControl(character) ? ' ' : character)
                .ToArray()));

        return string.Join(" | ", sanitized);
    }

    private static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        bool guardWrite,
        CancellationToken cancellationToken)
    {
        using var scope = MaxioCallScope.Begin(guardWrite);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(CallBudget);

        try
        {
            return await operation(budget.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SubscriptionBillingException(
                StatusCodes.Status504GatewayTimeout,
                "Subscription billing timed out.",
                outcomeUnknown: guardWrite);
        }
        catch (MaxioDuplicateSendPreventedException ex)
        {
            throw new SubscriptionBillingException(
                StatusCodes.Status502BadGateway,
                "The outcome of the Maxio request is being reconciled.",
                ex,
                outcomeUnknown: true);
        }
        catch (HttpRequestException ex)
        {
            throw new SubscriptionBillingException(
                StatusCodes.Status503ServiceUnavailable,
                "Subscription billing is temporarily unavailable.",
                ex,
                outcomeUnknown: guardWrite);
        }
        catch (JsonException ex)
        {
            var statusCode = scope.State.LastStatusCode;
            if (statusCode.HasValue && (int)statusCode.Value >= StatusCodes.Status400BadRequest)
            {
                throw new SubscriptionBillingException(
                    (int)statusCode.Value,
                    "Maxio rejected the billing request.",
                    ex);
            }

            throw ProviderContractFailure(ex);
        }
    }
}
