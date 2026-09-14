using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Orchestrates the eShopOnWeb subscribe flow on top of <see cref="MaxioApiClient"/>: resolving
/// plans, ensuring an idempotent customer ↔ user mapping, and enrolling users without creating
/// duplicates. Translates low-level Maxio failures into <see cref="BillingException"/> so the API
/// surface can map them to the right status code.
/// </summary>
internal class MaxioBillingService : IMaxioBillingService
{
    // Subscription states that mean the user is NOT currently enrolled; anything else is treated as
    // a live enrollment for idempotency purposes.
    private static readonly HashSet<string> DeadStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended"
    };

    // One lock per user reference serializes concurrent subscribe attempts (e.g. a double-click)
    // within this process, so the ensure-customer + subscribe sequence runs exactly once at a time.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new(StringComparer.Ordinal);

    private readonly MaxioApiClient _client;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioBillingService> _logger;

    public MaxioBillingService(MaxioApiClient client, IOptions<MaxioSettings> settings, IAppLogger<MaxioBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var products = await ExecuteAsync(
            () => _client.ListProductsForFamilyAsync(_settings.ProductFamilyHandle, cancellationToken),
            $"list plans for product family '{_settings.ProductFamilyHandle}'");

        return products
            .Select(MapPlan)
            .OrderBy(p => p.PriceInCents)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserReference))
        {
            throw new BillingException("A user reference is required to subscribe.", isClientError: true);
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            throw new BillingException("A plan handle is required to subscribe.", isClientError: true);
        }

        EnsureConfigured();

        var gate = UserLocks.GetOrAdd(request.UserReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(request, cancellationToken);

            // Idempotency: if the user already has a live subscription to this plan, return it
            // rather than creating a duplicate.
            var existing = await ExecuteAsync(
                () => _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken),
                $"list subscriptions for customer {customer.Id}");

            var match = existing.FirstOrDefault(s =>
                string.Equals(s.Product?.Handle, request.PlanHandle, StringComparison.OrdinalIgnoreCase)
                && IsLive(s.State));

            if (match is not null)
            {
                _logger.LogInformation($"User '{request.UserReference}' is already subscribed to plan '{request.PlanHandle}' (subscription {match.Id}); returning existing subscription.");
                return new SubscribeResult(MapSubscription(match), customer.Id, request.UserReference, alreadyExisted: true);
            }

            var created = await CreateSubscriptionAsync(customer.Id, request.PlanHandle, cancellationToken);
            _logger.LogInformation($"Created subscription {created.Id} for user '{request.UserReference}' on plan '{request.PlanHandle}'.");
            return new SubscribeResult(MapSubscription(created), customer.Id, request.UserReference, alreadyExisted: false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string userReference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userReference))
        {
            throw new BillingException("A user reference is required to list subscriptions.", isClientError: true);
        }

        EnsureConfigured();

        var customer = await ExecuteAsync(
            () => _client.LookupCustomerByReferenceAsync(userReference, cancellationToken),
            $"look up customer by reference '{userReference}'");

        if (customer is null)
        {
            // No backing Maxio customer yet means the user has never subscribed.
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await ExecuteAsync(
            () => _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken),
            $"list subscriptions for customer {customer.Id}");

        return subscriptions
            .Select(MapSubscription)
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    /// <summary>
    /// Returns the Maxio customer for the user, creating one if none exists. Idempotent: a lost race
    /// on creation (422 because the reference is now taken) is recovered by re-looking-up the customer.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscribeRequest request, CancellationToken cancellationToken)
    {
        var existing = await ExecuteAsync(
            () => _client.LookupCustomerByReferenceAsync(request.UserReference, cancellationToken),
            $"look up customer by reference '{request.UserReference}'");

        if (existing is not null)
        {
            return existing;
        }

        var body = new CreateCustomerBody
        {
            FirstName = string.IsNullOrWhiteSpace(request.FirstName) ? request.UserReference : request.FirstName,
            LastName = string.IsNullOrWhiteSpace(request.LastName) ? "eShopOnWeb" : request.LastName,
            Email = request.Email,
            Reference = request.UserReference
        };

        try
        {
            var created = await _client.CreateCustomerAsync(body, cancellationToken);
            _logger.LogInformation($"Created Maxio customer {created.Id} for user '{request.UserReference}'.");
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // A concurrent request may have created the customer between our lookup and create
            // (the reference-uniqueness constraint rejects the second create). Recover by re-looking-up.
            var recovered = await ExecuteAsync(
                () => _client.LookupCustomerByReferenceAsync(request.UserReference, cancellationToken),
                $"re-look up customer by reference '{request.UserReference}' after create conflict");

            if (recovered is not null)
            {
                return recovered;
            }

            // The 422 was genuinely a validation problem (e.g. malformed email), not a race.
            throw new BillingException(
                $"Could not create a billing customer: {string.Join("; ", ex.Errors.DefaultIfEmpty(ex.Message))}",
                isClientError: true,
                innerException: ex);
        }
    }

    private async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        var body = new CreateSubscriptionBody { CustomerId = customerId, ProductHandle = planHandle };
        var uniquenessToken = Guid.NewGuid().ToString("N");

        try
        {
            return await _client.CreateSubscriptionAsync(body, uniquenessToken, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.IsClientError)
        {
            throw new BillingException(
                $"Could not create subscription for plan '{planHandle}': {string.Join("; ", ex.Errors.DefaultIfEmpty(ex.Message))}",
                isClientError: true,
                innerException: ex);
        }
        catch (MaxioApiException ex)
        {
            throw new BillingException($"The billing system failed to create the subscription: {ex.Message}", isClientError: false, innerException: ex);
        }
    }

    private void EnsureConfigured()
    {
        if (_settings.IsConfigured)
        {
            return;
        }

        var detail = string.Join(" ", _settings.GetConfigurationErrors());
        throw new BillingException(
            $"Maxio billing is not configured. {detail}",
            isClientError: false);
    }

    private static bool IsLive(string? state) =>
        !string.IsNullOrWhiteSpace(state) && !DeadStates.Contains(state);

    private static SubscriptionPlan MapPlan(MaxioProduct product) => new()
    {
        Id = product.Id,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty
    };

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? "unknown",
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
        Interval = subscription.Product?.Interval,
        IntervalUnit = subscription.Product?.IntervalUnit,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CreatedAt = subscription.CreatedAt
    };

    /// <summary>Runs a Maxio API operation, translating transport/API failures into <see cref="BillingException"/>.</summary>
    private async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, string description)
    {
        try
        {
            return await operation();
        }
        catch (MaxioApiException ex)
        {
            _logger.LogWarning($"Maxio failed to {description}: {(int)ex.StatusCode} {ex.Message}");
            throw new BillingException($"The billing system failed to {description}.", isClientError: ex.IsClientError, innerException: ex);
        }
    }
}
