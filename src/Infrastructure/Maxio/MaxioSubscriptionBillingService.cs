using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.Hooks;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

internal sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const int ProductsPerPage = 200;
    private const int MaximumProductPages = 10;
    private static readonly TimeSpan TotalCallBudget = TimeSpan.FromSeconds(20);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly SubscriptionProvisioningStore _provisioningStore;
    private readonly SubscriptionKeyedLock _keyedLock;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        SubscriptionProvisioningStore provisioningStore,
        SubscriptionKeyedLock keyedLock,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _provisioningStore = provisioningStore;
        _keyedLock = keyedLock;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        using var deadline = CreateDeadline(cancellationToken);
        try
        {
            var plans = new List<SubscriptionPlan>();
            var completed = false;
            for (var page = 1; page <= MaximumProductPages; page++)
            {
                var response = await ListProductPageAsync(page, deadline.Token);
                plans.AddRange(response.Select(MapPlan));

                if (response.Count < ProductsPerPage)
                {
                    completed = true;
                    break;
                }
            }

            if (!completed)
            {
                throw new BillingProviderException(
                    "The billing catalog is too large to return safely.",
                    BillingProviderFailure.InvalidResponse);
            }

            return plans
                .OrderBy(plan => plan.PriceInCents)
                .ThenBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (BillingProviderException exception)
        {
            LogFailure("ListPlans", null, exception);
            throw;
        }
    }

    public async Task<SubscriptionDetails> SubscribeAsync(
        BillingUser user,
        string planHandle,
        CancellationToken cancellationToken)
    {
        ValidateUser(user);
        var requestedHandle = ValidatePlanHandle(planHandle);
        var customerReference = BuildReference("eshop-c", user.Id);
        var subscriptionReference = BuildReference(
            "eshop-s",
            $"{user.Id}\n{requestedHandle.ToUpperInvariant()}");

        using var deadline = CreateDeadline(cancellationToken);
        using var subscriptionLock = await _keyedLock.AcquireAsync(subscriptionReference, deadline.Token);

        try
        {
            var existing = await FindSubscriptionAsync(subscriptionReference, deadline.Token);
            if (existing is not null)
            {
                return MapSubscription(existing, requireNextBillingDate: false);
            }

            var product = await ReadEligibleProductAsync(requestedHandle, deadline.Token);
            var canonicalHandle = product.Handle!;
            var claim = await _provisioningStore.TryAcquireAsync(
                customerReference,
                canonicalHandle.ToUpperInvariant(),
                subscriptionReference,
                deadline.Token);

            if (!claim.Acquired)
            {
                existing = await FindSubscriptionAsync(subscriptionReference, deadline.Token);
                if (existing is not null)
                {
                    return MapSubscription(existing, requireNextBillingDate: false);
                }

                throw new SubscriptionProvisioningInProgressException();
            }

            var collectionMethod = await GetNonCardCollectionMethodAsync(deadline.Token);
            var customer = await EnsureCustomerAsync(user, customerReference, deadline.Token);
            var customerId = customer.Id ?? throw InvalidProviderResponse();

            return await CreateSubscriptionAsync(
                canonicalHandle,
                customerId,
                collectionMethod,
                subscriptionReference,
                claim.LeaseToken!,
                deadline.Token);
        }
        catch (BillingProviderException exception)
        {
            LogFailure("Subscribe", subscriptionReference, exception);
            throw;
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(
        BillingUser user,
        CancellationToken cancellationToken)
    {
        ValidateUser(user);
        var customerReference = BuildReference("eshop-c", user.Id);
        using var deadline = CreateDeadline(cancellationToken);

        try
        {
            var customer = await FindCustomerAsync(customerReference, deadline.Token);
            if (customer is null)
            {
                return Array.Empty<SubscriptionDetails>();
            }

            var customerId = customer.Id ?? throw InvalidProviderResponse();
            var observation = new ResponseObservation();
            try
            {
                var responses = await _client.Customers.ListCustomerSubscriptions(
                    customerId,
                    requestOptions: observation.RequestOptions,
                    ct: deadline.Token);

                return responses
                    .Select(response => response.Subscription ?? throw InvalidProviderResponse())
                    .Select(subscription => MapSubscription(subscription, requireNextBillingDate: false))
                    .OrderByDescending(subscription => subscription.Id)
                    .ToArray();
            }
            catch (SdkException<RawError> exception)
            {
                throw FromStatus(exception.Error.StatusCode, exception);
            }
            catch (JsonException exception)
            {
                throw FromJsonFailure(observation.StatusCode, writeOperation: false, exception);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                throw ProviderUnavailable(exception);
            }
        }
        catch (BillingProviderException exception)
        {
            LogFailure("ListSubscriptions", customerReference, exception);
            throw;
        }
    }

    private async Task<IReadOnlyList<ProductResponse>> ListProductPageAsync(
        int page,
        CancellationToken cancellationToken)
    {
        var observation = new ResponseObservation();
        try
        {
            return await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: $"handle:{_settings.ProductFamilyHandle}",
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
                requestOptions: observation.RequestOptions,
                ct: cancellationToken);
        }
        catch (SdkException<ListProductsForProductFamilyError> exception)
        {
            if (exception.Error.TryGetString(out _))
            {
                throw new BillingProviderException(
                    "The configured billing catalog is unavailable.",
                    BillingProviderFailure.Unavailable,
                    HttpStatusCode.NotFound,
                    exception);
            }

            if (exception.Error.TryGetRawError(out var raw))
            {
                throw FromStatus(raw.StatusCode, exception);
            }

            throw InvalidProviderResponse(exception);
        }
        catch (JsonException exception)
        {
            throw FromJsonFailure(observation.StatusCode, writeOperation: false, exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw ProviderUnavailable(exception);
        }
    }

    private SubscriptionPlan MapPlan(ProductResponse response)
    {
        var product = response.Product;
        if (product.ArchivedAt.HasValue)
        {
            throw InvalidProviderResponse();
        }

        return new SubscriptionPlan(
            product.Handle ?? throw InvalidProviderResponse(),
            product.Name ?? throw InvalidProviderResponse(),
            product.Description,
            product.PriceInCents ?? throw InvalidProviderResponse(),
            product.Interval ?? throw InvalidProviderResponse(),
            product.IntervalUnit?.Value ?? throw InvalidProviderResponse(),
            product.RequireCreditCard ?? false);
    }

    private async Task<Product> ReadEligibleProductAsync(
        string planHandle,
        CancellationToken cancellationToken)
    {
        var observation = new ResponseObservation();
        try
        {
            var response = await _client.Products.ReadProductByHandle(
                planHandle,
                requestOptions: observation.RequestOptions,
                ct: cancellationToken);
            var product = response.Product;

            if (product.ArchivedAt.HasValue
                || !string.Equals(
                    product.ProductFamily?.Handle,
                    _settings.ProductFamilyHandle,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new BillingPlanNotFoundException(planHandle);
            }

            _ = MapPlan(response);
            return product;
        }
        catch (SdkException<RawError> exception) when (exception.Error.StatusCode == HttpStatusCode.NotFound)
        {
            throw new BillingPlanNotFoundException(planHandle);
        }
        catch (SdkException<RawError> exception)
        {
            throw FromStatus(exception.Error.StatusCode, exception);
        }
        catch (JsonException exception)
        {
            throw FromJsonFailure(observation.StatusCode, writeOperation: false, exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw ProviderUnavailable(exception);
        }
    }

    private async Task<Customer> EnsureCustomerAsync(
        BillingUser user,
        string customerReference,
        CancellationToken cancellationToken)
    {
        var existing = await FindCustomerAsync(customerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var localPart = user.Email.Split('@', 2)[0];
        var firstName = string.IsNullOrWhiteSpace(localPart)
            ? "eShop"
            : localPart[..Math.Min(localPart.Length, 50)];
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = "eShop customer",
                Email = user.Email,
                Reference = customerReference
            }
        };

        var observation = new ResponseObservation();
        BillingProviderException failure;
        try
        {
            var created = await _client.Customers.CreateCustomer(
                body,
                requestOptions: observation.RequestOptions,
                ct: cancellationToken);
            return created.Customer;
        }
        catch (SdkException<CreateCustomerError> exception)
        {
            if (exception.Error.TryGetCustomerErrorResponse1(out _))
            {
                failure = FromStatus(HttpStatusCode.UnprocessableEntity, exception);
            }
            else if (exception.Error.TryGetRawError(out var raw))
            {
                failure = FromStatus(raw.StatusCode, exception);
            }
            else
            {
                failure = InvalidProviderResponse(exception);
            }
        }
        catch (JsonException exception)
        {
            failure = FromJsonFailure(observation.StatusCode, writeOperation: true, exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            failure = UnknownWriteOutcome(exception);
        }

        var reconciled = await FindCustomerAsync(customerReference, cancellationToken);
        return reconciled ?? throw failure;
    }

    private async Task<CollectionMethod> GetNonCardCollectionMethodAsync(
        CancellationToken cancellationToken)
    {
        var observation = new ResponseObservation();
        try
        {
            var response = await _client.Sites.ReadSite(
                requestOptions: observation.RequestOptions,
                ct: cancellationToken);
            var relationshipInvoicingEnabled = response.Site.RelationshipInvoicingEnabled
                ?? throw InvalidProviderResponse();
            return relationshipInvoicingEnabled
                ? CollectionMethod.Remittance
                : CollectionMethod.Invoice;
        }
        catch (SdkException<RawError> exception)
        {
            throw FromStatus(exception.Error.StatusCode, exception);
        }
        catch (JsonException exception)
        {
            throw FromJsonFailure(observation.StatusCode, writeOperation: false, exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw ProviderUnavailable(exception);
        }
    }

    private async Task<Customer?> FindCustomerAsync(
        string customerReference,
        CancellationToken cancellationToken)
    {
        var observation = new ResponseObservation();
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                customerReference,
                requestOptions: observation.RequestOptions,
                ct: cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> exception) when (exception.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> exception)
        {
            throw FromStatus(exception.Error.StatusCode, exception);
        }
        catch (JsonException exception)
        {
            throw FromJsonFailure(observation.StatusCode, writeOperation: false, exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw ProviderUnavailable(exception);
        }
    }

    private async Task<Subscription?> FindSubscriptionAsync(
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var observation = new ResponseObservation();
        try
        {
            var response = await _client.Subscriptions.FindSubscription(
                subscriptionReference,
                requestOptions: observation.RequestOptions,
                ct: cancellationToken);
            return response.Subscription ?? throw InvalidProviderResponse();
        }
        catch (SdkException<FindSubscriptionError> exception)
        {
            if (exception.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (exception.Error.TryGetRawError(out var raw))
            {
                throw FromStatus(raw.StatusCode, exception);
            }

            throw InvalidProviderResponse(exception);
        }
        catch (JsonException exception)
        {
            throw FromJsonFailure(observation.StatusCode, writeOperation: false, exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw ProviderUnavailable(exception);
        }
    }

    private async Task<SubscriptionDetails> CreateSubscriptionAsync(
        string productHandle,
        int customerId,
        CollectionMethod collectionMethod,
        string subscriptionReference,
        string leaseToken,
        CancellationToken cancellationToken)
    {
        var request = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                PaymentCollectionMethod = collectionMethod,
                Reference = subscriptionReference
            }
        };

        var observation = new ResponseObservation();
        BillingProviderException failure;
        try
        {
            var response = await _client.Subscriptions.CreateSubscription(
                request,
                requestOptions: observation.RequestOptions,
                ct: cancellationToken);
            var subscription = response.Subscription ?? throw InvalidProviderResponse();
            var details = MapSubscription(subscription, requireNextBillingDate: true);
            await _provisioningStore.MarkCompletedAsync(
                subscriptionReference,
                leaseToken,
                details.Id,
                cancellationToken);
            return details;
        }
        catch (SdkException<CreateSubscriptionError> exception)
        {
            if (exception.Error.TryGetErrorListResponse1(out _))
            {
                failure = FromStatus(HttpStatusCode.UnprocessableEntity, exception);
            }
            else if (exception.Error.TryGetRawError(out var raw))
            {
                failure = FromStatus(raw.StatusCode, exception);
            }
            else
            {
                failure = InvalidProviderResponse(exception);
            }
        }
        catch (JsonException exception)
        {
            failure = FromJsonFailure(observation.StatusCode, writeOperation: true, exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            failure = UnknownWriteOutcome(exception);
        }

        var reconciled = await FindSubscriptionAsync(subscriptionReference, cancellationToken);
        if (reconciled is not null)
        {
            var details = MapSubscription(reconciled, requireNextBillingDate: false);
            await _provisioningStore.MarkCompletedAsync(
                subscriptionReference,
                leaseToken,
                details.Id,
                cancellationToken);
            return details;
        }

        if (failure.Failure == BillingProviderFailure.Rejected)
        {
            await _provisioningStore.ReleaseAsync(
                subscriptionReference,
                leaseToken,
                cancellationToken);
        }

        throw failure;
    }

    private static SubscriptionDetails MapSubscription(
        Subscription subscription,
        bool requireNextBillingDate)
    {
        var product = subscription.Product ?? throw InvalidProviderResponse();
        var nextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt;
        if (requireNextBillingDate && !nextBillingAt.HasValue)
        {
            throw InvalidProviderResponse();
        }

        return new SubscriptionDetails(
            subscription.Id ?? throw InvalidProviderResponse(),
            subscription.Reference ?? throw InvalidProviderResponse(),
            product.Handle ?? throw InvalidProviderResponse(),
            product.Name ?? throw InvalidProviderResponse(),
            subscription.ProductPriceInCents ?? product.PriceInCents ?? throw InvalidProviderResponse(),
            subscription.Currency,
            subscription.State?.Value ?? throw InvalidProviderResponse(),
            nextBillingAt,
            subscription.ProductPricePointId,
            product.ProductPricePointHandle,
            product.ProductPricePointName);
    }

    private static string ValidatePlanHandle(string planHandle)
    {
        var value = planHandle?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 100)
        {
            throw new ArgumentException("A planHandle of 1 to 100 characters is required.", nameof(planHandle));
        }

        return value;
    }

    private static void ValidateUser(BillingUser user)
    {
        if (string.IsNullOrWhiteSpace(user.Id) || string.IsNullOrWhiteSpace(user.Email))
        {
            throw new ArgumentException("A valid billing user is required.", nameof(user));
        }
    }

    private static string BuildReference(string prefix, string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"{prefix}-{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static CancellationTokenSource CreateDeadline(CancellationToken cancellationToken)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TotalCallBudget);
        return deadline;
    }

    private static BillingProviderException FromStatus(HttpStatusCode statusCode, Exception exception)
    {
        return statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new BillingProviderException(
                "Subscription billing is unavailable.",
                BillingProviderFailure.Unavailable,
                statusCode,
                exception),
            HttpStatusCode.TooManyRequests => new BillingProviderException(
                "Subscription billing is temporarily busy.",
                BillingProviderFailure.RateLimited,
                statusCode,
                exception),
            >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError =>
                new BillingProviderException(
                    "The billing provider rejected the request.",
                    BillingProviderFailure.Rejected,
                    statusCode,
                    exception),
            _ => ProviderUnavailable(exception, statusCode)
        };
    }

    private static BillingProviderException FromJsonFailure(
        HttpStatusCode? observedStatus,
        bool writeOperation,
        JsonException exception)
    {
        if (observedStatus is >= HttpStatusCode.BadRequest)
        {
            return FromStatus(observedStatus.Value, exception);
        }

        return new BillingProviderException(
            "The billing provider returned a response that could not be processed.",
            writeOperation ? BillingProviderFailure.UnknownOutcome : BillingProviderFailure.InvalidResponse,
            observedStatus,
            exception);
    }

    private static BillingProviderException ProviderUnavailable(
        Exception exception,
        HttpStatusCode? statusCode = null)
    {
        return new BillingProviderException(
            "Subscription billing is unavailable.",
            BillingProviderFailure.Unavailable,
            statusCode,
            exception);
    }

    private static BillingProviderException UnknownWriteOutcome(Exception exception)
    {
        return new BillingProviderException(
            "The billing provider did not confirm the subscription outcome. Retry safely to reconcile it.",
            BillingProviderFailure.UnknownOutcome,
            null,
            exception);
    }

    private static BillingProviderException InvalidProviderResponse(Exception? exception = null)
    {
        return new BillingProviderException(
            "The billing provider returned an incomplete response.",
            BillingProviderFailure.InvalidResponse,
            null,
            exception);
    }

    private void LogFailure(
        string operation,
        string? reference,
        BillingProviderException exception)
    {
        _logger.LogWarning(
            "Maxio billing operation {Operation} failed. Failure={Failure}; ProviderStatus={ProviderStatus}; Reference={Reference}",
            operation,
            exception.Failure,
            exception.ProviderStatusCode.HasValue ? (int)exception.ProviderStatusCode.Value : null,
            reference);
    }

    private sealed class ResponseObservation
    {
        public ResponseObservation()
        {
            RequestOptions = new RequestOptions
            {
                Hooks =
                [
                    SdkHook.OnResponse((response, _) => StatusCode = response.StatusCode)
                ]
            };
        }

        public HttpStatusCode? StatusCode { get; private set; }

        public RequestOptions RequestOptions { get; }
    }
}
