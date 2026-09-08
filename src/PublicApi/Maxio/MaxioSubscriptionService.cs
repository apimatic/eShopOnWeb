using System;
using System.Collections.Concurrent;
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
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Logging;
using SdkCreateSubscriptionRequest = MaxioAdvancedBilling.Models.CreateSubscriptionRequest;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Implements the subscription capability on top of Maxio Advanced Billing.
///
/// Idempotency model:
///  - One Maxio customer per eShopOnWeb user. The user id is stored as the Maxio customer
///    <c>reference</c>, which Maxio enforces as unique, so find-before-create is safe even when
///    the in-process cache is cold (e.g. after an app restart).
///  - Subscribing returns the shopper's existing non-terminal subscription to the requested plan
///    when one already exists, and enrollment is serialized per user, so a double-click cannot
///    create two customers/subscriptions.
///  - Writes go through <see cref="MaxioSingleSendHandler"/>, which refuses SDK transport-retry
///    re-sends; if a write's outcome is unknown anyway (dropped socket after the request left,
///    unreadable response), the service reconciles by re-reading Maxio state before failing.
///
/// Subscriptions are created with the <c>remittance</c> collection method: the eShopOnWeb
/// identity flow never captures a payment method, so the first-period balance is invoiced rather
/// than card-charged at signup. The seeded products do not require a credit card.
/// </summary>
public sealed class MaxioSubscriptionService : IMaxioSubscriptionService
{
    // Terminal states in which an existing subscription to a plan is NOT returned on a repeat
    // subscribe (a fresh subscription may be created instead). Wire values of SubscriptionState.
    private static readonly HashSet<string> NonResumableStates = new(StringComparer.Ordinal)
    {
        "canceled",
        "expired",
        "failed_to_create"
    };

    /// <summary>Whole-call budget for a single Maxio operation (the total, not per attempt).</summary>
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private const int ProductsPerPage = 200;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    // userId (eShopOnWeb) -> Maxio customer id. An in-process cache only: when cold, the customer
    // is re-found by its unique Maxio reference (= userId), so correctness never depends on it.
    private readonly ConcurrentDictionary<string, int> _customerIdByUser = new(StringComparer.Ordinal);

    // Serializes enrollment per user so concurrent double-clicks cannot both create.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _userGates = new(StringComparer.Ordinal);

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, MaxioOptions options, ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();

        string? currency = await TryReadSiteCurrencyAsync(cancellationToken);
        var products = await ListFamilyProductsAsync(cancellationToken);

        var plans = new List<SubscriptionPlanDto>();
        foreach (var productResponse in products)
        {
            Product? product = productResponse.Product;
            if (product is null || product.ArchivedAt is not null)
            {
                continue;
            }

            long? priceInCents = product.PriceInCents;
            if (priceInCents is null && product.Id is int productId && product.DefaultProductPricePointId is int pricePointId)
            {
                priceInCents = await TryReadDefaultPriceInCentsAsync(productId, pricePointId, cancellationToken);
            }

            plans.Add(new SubscriptionPlanDto
            {
                Id = product.Id,
                Handle = product.Handle,
                Name = product.Name,
                PriceInCents = priceInCents,
                Currency = currency
            });
        }

        return plans;
    }

    public async Task<SubscriptionEnrollment> SubscribeAsync(MaxioShopper shopper, string planHandle, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new MaxioBillingException("A plan must be selected.", StatusCodes.Status400BadRequest);
        }

        var gate = _userGates.GetOrAdd(shopper.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            int customerId = await EnsureCustomerAsync(shopper, cancellationToken).ConfigureAwait(false);

            SubscriptionDto? existing = await FindActiveSubscriptionAsync(customerId, planHandle, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                return new SubscriptionEnrollment(existing, CreatedNew: false);
            }

            return await CreateSubscriptionGuardedAsync(customerId, planHandle, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(MaxioShopper shopper, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        int? customerId;
        if (_customerIdByUser.TryGetValue(shopper.Id, out int cached))
        {
            customerId = cached;
        }
        else
        {
            try
            {
                var customer = await BoundedAsync(
                    token => _client.Customers.ReadCustomerByReference(reference: shopper.Id, ct: token),
                    cancellationToken).ConfigureAwait(false);
                customerId = customer.Customer?.Id;
            }
            catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                // No Maxio customer yet -> no subscriptions. Not an error.
                return Array.Empty<SubscriptionDto>();
            }
            catch (SdkException<RawError> ex)
            {
                throw TranslateProviderError(ex, "looking up the customer");
            }
            catch (HttpRequestException ex)
            {
                throw TranslateTransportError(ex);
            }

            if (customerId is null)
            {
                return Array.Empty<SubscriptionDto>();
            }

            _customerIdByUser[shopper.Id] = customerId.Value;
        }

        return await ListSubscriptionsAsync(customerId.Value, cancellationToken).ConfigureAwait(false);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
            string.IsNullOrWhiteSpace(_options.Subdomain) ||
            string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new MaxioBillingException(
                "The billing service is not configured. Set Maxio:ApiKey, Maxio:Subdomain and Maxio:ProductFamilyHandle.",
                StatusCodes.Status500InternalServerError);
        }
    }

    // ---- customer --------------------------------------------------------------------------------

    private async Task<int> EnsureCustomerAsync(MaxioShopper shopper, CancellationToken cancellationToken)
    {
        if (_customerIdByUser.TryGetValue(shopper.Id, out int cached))
        {
            return cached;
        }

        int customerId;
        try
        {
            var customer = await BoundedAsync(
                token => _client.Customers.ReadCustomerByReference(reference: shopper.Id, ct: token),
                cancellationToken).ConfigureAwait(false);
            customerId = customer.Customer?.Id
                ?? throw new MaxioBillingException("The billing service returned an unreadable customer record.", StatusCodes.Status502BadGateway);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            customerId = await CreateCustomerGuardedAsync(shopper, cancellationToken).ConfigureAwait(false);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateProviderError(ex, "looking up the customer");
        }
        catch (HttpRequestException ex)
        {
            throw TranslateTransportError(ex);
        }

        _customerIdByUser[shopper.Id] = customerId;
        return customerId;
    }

    private async Task<int> CreateCustomerGuardedAsync(MaxioShopper shopper, CancellationToken cancellationToken)
    {
        (string firstName, string lastName) = ResolveNames(shopper);

        try
        {
            CustomerResponse response;
            using (MaxioSingleSendHandler.BeginWriteScope())
            {
                response = await BoundedAsync(
                    token => _client.Customers.CreateCustomer(new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = firstName,
                            LastName = lastName,
                            Email = shopper.Email,
                            Reference = shopper.Id
                        }
                    }, token),
                    cancellationToken).ConfigureAwait(false);
            }

            return response.Customer?.Id
                ?? throw new MaxioBillingException("The billing service returned an unreadable customer record.", StatusCodes.Status502BadGateway);
        }
        catch (Exception ex) when (IsAmbiguousWriteFailure(ex, cancellationToken) || ex is SdkException<CreateCustomerError>)
        {
            // The create may still have happened (duplicate-reference race on another request, a
            // resend that reached Maxio before the socket dropped, an unreadable response, or a
            // 422 whose body the generated error model could not parse). Settle it by re-reading.
            try
            {
                var customer = await BoundedAsync(
                    token => _client.Customers.ReadCustomerByReference(reference: shopper.Id, ct: token),
                    cancellationToken).ConfigureAwait(false);
                if (customer.Customer?.Id is int winnerId)
                {
                    return winnerId;
                }
            }
            catch (SdkException<RawError> reRead) when (reRead.Error.StatusCode == HttpStatusCode.NotFound)
            {
                // The create did not take effect; report the original failure below.
            }
            catch (SdkException<RawError> reRead)
            {
                throw TranslateProviderError(reRead, "confirming the customer");
            }

            throw new MaxioBillingException(
                "The billing service could not create the customer record for this account.",
                StatusCodes.Status502BadGateway,
                ex);
        }
    }

    // ---- subscriptions --------------------------------------------------------------------------

    private async Task<SubscriptionEnrollment> CreateSubscriptionGuardedAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        try
        {
            SubscriptionResponse response;
            using (MaxioSingleSendHandler.BeginWriteScope())
            {
                response = await BoundedAsync(
                    token => _client.Subscriptions.CreateSubscription(new SdkCreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = planHandle,
                            CustomerId = customerId,
                            PaymentCollectionMethod = CollectionMethod.Remittance
                        }
                    }, token),
                    cancellationToken).ConfigureAwait(false);
            }

            Subscription? subscription = response.Subscription;
            if (subscription is null)
            {
                throw new MaxioBillingException("The billing service returned an unreadable subscription record.", StatusCodes.Status502BadGateway);
            }

            var dto = await MapSubscriptionAsync(subscription, cancellationToken).ConfigureAwait(false);
            return new SubscriptionEnrollment(dto, CreatedNew: true);
        }
        catch (Exception ex) when (IsAmbiguousWriteFailure(ex, cancellationToken))
        {
            // One attempt was sent but its outcome is unknown: settle by re-reading Maxio state.
            SubscriptionDto? recovered = await FindActiveSubscriptionAsync(customerId, planHandle, cancellationToken).ConfigureAwait(false);
            if (recovered is not null)
            {
                return new SubscriptionEnrollment(recovered, CreatedNew: true);
            }

            throw new MaxioBillingException(
                "The billing service could not confirm the subscription was created. Please review your subscriptions.",
                StatusCodes.Status502BadGateway,
                ex);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            throw TranslateCreateSubscriptionError(ex);
        }
    }

    private async Task<SubscriptionDto?> FindActiveSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        IReadOnlyList<SubscriptionResponse> subscriptions = await ListCustomerSubscriptionResponsesAsync(customerId, cancellationToken).ConfigureAwait(false);

        foreach (var response in subscriptions)
        {
            Subscription? subscription = response.Subscription;
            if (subscription is null)
            {
                continue;
            }

            string? subscriptionPlanHandle = subscription.Product?.Handle;
            if (subscriptionPlanHandle is null ||
                !subscriptionPlanHandle.Equals(planHandle, StringComparison.OrdinalIgnoreCase) ||
                IsNonResumable(subscription.State))
            {
                continue;
            }

            return await MapSubscriptionAsync(subscription, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private async Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        IReadOnlyList<SubscriptionResponse> subscriptions = await ListCustomerSubscriptionResponsesAsync(customerId, cancellationToken).ConfigureAwait(false);

        var result = new List<SubscriptionDto>(subscriptions.Count);
        foreach (var response in subscriptions)
        {
            if (response.Subscription is { } subscription)
            {
                result.Add(await MapSubscriptionAsync(subscription, cancellationToken).ConfigureAwait(false));
            }
        }

        return result;
    }

    private async Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptionResponsesAsync(int customerId, CancellationToken cancellationToken)
    {
        try
        {
            return await BoundedAsync(
                token => _client.Customers.ListCustomerSubscriptions(customerId, token),
                cancellationToken).ConfigureAwait(false);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateProviderError(ex, "listing the subscriptions");
        }
        catch (HttpRequestException ex)
        {
            throw TranslateTransportError(ex);
        }
    }

    // ---- catalog --------------------------------------------------------------------------------

    private async Task<IReadOnlyList<ProductResponse>> ListFamilyProductsAsync(CancellationToken cancellationToken)
    {
        string familyKey = "handle:" + _options.ProductFamilyHandle;
        var all = new List<ProductResponse>();

        int page = 1;
        while (true)
        {
            IReadOnlyList<ProductResponse> batch;
            try
            {
                batch = await BoundedAsync(
                    token => _client.ProductFamilies.ListProductsForProductFamily(
                        productFamilyId: familyKey,
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
                        ct: token),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetRawError(out RawError? raw))
                {
                    throw TranslateProviderError(raw, "listing the plans");
                }

                // 404: the configured product family does not exist on this site.
                throw new MaxioBillingException(
                    $"The billing product family '{_options.ProductFamilyHandle}' was not found on the configured site.",
                    StatusCodes.Status500InternalServerError,
                    ex);
            }
            catch (SdkException<RawError> ex)
            {
                throw TranslateProviderError(ex, "listing the plans");
            }
            catch (HttpRequestException ex)
            {
                throw TranslateTransportError(ex);
            }

            all.AddRange(batch);
            if (batch.Count < ProductsPerPage)
            {
                break;
            }

            page++;
        }

        return all;
    }

    private async Task<string?> TryReadSiteCurrencyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(token => _client.Sites.ReadSite(token), cancellationToken).ConfigureAwait(false);
            return response.Site?.Currency;
        }
        catch (Exception ex) when (ex is MaxioBillingException or HttpRequestException or JsonException or MaxioWriteResendException)
        {
            // Currency is cosmetic on the catalog; a read failure must not take the endpoint down.
            _logger.LogWarning(ex, "Could not read the site currency from Maxio.");
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Reading the site currency from Maxio timed out.");
            return null;
        }
    }

    private async Task<long?> TryReadDefaultPriceInCentsAsync(int productId, int pricePointId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                token => _client.ProductPricePoints.ReadProductPricePoint(
                    productId: MaxioAdvancedBilling.Models.AnyOf.ProductIdModel.Int(productId),
                    pricePointId: MaxioAdvancedBilling.Models.AnyOf.PricePointIdModel.Int(pricePointId),
                    currencyPrices: null,
                    ct: token),
                cancellationToken).ConfigureAwait(false);
            return response.PricePoint?.PriceInCents;
        }
        catch (Exception ex) when (ex is MaxioBillingException or HttpRequestException or JsonException or MaxioWriteResendException)
        {
            _logger.LogWarning(ex, "Could not read the default price point for product {ProductId}.", productId);
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Reading the price point for product {ProductId} timed out.", productId);
            return null;
        }
    }

    // ---- projection -----------------------------------------------------------------------------

    private async Task<SubscriptionDto> MapSubscriptionAsync(Subscription subscription, CancellationToken cancellationToken)
    {
        Subscription source = subscription;

        // List/create payloads embed the product, but guard the null case defensively (a drifted
        // payload must not silently drop the plan identity from the DTO).
        if (source.Product is null && source.Id is int subscriptionId)
        {
            try
            {
                var reRead = await BoundedAsync(
                    token => _client.Subscriptions.ReadSubscription(subscriptionId, include: null, token),
                    cancellationToken).ConfigureAwait(false);
                if (reRead.Subscription is { Product: not null } enriched)
                {
                    source = enriched;
                }
            }
            catch (Exception ex) when (ex is MaxioBillingException or HttpRequestException or JsonException)
            {
                _logger.LogWarning(ex, "Could not enrich subscription {SubscriptionId} with its plan.", source.Id);
            }
        }

        return new SubscriptionDto
        {
            Id = source.Id,
            PlanHandle = source.Product?.Handle,
            PlanName = source.Product?.Name,
            State = source.State?.Value,
            PriceInCents = source.ProductPriceInCents,
            Currency = source.Currency,
            CurrentPeriodStartedAt = source.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = source.CurrentPeriodEndsAt,
            NextBillingDate = source.NextAssessmentAt
        };
    }

    private static bool IsNonResumable(SubscriptionState? state)
    {
        return state is not null && NonResumableStates.Contains(state.Value);
    }

    private static (string FirstName, string LastName) ResolveNames(MaxioShopper shopper)
    {
        // The eShopOnWeb identity records no first/last name, so they are derived from the email
        // when the caller did not provide them. Maxio requires both to be non-blank.
        string email = string.IsNullOrWhiteSpace(shopper.Email) ? shopper.Id : shopper.Email;
        int at = email.IndexOf('@');

        string firstName = FirstNonBlank(shopper.FirstName, at > 0 ? email[..at] : null, "Customer");
        string lastName = FirstNonBlank(shopper.LastName, at > 0 && at < email.Length - 1 ? email[(at + 1)..] : null, "User");

        return (firstName, lastName);
    }

    private static string FirstNonBlank(string? candidate, string? fallback, string finalFallback)
    {
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            return candidate.Trim();
        }

        return !string.IsNullOrWhiteSpace(fallback) ? fallback.Trim() : finalFallback;
    }

    // ---- call budget + error translation ----------------------------------------------------------

    private static bool IsAmbiguousWriteFailure(Exception ex, CancellationToken cancellationToken)
    {
        return ex is MaxioWriteResendException or HttpRequestException or JsonException
            || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested);
    }

    private static async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new MaxioBillingException("The billing service did not respond in time.", StatusCodes.Status504GatewayTimeout);
        }
    }

    private static MaxioBillingException TranslateProviderError(SdkException<RawError> ex, string context)
    {
        return TranslateProviderError(ex.Error, context, ex);
    }

    private static MaxioBillingException TranslateProviderError(RawError error, string context)
    {
        return TranslateProviderError(error, context, null);
    }

    private static MaxioBillingException TranslateProviderError(RawError error, string context, Exception? inner)
    {
        int status = MapUpstreamStatus(error.StatusCode, context);
        string message = (status, error.StatusCode) switch
        {
            (_, HttpStatusCode.Unauthorized) or (_, HttpStatusCode.Forbidden) =>
                "The billing service rejected the configured credentials.",
            (StatusCodes.Status503ServiceUnavailable, _) => "The billing service is temporarily unavailable. Please try again later.",
            _ => $"The billing service reported an error while {context}."
        };
        return inner is null
            ? new MaxioBillingException(message, status)
            : new MaxioBillingException(message, status, inner);
    }

    private static MaxioBillingException TranslateTransportError(HttpRequestException ex)
    {
        return new MaxioBillingException("The billing service could not be reached.", StatusCodes.Status502BadGateway, ex);
    }

    private static MaxioBillingException TranslateCreateSubscriptionError(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var list) && list.Errors is { Count: > 0 })
        {
            string detail = string.Join(" ", list.Errors);
            return new MaxioBillingException($"The subscription was rejected: {detail}", StatusCodes.Status400BadRequest, ex);
        }

        if (ex.Error.TryGetRawError(out RawError? raw))
        {
            return TranslateProviderError(raw, "creating the subscription", ex);
        }

        return new MaxioBillingException("The subscription was rejected by the billing service.", StatusCodes.Status400BadRequest, ex);
    }

    private static int MapUpstreamStatus(HttpStatusCode status, string context)
    {
        if (status == HttpStatusCode.Unauthorized || status == HttpStatusCode.Forbidden)
        {
            // Server-to-server credential problem: nothing the caller can act on.
            return StatusCodes.Status502BadGateway;
        }

        if (status == HttpStatusCode.TooManyRequests || (int)status >= 500)
        {
            return StatusCodes.Status503ServiceUnavailable;
        }

        if ((int)status >= 400 && (int)status < 500)
        {
            // Other upstream client errors (404, 409, 422...) reach the caller as-is; they can act
            // on a 4xx. 404s in lookup paths are already handled closer to the call.
            return (int)status;
        }

        return StatusCodes.Status502BadGateway;
    }
}
