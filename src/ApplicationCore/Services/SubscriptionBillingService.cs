using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.ApplicationCore.Models.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the subscribe flow against Maxio. Maxio is the system of record:
/// the eShopOnWeb user is linked to a Maxio customer via the customer's unique
/// <c>reference</c> (the user's username), so no local persistence is required and
/// the mapping survives restarts.
/// </summary>
public class SubscriptionBillingService : ISubscriptionBillingService
{
    // Subscription states in which a shopper already "has" the plan; used for idempotent replay.
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "past_due", "on_hold"
    };

    // Serializes subscribe calls per user so a double-click cannot race past the existing-subscription check.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new();

    private readonly IMaxioBillingClient _maxioClient;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        IMaxioBillingClient maxioClient,
        MaxioSettings settings,
        IAppLogger<SubscriptionBillingService> logger)
    {
        _maxioClient = maxioClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                "Maxio is not configured: Maxio:ProductFamilyHandle is missing (load the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable into user-secrets).");
        }

        var products = await _maxioClient.ListProductsForProductFamilyAsync(_settings.ProductFamilyHandle, cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null)
            .Select(p => new SubscriptionPlan
            {
                ProductId = p.Id,
                Handle = p.Handle ?? string.Empty,
                Name = p.Name ?? string.Empty,
                Description = p.Description ?? string.Empty,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit ?? string.Empty
            })
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<SubscriptionDetails> SubscribeAsync(
        string userReference, string email, string? firstName, string? lastName, string productHandle,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userReference, nameof(userReference));
        Guard.Against.NullOrEmpty(email, nameof(email));
        Guard.Against.NullOrEmpty(productHandle, nameof(productHandle));

        var userLock = UserLocks.GetOrAdd(userReference, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(userReference, email, firstName, lastName, cancellationToken);

            // Idempotency: replay the existing live subscription for this plan instead of double-subscribing.
            var existing = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var match = existing.FirstOrDefault(s =>
                string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase) &&
                s.State is not null && LiveStates.Contains(s.State));
            if (match is not null)
            {
                _logger.LogInformation(
                    $"User {userReference} already has subscription {match.Id} for plan {productHandle}; returning it (idempotent replay).");
                return ToDetails(match);
            }

            var subscription = await _maxioClient.CreateSubscriptionAsync(customer.Id, productHandle, cancellationToken);
            _logger.LogInformation($"Created Maxio subscription {subscription.Id} for user {userReference} on plan {productHandle}.");
            return ToDetails(subscription);
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> ListMySubscriptionsAsync(string userReference, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userReference, nameof(userReference));

        var customer = await _maxioClient.FindCustomerByReferenceAsync(userReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(ToDetails).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        string userReference, string email, string? firstName, string? lastName, CancellationToken cancellationToken)
    {
        var existing = await _maxioClient.FindCustomerByReferenceAsync(userReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var first = string.IsNullOrWhiteSpace(firstName) ? DeriveFirstName(email) : firstName!;
        var last = string.IsNullOrWhiteSpace(lastName) ? "Shopper" : lastName!;

        try
        {
            return await _maxioClient.CreateCustomerAsync(first, last, email, userReference, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Lost a race with a concurrent signup: the reference is unique in Maxio, so re-read the winner.
            var winner = await _maxioClient.FindCustomerByReferenceAsync(userReference, cancellationToken);
            if (winner is not null)
            {
                return winner;
            }
            throw;
        }
    }

    private static string DeriveFirstName(string email)
    {
        var localPart = email.Split('@')[0];
        return string.IsNullOrWhiteSpace(localPart) ? "Shopper" : localPart;
    }

    private static SubscriptionDetails ToDetails(MaxioSubscription s) => new()
    {
        SubscriptionId = s.Id,
        State = s.State ?? string.Empty,
        PlanHandle = s.Product?.Handle ?? string.Empty,
        PlanName = s.Product?.Name ?? string.Empty,
        PriceInCents = s.Product?.PriceInCents ?? 0,
        Interval = s.Product?.Interval ?? 0,
        IntervalUnit = s.Product?.IntervalUnit ?? string.Empty,
        NextBillingAt = s.NextAssessmentAt ?? s.CurrentPeriodEndsAt,
        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
        ActivatedAt = s.ActivatedAt,
        CanceledAt = s.CanceledAt,
        CreatedAt = s.CreatedAt
    };
}
