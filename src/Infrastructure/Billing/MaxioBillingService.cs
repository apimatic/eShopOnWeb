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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by Maxio Advanced Billing. All SDK interaction is
/// confined here; the rest of the app sees only domain types and <see cref="BillingException"/>.
///
/// Idempotency: the customer is keyed by a deterministic <c>reference</c> (the eShop user id) with
/// read-before-create; the subscription is keyed by <c>eshop:{reference}:{planHandle}</c> and looked up
/// before creating. Both paths are serialized per user by <see cref="KeyedSemaphore"/> so a double-click
/// cannot race into duplicate customers or subscriptions.
///
/// Bounding: every SDK call runs under a linked CancellationToken carrying a total budget, because the
/// SDK's own timeout is per-attempt, not per-call.
/// </summary>
public class MaxioBillingService : ISubscriptionBillingService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly KeyedSemaphore _locks;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        KeyedSemaphore locks,
        ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _locks = locks;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyId = await ResolveProductFamilyIdAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<ProductResponse> products;
        try
        {
            products = await RunBoundedAsync(
                ct => _client.ProductFamilies.ListProductsForProductFamily(
                    familyId,
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
                    ct: ct),
                cancellationToken).ConfigureAwait(false);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var notFound))
                throw new BillingException($"The configured product family was not found: {notFound}", 502, ex);
            if (ex.Error.TryGetRawError(out var raw))
                throw FromRaw(raw, "list subscription plans", ex);
            throw new BillingException("Failed to list subscription plans.", 502, ex);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "list subscription plans", cancellationToken);
        }

        var plans = products
            .Select(p => p.Product)
            .Where(p => !string.IsNullOrWhiteSpace(p.Handle))
            .Select(MapPlan)
            .ToList();

        _logger.LogInformation("Listed {Count} subscription plan(s) for family {Family}.", plans.Count, _settings.ProductFamilyHandle);
        return plans;
    }

    public async Task<SubscriptionResult> SubscribeAsync(BillingUser user, string? planHandle, CancellationToken cancellationToken = default)
    {
        if (user is null) throw new ArgumentNullException(nameof(user));

        // Resolve + validate the target plan against the live catalog before any write.
        var plans = await ListPlansAsync(cancellationToken).ConfigureAwait(false);
        var plan = ResolvePlan(plans, planHandle);
        var subscriptionReference = BuildSubscriptionReference(user.Reference, plan.Handle);

        // Serialize per user so a double-click cannot race customer/subscription creation.
        using var gate = await _locks.LockAsync(user.Reference, cancellationToken).ConfigureAwait(false);

        // Idempotent subscription check first: if one already exists for this user+plan, return it.
        var existing = await FindSubscriptionAsync(subscriptionReference, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            _logger.LogInformation("Subscription {Reference} already exists (id {Id}); returning existing.",
                subscriptionReference, existing.Id);
            return new SubscriptionResult(MapSubscription(existing, plan.Handle), alreadyExisted: true);
        }

        // Ensure a billing customer exists for this user (idempotent).
        var customerId = await EnsureCustomerAsync(user, cancellationToken).ConfigureAwait(false);

        // Enroll the customer in the plan.
        var created = await CreateSubscriptionAsync(plan.Handle, customerId, subscriptionReference, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Created subscription {Id} for customer {CustomerId} on plan {Plan}.",
            created.Id, customerId, plan.Handle);
        return new SubscriptionResult(MapSubscription(created, plan.Handle), alreadyExisted: false);
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(string userReference, CancellationToken cancellationToken = default)
    {
        var customer = await ReadCustomerByReferenceAsync(userReference, cancellationToken).ConfigureAwait(false);
        if (customer?.Id is null)
        {
            // No billing customer yet => the user has no subscriptions.
            return Array.Empty<SubscriptionDetails>();
        }

        IReadOnlyList<SubscriptionResponse> subscriptions;
        try
        {
            subscriptions = await RunBoundedAsync(
                ct => _client.Customers.ListCustomerSubscriptions(customer.Id.Value, ct: ct),
                cancellationToken).ConfigureAwait(false);
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error, "list your subscriptions", ex);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "list your subscriptions", cancellationToken);
        }

        return subscriptions
            .Select(s => s.Subscription)
            .Where(s => s is not null)
            .Select(s => MapSubscription(s!, null))
            .ToList();
    }

    // ---- Customer helpers ----

    private async Task<int> EnsureCustomerAsync(BillingUser user, CancellationToken cancellationToken)
    {
        var existing = await ReadCustomerByReferenceAsync(user.Reference, cancellationToken).ConfigureAwait(false);
        if (existing?.Id is not null)
            return existing.Id.Value;

        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                Reference = user.Reference,
            }
        };

        try
        {
            var response = await RunBoundedAsync(
                ct => _client.Customers.CreateCustomer(body, ct: ct),
                cancellationToken).ConfigureAwait(false);

            var id = response.Customer.Id
                ?? throw new BillingException("The billing provider created a customer without an id.", 502);
            _logger.LogInformation("Created billing customer {Id} for reference {Reference}.", id, user.Reference);
            return id;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // A 422 here can be a lost race (another concurrent request already created the customer with
            // this reference). Reconcile by re-reading before treating it as a hard failure.
            var reread = await ReadCustomerByReferenceAsync(user.Reference, cancellationToken).ConfigureAwait(false);
            if (reread?.Id is not null)
                return reread.Id.Value;

            throw new BillingException("Could not create a billing customer for your account.", 502, ex);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "create your billing customer", cancellationToken);
        }
    }

    private async Task<Customer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await RunBoundedAsync(
                ct => _client.Customers.ReadCustomerByReference(reference, ct: ct),
                cancellationToken).ConfigureAwait(false);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // No customer with this reference yet.
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error, "look up your billing customer", ex);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "look up your billing customer", cancellationToken);
        }
    }

    // ---- Subscription helpers ----

    private async Task<Subscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await RunBoundedAsync(
                ct => _client.Subscriptions.FindSubscription(reference, ct: ct),
                cancellationToken).ConfigureAwait(false);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
                return null; // 404 => no existing subscription for this reference
            if (ex.Error.TryGetRawError(out var raw))
                throw FromRaw(raw, "look up your subscription", ex);
            throw new BillingException("Could not look up your subscription.", 502, ex);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "look up your subscription", cancellationToken);
        }
    }

    private async Task<Subscription> CreateSubscriptionAsync(string planHandle, int customerId, string reference, CancellationToken cancellationToken)
    {
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = planHandle,
                CustomerId = customerId,
                Reference = reference,
                // Bill by invoice (Relationship Invoicing "remittance") rather than auto-charging a card,
                // so a shopper can subscribe without capturing a payment method — matching the plan config.
                PaymentCollectionMethod = CollectionMethod.Remittance,
            }
        };

        try
        {
            var response = await RunBoundedAsync(
                ct => _client.Subscriptions.CreateSubscription(body, ct: ct),
                cancellationToken).ConfigureAwait(false);

            return response.Subscription
                ?? throw new BillingException("The billing provider created a subscription without details.", 502);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            // 422 is a validation rejection the caller may be able to act on (e.g. an invalid plan).
            if (ex.Error.TryGetErrorListResponse1(out var errors))
                throw new BillingException(DescribeErrors(errors), 422, ex);
            if (ex.Error.TryGetRawError(out var raw))
                throw FromRaw(raw, "create your subscription", ex);
            throw new BillingException("Could not create your subscription.", 502, ex);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "create your subscription", cancellationToken);
        }
    }

    // ---- Product family resolution ----

    private async Task<string> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await RunBoundedAsync(
                ct => _client.ProductFamilies.ListProductFamilies(
                    dateField: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    ct: ct),
                cancellationToken).ConfigureAwait(false);
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error, "resolve the product family", ex);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "resolve the product family", cancellationToken);
        }

        var match = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f is not null &&
                string.Equals(f.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (match?.Id is null)
        {
            throw new BillingException(
                $"No product family with handle '{_settings.ProductFamilyHandle}' exists on the configured Maxio site.",
                502);
        }

        return match.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    // ---- Mapping ----

    private static SubscriptionPlan MapPlan(Product product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = product.Description,
        PriceInCents = product.PriceInCents ?? 0,
        Interval = product.Interval ?? 0,
        IntervalUnit = product.IntervalUnit?.Value,
    };

    private static SubscriptionDetails MapSubscription(Subscription subscription, string? fallbackPlanHandle) => new()
    {
        SubscriptionId = subscription.Id ?? 0,
        PlanHandle = subscription.Product?.Handle ?? fallbackPlanHandle,
        PlanName = subscription.Product?.Name,
        State = subscription.State?.Value,
        PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0,
        NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
        Reference = subscription.Reference,
    };

    private SubscriptionPlan ResolvePlan(IReadOnlyList<SubscriptionPlan> plans, string? requestedHandle)
    {
        if (plans.Count == 0)
            throw new BillingException("No subscription plans are currently available.", 409);

        var handle = !string.IsNullOrWhiteSpace(requestedHandle) ? requestedHandle : _settings.DefaultPlanHandle;

        if (!string.IsNullOrWhiteSpace(handle))
        {
            var match = plans.FirstOrDefault(p => string.Equals(p.Handle, handle, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                // An explicit, caller-supplied unknown handle is a 404; a misconfigured default is ours (502).
                if (!string.IsNullOrWhiteSpace(requestedHandle))
                    throw new BillingException($"Unknown subscription plan '{requestedHandle}'.", 404);
                throw new BillingException($"The configured default plan '{handle}' was not found in the catalog.", 502);
            }
            return match;
        }

        // No handle requested and none configured: fall back to the first available plan.
        return plans[0];
    }

    private static string BuildSubscriptionReference(string userReference, string planHandle)
        => $"eshop:{userReference}:{planHandle}";

    private static string DescribeErrors(ErrorListResponse1 errors)
    {
        var messages = errors.Errors?.Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
        return messages is { Count: > 0 }
            ? $"The subscription was rejected: {string.Join("; ", messages)}"
            : "The subscription was rejected by the billing provider.";
    }

    // ---- Error translation ----

    /// <summary>Maps a provider <see cref="RawError"/> to a coherent, caller-safe <see cref="BillingException"/>.</summary>
    private BillingException FromRaw(RawError raw, string action, Exception inner)
    {
        var status = (int)raw.StatusCode;
        // Our-credentials / our-quota problems are not the caller's fault -> 5xx.
        if (status is 401 or 403)
        {
            _logger.LogError(inner, "Billing provider rejected our credentials (HTTP {Status}) while trying to {Action}.", status, action);
            return new BillingException("The billing provider is unavailable.", 502, inner);
        }
        if (status == 429)
        {
            _logger.LogWarning(inner, "Billing provider rate-limited us while trying to {Action}.", action);
            return new BillingException("The billing provider is temporarily unavailable. Please try again shortly.", 503, inner);
        }
        // A caller-actionable 4xx is surfaced as the same status.
        if (status is >= 400 and < 500)
        {
            _logger.LogWarning(inner, "Billing provider returned HTTP {Status} while trying to {Action}: {Body}", status, action, Safe(raw));
            return new BillingException($"The billing provider rejected the request while trying to {action}.", status, inner);
        }
        _logger.LogError(inner, "Billing provider returned HTTP {Status} while trying to {Action}: {Body}", status, action, Safe(raw));
        return new BillingException($"The billing provider failed while trying to {action}.", 502, inner);
    }

    private Exception Translate(Exception ex, string action, CancellationToken cancellationToken)
    {
        switch (ex)
        {
            case BillingException:
                return ex; // already translated
            case OperationCanceledException when cancellationToken.IsCancellationRequested:
                return ex; // genuine caller cancellation — propagate as-is
            case OperationCanceledException: // includes TaskCanceledException from the per-call budget
                _logger.LogWarning(ex, "Billing call timed out while trying to {Action}.", action);
                return new BillingException($"The billing provider did not respond while trying to {action}.", 504, ex);
            case HttpRequestException:
                _logger.LogError(ex, "Billing provider unreachable while trying to {Action}.", action);
                return new BillingException("The billing provider is currently unreachable.", 502, ex);
            case JsonException:
                _logger.LogError(ex, "Billing provider returned an unprocessable response while trying to {Action}.", action);
                return new BillingException("The billing provider returned a response that could not be processed.", 502, ex);
            default:
                _logger.LogError(ex, "Unexpected error while trying to {Action}.", action);
                return new BillingException($"An unexpected error occurred while trying to {action}.", 502, ex);
        }
    }

    private static string Safe(RawError raw)
    {
        try
        {
            var body = raw.ReadAsString();
            return body.Length > 500 ? body.Substring(0, 500) : body;
        }
        catch
        {
            return "<unreadable>";
        }
    }

    private async Task<T> RunBoundedAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        // The SDK Timeout is per-attempt; a CancellationToken deadline is the only thing that bounds a whole call.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(_settings.TimeoutSeconds));
        return await operation(cts.Token).ConfigureAwait(false);
    }
}
