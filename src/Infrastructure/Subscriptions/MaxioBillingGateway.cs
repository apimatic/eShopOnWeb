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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions;

public sealed class MaxioBillingGateway : ISubscriptionBillingGateway
{
    private const int PageSize = 20;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(25);
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;

    public MaxioBillingGateway(MaxioAdvancedBillingClient client, MaxioOptions options)
    {
        _client = client;
        _options = options;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var plans = new List<SubscriptionPlan>();
        for (var page = 1; ; page++)
        {
            using var scope = MaxioRequestScope.Begin(write: false);
            using var cts = CreateCallToken(cancellationToken);
            IReadOnlyList<ProductResponse> response;
            try
            {
                response = await _client.Products.ListProducts(
                    dateField: null,
                    filter: null,
                    endDate: null,
                    endDatetime: null,
                    startDate: null,
                    startDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: page,
                    perPage: PageSize,
                    ct: cts.Token);
            }
            catch (SdkException<RawError> exception)
            {
                throw FromRawError(exception.Error, exception);
            }
            catch (Exception exception) when (IsBoundaryException(exception))
            {
                throw FromBoundaryException(exception, scope, write: false, cancellationToken);
            }

            plans.AddRange(response
                .Select(item => item.Product)
                .Where(IsAvailableConfiguredPlan)
                .Select(MapPlan));

            if (response.Count < PageSize)
            {
                return plans;
            }
        }
    }

    public async Task<SubscriptionPlan?> GetPlanAsync(
        string productHandle,
        CancellationToken cancellationToken)
    {
        using var scope = MaxioRequestScope.Begin(write: false);
        using var cts = CreateCallToken(cancellationToken);
        try
        {
            var response = await _client.Products.ReadProductByHandle(productHandle, ct: cts.Token);
            return IsAvailableConfiguredPlan(response.Product) ? MapPlan(response.Product) : null;
        }
        catch (SdkException<RawError> exception) when (exception.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> exception)
        {
            throw FromRawError(exception.Error, exception);
        }
        catch (Exception exception) when (IsBoundaryException(exception))
        {
            throw FromBoundaryException(exception, scope, write: false, cancellationToken);
        }
    }

    public async Task EnsureCustomerAsync(
        SubscriptionCustomer customer,
        CancellationToken cancellationToken)
    {
        if (await CustomerExistsAsync(customer.Reference, cancellationToken))
        {
            return;
        }

        using var scope = MaxioRequestScope.Begin(write: true);
        using var cts = CreateCallToken(cancellationToken);
        try
        {
            await _client.Customers.CreateCustomer(
                new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = customer.FirstName,
                        LastName = customer.LastName,
                        Email = customer.Email,
                        Reference = customer.Reference
                    }
                },
                ct: cts.Token);
        }
        catch (SdkException<CreateCustomerError> exception)
        {
            if (exception.Error.TryGetCustomerErrorResponse1(out _))
            {
                if (await CustomerExistsAsync(customer.Reference, cancellationToken))
                {
                    return;
                }
                throw new SubscriptionBillingException(
                    "Maxio rejected the customer profile.",
                    providerStatusCode: 422,
                    innerException: exception);
            }

            if (exception.Error.TryGetRawError(out var raw))
            {
                throw FromRawError(raw, exception);
            }

            throw new SubscriptionBillingException("Maxio rejected the customer profile.", innerException: exception);
        }
        catch (Exception exception) when (IsBoundaryException(exception))
        {
            if (await CustomerExistsAfterFailureAsync(customer.Reference, cancellationToken))
            {
                return;
            }
            throw FromBoundaryException(exception, scope, write: true, cancellationToken);
        }
    }

    public async Task<SubscriptionDetails?> FindSubscriptionAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        using var scope = MaxioRequestScope.Begin(write: false);
        using var cts = CreateCallToken(cancellationToken);
        try
        {
            var response = await _client.Subscriptions.FindSubscription(reference, ct: cts.Token);
            return response.Subscription is null ? null : MapSubscription(response.Subscription);
        }
        catch (SdkException<FindSubscriptionError> exception)
        {
            if (exception.Error.TryGetNoContent(out _))
            {
                return null;
            }
            if (exception.Error.TryGetRawError(out var raw))
            {
                throw FromRawError(raw, exception);
            }
            throw new SubscriptionBillingException("Maxio could not find the subscription.", innerException: exception);
        }
        catch (Exception exception) when (IsBoundaryException(exception))
        {
            throw FromBoundaryException(exception, scope, write: false, cancellationToken);
        }
    }

    public async Task<SubscriptionDetails> CreateSubscriptionAsync(
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        using var scope = MaxioRequestScope.Begin(write: true);
        using var cts = CreateCallToken(cancellationToken);
        try
        {
            var response = await _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = productHandle,
                        CustomerReference = customerReference,
                        Reference = subscriptionReference
                    }
                },
                ct: cts.Token);

            if (response.Subscription is null)
            {
                throw new SubscriptionBillingException(
                    "Maxio returned an incomplete subscription response.",
                    outcomeUnknown: true);
            }
            return MapSubscription(response.Subscription, outcomeUnknown: true);
        }
        catch (SdkException<CreateSubscriptionError> exception)
        {
            if (exception.Error.TryGetErrorListResponse1(out var errorResponse))
            {
                throw new SubscriptionBillingException(
                    BuildSafeProviderMessage(
                        "Maxio rejected the subscription request.",
                        errorResponse.Errors),
                    providerStatusCode: 422,
                    innerException: exception);
            }
            if (exception.Error.TryGetRawError(out var raw))
            {
                throw FromRawError(raw, exception);
            }
            throw new SubscriptionBillingException("Maxio rejected the subscription request.", innerException: exception);
        }
        catch (SubscriptionBillingException)
        {
            throw;
        }
        catch (Exception exception) when (IsBoundaryException(exception))
        {
            throw FromBoundaryException(exception, scope, write: true, cancellationToken);
        }
    }

    private async Task<bool> CustomerExistsAsync(string reference, CancellationToken cancellationToken)
    {
        using var scope = MaxioRequestScope.Begin(write: false);
        using var cts = CreateCallToken(cancellationToken);
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: cts.Token);
            return string.Equals(response.Customer.Reference, reference, StringComparison.Ordinal);
        }
        catch (SdkException<RawError> exception) when (exception.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (SdkException<RawError> exception)
        {
            throw FromRawError(exception.Error, exception);
        }
        catch (Exception exception) when (IsBoundaryException(exception))
        {
            throw FromBoundaryException(exception, scope, write: false, cancellationToken);
        }
    }

    private async Task<bool> CustomerExistsAfterFailureAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        try
        {
            return await CustomerExistsAsync(reference, cancellationToken);
        }
        catch (SubscriptionBillingException)
        {
            return false;
        }
    }

    private bool IsAvailableConfiguredPlan(Product product) =>
        product.ArchivedAt is null &&
        product.RequireCreditCard != true &&
        string.Equals(product.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(product.Handle) &&
        !string.IsNullOrWhiteSpace(product.Name) &&
        product.PriceInCents.HasValue;

    private static SubscriptionPlan MapPlan(Product product) =>
        new(
            product.Handle!,
            product.Name!,
            product.Description,
            product.PriceInCents!.Value,
            product.Interval,
            product.IntervalUnit?.Value);

    private static SubscriptionDetails MapSubscription(Subscription subscription, bool outcomeUnknown = false)
    {
        if (string.IsNullOrWhiteSpace(subscription.Reference) ||
            string.IsNullOrWhiteSpace(subscription.Product?.Handle) ||
            string.IsNullOrWhiteSpace(subscription.Product.Name) ||
            !subscription.ProductPriceInCents.HasValue ||
            subscription.State is null)
        {
            throw new SubscriptionBillingException(
                "Maxio returned an incomplete subscription response.",
                outcomeUnknown: outcomeUnknown);
        }

        return new SubscriptionDetails(
            subscription.Reference,
            subscription.Product.Handle,
            subscription.Product.Name,
            subscription.ProductPriceInCents.Value,
            subscription.Currency,
            subscription.State.Value,
            subscription.NextAssessmentAt);
    }

    private static CancellationTokenSource CreateCallToken(CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return cts;
    }

    private static bool IsBoundaryException(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or JsonException or MaxioWriteRetryBlockedException;

    private static SubscriptionBillingException FromBoundaryException(
        Exception exception,
        MaxioRequestScope scope,
        bool write,
        CancellationToken callerToken)
    {
        if (exception is TaskCanceledException && callerToken.IsCancellationRequested)
        {
            return new SubscriptionBillingException("The billing request was canceled.", innerException: exception);
        }

        var status = scope.LastStatusCode;
        if (exception is JsonException && status is >= HttpStatusCode.BadRequest)
        {
            return new SubscriptionBillingException(
                "Maxio rejected the request, but its error response could not be processed.",
                (int)status.Value,
                outcomeUnknown: false,
                innerException: exception);
        }

        return new SubscriptionBillingException(
            write
                ? "The Maxio write outcome is unknown and must be reconciled."
                : "Maxio is currently unavailable.",
            outcomeUnknown: write,
            innerException: exception);
    }

    private static SubscriptionBillingException FromRawError(RawError error, Exception exception) =>
        new(
            (int)error.StatusCode >= 500
                ? "Maxio is currently unavailable."
                : "Maxio rejected the billing request.",
            providerStatusCode: (int)error.StatusCode,
            innerException: exception);

    private static string BuildSafeProviderMessage(
        string fallback,
        IReadOnlyList<string> errors)
    {
        const int maxDetails = 3;
        const int maxDetailLength = 300;
        string[] sensitiveTerms =
        [
            "api key",
            "apikey",
            "authorization",
            "basic ",
            "bearer ",
            "password",
            "secret",
            "token"
        ];

        var details = errors
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Select(message => new string(message
                .Where(character => !char.IsControl(character))
                .ToArray())
                .Trim())
            .Where(message => message.Length > 0)
            .Where(message => !sensitiveTerms.Any(term =>
                message.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .Select(message => message.Length <= maxDetailLength
                ? message
                : string.Concat(message.AsSpan(0, maxDetailLength), "…"))
            .Take(maxDetails)
            .ToArray();

        return details.Length == 0
            ? fallback
            : string.Concat(fallback, " ", string.Join(" ", details));
    }
}
