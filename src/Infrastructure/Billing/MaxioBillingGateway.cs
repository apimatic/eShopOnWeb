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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioBillingGateway : IMaxioBillingGateway
{
    private const int PageSize = 20;
    private static readonly TimeSpan SandboxCacheDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan TotalCallBudget = TimeSpan.FromSeconds(30);
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly MaxioWriteGuard _writeGuard;
    private readonly ILogger<MaxioBillingGateway> _logger;
    private readonly SemaphoreSlim _siteGate = new(1, 1);
    private DateTimeOffset _siteVerifiedUntil;
    private string _currency = string.Empty;

    public MaxioBillingGateway(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        MaxioWriteGuard writeGuard,
        ILogger<MaxioBillingGateway> logger)
    {
        _client = client;
        _options = options.Value;
        _writeGuard = writeGuard;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken)
    {
        await EnsureSandboxAsync(cancellationToken);
        var plans = new List<SubscriptionPlan>();

        try
        {
            for (var page = 1; page <= 100; page++)
            {
                var responses = await BoundedAsync(
                    ct => _client.Products.ListProducts(
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
                        ct: ct),
                    cancellationToken);

                foreach (var response in responses)
                {
                    var product = response.Product;
                    if (product.ArchivedAt != null ||
                        product.Handle == null ||
                        !string.Equals(product.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (product.PriceInCents == null || product.Interval == null || product.IntervalUnit == null)
                    {
                        throw new BillingException(
                            BillingErrorKind.InvalidProviderResponse,
                            "Maxio returned an incomplete subscription plan.");
                    }

                    plans.Add(new SubscriptionPlan(
                        product.Handle,
                        product.Name ?? product.Handle,
                        product.Description,
                        product.PriceInCents.Value,
                        product.Interval.Value,
                        product.IntervalUnit.Value,
                        _currency));
                }

                if (responses.Count < PageSize)
                {
                    return plans;
                }
            }

            throw new BillingException(BillingErrorKind.InvalidProviderResponse, "Maxio product pagination did not terminate.");
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError(ex.Error, "Maxio could not list subscription plans.", ex);
        }
        catch (JsonException ex)
        {
            throw InvalidResponse(ex);
        }
        catch (HttpRequestException ex)
        {
            throw Unavailable(ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw Unavailable(ex);
        }
    }

    public async Task EnsureCustomerAsync(
        BillingUser user,
        string customerReference,
        CancellationToken cancellationToken)
    {
        await EnsureSandboxAsync(cancellationToken);
        if (await FindCustomerAsync(customerReference, cancellationToken))
        {
            return;
        }

        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                Reference = customerReference
            }
        };

        try
        {
            await EnsureSandboxAsync(cancellationToken);
            using (_writeGuard.Begin())
            {
                var response = await BoundedAsync(
                    ct => _client.Customers.CreateCustomer(request, ct: ct),
                    cancellationToken);
                if (!string.Equals(response.Customer.Reference, customerReference, StringComparison.Ordinal))
                {
                    throw new BillingException(BillingErrorKind.Conflict, "Maxio returned a different billing customer.");
                }
            }
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                if (await FindCustomerAsync(customerReference, cancellationToken))
                {
                    return;
                }

                throw new BillingException(BillingErrorKind.Validation, "Maxio rejected the billing customer profile.", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError(raw, "Maxio could not create the billing customer.", ex);
            }

            throw new BillingException(BillingErrorKind.Unavailable, "Maxio could not create the billing customer.", ex);
        }
        catch (BillingException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or MaxioWriteRetryBlockedException)
        {
            if (await FindCustomerAsync(customerReference, cancellationToken))
            {
                _logger.LogWarning("Recovered Maxio customer {CustomerReference} after an ambiguous create response.", customerReference);
                return;
            }

            throw Unavailable(ex);
        }
    }

    public async Task<SubscriptionConfirmation?> FindSubscriptionAsync(
        string subscriptionReference,
        string expectedCustomerReference,
        string expectedProductHandle,
        CancellationToken cancellationToken)
    {
        await EnsureSandboxAsync(cancellationToken);
        try
        {
            var response = await BoundedAsync(
                ct => _client.Subscriptions.FindSubscription(subscriptionReference, ct: ct),
                cancellationToken);
            if (response.Subscription == null)
            {
                throw new BillingException(BillingErrorKind.InvalidProviderResponse, "Maxio returned an incomplete subscription.");
            }

            return MapSubscription(
                response.Subscription,
                subscriptionReference,
                expectedCustomerReference,
                expectedProductHandle);
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError(raw, "Maxio could not read the subscription.", ex);
            }

            throw new BillingException(BillingErrorKind.Unavailable, "Maxio could not read the subscription.", ex);
        }
        catch (JsonException ex)
        {
            throw InvalidResponse(ex);
        }
        catch (HttpRequestException ex)
        {
            throw Unavailable(ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw Unavailable(ex);
        }
    }

    public async Task<SubscriptionConfirmation> CreateSubscriptionAsync(
        string subscriptionReference,
        string customerReference,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var existing = await FindSubscriptionAsync(
            subscriptionReference,
            customerReference,
            productHandle,
            cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var request = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerReference = customerReference,
                Reference = subscriptionReference,
                PaymentCollectionMethod = CollectionMethod.Invoice
            }
        };

        try
        {
            await EnsureSandboxAsync(cancellationToken);
            using (_writeGuard.Begin())
            {
                var response = await BoundedAsync(
                    ct => _client.Subscriptions.CreateSubscription(request, ct: ct),
                    cancellationToken);
                if (response.Subscription == null)
                {
                    throw new BillingException(BillingErrorKind.InvalidProviderResponse, "Maxio returned an incomplete subscription.");
                }

                return MapSubscription(
                    response.Subscription,
                    subscriptionReference,
                    customerReference,
                    productHandle);
            }
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var validation))
            {
                var recovered = await FindSubscriptionAsync(
                    subscriptionReference,
                    customerReference,
                    productHandle,
                    cancellationToken);
                if (recovered != null)
                {
                    return recovered;
                }

                throw new BillingException(
                    BillingErrorKind.Validation,
                    "Maxio rejected the subscription.",
                    ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError(raw, "Maxio could not create the subscription.", ex);
            }

            throw new BillingException(BillingErrorKind.Unavailable, "Maxio could not create the subscription.", ex);
        }
        catch (BillingException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or MaxioWriteRetryBlockedException)
        {
            var recovered = await FindSubscriptionAsync(
                subscriptionReference,
                customerReference,
                productHandle,
                cancellationToken);
            if (recovered != null)
            {
                _logger.LogWarning(
                    "Recovered Maxio subscription {SubscriptionReference} after an ambiguous create response.",
                    subscriptionReference);
                return recovered;
            }

            throw Unavailable(ex);
        }
    }

    private async Task<bool> FindCustomerAsync(string customerReference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                ct => _client.Customers.ReadCustomerByReference(customerReference, ct: ct),
                cancellationToken);
            if (!string.Equals(response.Customer.Reference, customerReference, StringComparison.Ordinal))
            {
                throw new BillingException(BillingErrorKind.Conflict, "Maxio returned a different billing customer.");
            }

            return true;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError(ex.Error, "Maxio could not read the billing customer.", ex);
        }
        catch (JsonException ex)
        {
            throw InvalidResponse(ex);
        }
        catch (HttpRequestException ex)
        {
            throw Unavailable(ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw Unavailable(ex);
        }
    }

    private async Task EnsureSandboxAsync(CancellationToken cancellationToken)
    {
        if (_siteVerifiedUntil > DateTimeOffset.UtcNow)
        {
            return;
        }

        await _siteGate.WaitAsync(cancellationToken);
        try
        {
            if (_siteVerifiedUntil > DateTimeOffset.UtcNow)
            {
                return;
            }

            try
            {
                var response = await BoundedAsync(ct => _client.Sites.ReadSite(ct: ct), cancellationToken);
                if (response.Site.Test != true)
                {
                    throw new BillingException(BillingErrorKind.SandboxRequired, "Maxio billing writes require a sandbox site.");
                }

                if (string.IsNullOrWhiteSpace(_options.BaseUrl) &&
                    !string.Equals(response.Site.Subdomain, _options.Subdomain, StringComparison.OrdinalIgnoreCase))
                {
                    throw new BillingException(BillingErrorKind.SandboxRequired, "The Maxio site identity did not match configuration.");
                }

                _currency = response.Site.Currency ?? string.Empty;
                _siteVerifiedUntil = DateTimeOffset.UtcNow + SandboxCacheDuration;
            }
            catch (SdkException<RawError> ex)
            {
                throw MapRawError(ex.Error, "The Maxio sandbox site could not be verified.", ex);
            }
            catch (JsonException ex)
            {
                throw InvalidResponse(ex);
            }
            catch (HttpRequestException ex)
            {
                throw Unavailable(ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw Unavailable(ex);
            }
        }
        finally
        {
            _siteGate.Release();
        }
    }

    private static SubscriptionConfirmation MapSubscription(
        Subscription subscription,
        string expectedReference,
        string expectedCustomerReference,
        string expectedProductHandle)
    {
        if (!string.Equals(subscription.Reference, expectedReference, StringComparison.Ordinal) ||
            !string.Equals(subscription.Customer?.Reference, expectedCustomerReference, StringComparison.Ordinal) ||
            !string.Equals(subscription.Product?.Handle, expectedProductHandle, StringComparison.Ordinal))
        {
            throw new BillingException(BillingErrorKind.Conflict, "Maxio returned a subscription owned by a different identity or plan.");
        }

        if (subscription.ProductPriceInCents == null || subscription.State == null || subscription.Product?.Name == null)
        {
            throw new BillingException(BillingErrorKind.InvalidProviderResponse, "Maxio returned an incomplete subscription.");
        }

        return new SubscriptionConfirmation(
            expectedReference,
            expectedProductHandle,
            subscription.Product.Name,
            subscription.ProductPriceInCents.Value,
            subscription.State.Value,
            subscription.NextAssessmentAt);
    }

    private static BillingException MapRawError(RawError error, string fallback, Exception innerException)
    {
        return error.StatusCode switch
        {
            HttpStatusCode.BadRequest => new BillingException(BillingErrorKind.InvalidRequest, fallback, innerException),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new BillingException(BillingErrorKind.UnauthorizedProvider, "Maxio authentication failed.", innerException),
            HttpStatusCode.NotFound => new BillingException(BillingErrorKind.NotFound, fallback, innerException),
            HttpStatusCode.Conflict => new BillingException(BillingErrorKind.Conflict, fallback, innerException),
            HttpStatusCode.UnprocessableEntity => new BillingException(BillingErrorKind.Validation, fallback, innerException),
            HttpStatusCode.TooManyRequests => new BillingException(BillingErrorKind.Throttled, "Maxio is temporarily throttling requests.", innerException),
            _ => new BillingException(BillingErrorKind.Unavailable, fallback, innerException)
        };
    }

    private static BillingException InvalidResponse(Exception innerException) =>
        new(BillingErrorKind.InvalidProviderResponse, "Maxio returned a response that could not be processed.", innerException);

    private static BillingException Unavailable(Exception innerException) =>
        new(BillingErrorKind.Unavailable, "Maxio is temporarily unavailable.", innerException);

    private static async Task<T> BoundedAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TotalCallBudget);
        return await operation(budget.Token);
    }
}
