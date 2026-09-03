using System;
using System.Collections.Concurrent;
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
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio Advanced Billing implementation of <see cref="ISubscriptionBillingService"/>. This is the
/// single boundary where the SDK is called: every provider failure (typed API error, transport
/// failure, or an undeserialisable body) is translated here into a
/// <see cref="BillingException"/> carrying a caller-safe message and an appropriate HTTP status, so
/// no SDK/framework type ever reaches the API surface.
/// </summary>
public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    // The whole flow (which may be several sequential provider calls) is bounded by this budget,
    // linked to the caller's cancellation. The SDK's per-attempt Retry.Timeout is a separate,
    // narrower bound; this is the total the caller experiences.
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    // States in which a subscription no longer occupies the plan, so subscribing again is allowed.
    private static readonly HashSet<string> TerminalStates =
        new(StringComparer.OrdinalIgnoreCase) { "canceled", "expired", "failed_to_create" };

    // Per-subscriber single-flight gate: serialises concurrent subscribe attempts for the same
    // shopper so the read-before-write idempotency check cannot be raced (closes the TOCTOU window
    // that two simultaneous double-click requests would otherwise slip through). One entry per
    // distinct shopper for the process lifetime.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeGates = new();

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        IAppLogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
        => BoundedAsync(async ct =>
        {
            try
            {
                return await ListPlansAsync(ct);
            }
            catch (Exception ex)
            {
                throw Translate(ex, "The subscription plans could not be retrieved.", ct);
            }
        }, cancellationToken);

    public Task<CustomerSubscription> SubscribeAsync(
        SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        if (subscriber is null) throw new ArgumentNullException(nameof(subscriber));
        if (string.IsNullOrWhiteSpace(planHandle)) throw new PlanNotFoundException(planHandle ?? string.Empty);

        return BoundedAsync(async ct =>
        {
            try
            {
                // Validate the requested plan against the family up front: a bad handle is a caller
                // error (400), not a provider failure, and it confirms the plan exists before we mutate.
                var plans = await ListPlansAsync(ct);
                var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
                if (plan is null) throw new PlanNotFoundException(planHandle);

                var gate = SubscribeGates.GetOrAdd(subscriber.Reference, _ => new SemaphoreSlim(1, 1));
                await gate.WaitAsync(ct);
                try
                {
                    var customer = await EnsureCustomerAsync(subscriber, ct);

                    // Read-before-write idempotency: if the shopper already has a live subscription
                    // to this plan, return it rather than enrolling twice.
                    var existingSubscriptions = await _client.Customers.ListCustomerSubscriptions(customer.Id!.Value, ct);
                    var live = existingSubscriptions
                        .Select(s => s.Subscription)
                        .FirstOrDefault(s => s is not null
                            && IsLive(s.State?.Value)
                            && string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
                    if (live is not null)
                    {
                        _logger.LogInformation("Subscriber {Reference} already has a live subscription to {Plan}; returning existing.",
                            subscriber.Reference, planHandle);
                        return MapSubscription(live);
                    }

                    var body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = planHandle,
                            CustomerReference = subscriber.Reference,
                            // Invoice/remit the initial charge instead of auto-collecting it, so a
                            // payment-method-not-required plan enrolls without a payment profile.
                            // (Automatic collection of a priced plan's first charge is otherwise
                            // rejected 422 "No payment method was on file".)
                            PaymentCollectionMethod = CollectionMethod.Remittance
                            // No payment fields: the seeded plans do not require a payment method.
                        }
                    };

                    var response = await _client.Subscriptions.CreateSubscription(body, ct);
                    var subscription = response.Subscription
                        ?? throw new BillingUnavailableException("The billing provider returned a response that could not be processed.");

                    _logger.LogInformation("Created subscription {Id} for subscriber {Reference} on {Plan}.",
                        subscription.Id, subscriber.Reference, planHandle);
                    return MapSubscription(subscription);
                }
                finally
                {
                    gate.Release();
                }
            }
            catch (Exception ex)
            {
                throw Translate(ex, "The subscription could not be created.", ct);
            }
        }, cancellationToken);
    }

    public Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(
        SubscriberIdentity subscriber, CancellationToken cancellationToken = default)
    {
        if (subscriber is null) throw new ArgumentNullException(nameof(subscriber));

        return BoundedAsync(async ct =>
        {
            try
            {
                var customer = await TryReadCustomerAsync(subscriber.Reference, ct);
                if (customer?.Id is null)
                    return (IReadOnlyList<CustomerSubscription>)Array.Empty<CustomerSubscription>();

                var subscriptions = await _client.Customers.ListCustomerSubscriptions(customer.Id.Value, ct);
                var mapped = subscriptions
                    .Select(s => s.Subscription)
                    .Where(s => s is not null)
                    .Select(s => MapSubscription(s!))
                    .ToList();
                return (IReadOnlyList<CustomerSubscription>)mapped;
            }
            catch (Exception ex)
            {
                throw Translate(ex, "The subscriptions could not be retrieved.", ct);
            }
        }, cancellationToken);
    }

    // --- SDK calls (no boundary translation here; callers wrap) -------------------------------

    private async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken ct)
    {
        var familyId = await ResolveFamilyIdAsync(ct);
        var products = await _client.ProductFamilies.ListProductsForProductFamily(
            productFamilyId: familyId,
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
            ct: ct);

        return products
            .Select(p => MapPlan(p.Product))
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();
    }

    /// <summary>Resolves the numeric product-family id from the configured handle (numeric ids are not stable).</summary>
    private async Task<string> ResolveFamilyIdAsync(CancellationToken ct)
    {
        var handle = _settings.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(handle))
            throw new BillingUnavailableException("The subscription product family is not configured.");

        var families = await _client.ProductFamilies.ListProductFamilies(
            dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, ct: ct);

        var match = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f is not null && string.Equals(f.Handle, handle, StringComparison.OrdinalIgnoreCase));

        if (match?.Id is null)
            throw new BillingUnavailableException("The configured subscription product family could not be found.");

        return match.Id.Value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Ensures a Maxio customer exists for the shopper, keyed idempotently on the reference.</summary>
    private async Task<Customer> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken ct)
    {
        var existing = await TryReadCustomerAsync(subscriber.Reference, ct);
        if (existing?.Id is not null) return existing;

        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = subscriber.FirstName,
                LastName = subscriber.LastName,
                Email = subscriber.Email,
                Reference = subscriber.Reference
            }
        };

        try
        {
            var created = await _client.Customers.CreateCustomer(body, ct);
            if (created.Customer?.Id is null)
                throw new BillingUnavailableException("The billing provider returned a response that could not be processed.");
            _logger.LogInformation("Created Maxio customer {Id} for subscriber {Reference}.", created.Customer.Id, subscriber.Reference);
            return created.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // A racing create (duplicate reference) is rejected 422; the reference is unique, so the
            // customer now exists — re-read and use it instead of failing the subscribe.
            var reread = await TryReadCustomerAsync(subscriber.Reference, ct);
            if (reread?.Id is not null) return reread;
            throw TranslateCreateCustomerError(ex);
        }
    }

    /// <summary>Reads a customer by reference; returns null when none exists (404).</summary>
    private async Task<Customer?> TryReadCustomerAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    // --- mapping -------------------------------------------------------------------------------

    private static SubscriptionPlan? MapPlan(Product? product)
    {
        if (product?.Handle is null) return null;
        return new SubscriptionPlan
        {
            Handle = product.Handle,
            Name = product.Name ?? product.Handle,
            PriceInCents = product.PriceInCents,
            Price = CentsToAmount(product.PriceInCents),
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit?.Value
        };
    }

    private static CustomerSubscription MapSubscription(Subscription subscription)
        => new()
        {
            Id = subscription.Id,
            PlanHandle = subscription.Product?.Handle,
            PlanName = subscription.Product?.Name,
            PriceInCents = subscription.ProductPriceInCents,
            Price = CentsToAmount(subscription.ProductPriceInCents),
            State = subscription.State?.Value,
            NextBillingDate = subscription.NextAssessmentAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
        };

    private static decimal CentsToAmount(long? cents) => cents.HasValue ? cents.Value / 100m : 0m;

    private static bool IsLive(string? state) => state is not null && !TerminalStates.Contains(state);

    // --- resilience & failure translation ------------------------------------------------------

    /// <summary>Total-call budget linked to the caller's cancellation (see <see cref="CallBudget"/>).</summary>
    private static async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await operation(cts.Token);
    }

    /// <summary>Translates any provider failure into a caller-safe <see cref="BillingException"/>.</summary>
    private static BillingException Translate(Exception ex, string context, CancellationToken callerToken)
    {
        switch (ex)
        {
            case BillingException billing:
                return billing;
            case SdkException<CreateSubscriptionError> sub:
                return TranslateCreateSubscriptionError(sub);
            case SdkException<CreateCustomerError> cust:
                return TranslateCreateCustomerError(cust);
            case SdkException<ListProductsForProductFamilyError> prod:
                return TranslateProductsError(prod, context);
            case SdkException<RawError> raw:
                return new BillingUnavailableException($"{context} (provider returned HTTP {(int)raw.Error.StatusCode}).", ex);
            case JsonException:
                return new BillingUnavailableException("The billing provider returned a response that could not be processed.", ex);
            case HttpRequestException:
                return new BillingUnavailableException("The billing provider could not be reached.", ex);
            case OperationCanceledException when !callerToken.IsCancellationRequested:
                // Not the caller cancelling — our own budget elapsed.
                return new BillingUnavailableException("The billing provider did not respond in time.", ex);
            default:
                return new BillingUnavailableException(context, ex);
        }
    }

    private static BillingException TranslateCreateCustomerError(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out var typed))
        {
            var messages = new List<string>();
            if (typed?.Errors?.PerPage is { } perPage) messages.AddRange(perPage);
            if (typed?.Errors?.PricePoint is { } pricePoint) messages.AddRange(pricePoint);
            var detail = messages.Count > 0
                ? string.Join("; ", messages)
                : "The customer could not be created (validation failed).";
            return new BillingValidationException(detail, ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
            return new BillingValidationException($"The customer could not be created (HTTP {(int)raw.StatusCode}).", ex);
        return new BillingValidationException("The customer could not be created.", ex);
    }

    private static BillingException TranslateCreateSubscriptionError(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var typed))
        {
            var detail = typed?.Errors is { Count: > 0 } errors
                ? string.Join("; ", errors)
                : "The subscription could not be created (validation failed).";
            return new BillingValidationException(detail, ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
            return new BillingValidationException($"The subscription could not be created (HTTP {(int)raw.StatusCode}).", ex);
        return new BillingValidationException("The subscription could not be created.", ex);
    }

    private static BillingException TranslateProductsError(SdkException<ListProductsForProductFamilyError> ex, string context)
    {
        if (ex.Error.TryGetString(out var message) && !string.IsNullOrWhiteSpace(message))
            return new BillingUnavailableException($"{context} ({message}).", ex);
        if (ex.Error.TryGetRawError(out var raw))
            return new BillingUnavailableException($"{context} (provider returned HTTP {(int)raw.StatusCode}).", ex);
        return new BillingUnavailableException(context, ex);
    }
}
