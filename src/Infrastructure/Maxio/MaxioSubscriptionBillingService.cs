using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Orchestrates the subscribe flow against Maxio, the billing system of record.
/// Idempotency: the Maxio customer is keyed by the eShopOnWeb username via the
/// customer "reference" field, and an existing non-terminal subscription to the
/// same plan is returned instead of creating a duplicate.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    // States in which a subscription no longer blocks re-subscribing to the same plan.
    private static readonly HashSet<string> _terminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "ended", "suspended"
    };

    // Serializes subscribe attempts per shopper so a double-click (or concurrent
    // requests) within this host can never create two customers/subscriptions.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new();

    private readonly IMaxioClient _maxioClient;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioClient maxioClient,
        IOptions<MaxioSettings> settings,
        IAppLogger<MaxioSubscriptionBillingService> logger)
    {
        _maxioClient = maxioClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        _settings.Validate();

        var products = await _maxioClient.ListProductsAsync(_settings.ProductFamilyHandle, cancellationToken);
        return products
            .Where(p => !string.IsNullOrWhiteSpace(p.Handle))
            .Select(p => new SubscriptionPlan
            {
                Handle = p.Handle!,
                Name = p.Name ?? p.Handle!,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit ?? string.Empty
            })
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<SubscriptionDetails> SubscribeAsync(string customerReference, string email, string planHandle,
        CancellationToken cancellationToken = default)
    {
        _settings.Validate();

        var plans = await ListPlansAsync(cancellationToken);
        if (!plans.Any(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        var gate = _gates.GetOrAdd(customerReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await FindOrCreateCustomerAsync(customerReference, email, cancellationToken);

            var subscriptions = await _maxioClient.ListSubscriptionsAsync(customer.Id, cancellationToken);
            var existing = subscriptions.FirstOrDefault(s =>
                string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
                !IsTerminal(s.State));

            if (existing != null)
            {
                _logger.LogInformation(
                    "Shopper {Reference} already has subscription {SubscriptionId} to plan {PlanHandle}; returning it.",
                    customerReference, existing.Id, planHandle);
                return Map(existing);
            }

            var created = await _maxioClient.CreateSubscriptionAsync(customer.Id, planHandle, cancellationToken);
            _logger.LogInformation(
                "Created subscription {SubscriptionId} for shopper {Reference} on plan {PlanHandle}.",
                created.Id, customerReference, planHandle);
            return Map(created);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(string customerReference,
        CancellationToken cancellationToken = default)
    {
        _settings.Validate();

        var customer = await _maxioClient.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer == null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        var subscriptions = await _maxioClient.ListSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .OrderByDescending(s => s.ActivatedAt)
            .Select(Map)
            .ToList();
    }

    private async Task<MaxioCustomerDto> FindOrCreateCustomerAsync(string reference, string email, CancellationToken cancellationToken)
    {
        var existing = await _maxioClient.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        try
        {
            return await _maxioClient.CreateCustomerAsync(new MaxioCustomerAttributes
            {
                FirstName = reference,
                LastName = "eShopOnWeb",
                Email = email,
                Reference = reference
            }, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            // Another request created the customer between lookup and create; re-read it.
            var raced = await _maxioClient.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (raced != null)
            {
                return raced;
            }

            throw;
        }
    }

    private static bool IsTerminal(string? state) =>
        state != null && _terminalStates.Contains(state);

    private static SubscriptionDetails Map(MaxioSubscriptionDto subscription) => new()
    {
        SubscriptionId = subscription.Id,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents,
        Currency = subscription.Currency ?? string.Empty,
        State = subscription.State ?? string.Empty,
        ActivatedAt = subscription.ActivatedAt,
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt
    };
}
