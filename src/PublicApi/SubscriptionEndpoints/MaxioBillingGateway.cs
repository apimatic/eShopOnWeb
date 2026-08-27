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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MaxioBillingGateway : IMaxioBillingGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int ProductsPerPage = 100;

    private readonly MaxioAdvancedBilling.MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingGateway> _logger;

    public MaxioBillingGateway(
        MaxioAdvancedBilling.MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        ILogger<MaxioBillingGateway> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await ExecuteAsync(
                ct => _client.ProductFamilies.ListProductFamilies(
                    dateField: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    ct: ct),
                writeOnce: false,
                cancellationToken);
        }
        catch (SdkException<RawError> exception)
        {
            throw FromRawError("list product families", exception.Error, exception);
        }

        var matchingFamilies = families
            .Where(response => string.Equals(
                response.ProductFamily?.Handle,
                _options.ProductFamilyHandle,
                StringComparison.Ordinal))
            .ToList();

        if (matchingFamilies.Count != 1 || matchingFamilies[0].ProductFamily?.Id is not int familyId)
        {
            throw new MaxioIntegrationException(
                MaxioFailureKind.Configuration,
                "The configured Maxio product family is unavailable or ambiguous.");
        }

        var result = new List<MaxioPlan>();
        for (var page = 1; ; page++)
        {
            IReadOnlyList<ProductResponse> responses;
            try
            {
                responses = await ExecuteAsync(
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
                        perPage: ProductsPerPage,
                        ct: ct),
                    writeOnce: false,
                    cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> exception)
            {
                if (exception.Error.TryGetString(out _))
                {
                    throw new MaxioIntegrationException(
                        MaxioFailureKind.Configuration,
                        "The configured Maxio product family could not be read.",
                        exception,
                        HttpStatusCode.NotFound);
                }

                if (exception.Error.TryGetRawError(out var raw))
                {
                    throw FromRawError("list subscription plans", raw, exception);
                }

                throw InvalidProviderError("list subscription plans", exception);
            }

            foreach (var response in responses)
            {
                var product = response.Product;
                if (product.ArchivedAt is not null || product.RequireCreditCard != false ||
                    string.IsNullOrWhiteSpace(product.Handle) || string.IsNullOrWhiteSpace(product.Name) ||
                    product.PriceInCents is not long priceInCents || product.Interval is not int interval ||
                    product.IntervalUnit is null)
                {
                    continue;
                }

                result.Add(new MaxioPlan(
                    product.Handle,
                    product.Name,
                    product.Description,
                    priceInCents,
                    interval,
                    product.IntervalUnit.Value));
            }

            if (responses.Count < ProductsPerPage)
            {
                break;
            }
        }

        return result;
    }

    public async Task<MaxioCustomer?> FindCustomerAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await ExecuteAsync(
                ct => _client.Customers.ReadCustomerByReference(reference, ct: ct),
                writeOnce: false,
                cancellationToken);
            return MapCustomer(response.Customer);
        }
        catch (SdkException<RawError> exception) when (exception.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> exception)
        {
            throw FromRawError("find customer", exception.Error, exception);
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCustomerProfile profile,
        string reference,
        CancellationToken cancellationToken)
    {
        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = profile.FirstName,
                LastName = profile.LastName,
                Email = profile.Email,
                Reference = reference
            }
        };

        try
        {
            var response = await ExecuteAsync(
                ct => _client.Customers.CreateCustomer(request, ct: ct),
                writeOnce: true,
                cancellationToken);
            return MapCustomer(response.Customer);
        }
        catch (SdkException<CreateCustomerError> exception)
        {
            if (exception.Error.TryGetCustomerErrorResponse1(out _))
            {
                var existing = await FindCustomerAsync(reference, cancellationToken);
                if (existing is not null)
                {
                    return existing;
                }

                throw new MaxioIntegrationException(
                    MaxioFailureKind.Validation,
                    "Maxio rejected the customer profile.",
                    exception,
                    HttpStatusCode.UnprocessableEntity);
            }

            if (exception.Error.TryGetRawError(out var raw))
            {
                throw FromRawError("create customer", raw, exception);
            }

            throw InvalidProviderError("create customer", exception);
        }
    }

    public async Task<MaxioSubscription?> FindSubscriptionAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await ExecuteAsync(
                ct => _client.Subscriptions.FindSubscription(reference, ct: ct),
                writeOnce: false,
                cancellationToken);
            return MapSubscription(response.Subscription);
        }
        catch (SdkException<FindSubscriptionError> exception)
        {
            if (exception.Error.TryGetNoContent(out var notFound) &&
                notFound.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (exception.Error.TryGetRawError(out var raw))
            {
                throw FromRawError("find subscription", raw, exception);
            }

            throw InvalidProviderError("find subscription", exception);
        }
    }

    public async Task<MaxioSubscriptionCreateResult> CreateSubscriptionAsync(
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
            var response = await ExecuteAsync(
                ct => _client.Subscriptions.CreateSubscription(request, ct: ct),
                writeOnce: true,
                cancellationToken);
            return new MaxioSubscriptionCreateResult(MapSubscription(response.Subscription), Created: true);
        }
        catch (SdkException<CreateSubscriptionError> exception)
        {
            if (exception.Error.TryGetErrorListResponse1(out var validation))
            {
                var validationReasons = string.Join(
                    " | ",
                    validation.Errors
                        .Where(reason => !string.IsNullOrWhiteSpace(reason))
                        .Take(10)
                        .Select(SanitizeValidationReason));
                _logger.LogWarning(
                    "Maxio rejected subscription creation with {ErrorCount} validation errors. " +
                    "Validation reasons: {ValidationReasons}",
                    validation.Errors.Count,
                    string.IsNullOrEmpty(validationReasons) ? "unspecified" : validationReasons);
                var existing = await FindSubscriptionAsync(subscriptionReference, cancellationToken);
                if (existing is not null)
                {
                    return new MaxioSubscriptionCreateResult(existing, Created: false);
                }

                throw new MaxioIntegrationException(
                    MaxioFailureKind.Validation,
                    "Maxio rejected the subscription request.",
                    exception,
                    HttpStatusCode.UnprocessableEntity);
            }

            if (exception.Error.TryGetRawError(out var raw))
            {
                throw FromRawError("create subscription", raw, exception);
            }

            throw InvalidProviderError("create subscription", exception);
        }
    }

    public async Task<MaxioSubscription> ReadSubscriptionAsync(
        int subscriptionId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await ExecuteAsync(
                ct => _client.Subscriptions.ReadSubscription(subscriptionId, include: null, ct: ct),
                writeOnce: false,
                cancellationToken);
            return MapSubscription(response.Subscription);
        }
        catch (SdkException<RawError> exception)
        {
            throw FromRawError("read subscription", exception.Error, exception);
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var responses = await ExecuteAsync(
                ct => _client.Customers.ListCustomerSubscriptions(customerId, ct: ct),
                writeOnce: false,
                cancellationToken);

            return responses
                .Where(response => string.Equals(
                    response.Subscription?.Product?.ProductFamily?.Handle,
                    _options.ProductFamilyHandle,
                    StringComparison.Ordinal))
                .Select(response => MapSubscription(response.Subscription))
                .ToList();
        }
        catch (SdkException<RawError> exception)
        {
            throw FromRawError("list customer subscriptions", exception.Error, exception);
        }
    }

    private static MaxioCustomer MapCustomer(Customer customer)
    {
        if (customer.Id is not int id || string.IsNullOrWhiteSpace(customer.Reference))
        {
            throw new MaxioIntegrationException(
                MaxioFailureKind.InvalidResponse,
                "Maxio returned an incomplete customer response.");
        }

        return new MaxioCustomer(id, customer.Reference);
    }

    private static MaxioSubscription MapSubscription(Subscription? subscription)
    {
        if (subscription?.Id is not int id || subscription.Product is not { } product ||
            string.IsNullOrWhiteSpace(product.Handle) || string.IsNullOrWhiteSpace(product.Name) ||
            subscription.State is null)
        {
            throw new MaxioIntegrationException(
                MaxioFailureKind.InvalidResponse,
                "Maxio returned an incomplete subscription response.");
        }

        return new MaxioSubscription(
            id,
            subscription.Reference,
            product.Handle,
            product.Name,
            subscription.ProductPriceInCents,
            subscription.CurrentBillingAmountInCents,
            subscription.State.Value,
            subscription.NextAssessmentAt);
    }

    private static MaxioIntegrationException FromRawError(
        string operation,
        RawError error,
        Exception exception)
    {
        var status = error.StatusCode;
        var kind = status is HttpStatusCode.BadRequest or HttpStatusCode.Conflict or
            HttpStatusCode.UnprocessableEntity
            ? MaxioFailureKind.Validation
            : MaxioFailureKind.Unavailable;
        return new MaxioIntegrationException(
            kind,
            kind == MaxioFailureKind.Validation
                ? "Maxio rejected the billing request."
                : "Maxio billing is temporarily unavailable.",
            exception,
            status);
    }

    private static MaxioIntegrationException InvalidProviderError(string operation, Exception exception)
    {
        return new MaxioIntegrationException(
            MaxioFailureKind.InvalidResponse,
            $"Maxio returned an unrecognized error while attempting to {operation}.",
            exception);
    }

    private static string SanitizeValidationReason(string reason)
    {
        var normalized = string.Concat(reason.Select(character =>
            char.IsControl(character) ? ' ' : character)).Trim();
        return normalized.Length <= 256 ? normalized : normalized[..256];
    }

    private static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> call,
        bool writeOnce,
        CancellationToken cancellationToken)
    {
        using var scope = MaxioCallScope.Begin(writeOnce);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(CallBudget);

        try
        {
            return await call(budget.Token);
        }
        catch (MaxioWriteReplayBlockedException exception)
        {
            throw new MaxioIntegrationException(
                MaxioFailureKind.AmbiguousWrite,
                "The Maxio write outcome is being reconciled.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new MaxioIntegrationException(
                writeOnce ? MaxioFailureKind.AmbiguousWrite : MaxioFailureKind.Unavailable,
                writeOnce
                    ? "The Maxio write outcome is being reconciled."
                    : "Maxio billing is temporarily unavailable.",
                exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MaxioIntegrationException(
                writeOnce ? MaxioFailureKind.AmbiguousWrite : MaxioFailureKind.Unavailable,
                writeOnce
                    ? "The Maxio write outcome is being reconciled."
                    : "Maxio billing timed out.",
                exception);
        }
        catch (JsonException exception)
        {
            var status = scope.State.LastStatusCode;
            if (status is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError)
            {
                throw new MaxioIntegrationException(
                    MaxioFailureKind.Validation,
                    "Maxio rejected the billing request.",
                    exception,
                    status);
            }

            throw new MaxioIntegrationException(
                writeOnce ? MaxioFailureKind.AmbiguousWrite : MaxioFailureKind.InvalidResponse,
                writeOnce
                    ? "The Maxio write outcome is being reconciled."
                    : "Maxio returned a response that could not be processed.",
                exception,
                status);
        }
    }
}
