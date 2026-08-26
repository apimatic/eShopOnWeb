using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Orchestrates the subscribe flow against Maxio Advanced Billing, which is the
/// billing system of record. Customer identity is linked via the Maxio customer
/// "reference" field set to the eShopOnWeb username, so the mapping survives
/// restarts without any local persistence.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    // States in which a subscription still occupies the plan; subscribing again must
    // return the existing one instead of creating a duplicate.
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "assessing", "past_due", "unpaid", "on_hold", "pending", "pending_renewal"
    };

    private readonly IMaxioClient _maxioClient;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(IMaxioClient maxioClient, ILogger<SubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _maxioClient.ListProductsAsync(cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null)
            .OrderBy(p => p.PriceInCents)
            .Select(p => new SubscriptionPlanDto
            {
                Handle = p.Handle,
                Name = p.Name,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit,
                ProductFamilyHandle = p.ProductFamily?.Handle ?? string.Empty
            })
            .ToList();
    }

    public async Task<SubscribeOutcome?> SubscribeAsync(string username, string productHandle, CancellationToken cancellationToken = default)
    {
        var plans = await ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            return null;
        }

        var customer = await EnsureCustomerAsync(username, cancellationToken);
        var subscriptions = await _maxioClient.ListSubscriptionsAsync(customer.Id, cancellationToken);

        var existing = subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase) &&
            LiveStates.Contains(s.State));

        if (existing is not null)
        {
            _logger.LogInformation(
                "User {Username} already has subscription {SubscriptionId} for plan {PlanHandle}; returning it instead of creating a duplicate.",
                username, existing.Id, plan.Handle);
            return new SubscribeOutcome(Map(existing), AlreadyExisted: true);
        }

        var created = await _maxioClient.CreateSubscriptionAsync(plan.Handle, customer.Id, cancellationToken);
        _logger.LogInformation("Created subscription {SubscriptionId} for user {Username} on plan {PlanHandle}.",
            created.Id, username, plan.Handle);
        return new SubscribeOutcome(Map(created), AlreadyExisted: false);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string username, CancellationToken cancellationToken = default)
    {
        var customer = await _maxioClient.FindCustomerByReferenceAsync(username, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxioClient.ListSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .OrderByDescending(s => s.CreatedAt)
            .Select(Map)
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string username, CancellationToken cancellationToken)
    {
        var existing = await _maxioClient.FindCustomerByReferenceAsync(username, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var localPart = username.Split('@')[0];
        try
        {
            return await _maxioClient.CreateCustomerAsync(
                firstName: localPart,
                lastName: "User",
                email: username,
                reference: username,
                cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // A concurrent request created the customer first; the reference is unique per site,
            // so a re-lookup resolves the race without creating a duplicate.
            var raced = await _maxioClient.FindCustomerByReferenceAsync(username, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private static SubscriptionDto Map(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        ProductHandle = subscription.Product?.Handle ?? string.Empty,
        ProductName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents,
        Currency = subscription.Currency,
        NextBillingDate = subscription.NextAssessmentAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CreatedAt = subscription.CreatedAt
    };
}
