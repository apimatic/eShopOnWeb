using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionDto> SubscribeAsync(ClaimsPrincipal principal, string planHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}

public sealed class SubscriptionService : ISubscriptionService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMaxioBillingClient _maxio;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _userLocks = new(StringComparer.Ordinal);

    public SubscriptionService(UserManager<ApplicationUser> userManager, IMaxioBillingClient maxio)
    {
        _userManager = userManager;
        _maxio = maxio;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxio.ListPlansAsync(cancellationToken);
        return products
            .Where(product => !string.IsNullOrWhiteSpace(product.Handle) && product.ArchivedAt is null)
            .Select(ToPlan)
            .ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(ClaimsPrincipal principal, string planHandle, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(principal);
        var plans = await _maxio.ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(candidate => string.Equals(candidate.Handle, planHandle, StringComparison.OrdinalIgnoreCase)
            && candidate.ArchivedAt is null);
        if (plan?.Handle is null)
        {
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        var customerReference = GetCustomerReference(user.Id);
        var subscriptionReference = GetSubscriptionReference(user.Id, plan.Handle);
        var userLock = _userLocks.GetOrAdd(user.Id, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        try
        {
            var existingSubscription = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (existingSubscription is not null)
            {
                return ToSubscription(existingSubscription, plan);
            }

            var customer = await _maxio.FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (customer is null)
            {
                customer = await CreateCustomerRecoveringRaceAsync(user, customerReference, cancellationToken);
            }

            // Check again after customer creation: another process may have completed signup
            // while this request was creating or looking up the customer.
            existingSubscription = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (existingSubscription is not null)
            {
                return ToSubscription(existingSubscription, plan);
            }

            try
            {
                var created = await _maxio.CreateSubscriptionAsync(plan.Handle, customerReference, subscriptionReference, cancellationToken);
                return ToSubscription(created, plan);
            }
            catch (MaxioApiException exception) when ((int)exception.StatusCode is >= 400 and < 500)
            {
                // Maxio enforces unique references. Recover a concurrent successful create
                // instead of making a second subscription or surfacing a false failure.
                var concurrentSubscription = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
                if (concurrentSubscription is not null)
                {
                    return ToSubscription(concurrentSubscription, plan);
                }

                throw;
            }
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(principal);
        var customer = await _maxio.FindCustomerByReferenceAsync(GetCustomerReference(user.Id), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(subscription => ToSubscription(subscription, subscription.Product)).ToList();
    }

    private async Task<MaxioCustomer> CreateCustomerRecoveringRaceAsync(ApplicationUser user, string reference, CancellationToken cancellationToken)
    {
        try
        {
            return await _maxio.CreateCustomerAsync(
                "eShopOnWeb",
                user.UserName ?? "Customer",
                user.Email ?? user.UserName ?? reference,
                reference,
                cancellationToken);
        }
        catch (MaxioApiException exception) when ((int)exception.StatusCode is >= 400 and < 500)
        {
            var concurrentCustomer = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (concurrentCustomer is not null)
            {
                return concurrentCustomer;
            }

            throw;
        }
    }

    private async Task<ApplicationUser> GetUserAsync(ClaimsPrincipal principal)
    {
        var username = principal.FindFirstValue(ClaimTypes.Name) ?? principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException("The authenticated token does not contain a user name.");
        }

        return await _userManager.FindByNameAsync(username)
            ?? throw new InvalidOperationException("The authenticated user no longer exists.");
    }

    private static string GetCustomerReference(string userId) => $"eshoponweb-user-{userId}";

    private static string GetSubscriptionReference(string userId, string planHandle) =>
        $"eshoponweb-subscription-{userId}-{planHandle}";

    private static SubscriptionPlanDto ToPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        RequiresPaymentMethod = product.RequireCreditCard
    };

    private static SubscriptionDto ToSubscription(MaxioSubscription subscription, MaxioProduct? plan)
    {
        var nextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt;
        return new SubscriptionDto
        {
            Id = subscription.Id,
            Reference = subscription.Reference,
            PlanHandle = plan?.Handle,
            PlanName = plan?.Name,
            PriceInCents = subscription.PriceInCents != 0 ? subscription.PriceInCents : plan?.PriceInCents,
            Currency = subscription.Currency,
            Interval = plan?.Interval,
            IntervalUnit = plan?.IntervalUnit,
            State = subscription.State,
            NextBillingDate = nextBillingDate
        };
    }
}

public sealed class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string handle) : base($"Subscription plan '{handle}' was not found.") { }
}

public sealed class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool RequiresPaymentMethod { get; set; }
}

public sealed class SubscriptionDto
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public long? PriceInCents { get; set; }
    public string? Currency { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; set; }
}
