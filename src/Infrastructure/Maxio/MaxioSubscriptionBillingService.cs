using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by the Maxio Advanced Billing .NET SDK.
/// All provider failures are translated into <see cref="SubscriptionBillingException"/> with a
/// caller-safe message; the raw SDK/provider exception text never leaves this class.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    // Whole-call budget applied to every SDK call (the per-attempt RetryOptions.Timeout is not a total).
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    // Non-terminal states: an existing subscription in one of these blocks a duplicate enrollment.
    private static readonly HashSet<string> NonTerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "pending", "assessing", "past_due", "soft_failure",
        "paused", "unpaid", "on_hold", "suspended", "awaiting_signup"
    };

    // Per-user gate: serializes concurrent subscribe requests for the same shopper within this process,
    // closing the double-click race around the read-before-write dedup check.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeGates = new();

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

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);

        IReadOnlyList<ProductResponse> products;
        try
        {
            products = await BoundedAsync(ct => _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: familyId.ToString(),
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: false,
                include: null,
                page: 1,
                perPage: 200,
                ct: ct), cancellationToken);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            // 404 => the configured family id disappeared between the two calls; any other status is ours.
            throw ToProviderUnavailable("list subscription plans", ex);
        }
        catch (Exception ex) when (IsBoundaryFault(ex, cancellationToken))
        {
            throw ToBoundary("list subscription plans", ex);
        }

        return products
            .Select(p => p.Product)
            .Where(p => !string.IsNullOrEmpty(p.Handle))
            .Select(p => new SubscriptionPlan
            {
                Handle = p.Handle!,
                Name = p.Name ?? p.Handle!,
                Description = p.Description,
                PriceInCents = p.PriceInCents ?? 0,
                Interval = p.Interval ?? 0,
                IntervalUnit = p.IntervalUnit?.Value,
                ProductFamilyHandle = p.ProductFamily?.Handle ?? _settings.ProductFamilyHandle
            })
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscriberInfo subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        // Cross-operation invariant: the plan handle must be one the configured family actually offers.
        var plans = await GetPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new SubscriptionBillingException(
                $"Unknown subscription plan '{planHandle}'.", SubscriptionBillingErrorKind.InvalidRequest);
        }

        var gate = SubscribeGates.GetOrAdd(subscriber.UserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customerId = await EnsureCustomerAsync(subscriber, cancellationToken);

            // Read-before-write dedup: an existing non-terminal subscription to this plan is a no-op.
            var existing = await FindActiveSubscriptionAsync(customerId, plan.Handle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Shopper {UserId} already has subscription {SubscriptionId} on plan {PlanHandle}; returning existing.",
                    subscriber.UserId, existing.Id, plan.Handle);
                return new SubscribeResult(existing, alreadyExisted: true);
            }

            var body = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = plan.Handle,
                    CustomerId = customerId,
                    Reference = SubscriptionReference(subscriber.UserId, plan.Handle),
                    // Invoice-based collection so subscribe succeeds with no card on file (plans require
                    // no payment method). Configurable per site; the site's default (automatic) would
                    // otherwise fail immediate collection with "no payment method on file".
                    PaymentCollectionMethod = CollectionMethod.FromValue(_settings.CollectionMethod)
                }
            };

            SubscriptionResponse response;
            try
            {
                response = await BoundedAsync(ct => _client.Subscriptions.CreateSubscription(body, ct: ct), cancellationToken);
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                if (ex.Error.TryGetErrorListResponse1(out var errors))
                {
                    var message = errors.Errors is { Count: > 0 }
                        ? string.Join("; ", errors.Errors)
                        : "The subscription could not be created.";
                    throw new SubscriptionBillingException(message, SubscriptionBillingErrorKind.Validation, ex);
                }

                throw ToProviderUnavailable("create subscription", ex);
            }
            catch (Exception ex) when (IsBoundaryFault(ex, cancellationToken))
            {
                throw ToBoundary("create subscription", ex);
            }

            var created = MapSubscription(response.Subscription)
                ?? throw new SubscriptionBillingException(
                    "The billing provider returned an empty subscription response.",
                    SubscriptionBillingErrorKind.ProviderUnavailable);

            _logger.LogInformation(
                "Created subscription {SubscriptionId} for shopper {UserId} (Maxio customer {CustomerId}) on plan {PlanHandle}, state {State}.",
                created.Id, subscriber.UserId, customerId, plan.Handle, created.State);

            return new SubscribeResult(created, alreadyExisted: false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetMySubscriptionsAsync(SubscriberInfo subscriber, CancellationToken cancellationToken = default)
    {
        var customerId = await TryReadCustomerIdAsync(subscriber.UserId, cancellationToken);
        if (customerId is null)
        {
            // No Maxio customer yet => the shopper has no subscriptions.
            return Array.Empty<CustomerSubscription>();
        }

        IReadOnlyList<SubscriptionResponse> subscriptions;
        try
        {
            subscriptions = await BoundedAsync(ct => _client.Customers.ListCustomerSubscriptions(customerId.Value, ct: ct), cancellationToken);
        }
        catch (Exception ex) when (IsBoundaryFault(ex, cancellationToken))
        {
            throw ToBoundary("list subscriptions", ex);
        }

        return subscriptions
            .Select(s => MapSubscription(s.Subscription))
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();
    }

    // --- customer resolution (idempotent by reference = eShop user id) ---

    private async Task<int> EnsureCustomerAsync(SubscriberInfo subscriber, CancellationToken cancellationToken)
    {
        var existingId = await TryReadCustomerIdAsync(subscriber.UserId, cancellationToken);
        if (existingId is not null)
        {
            return existingId.Value;
        }

        var (firstName, lastName, email) = DeriveCustomerName(subscriber);
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = subscriber.UserId
            }
        };

        try
        {
            var response = await BoundedAsync(ct => _client.Customers.CreateCustomer(body, ct: ct), cancellationToken);
            var id = response.Customer.Id
                ?? throw new SubscriptionBillingException(
                    "The billing provider returned a customer with no id.",
                    SubscriptionBillingErrorKind.ProviderUnavailable);
            _logger.LogInformation("Created Maxio customer {CustomerId} for shopper {UserId}.", id, subscriber.UserId);
            return id;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // A 422 here is most likely a create/create race on the unique reference — re-read and use the
            // winner. If the customer still isn't there, it was a genuine validation failure.
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                var raced = await TryReadCustomerIdAsync(subscriber.UserId, cancellationToken);
                if (raced is not null)
                {
                    return raced.Value;
                }

                throw new SubscriptionBillingException(
                    "The billing customer could not be created.", SubscriptionBillingErrorKind.Validation, ex);
            }

            throw ToProviderUnavailable("create billing customer", ex);
        }
        catch (Exception ex) when (IsBoundaryFault(ex, cancellationToken))
        {
            throw ToBoundary("create billing customer", ex);
        }
    }

    private async Task<int?> TryReadCustomerIdAsync(string userId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(ct => _client.Customers.ReadCustomerByReference(userId, ct: ct), cancellationToken);
            return response.Customer.Id;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (IsBoundaryFault(ex, cancellationToken))
        {
            throw ToBoundary("look up billing customer", ex);
        }
    }

    private async Task<CustomerSubscription?> FindActiveSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        IReadOnlyList<SubscriptionResponse> subscriptions;
        try
        {
            subscriptions = await BoundedAsync(ct => _client.Customers.ListCustomerSubscriptions(customerId, ct: ct), cancellationToken);
        }
        catch (Exception ex) when (IsBoundaryFault(ex, cancellationToken))
        {
            throw ToBoundary("check existing subscriptions", ex);
        }

        foreach (var wrapper in subscriptions)
        {
            var s = wrapper.Subscription;
            if (s is null)
            {
                continue;
            }

            var handle = s.Product?.Handle;
            var state = s.State?.Value;
            if (string.Equals(handle, planHandle, StringComparison.OrdinalIgnoreCase)
                && state is not null
                && NonTerminalStates.Contains(state))
            {
                return MapSubscription(s);
            }
        }

        return null;
    }

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await BoundedAsync(ct => _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: ct), cancellationToken);
        }
        catch (Exception ex) when (IsBoundaryFault(ex, cancellationToken))
        {
            throw ToBoundary("resolve product family", ex);
        }

        var match = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f is not null
                && string.Equals(f.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (match?.Id is null)
        {
            throw new SubscriptionBillingException(
                $"The configured product family '{_settings.ProductFamilyHandle}' was not found in Maxio.",
                SubscriptionBillingErrorKind.ProviderUnavailable);
        }

        return match.Id.Value;
    }

    // --- mapping & helpers ---

    private static CustomerSubscription? MapSubscription(Subscription? s)
    {
        if (s?.Id is null)
        {
            return null;
        }

        return new CustomerSubscription
        {
            Id = s.Id.Value,
            PlanHandle = s.Product?.Handle,
            PlanName = s.Product?.Name,
            State = s.State?.Value,
            PriceInCents = s.CurrentBillingAmountInCents ?? s.ProductPriceInCents,
            NextBillingAt = s.NextAssessmentAt ?? s.CurrentPeriodEndsAt,
            Reference = s.Reference
        };
    }

    private static string SubscriptionReference(string userId, string planHandle) => $"eshop-{userId}-{planHandle}";

    private static (string FirstName, string LastName, string Email) DeriveCustomerName(SubscriberInfo subscriber)
    {
        var email = !string.IsNullOrWhiteSpace(subscriber.Email) ? subscriber.Email! : subscriber.UserName;
        var localPart = email.Contains('@') ? email[..email.IndexOf('@')] : email;
        var firstName = string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart;
        return (firstName, "eShopOnWeb", email);
    }

    private async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    /// <summary>True for the exception kinds this class translates at its boundary (transport, timeout, drifted body).</summary>
    private static bool IsBoundaryFault(Exception ex, CancellationToken callerToken) =>
        ex switch
        {
            // A caller-initiated cancellation must propagate as cancellation, not become a provider error.
            OperationCanceledException when callerToken.IsCancellationRequested => false,
            System.Text.Json.JsonException => true,
            System.Net.Http.HttpRequestException => true,
            OperationCanceledException => true, // our budget elapsed (a timeout), not the caller's cancel
            _ => false
        };

    private SubscriptionBillingException ToBoundary(string action, Exception ex)
    {
        _logger.LogWarning(ex, "Maxio call failed while trying to {Action}.", action);
        var message = ex is System.Text.Json.JsonException
            ? "The billing provider returned a response that could not be processed."
            : "The billing provider is currently unavailable. Please try again.";
        return new SubscriptionBillingException(message, SubscriptionBillingErrorKind.ProviderUnavailable, ex);
    }

    private SubscriptionBillingException ToProviderUnavailable<TError>(string action, SdkException<TError> ex)
        where TError : ApiError
    {
        var status = ex.Error.TryGetRawError(out var raw) ? (int?)raw.StatusCode : null;
        _logger.LogWarning(ex, "Maxio returned an error (HTTP {Status}) while trying to {Action}.", status, action);
        return new SubscriptionBillingException(
            "The billing provider is currently unavailable. Please try again.",
            SubscriptionBillingErrorKind.ProviderUnavailable, ex);
    }
}
