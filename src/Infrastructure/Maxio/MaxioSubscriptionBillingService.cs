using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by the Maxio Advanced Billing .NET SDK. Maxio is the
/// system of record; the eShopOnWeb user is mapped to a Maxio customer by a stable <c>reference</c>.
/// </summary>
public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    // Whole-request budget. The SDK's RetryOptions.Timeout is PER ATTEMPT; only a CancellationToken deadline
    // bounds the full call (including retries), so every method runs under this linked deadline.
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    // Subscription states that already grant (or are working toward) access — an existing subscription in one
    // of these is reused rather than duplicated. Terminal states below are treated as "may subscribe again".
    private static readonly HashSet<string> TerminalStates =
        new(StringComparer.OrdinalIgnoreCase) { "canceled", "expired", "failed_to_create", "trial_ended" };

    // Serializes the ensure-customer + check + create critical section per user, so concurrent double-clicks
    // in this process cannot create two customers/subscriptions. Process-wide (static) regardless of the
    // service's DI lifetime.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new();

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync("list subscription plans", cancellationToken, async token =>
        {
            var familyId = await ResolveFamilyIdAsync(token);
            var products = await ListFamilyProductsAsync(familyId, token);

            var plans = new List<SubscriptionPlan>();
            foreach (var response in products)
            {
                var product = response.Product;
                if (product is null || product.ArchivedAt is not null || string.IsNullOrEmpty(product.Handle))
                {
                    continue;
                }
                plans.Add(MapPlan(product));
            }
            return (IReadOnlyList<SubscriptionPlan>)plans;
        });

    public Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default) =>
        ExecuteAsync($"subscribe to plan '{planHandle}'", cancellationToken, async token =>
        {
            // Validate the plan belongs to the configured family (also yields name/price for confirmation).
            var product = await FindPlanProductAsync(planHandle, token);
            var handle = product.Handle!;

            var gate = SubscribeLocks.GetOrAdd(subscriber.Reference, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(token);
            try
            {
                var customerId = await EnsureCustomerAsync(subscriber, token);

                // Idempotency: reuse an existing live subscription to this plan rather than creating another.
                var existing = await _client.Customers.ListCustomerSubscriptions(customerId, ct: token);
                foreach (var response in existing)
                {
                    var current = response.Subscription;
                    if (current is null)
                    {
                        continue;
                    }
                    if (string.Equals(current.Product?.Handle, handle, StringComparison.OrdinalIgnoreCase)
                        && IsReusableState(current.State))
                    {
                        _logger.LogInformation(
                            "Reusing existing subscription {SubscriptionId} for customer {CustomerId} on plan {Plan}.",
                            current.Id, customerId, handle);
                        return ToSubscribeResult(current, handle, product, customerId, alreadySubscribed: true);
                    }
                }

                var created = await _client.Subscriptions.CreateSubscription(new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = handle,
                        CustomerId = customerId
                    }
                }, ct: token);

                var subscription = created.Subscription
                    ?? throw new SubscriptionBillingException("The billing provider did not return a subscription.");

                _logger.LogInformation(
                    "Created subscription {SubscriptionId} for customer {CustomerId} on plan {Plan} (state {State}).",
                    subscription.Id, customerId, handle, subscription.State?.Value);

                return ToSubscribeResult(subscription, handle, product, customerId, alreadySubscribed: false);
            }
            finally
            {
                gate.Release();
            }
        });

    public Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string subscriberReference, CancellationToken cancellationToken = default) =>
        ExecuteAsync("list the user's subscriptions", cancellationToken, async token =>
        {
            var customerId = await TryReadCustomerIdByReferenceAsync(subscriberReference, token);
            if (customerId is null)
            {
                return (IReadOnlyList<CustomerSubscription>)Array.Empty<CustomerSubscription>();
            }

            var subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId.Value, ct: token);
            var result = new List<CustomerSubscription>();
            foreach (var response in subscriptions)
            {
                if (response.Subscription is not null)
                {
                    result.Add(MapSubscription(response.Subscription));
                }
            }
            return result;
        });

    // ── Customer resolution (idempotent) ─────────────────────────────────────────────────────────────

    private async Task<int> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken token)
    {
        var existing = await TryReadCustomerIdByReferenceAsync(subscriber.Reference, token);
        if (existing is not null)
        {
            return existing.Value;
        }

        try
        {
            var created = await _client.Customers.CreateCustomer(new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = subscriber.FirstName,
                    LastName = subscriber.LastName,
                    Email = subscriber.Email,
                    Reference = subscriber.Reference
                }
            }, ct: token);

            var id = created.Customer.Id
                ?? throw new SubscriptionBillingException("The billing provider did not return a customer id.");
            _logger.LogInformation("Created Maxio customer {CustomerId} for reference {Reference}.", id, subscriber.Reference);
            return id;
        }
        catch (SdkException<CreateCustomerError>)
        {
            // A concurrent create (double-click) loses the race on the unique reference and comes back 422.
            // Recover by re-reading: if the customer now exists, the 422 was the duplicate — use it.
            var recovered = await TryReadCustomerIdByReferenceAsync(subscriber.Reference, token);
            if (recovered is not null)
            {
                return recovered.Value;
            }
            throw; // a genuine validation failure — let the boundary translate it.
        }
    }

    private async Task<int?> TryReadCustomerIdByReferenceAsync(string reference, CancellationToken token)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: token);
            return response.Customer.Id;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    // ── Catalog helpers ──────────────────────────────────────────────────────────────────────────────

    private async Task<int> ResolveFamilyIdAsync(CancellationToken token)
    {
        var families = await _client.ProductFamilies.ListProductFamilies(
            dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, ct: token);

        foreach (var response in families)
        {
            var family = response.ProductFamily;
            if (family?.Id is { } id
                && string.Equals(family.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
            {
                return id;
            }
        }

        throw new SubscriptionBillingException(
            $"Configured Maxio product family '{_settings.ProductFamilyHandle}' was not found on the site.");
    }

    private Task<IReadOnlyList<ProductResponse>> ListFamilyProductsAsync(int familyId, CancellationToken token) =>
        _client.ProductFamilies.ListProductsForProductFamily(
            familyId.ToString(CultureInfo.InvariantCulture),
            dateField: null, filter: null, startDate: null, endDate: null,
            startDatetime: null, endDatetime: null, includeArchived: false, include: null,
            page: 1, perPage: 200, ct: token);

    private async Task<Product> FindPlanProductAsync(string planHandle, CancellationToken token)
    {
        var familyId = await ResolveFamilyIdAsync(token);
        var products = await ListFamilyProductsAsync(familyId, token);

        foreach (var response in products)
        {
            var product = response.Product;
            if (product?.Handle is not null && product.ArchivedAt is null
                && string.Equals(product.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            {
                return product;
            }
        }

        throw new SubscriptionBillingException(
            $"'{planHandle}' is not an available subscription plan.", (int)HttpStatusCode.BadRequest, isCallerError: true);
    }

    private static bool IsReusableState(SubscriptionState? state) =>
        state?.Value is { } value && !TerminalStates.Contains(value);

    // ── Mapping ──────────────────────────────────────────────────────────────────────────────────────

    private static SubscriptionPlan MapPlan(Product product) => new(
        Handle: product.Handle!,
        Name: product.Name ?? product.Handle!,
        Description: product.Description,
        PriceInCents: product.PriceInCents ?? 0,
        Price: (product.PriceInCents ?? 0) / 100m,
        Interval: product.Interval,
        IntervalUnit: product.IntervalUnit?.Value,
        RequiresPaymentMethod: product.RequireCreditCard ?? false);

    private static CustomerSubscription MapSubscription(Subscription subscription) => new(
        Id: subscription.Id ?? 0,
        PlanHandle: subscription.Product?.Handle,
        PlanName: subscription.Product?.Name,
        PriceInCents: subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
        State: subscription.State?.Value ?? "unknown",
        CurrentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
        NextBillingDate: subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);

    private static SubscribeResult ToSubscribeResult(Subscription subscription, string handle, Product product, int customerId, bool alreadySubscribed) => new(
        SubscriptionId: subscription.Id ?? 0,
        CustomerId: customerId,
        PlanHandle: subscription.Product?.Handle ?? handle,
        PlanName: subscription.Product?.Name ?? product.Name,
        PriceInCents: subscription.ProductPriceInCents ?? product.PriceInCents,
        State: subscription.State?.Value ?? "unknown",
        NextBillingDate: subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        AlreadySubscribed: alreadySubscribed);

    // ── Boundary: one deadline + one translation for every failure ─────────────────────────────────────

    private async Task<T> ExecuteAsync<T>(string action, CancellationToken callerToken, Func<CancellationToken, Task<T>> body)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        cts.CancelAfter(CallBudget);
        try
        {
            return await body(cts.Token);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw; // the caller aborted — propagate as a genuine cancellation.
        }
        catch (OperationCanceledException ex)
        {
            // Our budget elapsed, or the SDK's per-attempt timeout exhausted retries (surfaces as
            // TaskCanceledException). Either way: no usable response.
            _logger.LogWarning(ex, "Maxio call timed out while trying to {Action}.", action);
            throw new SubscriptionBillingException(
                "The billing provider did not respond in time.", (int)HttpStatusCode.GatewayTimeout, isCallerError: false, ex);
        }
        catch (SubscriptionBillingException)
        {
            throw; // already translated (e.g. plan-not-found, config error).
        }
        catch (Exception ex)
        {
            throw Translate(ex, action);
        }
    }

    private SubscriptionBillingException Translate(Exception ex, string action)
    {
        switch (ex)
        {
            case SdkException<CreateCustomerError> e:
                if (e.Error.TryGetCustomerErrorResponse1(out var customerBody))
                {
                    return CallerError(DescribeCustomerErrors(customerBody), (int)HttpStatusCode.UnprocessableEntity, action, e);
                }
                if (e.Error.TryGetRawError(out var customerRaw))
                {
                    return FromRaw(customerRaw, action, e);
                }
                return Unknown(action, e);

            case SdkException<CreateSubscriptionError> e:
                if (e.Error.TryGetErrorListResponse1(out var subscriptionBody))
                {
                    var message = subscriptionBody.Errors.Count > 0
                        ? string.Join("; ", subscriptionBody.Errors)
                        : "The subscription request was rejected by the billing provider.";
                    return CallerError(message, (int)HttpStatusCode.UnprocessableEntity, action, e);
                }
                if (e.Error.TryGetRawError(out var subscriptionRaw))
                {
                    return FromRaw(subscriptionRaw, action, e);
                }
                return Unknown(action, e);

            case SdkException<ListProductsForProductFamilyError> e:
                if (e.Error.TryGetString(out var notFound))
                {
                    _logger.LogWarning("Maxio product family lookup failed while trying to {Action}: {Detail}", action, notFound);
                    return new SubscriptionBillingException(
                        "The configured billing product family could not be found.", (int)HttpStatusCode.NotFound, isCallerError: false, e);
                }
                if (e.Error.TryGetRawError(out var productsRaw))
                {
                    return FromRaw(productsRaw, action, e);
                }
                return Unknown(action, e);

            case SdkException<RawError> e:
                return FromRaw(e.Error, action, e);

            case JsonException e:
                _logger.LogError(e, "Maxio returned a response that could not be processed while trying to {Action}.", action);
                return new SubscriptionBillingException(
                    "The billing provider returned a response that could not be processed.", null, isCallerError: false, e);

            case HttpRequestException e:
                _logger.LogError(e, "Could not reach Maxio while trying to {Action}.", action);
                return new SubscriptionBillingException(
                    "The billing provider is currently unreachable.", null, isCallerError: false, e);

            default:
                return Unknown(action, ex);
        }
    }

    private SubscriptionBillingException FromRaw(RawError raw, string action, Exception inner)
    {
        var status = (int)raw.StatusCode;
        _logger.LogWarning("Maxio returned HTTP {Status} while trying to {Action}: {Body}", status, action, SafeReadBody(raw));

        // Our credentials (401/403) or our quota (429) are not the caller's fault — surface as a gateway error.
        var callerError = status is >= 400 and < 500 and not 401 and not 403 and not 429;
        var message = callerError
            ? $"The billing provider rejected the request (HTTP {status})."
            : "The billing provider could not complete the request.";
        return new SubscriptionBillingException(message, status, callerError, inner);
    }

    private SubscriptionBillingException CallerError(string message, int status, string action, Exception inner)
    {
        _logger.LogWarning("Maxio rejected the request to {Action} (HTTP {Status}): {Message}", action, status, message);
        return new SubscriptionBillingException(message, status, isCallerError: true, inner);
    }

    private SubscriptionBillingException Unknown(string action, Exception inner)
    {
        _logger.LogError(inner, "Unexpected error while trying to {Action}.", action);
        return new SubscriptionBillingException(
            "An unexpected error occurred talking to the billing provider.", null, isCallerError: false, inner);
    }

    private static string DescribeCustomerErrors(CustomerErrorResponse1 body)
    {
        if (body.Errors is { } errors && errors.TryGetListOfString(out var messages) && messages.Count > 0)
        {
            return string.Join("; ", messages);
        }
        return "The customer details were rejected by the billing provider.";
    }

    private static string SafeReadBody(RawError raw)
    {
        try
        {
            return raw.ReadAsString();
        }
        catch
        {
            return "<unreadable body>";
        }
    }
}
