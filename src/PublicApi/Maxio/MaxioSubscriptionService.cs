using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    /// <summary>
    /// Subscription states (maxio-spec/components/schemas/Subscription-State.yaml) that mean
    /// "this subscription is over" - a new signup to the same plan should not be blocked by it.
    /// </summary>
    private static readonly HashSet<string> EndOfLifeStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create"
    };

    // Serializes concurrent subscribe attempts for the same user within this process, so a
    // double-click can't race the "does a subscription already exist" check below. Combined
    // with Maxio's unique customer reference constraint, this makes signup safe to retry.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new();

    private readonly IMaxioApiClient _client;
    private readonly MaxioOptions _options;
    private readonly IAppLogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(IMaxioApiClient client, IOptions<MaxioOptions> options, IAppLogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _client.ListProductsForFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
        return products.Select(ToPlanDto).ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(string userReference, string email, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle is required.", nameof(planHandle));
        }

        var plans = await _client.ListProductsForFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new MaxioPlanNotFoundException(planHandle);
        }

        var userLock = UserLocks.GetOrAdd(userReference, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(userReference, email, cancellationToken);

            var existingSubscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var existing = existingSubscriptions.FirstOrDefault(s =>
                string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase) &&
                !EndOfLifeStates.Contains(s.State));

            if (existing is not null)
            {
                _logger.LogInformation(
                    "User {0} already has a live subscription {1} to plan {2}; returning it instead of creating a duplicate.",
                    userReference, existing.Id, plan.Handle);
                return ToSubscriptionDto(existing);
            }

            var created = await _client.CreateSubscriptionAsync(
                new CreateMaxioSubscriptionRequest
                {
                    ProductHandle = plan.Handle,
                    CustomerReference = userReference
                },
                cancellationToken);

            _logger.LogInformation(
                "Created Maxio subscription {0} for user {1} to plan {2}.",
                created.Id, userReference, plan.Handle);

            return ToSubscriptionDto(created);
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(string userReference, CancellationToken cancellationToken = default)
    {
        var customer = await _client.LookupCustomerByReferenceAsync(userReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(ToSubscriptionDto).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string userReference, string email, CancellationToken cancellationToken)
    {
        var existing = await _client.LookupCustomerByReferenceAsync(userReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveNameFromEmail(email);

        _logger.LogInformation("No Maxio customer found for reference {0}; creating one.", userReference);

        return await _client.CreateCustomerAsync(
            new CreateMaxioCustomerRequest
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = userReference
            },
            cancellationToken);
    }

    private static (string FirstName, string LastName) DeriveNameFromEmail(string email)
    {
        var localPart = email.Split('@')[0];
        var segments = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

        static string Capitalize(string value) =>
            value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

        var firstName = segments.Length > 0 ? Capitalize(segments[0]) : "eShopOnWeb";
        var lastName = segments.Length > 1 ? Capitalize(segments[^1]) : "Subscriber";
        return (firstName, lastName);
    }

    private static SubscriptionPlanDto ToPlanDto(MaxioProduct product) => new()
    {
        Handle = product.Handle,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        RequireCreditCard = product.RequireCreditCard
    };

    private static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription) => new()
    {
        MaxioSubscriptionId = subscription.Id,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.Product?.PriceInCents ?? 0,
        State = subscription.State,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingDate = subscription.NextAssessmentAt,
        CreatedAt = subscription.CreatedAt
    };
}
