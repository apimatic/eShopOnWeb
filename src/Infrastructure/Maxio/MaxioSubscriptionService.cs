using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Json;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by Maxio Advanced Billing. Owns the
/// idempotency rules: a customer is keyed to the eShopOnWeb user by reference, and a subscribe
/// call reuses an existing live subscription for the plan rather than creating a duplicate.
/// </summary>
internal sealed class MaxioSubscriptionService : ISubscriptionBillingService
{
    // Plans in this catalog do not require a card; invoice/remittance collection lets the
    // subscription activate without capturing a payment method.
    private const string PaymentCollectionMethod = "remittance";

    private readonly MaxioClient _client;
    private readonly MaxioSettings _settings;
    private readonly KeyedAsyncLock _subscribeLock;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(MaxioClient client, MaxioSettings settings,
        KeyedAsyncLock subscribeLock, ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings;
        _subscribeLock = subscribeLock;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await GuardAsync(() => _client.GetProductsForFamilyAsync(_settings.ProductFamilyHandle, cancellationToken));

        return products
            .Where(p => !string.IsNullOrWhiteSpace(p.Handle))
            .OrderBy(p => p.PriceInCents)
            .Select(MapPlan)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        // Serialize the whole check-then-create flow per user so concurrent double-clicks can
        // never each create a customer/subscription (see KeyedAsyncLock).
        using var _ = await _subscribeLock.AcquireAsync(request.UserReference, cancellationToken);

        // 1. Validate the requested plan exists in the configured family (avoids leaking arbitrary handles to Maxio).
        var products = await GuardAsync(() => _client.GetProductsForFamilyAsync(_settings.ProductFamilyHandle, cancellationToken));
        var plan = products.FirstOrDefault(p =>
            string.Equals(p.Handle, request.PlanHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new SubscriptionBillingException(
                $"Unknown plan '{request.PlanHandle}'. It is not part of the '{_settings.ProductFamilyHandle}' product family.",
                (int)HttpStatusCode.NotFound);
        }

        // 2. Ensure a Maxio customer exists for this user (idempotent by reference).
        var customer = await EnsureCustomerAsync(request, cancellationToken);

        // 3. If the user is already enrolled in this plan, return that subscription (idempotent replay).
        var existing = await FindLiveSubscriptionForPlanAsync(customer.Id, plan.Handle!, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "User {Reference} is already subscribed to {Plan} (subscription {SubscriptionId}); returning existing.",
                request.UserReference, plan.Handle, existing.Id);
            return new SubscribeResult(MapSubscription(existing), alreadyExisted: true);
        }

        // 4. Create the subscription. The uniqueness_token is stable across the HTTP client's
        //    automatic retries of this single call (so a lost-response retry can't double-create),
        //    but unique per subscribe attempt so it never falsely blocks a later re-subscribe.
        var createRequest = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = plan.Handle!,
                CustomerId = customer.Id,
                PaymentCollectionMethod = PaymentCollectionMethod,
                UniquenessToken = $"eshop-subscribe:{customer.Id}:{plan.Handle}:{Guid.NewGuid():N}"
            }
        };

        try
        {
            var created = await _client.CreateSubscriptionAsync(createRequest, cancellationToken);
            _logger.LogInformation(
                "Created subscription {SubscriptionId} ({State}) for user {Reference} on plan {Plan}.",
                created.Id, created.State, request.UserReference, plan.Handle);
            return new SubscribeResult(MapSubscription(created), alreadyExisted: false);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            // The uniqueness_token blocked a duplicate of an in-flight create (e.g. a retried
            // request whose original succeeded). Re-read and return the created subscription.
            var raced = await FindLiveSubscriptionForPlanAsync(customer.Id, plan.Handle!, cancellationToken);
            if (raced is not null)
                return new SubscribeResult(MapSubscription(raced), alreadyExisted: true);

            throw new SubscriptionBillingException(
                "A concurrent subscribe request is in progress for this plan. Please retry in a moment.",
                (int)HttpStatusCode.Conflict, ex);
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex);
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string userReference, CancellationToken cancellationToken = default)
    {
        var customer = await GuardAsync(() => _client.LookupCustomerByReferenceAsync(userReference, cancellationToken));
        if (customer is null)
            return Array.Empty<CustomerSubscription>();

        var subscriptions = await GuardAsync(() => _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken));
        return subscriptions
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .Select(MapSubscription)
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscribeRequest request, CancellationToken ct)
    {
        var existing = await GuardAsync(() => _client.LookupCustomerByReferenceAsync(request.UserReference, ct));
        if (existing is not null)
            return existing;

        var createRequest = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = request.FirstName,
                LastName = request.LastName,
                Email = request.Email,
                Reference = request.UserReference
            }
        };

        try
        {
            var created = await _client.CreateCustomerAsync(createRequest, ct);
            _logger.LogInformation("Created Maxio customer {CustomerId} for user {Reference}.", created.Id, request.UserReference);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Another request created the customer for this reference in between our lookup and create.
            var raced = await GuardAsync(() => _client.LookupCustomerByReferenceAsync(request.UserReference, ct));
            if (raced is not null)
                return raced;

            throw Translate(ex);
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionForPlanAsync(long customerId, string planHandle, CancellationToken ct)
    {
        var subscriptions = await GuardAsync(() => _client.ListCustomerSubscriptionsAsync(customerId, ct));
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) && IsLiveState(s.State));
    }

    private static bool IsLiveState(string? state) =>
        state is "active" or "trialing" or "assessing" or "pending"
            or "past_due" or "soft_failure" or "paused" or "awaiting_signup";

    private SubscriptionPlan MapPlan(MaxioProduct product) => new(
        handle: product.Handle!,
        name: product.Name ?? product.Handle!,
        description: product.Description,
        priceInCents: product.PriceInCents,
        interval: product.Interval,
        intervalUnit: product.IntervalUnit ?? "month",
        productFamilyHandle: product.ProductFamily?.Handle ?? _settings.ProductFamilyHandle,
        requiresPaymentMethod: product.RequireCreditCard);

    private static CustomerSubscription MapSubscription(MaxioSubscription s) => new(
        id: s.Id,
        state: s.State ?? "unknown",
        planHandle: s.Product?.Handle,
        planName: s.Product?.Name,
        priceInCents: s.ProductPriceInCents ?? s.Product?.PriceInCents ?? 0,
        currency: s.Currency,
        interval: s.Product?.Interval,
        intervalUnit: s.Product?.IntervalUnit,
        currentPeriodStartedAt: s.CurrentPeriodStartedAt,
        currentPeriodEndsAt: s.CurrentPeriodEndsAt,
        nextBillingAt: s.NextAssessmentAt ?? s.CurrentPeriodEndsAt,
        createdAt: s.CreatedAt,
        paymentCollectionMethod: s.PaymentCollectionMethod);

    private static async Task<T> GuardAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex);
        }
    }

    private static SubscriptionBillingException Translate(MaxioApiException ex)
    {
        var status = ex.StatusCode switch
        {
            HttpStatusCode.NotFound => (int)HttpStatusCode.NotFound,
            HttpStatusCode.UnprocessableEntity => (int)HttpStatusCode.UnprocessableEntity,
            HttpStatusCode.Conflict => (int)HttpStatusCode.Conflict,
            HttpStatusCode.TooManyRequests => (int)HttpStatusCode.ServiceUnavailable,
            _ => (int)HttpStatusCode.BadGateway
        };

        var detail = ex.Errors.Count > 0 ? string.Join("; ", ex.Errors) : ex.Message;
        return new SubscriptionBillingException($"Billing system error: {detail}", status, ex);
    }
}
