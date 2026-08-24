using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public class SubscriptionService : ISubscriptionService
{
    // States in which a subscription no longer bills; a subscription in any other state
    // counts as "live" and makes Subscribe idempotent for the same plan.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended"
    };

    private readonly IMaxioClient _maxioClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(IMaxioClient maxioClient, IOptions<MaxioSettings> settings, ILogger<SubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _maxioClient.ListProductsForProductFamilyAsync(_settings.ProductFamilyHandle, cancellationToken);
        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrEmpty(p.Handle))
            .Select(ToPlanDto)
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<SubscriptionPlanDto?> GetPlanAsync(string productHandle, CancellationToken cancellationToken = default)
    {
        var plans = await ListPlansAsync(cancellationToken);
        return plans.FirstOrDefault(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<SubscribeResult> SubscribeAsync(string username, string productHandle, CancellationToken cancellationToken = default)
    {
        var plan = await GetPlanAsync(productHandle, cancellationToken);
        if (plan is null)
        {
            throw new PlanNotFoundException(productHandle);
        }

        var customer = await EnsureCustomerAsync(username, cancellationToken);

        var existing = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var live = existing.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase) &&
            s.State is not null && !TerminalStates.Contains(s.State));
        if (live is not null)
        {
            _logger.LogInformation("Reusing existing Maxio subscription {SubscriptionId} for {Username} on plan {PlanHandle}",
                live.Id, username, plan.Handle);
            return new SubscribeResult(ToSubscriptionDto(live), created: false);
        }

        // "remittance" enrolls the shopper without capturing payment at signup, matching the
        // seeded plans' "payment method not required" configuration (spec: Collection-Method).
        var created = await _maxioClient.CreateSubscriptionAsync(new MaxioCreateSubscription
        {
            ProductHandle = plan.Handle,
            CustomerId = customer.Id,
            PaymentCollectionMethod = "remittance",
            // Maxio requires subscription references to be unique, so scope it to the plan.
            Reference = $"{username}:{plan.Handle}"
        }, cancellationToken);

        _logger.LogInformation("Created Maxio subscription {SubscriptionId} for {Username} on plan {PlanHandle}",
            created.Id, username, plan.Handle);
        return new SubscribeResult(ToSubscriptionDto(created), created: true);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string username, CancellationToken cancellationToken = default)
    {
        var customer = await _maxioClient.FindCustomerByReferenceAsync(username, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(ToSubscriptionDto).ToList();
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
            return await _maxioClient.CreateCustomerAsync(new MaxioCreateCustomer
            {
                FirstName = localPart,
                LastName = "eShopOnWeb",
                Email = username,
                Reference = username
            }, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Lost a race with a concurrent signup for the same reference; the customer now exists.
            var winner = await _maxioClient.FindCustomerByReferenceAsync(username, cancellationToken);
            if (winner is not null)
            {
                return winner;
            }

            throw;
        }
    }

    private static SubscriptionPlanDto ToPlanDto(MaxioProduct product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = product.Description ?? string.Empty,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty,
        RequiresPaymentMethod = product.RequireCreditCard
    };

    private static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? string.Empty,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents,
        Interval = subscription.Product?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit ?? string.Empty,
        Currency = subscription.Currency,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        NextBillingAt = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
        CanceledAt = subscription.CanceledAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
        CreatedAt = subscription.CreatedAt
    };
}
