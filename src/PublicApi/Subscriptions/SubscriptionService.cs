using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>Coordinates identity and the authoritative Maxio subscription state.</summary>
internal sealed class SubscriptionService : ISubscriptionService
{
    private readonly IMaxioClient _maxio;
    private readonly UserManager<ApplicationUser> _users;
    private readonly MaxioOptions _options;
    // The service is scoped because it uses Identity's scoped UserManager. Keep
    // the idempotency gate process-wide so concurrent HTTP requests share it.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscriptionLocks = new(StringComparer.Ordinal);

    public SubscriptionService(IMaxioClient maxio, UserManager<ApplicationUser> users, IOptions<MaxioOptions> options)
    {
        _maxio = maxio;
        _users = users;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var plans = await _maxio.ListProductsForProductFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
        return plans
            .Where(plan => plan.ArchivedAt is null && !string.IsNullOrWhiteSpace(plan.Handle))
            .Select(MapPlan)
            .OrderBy(plan => plan.PriceInCents)
            .ToList();
    }

    public async Task<CreateSubscriptionResponse> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new SubscriptionValidationException("planHandle is required.");
        }

        var user = await GetUserAsync(userName);
        var plans = await _maxio.ListProductsForProductFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
        var plan = plans.FirstOrDefault(candidate =>
            candidate.ArchivedAt is null &&
            string.Equals(candidate.Handle, planHandle, StringComparison.Ordinal));
        if (plan?.Handle is null)
        {
            throw new SubscriptionValidationException("The requested plan is not available in the configured product family.");
        }

        // A request keyed by the stable eShop user id and Maxio product handle makes a
        // repeat click safe within this host process. The stable customer reference also
        // makes customer creation idempotent across restarts and deployments.
        var key = $"{user.Id}:{plan.Handle}";
        var gate = SubscriptionLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(user, cancellationToken);
            var subscriptionReference = BuildSubscriptionReference(user.Id, plan.Handle);
            var existing = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var match = existing.FirstOrDefault(subscription =>
                string.Equals(subscription.Reference, subscriptionReference, StringComparison.Ordinal));
            if (match is not null)
            {
                return new CreateSubscriptionResponse { Created = false, Subscription = MapSubscription(match) };
            }

            var created = await _maxio.CreateSubscriptionAsync(customer.Id, plan.Handle, subscriptionReference, cancellationToken);
            return new CreateSubscriptionResponse { Created = true, Subscription = MapSubscription(created) };
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(string userName, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(userName);
        var customer = await _maxio.FindCustomerByReferenceAsync(BuildCustomerReference(user.Id), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(MapSubscription).OrderByDescending(subscription => subscription.NextBillingDate).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var reference = BuildCustomerReference(user.Id);
        var customer = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is not null) return customer;

        var (firstName, lastName) = GetCustomerName(user);
        try
        {
            return await _maxio.CreateCustomerAsync(firstName, lastName, user.Email ?? user.UserName!, reference, cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            // Reference is unique in Maxio. If another request/process won the race,
            // resolve its customer rather than creating a second one.
            var concurrentCustomer = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (concurrentCustomer is not null) return concurrentCustomer;
            throw;
        }
    }

    private async Task<ApplicationUser> GetUserAsync(string userName) =>
        await _users.FindByNameAsync(userName) ?? throw new SubscriptionValidationException("The authenticated user no longer exists.");

    private static (string FirstName, string LastName) GetCustomerName(ApplicationUser user)
    {
        // Identity's standard user has no first/last-name fields, while Maxio's
        // Create Customer contract requires both. Keep email as the contact value and
        // use deterministic, non-empty values for this reference app's customer record.
        return ("eShop", "Customer");
    }

    private static string BuildCustomerReference(string userId) => $"eshop-user:{userId}";
    private static string BuildSubscriptionReference(string userId, string productHandle) => $"eshop-subscription:{userId}:{productHandle}";

    private static SubscriptionPlanDto MapPlan(MaxioProduct plan) => new()
    {
        Handle = plan.Handle!,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit
    };

    private static SubscriptionDto MapSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents,
        State = subscription.State,
        // The contract defines next_assessment_at as the next payment attempt;
        // current_period_ends_at is the fallback when it is absent.
        NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt
    };
}

internal sealed class SubscriptionValidationException : Exception
{
    public SubscriptionValidationException(string message) : base(message) { }
}
