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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal sealed class MaxioBillingGateway : IMaxioBillingGateway
{
    private const int PageSize = 100;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(25);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly MaxioRequestContext _requestContext;

    public MaxioBillingGateway(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        MaxioRequestContext requestContext)
    {
        _client = client;
        _options = options.Value;
        _requestContext = requestContext;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await CallAsync(
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
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error, "Maxio could not return product families.", ex);
        }

        var family = families
            .Select(item => item.ProductFamily)
            .SingleOrDefault(item => item is not null &&
                                     item.ArchivedAt is null &&
                                     string.Equals(item.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal));
        if (family?.Id is null)
        {
            throw new MaxioProviderException(
                MaxioFailureKind.ProviderResponse,
                "The configured Maxio product family is unavailable.",
                HttpStatusCode.ServiceUnavailable);
        }

        var plans = new List<SubscriptionPlanDto>();
        for (var page = 1; ; page++)
        {
            IReadOnlyList<ProductResponse> response;
            try
            {
                response = await CallAsync(
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
                        perPage: PageSize,
                        ct: ct),
                    writeOnce: false,
                    cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out _))
                {
                    throw new MaxioProviderException(
                        MaxioFailureKind.ProviderResponse,
                        "The configured Maxio product family is unavailable.",
                        HttpStatusCode.ServiceUnavailable,
                        ex);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw FromRaw(raw, "Maxio could not return subscription plans.", ex);
                }

                throw MalformedError(ex);
            }

            plans.AddRange(response
                .Select(item => item.Product)
                .Where(product => product.ArchivedAt is null &&
                                  !string.IsNullOrWhiteSpace(product.Handle) &&
                                  product.PriceInCents.HasValue)
                .Select(product => new SubscriptionPlanDto(
                    product.Handle!,
                    product.Name ?? product.Handle!,
                    product.Description,
                    product.PriceInCents!.Value,
                    product.Interval,
                    product.IntervalUnit?.Value,
                    product.RequireCreditCard ?? false)));

            if (response.Count < PageSize) break;
        }

        return plans;
    }

    public async Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await CallAsync(
                ct => _client.Customers.ReadCustomerByReference(reference, ct: ct),
                writeOnce: false,
                cancellationToken);
            var customer = response.Customer;
            if (customer.Id is null || !string.Equals(customer.Reference, reference, StringComparison.Ordinal))
            {
                throw MalformedSuccess();
            }

            return new MaxioCustomer(customer.Id.Value, customer.Reference!);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error, "Maxio could not look up the customer.", ex);
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(BillingUser user, string reference, CancellationToken cancellationToken)
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
            var response = await CallAsync(
                ct => _client.Customers.CreateCustomer(body: request, ct: ct),
                writeOnce: true,
                cancellationToken);
            var customer = response.Customer;
            if (customer.Id is null || !string.Equals(customer.Reference, reference, StringComparison.Ordinal))
            {
                throw MalformedSuccess();
            }

            return new MaxioCustomer(customer.Id.Value, customer.Reference!);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                var existing = await FindCustomerAsync(reference, cancellationToken);
                if (existing is not null) return existing;

                throw new MaxioProviderException(
                    MaxioFailureKind.ProviderResponse,
                    "Maxio rejected the customer profile.",
                    HttpStatusCode.UnprocessableEntity,
                    ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, "Maxio could not create the customer.", ex);
            }

            throw MalformedError(ex);
        }
        catch (MaxioProviderException ex) when (ex.Kind is MaxioFailureKind.AmbiguousWrite or MaxioFailureKind.Transport)
        {
            var existing = await FindCustomerAsync(reference, cancellationToken);
            if (existing is not null) return existing;
            throw;
        }
    }

    public async Task<SubscriptionDto?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await CallAsync(
                ct => _client.Subscriptions.FindSubscription(reference: reference, ct: ct),
                writeOnce: false,
                cancellationToken);
            return ProjectSubscription(response, reference);
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _)) return null;
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, "Maxio could not look up the subscription.", ex);
            }

            throw MalformedError(ex);
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(
        string customerReference,
        string productHandle,
        string reference,
        CancellationToken cancellationToken)
    {
        var request = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
        {
            Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerReference = customerReference,
                Reference = reference,
                PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance
            }
        };

        try
        {
            var response = await CallAsync(
                ct => _client.Subscriptions.CreateSubscription(body: request, ct: ct),
                writeOnce: true,
                cancellationToken);
            return ProjectSubscription(response, reference);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var error))
            {
                var details = string.Join(
                    "; ",
                    error.Errors
                        .Where(message => !string.IsNullOrWhiteSpace(message))
                        .Take(3)
                        .Select(message => message.Length <= 256 ? message : message[..256]));
                throw new MaxioProviderException(
                    MaxioFailureKind.ProviderResponse,
                    string.IsNullOrWhiteSpace(details)
                        ? "Maxio rejected the subscription request."
                        : $"Maxio rejected the subscription request: {details}",
                    HttpStatusCode.UnprocessableEntity,
                    ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, "Maxio could not create the subscription.", ex);
            }

            throw MalformedError(ex);
        }
        catch (MaxioProviderException ex) when (ex.Kind is MaxioFailureKind.AmbiguousWrite or MaxioFailureKind.Transport)
        {
            var existing = await FindSubscriptionAsync(reference, cancellationToken);
            if (existing is not null) return existing;
            throw;
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        try
        {
            var responses = await CallAsync(
                ct => _client.Customers.ListCustomerSubscriptions(customerId, ct: ct),
                writeOnce: false,
                cancellationToken);
            return responses.Select(response => ProjectSubscription(response, expectedReference: null)).ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error, "Maxio could not return the customer's subscriptions.", ex);
        }
    }

    private async Task<T> CallAsync<T>(
        Func<CancellationToken, Task<T>> call,
        bool writeOnce,
        CancellationToken cancellationToken)
    {
        using var scope = _requestContext.Begin(writeOnce);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(CallBudget);

        try
        {
            return await call(budget.Token);
        }
        catch (MaxioWriteAlreadyAttemptedException ex)
        {
            throw new MaxioProviderException(
                MaxioFailureKind.AmbiguousWrite,
                "The Maxio write outcome is being reconciled.",
                innerException: ex);
        }
        catch (JsonException ex)
        {
            var status = _requestContext.LastStatusCode;
            if (status is not null && (int)status >= 400)
            {
                throw new MaxioProviderException(
                    MaxioFailureKind.ProviderResponse,
                    "Maxio rejected the request, but its error details could not be processed.",
                    status,
                    ex);
            }

            throw new MaxioProviderException(
                MaxioFailureKind.MalformedResponse,
                "Maxio returned a response that could not be processed.",
                status,
                ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MaxioProviderException(
                writeOnce ? MaxioFailureKind.AmbiguousWrite : MaxioFailureKind.Transport,
                "Maxio did not respond before the request deadline.",
                innerException: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioProviderException(
                writeOnce ? MaxioFailureKind.AmbiguousWrite : MaxioFailureKind.Transport,
                "Maxio is temporarily unreachable.",
                innerException: ex);
        }
    }

    private static SubscriptionDto ProjectSubscription(SubscriptionResponse response, string? expectedReference)
    {
        var subscription = response.Subscription ?? throw MalformedSuccess();
        var product = subscription.Product;
        if (subscription.Id is null ||
            string.IsNullOrWhiteSpace(subscription.Reference) ||
            product is null ||
            string.IsNullOrWhiteSpace(product.Handle) ||
            (expectedReference is not null && !string.Equals(subscription.Reference, expectedReference, StringComparison.Ordinal)))
        {
            throw MalformedSuccess();
        }

        return new SubscriptionDto(
            subscription.Id.Value,
            subscription.Reference,
            product.Handle,
            product.Name ?? product.Handle,
            subscription.ProductPriceInCents ?? product.PriceInCents,
            subscription.State?.Value,
            subscription.NextAssessmentAt,
            subscription.CurrentPeriodEndsAt);
    }

    private static MaxioProviderException FromRaw(RawError raw, string message, Exception innerException) =>
        new(MaxioFailureKind.ProviderResponse, message, raw.StatusCode, innerException);

    private static MaxioProviderException MalformedSuccess() =>
        new(MaxioFailureKind.MalformedResponse, "Maxio returned an incomplete response.");

    private static MaxioProviderException MalformedError(Exception innerException) =>
        new(MaxioFailureKind.MalformedResponse, "Maxio returned an error that could not be processed.", innerException: innerException);
}
